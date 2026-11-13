"""Wire protocol for the LinuxCNC <-> Godot bridge. See protocol.md for the spec
this implements; keep the two in sync by hand (no schema compiler in this project)."""
from __future__ import annotations

import struct
import time
from dataclasses import dataclass, field
from enum import IntEnum

MAGIC = 0xC0

_HEADER = struct.Struct("<BBQq")  # magic, packet_type, seq, t_mono_ns


def mono_ns() -> int:
    return time.monotonic_ns()


class PacketType(IntEnum):
    STATE = 1
    EVENT = 2
    PREVIEW = 3
    COMMAND = 16
    ACK = 17


class TaskMode(IntEnum):
    MANUAL = 0
    AUTO = 1
    MDI = 2


class InterpState(IntEnum):
    IDLE = 0
    READING = 1
    PAUSED = 2
    WAITING = 3


class EventKind(IntEnum):
    ALARM = 0
    PROGRAM_LOADED = 1
    TOOL_CHANGE_REQUEST = 2
    INFO = 3


class MotionType(IntEnum):
    RAPID = 0
    LINEAR = 1
    ARC_CW = 2
    ARC_CCW = 3


class CommandKind(IntEnum):
    JOG_START = 0
    JOG_STOP = 1
    JOG_INCREMENTAL = 2
    SET_MODE = 3
    MDI = 4
    PROGRAM_OPEN = 5
    PROGRAM_RUN = 6
    PROGRAM_PAUSE = 7
    PROGRAM_STOP = 8
    PROGRAM_STEP = 9
    SET_FEED_OVERRIDE = 10
    SET_SPINDLE_OVERRIDE = 11
    SPINDLE_ON = 12
    SPINDLE_OFF = 13
    TOOL_CHANGE_CONFIRM = 14
    HOME_AXIS = 15
    ESTOP_RESET = 16
    MACHINE_ON = 17
    MACHINE_OFF = 18


# pos_x pos_z vel_x vel_z feed_rate spindle_rpm (f64 x6) | feed_ovr spindle_ovr (f32 x2)
# | tool_nr (i32) | tool_nose_radius (f32) | spindle_enabled task_mode interp_state
# exec_state (u8 x4) | motion_line (i32) | estop machine_on homed_x homed_z in_cycle
# (u8 x5) | pad (x1)
_STATE_BODY = struct.Struct("<ddddddffifBBBBiBBBBBx")


@dataclass
class StateSample:
    pos_x: float
    pos_z: float
    vel_x: float
    vel_z: float
    feed_rate: float
    spindle_rpm: float
    feed_override: float
    spindle_override: float
    tool_number: int
    tool_nose_radius: float
    spindle_enabled: bool
    task_mode: int
    interp_state: int
    exec_state: int
    motion_line: int
    estop: bool
    machine_on: bool
    homed_x: bool
    homed_z: bool
    in_cycle: bool

    def pack(self, seq: int, t_mono_ns: int | None = None) -> bytes:
        header = _HEADER.pack(MAGIC, PacketType.STATE, seq, t_mono_ns or mono_ns())
        body = _STATE_BODY.pack(
            self.pos_x, self.pos_z, self.vel_x, self.vel_z, self.feed_rate,
            self.spindle_rpm, self.feed_override, self.spindle_override,
            self.tool_number, self.tool_nose_radius, int(self.spindle_enabled),
            self.task_mode, self.interp_state, self.exec_state, self.motion_line,
            int(self.estop), int(self.machine_on), int(self.homed_x),
            int(self.homed_z), int(self.in_cycle),
        )
        return header + body

    @staticmethod
    def unpack(body: bytes) -> "StateSample":
        (pos_x, pos_z, vel_x, vel_z, feed_rate, spindle_rpm, feed_ovr, spindle_ovr,
         tool_nr, tool_radius, spindle_en, task_mode, interp_state, exec_state,
         motion_line, estop, machine_on, homed_x, homed_z, in_cycle) = _STATE_BODY.unpack(body)
        return StateSample(
            pos_x=pos_x, pos_z=pos_z, vel_x=vel_x, vel_z=vel_z, feed_rate=feed_rate,
            spindle_rpm=spindle_rpm, feed_override=feed_ovr, spindle_override=spindle_ovr,
            tool_number=tool_nr, tool_nose_radius=tool_radius, spindle_enabled=bool(spindle_en),
            task_mode=task_mode, interp_state=interp_state, exec_state=exec_state,
            motion_line=motion_line, estop=bool(estop), machine_on=bool(machine_on),
            homed_x=bool(homed_x), homed_z=bool(homed_z), in_cycle=bool(in_cycle),
        )


def pack_header(packet_type: int, seq: int, t_mono_ns: int | None = None) -> bytes:
    return _HEADER.pack(MAGIC, packet_type, seq, t_mono_ns or mono_ns())


def unpack_header(data: bytes):
    magic, packet_type, seq, t_mono_ns = _HEADER.unpack_from(data, 0)
    if magic != MAGIC:
        raise ValueError(f"bad magic byte 0x{magic:02x}")
    return packet_type, seq, t_mono_ns, _HEADER.size


def pack_event(seq: int, event_kind: int, text: str) -> bytes:
    payload = text.encode("utf-8")
    header = pack_header(PacketType.EVENT, seq)
    return header + struct.pack("<BH", event_kind, len(payload)) + payload


def unpack_event(body: bytes):
    event_kind, text_len = struct.unpack_from("<BH", body, 0)
    text = body[3:3 + text_len].decode("utf-8")
    return event_kind, text


@dataclass
class PreviewSegment:
    motion_type: int
    line_number: int
    start_x: float
    start_z: float
    end_x: float
    end_z: float
    feed_rate: float


_SEGMENT = struct.Struct("<Bi dddd d".replace(" ", ""))
_PREVIEW_HEADER = struct.Struct("<III")

MAX_SEGMENTS_PER_CHUNK = 40  # keeps each UDP packet comfortably under ~1200 bytes


def pack_preview_chunk(seq: int, total_segments: int, chunk_index: int,
                        segments: list[PreviewSegment]) -> bytes:
    header = pack_header(PacketType.PREVIEW, seq)
    body = _PREVIEW_HEADER.pack(total_segments, chunk_index, len(segments))
    for s in segments:
        body += _SEGMENT.pack(s.motion_type, s.line_number, s.start_x, s.start_z,
                               s.end_x, s.end_z, s.feed_rate)
    return header + body


def unpack_preview_chunk(body: bytes):
    total_segments, chunk_index, count = _PREVIEW_HEADER.unpack_from(body, 0)
    offset = _PREVIEW_HEADER.size
    segments = []
    for _ in range(count):
        motion_type, line_number, sx, sz, ex, ez, feed = _SEGMENT.unpack_from(body, offset)
        segments.append(PreviewSegment(motion_type, line_number, sx, sz, ex, ez, feed))
        offset += _SEGMENT.size
    return total_segments, chunk_index, segments


def pack_ack(seq: int, acked_client_seq: int, ok: bool, text: str = "") -> bytes:
    payload = text.encode("utf-8")
    header = pack_header(PacketType.ACK, seq)
    return header + struct.pack("<QBH", acked_client_seq, int(ok), len(payload)) + payload


def unpack_ack(body: bytes):
    acked_client_seq, ok, text_len = struct.unpack_from("<QBH", body, 0)
    off = struct.calcsize("<QBH")
    text = body[off:off + text_len].decode("utf-8")
    return acked_client_seq, bool(ok), text


@dataclass
class Command:
    command_kind: int
    client_seq: int
    args: bytes = b""

    def pack(self, seq: int) -> bytes:
        header = pack_header(PacketType.COMMAND, seq)
        return header + struct.pack("<BQ", self.command_kind, self.client_seq) + self.args

    @staticmethod
    def unpack(body: bytes) -> "Command":
        command_kind, client_seq = struct.unpack_from("<BQ", body, 0)
        off = struct.calcsize("<BQ")
        return Command(command_kind, client_seq, body[off:])

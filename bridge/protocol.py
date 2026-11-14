"""Wire protocol for the LinuxCNC <-> Godot bridge. See protocol.md for the spec
this implements; keep the two in sync by hand (no schema compiler in this project)."""
from __future__ import annotations

import struct
import time
from dataclasses import dataclass, field
from enum import IntEnum

MAGIC = 0xC0

# The 6 conventional CNC axis letters (LinuxCNC's own convention), fixed
# capacity rather than variable-length on the wire: no framing complexity,
# and adding a machine with a different active-axis set (e.g. a 3-axis
# mill alongside this 2-axis lathe) needs zero protocol change -- it just
# sets a different ACTIVE_AXIS_MASK bit pattern over the same fixed slots.
AXIS_CAPACITY = 6
AXIS_X, AXIS_Y, AXIS_Z, AXIS_A, AXIS_B, AXIS_C = range(AXIS_CAPACITY)

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


# position velocity (f64 x AXIS_CAPACITY*2) | feed_rate spindle_rpm (f64 x2)
# | feed_ovr spindle_ovr (f32 x2) | tool_nr (i32) | tool_nose_radius (f32)
# | spindle_enabled task_mode interp_state exec_state (u8 x4) | motion_line (i32)
# | estop machine_on in_cycle active_axis_mask homed_mask (u8 x5)
_STATE_BODY = struct.Struct("<" + "d" * (AXIS_CAPACITY * 2) + "ddffifBBBBiBBBBB")


@dataclass
class StateSample:
    position: tuple[float, ...]  # AXIS_CAPACITY-long, machine mm/deg, indexed by AXIS_X.. etc
    velocity: tuple[float, ...]  # same shape as position
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
    in_cycle: bool
    active_axis_mask: int  # bit i set => this machine actually uses axis i
    homed_mask: int        # bit i set => axis i is homed

    def pack(self, seq: int, t_mono_ns: int | None = None) -> bytes:
        header = _HEADER.pack(MAGIC, PacketType.STATE, seq, t_mono_ns or mono_ns())
        body = _STATE_BODY.pack(
            *_fixed(self.position), *_fixed(self.velocity),
            self.feed_rate, self.spindle_rpm, self.feed_override, self.spindle_override,
            self.tool_number, self.tool_nose_radius, int(self.spindle_enabled),
            self.task_mode, self.interp_state, self.exec_state, self.motion_line,
            int(self.estop), int(self.machine_on), int(self.in_cycle),
            self.active_axis_mask, self.homed_mask,
        )
        return header + body

    @staticmethod
    def unpack(body: bytes) -> "StateSample":
        fields = _STATE_BODY.unpack(body)
        position, velocity = fields[0:AXIS_CAPACITY], fields[AXIS_CAPACITY:AXIS_CAPACITY * 2]
        (feed_rate, spindle_rpm, feed_ovr, spindle_ovr, tool_nr, tool_radius, spindle_en,
         task_mode, interp_state, exec_state, motion_line, estop, machine_on, in_cycle,
         active_axis_mask, homed_mask) = fields[AXIS_CAPACITY * 2:]
        return StateSample(
            position=position, velocity=velocity, feed_rate=feed_rate,
            spindle_rpm=spindle_rpm, feed_override=feed_ovr, spindle_override=spindle_ovr,
            tool_number=tool_nr, tool_nose_radius=tool_radius, spindle_enabled=bool(spindle_en),
            task_mode=task_mode, interp_state=interp_state, exec_state=exec_state,
            motion_line=motion_line, estop=bool(estop), machine_on=bool(machine_on),
            in_cycle=bool(in_cycle), active_axis_mask=active_axis_mask, homed_mask=homed_mask,
        )


def _fixed(values: tuple[float, ...]) -> tuple[float, ...]:
    """Pads/truncates to exactly AXIS_CAPACITY entries -- callers may pass a
    shorter tuple (e.g. just the axes a 2-axis lathe actually has)."""
    values = tuple(values)[:AXIS_CAPACITY]
    return values + (0.0,) * (AXIS_CAPACITY - len(values))


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
    start: tuple[float, ...]  # AXIS_CAPACITY-long, same indexing as StateSample.position
    end: tuple[float, ...]
    feed_rate: float


_SEGMENT = struct.Struct("<Bi" + "d" * (AXIS_CAPACITY * 2) + "d")
_PREVIEW_HEADER = struct.Struct("<III")

MAX_SEGMENTS_PER_CHUNK = 40  # keeps each UDP packet comfortably under ~1200 bytes


def pack_preview_chunk(seq: int, total_segments: int, chunk_index: int,
                        segments: list[PreviewSegment]) -> bytes:
    header = pack_header(PacketType.PREVIEW, seq)
    body = _PREVIEW_HEADER.pack(total_segments, chunk_index, len(segments))
    for s in segments:
        body += _SEGMENT.pack(s.motion_type, s.line_number, *_fixed(s.start), *_fixed(s.end), s.feed_rate)
    return header + body


def unpack_preview_chunk(body: bytes):
    total_segments, chunk_index, count = _PREVIEW_HEADER.unpack_from(body, 0)
    offset = _PREVIEW_HEADER.size
    segments = []
    for _ in range(count):
        fields = _SEGMENT.unpack_from(body, offset)
        motion_type, line_number = fields[0], fields[1]
        start = fields[2:2 + AXIS_CAPACITY]
        end = fields[2 + AXIS_CAPACITY:2 + AXIS_CAPACITY * 2]
        feed = fields[-1]
        segments.append(PreviewSegment(motion_type, line_number, start, end, feed))
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

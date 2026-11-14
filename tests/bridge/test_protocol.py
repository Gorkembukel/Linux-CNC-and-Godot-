"""Run inside WSL2 (needs no linuxcnc module, just the bridge/ package):
    python3 -m pytest Tests/bridge/test_protocol.py
"""
import dataclasses
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "bridge"))

from protocol import (  # noqa: E402
    AXIS_CAPACITY, AXIS_X, AXIS_Z, StateSample, PacketType, TaskMode, InterpState,
    Command, CommandKind, PreviewSegment, pack_event, unpack_event, pack_ack, unpack_ack,
    pack_preview_chunk, unpack_preview_chunk, unpack_header,
)


def _sample():
    position = [0.0] * AXIS_CAPACITY
    position[AXIS_X], position[AXIS_Z] = 12.5, -30.25
    velocity = [0.0] * AXIS_CAPACITY
    velocity[AXIS_X], velocity[AXIS_Z] = 1.0, -2.0
    return StateSample(
        position=tuple(position), velocity=tuple(velocity), feed_rate=150.0,
        spindle_rpm=800.0, feed_override=1.0, spindle_override=1.0, tool_number=2,
        tool_nose_radius=0.4, spindle_enabled=True, task_mode=int(TaskMode.AUTO),
        interp_state=int(InterpState.IDLE), exec_state=2, motion_line=42,
        estop=False, machine_on=True, in_cycle=True,
        active_axis_mask=(1 << AXIS_X) | (1 << AXIS_Z), homed_mask=(1 << AXIS_X) | (1 << AXIS_Z),
    )


def test_state_sample_roundtrip():
    s = _sample()
    data = s.pack(seq=777, t_mono_ns=123456789)
    packet_type, seq, t_mono_ns, hlen = unpack_header(data)
    assert packet_type == PacketType.STATE
    assert seq == 777
    assert t_mono_ns == 123456789

    s2 = StateSample.unpack(data[hlen:])
    d1, d2 = dataclasses.asdict(s), dataclasses.asdict(s2)
    d1["tool_nose_radius"] = round(d1["tool_nose_radius"], 4)
    d2["tool_nose_radius"] = round(d2["tool_nose_radius"], 4)
    assert d1 == d2


def test_state_sample_packet_is_159_bytes():
    # Locks the wire size so an accidental field reorder/width change in
    # protocol.py is caught immediately instead of silently desyncing from
    # Scripts/Bridge/BridgeProtocol.cs on the Godot side. 159 = 18-byte
    # header + 141-byte body (6-axis position+velocity fixed capacity, see
    # protocol.py's AXIS_CAPACITY).
    assert len(_sample().pack(seq=0)) == 159


def test_event_roundtrip():
    data = pack_event(seq=1, event_kind=2, text="tool change requested")
    _, _, _, hlen = unpack_header(data)
    kind, text = unpack_event(data[hlen:])
    assert kind == 2
    assert text == "tool change requested"


def test_ack_roundtrip():
    data = pack_ack(seq=3, acked_client_seq=99, ok=True)
    _, _, _, hlen = unpack_header(data)
    acked, ok, text = unpack_ack(data[hlen:])
    assert acked == 99
    assert ok is True
    assert text == ""


def test_command_roundtrip():
    cmd = Command(command_kind=int(CommandKind.MDI), client_seq=123, args=b"G0 X10")
    data = cmd.pack(seq=5)
    _, seq, _, hlen = unpack_header(data)
    assert seq == 5
    decoded = Command.unpack(data[hlen:])
    assert decoded.command_kind == int(CommandKind.MDI)
    assert decoded.client_seq == 123
    assert decoded.args == b"G0 X10"


def test_command_dedup_ordering_contract():
    # The actual dedup logic lives in command_server.CommandServer (bridge side);
    # here we just lock the contract it depends on: client_seq must be strictly
    # increasing for a command to be considered new.
    first = Command(int(CommandKind.PROGRAM_RUN), client_seq=10)
    retransmit = Command(int(CommandKind.PROGRAM_RUN), client_seq=10)
    newer = Command(int(CommandKind.PROGRAM_RUN), client_seq=11)
    assert retransmit.client_seq <= first.client_seq  # would be dropped
    assert newer.client_seq > first.client_seq  # would be applied


def _xz(x: float, z: float) -> tuple[float, ...]:
    v = [0.0] * AXIS_CAPACITY
    v[AXIS_X], v[AXIS_Z] = x, z
    return tuple(v)


def test_preview_chunk_roundtrip_and_chunking():
    segs = [
        PreviewSegment(motion_type=1, line_number=10, start=_xz(0, 0), end=_xz(10, 0), feed_rate=150.0),
        PreviewSegment(motion_type=0, line_number=11, start=_xz(10, 0), end=_xz(10, 5), feed_rate=0.0),
    ]
    packet = pack_preview_chunk(seq=6, total_segments=2, chunk_index=0, segments=segs)
    _, _, _, hlen = unpack_header(packet)
    total, idx, decoded = unpack_preview_chunk(packet[hlen:])
    assert total == 2
    assert idx == 0
    assert len(decoded) == 2
    assert decoded[0] == segs[0]
    assert decoded[1] == segs[1]

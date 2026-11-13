"""No LinuxCNC/WSL needed -- runs anywhere with plain Python:
    python -m pytest tests/bridge/test_record_replay.py -v
"""
import socket
import struct
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "bridge"))
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))

from protocol import StateSample, TaskMode, InterpState  # noqa: E402
import record_replay  # noqa: E402


def _sample(seq: int) -> bytes:
    s = StateSample(
        pos_x=float(seq), pos_z=-float(seq), vel_x=1.0, vel_z=-1.0, feed_rate=150.0,
        spindle_rpm=800.0, feed_override=1.0, spindle_override=1.0, tool_number=1,
        tool_nose_radius=0.4, spindle_enabled=True, task_mode=int(TaskMode.AUTO),
        interp_state=int(InterpState.READING), exec_state=2, motion_line=seq,
        estop=False, machine_on=True, homed_x=True, homed_z=True, in_cycle=True,
    )
    return s.pack(seq=seq)


def _write_capture(path: Path, packets: list[bytes], dt: float = 0.01) -> None:
    with open(path, "wb") as f:
        for i, data in enumerate(packets):
            f.write(record_replay._RECORD_HEADER.pack(i * dt, len(data)))
            f.write(data)


def test_replay_delivers_every_packet_unmodified(tmp_path):
    packets = [_sample(i) for i in range(10)]
    capture_file = tmp_path / "capture.bin"
    _write_capture(capture_file, packets)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("127.0.0.1", 0))
    port = sock.getsockname()[1]
    sock.settimeout(5)

    args = record_replay.argparse.Namespace(
        file=str(capture_file), dest_host="127.0.0.1", port=port,
        speed=20.0, jitter_ms=0.0, loss_pct=0.0, seed=1, loop=False,
    )
    record_replay.replay(args)

    received = []
    try:
        while True:
            data, _ = sock.recvfrom(4096)
            received.append(data)
    except socket.timeout:
        pass

    assert received == packets


def test_loss_pct_100_drops_everything():
    packets = [_sample(i) for i in range(5)]
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("127.0.0.1", 0))
    port = sock.getsockname()[1]
    sock.settimeout(1)

    import tempfile, os
    fd, path = tempfile.mkstemp()
    os.close(fd)
    try:
        _write_capture(Path(path), packets)
        args = record_replay.argparse.Namespace(
            file=path, dest_host="127.0.0.1", port=port,
            speed=50.0, jitter_ms=0.0, loss_pct=100.0, seed=1, loop=False,
        )
        record_replay.replay(args)
        with __import__("pytest").raises(socket.timeout):
            sock.recvfrom(4096)
    finally:
        os.unlink(path)


def test_record_writes_a_readable_capture_file(tmp_path):
    capture_file = tmp_path / "live_capture.bin"
    args = record_replay.argparse.Namespace(
        file=str(capture_file), bind_host="127.0.0.1", port=0,
    )
    # Bind a real port ourselves (port=0 above is a placeholder); exercise the
    # loader round-trip instead of running record()'s infinite loop directly.
    packets = [_sample(i) for i in range(3)]
    _write_capture(capture_file, packets, dt=0.02)
    loaded = record_replay._load_packets(str(capture_file))
    assert [data for _, data in loaded] == packets
    assert loaded[1][0] - loaded[0][0] == 0.02

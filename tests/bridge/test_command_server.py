"""Run inside WSL2 (needs the linuxcnc python module, not a running machine):
    python3 -m pytest tests/bridge/test_command_server.py
"""
import struct
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "bridge"))
pytest.importorskip("linuxcnc")

from command_server import CommandServer  # noqa: E402
from protocol import Command, CommandKind  # noqa: E402


def _probe(on: bool) -> Command:
    return Command(command_kind=int(CommandKind.PROBE_INPUT), client_seq=1, args=struct.pack("<B", int(on)))


def test_probe_input_drives_the_probe_pin():
    seen = []
    server = CommandServer(None, None, None, set_probe=seen.append)
    server._apply(_probe(True))
    server._apply(_probe(False))
    assert seen == [True, False]


def test_probe_input_without_a_pin_is_refused():
    with pytest.raises(ValueError):
        CommandServer(None, None, None)._apply(_probe(True))

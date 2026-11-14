"""Integration test: needs a real LinuxCNC (linuxcnc-uspace) install, so this
only runs inside WSL2 Ubuntu, not on Windows:
    python3 -m pytest tests/bridge/test_preview.py -v
"""
import sys
from pathlib import Path

import pytest

BRIDGE_DIR = Path(__file__).resolve().parents[2] / "bridge"
CONFIG_DIR = Path(__file__).resolve().parents[2] / "linuxcnc-config"
sys.path.insert(0, str(BRIDGE_DIR))

linuxcnc = pytest.importorskip("linuxcnc", reason="only runs inside WSL2 with linuxcnc-uspace installed")

from deploy import sync_linuxcnc_config  # noqa: E402
from linuxcnc_process import LinuxCNCProcess  # noqa: E402
from preview import preview_program  # noqa: E402
from protocol import AXIS_X, AXIS_Z, MotionType  # noqa: E402


@pytest.fixture(scope="module")
def running_machine():
    ini_path = sync_linuxcnc_config(str(CONFIG_DIR))
    with LinuxCNCProcess(ini_path) as proc:
        stat = linuxcnc.stat()
        stat.poll()
        yield stat, Path(ini_path).parent


def test_demo_program_preview_matches_known_path(running_machine):
    stat, config_dir = running_machine
    ngc = config_dir / "nc_files" / "demo_facing_turning.ngc"

    segments = preview_program(stat, str(ngc), str(config_dir))

    assert len(segments) == 28

    # First move: rapid from origin to the facing start point (G0 X26.0 Z2.0).
    first = segments[0]
    assert first.motion_type == MotionType.RAPID
    assert first.start[AXIS_X] == pytest.approx(0.0, abs=1e-6)
    assert first.start[AXIS_Z] == pytest.approx(0.0, abs=1e-6)
    assert first.end[AXIS_X] == pytest.approx(26.0, abs=1e-3)
    assert first.end[AXIS_Z] == pytest.approx(2.0, abs=1e-3)

    # Facing cut: linear move at F80 in to the centerline (X-0.5).
    facing_cut = segments[2]
    assert facing_cut.motion_type == MotionType.LINEAR
    assert facing_cut.end[AXIS_X] == pytest.approx(-0.5, abs=1e-3)
    assert facing_cut.feed_rate == pytest.approx(80.0, abs=0.5)

    # A roughing pass cutting down to Z-60 at feed 150 (there are 4 of these,
    # at radii 25/23/21/20.2mm; this checks the second one specifically).
    rough_pass = segments[9]
    assert rough_pass.motion_type == MotionType.LINEAR
    assert rough_pass.end[AXIS_Z] == pytest.approx(-60.0, abs=1e-3)
    assert rough_pass.end[AXIS_X] == pytest.approx(23.0, abs=1e-3)
    assert rough_pass.feed_rate == pytest.approx(150.0, abs=0.5)

    # Final X extent across the whole program: the retract move at the very end
    # (G0 X30.0 Z5.0) is the widest point, the parting pass (X-0.5) the narrowest.
    max_x = max(s.end[AXIS_X] for s in segments)
    min_x = min(s.end[AXIS_X] for s in segments)
    assert max_x == pytest.approx(30.0, abs=1e-3)
    assert min_x == pytest.approx(-0.5, abs=1e-3)

"""Integration test: needs linuxcnc-uspace, WSL2 only:
    python3 -m pytest tests/bridge/test_linuxcnc_process.py -v
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


def test_start_stop_lifecycle_is_clean():
    ini_path = sync_linuxcnc_config(str(CONFIG_DIR))
    proc = LinuxCNCProcess(ini_path)
    proc.start()
    try:
        proc.wait_for_nml_ready()
        assert proc.is_running()
        stat = linuxcnc.stat()
        stat.poll()
        assert stat.axis_mask == 0b101  # X (bit0) + Z (bit2), per sim_lathe.ini
    finally:
        proc.stop()
    assert not proc.is_running()


def test_home_and_jog_both_axes():
    ini_path = sync_linuxcnc_config(str(CONFIG_DIR))
    with LinuxCNCProcess(ini_path) as proc:
        stat = linuxcnc.stat()
        cmd = linuxcnc.command()
        stat.poll()
        cmd.state(linuxcnc.STATE_ESTOP_RESET)
        cmd.state(linuxcnc.STATE_ON)
        cmd.home(-1)

        import time
        deadline = time.time() + 20
        while time.time() < deadline:
            stat.poll()
            if all(stat.homed[:2]):
                break
            time.sleep(0.2)
        assert all(stat.homed[:2])

        cmd.mode(linuxcnc.MODE_MDI)
        cmd.wait_complete()
        cmd.mdi("G21 G8 G90")
        cmd.wait_complete()
        cmd.mdi("G0 X12.5 Z-7.5")
        cmd.wait_complete()
        time.sleep(0.3)
        stat.poll()
        assert stat.actual_position[0] == pytest.approx(12.5, abs=0.01)
        assert stat.actual_position[2] == pytest.approx(-7.5, abs=0.01)

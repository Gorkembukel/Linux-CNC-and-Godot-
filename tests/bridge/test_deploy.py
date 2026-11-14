"""Runs anywhere (no LinuxCNC needed):
    python3 -m pytest tests/bridge/test_deploy.py -v
"""
import sys
from pathlib import Path

BRIDGE_DIR = Path(__file__).resolve().parents[2] / "bridge"
sys.path.insert(0, str(BRIDGE_DIR))

import deploy  # noqa: E402


def test_sync_nc_files_adds_new_and_updates_edited_programs(tmp_path, monkeypatch):
    repo = tmp_path / "repo" / "linuxcnc-config"
    (repo / "nc_files").mkdir(parents=True)
    (repo / "machine.ini").write_text("[EMC]
")
    (repo / "nc_files" / "a.ngc").write_text("G0 X1
")
    monkeypatch.setattr(deploy, "NATIVE_ROOT", tmp_path / "native")
    deploy.sync_linuxcnc_config(str(repo), "machine.ini")
    native = tmp_path / "native" / "linuxcnc-config" / "nc_files"

    (repo / "nc_files" / "a.ngc").write_text("G0 X2
")
    (repo / "nc_files" / "b.ngc").write_text("G0 X3
")
    deploy.sync_nc_files(str(repo))

    assert (native / "a.ngc").read_text() == "G0 X2
"
    assert (native / "b.ngc").read_text() == "G0 X3
"

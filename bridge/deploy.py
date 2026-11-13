"""Copies linuxcnc-config/ from the Windows-mounted repo (drvfs, /mnt/c/...) to a
plain WSL-native path before running LinuxCNC there.

Found by trial and error: drvfs (the 9p-based /mnt/c mount) does not support
hard links, and both LinuxCNC's own parameter-file backup (`link()` on the .var
file once one already exists) and our own stdin-FIFO trick for keeping the
headless instance alive hit filesystem quirks on it -- the FIFO was flaky
("cannot create fifo: File exists" right after a successful `rm`) and the var
file link failed outright with EXDEV once a previous run had created one,
which is fatal inside the `gcode.parse()` preview path specifically (the main
`linuxcnc` launcher happens to tolerate it, but only the first time, when the
var file doesn't exist yet and the error is ENOENT instead). Running from a
native ext4 path removes the whole category of problems; the Windows-mounted
copy stays the source of truth (version controlled) and is just re-synced here
on every bridge start.
"""
from __future__ import annotations

import shutil
from pathlib import Path

NATIVE_ROOT = Path.home() / "godot-cnc-trainer"


def sync_linuxcnc_config(repo_config_dir: str) -> str:
    """Copies repo_config_dir (expected to be the /mnt/c/... linuxcnc-config
    directory) to NATIVE_ROOT/linuxcnc-config, overwriting it, and returns the
    path to the native copy's sim_lathe.ini."""
    src = Path(repo_config_dir).resolve()
    dest = NATIVE_ROOT / "linuxcnc-config"
    if dest.exists():
        shutil.rmtree(dest)
    shutil.copytree(src, dest)
    return str(dest / "sim_lathe.ini")

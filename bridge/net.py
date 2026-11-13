"""Networking helpers specific to running the bridge inside WSL2 NAT."""
from __future__ import annotations

import re
import subprocess

_DEFAULT_ROUTE_RE = re.compile(r"default via (\S+)")


def discover_windows_host_ip() -> str:
    """The Windows host's address as seen from inside the WSL2 VM (its vEthernet
    (WSL) adapter). WSL2 NAT does not forward unsolicited Linux->Windows traffic
    through `localhost`, only Windows->Linux, so sending the state stream to
    Godot requires this address explicitly; it's the default gateway and is
    stable for the life of one `wsl` session."""
    out = subprocess.run(["ip", "route", "show", "default"], check=True,
                          capture_output=True, text=True).stdout
    m = _DEFAULT_ROUTE_RE.search(out)
    if not m:
        raise RuntimeError(f"could not find default gateway in `ip route` output: {out!r}")
    return m.group(1)

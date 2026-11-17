"""Networking helpers specific to running the bridge inside WSL2 NAT."""
from __future__ import annotations

import re
import subprocess

_DEFAULT_ROUTE_RE = re.compile(r"default via (\S+)")


def discover_own_ip() -> str:
    """This WSL2 VM's own address on its NAT network (eth0) -- the address
    Godot/Windows must send COMMAND packets to.

    Found live, the hard way: WSL2's documented "localhost forwarding" (a
    socket bound in the VM is reachable from Windows at 127.0.0.1) does NOT
    work for UDP on this machine/WSL build -- packets sent from Windows to
    127.0.0.1:9201 were silently dropped (confirmed with a raw socket test: no
    ICMP port-unreachable even though nothing was listening on that address,
    and once something *was* listening, 127.0.0.1 still produced no delivery,
    while this VM's real eth0 address worked immediately). So Godot cannot
    just assume 127.0.0.1; it reads this address from the endpoint file this
    module's caller writes (see main.py) instead."""
    out = subprocess.run(["ip", "addr", "show", "eth0"], check=True,
                          capture_output=True, text=True).stdout
    m = re.search(r"inet (\S+)/", out)
    if not m:
        raise RuntimeError(f"could not find eth0 inet address in `ip addr` output: {out!r}")
    return m.group(1)


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

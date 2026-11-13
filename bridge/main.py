"""Bridge service entrypoint: brings up LinuxCNC (sim lathe), then runs the
STATE publisher and COMMAND server until killed (Ctrl+C / SIGTERM).

Usage (inside WSL2 Ubuntu, with linuxcnc-uspace installed):
    python3 bridge/main.py [--rate-hz 250]
"""
from __future__ import annotations

import argparse
import signal
import socket
import sys
import threading
from pathlib import Path

import linuxcnc

sys.path.insert(0, str(Path(__file__).resolve().parent))
from deploy import sync_linuxcnc_config  # noqa: E402
from linuxcnc_process import LinuxCNCProcess  # noqa: E402
from net import discover_windows_host_ip  # noqa: E402
from state_publisher import StatePublisher  # noqa: E402
from command_server import CommandServer  # noqa: E402

STATE_PORT = 9200
COMMAND_PORT = 9201

REPO_ROOT = Path(__file__).resolve().parent.parent
REPO_CONFIG_DIR = REPO_ROOT / "linuxcnc-config"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--rate-hz", type=float, default=250.0)
    ap.add_argument("--state-port", type=int, default=STATE_PORT)
    ap.add_argument("--command-port", type=int, default=COMMAND_PORT)
    args = ap.parse_args()

    print(f"[bridge] syncing config from {REPO_CONFIG_DIR} to native WSL filesystem...")
    ini_path = sync_linuxcnc_config(str(REPO_CONFIG_DIR))
    print(f"[bridge] native ini: {ini_path}")

    dest_ip = discover_windows_host_ip()
    print(f"[bridge] Windows host (state stream destination): {dest_ip}")

    stop_event = threading.Event()

    def handle_sigterm(signum, frame):
        print("[bridge] shutting down...")
        stop_event.set()

    signal.signal(signal.SIGTERM, handle_sigterm)
    signal.signal(signal.SIGINT, handle_sigterm)

    with LinuxCNCProcess(ini_path) as proc:
        print("[bridge] LinuxCNC is up.")
        stat = linuxcnc.stat()
        cmd = linuxcnc.command()
        stat.poll()

        state_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        publisher = StatePublisher(stat, state_sock, (dest_ip, args.state_port), rate_hz=args.rate_hz)

        command_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        command_sock.bind(("0.0.0.0", args.command_port))
        server = CommandServer(stat, cmd, command_sock)

        command_thread = threading.Thread(target=server.run_forever, args=(stop_event.is_set,), daemon=True)
        command_thread.start()
        print(f"[bridge] publishing state to {dest_ip}:{args.state_port} at {args.rate_hz} Hz, "
              f"listening for commands on 0.0.0.0:{args.command_port}")

        try:
            publisher.run_forever(stop_event.is_set)
        except KeyboardInterrupt:
            pass
        stop_event.set()
        command_thread.join(timeout=2.0)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

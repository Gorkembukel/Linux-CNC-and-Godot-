"""Bridge service entrypoint: brings up LinuxCNC (sim lathe), then runs the
STATE publisher and COMMAND server until killed (Ctrl+C / SIGTERM).

Usage (inside WSL2 Ubuntu, with linuxcnc-uspace installed):
    python3 bridge/main.py [--ini sim_mill.ini] [--rate-hz 250]
"""
from __future__ import annotations

import argparse
import os
import signal
import socket
import subprocess
import sys
import threading
import time
from pathlib import Path

import hal
import linuxcnc

sys.path.insert(0, str(Path(__file__).resolve().parent))
from deploy import sync_linuxcnc_config, sync_nc_files  # noqa: E402
from linuxcnc_process import LinuxCNCProcess  # noqa: E402
from net import discover_windows_host_ip, discover_own_ip  # noqa: E402
from state_publisher import StatePublisher  # noqa: E402
from command_server import CommandServer  # noqa: E402
from preview import preview_program  # noqa: E402
from protocol import MAX_SEGMENTS_PER_CHUNK, pack_preview_chunk  # noqa: E402

# A 38,000-line 3+2 program is ~40,000 segments: enough for the plot, and
# ~1000 packets sent paced so the UDP burst is not dropped.
MAX_PREVIEW_SEGMENTS = 40000

STATE_PORT = 9200
COMMAND_PORT = 9201

REPO_ROOT = Path(__file__).resolve().parent.parent
REPO_CONFIG_DIR = REPO_ROOT / "linuxcnc-config"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--ini", default="sim_lathe.ini", help="INI file name inside linuxcnc-config/")
    ap.add_argument("--rate-hz", type=float, default=250.0)
    ap.add_argument("--state-port", type=int, default=STATE_PORT)
    ap.add_argument("--command-port", type=int, default=COMMAND_PORT)
    args = ap.parse_args()

    print(f"[bridge] syncing config from {REPO_CONFIG_DIR} to native WSL filesystem...")
    ini_path = sync_linuxcnc_config(str(REPO_CONFIG_DIR), args.ini)
    print(f"[bridge] native ini: {ini_path}")
    # The preview's own interpreter finds the INI (and with it any REMAP, such
    # as the Fanuc T0101 call) through INI_FILE_NAME, as under the linuxcnc
    # launcher; its relative paths (SUBROUTINE_PATH = ./) are the config dir's.
    os.environ["INI_FILE_NAME"] = ini_path
    os.chdir(Path(ini_path).parent)

    # trivkins assigns joints in the order the axis letters are listed.
    coordinates = linuxcnc.ini(ini_path).find("TRAJ", "COORDINATES").split()
    joint_axis_map = tuple((joint, "XYZABC".index(letter)) for joint, letter in enumerate(coordinates))

    dest_ip = discover_windows_host_ip()
    print(f"[bridge] Windows host (state stream destination): {dest_ip}")

    own_ip = discover_own_ip()
    endpoint_file = REPO_ROOT / "bridge" / ".bridge_endpoint"
    endpoint_file.write_text(own_ip)
    print(f"[bridge] own address (command destination for Godot): {own_ip} "
          f"(written to {endpoint_file} -- 127.0.0.1 does NOT work here, see net.py)")

    stop_event = threading.Event()

    def handle_sigterm(signum, frame):
        print("[bridge] shutting down...")
        stop_event.set()

    signal.signal(signal.SIGTERM, handle_sigterm)
    signal.signal(signal.SIGINT, handle_sigterm)

    with LinuxCNCProcess(ini_path) as proc:
        print("[bridge] LinuxCNC is up.")

        # basic_sim.tcl loops iocontrol's tool-change straight back into
        # tool-changed, which completes every M6 instantly. Cutting that loop
        # makes M6 wait for the operator's confirm (CommandServer sets the pin).
        subprocess.run(["halcmd", "unlinkp", "iocontrol.0.tool-changed"], check=True)
        # hal.get_value only works in a process that owns a HAL component.
        hal_component = hal.component(f"godot-bridge-{args.command_port}")
        hal_component.newpin("probe", hal.HAL_BIT, hal.HAL_OUT)
        hal_component.ready()
        # basic_sim.tcl leaves motion.probe-input unconnected: Godot's touch
        # probe drives it, so G38.x runs on the real control.
        subprocess.run(["halcmd", "net", "godot-probe", f"godot-bridge-{args.command_port}.probe",
                        "motion.probe-input"], check=True)

        def set_probe(on: bool) -> None:
            hal_component["probe"] = on

        # basic_sim's sim_spindle slews the commanded speed through
        # limit_speed (rpm/s) and waits on near_speed for at-speed: give it
        # this machine's spindle mass, and report the speed it has reached.
        accel = linuxcnc.ini(ini_path).find("SPINDLE_0", "GODOT_ACCEL_RPM_PER_SEC")
        if accel:
            subprocess.run(["halcmd", "setp", "limit_speed.maxv", accel], check=True)

        def spindle_actual():
            try:
                return float(hal.get_value("limit_speed.out"))
            except Exception:  # noqa: BLE001 - a HAL without sim_spindle
                return None
        stat = linuxcnc.stat()
        cmd = linuxcnc.command()
        stat.poll()

        command_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        command_sock.bind(("0.0.0.0", args.command_port))

        # State goes out of the command socket, to whichever address last
        # sent a SUBSCRIBE to it: a stateful firewall on the Windows side
        # then sees the stream as replies to Godot's own outbound packets
        # and lets it through with no inbound rule. The gateway address is
        # only the destination until the first SUBSCRIBE arrives.
        publisher = StatePublisher(stat, command_sock, (dest_ip, args.state_port), rate_hz=args.rate_hz,
                                   joint_axis_map=joint_axis_map,
                                   tool_change_pending=lambda: hal.get_value("iocontrol.0.tool-change"),
                                   spindle_actual=spindle_actual,
                                   error_channel=linuxcnc.error_channel())

        def retarget(addr):
            publisher.dest_addr = addr

        # The preview parses with its own interpreter and its own stat, on a
        # thread of its own: a long program must not hold up the commands.
        preview_stat = linuxcnc.stat()
        preview_seq = [0]

        def send_preview(program: str) -> None:
            def work():
                try:
                    preview_stat.poll()
                    segments = preview_program(preview_stat, str(Path(ini_path).parent / program),
                                               str(Path(ini_path).parent))[:MAX_PREVIEW_SEGMENTS]
                except Exception as e:  # noqa: BLE001 - a program the preview cannot parse still runs
                    print(f"[bridge] preview of {program} failed: {e}")
                    return
                chunks = [segments[i:i + MAX_SEGMENTS_PER_CHUNK]
                          for i in range(0, len(segments), MAX_SEGMENTS_PER_CHUNK)] or [[]]
                for index, chunk in enumerate(chunks):
                    command_sock.sendto(pack_preview_chunk(preview_seq[0], len(segments), index, chunk),
                                        publisher.dest_addr)
                    preview_seq[0] += 1
                    time.sleep(0.001)
            threading.Thread(target=work, daemon=True).start()

        server = CommandServer(stat, cmd, command_sock, on_subscribe=retarget,
                               before_program_open=lambda: sync_nc_files(str(REPO_CONFIG_DIR)),
                               joint_axis_map=joint_axis_map, after_program_open=send_preview,
                               set_probe=set_probe)

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

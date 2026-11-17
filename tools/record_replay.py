"""Records the bridge's UDP STATE stream to a file and plays it back, so Godot
development/testing can happen without LinuxCNC (or WSL2) running at all --
brief section 8.

Runs as a PLAIN Python script with no extra dependencies, on Windows directly
(no WSL needed) since both recording and replay just read/write ordinary UDP
packets -- the WSL2-specific networking quirks documented in PLAN.md only
apply to the real bridge talking across the Windows/WSL2 boundary; this tool
never needs to cross that boundary.

Usage:
    # While the real bridge is running and Godot is NOT (so this tool can bind
    # the port Godot would otherwise use):
    python tools/record_replay.py record capture.bin

    # Later, with Godot running and the real bridge stopped:
    python tools/record_replay.py replay capture.bin
    python tools/record_replay.py replay capture.bin --jitter-ms 15 --loss-pct 5
    python tools/record_replay.py replay capture.bin --speed 2.0 --loop
"""
from __future__ import annotations

import argparse
import random
import socket
import struct
import time
from pathlib import Path

_RECORD_HEADER = struct.Struct("<dI")  # relative_time_seconds, payload_length


def record(args: argparse.Namespace) -> None:
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind((args.bind_host, args.port))
    print(f"[record] listening on {args.bind_host}:{args.port}, writing to {args.file}")
    print("[record] Ctrl+C to stop")
    t0 = time.monotonic()
    count = 0
    with open(args.file, "wb") as f:
        try:
            while True:
                data, _addr = sock.recvfrom(4096)
                t = time.monotonic() - t0
                f.write(_RECORD_HEADER.pack(t, len(data)))
                f.write(data)
                count += 1
                if count % 500 == 0:
                    print(f"[record] {count} packets, {t:.1f}s elapsed")
        except KeyboardInterrupt:
            pass
    print(f"[record] stopped: {count} packets recorded over {time.monotonic() - t0:.1f}s")


def _load_packets(path: str) -> list[tuple[float, bytes]]:
    packets: list[tuple[float, bytes]] = []
    with open(path, "rb") as f:
        while True:
            header = f.read(_RECORD_HEADER.size)
            if len(header) < _RECORD_HEADER.size:
                break
            t, length = _RECORD_HEADER.unpack(header)
            data = f.read(length)
            packets.append((t, data))
    return packets


def replay(args: argparse.Namespace) -> None:
    packets = _load_packets(args.file)
    if not packets:
        print(f"[replay] {args.file} has no packets")
        return
    print(f"[replay] loaded {len(packets)} packets from {args.file}, "
          f"sending to {args.dest_host}:{args.port} at {args.speed}x speed")
    if args.jitter_ms:
        print(f"[replay] injecting up to +-{args.jitter_ms}ms jitter")
    if args.loss_pct:
        print(f"[replay] dropping ~{args.loss_pct}% of packets")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    dest = (args.dest_host, args.port)
    rng = random.Random(args.seed)

    while True:
        t0 = time.monotonic()
        sent = dropped = 0
        for t, data in packets:
            if args.loss_pct > 0 and rng.random() * 100 < args.loss_pct:
                dropped += 1
                continue
            target_time = t / args.speed
            if args.jitter_ms > 0:
                target_time += rng.uniform(-args.jitter_ms, args.jitter_ms) / 1000.0
            sleep_for = target_time - (time.monotonic() - t0)
            if sleep_for > 0:
                time.sleep(sleep_for)
            sock.sendto(data, dest)
            sent += 1
        print(f"[replay] pass complete: sent={sent} dropped={dropped}")
        if not args.loop:
            break


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    rec = sub.add_parser("record", help="capture the bridge's UDP stream to a file")
    rec.add_argument("file")
    rec.add_argument("--bind-host", default="0.0.0.0")
    rec.add_argument("--port", type=int, default=9200)
    rec.set_defaults(func=record)

    rep = sub.add_parser("replay", help="play a captured file back as if it were the bridge")
    rep.add_argument("file")
    rep.add_argument("--dest-host", default="127.0.0.1")
    rep.add_argument("--port", type=int, default=9200)
    rep.add_argument("--speed", type=float, default=1.0, help="playback speed multiplier")
    rep.add_argument("--jitter-ms", type=float, default=0.0, help="max +-jitter injected per packet")
    rep.add_argument("--loss-pct", type=float, default=0.0, help="percent chance to drop each packet")
    rep.add_argument("--seed", type=int, default=0, help="RNG seed, for reproducible jitter/loss")
    rep.add_argument("--loop", action="store_true", help="repeat the capture indefinitely")
    rep.set_defaults(func=replay)

    args = ap.parse_args()
    args.func(args)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

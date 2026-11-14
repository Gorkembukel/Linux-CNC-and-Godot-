# CNC Operator Training Simulator (Godot + LinuxCNC)

A 2-axis lathe operator training simulator. LinuxCNC (running in WSL2 Ubuntu)
is the machine's "brain" — it's authoritative for every axis position, mode,
and alarm. Godot 4 (C#) is the visual/interactive layer: it never changes
machine state on its own, only sends commands and renders what it's told.

See `PLAN.md` for the full architecture, phase-by-phase status, and every
non-obvious environment issue found and fixed along the way (read this if
something here doesn't work and matches a symptom described there — it's
almost certainly already diagnosed). See `PERF.md` for benchmark numbers and
`ASSETS.md` for what's a real model vs. a placeholder primitive.

## One-time setup (per machine)

This project only runs on **Windows with WSL2** — LinuxCNC has no Windows
port, even its non-realtime "uspace" mode needs a real Linux kernel.

1. **WSL2 + Ubuntu.** If `wsl -l -v` doesn't show a working `Ubuntu` entry:
   ```
   wsl --update
   wsl --install -d Ubuntu
   ```
   (If an existing `Ubuntu` entry is broken/orphaned -- e.g. `wsl --status`
   complains the kernel file is missing, or starting it fails with a disk/vhdx
   error -- `wsl --unregister Ubuntu` first, then install fresh. Nothing is
   lost: a broken registration like that has no working data to lose.)

2. **Install LinuxCNC inside Ubuntu:**
   ```
   wsl -d Ubuntu -- sudo apt-get update
   wsl -d Ubuntu -- sudo apt-get install -y linuxcnc-uspace linuxcnc-uspace-dev python3-pip git build-essential
   ```

3. **Windows Firewall rule** (optional fallback — the state stream now
   arrives as replies to Godot's own outbound SUBSCRIBE packets, which a
   stateful firewall lets through without any inbound rule; see the
   networking section below). Only needed if a firewall on your machine
   does not track UDP replies. In an **elevated** PowerShell:
   ```
   New-NetFirewallRule -DisplayName "Godot CNC Bridge" -Direction Inbound -Protocol UDP -LocalPort 9200,9201 -Action Allow -Profile Any
   ```

4. **.NET SDK 8 or newer** on Windows (for building the Godot C# project) —
   check with `dotnet --version`.

5. **Godot 4.7+ with Mono/C# support**, anywhere on Windows. Edit
   `$GodotExe` at the top of `start.ps1` to point at your
   `Godot_*_mono_win64_console.exe`.

You do **not** need to install anything for the Godot↔WSL networking itself —
the bridge auto-discovers both sides' addresses at runtime (see "How the
networking works" below); nothing is hardcoded to a specific IP.

## Running it

```
.\start.ps1
```

That's it — just launches Godot. Godot itself starts each machine's backing
LinuxCNC/bridge process in WSL on its own (`Scripts/Machines/
MachineProcessController.cs`) and stops it cleanly when you close the game
window; there's no separate bridge window to watch or close by hand anymore.
**First cold start of LinuxCNC can take up to ~90 seconds** — this is normal
(see PLAN.md's Phase 0 notes on why), not a hang; the status label just
reads "BRIDGE DISCONNECTED" until it connects on its own.

If you want to watch the bridge's own live output while debugging (instead
of its log file, `bridge/bridge_session_<MachineName>.log`) or pass different bridge
flags, see "Manual startup" below — but don't also run `start.ps1`/Godot at
the same time: Godot will start (and the cleanup step it runs first will
disrupt) its own copy regardless, which isn't what you want while watching
a hand-started one.

### Operator panel walkthrough

Once Godot is open, the on-screen Operator Panel (bottom-left):

1. **MACHINE ON** — resets e-stop and turns the machine on.
2. **HOME ALL** (or HOME X / HOME Z individually) — references both axes.
   Takes a few seconds.
3. **LOAD PROGRAM** — the text field defaults to the bundled demo program
   (`nc_files/demo_facing_turning.ngc`, a facing + 4-pass rough turn + finish
   + part-off on a 50mm bar). Edit the path for a different program, relative
   to `linuxcnc-config/`.
4. **CYCLE START** — runs it. **FEED HOLD** pauses, **STOP** aborts,
   **SINGLE BLOCK STEP** single-steps.
5. When a tool change comes up, an orange **"TOOL CHANGE PENDING"** banner
   appears — press **CONFIRM TOOL CHANGE** to let the program continue (there
   is no physical turret to simulate a real swap, so this just acknowledges
   it).
6. **Jog**: pick CONTINUOUS or an increment size, then hold/click X±/Z±.
7. **MDI**: type a line, Enter or EXECUTE.
8. **Touch off**: jog the tool to a known reference surface, enter its known
   dimension, press the matching TOUCH OFF button to set that tool's offset.

**Camera**: right-click-drag to orbit, scroll to zoom — it starts at a fixed
angle, you'll want to zoom in to actually see the cut and chips.

**F3** toggles the debug overlay (predicted vs. confirmed position, error
graph, stream jitter, command latency, frame time, scheduler budgets/queues).

### Manual startup (debugging only -- `start.ps1` no longer needs this)

```
# 1. Clean stale state (always safe to run, even if nothing is running):
wsl -d Ubuntu -- bash /mnt/c/<path-to-repo>/bridge/clear_stale_shm.sh

# 2. Start the bridge (blocks; leave this window open -- don't also run
#    start.ps1/Godot at the same time, see "Running it" above):
wsl -d Ubuntu -- python3 -u /mnt/c/<path-to-repo>/bridge/main.py --rate-hz 100
# wait for the line: "[bridge] publishing state to ...:9200 at 100.0 Hz, ..."

# 3. In a separate terminal, build and run Godot:
dotnet build Linux.csproj
& "<path-to-godot>\Godot_..._console.exe" --path "<path-to-repo>"
```

## How the networking works (read this before debugging connection issues)

- **Bridge → Godot** (position/state stream): once a second Godot sends a
  SUBSCRIBE packet from its state socket to the bridge's command port, and
  the bridge sends the state stream back to that packet's source address,
  out of the command socket. To a stateful firewall the stream is a reply
  to Godot's own traffic, so it passes with no inbound rule — unsolicited
  WSL → Windows UDP was found blocked by a third-party firewall (Kaspersky)
  that ignores Windows Firewall rules entirely. Until the first SUBSCRIBE
  arrives the bridge falls back to sending to the Windows host's address
  (`ip route`'s default gateway) on the state port.
- **Ports per machine**: all WSL distros share one network address, so each
  machine needs its own pair — lathe 9200/9201, mill 9202/9203.
- **Godot → bridge** (commands, 9201/udp): WSL2's usual "a socket in the VM is
  reachable from Windows at `127.0.0.1`" **does not work for UDP** on at least
  some WSL2 builds (confirmed the hard way during development). So instead,
  the bridge writes its own real address to `bridge/.bridge_endpoint` on every
  start, and `BridgeClient.cs` reads that file at startup instead of assuming
  `127.0.0.1`. If you ever see the panel's buttons have no effect on the
  machine, check that this file exists and was written by the *currently
  running* bridge (its WSL IP changes across `wsl --shutdown` cycles, though
  not across ordinary bridge restarts within one WSL session).

## Tests

```
# C# (xUnit) -- pure logic, no LinuxCNC/bridge needed:
cd Tests
dotnet test

# Python (pytest) -- most need linuxcnc-uspace, so run inside WSL2:
wsl -d Ubuntu -- python3 -m pytest /mnt/c/<path-to-repo>/tests/bridge/ -v
```
`tests/bridge/test_record_replay.py` and `test_protocol.py` need no LinuxCNC
and also run fine on Windows directly if you `pip install pytest` there.

**Don't run the Python LinuxCNC-lifecycle tests (`test_linuxcnc_process.py`,
`test_preview.py`) while a bridge/Godot session is already running** — they
start and stop their own LinuxCNC instance and will conflict with a live one.

## Record/replay (develop without LinuxCNC running at all)

```
# While the real bridge is running and Godot is NOT:
python tools\record_replay.py record capture.bin

# Later, with Godot running and the bridge stopped:
python tools\record_replay.py replay capture.bin
python tools\record_replay.py replay capture.bin --jitter-ms 15 --loss-pct 5 --loop
```

## Project layout

```
bridge/              Python bridge: LinuxCNC process lifecycle, wire protocol,
                      state publisher, command server, G-code preview parsing
linuxcnc-config/      LinuxCNC sim lathe INI/tool-table/demo NC program
Scripts/              Godot C# source
  Bridge/              BridgeClient (networking), wire protocol mirror
  Sim/                 PredictionModel (client-side prediction/reconciliation)
  Material/            LatheRadiusProfile + chunked mesh building
  Chips/               Chip classification + pooled spawner
  Scheduling/          Priority job scheduler, adaptive budget, benchmark
  Panel/               Operator panel UI
Scenes/Main.tscn       The one scene
tests/                 C# (xUnit) + Python (pytest) tests
tools/                 record_replay.py
PLAN.md, PERF.md, ASSETS.md   Architecture/decisions, benchmarks, asset status
```

## Known limitations (also see PLAN.md's "non-obvious things" list)

- The cut-profile visual state (which parts of the stock have been removed)
  lives only in Godot's memory — closing/reopening Godot resets the visible
  workpiece to full uncut stock even though the real LinuxCNC machine's
  actual position/program state is untouched. There's no mechanism yet to
  resync the visual history from the real machine.
- No 3D models for the machine body, chuck, tool holder, or shop environment
  — see `ASSETS.md` for the full list of what's a placeholder primitive.
- Chip visuals use one pooled stretched-box mesh for every chip type
  (distinguished by size/elongation/spin/color), not true per-type curled
  ribbon geometry — a deliberate scope cut for a training-visualization tool,
  not an oversight (see `Scripts/Chips/ChipSpawner.cs`'s own doc comment).

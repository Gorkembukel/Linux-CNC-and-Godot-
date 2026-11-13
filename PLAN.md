# CNC Operator Training Simulator — Plan

Status: **Draft, awaiting approval.** Nothing beyond this file has been written yet.

## 0. Environment inspection — findings

| Item | Finding |
|---|---|
| Working dir | `C:\Users\gorke\Documents\linux`, not yet a git repo, contains a blank Godot **4.7** project (`project.godot`) with `.NET`/Mono (C#) enabled (`[dotnet] project/assembly_name="Linux"`), physics engine **Jolt**, renderer forced to `d3d12` on Windows. No scenes/scripts yet — only the default icon. |
| Godot editor | Resolved: `E:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe` (console variant `..._console.exe` available for headless/CLI use). Matches project's `config/features=("4.7", "Forward Plus")`. |
| .NET SDK | **9.0.320** installed natively on Windows (`dotnet --list-sdks`). Good — C# scripts will build fine. |
| Git | Available at `D:\Git`. Repo not yet initialized. |
| LinuxCNC | **Not installed anywhere.** LinuxCNC has no Windows port (it needs a Linux kernel; even the non-realtime "uspace" motion driver is Linux-only). |
| WSL2 | Registered, distro **Ubuntu** present but **stopped**, and the **WSL2 kernel component itself is missing** (`wsl --status` → "The WSL 2 kernel file is not found"). The Ubuntu distro cannot currently start. Fixing this needs `wsl --update`, which failed Claude Code's auto-mode safety classifier as a system-modifying action — **I did not run it**. |
| Docker | Not installed. |
| Python | 3.10.0 native on Windows; no `linuxcnc` Python module (expected — that module only exists inside a LinuxCNC install). |
| CPU / RAM | AMD Ryzen 5 2600, 6 cores / 12 threads, ~16 GiB RAM, Windows 10 Enterprise x64. |
| `C:\dev` | Does not exist yet; will create if/when needed (e.g. GDExtension build output, WSL-shared folder). |

### Blocking issue — needs your action before Phase 0 can run end-to-end

LinuxCNC must run inside **WSL2 Ubuntu**, and WSL2's kernel component needs to be installed/updated first. Please run, in an elevated terminal:

```
wsl --update
wsl --shutdown
wsl -d Ubuntu
```

Until that's done I'll build everything that doesn't require a live LinuxCNC process (Godot scaffolding, bridge protocol code, record/replay tool with synthetic data, unit tests) and switch Phase 0's live demo to real LinuxCNC as soon as WSL is up. I will not attempt `wsl --update` or other system-level installs myself without your go-ahead each time.

Also tell me, or let me look for, the Godot editor executable path so I can build/run the project from the CLI — otherwise I'll hand you exact steps to run manually in the editor at each phase's demo checkpoint.

## 1. Key assumptions (recorded per your instruction, will keep going unless you object)

1. **Target machine: 2-axis lathe (X/Z)**, per your own reasoning in the brief. Material model abstracted behind a `IMaterialModel` interface; `LatheRadiusProfile` implements it now, a future `HeightmapMillModel` later.
2. **Language: C#, not GDScript**, for all simulation/game logic, UI, and networking, because the project is already scaffolded with Mono/.NET and a current SDK is installed. This satisfies the brief's intent ("script only in GDScript, hot paths in C++") better than it sounds — C# is already far closer to GDExtension performance than GDScript is, so the GDExtension (C++) escalation path is reserved for **anything C# profiling in Phase 5 shows is still too slow** (expected candidates: the per-sample sweep/mesh-update inner loop, chip particle simulation at high counts). I will not write a GDExtension module speculatively.
3. **LinuxCNC runs in WSL2 Ubuntu**, not a Linux VM or Docker, since WSL2 is already registered and is the lowest-friction path once its kernel is updated. The LinuxCNC *sim* configuration (non-realtime "uspace") is used — no RT kernel/PREEMPT_RT needed, matching the brief.
4. **Reference hardware = the actual dev machine**: Ryzen 5 2600 (6C/12T), 16 GB RAM, Windows 10. This is a lower mid-range machine (the brief's "mid-range 6-8 core" assumption, at the bottom of that range) — frame budget targets (30 FPS floor, 60 FPS reach) will be validated against this specific CPU.
5. **Transport for the position bridge:** start with the `linuxcnc` Python `stat`/`command` module (used for G-code preview parsing and all non-hot-path status), plus a small userspace C component published over a HAL pin sampled by a lightweight Python/C publisher at the target 200–1000 Hz, sent over **loopback UDP** (not shared memory) from the WSL2 guest to Windows. Rationale: UDP over the WSL2 NAT'd loopback is simple, cross-boundary (guest↔host), and good enough at these rates; shared memory would need a cross-VM mechanism (9P/virtio, more complex) for marginal gain. This choice gets validated with real latency/jitter numbers in Phase 0 and written to `PLAN.md`/`PERF.md` — if UDP jitter across the WSL2 NAT boundary is too high, I'll fall back to a Unix domain socket exposed via `wsl.exe` port-proxying, or `/mnt/wslg`-style shared memory, and document why.
6. **Repo layout** keeps the existing Godot project at the repo root (its `.godot` cache and UIDs already exist there; moving it risks breaking import ids for no benefit). Non-Godot pieces live in sibling folders Godot will not import as resources:
   ```
   /                        Godot 4.7 C# project (existing project.godot, scenes/, Scripts/)
   /bridge/                 Python bridge service (LinuxCNC stat/command, HAL publisher, UDP sender)
   /linuxcnc-config/        LinuxCNC sim INI/HAL config for the 2-axis lathe
   /extension/              GDExtension C++ module (created only if/when Phase 5 profiling demands it)
   /tools/                  record/replay tool, jitter/loss injector, benchmark scene driver
   /tests/                  unit tests (C# GoDotTest/GdUnit for Godot-side, pytest for bridge-side)
   PLAN.md, PERF.md, ASSETS.md, README.md
   ```
7. **Asset sourcing**: I most likely do not have live web access in this environment; I will build Phase 0–3 with primitive-only placeholder geometry (boxes/cylinders for the machine, panel, tool) and list every missing model in `ASSETS.md` as `MISSING — placeholder: <primitive used>`, swappable later behind a single import path per part. If you do have a way for me to fetch CC0/CC-BY models (e.g. you paste direct URLs), I'll use them and record source/author/license in `ASSETS.md`; I will not guess licensing.
8. **Git**: I will `git init`, commit this scaffolding to a first commit, then do all further work on feature branches per phase (`phase-0-bridge`, `phase-1-prediction`, …), never committing directly to main, and will stop for your review before opening/merging anything — per your standing git workflow preference.

## 2. Phase plan

Each phase ends with: a runnable demo (documented steps, since I may not be able to launch the Godot editor myself), automated tests, and a status note appended to this file (what works / what's missing / measured numbers).

- **Phase 0 — Environment + heartbeat.** LinuxCNC lathe sim config running in WSL2. Minimal Python bridge publishes one axis position over UDP. Godot C# scene receives it and moves a cube. *Blocked on WSL kernel update above for the live portion*; bridge protocol, Godot receiver, and a synthetic-data fallback (via the Phase-? record/replay tool, built early) proceed regardless.
- **Phase 1 — Bridge protocol v1 + prediction/reconciliation + machine model + tool motion.** Timestamped, sequenced packet format; G-code preview parse via LinuxCNC's own interpreter in preview mode; client-side prediction with configurable smoothing/snap thresholds; debug overlay (predicted vs. real, error graph, latency/jitter).
- **Phase 2 — Material removal.** Chunked radius-array profile along Z, swept only between confirmed samples, partial mesh regeneration, derived chip cross-section (depth of cut, feed/rev, contact length).
- **Phase 3 — Procedural chips.** Ribbon meshes for continuous/spiral chips, GPU particles for small chips, pooled, simplified physics + sleep, material presets (steel/aluminum/brass) driving chip type/color/gloss.
- **Phase 4 — Operator panel.** Original "Fanuc-style" (not Fanuc-branded) panel: MDI, JOG, handwheel, cycle start/feed hold, tool-offset measurement workflow.
- **Phase 5 — Performance hardening.** Job scheduler with priority classes and time-slicing, adaptive budgets, profiling overlay, benchmark scene, `PERF.md`; GDExtension escalation only where C# profiling proves necessary.

## 3. Architecture at a glance

- **Authority**: LinuxCNC only. Godot never mutates machine state, only sends `command` calls through the bridge and renders what it's told.
- **Bridge process** (Python, in WSL2): talks to LinuxCNC via `linuxcnc.stat`/`linuxcnc.command` for status/mode/offsets/program state and preview parsing; a small HAL/C sampler pushes high-rate axis positions; everything is serialized into one timestamped, sequenced UDP packet stream to Godot. Robust to loss/reorder: Godot only ever trusts the highest sequence number seen and extrapolates between.
- **Godot C# side**: `BridgeClient` (networking + reconnect state machine) → `MachineState` (confirmed, authoritative) → `PredictedState` (render-time extrapolation from last confirmed sample + preview path or dead-reckoned velocity) → `IMaterialModel` (mutated only from confirmed swept volumes) → chip system (cosmetic, driven by predicted *and* confirmed data) → job scheduler (priority classes/time slicing/adaptive budget) coordinating all of the above against the frame budget.
- **Debug tooling**: overlay (predicted vs real, error magnitude, latency/jitter, scheduler queue depths/drops), record/replay tool with jitter/loss injection, benchmark scene.

## 4. Risks

1. **WSL2/LinuxCNC availability** (see blocker above) — biggest near-term risk; everything in Phase 0's live demo depends on it.
2. **UDP across the WSL2 NAT boundary** may have more latency/jitter than same-host IPC; needs real measurement before committing to it past Phase 0.
3. **No Godot CLI access yet** — I may not be able to self-verify editor-only steps (import settings, visual results); I'll give you exact manual verification steps when I can't script it, and say so explicitly rather than claim an untested result works.
4. **No confirmed web access** — asset sourcing (Section 7 of the brief) may stay placeholder-only; I'll flag this per phase rather than silently ship primitives as "done."
5. **C# vs GDExtension performance gap is unverified** until Phase 5 profiling — if C# can't hit the frame budget for the mesh/material hot path on the Ryzen 5 2600, that phase grows to include the GDExtension module; I've scoped for that possibility but won't build it preemptively.
6. **LinuxCNC gives no chip data** (confirmed in the brief) — chip physics direction (continuous vs. segmented vs. broken) is a derived heuristic, not physically simulated; I'll document the heuristic's assumptions in `ASSETS.md`/code comments so its limits are clear, not oversold as physically accurate.

---

## Phase 0/1 status (2026-11-13) — bridge foundation working end-to-end

**What works, verified live against real LinuxCNC (not just read, actually run repeatedly):**
- LinuxCNC 2.9.4 (`linuxcnc-uspace`) sim lathe running headless in WSL2 Ubuntu 26.04, no GUI, no realtime kernel.
- `bridge/linuxcnc_process.py` starts/stops it cleanly and reliably (confirmed 3+ consecutive clean runs).
- `bridge/state_publisher.py` streams STATE packets (position, velocity, feed, spindle, tool, mode, homed, estop, ...) over UDP at a configurable rate; **load-tested at 100 Hz for 8s straight with zero packet loss** across the real WSL2-NAT-to-Windows path.
- `bridge/command_server.py` receives commands (jog, MDI, mode, program control, overrides, tool-change-confirm, homing, estop/on/off) and dedupes by client sequence number.
- `bridge/preview.py` parses a full G-code program via LinuxCNC's own interpreter (`gcode.parse`, the same mechanism Axis's GUI preview uses) into a flat segment list — verified against `demo_facing_turning.ngc`: 28 segments, correct positions and feed rates.
- `bridge/main.py` ties it all together into one runnable service.
- `linuxcnc-config/sim_lathe.ini` + `.tbl` + a demo NC program for a 2-axis lathe (X/Z, metric, 3 tools).

**Five non-obvious environment problems found and fixed along the way** (each cost real debugging time, recorded here so they're never re-discovered):
1. **WSL2 kernel + Ubuntu's disk image were both missing/broken** on this machine — required `wsl --update` and a full `wsl --unregister`/reinstall (done by the user, elevated; I can't run WSL-level installs myself).
2. **`check_config`/display handling**: LinuxCNC's launcher pops a blocking Tk "show_errors" dialog instead of streaming to stdout whenever stdin isn't a tty *and* `$DISPLAY` is set — true here because WSL happens to have an X server at `:0`. Fix: strip `DISPLAY`/`WAYLAND_DISPLAY` from the subprocess's environment before launching (`linuxcnc_process.py`).
3. **Stale SysV IPC (shm + semaphores) from a non-graceful shutdown silently wedges the next start**, sometimes for 90+ seconds, sometimes instantly, depending on whether a leftover semaphore happens to be in a locked state. Fix: sweep all SysV shm/sem/msg owned by the user before every start (`bridge/clear_stale_shm.sh`, called automatically).
4. **The very first `linuxcnc.stat()`/`.poll()` call in a process, made before the child's NML shared memory actually exists, wedges for the rest of that process's life** — found by comparing "fails for 90s in-process" against "a brand new process connects instantly the moment the old one gives up". Fix: wait for a concrete log marker ("DUMMY DISPLAY MODULE...") proving HAL/motion/task/io are already up *before* constructing the first `stat()` object anywhere. A second, separate gotcha: the first successful poll can still read a half-populated snapshot (`axis_mask==0`) — fixed by waiting for `axis_mask != 0`, not just "poll() didn't throw".
5. **drvfs (`/mnt/c/...`) doesn't support hard links or reliable FIFOs**, which broke both an early stdin-FIFO experiment and, non-obviously, LinuxCNC's own parameter-file backup the moment a `.var` file already existed (fatal specifically inside the G-code preview path, merely a tolerated warning in the main instance). Fix: `bridge/deploy.py` copies `linuxcnc-config/` to a native WSL path (`~/godot-cnc-trainer/`) before every run; the Windows-mounted copy stays the source of truth.
6. **Windows Firewall silently drops inbound UDP from the `vEthernet (WSL)` adapter by default** (confirmed with a raw send/receive test: WSL sent, Windows never saw it, on an unfiltered port). Fixed with one elevated rule: `New-NetFirewallRule -DisplayName "Godot CNC Bridge" -Direction Inbound -Protocol UDP -LocalPort 9200,9201 -Action Allow -Profile Any` (the user ran this). **Anyone else setting this project up on a fresh Windows machine will need the same rule — this must go in README.md's setup steps.**

**Also found, unrelated to the above:** the G-code preview's throwaway `gcode.parse()` interpreter instance reports canon coordinates and feed rates in inches whenever the program contains a `G21`, even on an all-mm-configured machine — compensated with an explicit ×25.4 in `preview.py` (`PreviewCanon._record`/`set_feed_rate`), since every real program here is metric by project convention. Root cause not fully understood (not documented anywhere findable); flagged as a known quirk rather than a clean fix.

**What's still missing for Phase 1 to be "done":**
- Prediction/reconciliation logic (currently only the raw confirmed-state stream exists; nothing dead-reckons or smooths yet).
- The Godot C# side entirely (BridgeClient, MachineState, debug overlay) — next up.
- Latency/jitter measurement with numbers written to `PERF.md` (we proved zero loss at 100 Hz for 8s; haven't yet measured one-way latency or jitter precisely, or pushed to the target 200-1000 Hz).
- Unit tests for the protocol, preview, and prediction (protocol round-trip was sanity-checked ad hoc in-session, not yet a committed pytest suite).

---

## Phase 0 complete (2026-11-13) — full pipeline verified end-to-end

Added the Godot C# side: `Scripts/Bridge/BridgeProtocol.cs` (byte-exact mirror of
`bridge/protocol.py`), `BridgeClient.cs` (autoload singleton: binds the state UDP
socket, sends commands to the bridge via WSL2's Windows->Linux localhost
forwarding, tracks connection health with a timeout, never blocks), `MachineState.cs`
(confirmed-state holder), and a minimal `Scenes/Main.tscn` demo (a cube at the
reported X/Z position + an on-screen status label).

**Verified for real, not just by inspection:** started the bridge in WSL2, ran Godot
headless (`--headless`) against the actual built `Linux.dll`, and watched its debug
output track the live machine: connected immediately, polled at the full 100 Hz,
and after jogging the real LinuxCNC instance to X=35/Z=-70 from a third, independent
script, Godot's next read showed exactly `pos=(35.000,-70.000)` within one state
packet. This is Phase 0's literal success criterion from the brief ("move a cube in
Godot" from a bridged axis position) with both axes, not just one.

One more environment fix needed, beyond the two from Phase 0/1 above: Godot's C# build needs a hand-written `Linux.csproj`
(`Sdk="Godot.NET.Sdk/4.7.2"`, `net8.0`) since the engine doesn't generate one in
`--headless` mode — only discovered by trying. `dotnet build` then needs NuGet
access (confirmed working on this machine) to restore the Godot SDK packages.

**Status:** Phase 0 is done. Moving into Phase 1's remaining piece (prediction/
reconciliation + debug overlay) and Phase 2 (material removal) next, continuing
per the user's "do all phases, don't stop" instruction (2026-11-13).

---

## Phase 1 complete, Phase 2 (material removal) working end-to-end (2026-11-13)

**Phase 1:** `Scripts/Sim/PredictionModel.cs` (dead-reckon from the last confirmed
position+velocity, exponential smoothing for small errors, instant snap past a
configurable threshold) and `Scripts/DebugOverlay.cs` (F3: predicted vs.
confirmed position, an error-magnitude graph, state-stream jitter, command
round-trip latency). One honestly-scoped simplification: prediction dead-reckons
by velocity only, it does not thread along the specific preview-segment geometry
through corners/arcs -- noted as a deliberate simplification, not an oversight.
Also noted: true one-way bridge->Godot latency isn't computable without
cross-machine clock sync (WSL2 Linux clock vs. Windows clock); the overlay shows
command round-trip time (fully measurable on one clock) instead of a fabricated
one-way number.

**Phase 2:** `Scripts/Material/IMaterialModel.cs` + `LatheRadiusProfile.cs` (a
fixed-step radius array along Z; sweeps the tool between two CONFIRMED samples
only, nose-radius-offset, `min()`'d into the array, chunked dirty-tracking) and
`WorkpieceVisual.cs` (revolves the radius profile into a chunked mesh, rebuilding
only dirty chunks, capped per frame). Main.cs wires BridgeClient's confirmed
`StateUpdated` signal straight to the material sweep -- deliberately NOT gated on
program-running state, since a real lathe cuts whatever the tool touches
regardless of mode (jogging into the spinning stock is a real operator mistake,
and part of the point of a training sim).

**Found and fixed along the way:** `sim_lathe.ini`'s `TOOL_CHANGE_POSITION` had
Z=150, outside our own Z axis's configured 50mm max limit -- every M6 tool
change failed with "would exceed Z's positive limit" until this was caught by
actually running the full demo program through a tool change, not just
MDI-testing individual moves. Fixed to Z=40.

**Verified live, not just unit-tested:** ran the full demo program against the
live bridge (confirming/pulsing `iocontrol.0.tool-change`/`tool-changed` by hand
to stand in for Phase 4's not-yet-built panel), and watched Godot's debug output
live: the stock radius at Z=-30 read 25.000mm until the tool (radius 20.2mm +
0.8mm nose) passed that point, then read exactly 21.000mm from that frame on --
material removal driven by real LinuxCNC motion, through the real bridge, with
zero special-casing for the test.

**Test suite now 20 total, all passing:** 10 C# (xUnit: prediction model
behavior incl. a noisy-known-path convergence test, cross-language protocol
fixture, LatheRadiusProfile incl. a full 5-pass "known program -> expected final
profile" test matching the brief's own phrasing) + 10 pytest (protocol,
LinuxCNC lifecycle, G-code preview).

**What's still missing:** Phase 3 (chips), Phase 4 (operator panel), Phase 5
(job scheduler/adaptive budget/GDExtension/PERF.md), record/replay tooling,
ASSETS.md, README.md. Continuing now.

---

## HANDOFF / RESUME HERE (2026-11-13, session ending on usage limit)

The user's session is about to hit a usage limit; a fresh agent will continue
from here. Read this section first, then the Phase status sections above it
(all still accurate) for the "why" behind each decision.

### Exact repo state
- Branch: **`phase0-godot-bridge-client`** (git-workflow-discipline: real work
  happens on a feature branch, not `master` -- `master` only has the very first
  scaffold commit; don't be confused that it looks "behind", that's intentional
  and not yet merged, on purpose, per that skill).
- Last commits on this branch, in order: initial scaffold -> Godot bridge
  client (Phase 0) -> prediction/overlay/tests (Phase 1) -> material removal
  (Phase 2) -> chip system (Phase 3).
- **Uncommitted right now**, needs a commit as this session's very last action
  (see below): `Scripts/Panel/OperatorPanel.cs` (new), `Scenes/Main.tscn`
  (modified to add the panel node), `.godot/` cache changes (not tracked,
  ignore). Build is clean (0 errors/warnings), smoke-tested headless with no
  crash. **NOT YET live-tested against the real bridge** -- that's the first
  thing to do on resume (see "Immediate next steps").

### What's done and verified LIVE (not just compiled) -- Phases 0, 1, 2, 3
Everything in the Phase 0/1/2/3 status notes above this section. Summary: the
LinuxCNC sim lathe, Python bridge (state stream, command dispatch, G-code
preview), and Godot C# client (connection, prediction/reconciliation, debug
overlay, material removal, chip spawning) all work end-to-end, proven by
actually running the demo program and watching real position data flow through
every layer. 15 C# tests (`Tests/`, run with `dotnet test` from `Tests/`) + 10
Python tests (`tests/bridge/`, run with `python3 -m pytest tests/bridge/ -v`
**inside WSL2**, not Windows) all pass.

### What's in progress -- Phase 4, operator panel
`Scripts/Panel/OperatorPanel.cs` is written: mode selector, jog buttons
(continuous + incremental with a step-size dropdown), MDI line input, cycle
start/feed-hold/stop/single-block/tool-change-confirm, feed/spindle override
sliders, and a touch-off workflow (two text fields + buttons that compute and
send `G10 L1` tool-offset MDI lines from the confirmed position). Built
programmatically in `_Ready()` rather than hand-authored in the `.tscn` (more
maintainable for a control layout this size). Wired into `Scenes/Main.tscn` as
`UI/OperatorPanel`. **Watch for**: the namespace `CncTrainer.Panel` collides
with Godot's own `Panel` class -- already fixed once (use `Godot.Panel`
explicitly) but double-check if adding more code in that file.

### Immediate next steps (in order)
1. **Live-test the operator panel** against the real bridge. Godot's
   `--headless` mode can't simulate button clicks easily, so either: (a) run
   Godot normally (not headless) if a display is available to the session, or
   (b) write a small throwaway C# or `InputEventMouseButton`-driven script to
   fire the panel's button `Pressed` signals directly and confirm the right
   bridge command goes out (cheaper, no real window needed) -- see how prior
   phases validated via `CNC_DEBUG_PRINT=1` + headless + a separate script
   driving LinuxCNC directly as the "other side" of the conversation; the same
   pattern works here by calling `OperatorPanel`'s build methods' button
   actions directly in a quick test scene, or by checking the UDP commands
   Godot sends with a raw socket listener (mirrors how `udp_listen.py` in the
   scratchpad validated the state stream back in Phase 0).
2. Bridge-side: `CommandServer.TOOL_CHANGE_CONFIRM` pulses
   `iocontrol.0.tool-changed` via `halcmd setp` -- confirm this actually still
   works when invoked over the real UDP command path (it was only tested via
   direct `halcmd` calls from a scratch script before, not through
   `OperatorPanel` -> `BridgeClient.ConfirmToolChange()` -> UDP ->
   `CommandServer` end to end).
3. Once the panel is verified, commit it (Phase 4 status note + commit,
   matching the pattern of every phase above).
4. **Phase 5**: job scheduler with priority classes/time-slicing/adaptive
   budget, a profiling overlay (frame time, per-class time, queue
   lengths/drops), a benchmark scene, `PERF.md` with real numbers from this
   machine (Ryzen 5 2600, 6C/12T, 16GB -- see PLAN.md's environment table at
   the top). GDExtension (C++) only if C# profiling actually shows it's
   needed -- don't build it speculatively (recorded as an assumption early in
   this file).
5. Record/replay tool (`tools/` per the original repo-layout assumption,
   not yet created) -- captures the bridge's UDP stream to a file and plays it
   back with configurable jitter/packet-loss injection, so Godot-side dev
   doesn't need LinuxCNC running. This directly reuses `bridge/protocol.py`'s
   pack/unpack functions.
6. `ASSETS.md` (placeholder-primitives only so far -- box meshes for
   everything; list what's missing, per the brief's section 7 fallback plan)
   and `README.md` (setup steps: `wsl --update`/reinstall already done by the
   user; `apt install linuxcnc-uspace` already done; the Windows Firewall rule
   already added by the user -- all one-time, already-completed steps on
   *this* machine, but must be documented for anyone else setting the project
   up fresh).

### How to resume testing (commands that work on this exact machine)
```
# Clean LinuxCNC/IPC state (always do this before starting it fresh):
wsl -u root -d Ubuntu -- bash -lc "pkill -9 -x linuxcncsvr; pkill -9 -x rtapi_app; pkill -9 -x milltask; sleep 1; bash /mnt/c/Users/gorke/Documents/linux/bridge/clear_stale_shm.sh"

# Start the bridge (run in background, wait for "publishing state" in its output):
wsl -d Ubuntu -- python3 -u /mnt/c/Users/gorke/Documents/linux/bridge/main.py --rate-hz 100

# Build the Godot C# project:
cd C:\Users\gorke\Documents\linux && dotnet build Linux.csproj

# Run Godot headless (ALWAYS capture the PID and Stop-Process it explicitly
# afterward with its own literal PID -- a pattern-based Stop-Process on
# multiple matches gets blocked by the safety classifier, and --quit-after
# does NOT reliably exit the process, both discovered the hard way this
# session; leftover processes hold UDP port 9200 and silently break the next
# test):
$p = Start-Process -FilePath "E:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" -ArgumentList "--headless","--path","C:\Users\gorke\Documents\linux" -PassThru -RedirectStandardOutput "<some log path>" -NoNewWindow
# ... wait, read the log, then:
Stop-Process -Id $p.Id -Force

# C# tests:
cd C:\Users\gorke\Documents\linux\Tests && dotnet test

# Python tests (inside WSL; note the actual on-disk folder is lowercase
# "tests" even though Windows/dotnet happily display/accept "Tests" --
# case-insensitive NTFS vs. case-sensitive WSL, discovered the hard way):
wsl -d Ubuntu -- python3 -m pytest /mnt/c/Users/gorke/Documents/linux/tests/bridge/ -v
```

### Non-obvious things worth NOT re-discovering (full detail is in the Phase
status sections above; this is just the index)
1. WSL2 kernel + Ubuntu disk image were both broken on first use -- fixed, done.
2. LinuxCNC's launcher pops a blocking GUI dialog unless `$DISPLAY` is stripped
   from its subprocess environment -- already handled in `linuxcnc_process.py`.
3. Stale SysV IPC (shm+semaphores) after a non-graceful shutdown randomly wedges
   the next start for up to 90+ seconds -- already handled automatically
   (`clear_stale_shm.sh`, called from `start()`).
4. The *first* `linuxcnc.stat()` call made before the child's NML shared memory
   exists permanently wedges for that process's whole life -- already handled
   (`wait_for_nml_ready` waits for a log marker first).
5. drvfs (`/mnt/c/...`) doesn't support hard links/reliable FIFOs -- already
   handled (`deploy.py` copies config to a native WSL path before running).
6. Windows Firewall blocks inbound UDP from `vEthernet (WSL)` by default -- the
   user already added the fix rule (`New-NetFirewallRule ... -LocalPort
   9200,9201`); anyone else setting this up fresh will hit this and needs the
   same rule (must go in README.md).
7. `gcode.parse()`'s standalone preview interpreter reports coordinates/feed
   rates in inches whenever the program contains a `G21`, even on an all-mm
   machine -- compensated with an explicit ×25.4 in `preview.py`, documented
   there as an unexplained quirk, not a true fix.
8. `sim_lathe.ini`'s `TOOL_CHANGE_POSITION` originally had Z=150, outside the
   configured 50mm Z limit -- fixed to Z=40. Only an actual full-program run
   (not MDI-testing individual moves) surfaces bugs like this; keep doing full
   runs, not just unit moves, when validating later phases.
9. Repeatedly killing Godot processes by *pattern* (`Where-Object` + pipe to
   `Stop-Process`) gets blocked by the safety classifier as "destructive/
   unwanted"; killing by a single literal PID works. `--quit-after` is not a
   reliable way to make a headless Godot process actually exit on its own.

10. WSL2's documented UDP "localhost forwarding" (Windows -> 127.0.0.1 reaches
    a socket bound in the VM) does NOT work on this machine -- confirmed with a
    raw socket test. Fixed: the bridge writes its own eth0 address to
    `bridge/.bridge_endpoint` on every start; `BridgeClient.cs` reads it
    instead of assuming `127.0.0.1`. This is why the Godot->bridge COMMAND path
    (unlike the bridge->Godot STATE path) stayed broken through all of Phases
    0-3 despite everything else working -- nothing in those phases ever
    actually sent a command from Godot to the bridge over the network; they
    all drove LinuxCNC directly via a second script standing in for Godot.
    **Lesson for future phases: a path being architecturally symmetric to one
    that's proven working is not proof it works -- test each direction live.**
11. `CommandServer.run_forever` had no exception handling around
    `handle_packet` itself (only around applying an already-parsed command) --
    one malformed/non-protocol UDP packet permanently killed the whole
    command-listening thread with no visible error anywhere. Fixed: caught and
    logged per-packet now.
12. Real chip sizes (sub-mm to a few mm) are only a handful of pixels at a
    camera distance that can see the whole ~300mm part -- invisible in
    practice. Fixed with an explicit `VisualSizeMultiplier` (6x) in
    `ChipSpawner`, documented as a visibility exaggeration, not physical
    accuracy.
13. The material/chip state (`LatheRadiusProfile`, chip pool) lives only in
    Godot's memory, with no persistence -- closing/reopening Godot resets the
    visual workpiece to full uncut stock even though the real LinuxCNC
    machine's position/program state is untouched and continues normally. Not
    a bug exactly (Godot *should* reconnect cleanly, and does), but a real,
    currently-unaddressed gap: there's no "resync the visual cut history from
    the real machine" mechanism. Worth a line in a future phase or in
    known-limitations documentation (README.md/ASSETS.md), not silently
    ignored.

## Phase 4 (operator panel) -- confirmed working live, end to end (2026-11-13)

User-driven, not just agent-driven: the user opened the Godot app themselves,
clicked through MACHINE ON -> HOME ALL -> LOAD PROGRAM -> CYCLE START ->
CONFIRM TOOL CHANGE on the actual on-screen panel, ran the real demo program
on the real LinuxCNC instance, and confirmed seeing chips fall during cutting
once the two fixes above (command routing, chip visual size) landed. Also
added `Scripts/CameraController.cs` (right-drag orbit, scroll zoom) after the
user reported being stuck at a single fixed angle.

The `CNC_COMMAND_SELFTEST` hook added earlier in `Main.cs` to debug the command
path is no longer needed for that purpose (the real bug was the network path,
not the panel logic) but is harmless and left in place, gated off by default.

**Still not done in Phase 4**: nothing else identified yet; the panel covers
mode/jog/MDI/program-load/cycle-control/overrides/tool-change/touch-off per
the brief. Revisit if gaps turn up during further use.

---
*Phase 0 environment approved implicitly by the user proceeding with the WSL fix; "do all phases, don't stop" given 2026-11-13.*

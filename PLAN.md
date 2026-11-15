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
14. From this Windows Git-Bash tool specifically (not PowerShell): passing
    `wsl.exe` a bare POSIX path as its own argv (e.g.
    `wsl.exe -d Ubuntu -- python3 -u /mnt/c/.../main.py`) gets silently
    mangled by Git Bash's own path translation into garbage like
    `D:/Git/mnt/c/.../main.py` before `wsl.exe` ever sees it -- confirmed by
    comparing `wsl.exe -d Ubuntu -- pwd` (fine) against the same tool passed
    an explicit `/mnt/c/...` path (broken). Fix: always wrap the whole
    command as ONE string argument to `bash -lc "..."`, e.g.
    `wsl.exe -d Ubuntu -- bash -lc "python3 -u /mnt/c/.../main.py"` -- this
    is the pattern used everywhere above and it isn't affected.
15. `/usr/bin/linuxcnc` (the installed system launcher, not anything in
    this repo) refuses to run a second concurrent instance on the same WSL
    distro -- not a shared-memory/NML-key collision (tried giving each its
    own `.nml` file with distinct keys first; made no difference), but a
    single, globally fixed `LOCKFILE=/tmp/linuxcnc.lock` the script checks
    on every headless start: if one's already there, it assumes a crashed
    leftover and kills whatever's running before starting itself. `/tmp` is
    one filesystem shared by every user on a distro, so a second Linux user
    doesn't escape it either -- only a genuinely separate WSL distro (its
    own filesystem) does. Fixed by installing a second distro
    (`Ubuntu-26.04`, matching the original's version -- confirmed 24.04
    does NOT carry the `linuxcnc-uspace` package, 26.04 does) for the
    mill's own LinuxCNC instance; empirically verified two concurrent
    instances across the two distros don't interfere with each other at
    all. See the "Phase A" status section above for the full trail.

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

## `start.ps1` confirmed working end to end (2026-11-13)

User-run, not simulated: with the previous session's bridge/Godot fully
closed, ran `start.ps1` and watched it clean stale state, start the bridge in
its own window, detect readiness (fast this time -- LinuxCNC's cold-start
time is genuinely variable run to run, as documented in Phase 0's notes, not
always the ~90s worst case), launch Godot automatically, and the user
confirmed "BRIDGE CONNECTED" on screen. **Every deliverable from the brief's
section 9 is now done and the single-command startup path is proven, not
just written.**

## Full test suite confirmed clean in one run (2026-11-13)

With no bridge/Godot session active, ran everything back to back: 21 C#
(`dotnet test`) + 13 Python (`pytest tests/bridge/`, including the
LinuxCNC-lifecycle tests that need their own instance and would conflict with
a live session) -- **34/34 passing**, and confirmed no LinuxCNC processes were
left running afterward.

## Primitive machine body + physical console added (2026-11-13)

After an extensive search found no suitable CC0/CC-BY lathe/chuck/tool-holder
model (see ASSETS.md for the full search record -- everything findable is
either commercial-licensed or STL-only with no conversion tool available
here), built a primitive-based lathe body per the brief's own explicit
fallback and a user request for more visual realism:

- `Scripts/MachineVisual/MachineBody.cs`: bed, headstock, chuck (sized larger
  than the stock radius, positioned at the chuck-end Z), and a carriage that
  tracks the tool's Z position every frame -- all procedurally generated
  primitives, following this project's existing pattern (WorkpieceVisual,
  ChipSpawner) rather than hand-tuned `.tscn` transforms.
- `Scripts/MachineVisual/ConsoleStand.cs`: a free-standing physical console
  (post + tilted panel + decorative buttons) with a monitor screen rendered
  via a `SubViewport` showing live position/feed/spindle/tool/mode data.
  **Scope note, stated plainly in the file's own doc comment**: this screen
  is read-only/visual -- the proven, user-confirmed-working 2D `OperatorPanel`
  overlay remains the actual way to send commands. Making the 3D console
  independently clickable (3D raycasting onto UI, or duplicating
  OperatorPanel's logic into the SubViewport) was deliberately left out to
  avoid risking the working control path for a visual-only improvement.

Found and fixed one bug while building this: both new scripts, declared in
`namespace CncTrainer` (not a nested namespace), got `CS0118: 'Material' is a
namespace but is used as a type` -- the same class of collision as the earlier
`Panel`/`Godot.Panel` issue, caused by `CncTrainer.Material` (the Phase 2
namespace) shadowing `Godot.Material` for unqualified `Material` references
in any file sharing the parent `CncTrainer` namespace. Fixed by qualifying as
`Godot.Material` explicitly, same pattern as before -- worth remembering if
adding more top-level (non-nested) scripts: check for this before assuming a
build error is something else.

## Chuck join fix, FP/TP player controller, and UI shrink (2026-11-13)

- Fixed a visual bug reported live: the chuck was butted exactly flush
  against the stock's starting Z, so a facing cut right at that point thinned
  the stock to near-zero radius exactly at the chuck face, looking like the
  bar had snapped off. Extended the chuck to overlap ~30mm into the stock's
  own range so that region is always hidden behind opaque chuck geometry.
- Added `Scripts/Player/PlayerController.cs`: a CharacterBody3D with capsule
  collision walking against the floor/machine/console collision from the
  meter-scale refactor, WASD + mouse-look, V toggles first-/third-person
  (SpringArm3D), E near the console toggles mouse capture to use the 2D
  OperatorPanel. The pre-existing orbit CameraController is no longer the
  default (`current=false`) but stays in the scene as a dev tool.
- **Mouse-look bug, found live**: WASD worked, looking did not. Root cause:
  `_UnhandledInput` -- with the operator panel's Controls covering part of the
  screen, GUI mouse-motion handling ate the look input before it reached an
  Unhandled callback. Fixed by switching to `_Input`. Also added a
  click-to-recapture safety net for a focus-timing edge case.
- **UI covered most of the screen, found live**: set an explicit 1920x1080
  viewport in `project.godot` (there was none before -- whatever the default
  was, it wasn't 1080p), gave `OperatorPanel` a shared small `Theme`
  (12px default font, shrinks every child control uniformly), reduced its
  bounds 804x404 -> 550x250, split its overcrowded 8-button mode row into two
  rows, and shortened several of the longest button labels. `StatusLabel` and
  `DebugOverlay` shrunk to match.
- **Known gap, not yet addressed**: the player has no visible mesh at all --
  third-person mode (V) currently shows nothing where the character's body
  should be. Next up.

## Player loop functionally complete (2026-11-13)

Added the placeholder body visual (capsule torso + sphere head, shown only in
third-person -- same "no suitable CC0/CC-BY humanoid found" situation as the
machine body). Found and fixed a second camera bug live: third-person pitch
was inverted (FP felt right, TP felt backwards) because the SpringArm3D was
nested as a child of the already-pitched `_head` node with an extra 180deg
rotation on top, composing badly. Fixed by making the arm a sibling of `_head`
and applying pitch to both independently as flat Euler assignments.

**The core human-interaction loop the user asked for is now functionally in
place**: walk (WASD) and collide with the floor/machine/console, look around
(mouse, fixed from an earlier GUI-input-stealing bug), switch first-/third-
person (V), approach the console and get a prompt, press E to use the real
2D operator panel. Not yet re-confirmed live after the pitch fix specifically
(asked the user; no response yet on that one point, but no new complaint
either).

**Still open, not yet asked to prioritize:**
- True 3D-interactive console (clicking the physical model's buttons) vs.
  today's proximity+E shortcut to the existing 2D panel -- deliberately
  scoped out so far (see ConsoleStand.cs's doc comment).
- Workpiece visual state not persisted across a Godot restart (noted since
  Phase 2/3, still true).
- Chip ribbon/spiral geometry is still the simplified pooled-box approach
  (ChipSpawner.cs's own doc comment).
- Haven't re-run the full test suite (C#+Python) since before this player-
  controller work started, specifically to confirm nothing in the WorldScale/
  collision refactor regressed the LinuxCNC-lifecycle or preview tests (those
  need no live bridge session running to execute safely).

## 3D-interactive console, readable captions, status LEDs (2026-11-14)

The "True 3D-interactive console" item above is done: `ConsoleStand.cs` was
rebuilt as a real multi-page CNC-style control (`b49a9d5`, `ab5f5a3`) -- every
face button (page nav, ON/HOME/CYCLE/HOLD/STOP/T.CHG/jog/RUN, E-STOP) is a
real `StaticBody3D` the player's crosshair raycast can hit and press
(`TryPress`, called from `PlayerController`), driving the same `BridgeClient`
calls the 2D panel uses. MDI typing/touch-off text entry deliberately stays
2D-only (a world-space button can't host a text field).

Found and fixed live: button captions were dark text on dark button material
-- unreadable (`900edaa`). Fixed with a bright amber, black-outlined,
billboarded `Label3D` (`AddButtonCaption`) so captions read against any
background color from any approach angle.

Added three status LEDs (POWER/CYCLE/ALARM) to the panel, visible without
reading the screen, matching a real CNC pendant's convention. Built in one
commit but initially left unwired (materials existed, nothing drove them);
wired up this session: `SetLed` now flips each LED's emission energy from
`BridgeClient.State`'s `MachineOn`/`InCycle`/`Estop` every frame, same
pattern as the screen text, and forces all three dark when the bridge isn't
connected.

Re-ran the full test suite per the still-open item above: 21 C# (`dotnet test
tests/Tests.csproj`) + 13 Python (`pytest tests/bridge/` inside WSL2,
including the two LinuxCNC-lifecycle tests) -- **34/34 passing**, confirming
nothing in the WorldScale/collision/player-controller work regressed the
bridge or prediction/material logic.

**Still open:**
- Workpiece visual state not persisted across a Godot restart (Phase 2/3,
  still true).
- Chip ribbon/spiral geometry is still the simplified pooled-box approach.

## Chuck/cut realism fix + finished parts become real pickupable objects (2026-11-14)

User-reported realism problems, fixed before moving on to mill support and a
training/scenario mode (the next two agreed priorities; part transfer via a
robot arm is explicitly deferred further still -- not started):

**Chuck/cut orientation bug.** The lathe looked like it tore material
directly off the chuck. Root cause, found by reading the demo program and
the machine geometry together: `MachineBody.cs`'s chuck/headstock sat at
`StockZMax` (+5mm) -- the SAME end where `demo_facing_turning.ngc`'s own
motion (face at Z0 -> turn OD to Z-60 -> part at Z-55) actually cuts. That
program's own header comment ("Z0 = chuck face") had it backwards: Z0 is the
stock's free/exposed tip (faced first), more-negative Z is toward the chuck.
Fixed by moving the chuck/headstock to the `StockZMin` end instead -- purely
decorative geometry (the chuck has no collision), no G-code or LinuxCNC
coordinate changes. Fixed the program's misleading comment too (motion
itself was already correct).

**Finished parts now become real objects.** A parting cut used to just leave
a permanent zero-radius "ghost" in the one continuous stock mesh. Now:
`LatheRadiusProfile.Sweep` detects a bucket crossing from material to ~0
radius (a parting cut) and reports it; `LatheRadiusProfile.Sever` splits the
stock there, returning the detached piece's pre-cut radii; the new
`WorkpiecePartBuilder` (pure-math volume/centroid separated from
engine-dependent mesh/collision building, same split as
`LatheRadiusProfile`/`WorkpieceVisual`) turns that into a real mesh, a
convex-hull collision shape, and a mass computed from the material's real
density (added `MaterialPreset.DensityKgPerM3`: steel 7850, aluminum 2700,
brass 8500 kg/m3); `Main.ExtractFinishedPart` spawns it as a `RigidBody3D`
tagged `"pickupable"`. `PlayerController` gained an `F` key (reusing the
same crosshair-raycast pattern as the console buttons) to pick up/carry/drop
it, with the interact-prompt label repurposed to show which applies.

**Two real bugs found live, not just by inspection** (ran the actual demo
program and separate MDI-driven parting cuts against the live bridge, with
Godot running headless and `CNC_DEBUG_PRINT=1`, watching for errors across
several successive part-offs):
1. Jolt failed to build a convex collision shape for a thin part
   ("initial triangle area too small") -- the facing-the-tip operation at
   the very start of every real turning program is, in this 1D
   radius-profile model, structurally identical to a parting cut (both are
   "a bucket's radius reached ~0"), so it was spawning a thin 5mm-long disc
   every run, thinner than Jolt's default 0.04m convex margin. Fixed by
   setting an explicit small `Margin` (0.001m) on the generated
   `ConvexPolygonShape3D`. (Also added a `MinPartLengthMm` floor in
   `WorkpiecePartBuilder.Build` as a separate, smaller safety net for
   genuinely-degenerate slivers; the 5mm facing stub itself is now handled
   correctly rather than suppressed -- it's a real, if small, piece of
   material a facing cut removes.)
2. `LatheRadiusProfile.Sever` always zeroed from the cut point to the
   array's true end, even when an earlier `Sever` had already zeroed part of
   that range -- so a second part-off on the same remaining stock kept
   re-including the first extraction's already-empty tail as if it were
   still live material (a growing zero-radius "ghost" on the new piece, got
   worse with every subsequent part-off). Fixed by stopping at the first
   already-zero bucket, confirmed with a repeated-severance test and by
   re-running the live MDI reproduction (bucket counts matched expectations
   exactly after the fix, were inflated before it).

**Stated simplifications, not solved, same spirit as this project's existing
ones:** the collision shape is a convex hull of the revolved profile (fine
for ordinary stepped/tapered turning, would smooth over a re-entrant groove
if one's ever cut); a held part doesn't do obstruction/collision sweeping
against walls while carried (glued-to-camera carry, like most games); robot/
automation part transfer between machines is not started at all yet.

**User-confirmed live (2026-11-14)**: ran it themselves via `start.ps1` and
confirmed it looks right -- the chuck's new position and the finished-part
pickup/carry both hold up in practice, not just in the headless/MDI smoke
test above.

## Real tool turret replaces the "red cube" (2026-11-14)

Before starting mill support, fixed another user-reported realism gap: the
tool was a plain red cube, teleporting instantly on a tool change with no
sense of a real magazine. `MachineBody.cs` gained a 3-station turret (one
per real tool in `sim_lathe.tbl`: T1 roughing/amber insert, T2
finishing/cyan insert, T3 parting/wider insert matching its 3mm-wide note),
mounted on the existing `_carriage` so it inherits the carriage's Z
tracking for free.

Two things animate every frame in the new `MachineBody.UpdateTool`: the
turret's rotation rate-limits toward whichever station matches the bridge's
current `ToolNumber` (a real "change motion" instead of an instant swap),
and the turret's own Y position tracks `toolTipWorldY + TurretToolReachMm`
so the active (fixed-length) tool's insert lands exactly on the real tip at
any radius/retract distance -- this also removes the carriage's old "doesn't
model the cross-slide's own radial motion" caveat, since the turret now
does. The two small angle formulas (target angle from tool number;
rate-limited step) are pulled out as pure-math `public static` methods,
unit-tested the same way `LatheRadiusProfile`'s math is.

`ToolCube` (the old flat marker, plus its now-unused mesh/material
sub-resources) removed from `Scenes/Main.tscn`; `Main.cs` now calls
`_machineBody.UpdateTool(...)` instead of positioning it directly.

Verified live against the real bridge (headless Godot, `CNC_DEBUG_PRINT=1`):
drove 4 MDI tool changes (T1->T2->T3->T1, each confirmed via the existing
`iocontrol.0.tool-changed` halcmd pulse) plus a retract/approach jog, with
no engine errors. Visual confirmation of how the rotation/animation actually
looks still needs the user's own run via `start.ps1` -- this agent has no
windowed session.

## Phase A: N-axis, SOLID machine architecture -- foundation for mill support (2026-11-14)

Before building the actual mill, generalized the entire bridge/sim stack
from "hardcoded 2-axis lathe" to an N-axis, multi-machine-capable design per
the user's explicit request ("sektörde kaç eksenli makina varsa hepsini
kapsayacak şekilde OOD ve SOLID prensiplerine uygun bir tasarım yap") and
their choice to run the mill **alongside** the lathe in the same world
later, not a separate scene. Three research passes (wire protocol, C#
bridge/UI layer, material-model/test fixtures) mapped every 2-axis
assumption before touching anything -- see the approved plan's Context
section for the full list.

**Wire protocol** (`bridge/protocol.py` + `Scripts/Bridge/BridgeProtocol.cs`,
hand-mirrored as always): `StateSample`'s `pos_x/pos_z/vel_x/vel_z` became
fixed-capacity `position`/`velocity` (6 slots -- X,Y,Z,A,B,C, LinuxCNC's own
axis-letter order) plus `active_axis_mask`/`homed_mask` bytes replacing
`homed_x`/`homed_z`. Fixed capacity, not variable-length: zero framing
complexity, and a machine with different active axes (the mill) needs zero
protocol change. `PreviewSegment` got the same treatment. Packet grew from
96 to 159 bytes (18-byte header + 141-byte body) -- irrelevant at 100Hz on
loopback. `AxisId` (`CncTrainer.Bridge`, mirrored by `bridge/protocol.py`'s
`AXIS_X..AXIS_C` constants) and `AxisVector` (a zero-allocation 6-float
struct, same shape as Godot's own Vector2/Vector3, with `+`/`-`/`*`/
`Length()`) replace the old lathe-only `Vector2` convention everywhere:
`MachineState`, `PredictionModel` (the single largest "shape of the data"
change -- it's built entirely on vector arithmetic).

**`BridgeClient` is no longer a Godot autoload singleton** -- it's a plain
node, instantiated as a child of `Main` in `Scenes/Main.tscn`, configured
per-instance (ports, endpoint file, axes). This is the actual fix that makes
a second live machine possible; a fixed `/root/BridgeClient` path cannot
support two. Every consumer that used to self-lookup that fixed path
(`Main.cs`, `OperatorPanel.cs`, `ConsoleStand.cs`, `DebugOverlay.cs`) instead
receives its reference injected via a new `Configure(MachineRig rig)` method,
called from `Main._Ready` once the rig exists.

**New `Scripts/Machines/`**: `MachineProfile` (per-machine-type axis/joint
descriptor -- deliberately just data, no factory callbacks yet; Phase A has
exactly one real machine type, and a factory with one implementation to
factor against would be speculative) and `MachineRig` (bundles one live
machine's `BridgeClient` + `MachineProfile`). `Main.cs` holds a
`List<MachineRig>` -- today exactly one (the lathe); Phase B's mill appends
a second without changing this structure. **Important finding that shaped
the design**: `IMaterialModel.Sweep`'s `SweepResult` (1D dirty-bucket
indices, a "severed index" with no equivalent on a mill) is fundamentally a
lathe-radius-profile concept that can't honestly represent a mill's 2D
heightmap -- forcing both to implement one shared interface would violate
Liskov Substitution. Left `IMaterialModel`/`LatheRadiusProfile` exactly as
they are; `Main.cs`'s cutting logic stays explicitly lathe-specific (a small
`LatheXZ` helper projects the new 6-axis `AxisVector` down to the
`Vector2(radius, Z)` that code already expects) rather than being forced
through a leaky abstraction. `ChipClassifier` needed zero changes -- it
already only takes 3 plain scalars with no lathe-specific math in its body.

**OperatorPanel/ConsoleStand's jog and home buttons now loop over the
owning rig's axis list** instead of 4 hardcoded X/Z button literals --
verified to produce the exact same labels/behavior for the lathe (zero
visible change), ready for however many axes a future machine's rig
reports. `ConsoleStand` (a `[Tool]` script that must still preview correctly
in the editor with no bridge) builds its physical buttons once with a
placeholder default axis list, then rebuilds via `Configure()` once the
real rig exists, so its buttons always match whatever it's actually wired
to.

**Verified live, not just by inspection**: 36 C# tests (29 -> 36: new
`AxisVectorTests`, updated `ProtocolCrossLanguageTests`/
`PredictionModelTests`) + 13 Python tests (updated `test_protocol.py`,
`test_preview.py`, `test_record_replay.py` for the new field shapes) all
green. Then the critical check: restarted the bridge+LinuxCNC fresh, ran
headless Godot with `CNC_DEBUG_PRINT=1` through the exact same sequence as
the previous session's feature work -- 4 MDI tool changes (turret
animation), then two real parting cuts producing two correctly-sized,
correctly-massed finished parts (0.748kg/98 buckets and 0.216kg/29 buckets,
matching the pre-refactor baseline within normal sampling noise) -- with
zero engine errors throughout. One red herring during this check: an
"extraction didn't fire" moment that turned out to be leftover tool state
(T1's 0.8mm nose radius) from chaining two ad hoc test scripts in one
LinuxCNC session, not a regression -- confirmed by a clean restart.

**Explicitly deferred to Phase B**: the mill itself (3-axis LinuxCNC sim
config, the heightmap material model, mill visuals, a mill demo program),
and the real open risk flagged in the approved plan -- LinuxCNC's uspace
NML/HAL uses fixed, global (not per-instance) SysV IPC keys by default, and
this repo's own cleanup scripts (`clear_stale_shm.sh`, `linuxcnc_process.py`'s
`pkill -x` by fixed process name) are global, not per-instance, so running
two LinuxCNC processes as the same Linux user needs its own instance-id
support verified working here first, before the mill's own bridge process
is built. Not solved in Phase A; flagged so it isn't a surprise in Phase B.

## Godot procedurally owns the LinuxCNC bridge process lifecycle (2026-11-14)

User-driven ask, in their own words: no manual intervention on either the
"server" (WSL/bridge) side or the game side, each time -- Godot itself
should launch LinuxCNC with the right parameters, for however many
machines eventually exist. Before this, `start.ps1` opened a **separate
visible PowerShell window** running the bridge, waited for it, launched
Godot, and told the user to close the bridge window by hand afterward.

- `MachineProfile` (`Scripts/Machines/MachineProfile.cs`) gained launch
  metadata: `WslDistro`, `StatePort`/`CommandPort`, `RateHz` -- the single
  source of truth for both the bridge-launch args and (already)
  `BridgeClient`'s own connection config, so they can't silently drift
  apart as more machines are added.
- New static `Scripts/Machines/MachineProcessController.cs`
  (`StartBackingProcess`/`StopBackingProcess`, stateless, same style as
  `WorkpiecePartBuilder`): shells out to `wsl.exe` to run
  `clear_stale_shm.sh` then launch `bridge/main.py` (fire-and-forget --
  `BridgeClient` already tolerates "not connected yet" and reconnects on
  its own), redirecting output to `bridge/bridge_session.log` instead of a
  visible window.
- `Main.cs` calls `StartBackingProcess` in `_Ready()` for the lathe's rig,
  and overrides `_Notification` to call `StopBackingProcess` for every rig
  on `NotificationWMCloseRequest` (the real close-the-window play flow)
  before quitting -- **stated limitation**: the Godot editor's own Stop
  button can force-kill the process without delivering that notification
  at all, a known Godot behavior, not fixable from here.
- `start.ps1` simplified to just launching Godot; `README.md` updated to
  match (manual bridge startup is now a debugging-only fallback, explicitly
  not to be run at the same time as Godot).

**Real bug found and fixed live, not just in theory**: a bare
`pkill -SIGTERM -f bridge/main.py` was NOT reliable enough on its own.
`wsl.exe -d <distro> -- bash -lc "..."` makes `bash` the direct child and
`python3` ITS child; `pkill -f` matches both (it matches the whole command
line, and bash's own cmdline contains the inner python invocation text
too), and killing the outer bash let WSL's own invocation teardown cut the
deeply-nested grandchild chain (python -> the `linuxcnc` wrapper ->
`linuxcncsvr`/`rtapi_app`) short mid-shutdown -- confirmed live: `main.py`'s
own clean-shutdown code ran (its log line printed) but `linuxcncsvr` and
`rtapi_app` were still alive and running minutes later regardless. Fixed by
having `StopBackingProcess` unconditionally run the same sweep
`LinuxCNCProcess._kill_stragglers`/`clear_stale_shm.sh` already use for
exactly this situation, after the SIGTERM, instead of trying to solve the
exact WSL process-teardown race. Verified clean afterward both ways: zero
matching processes, zero stale SysV IPC segments.

**Verified live**: fresh WSL state, no manual bridge start at all, launched
headless Godot -- it started its own bridge automatically, connected within
~5 seconds, zero engine errors; then the (now two-step) stop sequence left
nothing running and nothing stale behind. 36 C# tests + 13 Python tests
still green. **Not yet verified by the user**: the actual
`_Notification(NotificationWMCloseRequest)` path specifically needs a real
windowed run + closing the window, which this agent cannot do.
(Update: the user ran it and confirmed it works -- "tamam çalışıyor".)

## Scene split (workshop / machine / player) + machine-scene generator plugin (2026-11-14)

User ask, before adding anything new: stop keeping everything flat in
`Main.tscn`. Now:

- `Scenes/Machines/Lathe.tscn` -- the lathe as its own scene (BridgeClient,
  Workpiece, MachineBody, ConsoleStand, ChipSpawner, Parts), root script
  `Scripts/Machines/LatheMachine.cs`, which took over everything
  lathe-specific that used to sit in `Main.cs` (cutting/chip/part-extraction
  logic, scheduler, benchmark hook, its own backing-process start). Its
  `[Export]`s (`MachineName`/`WslDistro`/`StatePort`/`CommandPort`/`RateHz`)
  replace the old hardcoded `MachineProfile.Lathe` (removed).
- `Scenes/Player.tscn` -- the player as its own scene (no script changes).
- `Scenes/Main.tscn` is the workshop: camera, light, a plain static `Floor`
  (moved out of `MachineBody.cs`, which no longer builds it), the UI canvas,
  and instances of the two scenes above. `Main.cs` shrank to wiring the
  workshop UI to `lathe.Rig`, the status label, and the shutdown fan-out.
- `DebugOverlay.Configure` now takes the `JobScheduler` directly and
  `BenchmarkRunner` takes a plain `Node` -- neither needs `Main` anymore.
- `addons/cnc_machine_generator/` -- editor plugin (enabled in
  `project.godot`), one dock: template scene + name/distro/ports/rate ->
  writes `Scenes/Machines/<Name>.tscn`. Refuses to overwrite. Uses
  `Node.Set(name, value)`, so any machine root script exposing the same
  five exports works as a template. Uses Godot 4.7's `EditorDock`/`AddDock`
  (the older `AddControlToDock` is deprecated there).

**Found while verifying the generator, fixed**: a generated scene's
`BridgeClient` child kept its default 9200/9201 ports, so a machine
generated on other ports would start its bridge there and never hear it
(`BridgeClient` binds in its own `_Ready()`, before the machine root's).
The plugin now writes the ports onto the `BridgeClient` child too.

**Also fixed**: `StopBackingProcess`'s pkill pattern is now
`'[b]ridge/main.py'` -- in a compound command, a plain `-f bridge/main.py`
also matches (and kills) the very `bash -lc` running the sweep.

**Still open for a second live machine** (Phase B, not solved here):
`bridge/main.py` always writes the one fixed `bridge/.bridge_endpoint`
file and always deploys `linuxcnc-config/`; a second machine needs both
parametrized, and the generator can't do that from the Godot side.

**Verified by this agent**: 36 C# tests green; headless game run after the
split gives the same three part extractions as before (0.069kg/10,
0.748kg/98, 0.216kg/29), no engine errors; the generator's exact
Load/Instantiate/Set/Pack/Save sequence run headless produces a scene that
loads back with the right values; the editor starts headless with the
plugin enabled and no errors. **Not verified**: clicking the dock in a
real editor window.

---

## Earlier resume note (2026-11-14) -- superseded by the RESUME HERE at the end of this file

**Phase A done and user-confirmed live** (see the "Phase A" status section
just above for full detail): the entire bridge/sim stack generalized from
hardcoded-2-axis to N-axis/SOLID (new wire protocol, `AxisId`/`AxisVector`,
de-singletoned `BridgeClient`, `MachineRig`/`MachineProfile`), with the
existing lathe migrated through it. Verified live by this agent (headless
Godot + real bridge, zero engine errors, same masses/bucket counts as the
pre-refactor baseline) AND by the user themselves via `start.ps1`
("start.ps1 ile açıp kontrol ettim iyi", 2026-11-14) -- clear to build
Phase B on top of this. Branch `phase0-godot-bridge-client`, not pushed (no
remote configured; user's choice to skip pushing for now). 36 C# tests +
13 Python tests all green as of the last commit before this note.

**Spiked the LinuxCNC multi-instance risk (2026-11-14) -- confirmed real,
not solved yet.** Started two LinuxCNC instances concurrently under the
same WSL2 Linux user (the existing deployed lathe config + an exact copy in
a second directory, both with unmodified default NML settings). Found by
direct inspection first: `/usr/share/linuxcnc/linuxcnc.nml` (the default
NML buffer definition file, used because neither INI sets `[EMC] NML_FILE`)
hardcodes explicit numeric SysV keys per buffer (`emcCommand`=1001,
`emcStatus`=1002, `emcError`=1003, `toolCmd`=1004, `toolSts`=1005) --
identical for any config that doesn't override `NML_FILE`. Empirically
confirmed the collision this causes: started instance 1, homed it, moved it
to a known position (confirmed via its own `stat()`/`command()`), then
started instance 2 from a copy of the same config -- **instance 1's own
`linuxcncsvr` and `milltask` processes disappeared** (died) the moment
instance 2 started, even though instance 1 was never stopped. (A
superficial `stat.poll()` against the old connection still returned
plausible-looking numbers afterward, which is itself a trap -- don't take
"it didn't throw" as proof of health; check the actual process list.)

**Tried the per-instance `.nml`-file fix first (per the user's own
preference) -- it did NOT work.** Gave the copied config its own `.nml`
file with every buffer's numeric key shifted (1001-1005 -> 2001-2005) and
pointed to it via `[EMC] NML_FILE`. Instance 1's `linuxcncsvr` still died
the moment instance 2 started -- identical symptom as before. Reading the
actual failure log (`linuxcnc_bridge.log`) revealed the REAL root cause,
unrelated to NML/shared-memory keys entirely: `/usr/bin/linuxcnc` (the
system launcher script itself, not anything in this repo) checks a single,
globally fixed `LOCKFILE=/tmp/linuxcnc.lock` on every headless start, and
when it finds one already present, it assumes that's a crashed leftover
and **actively kills whatever's already running** before starting itself.
This isn't a resource collision we can configure around from the INI side
at all -- it's deliberate behavior in the installed launcher. Confirmed by
inspecting `/usr/bin/linuxcnc` directly (`grep -n LOCKFILE`).

**Why this also rules out "separate Linux user, same WSL distro"**: `/tmp`
is one filesystem shared by every user on a given WSL distro, so a second
user would hit the exact same `/tmp/linuxcnc.lock`. Only a genuinely
separate WSL distro (its own filesystem, its own `/tmp`) escapes it.

**Installed and empirically verified a second, fully separate WSL distro**
(user's choice, after seeing this): `Ubuntu-26.04`, installed to
`E:\WSL\Ubuntu-26.04-Mill` (not C: -- disk space was tight, ~9GB free, user
chose E:), `linuxcnc-uspace` installed via `apt` (same package, same
`resolute/universe` repo as the original "Ubuntu" distro -- note
**Ubuntu 24.04 does NOT carry this package**, confirmed by trying it first
and finding nothing in its repos; 26.04 does), a `gorke` user set as
`/etc/wsl.conf`'s default. **Proof, not just theory**: started a LinuxCNC
instance in the original "Ubuntu" distro, homed it, moved it to a specific
position (12.345, -6.789), then started a second instance in
`Ubuntu-26.04` concurrently, homed it, moved IT to a different position
(99.0, -88.0) -- instance 1's full process set (`linuxcncsvr`/`io`/
`halui`/`milltask`) stayed alive and untouched throughout, and reported
exactly 12.345/-6.789 right up to being cleanly stopped afterward. Full
process isolation confirmed; this is the mill's isolation strategy, settled.

**Since then, also done this session**: Godot now procedurally starts and
stops each machine's backing LinuxCNC/bridge process itself (see the
"Godot procedurally owns the LinuxCNC bridge process lifecycle" section
just above) -- `start.ps1` is now just "launch Godot," no bridge window, no
manual cleanup. Verified live by this agent (fresh state, no manual bridge
start, connects on its own within ~5s; stop sequence leaves nothing
running/stale). **Not yet verified by the user** -- specifically the
`NotificationWMCloseRequest` shutdown path needs a real windowed run
(launch via `start.ps1`, use it, close the window) that this agent cannot
do; ask for that before relying on it further.

**Next, in the order the user picked ("3 ve 1'i yapalım"), now that Phase A
exists and the isolation strategy is proven**:
1. **Phase B -- the mill itself.** A 3-axis LinuxCNC sim config (deployed
   to and run from the new `Ubuntu-26.04` distro), a heightmap-based
   material model (deliberately NOT `IMaterialModel`/`LatheRadiusProfile`
   -- see Phase A's note on why that would violate LSP), new mill
   machine-body visuals, a mill demo G-code program, a second bridge
   process (own ports, own `.bridge_endpoint`-equivalent file) talking to a
   second `MachineRig` in the same Godot scene as the lathe, started via
   the new `MachineProcessController` with its own `MachineProfile` (just
   add a second profile + a second `BridgeClient` node -- the controller
   and lifecycle hooks already support more than one). `deploy.py`/
   `linuxcnc_process.py`/`clear_stale_shm.sh` still need to become
   distro-aware internally (which `wsl.exe -d <name>` to shell out
   through) -- not done yet, next concrete step.
2. **Training/scenario mode** -- exercises, mistake detection, scoring/time
   tracking. Not started at all yet. The one piece of "operator training"
   the project's own name promises that doesn't exist yet.

**Known environment constraint, not a code gap**: this agent has no
windowed Godot session in this environment, only `--headless` + driving
LinuxCNC directly via its Python module for live smoke-testing. Every
feature this session was verified live that way (no engine errors across
real bridge traffic), but the actual *visual*/*feel* result -- and, now,
the window-close shutdown path specifically -- always needs the user's own
run via `start.ps1`. Ask for that confirmation explicitly rather than
assuming it from a clean headless run.

---
*Phase 0 environment approved implicitly by the user proceeding with the WSL fix; "do all phases, don't stop" given 2026-11-13.*

## Phase B, first slice: a live 3-axis mill next to the lathe (2026-11-14)

**Bridge side**
- `bridge/main.py --ini <file>` picks the INI inside `linuxcnc-config/`
  (default `sim_lathe.ini`); the joint->axis map for the homed mask is read
  from that INI (`[TRAJ] COORDINATES`) instead of being hardcoded lathe X/Z.
  `deploy.sync_linuxcnc_config` takes the INI name.
- New `linuxcnc-config/sim_mill.ini` (XYZ trivkins, Z homes first and parks
  at Z=50), `sim_mill.tbl` (T1 10mm, T2 6mm flat end mills, D = diameter),
  `nc_files/demo_mill_pocket.ngc` (face pass, 50x30 pocket, circular
  channel, four corner holes). Origin = centre of the stock top face.
- "Distro-aware deploy.py/linuxcnc_process.py" turned out to be unnecessary:
  those run INSIDE the distro, each distro has its own home and /tmp. Only
  the INI name needed parametrizing. Both distros share ONE network address
  (same WSL2 VM), so `bridge/.bridge_endpoint` can stay a single shared
  file, but ports must differ per machine: lathe 9200/9201, mill 9202/9203.

**Real environment bug found and fixed: state stream blocked by Kaspersky.**
At the start of this session NO state packets reached Windows at all, lathe
included (plain UDP listener on 9200 got nothing; the Windows Firewall rule
was present and enabled). This machine runs Kaspersky Security Cloud as its
firewall product, which ignores Windows Firewall rules and drops unsolicited
WSL -> Windows UDP. Replies to an outbound socket DO pass (verified both
ways with raw sockets). Fix: new SUBSCRIBE packet (type 18). BridgeClient
sends it once a second from its state socket to the bridge command port;
the bridge sends the state stream to that source address out of the command
socket. No inbound firewall rule is needed any more, for any number of
machines. Why it worked earlier the same day is unknown (not investigated).

**Godot side**
- `Scripts/Material/MillHeightmap.cs` -- height grid (0.5mm) cut by a flat
  end mill; exact swept-disc test per node (handles plunges, slots, ramps).
  Deliberately separate from `IMaterialModel` (see Phase A note on LSP).
  7 unit tests in `tests/MillHeightmapTests.cs`.
- `Scripts/Material/MillStockVisual.cs` -- chunked grid mesh (40x40 cells
  per chunk, direct ArrayMesh arrays, side walls on the outer edge).
- `Scripts/MachineVisual/MillBody.cs` -- primitive gantry mill: stock fixed,
  beam = machine Y, carriage = X, quill = Z. Local axes: X = machine X,
  Y up = machine Z, Z = -machine Y.
- `Scripts/Machines/MillMachine.cs` + `Scenes/Machines/Mill.tscn`, instanced
  in `Main.tscn` at x = +1.6m. Same five launch exports as the lathe plus
  `IniFile` (also added to `LatheMachine`), so the generator plugin can use
  either scene as a template.
- `IMachine` (Rig + StopBackingProcess): `Main` now stops every machine on
  window close. `MachineProfile.IniFile`; bridge log is now per machine
  (`bridge/bridge_session_<Name>.log`).
- `ConsoleStand`: axis readout/homed line follow the rig axis list, new
  `DemoProgram` export; registers in group `console_stands` and the player
  now interacts with whichever console is nearest.

**Verified by this agent (headless Godot + both real bridges)**: both
machines start and connect on their own; a driven slot cut removed 958 mm3
in Godot vs 957 analytic; the full demo program ran to the end (212 s at
150% feed) with 23.3 cm3 removed vs ~23.2 expected, zero engine errors, no
disconnects. 43 C# tests + 13 Python tests green.

**NOT verified -- needs the user in a real window**: how the mill, the cut
stock and chips actually look; walking to the mill console and pressing
ON / HOME / RUN there; closing the window stops both machines.

**Known gaps left on purpose**
- The 2D operator panel, debug overlay and status label are still wired to
  the lathe only; the mill is driven from its 3D console.
- `StatePublisher` sends `stat.feedrate` (the override scale, ~1.0) as
  `feed_rate`; lathe chip classification uses it. The mill derives feed
  from confirmed velocity instead. Not changed for the lathe -- it would
  alter user-confirmed chip behaviour; decide with the user.
- `ConsoleStand._Ready` sets `Position = BasePosition`, so moving the node
  in the editor has no effect at runtime (the lathe scene has such a move).
- G-code preview is still X/Z only. No tool-length offsets on the mill.
- The mill tool change completed without a T.CHG confirm in the driven run;
  not investigated.

---

## RESUME HERE (2026-11-14, after Phase B first slice)

Branch `phase0-godot-bridge-client`, no remote. Read the Phase B section
just above. Next:
1. Ask the user to run `start.ps1` and confirm the mill visually (see the
   NOT verified list above), then fix whatever looks wrong.
2. Decide with the user: point the 2D panel / overlay at the nearest
   machine, and whether to fix the lathe `feed_rate` field.
3. Training/scenario mode (exercises, mistake detection, scoring) -- still
   not started.

### Follow-up, same day (user asks): generator plugin, per-machine 2D panel, editable console
- **2D operator panel**: MACHINE selector at the top (`OperatorPanel.SetMachines`),
  rows rebuild per machine (axes, default program, touch-off: lathe X/Z, mill
  Z only). The debug overlay and status label follow the selection. This
  closes the "2D panel is lathe-only" gap above.
- **ConsoleStand**: `BasePosition` removed -- the node transform in the scene
  is now what counts, so it can be moved/rotated in the editor. `PostHeight`
  and `PanelTiltDegrees` rebuild the geometry live when edited. The demo
  program moved to the machine root (`DemoProgram` export -> `MachineProfile`).
  NOTE: the lathe console now really sits where the scene says (x=-0.824);
  before, the runtime silently forced it back to x=-0.15.
- **Generator plugin**: template dropdown lists `Scenes/Machines/*.tscn`,
  distro/rate prefilled from the template, free port pair suggested, name and
  port-collision validation, same-distro warning, optional "Add to the open
  scene" (placed 1.6 m right of the last machine). INI/demo program come from
  the template.
- Verified by the agent: build + 43 tests, headless editor loads the plugin
  with no errors, headless game connects both machines with no errors.
  NOT verified (needs a real window): the dock showing up and a full
  Generate click, switching machines in the 2D panel, console placement.

---

## Spec-driven machine generator (2026-11-14, branch `machine-spec-generator`)

User ask: the generator plugin only copied a template scene; it must let
someone describe a real machine in full (axes, travels, tools, sizes,
physical build) and get the scene procedurally. Decisions taken with the
user: full cutting on 4/5-axis machines, the hand-written `Lathe.tscn` /
`Mill.tscn` and their body code stay untouched (generated machines are a
separate code path), editing happens in a dock + the Godot Inspector.

- `Scripts/Machines/Spec/`: `MachineSpec` (+ `AxisSpec`, `ToolSpec`,
  `MillStructure`, `LatheStructure`) are `[GlobalClass]` Resources, the single
  source of truth for one machine. `MachinePresets` has nine starting points
  (benchtop mill/lathe that run the two demo programs, C-frame VMC, gantry
  router, 4-axis indexer, 5-axis trunnion, 5-axis swivel head, flat-bed and
  slant-bed lathes). `MachineSpec.Validate()` lists what would stop it working.
- `LinuxCncConfigWriter`: spec -> `linuxcnc-config/gen_<Name>.ini/.tbl`
  (gitignored), rewritten at every machine start so they cannot drift from
  the spec. Rotary axes become ANGULAR joints under trivkins; joints are in
  X Y Z A B C order whatever the chain order is. No bridge change was needed.
- `MachineKinematics`: chain order = axis list order; each axis is carried
  by the tool side or the workpiece side. Drives the body nodes and gives the
  tool pose in the stock frame for cutting.
- `VoxelStock` + `VoxelMesher` + `VoxelStockVisual`: signed-distance voxel
  stock cut by a cutter in any orientation (flat / bull / ball end mill,
  drill, chamfer), chunked surface-nets mesh. Every generated mill uses it,
  3-axis ones included; `Mill.tscn` keeps its heightmap. Surface nets bevel
  sharp edges by about one voxel step (visual only, volumes come from the
  field).
- `ProceduralMillBody` / `ProceduralLatheBody` / `ToolMeshBuilder`: bodies
  built from the spec (C-frame or gantry, tables/saddles, trunnion, indexer,
  platter, swivel-head fork, magazine, enclosure; flat or slant bed, jaw
  chuck, disc turret or tool post, tailstock).
- `ProceduralMachine` (`[Tool]`, `IMachine`): the scene stores only this
  node, its spec and a `ConsoleStand`; the rest is rebuilt from the spec, in
  the editor too (edit the spec in the Inspector, the machine reshapes). Its
  origin is ON THE FLOOR (place at y = `WorldScale.FloorY`), unlike the two
  hand-written machines. Runtime logic is in `MillCutting` / `LatheCutting`.
- `MachineProcessController` refuses to start a second machine in a WSL
  distro that already runs one (it would kill the first, see item 15 above);
  `Main` skips machines without a rig.
- Plugin dock: preset list (built-in + `res://Machines/Specs/*.tres`), live
  3D preview, embedded Inspector, problem list, "Save as preset", Generate.

**Verified by the agent, live against LinuxCNC**: generated benchtop mill ran
`demo_mill_pocket.ngc` to the end, 23.17 cm3 removed (heightmap mill: 23.3,
analytic ~23.2); generated benchtop lathe ran `demo_facing_turning.ngc`,
parts 0.069 kg and 0.576 kg; generated 5-axis trunnion started, homed, and an
MDI sequence removed 393 mm3 on the top face, +394 on a side face at A90,
+997 for a 20 mm slot there (analytic 393 / 393 / 1000). Workshop run with
the hand-written lathe + a generated mill connected both and refused a third
machine on a taken distro; the original `Main.tscn` still connects both
machines. 56 C# tests green. Bodies were checked on screenshots from a
windowed run (so a windowed Godot DOES work from the agent session).

**NOT verified (needs the user in the editor)**: the dock itself (preview
orbit, embedded Inspector editing arrays of axes/tools, Generate button,
live rebuild when a placed machine's spec is edited); chips and the console
on a generated machine in a real play session.

**Left out on purpose**: mill-turn lathes (C/Y axis), RTCP/TCP kinematics,
tool-length offsets (the tool tip is always at the reported position, a
longer tool lifts the head), collision of holder/head with the part, G-code
preview beyond X/Z. Pytest bridge suite not re-run (no bridge file changed).

## Tool change confirm fixed (2026-11-15, branch `demo-programs`)

User report: "Confirm tool change" does not work. Root cause, found on a live
instance: `basic_sim.tcl -no_use_hal_manualtoolchange` does NOT leave
`iocontrol.0.tool-changed` unwired (the old comment was wrong) -- it nets
`tool-change` straight back into `tool-changed`. So every M6 completed on its
own, and the confirm command's `halcmd setp` failed every time with "pin is
connected to a signal". This also explains the older open note "the mill tool
change completed without a T.CHG confirm".

- `bridge/main.py` unlinks `iocontrol.0.tool-changed` once LinuxCNC is up, so
  M6 now WAITS for the operator (program and MDI alike).
- `CommandServer._confirm_tool_change` holds `tool-changed` until iocontrol
  drops `tool-change` (iocontrol samples every 100 ms; a quick pulse can be
  missed) and does nothing when no change is pending.
- LinuxCNC reports INTERP_READING during the wait, so the panels' "TOOL
  CHANGE PENDING" text (keyed on `InterpState.Waiting`) never showed. The
  state publisher now reports WAITING while the `tool-change` pin is TRUE.
  No wire-format change.

Verified live through the real UDP command path: M6 in a program and in MDI
stays pending (tool unchanged after 4 s), a confirm completes it, an early
confirm does not pre-confirm, the state stream shows WAITING only while
pending. 14 pytest green. NOT verified: the buttons in a real game window.

Also on this branch, NOT yet run against LinuxCNC (the run was stopped by the
host for low memory after ~60 s): eleven demo programs in
`linuxcnc-config/nc_files/` and the nc_files re-sync on LOAD PROGRAM. The
throwaway live harness is in the untracked `tmp_verify/`.

### Follow-up, same day: pending indicator was wrong (user report on the 5-axis machine)

"TOOL CHANGE PENDING" lit up while the machine was cutting. Cause: the panels
keyed it on `InterpState.Waiting`, which LinuxCNC also reports during ordinary
program motion (interpreter waiting on the motion queue) -- so the previous
note's "report WAITING while pending" was the wrong carrier. Replaced by a
dedicated `tool_change_pending` u8 appended to the STATE packet (160 bytes
now, both sides + fixture updated). The user's own 5-axis program had no
`T1 M6`, so LinuxCNC ran it with an empty spindle (T0: no tool drawn, nothing
cut) -- added the line. Found by inspecting the user's RUNNING instance
read-only; nothing was restarted. 56 C# + 11 non-LinuxCNC pytest green.
NOT verified live (both distros were in use by the user's session): the new
flag end to end, and the eleven demo programs.

## Statue of Liberty program for Trunnion5Axis (2026-11-15)

`tools/statue_cam.py` generates `nc_files/trunnion5_ozgurluk_heykeli.ngc`
(38.7k lines, G0/G1 only): the statue is a set of simple solids, every pass is
3+2 at A90 following the height map seen from that side (rough: T1 12 mm flat,
4 sides, 15 mm levels, 1 mm allowance; finish: T2 4 mm ball, 6 sides, 1.5 mm
rows; plus one pass from above). `Trunnion5Axis.tscn` now carries what it
needs: 80 x 80 x 170 block (top 140 carved, bottom 30 left for clamping),
A pivot 120 mm below the top face, Z min -200, T1/T2 as above, tool change
position Z300. Verified OFFLINE: the whole program run through the game's own
`MachineKinematics` + `VoxelStock` (no limit hits, no rapid into material,
760 cm3 removed, 112 m of feed moves) and looked at on screenshots; parsed to
PROGRAM_END by LinuxCNC's standalone `rs274`. NOT run on a live LinuxCNC
instance or seen in the game. No holder/spindle collision check exists.

### "Cannot home / cycle start" on the 5-axis machine (2026-11-15)

Not the G-code and not the machine: the same config homed, ran the statue
program and changed tools through (a) raw UDP commands, (b) the real
`Trunnion5Axis.tscn` driven via `BridgeClient`, (c) the real `Main.tscn` with
the real `OperatorPanel` buttons pressed programmatically. Most likely cause:
the 2D panel acts on the machine picked in its MACHINE selector, which starts
on the first machine (Lathe), and pressing E at a console only toggled the
mouse -- so at the 5-axis console the panel was still driving the lathe.
Fixed: E at a console now selects that console's machine in the panel
(`ConsoleStand.Rig`, `OperatorPanel.SelectMachine`). The user's actual
failing session could not be reproduced, so this is the probable cause, not a
proven one. Side finding, not touched: `BridgeClient.LastCommandRttMs` stays
NaN in these runs (ACKs apparently never matched).

## Hands-on setup, phases 1-2 (2026-11-15, branch `workholding-setup`)

Approved plan: `~/.claude/plans/vectorized-stargazing-dragonfly.md` (stock
station, bolt-by-bolt workholding, real zeroing, consequences; 5 phases).

**Phase 1 -- interaction + stock station.** `Scripts/Interaction/`
(`IInteractable`: Prompt / Use = left click / Turn = mouse wheel;
`Carryable`; `HandTool` with nine tool kinds). `PlayerController`: crosshair
ray finds the nearest `IInteractable` ancestor, F picks up/drops, E opens
the station panel, `Notify` shows a warning, `Give`/`ReleaseHeld`.
`Scripts/Workshop/`: `StockBlank` (shape/size/material -> volume, mass),
`StockItem` (carryable stock), `StockStation` (HUD panel + preview monitor
from one SubViewport, "ÜRET" spawns the stock on the tray), `ToolBench`.
Both are in `Main.tscn`; the floor is 12 x 10 m now.

**Phase 2 -- mill vise.** `Scripts/Fixtures/` (the namespace cannot be
`Workholding`: that is an enum in `CncTrainer.Spec`): `Vise` (carry to the
table, 4 `Fastener` holes over the nearest T-slots, parallels, stock placed
where the player aims along the jaws, screw via wheel with the vise handle,
mallet seating), `BoltItem`, `MillTable`, `MillWorkholding` (fixture cart
next to the machine; turns the stock in the jaws into the `VoxelStock` being
cut AT ITS REAL POSITION; throws the part and switches the machine off if it
is cut while the vise or its bolts are not tight; "ÇARPMA" when the cutter
enters a jaw). `MachineSpec.ManualSetup` turns this on (vise mills only so
far); off = old behaviour, stock pre-placed at machine zero. `MillCutting`
no longer owns a fixed stock (`MountStock` / `ClearStock`). New scene
`Scenes/Machines/BenchMill.tscn` (manual setup, distro Ubuntu-26.04, ports
9206/9207) -- NOT placed in `Main.tscn`: both distros are taken there.

Verified live on LinuxCNC with a scripted setup (vise at X+30, stock at
+10 along the jaws, G54 set with `G10 L2`): correct setup ->
`demo_mill_pocket` removes 23.07 cm3 with no alarm; vise torque 1/3 -> part
thrown after 20 mm3, machine off; no parallels -> jaw crash alarm on the
first approach. 58 C# tests green. Looked at on screenshots: station,
panel, bench, vise on the table.

NOT verified (needs the user playing): every keyboard/mouse path -- picking
up, carrying, placing by aiming, wheel-turning bolts and the screw, the
prompts. These were only exercised through the methods they call.

Open: phases 3 (work-offset packet, WORK/MACHINE readout, edge finder,
paper Z touch, SET buttons, setup sheet), 4 (lathe chuck), 5 (clamps,
centring plate for the trunnion, 4th-axis chuck, vise tramming).

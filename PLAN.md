# CNC Operator Training Simulator — status

Current state, open work and the things not worth re-discovering. The dated
history of how it got here is in git (`git log`, and `PLAN.md` before
2026-11-17 holds the full development log).

## Current state (2026-11-17)

Branch `cutting-realism` (pushed to `origin`; stacked on
`hands-on-lathe-and-fixtures` -> `single-distro-namespaces` -> ...; nothing
merged, no PR). 80 C# tests, 12 pytest (+2 LinuxCNC lifecycle tests).

Done, each verified through scripted operators on real LinuxCNC:
- Bridge (Python in WSL) <-> Godot C# client, prediction, N-axis protocol,
  Godot starts/stops every machine's LinuxCNC itself.
- Machine generator plugin (`MachineSpec` -> scene + `gen_<Name>.ini/.tbl`),
  voxel stock for any tool orientation, procedural mill/lathe bodies.
- Hands-on setup: stock station, vise / step clamps / centring plate /
  indexer chuck / lathe chuck, tramming, edge finder, paper Z, setup sheets.
- Realism: shared foundation (`MachineSafety`, `CuttingProcess`, `StockSeat`,
  `Tram`, `BoltedFixture`, `MachineSetup`), cutting load + Taylor wear
  (`CutMonitor`), physical tools (`ToolItem`, `ToolSocket`, tool cart, length
  gauge, `G10 L10` + `G43`), collisions of holder/spindle/shank/rapids,
  measuring (`IMeasurable`: caliper, micrometer, depth gauge), door
  interlock, coolant, a crash unhomes the machine.

`Main.tscn`: BenchLathe (9200/9201), BenchMill (9206/9207), Trunnion5Axis,
FlatBedLathe (9208/9209, uncommitted), tool carts, fixture carts, stock
station, tool bench. A session starts with EMPTY spindles/turrets and an OPEN
door on enclosed machines.

## Open

1. **User play-test** of every keyboard/mouse path. The agent has only
   called the methods behind them: carrying, placing by aim, wheel-turning
   bolts/screw, loading tools from the cart, length gauge, gauge readings
   under the crosshair, door, COOLANT buttons, prompts.
2. **User decision**: delete the legacy hand-written path (`LatheMachine`,
   `MillMachine`, `MachineBody`, `MillBody`, `MillHeightmap`,
   `MillStockVisual`, `Lathe.tscn`, `Mill.tscn`)? No scene in `Main.tscn`
   uses it; nothing new is built on it.
3. **Training / scenario mode** (work order, drawing with tolerances, report
   card) — not started. Inputs exist: `IMeasurable`,
   `MachineSafety.Incidents`, `CutMonitor` (peak load, chatter, wear).
4. Not modelled: turret / tailstock / enclosure collisions, mill head vs.
   vise body sideways, guards on open bench machines, sound, tool magazine
   logic (every mill tool change is by hand), lathe boring/drilling, RTCP,
   G-code preview beyond X/Z, persistence of cut state across a restart.
5. The eleven demo programs in `linuxcnc-config/nc_files/` have never all
   run end to end live (the statue program only offline + `rs274`).
6. Side finding, not investigated: `BridgeClient.LastCommandRttMs` stays NaN.

## Design decisions that are easy to undo by accident

- LinuxCNC is the only authority. Material is removed only from CONFIRMED
  samples; prediction is visual only.
- Each machine: own LinuxCNC in distro `Ubuntu`, isolated by
  `bridge/run_isolated.sh` (private mount/`/tmp`, IPC, PID namespace,
  per-run `RTAPI_FIFO_PATH`). Each machine needs its own port pair; all
  share one WSL address.
- `ProceduralMachine` origin is ON THE FLOOR (y = `WorldScale.FloorY`); the
  legacy hand-written machines are not.
- Tool error belongs to the physical `ToolItem`, not to the tool number.
  Tool lengths are entered as the DIFFERENCE from nominal (machine Z still
  means "tip of a nominal tool").
- Every mill program needs `G43` after each `Tn M6`; without it LinuxCNC
  ignores tool length offsets.
- Mill/lathe cutting do NOT share `IMaterialModel` (lathe 1D radius profile
  vs. voxel field; forcing one interface broke LSP).
- STATE packet is 210 bytes, hand-mirrored in `bridge/protocol.py` and
  `Scripts/Bridge/BridgeProtocol.cs`; change both + the cross-language
  fixture + the size test together.
- Shortened on purpose: tool life 20 min at the recommended speed; chips
  drawn 6x real size (`ChipSpawner.VisualSizeMultiplier`).

## Non-obvious environment facts (already handled in code — don't undo)

- LinuxCNC launcher pops a blocking Tk dialog unless `DISPLAY` /
  `WAYLAND_DISPLAY` are stripped (`linuxcnc_process.py`).
- Stale SysV IPC after a crash wedges the next start for 90+ s:
  `clear_stale_shm.sh` runs before every start.
- The first `linuxcnc.stat()` made before NML exists wedges for the whole
  process: wait for the log marker, then for `axis_mask != 0`.
- `stat.poll()` can return plausible numbers from a dead instance — check the
  process list, not "it didn't throw".
- drvfs (`/mnt/c`) has no hard links/FIFOs: `deploy.py` copies the config to
  a native WSL path before every run.
- `/usr/bin/linuxcnc` kills any instance holding `/tmp/linuxcnc.lock` —
  that is why machines run in separate mount namespaces.
- Kaspersky Security Cloud is the firewall and drops unsolicited WSL ->
  Windows UDP (Windows Firewall rules are ignored). The state stream is a
  reply to Godot's SUBSCRIBE (type 18); any new network path must be
  reply-shaped.
- WSL2 `127.0.0.1` UDP forwarding does not work here: the bridge writes its
  address to `bridge/.bridge_endpoint`.
- `basic_sim.tcl -no_use_hal_manualtoolchange` nets `tool-change` straight
  into `tool-changed`; the bridge unlinks it so M6 waits for CONFIRM, and the
  confirm holds the pin until iocontrol drops `tool-change`.
- LinuxCNC reports `INTERP_WAITING` during ordinary motion too; tool-change
  pending has its own `tool_change_pending` byte.
- After homing LinuxCNC is in TELEOP and rejects joint jogs: jog the axis in
  teleop, the joint before homing. Jog direction comes from the velocity
  sign only. Jogging needs MANUAL mode.
- `gcode.parse()` preview reports inches when the program has `G21`:
  compensated with x25.4 in `preview.py` (root cause unknown).
- `StopBackingProcess` pkill pattern is `'[b]ridge/main.py'` (a plain
  pattern also kills the sweeping bash); always run the straggler sweep
  after SIGTERM.
- The Godot editor's Stop button kills the game without
  `NotificationWMCloseRequest`; only closing the window stops machines
  cleanly.
- C# namespace traps: `CncTrainer.Panel` / `CncTrainer.Material` shadow
  `Godot.Panel` / `Godot.Material` (qualify them); fixtures namespace cannot
  be `Workholding` (an enum in `CncTrainer.Spec`).
- Mouse look uses `_Input`, not `_UnhandledInput` (GUI eats motion).
- Jolt convex shapes for thin parts need `Margin = 0.001`.
- Fixture "stock moved" checks must be relative to the table, not global
  (moving tables re-mounted the stock every frame).
- Repo folder is lowercase `tests/` (WSL is case-sensitive).
- Full program runs find bugs MDI moves don't; a path symmetric to a working
  one is not proof — test each direction live.

## Working on this machine

- Godot: `E:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`.
  Windowed runs work from the agent session (save viewport PNGs and read
  them). Kill Godot by its literal PID; pattern kills get blocked and
  `--quit-after` is unreliable.
- The user runs the game embedded in the editor at 1920x1080; the bottom
  ~70 px are off-screen — keep HUD away from the bottom edge.
- Before any live test: `pgrep -x linuxcncsvr` in `Ubuntu`. The user is often
  playing; a test on a used port kills their session. Read-only inspection
  (`linuxcnc.stat()`, `halcmd show`) is safe.
- Build: `dotnet build Linux.csproj`. Tests: `dotnet test tests/Tests.csproj`;
  `wsl -d Ubuntu -- bash -lc "python3 -m pytest /mnt/c/Users/gorke/Documents/linux/tests/bridge/ -v"`
  (the LinuxCNC lifecycle tests stop a running game).
- Harnesses (untracked `tmp_verify/`, ports 9210/9211):
  `run_scene.ps1 -Scene tmp_fix -Scenarios a,b`. `TmpLathe` (ok, keyin,
  loose, stall, shank, rapid), `TmpFix` (tram, clamps, clampsloose,
  clampscrash, plate, door, chuck, chuckkey, snap, nospindle, wear, wearcool,
  toollen, wrongtool, empty, rapid, holder, table, vol), `TmpLook` (windowed
  screenshots). `look.ps1 -Scenario main` uses the REAL ports and takes over
  an open game. A harness killed on timeout leaves its Godot child running.

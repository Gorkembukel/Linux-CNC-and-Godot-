# CNC Operator Training Simulator — status

Current state, open work and the things not worth re-discovering. The dated
history of how it got here is in git (`git log`, and `PLAN.md` before
2026-11-17 holds the full development log).

## Current state (2026-11-17)

`main` holds everything up to PR #4 (operator view). Branch
`machine-limits-collisions` adds collisions, lathe drilling/boring and the
machine settings below (PR into `main`, merge is the user's call).
87 C# tests, 11 pytest (+2 LinuxCNC lifecycle tests, + preview test).

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
- Console phase 1 (branch `fanuc-style-console`): `ConsoleStand` rebuilt as a
  0i-F Plus style pendant (layout measured from a reference photo, generic
  brand text): LCD (`ConsoleScreen`) with softkeys and pages POS / PROG /
  OFS/SET / MESSAGE / SYSTEM / CSTM/GR / HELP, MDI keypad with key-in line and
  MDI blocks, mode keys (EDIT/AUTO/MDI/HANDLE/JOG/INC/REF), hold-to-jog,
  INC steps, handwheel + axis knob, feed/spindle override dials
  (`ConsoleKnob`, mouse wheel), spindle/coolant/tool-change keys, lamps.
  Verified live key by key (`tmp_verify/TmpConsole`, scenarios default and
  phase2). Bridge: PROGRAM_RUN resumes when paused.
- Console phase 2: PROG shows the program (executing line marked), DIR +
  SELECT picks any `nc_files` program, EDIT inserts / alters / deletes lines
  (AUTO then runs `nc_files/_edit_<name>`, gitignored; the original is not
  touched), OFS/SET TOOL and WORK views with MEASURE / INPUT. Offset MDI lines
  are shared with the 2D panel (`OffsetCommands`).
- Player: right-mouse zoom, crouch (Ctrl / C), lean (Q / R), Alt head offset
  that stops short of walls, and Tab close-up on a console (`ConsoleFocus`:
  the panel fills the screen, the wheel dollies toward the panel point under
  the cursor). Consoles stand 15 cm higher (PostHeight 1.1).
- Tool belt (`ToolBelt`, child of the player body): 8 holsters in a 120 deg
  arc at the front of the waist; F while looking at one takes / stows /
  swaps a `HandTool` (stock and fixtures stay carried). Holsters and stowed
  tools are in `Fastener.InteractionLayer`. Looking down past -0.9 rad the
  mouse turns only the head (up to 1.2 rad); looking up again the body takes
  over that turn. Harness: `tmp_verify/TmpBelt`.
- Console immersion pass: BLOCK SKIP, OPT STOP, rapid override (new bridge
  commands 20-23, also COOLANT_FLOOD so coolant works mid-program), PRG
  RESTART (run from the PROG cursor line), WORK LIGHT, held jog key stays
  down, GRAPH backplot on CSTM/GR, Fanuc-style alarm codes. Synthesised
  sound (`Scripts/Audio`): key click, alarm beep, spindle motor, cutting,
  servos, coolant. The 2D panel and status text are debug-only (F2); E at a
  console opens the close-up.
- Operator view (PR #4): within 2.5 m of a console a live picture of the real
  3D panel sits in the bottom-right corner (`ConsolePip`, clickable with the
  cursor free); arrows / PgUp / PgDn press the jog key in the same spot of the
  jog block (held = jog), Enter = CYCLE START, Space = FEED HOLD, -/= = feed
  override; keyboard presses glow on the panel.
- Collisions: lathe body as Z stretches with a reach from the centreline
  (`ProceduralLatheBody.Interference`): tool / turret / tool post vs chuck,
  headstock, tailstock, enclosure, and the turret itself in the bar. Mill:
  holder and spindle nose vs fixtures and table, vise base/ears/screw.
- Part orientation: Z / X turn the carried object a quarter turn (relative
  to the way the player faces); fixtures take it as held, squared to the
  nearest of the 24 orientations (`SquareTurn`, `StockSeat.Turn`). Machined
  stock stays a `StockItem` with its voxel field, so it goes back into any
  fixture turned over. A part off the lathe is a `StockItem` too, carrying
  its outside / bore profile (`Turned`): it goes back into a chuck either end
  first, and into a mill fixture, where its voxel field is built from the
  profile (`VoxelStock.Revolved`).
- Real tools: `ToolSpec.Geometry` (`ToolGeometry`) gives a lathe tool its
  insert (ISO C80 / D55 / V35 / T60 / S90 / W80, round, grooving blade, 60
  deg threading, or a drawn outline) and a mill tool a form profile.
  `ToolProfile` / `InsertOutline` (Scripts/Material) build the outline with
  the controlled point at its bounding corner; `LatheRadiusProfile.SweepProfile`
  / `SweepProfileBore` cut with it (threading repeats the form every pitch
  from where the pass started); `CutterShape.Form` cuts a mill form profile.
  Meshes: inserts in holders, fluted end mills / drills, face mill inserts,
  revolved form cutters (`RealToolMeshes`). Catalogue: `ToolLibrary`; the
  lathe presets use it. Editor: "Tool Designer" dock (same addon) to start
  from a catalogue / saved tool, draw a custom insert or form profile on a
  2D canvas, preview in 3D, save to `Machines/Tools`, add to the Machine
  Generator draft. Tools without Geometry cut as before.
- Middle-drag turns the carried object freely (fixtures still square it).
  A block goes into a lathe chuck in any orientation: the profile starts at
  the corner radius and keeps the block's half sizes (`LatheSection`), so
  the section is circle-cut-by-rectangle: mesh, chip volume, depth of cut,
  caliper, parted/removed parts and their mill voxel field all follow it,
  and the workpiece turns with the chuck.
- Lathe drilling and boring: `LatheRadiusProfile` keeps a bore radius per
  bucket (`SweepBore`); mesh, finished parts, mass and caliper (bore vs
  outside) know it. Drill deeper than its flutes and a boring bar in a hole
  too small for it crash.
- Lathe resolution and threads: a chucked bar's profile has a 0.1 mm step
  (`LatheWorkholding.ProfileStepMm`; was 0.5, which aliased fine pitches
  away: P1 came out 0.2 deep). A threading pass records its pitch per bucket;
  `WorkpieceVisual` draws those rings as a helix (each vertex takes a whole
  bucket's radius and is set back along the axis by the rest of the
  advance). Meshes are built in `MeshBuffer` (managed, normals computed
  there), 4 mm chunks, each built with one extra section per side for its
  normals (no shading seam between chunks). One cut's rebuild ~3 ms (Debug).
  A thread pass keeps its phase while it slows down and while the tool backs
  out of the groove (that retract used to cut a slot through the crests and
  raise "G0 into material"). Live: `TmpLathe` thread (G76 P2 K1.23 -> 1.224
  deep); offline renders: `tmp_verify/TmpThread`.
- Face cuts: a tool removes only the ring its outline sweeps per bucket
  (`CutSpan`): reaching the outside lowers it, reaching the bore / axis opens
  the bore. An OD tool plunged at the face centre opens only what its insert
  covers and its holder crashes on the rest of the face (realistic); a face
  recess leaving a rim is drill + boring bar outward. A ring wholly inside
  the wall (face groove) cannot be held by two radii: it opens toward the
  side that loses less. Live: `TmpLathe` facein / faceout / recessbar.
- Machine settings in `MachineSpec` (plugin inspector): spindle min/max rpm
  (+ chuck max rpm, INI `[SPINDLE_0]`), jog top speed (console dial scales),
  flood coolant yes/no, door interlock on/off, thermal growth, per axis home
  search/latch velocity (sign = direction), mechanical backlash and its
  compensation (INI `BACKLASH`; the rest is play in `MachineErrors`), wrapped
  rotary; lathe swing, max turning length, G7/G8 startup, tailstock Z; mill
  magazine (automatic change after `ToolChangeSeconds`, tool N in pocket N,
  random changer flag for LinuxCNC), max tool diameter/length, table load.
  `Validate` checks them; the plugin warns about axis letters the machine
  lacks and S above the max rpm in the demo program.

`Main.tscn`: BenchLathe (9200/9201), BenchMill (9206/9207), Trunnion5Axis,
FlatBedLathe (9208/9209, uncommitted), tool carts, fixture carts, stock
station, tool bench. A session starts with EMPTY spindles/turrets and an OPEN
door on enclosed machines.

## Open

1. **User play-test** of every keyboard/mouse path. The agent has only
   called the methods behind them: carrying, placing by aim, wheel-turning
   bolts/screw, loading tools from the cart, length gauge, gauge readings
   under the crosshair, door, COOLANT buttons, prompts, the new console
   (hold-to-jog, dials) and the new posture / zoom / console close-up.
2. **User decision**: delete the legacy hand-written path (`LatheMachine`,
   `MillMachine`, `MachineBody`, `MillBody`, `MillHeightmap`,
   `MillStockVisual`, `Lathe.tscn`, `Mill.tscn`)? No scene in `Main.tscn`
   uses it; nothing new is built on it.
3. **Training / scenario mode** (work order, drawing with tolerances, report
   card) — not started. Inputs exist: `IMeasurable`,
   `MachineSafety.Incidents`, `CutMonitor` (peak load, chatter, wear).
4. Not modelled: guards on open bench machines, RTCP, G-code preview beyond
   X/Z, persistence of cut state across a restart, tool deflection, a
   locking rotary brake (LinuxCNC `LOCKING_INDEXER` needs an unlock handshake
   basic_sim does not wire), the random changer moving tools between pockets
   on our side (LinuxCNC keeps its own pocket bookkeeping). The requested tool
   is not in the STATE packet, so an empty magazine pocket is only reported
   after the change.
5. The eleven demo programs in `linuxcnc-config/nc_files/` have never all
   run end to end live (the statue program only offline + `rs274`).
6. Side finding, not investigated: `BridgeClient.LastCommandRttMs` stays NaN.
7. Console: DRY RUN / machine lock and the conveyor / air keys were removed
   (blank caps); F1-F7 are unassigned builder keys. PRG RESTART uses
   LinuxCNC run-from-line, so tool / spindle set before that line are not
   replayed.
8. Console bug: FEED HOLD in MDI mode cannot be resumed. `CycleStart` only
   resumes a paused interpreter in AUTO; in MDI it re-sends the typed blocks
   (nothing, if none are pending). It should resume when paused, as in AUTO.

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

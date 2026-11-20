# CNC Operator Training Simulator — status

Current state, open work and the things not worth re-discovering. The dated
history of how it got here is in git (`git log`, and `PLAN.md` before
2026-11-17 holds the full development log).

## Current state (2026-11-20)

`main` holds everything through the coverage-analysis roadmap and its
follow-ups (PRs #22-#30). 168 C# tests, bridge pytest 17. The scenes
`Scenes/Workshop/JobBoard.tscn`, `InspectionTable.tscn` and
`ChuckCabinet.tscn` exist; the cabinet is placed in `Main.tscn`, the board
and the table are not yet.

Done, each verified through scripted operators on real LinuxCNC:
- Bridge (Python in WSL) <-> Godot C# client, prediction, N-axis protocol,
  Godot starts/stops every machine's LinuxCNC itself.
- Machine generator plugin (`MachineSpec` -> scene + `gen_<Name>.ini/.tbl`),
  voxel stock for any tool orientation, procedural mill/lathe bodies.
- Hands-on setup: stock station, vise / step clamps / centring plate /
  indexer chuck / lathe chuck, tramming, edge finder, paper Z, setup sheets.
- Realism foundation (`MachineSafety`, `CuttingProcess`, `StockSeat`,
  `Tram`, `BoltedFixture`, `MachineSetup`), cutting load + Taylor wear
  (`CutMonitor`), physical tools (`ToolItem`, `ToolSocket`, tool cart, length
  gauge, `G10 L10` + `G43`), collisions of holder/spindle/shank/rapids,
  measuring (`IMeasurable`: caliper, micrometer, depth gauge), door
  interlock, coolant, a crash unhomes the machine.
- Tool length: mill tool lengths from the spindle gauge line
  (`ToolLengthModel`, `MillStructure.ToolLengthReference`): forgetting G43
  dives a whole tool length. OFS/SET TOOL is a Fanuc-style table with
  GEOM/WEAR (`ToolOffsetTable`, wear kept Godot-side; LinuxCNC holds the sum).
- Console (0i-F Plus style `ConsoleStand`): pages POS / PROG / OFS/SET /
  MESSAGE / SYSTEM / CSTM/GR / HELP, MDI, EDIT (copies to `_edit_<name>`),
  mode keys, hold-to-jog, handwheel, override dials, BLOCK SKIP, OPT STOP,
  rapid override, PRG RESTART, WORK LIGHT, MC LOCK (`SetMachineLock`; release
  after the control moved unhomes). GRAPH draws the opened program
  (`preview.py` parses it with LinuxCNC's interpreter, PREVIEW chunks) and
  checks it against the setup (`Machining.ProgramCheck` over `IPathObstacle`:
  `MillWorkholding` fixture/table vs tool, holder, nose; `LatheCutting`
  chuck, tailstock, guarding, turret vs bar): first hit = red cross + CRASH
  line. FEED HOLD resumes with CYCLE START in AUTO and MDI. Operator view:
  `ConsolePip` corner window, keyboard jog/start/hold near a console; K
  turns that keyboard control and the window off / on. Synthesised sound
  (`Scripts/Audio/SynthSounds`).
- Player: zoom (right mouse), crouch, lean (Q / R), Alt head offset, Tab /
  E close-up on a console, tool belt (8 holsters, F), hacksaw (`SawAction`),
  workshop hand tools (glasses, chip hook, file / deburrer, dial indicator,
  mallet, pitch gauge, M20x2 ring gauge, Z setting block, cam key).
- Carrying (`PlayerController`): F holds a thing where it lies, Shift+F or B
  brings it to the hands (lifted at 2 m/s); wheel = hold distance
  (0.25-2 m, Shift: 1 cm); T or middle button + mouse turns it (Shift:
  fine); Z / X quarter turns. Physical handling (`WorkshopSettings`,
  Project Settings `cnc_trainer/workshop/physical_handling`,
  `hand_strength_n`, `weight_feel`): carried things other than hand tools
  stay dynamic, pulled by a force- and torque-limited spring; their weight
  is always held up, `weight_feel` of their mass is felt as swing; walking
  slows a little. Hand tools turned with the wheel spin about
  `HandTool.TurnAxis`; keys in a chuck wind with each notch.
- Scenario mode (`Scripts/Inspection`): `WorkOrder` / `PartFeature`
  resources (`Machines/WorkOrders/*.tres`), `JobBoard`, `InspectionTable`
  (report card: sizes, material, Ra, incidents, score, root-cause hint),
  `PartInspector` over `TurnedGeometry` / `VoxelGeometry` / `WithSurface`.
  Drawings: SVG geometry + `DrawingNotes` text (Godot's SVG import drops
  text and markers) drawn by `Player.DrawingView`.
- Realism pass: tool stick-out set at the length gauge, slender bar chatter
  (`CuttingLoad.SlenderWorkRatio`) with its own sound, Ra (`SurfaceFinish`),
  chip packing (`ChipEvacuation`), fixture creep under a loose clamp
  (`IStockFixture.SlipMmPerMm3`), stainless and Delrin.
- Measuring: touch probe T8 (`ToolKind.TouchProbe`; the bridge's `probe` pin
  is netted to `motion.probe-input`, command PROBE_INPUT 24; G38.2 read the
  top face at -0.012 and a side at -41.987 / -41.994 for -42.000);
  Z setting block (`FeelerGauge`, fit ±0.01 mm).
- Spindle: sim_spindle's `limit_speed` slews at
  `MachineSpec.SpindleAccelRpmPerSec` (0 = lathe 400, mill 2500 rpm/s; INI
  `[SPINDLE_0]GODOT_ACCEL_RPM_PER_SEC`); the bridge reports the speed
  reached, signed (M4 turns the other way); the control waits for at-speed.
- Fanuc T0101 (`MachineSpec.ToolCalls = FanucTurret`, disc-turret lathes):
  REMAP of T to `linuxcnc-config/fanuc_t.ngc` (lathe-fanucy's
  `remap.py` / `toplevel.py`), the turret confirms its own index; the bridge
  exports INI_FILE_NAME so the preview sees the remap. No preset uses it.
- Lathe work: drilling and boring, threads on a 0.1 mm profile, face cuts by
  the insert's swept ring, blocks in a chuck, parting that lets the part go,
  ID gripping on hard jaws (`ChuckGrip.Inside`).
- Chucks are things of their own (`ChuckModel`: size, length nose to jaw
  face, jaws, opening, bore, rpm, collet sizes, spindle nose D1-3..D1-8 with
  3 or 6 cams, jaw family). A lathe takes one for its own nose that clears
  its bed. On the lathe: cam key undoes the cams, the chuck lifts off (bare
  nose drawn) or swaps; cams locked again (spindle with one undone = Crash);
  `ProceduralLatheBody.FitChuck` / `FitBareNose` move the jaw face and the
  carriage zone. Collet chucks take one `ColletItem` per size (size to
  size - 1; loosened, the bar slides). Four-jaw independents seat ~0.3 mm
  off and are dialled in with the mallet.
- Scroll chuck mechanics (`ChuckJaws`): one scroll position; a jaw's radius
  = catch radius - pitch x (scroll - where it was caught) / turn - pitch x
  (number - 1) / N, so opening, centre error and drawing follow from it.
  Caught only as the thread start crosses its slot going in; let go only
  when turned back past where it was caught (then it lies loose); the key
  never lets go of a jaw holding a bar. Faults: wrong slot (a share of a
  pitch off), laid in after the start passed (a turn late, a pitch off).
  `ChuckVisual` draws every chuck from its model: CSG body (T-slots, bore),
  a spiral scroll disc that turns with the key, key sockets, slot numbers,
  cam studs; `JawMesh` stepped hard / reversed and soft jaws.
- Chuck stand (`ChuckStand`, a Carryable: fixed with a click, turntable on
  the wheel or Shift+wheel with the key in): a seated chuck gets its real
  slotted collision; jaws are bodies set into the guides by hand (body wider
  than the groove, tongue narrower), caught by the scroll only on a key
  notch that brings the start across their slot (`CatchLyingJaws`); let go,
  they are bodies again. In physical mode nothing on screen says how the
  jaws went in: the scroll down the slots, the feel and the sounds (Clink,
  Clack, Ratchet, Scrape) do. Nothing is simulated per frame.
- Chuck cabinet (`ChuckCabinet`): a curved enclosure like a lathe guard
  (`ChuckEnclosure`, curved doors sliding round the curve, 21 chucks from
  `ChuckModel.Showcase`, mostly D1-4), a drawer per `Catalogue` chuck with
  its numbered spare jaws or collets, a worktop with a stand.
- Machine settings in `MachineSpec` (spindle rpm and acceleration, jog,
  coolant, door interlock, thermal growth, homing, backlash, wrapped rotary,
  lathe swing / G7-G8 / tailstock / spindle nose / spare chucks / chuck
  rpm, mill magazine, probe overtravel and signal lead) with `Validate`.

`Main.tscn`: BenchLathe (9200/9201), BenchMill (9206/9207), Trunnion5Axis,
FlatBedLathe (9208/9209), tool carts, fixture carts, stock station, tool
bench, saw bench, the chuck cabinet at (-5.6, -1.1, -1) facing +X. A session starts with EMPTY spindles/turrets and an OPEN door on
enclosed machines.

## Open

1. **User play-test** of every keyboard/mouse path, especially the new
   carrying controls, physical jaw setting on the stand (tongue clearance,
   12 deg tolerance) and the sound cues. The agent drives the methods behind
   them in harness scenes only.
2. Place `JobBoard.tscn` and `InspectionTable.tscn` in `Main.tscn` (the
   user keeps uncommitted editor work in that file: stage only new lines).
3. Chucks, not done: jaws on the lathe itself are still set by click (the
   physical way is on the stand); a boring bar running into ID-gripping jaws;
   soft jaws for ID; reversed jaws' larger reach in the turret collision;
   the lathe workpiece assumes a turned part's 0.1 mm profile step; a
   tailstock does not count as support against slender-bar chatter.
4. Not modelled: guards on open bench machines, RTCP, persistence of cut
   state across a restart, tool deflection, a locking rotary brake, the
   random changer's own pocket moves; the requested tool is not in the STATE
   packet. The probe senses only the stock (not vise / table). GRAPH's check
   uses today's offsets for the whole program and does not check the stock.
5. Demo programs: not all eleven have run end to end live;
   `demo_facing_turning` on preset 1 drives the tool into the chuck (GRAPH
   flags it at N26).
6. Lathe X wear is entered in the tool table's own X units; radius vs
   diameter under G7 not checked live. `BridgeClient.LastCommandRttMs` stays
   NaN. DRY RUN key blank (LinuxCNC has no dry-run feed).
7. **Config mechanism**: `WorkshopSettings` (Project Settings,
   `cnc_trainer/workshop/*`) holds saw clamping, physical handling, hand
   strength and weight feel. Candidates: door interlock default, safety
   records, shortened tool life, chip size, belt slots, wear factors; an
   in-game instructor panel.

## Design decisions that are easy to undo by accident

- LinuxCNC is the only authority. Material is removed only from CONFIRMED
  samples; prediction is visual only.
- Each machine: own LinuxCNC in distro `Ubuntu`, isolated by
  `bridge/run_isolated.sh` (private mount/`/tmp`, IPC, PID namespace,
  per-run `RTAPI_FIFO_PATH`). Each machine needs its own port pair; all
  share one WSL address.
- `ProceduralMachine` origin is ON THE FLOOR (y = `WorldScale.FloorY`).
- Tool error belongs to the physical `ToolItem`, not to the tool number.
- Mill tool length reference is per machine (`MillStructure.ToolLengthReference`,
  `ToolLengthModel`). Default `SpindleGaugeLine`: LinuxCNC Z is the spindle
  gauge line, the table holds whole gauge lengths (catalogue length + 60 mm
  holder), the INI Z limits / home / tool change Z are the physical ones
  shifted up by the longest tool's gauge length (`AxisDef.ControlOffset`,
  taken back in `MachineKinematics.LinkTransform`), and G54 Z stays the
  physical Z of work zero. `NominalToolTip` is the old simplified model
  (Z = nominal tip, table holds only the setting error). `PresetToolLengths`
  starts the table with catalogue lengths. Without G43 the gauge line goes
  to the programmed Z: a soft-limit alarm when the part is low, a dive when
  it is in reach; a wrong H crashes (`TmpFix wrongh`).
- Every mill program needs `G43` after each `Tn M6`; without it LinuxCNC
  ignores tool length offsets.
- Mill/lathe cutting do NOT share `IMaterialModel` (lathe 1D radius profile
  vs. voxel field; forcing one interface broke LSP).
- OFS/SET TOOL is a Fanuc-style table of every tool (GEOM / WEAR, DIA or
  R / T): the bridge sends LinuxCNC's tool table (TOOL_TABLE packet, type 4),
  `BridgeClient.Offsets` (`ToolOffsetTable`) keeps the wear and shows
  geometry = LinuxCNC offset - wear. INPUT / +INPUT write geometry or wear
  (`OffsetCommands.SetToolGeometry` / `SetToolWear`), MEASURE only on the
  tool in the spindle and keeps its wear. Wear lives for the session, like
  LinuxCNC's table. Harness: `TmpConsole` scenarios `ofs`, `ofslathe`
  (`CNC_CONSOLE_PRESET=lathe`).
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
  loose, stall, shank, rapid, idgrip, collet, camsloose, liftoff, spin),
  `TmpFix` (tram, clamps, clampsloose, clampscrash, plate, door, chuck,
  chuckkey, snap, nospindle, wear, wearcool, toollen, wrongtool, empty,
  rapid, holder, table, vol, nog43, wrongh, pathcheck, probe, probecrash,
  block), `TmpLive` (CNC_LIVE_PRESET / PROGRAM / ORDER, CNC_TOOL_CALLS=fanuc),
  `TmpCabinet` (windowed: cabinet, stand, physical jaws; CNC_OUT for PNGs),
  `TmpHold` (headless: holding in place / bringing to the hands), `TmpLook`
  (windowed screenshots, scenarios main / lathe).
- Editing from the shell: bash heredocs here mangle `\n` escapes inside C#
  strings (and sometimes Turkish text). Write a Python script file with the
  Write tool and run it, or use Edit. `look.ps1 -Scenario main` uses the REAL ports and takes over
  an open game. A harness killed on timeout leaves its Godot child running.

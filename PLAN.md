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
- Hacksaw (`HandToolKind.Hacksaw`, `SawAction` on the player): with the saw
  in hand the crosshair on an `ISawable` shows the cut plane; a click marks
  it, then mouse forward/back strokes the saw (only forward strokes cut;
  above 0.45 m/s they cut 30 % and warn); T finishes, right click / Esc
  gives up; feet and view stay put meanwhile. Rates: steel 500, brass 1000,
  aluminium 1500 mm² per metre of stroke (Ø30 steel ≈ 11 full strokes).
  1 mm kerf. Loose stock is sawn where it lies (held still while sawing;
  both pieces drop) unless `cnc_trainer/workshop/saw_requires_clamping` is
  on (`StockItem.PlanCut`; a held piece defers to its holder). Targets:
  `BenchVise` on the `SawBench` (next to the stock
  station; clamp / take with a click; cuts outside the jaws only; the
  right-hand piece drops) splitting raw and turned stock
  (`StockItem.SplitAt`); a bar in the lathe chuck (`JawChuck.Saw` ->
  `LatheWorkholding`, spindle stopped, outside the jaws,
  `LatheCutting.SawThrough`; square cuts only), and the part in a mill
  fixture or indexer chuck (`MillWorkholding`, found through
  `Carryable.SawnBy`; the bigger piece stays, the smaller drops). The wheel
  turns the cut up to 60 deg about the vertical while aiming. Square cuts on
  raw / turned stock split exactly; angled cuts and milled stock go through
  voxels (`VoxelSaw`: `VoxelStock.PlaneSection` / `RemoveBeyond` /
  `Cropped`; a raw / turned item converts once, `StockItem.Voxels`), and
  their pieces are block items drawn by their voxels (`StockItem.FromVoxels`).
  Harnesses: `TmpSaw` (vise; CNC_SAW=angle), `TmpLathe` saw, `TmpFix` saw
  (live).
- Workshop hand tools (all on the 2.4 m tool bench, front to back): safety
  glasses (click to wear, G off; cutting within 3.5 m without them is a
  `NoGlasses` record, `MachineSafety.Record`, machine keeps running); chip
  hook (rakes chips under the crosshair; landed chips stay until raked or
  recycled; a bare hand on chips warns, or trips a turning machine,
  `UnsafeHands`); burrs (`StockItem.Burred`: sawn and parted-off pieces;
  file / deburring blade with the left button held and mouse strokes; a
  burred part fixtured warns and adds chuck runout); dial indicator on the
  lathe bar (`ChuckGrip` runout: random 0.03-0.12 mm TIR + tilt; click puts
  it on, the wheel turns the chuck by hand, needle / min / max under the
  crosshair; a mallet tap on the high side of a 2/3-tight chuck brings it
  in; spindle start with it on trips the machine); pitch gauge (reading) and
  the M20x2 ring gauge (`ThreadGaugeAction`, `ThreadCheck` / `ThreadSpec`
  6g: GO must run the thread, NO-GO stop within 1.5 turns). With the
  library 60 deg insert (0.1 nose) a nominal-depth P2 thread is fat: the
  demo `lathe_dis.ngc` turns Ø19.85 and cuts K1.49 (offline: GO on, NO-GO
  stops). Live on the hands-on bench lathe the OD came out Ø20.16 (machine
  errors / setup) and both sides refused on the major diameter: measure and
  correct the X offset first, as on a real machine. The crosshair looks past
  the belt except for F. Harnesses: `TmpTools`, `TmpLathe` runout / m20.
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
2. The legacy hand-written path (`LatheMachine`, `MillMachine`,
   `MachineBody`, `MillBody`, `MillHeightmap`, `MillStockVisual`,
   `Lathe.tscn`, `Mill.tscn`) is deleted; its turret angle math lives on in
   `TurretIndex`. Git history has it if it is ever wanted.
3. **Scenario mode v1** is in (`Scripts/Inspection`): `WorkOrder` /
   `PartFeature` resources under `res://Machines/WorkOrders/` (the board
   finds every .tres there), `JobBoard` (wheel browses, click takes / hands
   back an order; its drawing card stays top-left on the HUD via
   `IJobCard`), `InspectionTable` (put the finished part on it: report card
   with measured sizes, material, Ra, incidents since the order was taken,
   score and a root-cause hint). Measuring is `PartInspector` over
   `IPartGeometry`: `TurnedGeometry` (profile, read from whichever end
   matches the drawing), `VoxelGeometry` (`VoxelStock.Span` / `SurfaceY`,
   sub-voxel), `WithSurface` (adds Ra). Live-verified with the demo programs
   (`TmpLive` + `CNC_LIVE_ORDER`). Still to do: place `Scenes/Workshop/JobBoard.tscn`
   and `InspectionTable.tscn` in `Main.tscn` (left out on purpose while the
   user edits that scene); more orders. Drawings: `WorkOrder.Drawing` (an
   SVG under `Machines/WorkOrders/drawings/`) + `DrawingNotes` (text, since
   Godot's SVG import drops `<text>` and markers: arrowheads are paths),
   drawn bottom-left by `Player.DrawingView` while the order is open. Not
   checked by eye in the running game (headless cannot render text).
4. Not modelled: guards on open bench machines, RTCP, persistence of cut
   state across a restart, tool deflection, a
   locking rotary brake (LinuxCNC `LOCKING_INDEXER` needs an unlock handshake
   basic_sim does not wire), the random changer moving tools between pockets
   on our side (LinuxCNC keeps its own pocket bookkeeping). The requested tool
   is not in the STATE packet, so an empty magazine pocket is only reported
   after the change.
5. The eleven demo programs in `linuxcnc-config/nc_files/` have never all
   run end to end live (the statue program only offline + `rs274`).
   `lathe_kademeli_mil` was rewritten: G1 retracts at its shoulders (G0
   rubbed them), the big step is Ø36 to Z-64 so the 20 mm parting blade can
   part it at Z-55 (part 52 long: the blade's controlled point is its chuck-
   side corner), 95+ mm out of the chuck. It (HandsOnLathe, preset 2) and
   `demo_mill_pocket` run end to end and pass their work orders. Found on
   the way: `LatheRadiusProfile.Sever` stopped at the parting groove's own
   empty buckets, so a blade wider than one bucket never let a part go.
   `demo_facing_turning` on preset 1 still drives the tool into the chuck.
6. Side finding, not investigated: `BridgeClient.LastCommandRttMs` stays NaN.
7. Console: MC LOCK is back (`CuttingProcess.SetMachineLock`: the body
   stays, nothing cuts or collides; released after the control moved it
   unhomes the machine, as on a Fanuc). DRY RUN stays blank: LinuxCNC has no
   dry-run feed (only the 0-150 % override). Conveyor / air keys blank,
   F1-F7 unassigned. PRG RESTART uses LinuxCNC run-from-line, so tool /
   spindle set before that line are not replayed. GRAPH now draws the whole
   opened program dim underneath the cut path: the bridge parses it with
   LinuxCNC's interpreter on PROGRAM_OPEN (`preview.py`, all six axes, on a
   thread with its own stat) and streams PREVIEW chunks; `BridgeClient.ProgramPath`.
   While GRAPH is up the path is also checked against the setup
   (`Machining.ProgramCheck`, every 2 mm, at most 20000 points, once a
   second): each `IPathObstacle` (`MillWorkholding` = fixture and table with
   the spindle tool, holder and nose; `LatheCutting` = chuck, tailstock,
   guarding, turret vs. bar). The first hit is a red cross and a CRASH line.
   Limits: today's work and tool offsets for the whole program (a later
   tool's length is not used), the bar/stock is not checked (rapids through
   material the program has already cut would be false alarms).
8. Lathe X wear is entered in the tool table's own X units (G10 L1 X);
   whether that is radius or diameter under G7 was not checked live.
9. Realism pass (this branch): tool stick-out is the player's (wheel at the
   length gauge slides the cutter in its collet, `ToolItem.SlideInHolder`;
   chatter uses the real stick-out); a bar sticking out more than 4
   diameters chatters (`CuttingLoad.SlenderWorkRatio`, setup sheet warns; a
   tailstock does not yet count as support); chatter has its own sound;
   Ra from feed and nose / cutter radius of the last non-parting tool
   (`SurfaceFinish`, on the part and on the report card); chips pack in a
   deep dry pocket in ductile material and snap the tool (`ChipEvacuation`);
   a vise at 2/3 or a loose clamp nut lets the part creep under the cut
   (`IStockFixture.SlipMmPerMm3`, `StockItem.SlipMm`, the report card names
   it as the root cause); stainless and Delrin (`MaterialPreset` now owns
   name, saw rate and "needs coolant"); interchangeable lathe jaws (hard,
   hard reversed for big parts, soft bored to the part for near-zero runout;
   `ChuckGrip.Jaws`, `JawSetItem`, a jaw rack left of the lathe control).
   FEED HOLD in MDI now resumes with CYCLE START. ID gripping on the hard
   jaws (`ChuckGrip.Inside` / `InsertInside`, the key opens out then
   tightens; `JawChuck.BoreAt` finds the bore at the end that goes in).
   Live (TmpLathe `idgrip`): a Ø70 part held by a Ø40 x 25 bore turned to
   Ø66.4 on its outside, no incident. Not done: a boring bar running into
   the jaws inside the bore is not checked; soft jaws for ID; the lathe's
   workpiece profile assumes a turned part's StepMm is its own 0.1 mm;
   reversed jaws' larger reach in the turret collision.
   Chucks are things of their own (`ChuckModel`: diameter, length nose to
   jaw face, jaws, opening, bore, rated rpm, collet sizes); the lathe's own
   comes from its LatheStructure fields, spares from `SpareChucks` (default
   Ø125 4-jaw independent, ER32 collet). A chuck off the lathe is a
   `ChuckItem` carrying its `ChuckGrip` (its jaws stay in it). Change: key
   out, cam key undoes 3 cam locks (`JawChuck.CamsLocked`), swap, lock again;
   spindle with a cam undone = Crash. `ProceduralLatheBody.FitChuck` moves
   the jaw face (nose + length) and the carriage's chuck zone; bigger than
   the bed clears is refused. Jaws (`ChuckJaws`): numbered `JawItem`s, one
   at a time; the scroll's thread start moves a slot per 3 key notches;
   opening past full runs jaws off (3, 2, 1); a jaw in the wrong slot or a
   turn late is off by pitch/3 per slot or a pitch per turn, the chuck holds
   that far off (jaws closed in show it: they do not meet). 4-jaw: any
   order, ~0.3 mm seat, the mallet adjusts one jaw against the other. Collet:
   `ColletItem` per size (size to size-1), key only tightens/loosens, loose =
   the bar slides. Live (TmpLathe `collet` / `camsloose`): jaws out 3-2-1,
   reversed set in order meets, out of order 4 mm off (TIR ~8 mm), cams
   3->0, ER32 fitted (jaw face -150 -> -155), Ø20 collet 0.005 mm, loosened
   bar slid, turned with no incident; cams left undone -> Crash.
   Gauge block: `HandToolKind.GaugeBlock` (50.000 mm Z setting block) on the
   tool bench, felt like the paper (`Interaction.FeelerGauge`, fit ±0.01 mm
   against the paper's ±0.03); `MillCutting.TipGapMm` searches coarse then
   fine. Live (TmpFix `block`): snug at Z50.000, free at 50.020.
   Fanuc T0101 (`MachineSpec.ToolCalls = FanucTurret`, disc-turret lathes
   only): the INI gets `REMAP = T prolog=get_T_number ngc=fanuc_t` + a
   [PYTHON] section; `linuxcnc-config/fanuc_t.ngc` does M6 + G43 H<offset>
   (`remap.py` / `toplevel.py` from LinuxCNC's lathe-fanucy sample); the
   turret confirms its own index (`ProceduralMachine.IndexTurret`). The
   bridge exports INI_FILE_NAME and runs from the config dir so the GRAPH
   preview's interpreter sees the remap too. Live: `lathe_kademeli_mil_fanuc`
   on preset 2 graded KABUL 100/100, preview clear. No preset uses it by
   default. Wear stays Godot-side (PR #23), not T10001 rows.
   Touch probe (T8 on the bench and VMC mills, `ToolKind.TouchProbe`): the
   bridge nets its own `probe` pin to `motion.probe-input` (basic_sim leaves
   it free) and PROBE_INPUT (24) drives it; `MillCutting` samples 26 points
   on the ball against the stock, `MillWorkholding.CheckMeasuringTool` sends
   the signal and breaks the stylus past `MillStructure.ProbeOvertravelMm`.
   Live: G38.2 onto the top face read Z -0.012 at F100; onto a side, -41.987
   at F100 and -41.994 at F25 for -42.000 (signal latency + voxel SDF);
   `ProbeSignalLeadSec` can lead it, default 0. A G1 into the work with the
   probe in is stopped by LinuxCNC itself ("Probe tripped during non-probe
   move"). The probe only sees the stock, not the vise or table.
10. **Idea: a Godot-side config mechanism** for rules that are a choice, not
   physics. Today one setting exists: `cnc_trainer/workshop/saw_requires_clamping`
   in Project Settings (`WorkshopSettings`, default off). Candidates to move
   there: door interlock default, safety records (glasses / chip hand),
   shortened tool life, chip size multiplier, belt slot count, wear factors.
   Open questions: Project Settings vs a `GameSettings` resource vs
   `user://settings.cfg`; an in-game settings / instructor panel; per-machine
   overrides (MachineSpec already holds per-machine choices).

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
  loose, stall, shank, rapid), `TmpFix` (tram, clamps, clampsloose,
  clampscrash, plate, door, chuck, chuckkey, snap, nospindle, wear, wearcool,
  toollen, wrongtool, empty, rapid, holder, table, vol), `TmpLook` (windowed
  screenshots). `look.ps1 -Scenario main` uses the REAL ports and takes over
  an open game. A harness killed on timeout leaves its Godot child running.

# Bridge wire protocol v1

Transport: UDP. The bridge (WSL2/Linux) is the server for the **state** stream (it
sends unsolicited, high rate); the bridge is also the one *listening* for the
**command** stream (Godot sends on demand).

Because WSL2 uses NAT networking on this machine (no `.wslconfig` mirrored mode --
Windows 10 doesn't support it), the two directions are asymmetric and are solved
differently -- confirmed empirically, see `PLAN.md` Phase 0 notes:

- **Godot -> bridge (commands)**: WSL2 has had automatic "localhost forwarding" for
  Windows-initiated connections since early WSL2 -- a socket the bridge binds on
  `0.0.0.0:UDP_COMMAND_PORT` inside the VM is reachable from Windows at
  `127.0.0.1:UDP_COMMAND_PORT`. Godot just sends there; no discovery needed. Acks go
  back to whatever source address the command came from, same socket.
- **Bridge -> Godot (state/event/preview)**: that forwarding does not run in reverse,
  so the bridge sends to the Windows host's address on the WSL virtual network, i.e.
  the gateway from `ip route show default` inside the VM (stable for the life of one
  `wsl` session; re-read at bridge startup, see `discover_windows_host_ip()` in
  `bridge/net.py`). Godot binds `0.0.0.0:UDP_STATE_PORT` and just listens -- the
  gateway IP is only something the *bridge* needs to know.

Default ports: state `9200/udp`, command `9201/udp`.

All multi-byte fields are little-endian. Every packet starts with:

```
u8  magic       = 0xC0
u8  packet_type
u64 seq         (monotonically increasing per packet_type per sender, wraps at 2^64)
i64 t_mono_ns   (sender's monotonic clock, nanoseconds, NOT wall clock — only valid
                 for computing deltas against other timestamps from the same sender)
```

## STATE packet (bridge -> Godot), packet_type = 1, fixed size, sent at the configured
state-publish rate (default 250 Hz; configurable 200-1000 Hz per the brief)

Everything here is **confirmed** data straight from `linuxcnc.stat()` (or derived from
two consecutive confirmed samples, e.g. velocity) — nothing predicted. All positions
are in the machine's program units (mm here) in machine coordinates (G53), so Godot
applies work offsets itself if it wants a work-relative view (keeps the bridge dumb).

| field | type | meaning |
|---|---|---|
| pos_x, pos_z | f64, f64 | confirmed actual joint position (`stat.actual_position`) |
| vel_x, vel_z | f64, f64 | finite-difference velocity between this and the previous confirmed sample |
| feed_rate | f64 | current commanded feed rate, program units/min |
| spindle_rpm | f64 | `stat.spindle[0]['speed']`, signed (direction) |
| feed_override | f32 | 0..~1.5 |
| spindle_override | f32 | 0..~1.2 |
| spindle_enabled | u8 (bool) | |
| tool_number | i32 | active tool (`stat.tool_in_spindle`) |
| tool_nose_radius | f32 | from the tool table entry for the active tool |
| task_mode | u8 | 0=MANUAL 1=AUTO 2=MDI |
| interp_state | u8 | 0=IDLE 1=READING 2=PAUSED 3=WAITING |
| exec_state | u8 | LinuxCNC `exec_state` enum, passthrough |
| motion_line | i32 | current executing gcode line number (program preview sync) |
| estop | u8 (bool) | |
| machine_on | u8 (bool) | |
| homed_x, homed_z | u8, u8 (bool) | |
| in_cycle | u8 (bool) | program running (not paused/idle) |
| pad | u8[1] | alignment pad, reserved, must be 0 |

Total body after the 18-byte common header: 4+4+8+8+8+8+8+4+4+1+4+4+1+1+1+4+1+1+1+1+1 = 76 bytes (see
`protocol.py`/`BridgeProtocol.cs` for the authoritative `struct` layout — this table is
descriptive, the code is normative).

## EVENT packet (bridge -> Godot), packet_type = 2, variable size, sent only on change
(alarms, program-load results, tool-change requests) — small enough traffic that
reliability doesn't matter much, but Godot de-duplicates by `seq`.

```
u8  event_kind   (0=ALARM 1=PROGRAM_LOADED 2=TOOL_CHANGE_REQUEST 3=INFO)
u16 text_len
u8[text_len] text   (utf-8)
```

## PREVIEW packet (bridge -> Godot), packet_type = 3, sent once per program load (and
chunked — a program can have thousands of segments, so it's split across as many
packets as needed, each capped near 1200 bytes to stay under typical UDP MTU).

```
u32 total_segments
u32 chunk_index       (0-based)
u32 segments_in_chunk
repeated segments_in_chunk times:
    u8  motion_type    (0=rapid 1=linear 2=arc_cw 3=arc_ccw)
    i32 line_number
    f64 start_x, start_z
    f64 end_x, end_z
    f64 feed_rate      (program units/min, 0 for rapids)
```

## COMMAND packet (Godot -> bridge), packet_type = 16, sent on user action

```
u8  command_kind
u64 client_seq     (bridge drops any client_seq <= highest already applied, so a
                     retransmitted UDP packet is never double-applied)
... command-specific payload (see protocol.py CommandKind for the full list: JOG_START,
    JOG_STOP, JOG_INCREMENTAL, SET_MODE, MDI, PROGRAM_OPEN, PROGRAM_RUN, PROGRAM_PAUSE,
    PROGRAM_STOP, PROGRAM_STEP, SET_FEED_OVERRIDE, SET_SPINDLE_OVERRIDE, SPINDLE_ON,
    SPINDLE_OFF, TOOL_CHANGE_CONFIRM, HOME_AXIS, ESTOP_RESET, MACHINE_ON, MACHINE_OFF)
```

## ACK packet (bridge -> Godot), packet_type = 17

```
u64 acked_client_seq
u8  ok
u16 text_len
u8[text_len] text    (error message if ok == 0)
```

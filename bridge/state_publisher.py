"""Polls linuxcnc.stat() at a fixed rate and publishes STATE packets over UDP.

Keeps exactly ONE linuxcnc.stat() object for its whole life (see linuxcnc_process.py
docstring on wait_for_nml_ready for why re-creating it is dangerous)."""
from __future__ import annotations

import socket
import time

from protocol import (AXIS_CAPACITY, AXIS_X, AXIS_Z, EventKind, StateSample, TaskMode, InterpState, ToolTableEntry,
                      pack_event, pack_tool_table)

# stat.actual_position is ALWAYS axis-letter indexed (a 9-tuple X,Y,Z,A,B,C,
# U,V,W) regardless of kinematics -- same order as protocol.py's AXIS_*
# constants, so no per-machine mapping is needed for position/velocity at
# all, lathe or mill alike. stat.homed, by contrast, is indexed by JOINT
# number (this machine's kinematics config), which generally differs from
# axis-letter order, so homed status alone needs a (joint, AXIS_*) map --
# this lathe's is joint0->X, joint1->Z. A 3-axis mill's own StatePublisher
# instance would pass its own map here instead of this module gaining
# per-machine-type branches.
DEFAULT_LATHE_JOINT_AXIS_MAP = ((0, AXIS_X), (1, AXIS_Z))


class StatePublisher:
    def _spindle_rpm(self, spindle0) -> float:
        """Signed speed the spindle has reached (negative = M4). stat only
        has the commanded speed, unsigned, with the direction apart."""
        actual = self.spindle_actual()
        if actual is not None:
            return actual
        direction = spindle0["direction"] or (1 if spindle0["speed"] >= 0 else -1)
        return abs(spindle0["speed"]) * direction

    def __init__(self, stat, sock: socket.socket, dest_addr: tuple[str, int], rate_hz: float = 250.0,
                 joint_axis_map: tuple[tuple[int, int], ...] = DEFAULT_LATHE_JOINT_AXIS_MAP,
                 tool_change_pending=lambda: False, error_channel=None, spindle_actual=lambda: None):
        self.stat = stat
        self.sock = sock
        self.dest_addr = dest_addr
        self.period = 1.0 / rate_hz
        self.seq = 0
        self.joint_axis_map = joint_axis_map
        self.tool_change_pending = tool_change_pending
        self.spindle_actual = spindle_actual
        self.error_channel = error_channel
        self.active_axis_mask = 0
        for _joint, axis in joint_axis_map:
            self.active_axis_mask |= 1 << axis
        self._prev_pos: tuple[float, ...] | None = None
        self._prev_t: float | None = None
        self._tool_table: list[ToolTableEntry] | None = None
        self._tool_table_checked = 0.0
        self._tool_table_sent = 0.0
        self._tool_table_seq = 0

    def _tool_nose_radius(self) -> float:
        tool_no = self.stat.tool_in_spindle
        for entry in self.stat.tool_table:
            if entry.id == tool_no:
                return float(entry.diameter)  # lathe tool table reuses this field for nose radius
        return 0.0

    def tool_table(self) -> list[ToolTableEntry]:
        """Every real tool in LinuxCNC's table, once each (entry 0 is the
        spindle's copy of the loaded tool, empty slots have id -1)."""
        entries = {}
        for entry in self.stat.tool_table:
            if entry.id > 0 and entry.id not in entries:
                entries[entry.id] = ToolTableEntry(entry.id, float(entry.xoffset), float(entry.zoffset),
                                                   float(entry.diameter), int(entry.orientation))
        return [entries[number] for number in sorted(entries)]

    def _publish_tool_table(self) -> None:
        """On every change, and every few seconds anyway so a packet lost or
        a Godot that subscribed late still gets it."""
        now = time.monotonic()
        if now - self._tool_table_checked < 0.25:
            return
        self._tool_table_checked = now
        table = self.tool_table()
        if table == self._tool_table and now - self._tool_table_sent < 2.0:
            return
        self._tool_table = table
        self._tool_table_sent = now
        self.sock.sendto(pack_tool_table(self._tool_table_seq, table), self.dest_addr)
        self._tool_table_seq += 1

    def sample_once(self) -> StateSample:
        self.stat.poll()
        now = time.monotonic()

        position = tuple(self.stat.actual_position[:AXIS_CAPACITY])

        if self._prev_pos is not None and self._prev_t is not None and now > self._prev_t:
            dt = now - self._prev_t
            velocity = tuple((p - prev) / dt for p, prev in zip(position, self._prev_pos))
        else:
            velocity = (0.0,) * 6
        self._prev_pos = position
        self._prev_t = now

        homed_mask = 0
        for joint, axis in self.joint_axis_map:
            if self.stat.homed[joint]:
                homed_mask |= 1 << axis

        # linuxcnc MODE_MANUAL=1,MODE_AUTO=2,MODE_MDI=3 -- remap to our 0-based enum
        task_mode = {1: TaskMode.MANUAL, 2: TaskMode.AUTO, 3: TaskMode.MDI}.get(self.stat.task_mode, TaskMode.MANUAL)
        # linuxcnc INTERP_IDLE=1 INTERP_READING=2 INTERP_PAUSED=3 INTERP_WAITING=4 -- remap to 0-based
        interp_map = {1: InterpState.IDLE, 2: InterpState.READING, 3: InterpState.PAUSED, 4: InterpState.WAITING}
        interp_state = interp_map.get(self.stat.interp_state, InterpState.IDLE)

        spindle0 = self.stat.spindle[0]
        return StateSample(
            position=position, velocity=velocity,
            feed_rate=self.stat.feedrate, spindle_rpm=self._spindle_rpm(spindle0),
            feed_override=self.stat.feedrate, spindle_override=spindle0["override"],
            tool_number=self.stat.tool_in_spindle, tool_nose_radius=self._tool_nose_radius(),
            spindle_enabled=bool(spindle0["enabled"]), task_mode=int(task_mode),
            interp_state=int(interp_state), exec_state=self.stat.exec_state,
            motion_line=self.stat.motion_line, estop=bool(self.stat.estop),
            machine_on=bool(self.stat.enabled),
            in_cycle=self.stat.interp_state != 1,  # not INTERP_IDLE
            active_axis_mask=self.active_axis_mask, homed_mask=homed_mask,
            tool_change_pending=bool(self.tool_change_pending()),
            work_offset=tuple(self.stat.g5x_offset[i] + self.stat.g92_offset[i] + self.stat.tool_offset[i]
                              for i in range(AXIS_CAPACITY)),
            motion_type=int(self.stat.motion_type),
            coolant=(1 if self.stat.flood else 0) | (2 if self.stat.mist else 0),
        )

    def publish_once(self) -> None:
        sample = self.sample_once()
        packet = sample.pack(seq=self.seq)
        self.seq += 1
        self.sock.sendto(packet, self.dest_addr)
        self._publish_tool_table()

        # LinuxCNC's own operator messages ("tool not found", "would exceed
        # limit"...) are the only explanation a rejected command ever gets.
        error = self.error_channel.poll() if self.error_channel else None
        if error:
            self.sock.sendto(pack_event(self.seq, EventKind.ALARM, error[1].strip()[:400]), self.dest_addr)

    def run_forever(self, should_stop) -> None:
        next_tick = time.monotonic()
        while not should_stop():
            self.publish_once()
            next_tick += self.period
            sleep_for = next_tick - time.monotonic()
            if sleep_for > 0:
                time.sleep(sleep_for)
            else:
                next_tick = time.monotonic()  # fell behind; resync instead of free-running catch-up

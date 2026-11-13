"""Polls linuxcnc.stat() at a fixed rate and publishes STATE packets over UDP.

Keeps exactly ONE linuxcnc.stat() object for its whole life (see linuxcnc_process.py
docstring on wait_for_nml_ready for why re-creating it is dangerous)."""
from __future__ import annotations

import socket
import time

from protocol import StateSample, TaskMode, InterpState

_HOMED_X_JOINT = 0
_HOMED_Z_JOINT = 1


class StatePublisher:
    def __init__(self, stat, sock: socket.socket, dest_addr: tuple[str, int], rate_hz: float = 250.0):
        self.stat = stat
        self.sock = sock
        self.dest_addr = dest_addr
        self.period = 1.0 / rate_hz
        self.seq = 0
        self._prev_pos: tuple[float, float] | None = None
        self._prev_t: float | None = None

    def _tool_nose_radius(self) -> float:
        tool_no = self.stat.tool_in_spindle
        for entry in self.stat.tool_table:
            if entry.id == tool_no:
                return float(entry.diameter)  # lathe tool table reuses this field for nose radius
        return 0.0

    def sample_once(self) -> StateSample:
        self.stat.poll()
        now = time.monotonic()
        pos_x = self.stat.actual_position[0]
        pos_z = self.stat.actual_position[2]
        if self._prev_pos is not None and self._prev_t is not None and now > self._prev_t:
            dt = now - self._prev_t
            vel_x = (pos_x - self._prev_pos[0]) / dt
            vel_z = (pos_z - self._prev_pos[1]) / dt
        else:
            vel_x = vel_z = 0.0
        self._prev_pos = (pos_x, pos_z)
        self._prev_t = now

        # linuxcnc MODE_MANUAL=1,MODE_AUTO=2,MODE_MDI=3 -- remap to our 0-based enum
        task_mode = {1: TaskMode.MANUAL, 2: TaskMode.AUTO, 3: TaskMode.MDI}.get(self.stat.task_mode, TaskMode.MANUAL)
        # linuxcnc INTERP_IDLE=1 INTERP_READING=2 INTERP_PAUSED=3 INTERP_WAITING=4 -- remap to 0-based
        interp_map = {1: InterpState.IDLE, 2: InterpState.READING, 3: InterpState.PAUSED, 4: InterpState.WAITING}
        interp_state = interp_map.get(self.stat.interp_state, InterpState.IDLE)

        spindle0 = self.stat.spindle[0]
        return StateSample(
            pos_x=pos_x, pos_z=pos_z, vel_x=vel_x, vel_z=vel_z,
            feed_rate=self.stat.feedrate, spindle_rpm=spindle0["speed"],
            feed_override=self.stat.feedrate, spindle_override=spindle0["override"],
            tool_number=self.stat.tool_in_spindle, tool_nose_radius=self._tool_nose_radius(),
            spindle_enabled=bool(spindle0["enabled"]), task_mode=int(task_mode),
            interp_state=int(interp_state), exec_state=self.stat.exec_state,
            motion_line=self.stat.motion_line, estop=bool(self.stat.estop),
            machine_on=bool(self.stat.enabled),
            homed_x=bool(self.stat.homed[_HOMED_X_JOINT]), homed_z=bool(self.stat.homed[_HOMED_Z_JOINT]),
            in_cycle=self.stat.interp_state != 1,  # not INTERP_IDLE
        )

    def publish_once(self) -> None:
        sample = self.sample_once()
        packet = sample.pack(seq=self.seq)
        self.seq += 1
        self.sock.sendto(packet, self.dest_addr)

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

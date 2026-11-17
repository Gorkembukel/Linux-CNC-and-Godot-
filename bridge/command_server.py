"""Receives COMMAND packets over UDP and applies them via linuxcnc.command().

Dedupes by client_seq: a command whose client_seq is <= the highest one already
applied for that command_kind is dropped (a stale UDP retransmit must never be
re-applied -- e.g. a doubled "cycle start" would restart the program)."""
from __future__ import annotations

import socket
import subprocess
import threading
import time

import linuxcnc

from protocol import Command, CommandKind, pack_ack, unpack_header, PacketType

_JOG_CONTINUOUS = linuxcnc.JOG_CONTINUOUS
_JOG_INCREMENT = linuxcnc.JOG_INCREMENT
_JOG_STOP = linuxcnc.JOG_STOP


class CommandServer:
    def __init__(self, stat, cmd, sock: socket.socket, on_subscribe=None, before_program_open=None,
                 joint_axis_map=()):
        self.stat = stat
        self.joint_to_axis = dict(joint_axis_map)
        self.cmd = cmd
        self.sock = sock
        self.on_subscribe = on_subscribe
        self.before_program_open = before_program_open
        self._highest_client_seq: dict[int, int] = {}
        self._ack_seq = 0
        self._lock = threading.Lock()

    def _ack(self, addr, client_seq: int, ok: bool, text: str = "") -> None:
        pkt = pack_ack(self._ack_seq, client_seq, ok, text)
        self._ack_seq += 1
        self.sock.sendto(pkt, addr)

    def handle_packet(self, data: bytes, addr) -> None:
        packet_type, seq, t_mono_ns, hlen = unpack_header(data)
        if packet_type == PacketType.SUBSCRIBE:
            if self.on_subscribe:
                self.on_subscribe(addr)
            return
        if packet_type != PacketType.COMMAND:
            return
        command = Command.unpack(data[hlen:])
        with self._lock:
            highest = self._highest_client_seq.get(command.command_kind, -1)
            if command.client_seq <= highest:
                return  # stale retransmit, already applied
            self._highest_client_seq[command.command_kind] = command.client_seq
        try:
            self._apply(command)
            self._ack(addr, command.client_seq, True)
        except Exception as e:  # noqa: BLE001 - report to client instead of crashing the loop
            self._ack(addr, command.client_seq, False, str(e))

    def _apply(self, command: Command) -> None:
        import struct
        kind = command.command_kind
        args = command.args

        if kind == CommandKind.JOG_START:
            joint, vel = struct.unpack("<Bf", args)
            self.cmd.jog(_JOG_CONTINUOUS, *self._jog_target(joint), vel)
        elif kind == CommandKind.JOG_STOP:
            (joint,) = struct.unpack("<B", args)
            self.cmd.jog(_JOG_STOP, *self._jog_target(joint))
        elif kind == CommandKind.JOG_INCREMENTAL:
            joint, vel, inc = struct.unpack("<Bff", args)
            # LinuxCNC takes the direction from the velocity's sign alone; a
            # negative distance on top of that would flip it back.
            self.cmd.jog(_JOG_INCREMENT, *self._jog_target(joint), vel, abs(inc))
        elif kind == CommandKind.SET_MODE:
            (mode,) = struct.unpack("<B", args)
            self.cmd.mode({0: linuxcnc.MODE_MANUAL, 1: linuxcnc.MODE_AUTO, 2: linuxcnc.MODE_MDI}[mode])
        elif kind == CommandKind.MDI:
            self.cmd.mode(linuxcnc.MODE_MDI)
            self.cmd.mdi(args.decode("utf-8"))
        elif kind == CommandKind.PROGRAM_OPEN:
            if self.before_program_open:
                self.before_program_open()
            self.cmd.mode(linuxcnc.MODE_AUTO)
            self.cmd.program_open(args.decode("utf-8"))
        elif kind == CommandKind.PROGRAM_RUN:
            (start_line,) = struct.unpack("<i", args)
            if self.stat.paused:
                self.cmd.auto(linuxcnc.AUTO_RESUME)
            else:
                self.cmd.mode(linuxcnc.MODE_AUTO)
                self.cmd.auto(linuxcnc.AUTO_RUN, start_line)
        elif kind == CommandKind.PROGRAM_PAUSE:
            self.cmd.auto(linuxcnc.AUTO_PAUSE)
        elif kind == CommandKind.PROGRAM_STOP:
            self.cmd.abort()
        elif kind == CommandKind.PROGRAM_STEP:
            self.cmd.auto(linuxcnc.AUTO_STEP)
        elif kind == CommandKind.SET_FEED_OVERRIDE:
            (value,) = struct.unpack("<f", args)
            self.cmd.feedrate(value)
        elif kind == CommandKind.SET_SPINDLE_OVERRIDE:
            (value,) = struct.unpack("<f", args)
            self.cmd.spindleoverride(value)
        elif kind == CommandKind.SPINDLE_ON:
            (rpm,) = struct.unpack("<f", args)
            direction = linuxcnc.SPINDLE_FORWARD if rpm >= 0 else linuxcnc.SPINDLE_REVERSE
            self.cmd.spindle(direction, abs(rpm))
        elif kind == CommandKind.SPINDLE_OFF:
            self.cmd.spindle(linuxcnc.SPINDLE_OFF)
        elif kind == CommandKind.TOOL_CHANGE_CONFIRM:
            self._confirm_tool_change()
        elif kind == CommandKind.HOME_AXIS:
            (axis,) = struct.unpack("<B", args)
            self.cmd.home(-1 if axis == 0xFF else axis)
        elif kind == CommandKind.ESTOP_RESET:
            self.cmd.state(linuxcnc.STATE_ESTOP_RESET)
        elif kind == CommandKind.MACHINE_ON:
            self.cmd.state(linuxcnc.STATE_ON)
        elif kind == CommandKind.MACHINE_OFF:
            self.cmd.state(linuxcnc.STATE_OFF)
        elif kind == CommandKind.UNHOME:
            # Motion only lets go of the reference once the drives are off.
            # (The MACHINE_OFF sent just before this needs a moment to land.)
            time.sleep(0.3)
            self.cmd.unhome(-1)
        else:
            raise ValueError(f"unknown command_kind {kind}")

    def _jog_target(self, joint: int) -> tuple[bool, int]:
        """(joint mode?, index) for a jog of this joint. Once homed, LinuxCNC
        runs in teleop mode and rejects joint jogs ("cannot jog joint"); it
        wants the axis (X=0, Y=1, Z=2, A=3...) instead."""
        if self.stat.motion_mode == linuxcnc.TRAJ_MODE_TELEOP:
            return False, self.joint_to_axis.get(joint, joint)
        return True, joint

    @staticmethod
    def _confirm_tool_change() -> None:
        """Completes a pending M6: the operator's "the tool is in" signal.
        iocontrol only samples its pins every [EMCIO] CYCLE_TIME (100 ms), so
        tool-changed is held until iocontrol drops tool-change rather than
        pulsed. Without a pending request this does nothing, so an early
        press cannot pre-confirm the next change."""
        def pending() -> bool:
            out = subprocess.run(["halcmd", "getp", "iocontrol.0.tool-change"], check=True,
                                 capture_output=True, text=True).stdout
            return out.strip() == "TRUE"

        if not pending():
            return
        subprocess.run(["halcmd", "setp", "iocontrol.0.tool-changed", "1"], check=True)
        deadline = time.monotonic() + 2.0
        while pending() and time.monotonic() < deadline:
            time.sleep(0.02)
        subprocess.run(["halcmd", "setp", "iocontrol.0.tool-changed", "0"], check=True)

    def run_forever(self, should_stop) -> None:
        self.sock.settimeout(0.5)
        while not should_stop():
            try:
                data, addr = self.sock.recvfrom(2048)
            except socket.timeout:
                continue
            try:
                self.handle_packet(data, addr)
            except Exception as e:  # noqa: BLE001 - a malformed/garbage packet must
                # never kill this loop: found live, the hard way -- one bad
                # packet (e.g. a stray non-protocol UDP send) used to silently
                # end the whole command-listening thread, after which every
                # real command (including every operator panel button) went
                # nowhere with no error visible anywhere.
                print(f"[command_server] dropping malformed packet from {addr}: {e!r}")

"""Starts/stops a headless (DISPLAY=dummy) LinuxCNC instance and keeps it alive.

LinuxCNC's own launcher script blocks on `read foo` from stdin when
[DISPLAY]DISPLAY=dummy (see /usr/bin/linuxcnc, the "dummy" case) and only runs its
Cleanup/shutdown sequence once that read returns -- either a line of input, or EOF.
We hold stdin open as a pipe so it blocks forever, and close it to shut down the
machine (HAL, motion, task, io) cleanly instead of killing it.
"""
from __future__ import annotations

import os
import subprocess
import time
from pathlib import Path


class LinuxCNCProcess:
    def __init__(self, ini_path: str, log_path: str | None = None):
        self.ini_path = str(Path(ini_path).resolve())
        self.config_dir = str(Path(ini_path).resolve().parent)
        self.log_path = log_path or str(Path(self.config_dir) / "linuxcnc_bridge.log")
        self._proc: subprocess.Popen | None = None
        self._log_fh = None

    def start(self) -> None:
        if self._proc is not None:
            raise RuntimeError("already started")
        self._kill_stragglers()
        self._clear_stale_shm()
        self._log_fh = open(self.log_path, "wb")
        # The launcher script (/usr/bin/linuxcnc) streams straight to our stdout
        # only when stdin is a tty OR $DISPLAY is unset; otherwise (our case: a
        # piped stdin, and WSL happens to have an X server at :0) it silently
        # redirects its own output to /tmp files and, on any error, pops a Tk
        # "show_errors.tcl" dialog that blocks forever waiting for a click nobody
        # can give it. Dropping DISPLAY forces the plain streaming path.
        env = dict(os.environ)
        env.pop("DISPLAY", None)
        env.pop("WAYLAND_DISPLAY", None)
        self._proc = subprocess.Popen(
            ["stdbuf", "-oL", "-eL", "linuxcnc", "-r", self.ini_path],
            cwd=self.config_dir,
            stdin=subprocess.PIPE,
            stdout=self._log_fh,
            stderr=subprocess.STDOUT,
            text=False,
            env=env,
        )

    def is_running(self) -> bool:
        return self._proc is not None and self._proc.poll() is None

    def stop(self, timeout: float = 10.0) -> None:
        if self._proc is None:
            return
        try:
            if self._proc.stdin and not self._proc.stdin.closed:
                self._proc.stdin.close()
        except (BrokenPipeError, OSError):
            pass
        try:
            self._proc.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            self._proc.terminate()
            try:
                self._proc.wait(timeout=5.0)
            except subprocess.TimeoutExpired:
                self._proc.kill()
                self._proc.wait(timeout=5.0)
        finally:
            self._proc = None
            if self._log_fh:
                self._log_fh.close()
                self._log_fh = None
            self._kill_stragglers()
            self._clear_stale_shm()

    @staticmethod
    def _kill_stragglers() -> None:
        """LinuxCNC's own Cleanup() stops HAL/motion/task/io, but on a slow or
        interrupted shutdown (e.g. this process being killed instead of asked to
        close stdin) those children can survive and hold the NML/HAL state, which
        then blocks the next start. Only exact, known process names are targeted —
        never a broad substring pattern (that previously matched and killed an
        unrelated system process)."""
        for name in ("linuxcncsvr", "milltask", "rtapi_app"):
            subprocess.run(["pkill", "-9", "-x", name], check=False,
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    @staticmethod
    def _clear_stale_shm() -> None:
        """Remove all SysV IPC (shm + semaphores + message queues) owned by the
        current user, left behind by a previous instance that didn't shut down
        cleanly. Stale shm causes "emcStatusBuffer invalid"; a stale *semaphore*
        left in a locked state is worse -- it makes the next rtapi_app hang for
        an unpredictable, sometimes very long time waiting on a mutex nothing
        will ever release. See clear_stale_shm.sh (kept standalone for manual use
        too) -- this just shells out to it rather than duplicating the logic."""
        script = Path(__file__).resolve().parent / "clear_stale_shm.sh"
        subprocess.run(["bash", str(script)], check=False,
                        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def wait_for_nml_ready(self, timeout: float = 30.0) -> None:
        """Block until linuxcnc.stat() can connect, or raise TimeoutError.

        IMPORTANT, found by trial and error: the very first linuxcnc.stat()/
        .poll() call made in a process, if made before the child's NML shared
        memory actually exists, seems to wedge something inside libnml for the
        rest of that process's life -- every later attempt in that same process
        kept failing with "emcStatusBuffer invalid", for 90+ seconds straight,
        while a brand new process connected on its very first try the instant
        the old one gave up. So: wait for hard evidence the child has gotten far
        enough (its "DUMMY DISPLAY MODULE" line in the log, which prints only
        after HAL/motion/task/io are already up) before constructing the first
        stat() object at all, in this or any other process that will reuse it.
        """
        import linuxcnc

        marker_deadline = time.monotonic() + timeout
        marker = b"DUMMY DISPLAY MODULE"
        while time.monotonic() < marker_deadline:
            if not self.is_running():
                raise RuntimeError(
                    f"linuxcnc process exited before NML came up; see {self.log_path}"
                )
            try:
                if marker in Path(self.log_path).read_bytes():
                    break
            except FileNotFoundError:
                pass
            time.sleep(0.1)
        else:
            raise TimeoutError(
                f"linuxcnc never reached its startup marker in {timeout}s; see {self.log_path}"
            )

        last_err = None
        attempts = 0
        s = linuxcnc.stat()
        poll_deadline = time.monotonic() + timeout
        while time.monotonic() < poll_deadline:
            attempts += 1
            try:
                s.poll()
                # The very first successful poll() can still return a half-populated
                # snapshot (axis_mask==0, linear_units==0.0) even though the NML
                # channel itself is up; found by comparing consecutive polls. One
                # more poll a moment later reliably has axis_mask set, so treat that
                # as the real readiness signal rather than "poll() didn't throw".
                if s.axis_mask != 0:
                    return
                time.sleep(0.25)
            except Exception as e:  # noqa: BLE001 - NML raises plain Exception while cold
                last_err = e
                time.sleep(0.25)
        raise TimeoutError(
            f"linuxcnc NML never became ready after {attempts} attempts in {timeout}s: "
            f"{last_err!r}; see {self.log_path}"
        )

    def __enter__(self) -> "LinuxCNCProcess":
        self.start()
        self.wait_for_nml_ready()
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        self.stop()

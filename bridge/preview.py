"""G-code preview: parses a program with LinuxCNC's own interpreter (the `gcode`
extension module) in a canon that just records the path instead of moving any
machine -- the same approach the Axis GUI's preview uses (see `rs274.glcanon`),
but without that module's hard OpenGL dependency (`_togl`), which doesn't import
headless. We reuse Axis's lightweight, non-GL mixins (`rs274.interpret`:
`Translated`, `ArcsToSegmentsMixin`, `StatMixin`) for the parts that matter
(offsets/rotation, arc subdivision, tool table lookups) and fall back to a no-op
for every canon callback we don't care about for path extraction (dwell,
coolant, message, spindle, ...) via `__getattr__`, rather than hand-reproducing
the full, large, and only loosely documented canon interface."""
from __future__ import annotations

import gcode
from rs274.interpret import Translated, ArcsToSegmentsMixin, StatMixin

from protocol import AXIS_X, AXIS_Y, AXIS_Z, MotionType, PreviewSegment

_ORIGIN = (0.0,) * 6


import os

_DEBUG = os.environ.get("PREVIEW_DEBUG") == "1"


class _NoOp:
    """Returned for any canon callback we didn't implement; swallows the call."""
    def __call__(self, *args, **kwargs):
        return None

    def __bool__(self):
        return False


_NOOP = _NoOp()


class PreviewCanon(Translated, ArcsToSegmentsMixin, StatMixin):
    def __init__(self, stat, parameter_file: str, random_toolchanger: bool = False):
        StatMixin.__init__(self, stat, random_toolchanger)
        # Read directly (not called) by the interpreter's own Init() to load/save
        # named parameters (#5220 etc) for ITS OWN throwaway interpreter instance
        # -- found by tracing an unlabelled AttributeError-turned-C-error through
        # __getattr__ debug logging; not documented anywhere we could find.
        self.parameter_file = parameter_file
        self.messages: list[str] = []
        self.segments: list[PreviewSegment] = []
        self._last = _ORIGIN
        self.feed_rate = 0.0
        self.line_number = 0
        self.lathe_diameter_mode = False
        self.min_extents = [0.0, 0.0, 0.0]
        self.max_extents = [0.0, 0.0, 0.0]

    def __getattr__(self, name):
        if _DEBUG:
            print(f"[preview] __getattr__ fallback for {name!r}")
        return _NOOP

    # -- the handful of callbacks we actually care about --
    def next_line(self, state) -> None:
        self.line_number = state.sequence_number

    def set_feed_rate(self, arg) -> None:
        self.feed_rate = arg * self._INCH_TO_MM  # see _record's comment on the unit quirk

    def set_plane(self, plane) -> None:
        self.plane = plane  # consumed by ArcsToSegmentsMixin.arc_feed


    def message(self, text) -> None:
        self.messages.append(text)

    def straight_traverse_translated(self, x, y, z, a, b, c, u, v, w) -> None:
        self._record(MotionType.RAPID, x, y, z, a, b, c)

    def straight_feed_translated(self, x, y, z, a, b, c, u, v, w) -> None:
        self._record(MotionType.LINEAR, x, y, z, a, b, c)

    def straight_arcsegments(self, segs) -> None:
        for seg in segs:
            self._record(MotionType.ARC_CW, *seg[:6])

    # Empirically determined (not found documented anywhere): gcode.parse()'s
    # throwaway Interp instance reports canon coordinates in inches whenever the
    # parsed program contains a G21, even though the machine (and our own INI)
    # is configured entirely in mm and the live running instance has no such
    # issue -- a file with NO units statement at all passes through unscaled.
    # Every real program here always opens with G21 (project convention is
    # metric-only), so we unconditionally scale back to mm rather than trying
    # to detect the interpreter's chosen units after the fact.
    _INCH_TO_MM = 25.4

    def _record(self, motion_type: int, x: float, y: float, z: float, a: float = 0.0, b: float = 0.0,
                c: float = 0.0) -> None:
        """Linear axes scaled back to mm (rotary ones are degrees either way)."""
        end = (x * self._INCH_TO_MM, y * self._INCH_TO_MM, z * self._INCH_TO_MM, a, b, c)
        start = self._last
        if _DEBUG:
            print(f"[preview] _record type={motion_type} from={start} to={end}")
        if end != start:
            self.segments.append(PreviewSegment(
                motion_type=motion_type, line_number=self.line_number, start=start, end=end,
                feed_rate=0.0 if motion_type == MotionType.RAPID else self.feed_rate,
            ))
        self._last = end

    def calc_extents(self) -> None:
        points = [s.start for s in self.segments] + [s.end for s in self.segments]
        if points:
            self.min_extents = [min(p[i] for p in points) for i in (AXIS_X, AXIS_Y, AXIS_Z)]
            self.max_extents = [max(p[i] for p in points) for i in (AXIS_X, AXIS_Y, AXIS_Z)]


def preview_program(stat, filename: str, config_dir: str) -> list[PreviewSegment]:
    """Parse `filename` and return the flat list of path segments in machine
    (program) coordinates. `stat` is a live, already-polled linuxcnc.stat() --
    used only for units/tool-table context, exactly as Axis's own preview does.

    Uses its own scratch parameter file (config_dir/preview.var) rather than the
    live instance's -- this parse runs a second, throwaway interpreter instance
    in our own process, and sharing the live one's parameter file would race
    with milltask writing to it for no benefit (preview never needs named
    parameters to match the live machine's)."""
    from pathlib import Path
    scratch_var = str(Path(config_dir) / "preview.var")
    canon = PreviewCanon(stat, parameter_file=scratch_var)
    # Deliberately NOT passing a G20/G21 init code: canon output came back
    # divided by 25.4 (inch<->mm mixup) whenever one was included, even a
    # "redundant" G21 matching the machine's already-mm TRAJ config, found by
    # testing variants side by side. The machine's configured units apply
    # correctly on their own; the program's own G21 (every real program should
    # have one) takes over from there regardless.
    init_codes = ["G90", "G8"]
    result, seq = gcode.parse(filename, canon, init_codes, "")
    if result > gcode.MIN_ERROR:
        raise RuntimeError(f"gcode preview parse failed at line {seq}, result={result}: "
                            f"{gcode.strerror(result)}")
    canon.calc_extents()
    return canon.segments

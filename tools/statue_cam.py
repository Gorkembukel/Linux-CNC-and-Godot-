"""Generates linuxcnc-config/nc_files/trunnion5_ozgurluk_heykeli.ngc: a stylised
Statue of Liberty carved from an 80 x 80 x 170 mm block on the Trunnion5Axis
machine (A tilt + C rotary table, no tool-centre-point control).

    python tools/statue_cam.py

The statue is a set of simple solids. Every pass is 3+2: the table tilts to
A90 so the tool looks at the statue from the side, C picks the side, and the
tool follows the surface seen from there (a height map). Roughing uses four
sides, finishing six, plus one pass from above for the crown and the flame.

Part frame, mm: z up through the block centre, z = 0 on the block's top face,
the statue faces -y. H = height above the statue's base plane (z = H - CARVE).
"""
from pathlib import Path

import numpy as np

BLOCK = 80.0          # block is BLOCK x BLOCK x 170; the bottom 30 mm stays as the clamping base
CARVE = 140.0         # height of the carved zone, from the block's top face down
PIVOT_Z = -120.0      # A axis centreline, must match the machine's A axis PivotMm
GRID = 0.4
HALF = 46.0           # half-width of the height maps, covers the block corners
SAFE_Z = 80.0

ROUGH_R, ROUGH_STEP, ROUGH_ALLOWANCE = 6.0, 5.0, 1.0
ROUGH_LEVELS = (25.0, 10.0)
FINISH_R, FINISH_STEP = 2.0, 1.5


def capsule(p, a, b, ra, rb=None):
    """Inside a rounded cone from a (radius ra) to b (radius rb)."""
    rb = ra if rb is None else rb
    a, b = np.array(a, float), np.array(b, float)
    ab = b - a
    t = np.clip(((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1] + (p[2] - a[2]) * ab[2]) / ab.dot(ab), 0.0, 1.0)
    d2 = sum((p[i] - (a[i] + ab[i] * t)) ** 2 for i in range(3))
    return d2 <= (ra + (rb - ra) * t) ** 2


def ellipsoid(p, c, r):
    return sum(((p[i] - c[i]) / r[i]) ** 2 for i in range(3)) <= 1.0


def box(p, lo, hi):
    return np.logical_and.reduce([(p[i] >= lo[i]) & (p[i] <= hi[i]) for i in range(3)])


def inside(x, y, h):
    """True where the statue is. h = height above the statue's base plane."""
    p = (x, y, h)
    sq = np.maximum(np.abs(x), np.abs(y))
    s = sq <= 31.0                                                  # plinth
    s &= h <= 7.0
    s |= (h >= 7.0) & (h <= 44.0) & (sq <= 23.0 - 3.0 * (h - 7.0) / 37.0)   # pedestal shaft
    s |= (h >= 44.0) & (h <= 50.0) & (sq <= 24.5)                   # cornice

    k = np.clip((h - 50.0) / 41.0, 0.0, 1.0)                        # robe, with pleats near the hem
    pleat = 1.0 + 0.06 * (1.0 - k) * np.cos(8.0 * np.arctan2(y, x))
    s |= (h >= 50.0) & (h <= 91.0) & ((x / ((15.5 - 5.0 * k) * pleat)) ** 2 + (y / ((13.0 - 5.0 * k) * pleat)) ** 2 <= 1.0)
    s |= ellipsoid(p, (0, 0, 91), (10.5, 8, 4))                     # shoulders
    s |= capsule(p, (0, 0, 91), (0, 0, 98), 4.0)                    # neck
    s |= ellipsoid(p, (0, -0.5, 103), (6, 6.5, 7.5))                # head
    s |= (h >= 107.0) & (h <= 109.5) & (x ** 2 + y ** 2 <= 7.3 ** 2)   # crown band
    for deg in range(-72, 73, 24):                                  # seven rays, fanned left to right
        a = np.radians(deg)
        tip = (13.0 * np.sin(a), 0.0, 108.5 + 13.0 * np.cos(a))
        s |= capsule(p, (5.0 * np.sin(a), 0.0, 108.5 + 5.0 * np.cos(a)), tip, 1.7, 0.6)

    s |= capsule(p, (-8.5, 0, 89), (-17, 0, 113), 3.4, 3.0)         # raised right arm
    s |= capsule(p, (-17, 0, 113), (-18, 0, 121), 3.0, 2.6)
    s |= capsule(p, (-18, 0, 119), (-18, 0, 127), 2.2)              # torch handle
    s |= (h >= 127.0) & (h <= 129.5) & ((x + 18.0) ** 2 + y ** 2 <= 25.0)   # torch cup
    s |= ellipsoid(p, (-18, 0, 133.5), (3.2, 3.2, 5.0))             # flame

    s |= capsule(p, (9, 0, 88), (12, -4, 78), 3.2, 3.0)             # left arm holding the tablet
    s |= box(p, (7.5, -9.5, 71.0), (14.5, -6.5, 88.0))              # tablet
    return s


def side_height_map(c_deg):
    """For a tool looking along -u at table angle C: how far from the centre
    plane the statue reaches, per (H row, w column). u = (sin C, -cos C)."""
    c = np.radians(c_deg)
    t, u = (np.cos(c), np.sin(c)), (np.sin(c), -np.cos(c))
    w = np.arange(-HALF, HALF + GRID / 2, GRID)
    d = np.arange(HALF, -HALF - GRID / 2, -GRID)
    hs = np.arange(0.0, CARVE + GRID / 2, GRID)
    ww, dd = np.meshgrid(w, d, indexing="ij")
    x, y = ww * t[0] + dd * u[0], ww * t[1] + dd * u[1]
    out = np.full((len(hs), len(w)), -HALF)
    for i, h in enumerate(hs):
        solid = inside(x, y, np.full_like(x, h))
        first = solid.argmax(axis=1)
        out[i] = np.where(solid.any(axis=1), d[first], -HALF)
    return out


def top_height_map():
    """Statue height H seen from above, per (y row, x column)."""
    x = np.arange(-HALF, HALF + GRID / 2, GRID)
    hs = np.arange(CARVE, 90.0, -GRID)
    yy, xx = np.meshgrid(x, x, indexing="ij")
    out = np.zeros(xx.shape)
    done = np.zeros(xx.shape, bool)
    for h in hs:
        solid = inside(xx, yy, np.full_like(xx, h)) & ~done
        out[solid] = h
        done |= solid
    return out


def tool_envelope(height, radius, ball):
    """Lowest tip position that keeps a tool of this radius off the surface."""
    n = int(round(radius / GRID))
    out = np.full(height.shape, -1e9)
    padded = np.pad(height, n, constant_values=-1e9)
    for di in range(-n, n + 1):
        for dj in range(-n, n + 1):
            r2 = (di * GRID) ** 2 + (dj * GRID) ** 2
            if r2 > radius * radius:
                continue
            lift = np.sqrt(radius * radius - r2) - radius if ball else 0.0
            out = np.maximum(out, padded[n + di:n + di + height.shape[0], n + dj:n + dj + height.shape[1]] + lift)
    return out


def simplify(points, tolerance=0.02):
    """Keeps only the points needed to stay within tolerance of the path."""
    pts = np.array(points, float)
    kept, start = [0], 0
    while start < len(pts) - 1:
        end = start + 1
        while end + 1 < len(pts):
            a, b = pts[start], pts[end + 1]
            ab = b - a
            length = np.hypot(*ab)
            mid = pts[start + 1:end + 1] - a
            off = np.abs(ab[0] * mid[:, 1] - ab[1] * mid[:, 0]) / length if length > 1e-9 else np.hypot(mid[:, 0], mid[:, 1])
            if off.max() > tolerance:
                break
            end += 1
        kept.append(end)
        start = end
    return [tuple(pts[i]) for i in kept]


class Program:
    def __init__(self):
        self.lines = []

    def add(self, text):
        self.lines.append(text)

    def side_rows(self, c_deg, depth, tool_r, row_step, floor, feed, levels=(None,)):
        """Zigzag rows across the statue at table angle C. Machine axes at
        A90: X = across, Y = H - CARVE - PIVOT_Z, Z = PIVOT_Z + depth."""
        w = np.arange(-HALF, HALF + GRID / 2, GRID)
        stock_edge = BLOCK / 2 * (abs(np.sin(np.radians(c_deg))) + abs(np.cos(np.radians(c_deg))))
        self.add(f"G0 Z{SAFE_Z:.1f}")
        self.add(f"G0 A90.0 C{c_deg:.1f}")
        forward = True
        above = stock_edge
        for level in levels:
            # The tool centre stays a radius above the base plane so the
            # clamping base below it is never touched.
            for h in np.arange(tool_r, CARVE + row_step, row_step):
                row = depth[min(int(round(h / GRID)), depth.shape[0] - 1)]
                target = np.maximum(row, floor)
                if level is not None:
                    target = np.maximum(target, level)
                    work = np.nonzero(target < above - 0.05)[0]
                else:
                    work = np.nonzero(row > floor + 0.01)[0]
                if len(work) == 0:
                    continue
                lo, hi = max(work[0] - 8, 0), min(work[-1] + 8, len(w) - 1)
                pts = simplify([(w[j], target[j]) for j in range(lo, hi + 1)])
                if not forward:
                    pts.reverse()
                forward = not forward
                y = h - CARVE - PIVOT_Z
                self.add(f"G0 X{pts[0][0]:.3f} Y{y:.3f}")
                self.add(f"G0 Z{PIVOT_Z + stock_edge + 3.0:.3f}")
                self.add(f"G1 Z{PIVOT_Z + pts[0][1]:.3f} F{feed / 3:.0f}")
                self.add(f"G1 X{pts[1][0]:.3f} Z{PIVOT_Z + pts[1][1]:.3f} F{feed:.0f}")
                for x, d in pts[2:]:
                    self.add(f"X{x:.3f} Z{PIVOT_Z + d:.3f}")
                self.add(f"G0 Z{PIVOT_Z + stock_edge + 3.0:.3f}")
            if level is not None:
                above = level
        self.add(f"G0 Z{SAFE_Z:.1f}")

    def top_rows(self, height, feed):
        """Rows over the head, crown and flame with the table level (A0)."""
        x = np.arange(-HALF, HALF + GRID / 2, GRID)
        self.add(f"G0 Z{SAFE_Z:.1f}")
        self.add("G0 A0.0 C0.0")
        forward = True
        for yv in np.arange(-14.0, 14.01, FINISH_STEP):
            row = height[int(round((yv + HALF) / GRID))]
            work = np.nonzero(row > 100.0)[0]
            if len(work) == 0:
                continue
            lo, hi = max(work[0] - 6, 0), min(work[-1] + 6, len(x) - 1)
            pts = simplify([(x[j], max(row[j], 100.0) - CARVE) for j in range(lo, hi + 1)])
            if not forward:
                pts.reverse()
            forward = not forward
            self.add(f"G0 X{pts[0][0]:.3f} Y{yv:.3f}")
            self.add("G0 Z3.0")
            self.add(f"G1 Z{pts[0][1]:.3f} F{feed / 3:.0f}")
            self.add(f"G1 X{pts[1][0]:.3f} Z{pts[1][1]:.3f} F{feed:.0f}")
            for xv, z in pts[2:]:
                self.add(f"X{xv:.3f} Z{z:.3f}")
            self.add("G0 Z3.0")
        self.add(f"G0 Z{SAFE_Z:.1f}")


def main():
    prog = Program()
    for line in (
        "%",
        "( 5 EKSEN - OZGURLUK HEYKELI )",
        "( Makine: Trunnion5Axis. Bu dosya tools/statue_cam.py ile uretilir, elle duzenlemeyin )",
        "( Kutuk: 80 x 80 x 170 mm aluminyum blok. X/Y sifiri ortada, Z0 ust yuzey )",
        "( Ustten 140 mm islenir, alttaki 30 mm baglama payi olarak kalir )",
        "( A ekseni ust yuzeyin 120 mm altindan gecer. Takim ucu merkez kontrolu yok )",
        "( T1 = 12 mm parmak freze, kaba. T2 = 4 mm kure freze, ince )",
        "",
        "G21 G17 G90 G54",
        "G64 P0.03",
        "",
        "( --- KABA: dort yandan, 15 mm kademelerle, yuzeyde 1 mm pay --- )",
        "T1 M6",
        "G43",
        "S9000 M3",
    ):
        prog.add(line)

    maps = {}
    for c in (0, 90, 180, 270):
        maps[c] = side_height_map(c)
        rough = tool_envelope(maps[c], ROUGH_R, ball=False) + ROUGH_ALLOWANCE
        prog.add(f"( kaba, C{c} )")
        prog.side_rows(c, rough, ROUGH_R, ROUGH_STEP, floor=-0.5, feed=6000, levels=ROUGH_LEVELS + (-HALF,))
    prog.add("M5")

    prog.add("")
    prog.add("( --- INCE: alti yandan, 1.5 mm adim --- )")
    prog.add("T2 M6")
    prog.add("G43")
    prog.add("S16000 M3")
    for c in (0, 60, 120, 180, 240, 300):
        height = maps[c] if c in maps else side_height_map(c)
        prog.add(f"( ince, C{c} )")
        prog.side_rows(c, tool_envelope(height, FINISH_R, ball=True), FINISH_R, FINISH_STEP, floor=-0.5, feed=6000)

    prog.add("( ince, ustten: tac ve mesale )")
    prog.top_rows(tool_envelope(top_height_map(), FINISH_R, ball=True), feed=3000)

    for line in ("M5", "G0 A0.0 C0.0", "G0 X0.0 Y0.0", "M2", "%"):
        prog.add(line)

    out = Path(__file__).resolve().parent.parent / "linuxcnc-config" / "nc_files" / "trunnion5_ozgurluk_heykeli.ngc"
    out.write_text("\n".join(prog.lines) + "\n", newline="\n")
    print(f"{out}: {len(prog.lines)} lines")


if __name__ == "__main__":
    main()

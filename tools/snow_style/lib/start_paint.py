"""Painters for the start post's atlases (psxbake): painter(Pp, Nn, Z, ao, aa) -> (rgb_linear[n, 3], groove[n] or None).
The generator shed's finishes and wear language at the post's positions, one step further gone: the booth in a faded olive
board-and-batten (chalky where the weather hits, grey wood low down, moss at the foot, rust runs from the roof), white trims gone
grey, the corrugated roof with rust and a carpet of needles; the car an oxblood estate dulled to a chalky pink-brown on the
tops, rust at the arches, sills and the tailgate, mud from below, needles and leaves on the roof, hood and the scuttle; inside:
mould on the fabric, a sagging headliner. StartProps: the yellow tripod light, its battery box, old tyres, the rubbish.
Positions: the roots stand at start_kit.DISPLAY while baked (the booth 7 m to -x, the car at the origin)."""
import numpy as np
from noise import ss, mixc, fbm, dist_periodic, groove as gline
from psxbake import lin, boards, hash1
from gen_paint import DUST, RUST, RUST_D, _T, _common, run_m
import gen_kit as K
import start_kit as SK

OLIVE, OLIVE_FADE, OLIVE_WORN, OLIVE_STREAK = lin(92, 98, 66), lin(140, 142, 112), lin(124, 116, 98), lin(52, 56, 40)
MOSS, MOSS_D, NEEDLE_A, NEEDLE_B, LEAF = lin(70, 86, 40), lin(46, 58, 30), lin(132, 92, 46), lin(96, 70, 40), lin(150, 96, 44)
OX, OX_FADE, OX_CHALK = lin(112, 34, 30), lin(150, 90, 84), lin(168, 128, 118)
MUD, MUD_D = lin(92, 78, 60), lin(58, 48, 38)
BO = SK.DISPLAY['StartBooth']

DRAFT_COL = {   # sRGB, roughly the painters' base colours (start_build --draft)
    K.Z_SLAB: (132, 130, 124), SK.Z_PADC: (132, 130, 124), K.Z_FLOOR: (120, 104, 82), K.Z_CLAD: (92, 98, 66),
    K.Z_TRIMW: (200, 196, 182), K.Z_LINING: (178, 150, 108), K.Z_TIMBER: (150, 118, 78), K.Z_ROOF: (150, 154, 150),
    K.Z_ROOFUNDER: (118, 120, 116), K.Z_DOOR: (92, 98, 66), K.Z_DOORIN: (146, 118, 82), K.Z_STEELBLK: (36, 36, 36),
    K.Z_GALV: (150, 154, 152), SK.Z_DESK: (170, 140, 96),
    SK.Z_PAINT: (112, 34, 30), SK.Z_CBLACK: (36, 36, 38), SK.Z_UNDER: (40, 34, 28), SK.Z_TYRE: (30, 30, 30), SK.Z_RIM: (150, 150, 146),
    SK.Z_CHROME: (170, 170, 168), SK.Z_HEAD: (200, 204, 196), SK.Z_TAIL: (170, 40, 30), SK.Z_SEAT: (120, 98, 70),
    SK.Z_DASH: (40, 40, 42), SK.Z_CARPET: (60, 48, 38), SK.Z_LINER: (176, 166, 140), SK.Z_CARD: (96, 86, 72),
    SK.Z_GRILLE: (28, 28, 30), SK.Z_PLATE: (40, 38, 34), SK.Z_CARGO: (32, 32, 32), SK.Z_GAUGE: (20, 20, 20), SK.Z_RUBBER: (22, 22, 22),
    SK.Z_TRIPOD: (220, 170, 30), SK.Z_LHOUSE: (40, 40, 42), SK.Z_BATT: (96, 108, 116), SK.Z_TERM: (180, 30, 24),
    SK.Z_CABLE: (24, 24, 24), SK.Z_PTYRE: (34, 34, 34), SK.Z_BAG: (20, 22, 24), SK.Z_PLANK: (128, 118, 100),
    SK.Z_CARDB: (150, 116, 76), SK.Z_CAN: (150, 60, 40), SK.Z_BOTTLE: (70, 46, 22), SK.Z_RUSTB: (110, 58, 32),
    SK.Z_FOOT: (26, 26, 26), SK.Z_HANDLE: (150, 154, 152)}


def _litter(Pp, up, seed, amount=1.0):
    """needles and leaves on the surfaces that face up: (mask 0..1, colour)"""
    x, y = Pp[:, 0], Pp[:, 1]
    m = (ss(0.52, 0.72, fbm(Pp, 5.0, 3, seed=seed)) * 0.7 + ss(0.62, 0.80, fbm(Pp, 30.0, 1, seed=seed + 1)) * 0.6) * up * amount
    pick = hash1(np.floor(x * 53.0) + np.floor(y * 47.0) * 13.0, seed)
    c = np.where((pick > 0.70)[:, None], LEAF, np.where((pick > 0.35)[:, None], NEEDLE_A, NEEDLE_B))
    return np.clip(m, 0, 1), c


# ================================================================ the Start atlas: the booth and the car
def paint_start(Pp, Nn, Z, ao, aa):
    x, y, z = Pp[:, 0], Pp[:, 1], Pp[:, 2]
    n = len(x)
    n_lo, n_mid, n_hi, up, cav, edge = _common(Pp, Nn, ao, 801)
    T = _T(n_lo, n_hi)
    M = lambda zz: (Z == zz)
    col = np.tile(lin(128, 128, 128), (n, 1))
    gr = np.zeros(n, np.float32)
    bx, by = x - BO.x, y - BO.y                                       # booth-local
    zf = z - SK.FLOOR
    streak = ss(0.55, 0.80, fbm(Pp * np.array([1, 1, 0.12], np.float32), 6.0, 3, seed=811))
    lit, lit_c = _litter(Pp, up, 812)
    # ---- the pad: stained concrete, moss in the joints and at the walls, a carpet of needles
    m = M(SK.Z_PADC) | M(K.Z_SLAB)
    c = T(lin(132, 130, 124), 0.16, 0.22)
    c = mixc(c, lin(86, 82, 76), ss(0.10, -0.10, z) * 0.6 + cav * 0.4)
    moss = ss(0.55, 0.78, fbm(Pp, 9.0, 3, seed=813)) * (0.4 + 0.6 * cav)
    c = mixc(c, MOSS, moss * 0.65)
    c = mixc(c, lit_c, lit * 0.85)
    col[m] = c[m]
    gr = np.where(m, gline(dist_periodic(bx, 1.25, 0.0), 0.006, aa) * up * 0.6, gr)
    # ---- outside: board-and-batten in a faded olive
    m = M(K.Z_CLAD)
    fb = np.abs(Nn[:, 1]) > 0.5
    s_wall = np.where(fb, bx, by)
    bi, seam = boards(s_wall, 0.18, aa, 0.004)
    bat = 1 - ss(0.022 - aa, 0.022 + aa, dist_periodic(s_wall, 0.18))
    tone = 0.90 + 0.16 * hash1(bi + 7.0 * fb, 3)
    c = OLIVE * tone[:, None] * (0.86 + 0.20 * (n_lo - 0.5) + 0.10 * (n_hi - 0.5))[:, None]
    weather = np.where(Nn[:, 1] < -0.5, 1.0, np.where(Nn[:, 0] > 0.5, 0.7, 0.45))
    c = mixc(c, OLIVE_FADE, weather * ss(0.30, 0.70, n_lo) * 0.45)
    bare = ss(0.58, 0.80, fbm(Pp * np.array([1, 1, 0.3], np.float32), 14.0, 3, seed=814)) * (0.35 + 0.65 * ss(0.7, 0.10, zf))
    c = mixc(c, OLIVE_WORN, bare * (0.60 + 0.35 * weather))
    c = mixc(c, OLIVE_STREAK, streak * 0.45)
    c = mixc(c, lin(70, 40, 26), np.exp(-((by - SK.BULK.y) / 0.06) ** 2) * (bx > SK.X1 - 0.01) * ss(2.10, 1.5, z) * 0.55)
    c = mixc(c, MOSS_D, ss(0.40, 0.02, zf) * ss(0.45, 0.70, fbm(Pp, 12.0, 2, seed=815)) * 0.8)
    c = mixc(c, lin(56, 50, 42), ss(0.40, 0.06, zf) * 0.45)
    c = c * (1 - 0.10 * bat * ss(0.3, 0.9, n_mid))[:, None] * (1 + 0.08 * bat)[:, None]
    col[m] = c[m]
    edge_b = np.maximum(gline(dist_periodic(s_wall - 0.022, 0.18), 0.006, aa), gline(dist_periodic(s_wall + 0.022, 0.18), 0.006, aa))
    gr = np.where(m, edge_b * 0.8, gr)
    # ---- the door outside: olive boards, worn at the lever and the foot
    m = M(K.Z_DOOR)
    bi, seam = boards(by, 0.12, aa, 0.004)
    c = OLIVE * (0.90 + 0.16 * hash1(bi, 5))[:, None] * (0.86 + 0.18 * (n_lo - 0.5) * 2)[:, None]
    c = mixc(c, OLIVE_WORN, ss(0.58, 0.82, fbm(Pp * np.array([1, 1, 0.3], np.float32), 14.0, 3, seed=820)) * ss(0.8, 0.15, zf) * 0.9)
    c = mixc(c, lin(62, 54, 44), ss(0.40, 0.11, zf) * 0.6)
    c = mixc(c, lin(46, 52, 40), np.exp(-(((by - SK.LEAF['y0'] - 0.12) / 0.10) ** 2 + ((z - 1.02) / 0.14) ** 2)) * 0.6)
    c = mixc(c, OLIVE_STREAK, streak * 0.35)
    col[m] = c[m]
    gr = np.where(m, seam * 0.8, gr)
    m = M(K.Z_DOORIN)
    bi, seam = boards(by, 0.12, aa, 0.004)
    c = lin(140, 114, 80) * (0.90 + 0.16 * hash1(bi, 6))[:, None] * (0.84 + 0.2 * (n_lo - 0.5) * 2)[:, None]
    c = mixc(c, lin(88, 70, 52), cav * 0.4 + ss(0.5, 0.12, zf) * 0.4)
    col[m] = c[m]
    gr = np.where(m, seam * 0.7, gr)
    # ---- white trims gone grey, peeling
    m = M(K.Z_TRIMW)
    c = T(lin(200, 196, 182), 0.10, 0.12)
    c = mixc(c, lin(122, 114, 98), np.clip(edge * 0.9 + ss(0.62, 0.84, fbm(Pp, 18.0, 2, seed=821)) * 0.8, 0, 0.95))
    c = mixc(c, lin(90, 84, 72), ss(0.35, 0.08, zf) * 0.6 + cav * 0.35)
    c = mixc(c, lin(130, 122, 104), streak * 0.35)
    col[m] = c[m]
    # ---- inside: ply lining, the floor boards (trodden path door -> desk), raw spruce, the desk and the shelf
    m = M(K.Z_LINING)
    c = T(lin(172, 144, 104), 0.14, 0.16)
    grain = fbm(Pp * np.array([3.0, 3.0, 0.4], np.float32), 9.0, 2, seed=822)
    c = c * (0.90 + 0.14 * grain)[:, None]
    c = mixc(c, lin(90, 76, 58), ss(0.9, 0.2, zf) * 0.5 * (0.6 + 0.4 * n_mid))
    c = mixc(c, lin(70, 66, 56), ss(0.70, 0.86, fbm(Pp, 6.0, 3, seed=823)) * 0.45)          # damp stains
    col[m] = c[m]
    gr = np.where(m, gline(dist_periodic(np.where(fb, bx, by), 1.22, 0.0), 0.005, aa) * 0.7, gr)
    m = M(K.Z_FLOOR)
    bi, seam = boards(by, 0.14, aa, 0.004)
    c = lin(124, 106, 82) * (0.86 + 0.18 * hash1(bi, 9))[:, None] * (0.86 + 0.2 * (n_lo - 0.5) * 2)[:, None]
    path = np.exp(-(((bx - 0.45) / 0.35) ** 2)) * ss(0.6, -0.3, by) + np.exp(-(((by + 0.55) / 0.30) ** 2)) * ss(0.9, -0.4, bx) * 0.7
    c = mixc(c, lin(78, 66, 52), np.clip(path, 0, 1) * 0.55)
    c = mixc(c, lit_c, ss(0.70, 0.85, fbm(Pp, 9.0, 2, seed=824)) * 0.55)
    col[m] = c[m]
    gr = np.where(m, seam * 0.8, gr)
    m = M(K.Z_TIMBER)
    vert = np.abs(Nn[:, 2]) < 0.6
    g2 = fbm(Pp * np.where(vert[:, None], np.array([4.0, 4.0, 0.25], np.float32), np.array([0.3, 4.0, 4.0], np.float32)), 10.0, 2,
             seed=825)
    c = T(lin(142, 112, 76), 0.16, 0.12) * (0.86 + 0.22 * g2)[:, None]
    c = mixc(c, lin(84, 66, 48), cav * 0.55 + ss(0.6, 0.9, n_mid) * 0.25)
    c = mixc(c, DUST, up * 0.25)
    col[m] = c[m]
    m = M(SK.Z_DESK)
    c = T(lin(170, 140, 96), 0.10, 0.10)
    c = mixc(c, lin(110, 84, 56), edge * 0.6 + ss(0.65, 0.85, fbm(Pp, 14.0, 2, seed=826)) * 0.4)
    c = mixc(c, lin(60, 48, 36), np.exp(-(((bx + 0.30) / 0.12) ** 2 + ((by - SK.DESK[2] - 0.25) / 0.08) ** 2)) * up * 0.5)   # a mug ring
    c = mixc(c, DUST, up * 0.30)
    col[m] = c[m]
    # ---- the roof: corrugated sheet with rust runs, needles in the troughs; its underside
    for zz, top in ((K.Z_ROOF, True), (K.Z_ROOFUNDER, False)):
        m = M(zz)
        ph = np.cos(2 * np.pi * bx / 0.076)
        c = T(lin(146, 150, 146) if top else lin(114, 116, 112), 0.18, 0.20) * (0.82 + 0.18 * ph)[:, None]
        run = ss(0.52, 0.78, fbm(Pp * np.array([1, 0.15, 0.15], np.float32), 7.0, 3, seed=827))
        c = mixc(c, RUST if top else RUST_D, run * (0.85 if top else 0.45))
        if top:
            trough = ss(0.2, -0.6, ph)
            l2, lc2 = _litter(Pp, np.ones(n, np.float32), 828, 1.0)
            c = mixc(c, lc2, np.clip(l2 * (0.5 + 0.5 * trough), 0, 1) * 0.9)
            c = mixc(c, MOSS, ss(0.62, 0.82, fbm(Pp, 6.0, 2, seed=829)) * ss(0.5, 1.2, by) * 0.6)
        col[m] = c[m]
        gr = np.where(m, gline(dist_periodic(bx, 0.76, 0.38), 0.010, aa) * 0.8, gr)
    m = M(K.Z_STEELBLK)
    c = T(lin(36, 36, 36), 0.12, 0.2)
    c = mixc(c, RUST, np.clip(edge * 0.8 + ss(0.66, 0.84, fbm(Pp, 30.0, 2, seed=830)) * 0.6, 0, 0.9))
    col[m] = c[m]
    m = M(K.Z_GALV)
    spang = fbm(Pp, 90.0, 1, seed=831)
    c = T(lin(146, 150, 148), 0.10, 0.08) * (0.88 + 0.22 * spang)[:, None]
    c = mixc(c, RUST, run_m(Pp) * 0.55)
    col[m] = c[m]

    # ================================================================ the car
    # ---- paint: oxblood dulled to a chalky pink-brown on the tops, rust at the arches, sills and edges, mud from below
    m = M(SK.Z_PAINT)
    c = OX * (0.88 + 0.16 * (n_lo - 0.5) + 0.08 * (n_hi - 0.5))[:, None]
    c = mixc(c, OX_FADE, ss(0.15, 0.85, up) * (0.55 + 0.35 * n_lo))
    c = mixc(c, OX_CHALK, ss(0.6, 0.95, up) * ss(0.45, 0.80, fbm(Pp, 4.0, 3, seed=840)) * 0.6)
    arch = np.zeros(n, np.float32)
    for yc in (SK.YFA, SK.YRA):
        d = np.hypot(y - yc, z - SK.RW)
        arch = np.maximum(arch, ss(SK.RA + 0.10, SK.RA + 0.01, d) * (np.abs(x) > SK.CW - 0.06))
    sill = ss(0.42, 0.30, z) * (np.abs(x) > SK.CW - 0.06)
    tail = ss(0.70, 0.45, z) * (y > 2.15)
    rust_mask = np.clip(arch + sill + tail * 0.8 + edge * 0.5, 0, 1) * ss(0.35, 0.65, fbm(Pp, 22.0, 3, seed=841))
    bubbles = ss(0.80, 0.90, fbm(Pp, 55.0, 2, seed=842)) * (0.3 + 0.7 * ss(0.9, 0.4, z))
    c = mixc(c, RUST, np.clip(rust_mask * 1.2 + bubbles * 0.5, 0, 1))
    c = mixc(c, RUST_D, np.clip(rust_mask - 0.55, 0, 1) * 1.5)
    c = mixc(c, MUD, ss(0.62, 0.30, z) * (0.5 + 0.5 * ss(0.3, 0.7, fbm(Pp, 8.0, 2, seed=843))) * 0.85)
    c = mixc(c, MUD_D, ss(0.40, 0.28, z) * 0.6)
    c = mixc(c, lin(70, 60, 50), streak * 0.30 * (1 - up))
    c = mixc(c, lit_c, lit * 0.95)
    # years of needles: a mat in the scuttle and along the windscreen foot, drifts on the roof and the hood, dark tide marks
    cowl = np.exp(-(((y - SK.Y_WS + 0.10) / 0.16) ** 2)) * (z > 0.78)
    heavy = ss(0.30, 0.62, fbm(Pp, 7.0, 3, seed=845)) * ss(0.35, 0.95, up) * (0.45 + 0.55 * np.clip(cowl * 2.0, 0, 1))
    pick = hash1(np.floor(x * 61.0) + np.floor(y * 59.0) * 17.0, 846)
    ndl = np.where((pick > 0.55)[:, None], NEEDLE_A, np.where((pick > 0.25)[:, None], NEEDLE_B, lin(66, 50, 32)))
    c = mixc(c, lin(70, 62, 52), ss(0.2, 0.6, heavy) * 0.45)
    c = mixc(c, ndl, np.clip(heavy * 1.15, 0, 0.95))
    c = mixc(c, MOSS, ss(0.70, 0.86, fbm(Pp, 10.0, 2, seed=844)) * up * ss(1.2, 1.38, z) * 0.6)
    col[m] = c[m]
    # door / panel seams (grooves): front door, rear doors, the tailgate, the hood, the fuel flap
    side = np.abs(x) > SK.CW - 0.02
    seams = np.zeros(n, np.float32)
    for yy in (SK.DOORC['y0'], SK.DOORC['y1'] + 0.01, SK.C_PIL[0] - 0.02):
        seams = np.maximum(seams, gline(np.abs(y - yy), 0.004, aa) * side * (z > 0.34) * (z < 1.36))
    seams = np.maximum(seams, gline(np.abs(z - 0.36), 0.004, aa) * side * (y > SK.DOORC['y0']) * (y < SK.C_PIL[0]))
    seams = np.maximum(seams, gline(np.abs(y + 0.80), 0.004, aa) * (Nn[:, 2] > 0.8) * (z > 0.79))       # hood rear edge
    seams = np.maximum(seams, gline(np.abs(np.abs(x) - 0.78), 0.004, aa) * (Nn[:, 2] > 0.8) * (y < -0.80) * (z > 0.79))
    seams = np.maximum(seams, gline(np.abs(z - 0.86), 0.004, aa) * (y > 2.2))
    fuel = (np.abs(y - 1.75) < 0.08) & (np.abs(z - 0.74) < 0.07) & (x < -SK.CW + 0.02)
    seams = np.maximum(seams, gline(np.minimum(np.abs(np.abs(y - 1.75) - 0.08), np.abs(np.abs(z - 0.74) - 0.07)), 0.003, aa) * fuel)
    gr = np.where(m, seams * 0.9, gr)
    # ---- black plastics faded grey, the window rubbers
    for zz, base in ((SK.Z_CBLACK, lin(38, 38, 40)), (SK.Z_RUBBER, lin(24, 24, 24))):
        m = M(zz)
        c = T(base, 0.12, 0.16)
        c = mixc(c, lin(96, 94, 90), up * ss(0.4, 0.8, n_lo) * 0.5)
        c = mixc(c, MUD, ss(0.55, 0.30, z) * 0.6)
        c = mixc(c, lit_c, lit * 0.6)
        col[m] = c[m]
    m = M(SK.Z_UNDER)
    c = T(lin(44, 38, 32), 0.15, 0.2)
    c = mixc(c, RUST_D, ss(0.6, 0.85, fbm(Pp, 12.0, 2, seed=845)) * 0.7)
    c = mixc(c, MUD_D, np.full(n, 0.4, np.float32))
    col[m] = c[m]
    # ---- wheels: tyres grey with dirt, the steel rims with their hub caps rusted
    m = M(SK.Z_TYRE)
    c = T(lin(32, 32, 32), 0.12, 0.18)
    c = mixc(c, MUD, ss(0.18, 0.02, z) * 0.8 + ss(0.7, 0.9, fbm(Pp, 20.0, 2, seed=846)) * 0.3)
    c = mixc(c, lin(70, 70, 68), ss(0.4, 0.6, fbm(Pp * 3, 30.0, 1, seed=847)) * 0.25)
    col[m] = c[m]
    m = M(SK.Z_RIM)
    c = T(lin(150, 150, 146), 0.12, 0.16)
    c = mixc(c, RUST, ss(0.5, 0.75, fbm(Pp, 30.0, 2, seed=848)) * 0.8 + cav * 0.4)
    col[m] = c[m]
    m = M(SK.Z_CHROME)
    c = T(lin(168, 168, 166), 0.10, 0.2)
    c = mixc(c, RUST, ss(0.5, 0.8, fbm(Pp, 40.0, 2, seed=849)) * 0.7)
    col[m] = c[m]
    # ---- lamps: the headlamps' fluted glass (dirty), the tail lights red over amber, the grille, the empty plate
    m = M(SK.Z_HEAD)
    flute = 0.85 + 0.15 * (np.mod(np.floor(x / 0.012), 2))
    c = lin(190, 196, 188) * flute[:, None] * (0.75 + 0.25 * n_hi)[:, None]
    c = mixc(c, MUD, ss(0.62, 0.55, z) * 0.6 + ss(0.6, 0.8, n_mid) * 0.35)
    col[m] = c[m]
    m = M(SK.Z_TAIL)
    c = np.where((z > 0.58)[:, None], lin(156, 30, 24), np.where((z > 0.52)[:, None], lin(196, 120, 30), lin(176, 172, 160)))
    c = c * (0.80 + 0.2 * (np.mod(np.floor(z / 0.01), 2)))[:, None]
    c = mixc(c, MUD, 0.35 + ss(0.6, 0.85, n_mid) * 0.3)
    col[m] = c[m]
    m = M(SK.Z_GRILLE)
    bars = np.mod(np.floor((z - 0.48) / 0.022), 2)
    c = np.where((bars > 0)[:, None], lin(52, 52, 54), lin(14, 14, 16)) * (0.9 + 0.1 * n_hi)[:, None]
    col[m] = c[m]
    gr = np.where(m, gline(dist_periodic(z - 0.48, 0.022), 0.003, aa) * 0.6, gr)
    m = M(SK.Z_PLATE)
    holes = (np.abs(np.abs(x) - 0.17) < 0.008) & (np.abs(z - 0.555) < 0.008)
    c = T(lin(52, 48, 42), 0.1, 0.1)
    c = mixc(c, lin(132, 120, 100), (1 - ss(0.0, 0.015, np.minimum(np.abs(np.abs(x) - 0.26), np.abs(np.abs(z - 0.55) - 0.07)))) * 0.6)
    c[holes] = lin(10, 10, 10)
    col[m] = c[m]
    # ---- inside: fabric with mould, the dash cracked, carpet with leaves, the headliner sagging, vinyl door cards
    m = M(SK.Z_SEAT)
    weave = 0.9 + 0.1 * np.mod(np.floor(x / 0.006) + np.floor(z / 0.006), 2)
    c = T(lin(124, 100, 72), 0.12, 0.14) * weave[:, None]
    c = mixc(c, lin(70, 74, 56), ss(0.62, 0.82, fbm(Pp, 9.0, 3, seed=850)) * 0.6)             # mould
    c = mixc(c, lin(160, 150, 120), ss(0.75, 0.9, fbm(Pp, 20.0, 2, seed=851)) * 0.4)
    col[m] = c[m]
    m = M(SK.Z_DASH)
    c = T(lin(42, 42, 44), 0.12, 0.16)
    crack = gline(np.abs(fbm(Pp, 6.0, 2, seed=852) - 0.5), 0.004, aa) * up
    c = mixc(c, lin(90, 88, 84), up * 0.35 + crack * 0.6)
    c = mixc(c, DUST, up * 0.25)
    col[m] = c[m]
    m = M(SK.Z_GAUGE)
    col[m] = lin(18, 18, 18)
    for xc in (0.30, 0.46):
        d = np.hypot(x - xc, z - 0.795)
        ring = (d > 0.040) & (d < 0.046)
        face_ = d < 0.040
        col[m & face_] = lin(26, 26, 26)
        col[m & ring] = lin(150, 150, 146)
        tick = face_ & (d > 0.030) & (np.mod(np.floor(np.degrees(np.arctan2(z - 0.795, x - xc)) / 20), 2) == 0)
        col[m & tick] = lin(190, 190, 180)
    m = M(SK.Z_CARPET)
    c = T(lin(62, 50, 40), 0.14, 0.2)
    c = mixc(c, lit_c, ss(0.62, 0.82, fbm(Pp, 10.0, 2, seed=853)) * up * 0.7)
    c = mixc(c, MUD_D, ss(0.6, 0.8, n_mid) * 0.4)
    col[m] = c[m]
    m = M(SK.Z_LINER)
    c = T(lin(176, 166, 140), 0.10, 0.12)
    c = mixc(c, lin(120, 108, 86), ss(0.55, 0.80, fbm(Pp, 4.0, 3, seed=854)) * 0.6)            # water stains
    col[m] = c[m]
    m = M(SK.Z_CARD)
    c = T(lin(96, 86, 72), 0.12, 0.14)
    c = mixc(c, lin(60, 56, 48), ss(0.6, 0.85, fbm(Pp, 12.0, 2, seed=855)) * 0.5)
    col[m] = c[m]
    m = M(SK.Z_CARGO)
    rib = np.mod(np.floor(x / 0.03), 2)
    c = np.where((rib > 0)[:, None], lin(36, 36, 36), lin(26, 26, 26)) * (0.9 + 0.1 * n_hi)[:, None]
    c = mixc(c, lit_c, ss(0.70, 0.88, fbm(Pp, 10.0, 2, seed=856)) * 0.5)
    col[m] = c[m]
    return col, gr


# ================================================================ StartProps: the light, the battery, tyres, rubbish
def paint_props(Pp, Nn, Z, ao, aa):
    x, y, z = Pp[:, 0], Pp[:, 1], Pp[:, 2]
    n = len(x)
    n_lo, n_mid, n_hi, up, cav, edge = _common(Pp, Nn, ao, 901)
    T = _T(n_lo, n_hi)
    M = lambda zz: (Z == zz)
    col = np.tile(lin(128, 128, 128), (n, 1))
    gr = np.zeros(n, np.float32)
    lit, lit_c = _litter(Pp, up, 902, 0.8)
    L = SK.DISPLAY['StartLamp']
    lx, ly = x - L.x, y - L.y
    m = M(SK.Z_TRIPOD)                                                 # construction yellow, chipped, rust at the joints
    c = T(lin(222, 172, 30), 0.12, 0.16)
    c = mixc(c, lin(120, 100, 60), edge * 0.7 + ss(0.66, 0.84, fbm(Pp, 30.0, 2, seed=903)) * 0.5)
    c = mixc(c, RUST, ss(0.74, 0.88, fbm(Pp, 24.0, 2, seed=904)) * 0.6 + ss(0.15, 0.0, z) * 0.6)
    c = mixc(c, MUD, ss(0.25, 0.03, z) * 0.7)
    col[m] = c[m]
    m = M(SK.Z_LHOUSE)
    c = T(lin(40, 40, 42), 0.12, 0.16)
    c = mixc(c, lin(100, 98, 92), edge * 0.6 + up * 0.25)
    col[m] = c[m]
    gr = np.where(m, gline(dist_periodic(lx, 0.052, 0.0), 0.004, aa) * 0.4, gr)
    m = M(SK.Z_BATT)                                                   # blue-grey steel box, a yellow 12V label, dents and rust
    c = T(lin(96, 108, 116), 0.12, 0.16)
    b = SK.BATT['c']
    label = (np.abs(lx - b.x) < 0.10) & (np.abs(z - 0.16) < 0.05) & (Nn[:, 1] < -0.5)
    c[label] = lin(214, 176, 40)
    c = mixc(c, RUST, edge * 0.7 + ss(0.72, 0.86, fbm(Pp, 26.0, 2, seed=905)) * 0.5 + ss(0.06, 0.0, z) * 0.7)
    c = mixc(c, MUD, ss(0.12, 0.02, z) * 0.7)
    c = mixc(c, lit_c, lit * 0.8)
    col[m] = c[m]
    m = M(SK.Z_TERM)
    c = np.where((lx > b.x)[:, None], lin(30, 30, 30), lin(176, 30, 24)) * (0.85 + 0.15 * n_hi)[:, None]
    col[m] = c[m]
    for zz, base in ((SK.Z_CABLE, lin(24, 24, 24)), (SK.Z_FOOT, lin(28, 28, 28))):
        m = M(zz)
        c = T(base, 0.12, 0.16)
        c = mixc(c, MUD, ss(0.10, 0.0, z) * 0.6)
        col[m] = c[m]
    m = M(SK.Z_HANDLE)
    c = T(lin(150, 154, 152), 0.10, 0.08)
    c = mixc(c, RUST, run_m(Pp) * 0.5)
    col[m] = c[m]
    m = M(SK.Z_PTYRE)                                                  # old tyres: grey rubber, cracked, dirt, a little moss
    c = T(lin(36, 36, 36), 0.14, 0.2)
    c = mixc(c, lin(80, 80, 78), ss(0.55, 0.75, fbm(Pp * 2, 30.0, 1, seed=906)) * 0.3)
    c = mixc(c, MUD, ss(0.20, 0.0, z) * 0.6 + ss(0.65, 0.85, fbm(Pp, 9.0, 2, seed=907)) * 0.35)
    c = mixc(c, MOSS, ss(0.72, 0.86, fbm(Pp, 12.0, 2, seed=908)) * 0.5)
    c = mixc(c, lit_c, lit * 0.7)
    col[m] = c[m]
    gr = np.where(m, gline(dist_periodic(np.arctan2(y - SK.DISPLAY['StartTyres'].y, x - SK.DISPLAY['StartTyres'].x) * 0.3, 0.035),
                           0.003, aa) * (1 - up) * 0.5, gr)
    m = M(SK.Z_BAG)
    c = T(lin(22, 24, 26), 0.10, 0.12)
    c = mixc(c, lin(96, 100, 106), ss(0.55, 0.9, fbm(Pp, 14.0, 2, seed=909)) * (0.3 + 0.7 * up) * 0.45)     # the shine
    c = mixc(c, MUD, ss(0.12, 0.0, z) * 0.6)
    c = mixc(c, lit_c, lit * 0.8)
    col[m] = c[m]
    m = M(SK.Z_PLANK)
    g2 = fbm(Pp * np.array([0.3, 4.0, 4.0], np.float32), 10.0, 2, seed=910)
    c = T(lin(128, 118, 100), 0.16, 0.12) * (0.82 + 0.25 * g2)[:, None]
    c = mixc(c, lin(70, 64, 56), cav * 0.5 + ss(0.6, 0.85, n_mid) * 0.3)
    c = mixc(c, MOSS, ss(0.70, 0.86, fbm(Pp, 10.0, 2, seed=911)) * 0.4)
    col[m] = c[m]
    m = M(SK.Z_CARDB)
    c = T(lin(150, 116, 76), 0.14, 0.16)
    c = mixc(c, lin(96, 74, 50), ss(0.45, 0.75, fbm(Pp, 6.0, 3, seed=912)) * 0.7)
    col[m] = c[m]
    m = M(SK.Z_CAN)
    pick = hash1(np.floor(x * 8.0) + np.floor(y * 8.0) * 7.0, 913)
    c = np.where((pick > 0.5)[:, None], lin(160, 52, 40), lin(70, 110, 60))
    c = mixc(c, RUST, ss(0.45, 0.75, fbm(Pp, 40.0, 2, seed=914)) * 0.7)
    col[m] = c[m]
    m = M(SK.Z_BOTTLE)
    c = T(lin(70, 46, 22), 0.12, 0.2)
    c = mixc(c, lin(160, 140, 100), ss(0.7, 0.9, fbm(Pp, 30.0, 1, seed=915)) * 0.4)
    col[m] = c[m]
    m = M(SK.Z_RUSTB)
    c = T(lin(110, 58, 32), 0.2, 0.25)
    c = mixc(c, lin(60, 36, 24), ss(0.5, 0.8, n_mid) * 0.5)
    col[m] = c[m]
    return col, gr

"""Painters for the generator shed atlases (psxbake.make_atlas): painter(Pp, Nn, Z, ao, aa) -> (rgb_linear, groove).
The van / plane wear language on the shed's materials: falu-red board-and-batten with white trims, raw spruce
framing, birch-ply lining, galvanised corrugated roof with rust runs, a stained concrete floor; a Lister-green diesel
with oil seeping down, rusty heat on the exhaust, a radiator with fins; a grey switchboard, blue / red drums, yellow
spill pallet, red pump, shelf goods.  Shed frame: x east, y north, z up."""
import numpy as np
from noise import ss, lerp, mixc, fbm, dist_periodic, groove as gline
from psxbake import lin, boards, hash1
import gen_kit as K
from gen_kit import GY, FLOOR

OIL, GRIME, DUST, RUST, RUST_D = lin(28, 23, 18), lin(64, 58, 48), lin(118, 112, 100), lin(120, 62, 30), lin(70, 36, 20)


def _T(n_lo, n_hi):
    def T(c, a=0.20, b=0.14):
        return np.asarray(c, np.float32) * (0.88 + a * (n_lo - 0.5) * 2 + b * (n_hi - 0.5))[:, None]
    return T


def _common(Pp, Nn, ao, seed):
    n_lo = fbm(Pp, 2.2, 3, seed=seed)
    n_mid = fbm(Pp, 8.0, 3, seed=seed + 1)
    n_hi = fbm(Pp, 40.0, 2, seed=seed + 2)
    up = np.clip(Nn[:, 2], 0, 1)
    cav = ss(0.62, 0.30, ao)
    edge = ss(0.86, 0.97, ao) * ss(0.70, 0.88, fbm(Pp, 60.0, 2, seed=seed + 3))
    return n_lo, n_mid, n_hi, up, cav, edge


# ================================================================ shed
def paint_shed(Pp, Nn, Z, ao, aa):
    x, y, z = Pp[:, 0], Pp[:, 1], Pp[:, 2]
    n = len(x)
    n_lo, n_mid, n_hi, up, cav, edge = _common(Pp, Nn, ao, 101)
    T = _T(n_lo, n_hi)
    M = lambda zz: (Z == zz)
    col = np.tile(lin(128, 128, 128), (n, 1))
    gr = np.zeros(n, np.float32)
    gable = np.abs(Nn[:, 0]) > 0.5
    s_wall = np.where(gable, y, x)                                  # along the wall
    streak = ss(0.55, 0.80, fbm(Pp * np.array([1, 1, 0.12], np.float32), 6.0, 3, seed=111))
    # concrete slab and floor
    m = M(K.Z_SLAB)
    c = T(lin(132, 130, 124), 0.16, 0.22)
    c = mixc(c, lin(86, 82, 76), ss(0.05, 0.0, z) * 0.8 + cav * 0.4)
    c = mixc(c, lin(160, 158, 150), ss(0.06, 0.10, z) * up * 0.3)
    col[m] = c[m]
    m = M(K.Z_FLOOR)
    c = T(lin(116, 113, 106), 0.18, 0.20)
    trowel = fbm(Pp * np.array([1, 1, 0.0], np.float32), 1.2, 2, seed=112)
    c = c * (0.92 + 0.12 * trowel)[:, None]
    oil = np.exp(-(((x - 0.75) / 0.55) ** 2 + ((y - GY) / 0.42) ** 2)) * ss(0.35, 0.65, fbm(Pp, 3.5, 3, seed=113))
    oil += 0.8 * np.exp(-(((x + 0.48) / 0.18) ** 2 + ((y - 1.36) / 0.16) ** 2))            # under the day tank valve
    oil += 0.6 * np.exp(-(((x - 1.27) / 0.25) ** 2 + ((y - 0.98) / 0.10) ** 2))            # in front of drum 1
    oil += 0.5 * np.exp(-(((x - 1.20) / 0.12) ** 2 + ((y - GY + 0.48) / 0.12) ** 2))        # under the water separator
    c = mixc(c, OIL, np.clip(oil, 0, 0.85))
    path = np.exp(-((y / 0.55) ** 2)) * ss(-0.4, -2.3, x) + 0.6 * np.exp(-(((y + 1.0) / 0.35) ** 2)) * ss(-0.3, -1.6, x)
    c = mixc(c, lin(78, 72, 62), np.clip(path, 0, 1) * 0.45 * (0.6 + 0.4 * n_mid))        # trodden mud from the door
    c = mixc(c, lin(60, 56, 50), cav * 0.5)
    col[m] = c[m]
    gr = np.where(m, np.maximum(gline(np.abs(x), 0.008, aa), gline(np.abs(y), 0.008, aa)) * 0.7, gr)
    # board-and-batten cladding, falu red, weathered
    m = M(K.Z_CLAD)
    bi, seam = boards(s_wall, 0.20, aa, 0.004)
    bat = 1 - ss(0.024 - aa, 0.024 + aa, dist_periodic(s_wall, 0.20))                    # batten over each seam
    tone = 0.90 + 0.16 * hash1(bi + 7.0 * gable, 3)
    red = lin(132, 40, 30) * tone[:, None]
    c = red * (0.86 + 0.20 * (n_lo - 0.5) + 0.10 * (n_hi - 0.5))[:, None]
    bare = ss(0.62, 0.82, fbm(Pp * np.array([1, 1, 0.3], np.float32), 14.0, 3, seed=114)) * (0.35 + 0.65 * ss(0.6, 0.15, z))
    c = mixc(c, lin(126, 118, 104), bare * 0.85)                                         # flaked to grey wood low down
    c = mixc(c, lin(70, 30, 24), streak * 0.35)
    c = mixc(c, lin(60, 48, 40), ss(0.45, 0.10, z) * 0.55)                               # splash at the foot
    c = c * (1 - 0.10 * bat * ss(0.3, 0.9, n_mid))[:, None] * (1 + 0.08 * bat)[:, None]
    col[m] = c[m]
    edge_b = np.maximum(gline(dist_periodic(s_wall - 0.025, 0.20), 0.006, aa), gline(dist_periodic(s_wall + 0.025, 0.20), 0.006, aa))
    gr = np.where(m, edge_b * 0.8, gr)
    # white trims
    m = M(K.Z_TRIMW)
    c = T(lin(212, 208, 194), 0.10, 0.12)
    c = mixc(c, lin(132, 124, 108), np.clip(edge * 0.9 + ss(0.7, 0.88, fbm(Pp, 18.0, 2, seed=115)) * 0.6, 0, 0.9))
    c = mixc(c, lin(96, 88, 76), ss(0.35, 0.08, z) * 0.6 + cav * 0.35)
    c = mixc(c, lin(150, 140, 120), streak * 0.25)
    col[m] = c[m]
    # interior plywood lining: sheets 1.22 x 2.44 (seams), stud nail lines every 0.6, grime low, soot by the exhaust
    m = M(K.Z_LINING)
    c = T(lin(178, 150, 108), 0.14, 0.16)
    grain = fbm(Pp * np.array([3.0, 3.0, 0.4], np.float32), 9.0, 2, seed=116)
    c = c * (0.90 + 0.14 * grain)[:, None]
    sheet = np.maximum(gline(dist_periodic(s_wall, 1.22, 0.0), 0.005, aa), gline(dist_periodic(z - FLOOR, 1.22), 0.005, aa) * (z < 2.40))
    nails = (1 - ss(0.004 - aa, 0.006 + aa, dist_periodic(s_wall, 0.60, 0.30))) * (1 - ss(0.004 - aa, 0.006 + aa, dist_periodic(z, 0.15)))
    c = mixc(c, lin(96, 80, 60), ss(0.9, 0.25, z) * 0.45 * (0.6 + 0.4 * n_mid))
    near_e = np.exp(-(((y - K.EXH_Y) / 0.45) ** 2 + ((z - K.EXH_Z) / 0.35) ** 2)) * (x > 2.0)
    c = mixc(c, lin(30, 28, 26), np.clip(near_e * 0.9, 0, 0.9))
    hand = (np.exp(-(((y + 0.98) / 0.10) ** 2 + ((z - 1.40) / 0.10) ** 2)) * (x < -2.2)
            + np.exp(-(((x + 1.40) / 0.30) ** 2 + ((z - 1.45) / 0.25) ** 2)) * (y < -1.5))
    c = mixc(c, lin(90, 74, 56), np.clip(hand, 0, 1) * 0.35)
    c = mixc(c, lin(120, 98, 70), streak * 0.30)
    col[m] = c[m]
    gr = np.where(m, np.maximum(sheet * 0.75, nails * 0.6), gr)
    # raw spruce framing
    m = M(K.Z_TIMBER)
    vert = np.abs(Nn[:, 2]) < 0.6
    g2 = fbm(Pp * np.where(vert[:, None], np.array([4.0, 4.0, 0.25], np.float32), np.array([0.3, 4.0, 4.0], np.float32)), 10.0, 2, seed=117)
    c = T(lin(150, 118, 78), 0.16, 0.12) * (0.86 + 0.22 * g2)[:, None]
    c = mixc(c, lin(92, 72, 50), cav * 0.55 + ss(0.6, 0.9, n_mid) * 0.2)
    c = mixc(c, DUST, up * 0.25)
    col[m] = c[m]
    # corrugated roof (top) and its underside: 76 mm corrugation down the slope, sheets 0.76 wide, rust runs
    for zz, top in ((K.Z_ROOF, True), (K.Z_ROOFUNDER, False)):
        m = M(zz)
        ph = np.cos(2 * np.pi * x / 0.076)
        shade = 0.82 + 0.18 * ph
        c = T(lin(150, 154, 150) if top else lin(118, 120, 116), 0.18, 0.20) * shade[:, None]
        run = ss(0.58, 0.82, fbm(Pp * np.array([1, 0.15, 0.15], np.float32), 7.0, 3, seed=118)) * ss(0.4, 1.9, np.abs(y))
        c = mixc(c, RUST if top else RUST_D, run * (0.75 if top else 0.45))
        c = mixc(c, lin(96, 92, 86), ss(0.65, 0.85, n_mid) * 0.35)
        if not top:
            drip = ss(0.62, 0.80, fbm(Pp, 16.0, 2, seed=119))
            c = mixc(c, lin(60, 58, 52), drip * 0.35)
        col[m] = c[m]
        lap = gline(dist_periodic(x, 0.76, 0.38), 0.010, aa)
        gr = np.where(m, lap * 0.8, gr)
    # doors: outside red boards, inside raw boards
    m = M(K.Z_DOOR)
    bi, seam = boards(y, 0.12, aa, 0.004)
    c = lin(130, 38, 30) * (0.90 + 0.16 * hash1(bi, 5))[:, None] * (0.88 + 0.18 * (n_lo - 0.5) * 2)[:, None]
    c = mixc(c, lin(126, 118, 104), ss(0.62, 0.84, fbm(Pp * np.array([1, 1, 0.3], np.float32), 14.0, 3, seed=120)) * ss(0.7, 0.15, z) * 0.9)
    c = mixc(c, lin(66, 50, 42), ss(0.40, 0.11, z) * 0.6)
    c = mixc(c, lin(80, 44, 36), np.exp(-(((y + 0.25) / 0.10) ** 2 + ((z - 1.0) / 0.12) ** 2)) * 0.5)
    col[m] = c[m]
    gr = np.where(m, seam * 0.8, gr)
    m = M(K.Z_DOORIN)
    bi, seam = boards(np.where(np.abs(Nn[:, 1]) > 0.5, x, y), 0.12, aa, 0.004)
    c = lin(146, 118, 82) * (0.90 + 0.16 * hash1(bi, 6))[:, None] * (0.86 + 0.2 * (n_lo - 0.5) * 2)[:, None]
    c = mixc(c, lin(92, 74, 54), cav * 0.4 + ss(0.5, 0.12, z) * 0.4)
    col[m] = c[m]
    gr = np.where(m, seam * 0.7, gr)
    # metals and the rest
    m = M(K.Z_STEELBLK)
    c = T(lin(36, 36, 36), 0.12, 0.2)
    c = mixc(c, RUST, np.clip(edge * 0.7 + ss(0.72, 0.86, fbm(Pp, 30.0, 2, seed=121)) * 0.5, 0, 0.8))
    col[m] = c[m]
    m = M(K.Z_GALV)
    spang = fbm(Pp, 90.0, 1, seed=122)
    c = T(lin(150, 154, 152), 0.10, 0.08) * (0.88 + 0.22 * spang)[:, None]
    c = mixc(c, lin(196, 198, 192), ss(0.78, 0.90, fbm(Pp, 25.0, 2, seed=123)) * 0.4)
    c = mixc(c, RUST, run_m(Pp) * 0.35)
    col[m] = c[m]
    m = M(K.Z_RUSTPIPE)
    c = T(lin(104, 58, 34), 0.2, 0.25)
    c = mixc(c, lin(30, 26, 24), ss(2.6, 3.7, z) * 0.85 + ss(0.6, 0.85, n_mid) * 0.3)
    c = mixc(c, lin(70, 68, 72), ss(2.5, 2.4, x) * 0.5)
    col[m] = c[m]
    m = M(K.Z_CHEQUER)
    u_, v_ = (x + y) / 0.035, (x - y) / 0.035
    dia = (1 - ss(0.10, 0.22, np.abs(u_ - np.round(u_)))) * (1 - ss(0.25, 0.45, np.abs(v_ - np.round(v_))))
    c = T(lin(124, 124, 120), 0.12, 0.16) * (0.85 + 0.25 * dia)[:, None]
    c = mixc(c, lin(70, 66, 60), ss(0.5, 0.85, n_mid) * 0.4)
    col[m] = c[m]
    gr = np.where(m, (1 - dia) * 0.25, gr)
    col[M(K.Z_INSUL)] = T(lin(222, 220, 212), 0.06, 0.06)[M(K.Z_INSUL)]
    col[M(K.Z_WIRE)] = T(lin(22, 22, 22), 0.1, 0.1)[M(K.Z_WIRE)]
    m = M(K.Z_ENAMEL)
    c = T(lin(36, 70, 50), 0.10, 0.12)
    c = mixc(c, lin(20, 20, 20), ss(0.80, 0.92, fbm(Pp, 40.0, 2, seed=124)) * 0.9 + edge * 0.5)
    col[m] = c[m]
    col[M(K.Z_ENAMEL_IN)] = T(lin(232, 230, 222), 0.06, 0.08)[M(K.Z_ENAMEL_IN)]
    return col, gr


def run_m(Pp):
    return ss(0.70, 0.86, fbm(Pp * np.array([1, 1, 0.2], np.float32), 10.0, 2, seed=125))


# ================================================================ genset
def paint_genset(Pp, Nn, Z, ao, aa):
    x, y, z = Pp[:, 0], Pp[:, 1], Pp[:, 2]
    n = len(x)
    n_lo, n_mid, n_hi, up, cav, edge = _common(Pp, Nn, ao, 201)
    T = _T(n_lo, n_hi)
    M = lambda zz: (Z == zz)
    drip = ss(0.58, 0.78, fbm(Pp * np.array([1, 1, 0.25], np.float32), 9.0, 3, seed=211))
    col = np.tile(lin(120, 120, 120), (n, 1))
    gr = np.zeros(n, np.float32)
    green = lin(70, 102, 66)
    for zz in (K.Z_GPAINT, K.Z_ALT):
        m = M(zz)
        c = T(green, 0.18, 0.16)
        c = mixc(c, lin(40, 40, 38), edge * 0.85)
        c = mixc(c, OIL, np.clip(drip * (1 - ss(0.75, 1.05, z)) * 0.7 + cav * 0.45, 0, 0.85))
        c = mixc(c, DUST, up * 0.30 * ss(0.4, 0.8, n_mid))
        c = mixc(c, RUST, ss(0.72, 0.86, fbm(Pp, 22.0, 2, seed=212)) * ss(0.55, 0.32, z) * 0.6)
        if zz == K.Z_ALT:
            r = np.hypot(y - GY, z - 0.64)
            endc = (x < -0.548) & (r < 0.215)
            ang = np.arctan2(z - 0.64, y - GY)
            slots = (np.mod(ang / (2 * np.pi / 36), 1.0) < 0.45) & (r > 0.06) & (r < 0.20)
            c = np.where((endc & slots)[:, None], lin(14, 14, 14), c)
            vent = (x > 0.09) & (x < 0.17) & (np.mod((ang + np.pi) / (2 * np.pi / 48), 1.0) < 0.4) & (r > 0.255)
            c = np.where(vent[:, None], lin(16, 16, 16), c)
        col[m] = c[m]
    m = M(K.Z_SKID)
    c = T(lin(32, 32, 32), 0.12, 0.2)
    c = mixc(c, RUST, np.clip(edge * 0.8 + ss(0.70, 0.86, fbm(Pp, 26.0, 2, seed=213)) * 0.5, 0, 0.85))
    c = mixc(c, OIL, up * ss(0.5, 0.8, n_mid) * 0.6)
    col[m] = c[m]
    m = M(K.Z_EXH)
    c = T(lin(110, 64, 40), 0.22, 0.25)
    c = mixc(c, lin(78, 76, 80), ss(1.10, 0.98, z) * 0.6)                 # manifold heat-greyed
    c = mixc(c, lin(26, 24, 22), np.clip(ss(0.6, 0.85, n_mid) * 0.4 + cav * 0.5, 0, 0.8))
    c = mixc(c, lin(150, 80, 40), ss(0.75, 0.9, fbm(Pp, 30.0, 2, seed=214)) * 0.4)
    col[m] = c[m]
    m = M(K.Z_ALU2)
    c = T(lin(106, 108, 102), 0.16, 0.22)
    c = mixc(c, GRIME, np.clip(cav * 0.6 + ss(0.55, 0.8, n_mid) * 0.25, 0, 0.8))
    c = mixc(c, OIL, drip * 0.35)
    col[m] = c[m]
    m = M(K.Z_RAD)
    fins = 0.5 + 0.5 * np.cos(2 * np.pi * z / 0.006)
    tubes = gline(dist_periodic(y - GY, 0.012), 0.003, aa)
    c = T(lin(30, 32, 32), 0.1, 0.1) * (0.75 + 0.35 * fins)[:, None]
    c = mixc(c, lin(70, 66, 58), ss(0.65, 0.85, n_mid) * 0.35)
    col[m] = c[m]
    gr = np.where(m, np.maximum(tubes * 0.6, (1 - fins) * 0.25), gr)
    m = M(K.Z_RADT)
    c = T(lin(34, 34, 32), 0.1, 0.15)
    c = mixc(c, lin(150, 120, 60), edge * 0.6)
    col[m] = c[m]
    m = M(K.Z_FANB)
    c = T(lin(222, 150, 30), 0.12, 0.15)
    c = mixc(c, lin(50, 46, 40), np.clip(edge * 0.6 + cav * 0.4 + ss(0.6, 0.85, n_mid) * 0.3, 0, 0.8))
    col[m] = c[m]
    for zz, cc in ((K.Z_HOSE2, lin(28, 28, 28)), (K.Z_BELT, lin(24, 24, 24)), (K.Z_RUBBERM, lin(36, 34, 32)),
                   (K.Z_WIRERB, lin(20, 20, 20)), (K.Z_BLACK2, lin(24, 24, 24))):
        m = M(zz)
        c = mixc(T(cc, 0.1, 0.12), DUST, up * 0.3)
        col[m] = c[m]
    m = M(K.Z_STEEL3)
    c = T(lin(168, 170, 168), 0.12, 0.2)
    c = mixc(c, OIL, cav * 0.5)
    c = mixc(c, RUST, ss(0.75, 0.88, fbm(Pp, 40.0, 2, seed=215)) * 0.5)
    col[m] = c[m]
    m = M(K.Z_COPPER)
    c = mixc(T(lin(186, 110, 64), 0.15, 0.2), lin(90, 110, 80), ss(0.7, 0.88, fbm(Pp, 30.0, 2, seed=216)) * 0.5)
    col[m] = c[m]
    m = M(K.Z_BRASS2)
    c = mixc(T(lin(186, 148, 70), 0.15, 0.2), lin(90, 76, 46), cav * 0.6)
    col[m] = c[m]
    m = M(K.Z_GLASSB)
    sep_z = z - 0.36
    c = np.where((sep_z < 0.05)[:, None], lin(118, 126, 124), lin(206, 150, 40))
    col[m] = (c * (0.9 + 0.2 * n_hi)[:, None])[m]
    m = M(K.Z_BATTC)
    c = T(lin(42, 44, 46), 0.1, 0.12)
    c = np.where((Nn[:, 2] > 0.8)[:, None], c * 0.6 + lin(80, 82, 84) * 0.4, c)
    col[m] = c[m]
    m = M(K.Z_LEAD2)
    c = mixc(T(lin(112, 114, 116), 0.1, 0.15), lin(220, 222, 214), ss(0.75, 0.9, fbm(Pp, 60.0, 2, seed=217)) * 0.7)
    col[m] = c[m]
    col[M(K.Z_WIRER)] = T(lin(152, 30, 24), 0.12, 0.15)[M(K.Z_WIRER)]
    m = M(K.Z_YEL2)
    col[m] = mixc(T(lin(222, 182, 40), 0.12, 0.15), lin(60, 50, 30), edge * 0.6)[m]
    m = M(K.Z_RED2)
    col[m] = mixc(T(lin(184, 30, 24), 0.12, 0.15), lin(50, 20, 16), edge * 0.5)[m]
    m = M(K.Z_PANELBOX)
    c = T(lin(56, 60, 58), 0.12, 0.15)
    c = mixc(c, lin(120, 120, 114), edge * 0.6)
    c = mixc(c, DUST, up * 0.4)
    col[m] = c[m]
    m = M(K.Z_CANVAS2)
    weave = (0.5 + 0.25 * np.cos(2 * np.pi * x / 0.004) + 0.25 * np.cos(2 * np.pi * (y + z) / 0.004))
    c = T(lin(132, 120, 88), 0.15, 0.15) * (0.9 + 0.12 * weave)[:, None]
    c = mixc(c, lin(60, 56, 46), np.clip(ss(1.85, 1.62, x) * 0.5 + ss(0.6, 0.85, n_mid) * 0.3 + cav * 0.3, 0, 0.8))
    seamc = gline(np.abs(x - 1.94), 0.006, aa)
    col[m] = c[m]
    gr = np.where(m, seamc * 0.7, gr)
    m = M(K.Z_FILTERC)
    side = np.abs(Nn[:, 2]) < 0.7
    band = side & (((z > 0.68) & (z < 0.72) & (x > 0.85)) | ((np.abs(y - (GY + 0.25)) < 0.02) & (x < 0.70)))
    c = T(lin(204, 200, 184), 0.08, 0.1)
    c = np.where(band[:, None], lin(40, 70, 140), c)
    c = mixc(c, OIL, drip * 0.5 + cav * 0.3)
    col[m] = c[m]
    m = M(K.Z_AIRF)
    c = T(lin(30, 30, 30), 0.1, 0.12)
    lab = (np.abs(z - 1.15) < 0.028) & (np.abs(Nn[:, 2]) < 0.5) & (y < GY - 0.43) & (np.abs(x - 0.66) < 0.06)
    c = np.where(lab[:, None], lin(200, 196, 182), c)
    col[m] = c[m]
    m = M(K.Z_CHROME2)
    c = T(lin(196, 198, 200), 0.06, 0.1)
    c = mixc(c, lin(60, 62, 66), ss(0.2, 0.9, np.abs(Nn[:, 2])) * 0.5)
    col[m] = c[m]
    return col, gr


# ================================================================ props
def paint_props(Pp, Nn, Z, ao, aa):
    x, y, z = Pp[:, 0], Pp[:, 1], Pp[:, 2]
    n = len(x)
    n_lo, n_mid, n_hi, up, cav, edge = _common(Pp, Nn, ao, 301)
    T = _T(n_lo, n_hi)
    M = lambda zz: (Z == zz)
    col = np.tile(lin(120, 120, 120), (n, 1))
    gr = np.zeros(n, np.float32)
    m = M(K.Z_CAB)
    c = T(lin(194, 196, 190), 0.10, 0.12)
    c = mixc(c, lin(110, 108, 100), np.clip(edge * 0.6 + cav * 0.4 + ss(1.2, 0.9, z) * 0.35, 0, 0.8))
    c = mixc(c, lin(90, 84, 76), np.exp(-(((x + 1.07) / 0.06) ** 2 + ((z - 1.45) / 0.10) ** 2)) * 0.5)
    col[m] = c[m]
    m = M(K.Z_CABIN)
    col[m] = mixc(T(lin(150, 152, 148), 0.1, 0.12), lin(96, 96, 92), cav * 0.5)[m]
    for zz, cc in ((K.Z_BRK, lin(26, 26, 26)), (K.Z_TOGGLE, lin(18, 18, 18)), (K.Z_MCB, lin(210, 208, 198)),
                   (K.Z_CABLEB, lin(20, 20, 20)), (K.Z_CABLER, lin(156, 30, 24)), (K.Z_HOSEP, lin(30, 30, 30))):
        m = M(zz)
        col[m] = mixc(T(cc, 0.08, 0.1), GRIME, cav * 0.4)[m]
    m = M(K.Z_REDH)
    col[m] = mixc(T(lin(190, 30, 24), 0.1, 0.12), lin(60, 20, 16), edge * 0.5)[m]
    m = M(K.Z_TANKP)
    c = T(lin(102, 114, 100), 0.14, 0.15)
    c = mixc(c, OIL, np.clip(np.exp(-(((x + 0.55) / 0.06) ** 2)) * ss(1.74, 1.40, z) * (y < 1.25) * 0.7
                            + np.exp(-(((x + 0.48) / 0.05) ** 2)) * ss(1.32, 1.20, z) * 0.6, 0, 0.8))
    c = mixc(c, RUST, np.clip(edge * 0.6 + ss(1.36, 1.30, z) * 0.4 * ss(0.5, 0.8, n_mid), 0, 0.8))
    c = mixc(c, DUST, up * 0.35)
    col[m] = c[m]
    for zz, cc in ((K.Z_DRUMB, lin(38, 70, 122)), (K.Z_DRUMR, lin(164, 36, 26))):
        m = M(zz)
        c = T(cc, 0.18, 0.18)
        hoop = np.zeros(n, np.float32)
        side = np.abs(Nn[:, 2]) < 0.5
        for cx_, cy_, bz in ((1.27, 1.235, 0.24), (1.88, 1.235, 0.24), (1.95, -1.20, FLOOR)):
            rr = np.hypot(x - cx_, y - cy_)
            near = rr < 0.34
            hz = (z - bz)
            h = np.maximum.reduce([ss(0.035, 0.0, np.abs(hz - 0.015)), ss(0.03, 0.0, np.abs(hz - 0.29)),
                                   ss(0.03, 0.0, np.abs(hz - 0.58)), ss(0.04, 0.0, np.abs(hz - 0.865))])
            lid = ss(0.24, 0.28, rr) * 0.8 + ss(0.75, 0.9, fbm(Pp, 25.0, 2, seed=315)) * 0.4
            hoop = np.where(near, np.where(side, h, lid), hoop)                # rims rust, the lid only at its edge
        rust_d = lin(70, 50, 38) if zz == K.Z_DRUMB else RUST                  # dark rust: no purple on blue paint
        c = mixc(c, rust_d, np.clip(hoop * 0.7 * (0.5 + 0.5 * n_mid) + edge * 0.5, 0, 0.85))
        c = mixc(c, lin(200, 200, 196), ss(0.80, 0.92, fbm(Pp * np.array([1, 1, 4], np.float32), 30.0, 2, seed=311)) * 0.35)
        c = mixc(c, OIL, up * ss(0.5, 0.8, n_mid) * 0.5)
        col[m] = c[m]
    m = M(K.Z_PALLET)
    gx, gy = dist_periodic(x, 0.04), dist_periodic(y, 0.04)
    grid = np.maximum(gline(gx, 0.008, aa), gline(gy, 0.008, aa)) * (np.abs(z - 0.24) < 0.003)
    c = T(lin(222, 182, 32), 0.12, 0.15)
    c = mixc(c, lin(70, 60, 40), np.clip(cav * 0.5 + grid * 0.8 + ss(0.6, 0.85, n_mid) * 0.3, 0, 0.9))
    col[m] = c[m]
    gr = np.where(m, grid * 0.5, gr)
    m = M(K.Z_PUMP)
    col[m] = mixc(mixc(T(lin(150, 34, 24), 0.15, 0.2), lin(30, 26, 24), edge * 0.7), OIL, cav * 0.4)[m]
    m = M(K.Z_STEELP)
    c = mixc(T(lin(160, 162, 158), 0.14, 0.2), RUST, ss(0.70, 0.86, fbm(Pp, 36.0, 2, seed=312)) * 0.6 + cav * 0.2)
    col[m] = c[m]
    m = M(K.Z_BRASSP)
    col[m] = mixc(T(lin(190, 150, 70), 0.12, 0.18), lin(90, 74, 44), cav * 0.6)[m]
    m = M(K.Z_COPPERP)
    col[m] = mixc(T(lin(182, 106, 62), 0.14, 0.18), lin(86, 104, 78), ss(0.7, 0.88, fbm(Pp, 28.0, 2, seed=313)) * 0.5)[m]
    m = M(K.Z_WOOD)
    g2 = fbm(Pp * np.array([0.3, 4.0, 4.0], np.float32), 9.0, 2, seed=314)
    c = T(lin(156, 124, 82), 0.14, 0.12) * (0.86 + 0.22 * g2)[:, None]
    c = mixc(c, lin(90, 70, 48), cav * 0.5)
    c = mixc(c, DUST, up * 0.25)
    col[m] = c[m]
    m = M(K.Z_OILCAN)
    band = (z > 1.53) & (z < 1.66)
    c = np.where(band[:, None], lin(226, 190, 40), lin(36, 76, 150))
    c = mixc(c * (0.9 + 0.15 * n_hi)[:, None], OIL, cav * 0.4 + up * 0.2)
    col[m] = c[m]
    m = M(K.Z_JUG)
    lvl = z < 0.97 + 0.17
    c = np.where(lvl[:, None], lin(150, 196, 128), lin(222, 222, 212))
    col[m] = mixc(c * (0.92 + 0.1 * n_hi)[:, None], GRIME, cav * 0.3)[m]
    m = M(K.Z_BOXC)
    c = T(lin(172, 142, 98), 0.12, 0.12)
    c = np.where(((np.abs(Nn[:, 2]) < 0.5) & (np.abs(z - 1.05) < 0.03))[:, None], lin(40, 70, 140), c)
    col[m] = c[m]
    m = M(K.Z_TOOLBOX)
    col[m] = mixc(T(lin(176, 30, 24), 0.12, 0.15), lin(40, 34, 30), edge * 0.7)[m]
    m = M(K.Z_RAG)
    chk = ((np.floor(x / 0.02) + np.floor(y / 0.02) + np.floor(z / 0.02)) % 2).astype(np.float32)
    c = T(lin(120, 104, 84), 0.15, 0.15) * (0.88 + 0.16 * chk)[:, None]
    col[m] = mixc(c, OIL, ss(0.5, 0.8, n_mid) * 0.6)[m]
    m = M(K.Z_CABLEJ)
    col[m] = T(lin(184, 112, 60), 0.1, 0.15)[m]
    m = M(K.Z_EXT)
    lab = (z > 0.85) & (z < 1.02) & (np.abs(Nn[:, 2]) < 0.5)
    c = np.where(lab[:, None], lin(230, 228, 214), lin(200, 30, 26))
    col[m] = mixc(c * (0.92 + 0.12 * n_hi)[:, None], DUST, up * 0.3)[m]
    m = M(K.Z_MAT)
    rib = gline(dist_periodic(y, 0.012), 0.004, aa)
    c = T(lin(32, 32, 32), 0.1, 0.12)
    col[m] = mixc(c, lin(90, 84, 74), ss(0.55, 0.85, n_mid) * 0.35)[m]
    gr = np.where(m, rib * 0.4, gr)
    m = M(K.Z_HARDB)
    col[m] = T(lin(112, 82, 56), 0.1, 0.12)[m]
    return col, gr


def paint_snow(Pp, Nn, Z, ao, aa):
    n = len(Pp)
    sp = fbm(Pp, 70.0, 1, seed=401)
    c = np.tile(lin(236, 240, 248), (n, 1)) * (0.94 + 0.06 * fbm(Pp, 6.0, 2, seed=402))[:, None]
    c = mixc(c, lin(150, 170, 200), ss(0.8, 0.3, ao) * 0.6)
    c = mixc(c, lin(255, 255, 255), ss(0.86, 0.95, sp) * 0.5)
    return c, None

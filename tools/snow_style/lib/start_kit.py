"""Forest start (forest_start): the abandoned post at the entry to the restricted zone - five prefabs, each in its own frame
(Blender: z up, the front -y, metres; origin on the ground, z = 0; Unity (x, y, z) -> (-x, z, -y), the front -> +Z):
  StartBooth  Wachhäuschen 2 x 2 m, 2.6 m high: board-and-batten in faded olive, white trims, a mono-pitch corrugated roof;
              IX_Door on the right wall (hung outside, hinge at its back edge, 100 deg out), the window to the road (front),
              a desk under it (Slot_Table_*), the bulkhead lamp over the door (Lamp_Booth, Lens_Booth, LIGHT_Booth), the plate
              ZUFAHRT SPERRGEBIET / RESTRICTED AREA ACCESS
  StartCar    an 80s estate car without make or badges, long abandoned: dirt, needles, the rear right tyre flat (the body
              leans to it); the driver's door IX_CarDoor (front left, hinge at its front edge, 70 deg out); COL_Car
  StartLamp   a tripod work light on its own battery: the flood head Lamp_Flood (Lens_Flood, LIGHT_Flood) to blink / go out,
              the battery box on the ground, the cable up the mast
  StartTyres  three old tyres stacked and one leaning on them
  StartTrash  a heap by the booth: bin bags, boards, a flattened box, cans, a bottle, a bucket
Each root's parts / sockets are given in its own frame (the contract); in the .blend the roots stand apart (DISPLAY)."""
import math
from mathutils import Vector, Matrix
import bmesh
import van3_mech as VM
import gen_kit as K
from gen_kit import kb, part, socket, box6, face, prism_x, prism_y, wall_grid, quad, transform_kb, KBS, CONTRACT, LAMPS
from gen_kit import (Z_SLAB, Z_FLOOR, Z_CLAD, Z_TRIMW, Z_LINING, Z_TIMBER, Z_ROOF, Z_ROOFUNDER, Z_DOOR, Z_DOORIN, Z_STEELBLK,
                     Z_GALV)
from van2_kit import ZERO
import start_tex as ST

uv = ST.uv
TAU = 2 * math.pi
ROOTS = ['StartBooth', 'StartCar', 'StartLamp', 'StartTyres', 'StartTrash']
DISPLAY = {'StartBooth': Vector((-7.0, 0.0, 0.0)), 'StartCar': Vector((0.0, 0.0, 0.0)), 'StartLamp': Vector((5.5, -2.0, 0.0)),
           'StartTyres': Vector((5.5, 2.0, 0.0)), 'StartTrash': Vector((9.0, 0.0, 0.0))}      # where they stand in the .blend
ROOT_OF = {}                     # object name -> its root
BOXES = []                       # colliders: (name, parent, (x0, x1, y0, y1, z0, z1), rot_xyz_deg or None) in the root frame

# ---------------------------------------------------------------- zones (Start atlas: booth + car; StartProps: the rest)
(Z_PAINT, Z_CBLACK, Z_UNDER, Z_TYRE, Z_RIM, Z_CHROME, Z_HEAD, Z_TAIL, Z_SEAT, Z_DASH, Z_CARPET, Z_LINER, Z_CARD, Z_GRILLE,
 Z_PLATE, Z_CARGO, Z_GAUGE, Z_RUBBER) = range(30, 48)
(Z_DESK, Z_PADC) = (48, 49)
START_ZONES = [Z_SLAB, Z_FLOOR, Z_CLAD, Z_TRIMW, Z_LINING, Z_TIMBER, Z_ROOF, Z_ROOFUNDER, Z_DOOR, Z_DOORIN, Z_STEELBLK, Z_GALV] + \
              list(range(30, 50))
(Z_TRIPOD, Z_LHOUSE, Z_BATT, Z_TERM, Z_CABLE, Z_PTYRE, Z_BAG, Z_PLANK, Z_CARDB, Z_CAN, Z_BOTTLE, Z_RUSTB, Z_FOOT, Z_HANDLE) = \
    range(100, 114)
PROP_ZONES = list(range(100, 114))


def reg(name, atlas, root):
    ROOT_OF[name] = root
    return kb(name, atlas)


def r4(v):
    return [round(float(a), 4) + 0.0 for a in v]


def triangulate_ngons(k):
    big = [f for f in k.bm.faces if len(f.verts) > 4]
    if big:
        bmesh.ops.triangulate(k.bm, faces=big, quad_method='BEAUTY', ngon_method='BEAUTY')


def lathe_uv(k, c, h0, h1, rect, axis):
    """cylindrical UVs on every face of k (lens lathes)"""
    c, ax = Vector(c), Vector(axis).normalized()
    e1 = Vector((1, 0, 0)) if abs(ax.x) < 0.9 else Vector((0, 1, 0))
    e1 = (e1 - ax * e1.dot(ax)).normalized()
    e2 = ax.cross(e1)
    u0, v0, u1, v1 = rect
    for f in k.bm.faces:
        for l in f.loops:
            d = l.vert.co - c
            a = (math.atan2(d.dot(e2), d.dot(e1)) / TAU) % 1.0
            t = min(max((d.dot(ax) - h0) / (h1 - h0), 0.0), 1.0)
            l[k.uvl].uv = (u0 + (u1 - u0) * (0.05 + 0.9 * a), v0 + (v1 - v0) * t)


# ================================================================ StartBooth
FLOOR = 0.15
PAD = (-1.25, 1.25, -1.25, 1.25)                     # concrete pad, top 0.12; its sides go 0.30 into the ground
PAD_TOP, PAD_Z0 = 0.12, -0.30
X0, X1, Y0, Y1 = -1.0, 1.0, -1.0, 1.0                # outer faces
T = 0.08
IX0, IX1, IY0, IY1 = X0 + T, X1 - T, Y0 + T, Y1 - T  # room 1.84 x 1.84
PITCH = math.radians(6.0)                            # mono-pitch, high at the front (the road)
TP, CP, SP = math.tan(PITCH), math.cos(PITCH), math.sin(PITCH)
ZF = 2.38                                            # rafter bottom over the front outer face
OV_F, OV_B, OV_S = 0.30, 0.25, 0.22
YF, YB, XS = Y0 - OV_F, Y1 + OV_B, X1 + OV_S
RAFT_D, RAFT_W, PURL, SHEET = 0.10, 0.05, 0.035, 0.02
RAFTERS = [-0.70, 0.0, 0.70]
PURLINS = [-1.18, -0.70, -0.22, 0.26, 0.74, 1.12]
WIN = dict(u0=-0.80, u1=0.22, z0=1.00, z1=1.70)      # the window in the front wall (to the road)
DOOR = dict(y0=-0.30, y1=0.50, z0=FLOOR, z1=2.00)    # the opening in the right wall (+x)
LEAF = dict(x0=X1 + 0.004, x1=X1 + 0.050, y0=-0.32, y1=0.52, z0=FLOOR + 0.012, z1=2.022)
DOOR_PIV = Vector((X1 + 0.056, 0.525, 1.10))
DESK_Z = 0.86
DESK = (IX0, 0.42, IY0, IY0 + 0.46)                  # x0, x1, y0, y1 under the window
SIGN = dict(c=Vector((0.61, Y0 - 0.014, 1.38)), w=0.52, h=0.34)
BULK = Vector((X1, 0.10, 2.12))                      # the bulkhead's wall plate over the door


def z_rb(y):
    return ZF - (y - Y0) * TP


def z_rt(y):
    return z_rb(y) + RAFT_D / CP


def z_sb(y):
    return z_rb(y) + (RAFT_D + PURL) / CP


def z_st(y):
    return z_sb(y) + SHEET / CP


ZS = z_rb(IY1)                                       # side walls: full cells up to here, a wedge above
ZTOP_F, ZTOP_B = ZF, z_rb(IY1)


class WallBox:
    """boxes in wall coordinates: a across the wall, u along it, z up"""

    def __init__(self, k, axis):
        self.k, self.axis = k, axis
        self.A, self.U = ('y', 'x') if axis == 'y' else ('x', 'y')

    def S(self, *names):
        return tuple(n[0] + {'a': self.A, 'u': self.U, 'z': 'z'}[n[1]] for n in names)

    def P(self, a, u, z):
        return Vector((u, a, z)) if self.axis == 'y' else Vector((a, u, z))

    def box(self, ua, ub, za, zb, aa, ab, zone=Z_TRIMW, skip=(), k=None):
        lo = self.P(min(aa, ab), min(ua, ub), za)
        hi = self.P(max(aa, ab), max(ua, ub), zb)
        box6(k or self.k, tuple(lo), tuple(hi), zone, skip=self.S(*skip))

    def glass(self, g, a, ua, ub, za, zb):
        n = Vector((0, 1, 0)) if self.axis == 'y' else Vector((1, 0, 0))
        for d, s in ((-0.0005, -1), (0.0005, 1)):
            face(g, [self.P(a + d, ua, za), self.P(a + d, ub, za), self.P(a + d, ub, zb), self.P(a + d, ua, zb)], ZERO, n * s)


def window(k, g, axis, a_out, a_in, u0, u1, z0, z1, out_sign):
    """the shed's window: white frame and glazing cross, panes to g, white outside trims and sill, a spruce sill inside"""
    W = WallBox(k, axis)
    fw = 0.045
    lo_a, hi_a = sorted((a_out, a_in))
    f0, f1 = lo_a + 0.015, hi_a - 0.015
    am = (f0 + f1) / 2
    W.box(u0, u1, z0, z0 + fw, f0, f1, skip=('-z',))
    W.box(u0, u1, z1 - fw, z1, f0, f1, skip=('+z',))
    W.box(u0, u0 + fw, z0 + fw, z1 - fw, f0, f1, skip=('-u', '-z', '+z'))
    W.box(u1 - fw, u1, z0 + fw, z1 - fw, f0, f1, skip=('+u', '-z', '+z'))
    uc, zc = (u0 + u1) / 2, (z0 + z1) / 2
    W.box(uc - 0.015, uc + 0.015, z0 + fw, z1 - fw, am - 0.015, am + 0.015, skip=('-z', '+z'))
    W.box(u0 + fw, u1 - fw, zc - 0.015, zc + 0.015, am - 0.012, am + 0.012, skip=('-u', '+u'))
    W.glass(g, am, u0 + fw, u1 - fw, z0 + fw, z1 - fw)
    t0, t1 = sorted((a_out, a_out + out_sign * 0.022))
    into = '+a' if out_sign < 0 else '-a'
    W.box(u0 - 0.07, u1 + 0.07, z1, z1 + 0.07, t0, t1, skip=(into,))
    W.box(u0 - 0.07, u0, z0, z1, t0, t1, skip=(into, '-z', '+z'))
    W.box(u1, u1 + 0.07, z0, z1, t0, t1, skip=(into, '-z', '+z'))
    s0, s1 = sorted((a_out, a_out + out_sign * 0.06))
    W.box(u0 - 0.09, u1 + 0.09, z0 - 0.035, z0, s0, s1, skip=(into,))
    i0, i1 = sorted((a_in, a_in - out_sign * 0.05))
    W.box(u0 - 0.03, u1 + 0.03, z0 - 0.03, z0, i0, i1, zone=Z_TIMBER, skip=('-a' if out_sign < 0 else '+a',))


def booth_shell():
    k = reg('StartBooth', 'Start', 'StartBooth')
    g = reg('Booth_Glass', 'glass', 'StartBooth')
    wz = dict(out=Z_CLAD, reveal=Z_TIMBER, end=Z_CLAD, top=Z_TIMBER, **{'in': Z_LINING})
    x0, x1, y0, y1 = PAD                                              # the pad: sides, the ring round the booth, the floor
    box6(k, (x0, y0, PAD_Z0), (x1, y1, PAD_TOP), Z_PADC, skip=('-z', '+z'))
    for lo, hi in (((x0, y0), (x1, Y0)), ((x0, Y1), (x1, y1)), ((x0, Y0), (X0, Y1)), ((X1, Y0), (x1, Y1))):
        face(k, [(lo[0], lo[1], PAD_TOP), (hi[0], lo[1], PAD_TOP), (hi[0], hi[1], PAD_TOP), (lo[0], hi[1], PAD_TOP)], Z_PADC, (0, 0, 1))
    xs, ys = [IX0, 0.0, IX1], [IY0, 0.0, IY1]
    for i in range(2):
        for j in range(2):
            face(k, [(xs[i], ys[j], FLOOR), (xs[i + 1], ys[j], FLOOR), (xs[i + 1], ys[j + 1], FLOOR), (xs[i], ys[j + 1], FLOOR)],
                 Z_FLOOR, (0, 0, 1))
    face(k, [(IX1, DOOR['y0'], FLOOR), (X1, DOOR['y0'], FLOOR), (X1, DOOR['y1'], FLOOR), (IX1, DOOR['y1'], FLOOR)], Z_FLOOR, (0, 0, 1))
    # the floor's edge on the pad (a 3 cm sill all round under the cladding)
    box6(k, (X0, Y0, PAD_TOP), (X1, Y1, FLOOR), Z_TIMBER, skip=('-z', '+z'))
    # walls: front (the window), back (plain), right (the door), left (plain)
    wall_grid(k, 'y', Y0, IY0, [X0, WIN['u0'], WIN['u1'], X1], [FLOOR, WIN['z0'], WIN['z1'], ZTOP_F], {(1, 1)}, -1, wz)
    wall_grid(k, 'y', IY1, Y1, [X0, X1], [FLOOR, ZTOP_B], set(), 1, wz)
    wall_grid(k, 'x', IX1, X1, [IY0, DOOR['y0'], DOOR['y1'], IY1], [FLOOR, DOOR['z1'], ZS], {(1, 0)}, 1, wz, ends=False, top=False)
    wall_grid(k, 'x', X0, IX0, [IY0, IY1], [FLOOR, ZS], set(), -1, wz, ends=False, top=False)
    poly = [(IY0, ZS), (IY1, ZS), (IY1, z_sb(IY1) - 0.002), (IY0, z_sb(IY0) - 0.002)]
    prism_x(k, poly, IX1, X1, Z_LINING, Z_CLAD, [None, None, None, None])
    prism_x(k, poly, X0, IX0, Z_CLAD, Z_LINING, [None, None, None, None])
    bays = [(X0, RAFTERS[0] - RAFT_W / 2)] + [(a + RAFT_W / 2, b - RAFT_W / 2) for a, b in zip(RAFTERS[:-1], RAFTERS[1:])] + \
           [(RAFTERS[-1] + RAFT_W / 2, X1)]
    for (ya, yb), zt, e_ya, e_yb in (((Y0, IY0), ZTOP_F, Z_CLAD, Z_LINING), ((IY1, Y1), ZTOP_B, Z_LINING, Z_CLAD)):
        poly = [(ya, zt), (yb, zt), (yb, z_rt(yb)), (ya, z_rt(ya))]
        for xa, xb in bays:
            prism_x(k, poly, xa, xb, Z_CLAD if xa == X0 else None, Z_CLAD if xb == X1 else None, [None, e_yb, None, e_ya])
        poly = [(ya, z_rt(ya)), (yb, z_rt(yb)), (yb, z_sb(yb) - 0.002), (ya, z_sb(ya) - 0.002)]
        prism_x(k, poly, X0, X1, Z_CLAD, Z_CLAD, [None, e_yb, None, e_ya])
    window(k, g, 'y', Y0, IY0, WIN['u0'], WIN['u1'], WIN['z0'], WIN['z1'], -1)
    cb, ct = 0.12, 0.022                                              # white corner boards up to the sheet
    for sx in (-1, 1):
        for sy in (-1, 1):
            xo, yo = (X1 if sx > 0 else X0), (Y1 if sy > 0 else Y0)
            ztop = z_sb(yo) - 0.002
            xa, xb = sorted((xo, xo - sx * cb))
            ya, yb = sorted((yo, yo + sy * ct))
            box6(k, (xa, ya, FLOOR), (xb, yb, ztop), Z_TRIMW, skip=('-z', '+z', '+y' if sy < 0 else '-y'))
            xa, xb = sorted((xo, xo + sx * ct))
            ya, yb = sorted((yo + sy * ct, yo - sy * (cb - ct)))
            box6(k, (xa, ya, FLOOR), (xb, yb, ztop), Z_TRIMW, skip=('-z', '+z', '+x' if sx < 0 else '-x'))
    # door trims (white) beside and over the hung leaf; the hinge pintles
    for ya, yb in ((-0.45, -0.335), (0.535, 0.65)):
        box6(k, (X1, ya, FLOOR), (X1 + 0.022, yb, 2.04), Z_TRIMW, skip=('-x', '-z', '+z'))
    box6(k, (X1, -0.45, 2.04), (X1 + 0.022, 0.65, 2.15), Z_TRIMW, skip=('-x',))
    for zh in (0.32, 1.90):
        k.box(X1 + 0.022, X1 + 0.052, 0.535, 0.575, zh - 0.03, zh + 0.03, Z_STEELBLK)
    booth_roof(k)
    # the desk under the window: top, front lip, two brackets to the wall; a shelf board on the left wall
    x0d, x1d, y0d, y1d = DESK
    box6(k, (x0d, y0d, DESK_Z - 0.035), (x1d, y1d, DESK_Z), Z_DESK, skip=('-y', '-x'))
    for xb in (x0d + 0.12, x1d - 0.10):
        k.box(xb - 0.02, xb + 0.02, y0d, y0d + 0.30, DESK_Z - 0.30, DESK_Z - 0.035, Z_TIMBER)
        k.strip([Vector((xb, y0d + 0.01, DESK_Z - 0.28)), Vector((xb, y0d + 0.28, DESK_Z - 0.05))], Vector((1, 0, 0)), 0.035, 0.035,
                Z_TIMBER)
    box6(k, (IX0, -0.55, 1.45), (IX0 + 0.24, 0.45, 1.475), Z_DESK, skip=('-x',))
    for yb in (-0.40, 0.30):
        k.box(IX0, IX0 + 0.18, yb - 0.012, yb + 0.012, 1.33, 1.45, Z_STEELBLK)
    for i, (x, y) in enumerate(((-0.55, y0d + 0.24), (-0.10, y0d + 0.24), (0.25, y0d + 0.24))):
        socket(f'Slot_Table_{i + 1}', (x, y, DESK_Z), 'slot', parent='StartBooth',
               note='a place on the desk top for the note / the items (z = the top)')
    socket('Slot_Shelf', (IX0 + 0.12, -0.05, 1.475), 'slot', parent='StartBooth', note='the shelf board on the left wall')
    # the plate ZUFAHRT SPERRGEBIET / RESTRICTED AREA ACCESS on the front wall right of the window
    s = SIGN
    k.box(s['c'].x - s['w'] / 2 - 0.012, s['c'].x + s['w'] / 2 + 0.012, Y0 - 0.012, Y0, s['c'].z - s['h'] / 2 - 0.012,
          s['c'].z + s['h'] / 2 + 0.012, Z_GALV)
    Sg = reg('Booth_Sign', 'panel', 'StartBooth')
    quad(Sg, s['c'] - Vector((0, 0.0005, 0)), (0, -1, 0), s['w'], s['h'], uv('sign'))
    part('Booth_Sign', 'static', s['c'], parent='StartBooth',
         note='the plate ZUFAHRT SPERRGEBIET / RESTRICTED AREA ACCESS (German first, English under it; panel atlas)')
    part('Booth_Glass', 'static', (( WIN['u0'] + WIN['u1']) / 2, (Y0 + IY0) / 2, (WIN['z0'] + WIN['z1']) / 2), parent='StartBooth',
         note='the window pane (glass material)')
    part('StartBooth', 'static', (0, 0, 0), note='the post\'s booth (root): origin = the middle of the pad on the ground; floor '
                                                    '0.15, the pad 0.12 high and 0.30 into the ground')
    BOXES.extend([
        ('Booth_Front', 'StartBooth', (X0, X1, Y0, IY0, PAD_TOP, ZTOP_F), None),
        ('Booth_Back', 'StartBooth', (X0, X1, IY1, Y1, PAD_TOP, ZTOP_B), None),
        ('Booth_Left', 'StartBooth', (X0, IX0, IY0, IY1, PAD_TOP, ZS), None),
        ('Booth_Right_F', 'StartBooth', (IX1, X1, IY0, DOOR['y0'], PAD_TOP, ZS), None),
        ('Booth_Right_B', 'StartBooth', (IX1, X1, DOOR['y1'], IY1, PAD_TOP, ZS), None),
        ('Booth_Right_Top', 'StartBooth', (IX1, X1, DOOR['y0'], DOOR['y1'], DOOR['z1'], ZS), None),
        ('Booth_Roof', 'StartBooth', (-XS, XS, YF, YB, z_rb(0.0) - 0.02, z_st(0.0) + 0.02), (-math.degrees(PITCH), 0.0, 0.0)),
        ('Booth_Pad', 'StartBooth', (PAD[0], PAD[1], PAD[2], PAD[3], PAD_Z0, PAD_TOP), None),
        ('Booth_Desk', 'StartBooth', (DESK[0], DESK[1], DESK[2], DESK[3], DESK_Z - 0.04, DESK_Z), None)])


def booth_roof(k):
    for xr in RAFTERS + [-(XS - 0.025), XS - 0.025]:
        poly = [(YF, z_rb(YF)), (YB, z_rb(YB)), (YB, z_rt(YB)), (YF, z_rt(YF))]
        prism_x(k, poly, xr - RAFT_W / 2, xr + RAFT_W / 2, Z_TIMBER, Z_TIMBER, [Z_TIMBER, None, None, None])
    for yp in PURLINS:
        w = 0.022
        poly = [(yp - w, z_rt(yp - w)), (yp + w, z_rt(yp + w)), (yp + w, z_sb(yp + w) - 0.001), (yp - w, z_sb(yp - w) - 0.001)]
        prism_x(k, poly, -XS, XS, None, None, [None, Z_TIMBER, None, Z_TIMBER])
    top = [Vector((-XS, YF, z_st(YF))), Vector((XS, YF, z_st(YF))), Vector((XS, YB, z_st(YB))), Vector((-XS, YB, z_st(YB)))]
    bot = [Vector((-XS, YF, z_sb(YF))), Vector((XS, YF, z_sb(YF))), Vector((XS, YB, z_sb(YB))), Vector((-XS, YB, z_sb(YB)))]
    nrm = Vector((0, SP, CP))
    face(k, top, Z_ROOF, nrm)
    face(k, bot, Z_ROOFUNDER, -nrm)
    box6(k, (-XS, YF - 0.022, z_rb(YF) - 0.03), (XS, YF, z_st(YF) + 0.02), Z_TRIMW, skip=('+y', '-x', '+x'))
    box6(k, (-XS, YB, z_rb(YB) - 0.03), (XS, YB + 0.022, z_st(YB) + 0.02), Z_TRIMW, skip=('-y', '-x', '+x'))
    for sx in (-1, 1):
        xa, xb = sorted((sx * XS, sx * (XS + 0.022)))
        ya, yb = YF - 0.022, YB + 0.022
        poly = [(ya, z_st(ya) + 0.02 - 0.18), (yb, z_st(yb) + 0.02 - 0.18), (yb, z_st(yb) + 0.02), (ya, z_st(ya) + 0.02)]
        prism_x(k, poly, xa, xb, Z_TRIMW, Z_TRIMW)


def booth_door():
    D = reg('IX_Door', 'Start', 'StartBooth')
    L = LEAF
    x0, x1, y0, y1, zl, zh = L['x0'], L['x1'], L['y0'], L['y1'], L['z0'], L['z1']
    box6(D, (x0, y0, zl), (x1, y1, zh), {'+x': Z_DOOR, '-x': Z_DOORIN, '*': Z_DOOR})
    for z0_ in (0.28, 1.05, 1.82):                                   # white ledges and the brace (outside)
        box6(D, (x1, y0 + 0.03, z0_), (x1 + 0.02, y1 - 0.03, z0_ + 0.09), Z_TRIMW, skip=('-x',))
    D.strip([Vector((x1 + 0.009, y1 - 0.10, 0.38)), Vector((x1 + 0.009, y0 + 0.30, 1.04))], Vector((1, 0, 0)), 0.08, 0.016, Z_TRIMW)
    D.strip([Vector((x1 + 0.009, y1 - 0.10, 1.15)), Vector((x1 + 0.009, y0 + 0.30, 1.81))], Vector((1, 0, 0)), 0.08, 0.016, Z_TRIMW)
    for zh_ in (0.32, 1.90):                                          # strap hinges to the knuckles on the pivot line
        D.box(x1 + 0.021, x1 + 0.027, y1 - 0.42, DOOR_PIV.y - 0.006, zh_ - 0.02, zh_ + 0.02, Z_STEELBLK)
        D.cyl(Vector((DOOR_PIV.x, DOOR_PIV.y, zh_ - 0.03)), Vector((DOOR_PIV.x, DOOR_PIV.y, zh_ + 0.03)), 0.011, Z_STEELBLK, 8)
    hz = 1.02
    for xx, s in ((x1, 1), (x0, -1)):                                 # lever handles both sides, the lock case inside
        D.box(*sorted((xx, xx + s * 0.008)), y0 + 0.05, y0 + 0.09, hz - 0.10, hz + 0.05, Z_STEELBLK)
        D.cyl(Vector((xx + s * 0.008, y0 + 0.07, hz)), Vector((xx + s * 0.045, y0 + 0.07, hz)), 0.008, Z_STEELBLK, 6)
        D.cyl(Vector((xx + s * 0.045, y0 + 0.07, hz)), Vector((xx + s * 0.045, y0 + 0.19, hz)), 0.009, Z_STEELBLK, 6)
    D.box(x0 - 0.028, x0 - 0.008, y0 + 0.03, y0 + 0.14, hz - 0.12, hz + 0.06, Z_STEELBLK)
    part('IX_Door', 'hinge', DOOR_PIV, (0, 0, 1), (0, 100), parent='StartBooth', states=['shut', 'open'],
         note='the booth door in the right wall, hung outside on strap hinges at its back (+y) edge; opens outward (+) to 100 deg; '
              'lever handles both sides')
    socket('Hand_Door_Out', (x1 + 0.07, y0 + 0.15, hz), 'hand', parent='IX_Door', note='lever outside')
    socket('Hand_Door_In', (x0 - 0.07, y0 + 0.15, hz), 'hand', parent='IX_Door', note='lever inside')
    BOXES.append(('Door', 'IX_Door', (x0, x1, y0, y1, zl, zh), None))


def booth_lamp():
    k = KBS['StartBooth']
    b = BULK
    L = reg('Lamp_Booth', 'Start', 'StartBooth')                      # the caged bulkhead over the door (the shed's)
    L.box(b.x, b.x + 0.012, b.y - 0.06, b.y + 0.06, b.z - 0.075, b.z + 0.075, Z_STEELBLK)
    L.lathe(b + Vector((0.012, 0, 0)), (1, 0, 0), [(0.0, 0.07), (0.01, 0.085), (0.045, 0.085), (0.05, 0.072)], Z_STEELBLK, 12,
            cap0=True, cap1=False)
    dc = b + Vector((0.060, 0, 0))
    for a in (0, 60, 120):
        d = Vector((0, math.cos(math.radians(a)), math.sin(math.radians(a))))
        L.tube([dc + d * 0.078 * math.cos(math.radians(t)) + Vector((0.052 * math.sin(math.radians(t)), 0, 0))
                for t in range(0, 181, 30)], 0.003, Z_STEELBLK, 4, caps=False)
    k.tube([b + Vector((0.004, 0.0, 0.075)), b + Vector((0.004, 0.0, 0.20)), b + Vector((0.004, -0.45, 0.20))], 0.008, Z_STEELBLK, 6)
    part('Lamp_Booth', 'static', b, parent='StartBooth',
         note='the bulkhead lamp under the roof over the door: its own part (the housing and the cage); the lens and the halo are '
              'its children; the light LIGHT_Booth')
    Lb = reg('Lens_Booth', 'lens:booth', 'StartBooth')
    Lb.disc_uv(dc, Vector((1, 0, 0)), 0.068, uv('l_bulk'), 12, dome=0.04)
    part('Lens_Booth', 'static', dc + Vector((0.02, 0, 0)), parent='Lamp_Booth',
         note='the frosted dome: M_Start_Lamp_Booth (lit) / M_Start_Lamp_Booth_Off')
    Gb = reg('Glow_Booth', 'glow', 'StartBooth')
    quad(Gb, dc + Vector((0.045, 0, 0)), (1, 0, 0), 0.50, 0.50, uv('glow_warm'))
    u0, v0, u1, v1 = uv('glow_warm')
    quad(Gb, Vector((X1 + 0.17, dc.y, dc.z - 0.002)), (0, 0, -1), 0.32, 0.50, (u0, v0, (u0 + u1) / 2, v1), up=(1, 0, 0))
    part('Glow_Booth', 'static', dc, parent='Lamp_Booth', note='halo cards: additive, unlit; shown when the lamp is on')
    LAMPS.append(('Booth', dc + Vector((0.06, 0, -0.02)), (0.6, 0, -0.8), 'bulkhead', (0, 0, 1)))


def build_booth():
    booth_shell()
    booth_door()
    booth_lamp()


# ================================================================ StartCar
CW, CL = 0.85, 2.275                                  # half width, half length
YFA, YRA, TRK, RW, RWID = -1.42, 1.20, 0.70, 0.29, 0.17   # axles, half track, wheel radius / width
RA = 0.37                                             # arch radius
Z_SILL, Z_BELT = 0.30, 0.88
Y_WS, Y_RF, Y_RR, Z_ROOF = -0.78, -0.12, 2.13, 1.40   # windscreen base, roof front / rear, roof
WB, WR, ZB, ZR = 0.83, 0.70, 0.89, 1.38               # the glasshouse: half width at the belt / at the roof edge
DOORC = dict(y0=-0.76, y1=0.36, z0=0.36, z1=Z_BELT)   # the driver's door (left, +x)
B_PIL, C_PIL, D_PIL = (0.36, 0.44), (1.20, 1.28), (2.07, 2.20)
FLAT = (-TRK, YRA)                                    # the flat tyre: rear right
DROP = 0.07                                           # the flat rim sits this much lower
ROLL, PITCHC = -math.degrees(math.atan(DROP / 2 / (2 * TRK))), -math.degrees(math.atan(DROP / 2 / (YRA - YFA)))
BODY_PIV = Vector((TRK, YFA, RW))                     # the body leans about the front left wheel


def body_R():
    return (Matrix.Translation(BODY_PIV) @ Matrix.Rotation(math.radians(PITCHC), 4, 'X') @
            Matrix.Rotation(math.radians(ROLL), 4, 'Y') @ Matrix.Translation(-BODY_PIV))


def gh(t, side):
    """the glasshouse side plane: (x, z) at height fraction t (0 belt .. 1 roof edge)"""
    return side * (WB + (WR - WB) * t), ZB + (ZR - ZB) * t


def side_outline():
    """the lower body's side silhouette (y, z), counter-clockwise seen from +x (y right?): front bottom -> rear"""
    pts = [(-2.18, 0.33), (-2.19, 0.80), (Y_WS, 0.87), (2.22, 0.89), (2.24, 0.88), (2.24, 0.34)]
    pts.append((YRA + RA + 0.02, Z_SILL))
    for i in range(0, 13):
        a = math.radians(i * 15)
        pts.append((YRA + RA * math.cos(a), RW + RA * math.sin(a) + 0.01))
    pts.append((YFA + RA + 0.0, Z_SILL))
    for i in range(0, 13):
        a = math.radians(i * 15)
        pts.append((YFA + RA * math.cos(a), RW + RA * math.sin(a) + 0.01))
    pts.append((-2.12, Z_SILL + 0.01))
    return pts


def poly_side(k, pts, x, zone, nx):
    face(k, [(x, y, z) for y, z in pts], zone, (nx, 0, 0))


def car_body():
    k = reg('StartCar', 'Start', 'StartCar')
    out = side_outline()
    # left side (+x): cut round the driver's door: the part before it, the sill under it, the part behind it
    yd0, yd1, zd0, zd1 = DOORC['y0'], DOORC['y1'], DOORC['z0'], DOORC['z1']
    # the side skins as vertical strips (convex quads): bottom = the sill or the arch, top = the hood / belt line
    def z_top(y):
        if y <= Y_WS:
            return 0.80 + 0.07 * (y + 2.19) / (Y_WS + 2.19)
        if y <= 2.22:
            return 0.87 + 0.02 * (y - Y_WS) / (2.22 - Y_WS)
        return 0.88
    arch_f = [(YFA + RA * math.cos(math.radians(a)), RW + RA * math.sin(math.radians(a)) + 0.01) for a in range(180, -1, -15)]
    arch_r = [(YRA + RA * math.cos(math.radians(a)), RW + RA * math.sin(math.radians(a)) + 0.01) for a in range(180, -1, -15)]
    bottom = [(-2.185, 0.33), (-2.12, Z_SILL + 0.01)] + arch_f + [(yd0, Z_SILL), (yd1, Z_SILL)] + arch_r + [(2.24, Z_SILL + 0.04)]

    def strips(xs, nx, sta):
        for (ya, za, ta), (yb, zb, tb) in zip(sta[:-1], sta[1:]):
            if yb - ya < 1e-6:
                continue
            face(k, [(xs, ya, za), (xs, yb, zb), (xs, yb, tb), (xs, ya, ta)], Z_PAINT, (nx, 0, 0))
    full = [(y, z, z_top(y)) for y, z in bottom]
    full[0] = (-2.185, 0.33, 0.80)
    strips(-CW, -1, full)                                              # right side: whole
    strips(CW, 1, [t for t in full if t[0] <= yd0 + 1e-6])             # left side: before the door ...
    strips(CW, 1, [(yd0, Z_SILL, zd0), (yd1, Z_SILL, zd0)])            # ... the sill under it ...
    strips(CW, 1, [t for t in full if t[0] >= yd1 - 1e-6])             # ... and behind it
    # the jambs of the door opening (body cut between the skin and the inner panel)
    for yy, n in ((yd0, (0, 1, 0)), (yd1, (0, -1, 0))):
        face(k, [(CW - 0.05, yy, zd0), (CW, yy, zd0), (CW, yy, 0.87), (CW - 0.05, yy, 0.87)], Z_UNDER, n)
    face(k, [(CW - 0.05, yd0, zd0), (CW, yd0, zd0), (CW, yd1, zd0), (CW - 0.05, yd1, zd0)], Z_UNDER, (0, 0, 1))
    # hood (two quads, a crown down the middle) and the scuttle to the windscreen
    for xa, xb in ((-CW, 0.0), (0.0, CW)):
        zc = 0.012 if True else 0
        face(k, [(xa, -2.19, 0.80 + (zc if xa == 0 else 0)), (xb, -2.19, 0.80 + (zc if xb == 0 else 0)),
                 (xb, Y_WS, 0.87 + (zc if xb == 0 else 0)), (xa, Y_WS, 0.87 + (zc if xa == 0 else 0))], Z_PAINT, (0, 0, 1))
    # front face: bumper zone low, the headlamps, the grille, paint between
    xs = [-CW, -0.78, -0.42, -0.40, 0.40, 0.42, 0.78, CW]
    zs = [0.33, 0.48, 0.55, 0.75, 0.80]
    yfr = -2.19
    for i in range(len(xs) - 1):
        for j in range(len(zs) - 1):
            xa, xb, za, zb = xs[i], xs[i + 1], zs[j], zs[j + 1]
            zone = Z_PAINT
            xm, zm = (xa + xb) / 2, (za + zb) / 2
            if zb <= 0.48:
                zone = Z_CBLACK
            elif 0.55 <= zm <= 0.75 and 0.42 <= abs(xm) <= 0.78:
                zone = Z_HEAD
            elif 0.48 <= zm <= 0.80 and abs(xm) < 0.40:
                zone = Z_GRILLE
            face(k, [(xa, yfr, za), (xb, yfr, za), (xb, yfr, zb), (xa, yfr, zb)], zone, (0, -1, 0))
    # rear: the tailgate's lower face (lamps at the corners, the empty plate recess), the cargo floor's sill
    xs = [-CW, -0.62, -0.26, 0.26, 0.62, CW]
    zs = [0.34, 0.48, 0.62, 0.84, 0.89]
    for i in range(len(xs) - 1):
        for j in range(len(zs) - 1):
            xa, xb, za, zb = xs[i], xs[i + 1], zs[j], zs[j + 1]
            xm, zm = (xa + xb) / 2, (za + zb) / 2
            zone = Z_PAINT
            if zb <= 0.48:
                zone = Z_CBLACK
            elif abs(xm) > 0.62 and 0.48 <= zm <= 0.84:
                zone = Z_TAIL
            elif abs(xm) < 0.26 and 0.48 <= zm <= 0.62:
                zone = Z_PLATE
            face(k, [(xa, 2.24, za), (xb, 2.24, za), (xb, 2.24, zb), (xa, 2.24, zb)], zone, (0, 1, 0))
    # under: the floor pan seen from below; wheel wells (arch liners and the inner walls)
    face(k, [(-CW, -2.12, Z_SILL), (CW, -2.12, Z_SILL), (CW, 2.24, Z_SILL), (-CW, 2.24, Z_SILL)], Z_UNDER, (0, 0, -1))
    face(k, [(-CW, -2.18, 0.33), (CW, -2.18, 0.33), (CW, -2.12, Z_SILL + 0.01), (-CW, -2.12, Z_SILL + 0.01)], Z_UNDER, (0, -1, -1))
    for yc in (YFA, YRA):
        for s in (-1, 1):
            arc = [(yc + RA * math.cos(math.radians(a)), RW + RA * math.sin(math.radians(a)) + 0.01) for a in range(0, 181, 15)]
            for (ya, za), (yb, zb) in zip(arc[:-1], arc[1:]):
                n = Vector((0, -(ya + yb) / 2 + yc, -((za + zb) / 2 - RW))).normalized()
                face(k, [(s * CW, ya, za), (s * CW, yb, zb), (s * 0.60, yb, zb), (s * 0.60, ya, za)], Z_UNDER, n)
            face(k, [(s * 0.60, y, z) for y, z in arc], Z_UNDER, (s, 0, 0))
    # bumpers (black), the scuttle panel, wipers
    k.box(-0.86, 0.86, -2.29, -2.19, 0.32, 0.49, Z_CBLACK)
    k.box(-0.86, 0.86, 2.24, 2.30, 0.32, 0.49, Z_CBLACK)
    k.box(-0.80, 0.80, Y_WS - 0.02, Y_WS + 0.08, 0.86, 0.885, Z_CBLACK)
    for xw in (-0.36, 0.20):
        k.strip([Vector((xw, Y_WS + 0.04, 0.895)), Vector((xw + 0.42, Y_WS + 0.10, 0.93))], Vector((0, -0.6, 0.8)), 0.02, 0.012,
                Z_CBLACK)
    # belt-line tops of the side skins (to the inner trim)
    for s in (-1, 1):
        segs = [(Y_WS, 2.22)] if s < 0 else [(Y_WS, DOORC['y0']), (DOORC['y1'], 2.22)]
        for ya, yb in segs:
            face(k, [(s * (CW - 0.05), ya, 0.875), (s * CW, ya, 0.875), (s * CW, yb, 0.885), (s * (CW - 0.05), yb, 0.885)], Z_PAINT,
                 (0, 0, 1))
    # door handles (body: the right side's two doors and the rear left door), the fuel flap is paint
    for s, yh in ((-1, 0.20), (-1, 1.05), (1, 1.05)):
        k.box(*sorted((s * CW, s * (CW + 0.018))), yh - 0.07, yh + 0.07, 0.76, 0.79, Z_CHROME)
    triangulate_ngons(k)
    return k


def car_glasshouse():
    k = KBS['StartCar']
    g = reg('Car_Glass', 'glass', 'StartCar')
    # roof: top, the gutters, the headliner under it; roof rails
    face(k, [(-WR - 0.01, Y_RF - 0.02, Z_ROOF), (WR + 0.01, Y_RF - 0.02, Z_ROOF), (WR + 0.01, Y_RR + 0.02, Z_ROOF),
             (-WR - 0.01, Y_RR + 0.02, Z_ROOF)], Z_PAINT, (0, 0, 1))
    for s in (-1, 1):
        face(k, [(s * (WR + 0.01), Y_RF - 0.02, Z_ROOF), (s * (WR + 0.01), Y_RR + 0.02, Z_ROOF), (s * WR, Y_RR, ZR),
                 (s * WR, Y_RF, ZR)], Z_PAINT, (s, 0, 0))
        k.box(*sorted((s * 0.56, s * 0.60)), Y_RF + 0.35, Y_RR - 0.10, Z_ROOF, Z_ROOF + 0.035, Z_CBLACK)
        for yy in (Y_RF + 0.37, Y_RR - 0.12):
            k.box(*sorted((s * 0.55, s * 0.61)), yy - 0.03, yy + 0.03, Z_ROOF, Z_ROOF + 0.05, Z_CBLACK)
    face(k, [(-WR, Y_RF - 0.02, Z_ROOF), (WR, Y_RF - 0.02, Z_ROOF), (WR, Y_RF, ZR), (-WR, Y_RF, ZR)], Z_PAINT, (0, -1, 0))
    face(k, [(-WR, Y_RR, ZR), (WR, Y_RR, ZR), (WR, Y_RR + 0.02, Z_ROOF), (-WR, Y_RR + 0.02, Z_ROOF)], Z_PAINT, (0, 1, 0))
    face(k, [(-WR + 0.03, Y_RF + 0.02, ZR - 0.035), (WR - 0.03, Y_RF + 0.02, ZR - 0.035), (WR - 0.03, Y_RR - 0.02, ZR - 0.035),
             (-WR + 0.03, Y_RR - 0.02, ZR - 0.035)], Z_LINER, (0, 0, -1))
    # pillars: A along the windscreen edge, B, C, D in the side planes (body colour outside, liner inside)
    for s in (-1, 1):
        def P(y, t, inset=0.0):
            x, z = gh(t, s)
            return Vector((x - s * inset, y, z))
        a0, a1 = P(Y_WS, 0.0), P(Y_RF, 1.0)
        k.strip([a0 + Vector((0, 0.02, 0)), a1 + Vector((0, 0.02, -0.01))], Vector((s, 0, 0.25)), 0.08, 0.05, Z_PAINT)
        for (ya, yb), name in ((B_PIL, 'B'), (C_PIL, 'C'), (D_PIL, 'D')):
            if s > 0 and name == 'B':
                pass
            q = [P(ya, 0.0), P(yb, 0.0), P(yb + (0.02 if name == 'D' else 0), 1.0), P(ya, 1.0)]
            face(k, q, Z_PAINT, (s, 0, 0.3))
            qi = [P(ya, 0.0, 0.04), P(yb, 0.0, 0.04), P(yb + (0.02 if name == 'D' else 0), 1.0, 0.04), P(ya, 1.0, 0.04)]
            face(k, qi, Z_LINER, (-s, 0, -0.3))
            for (p0, p1, q0, q1), n in (((q[0], q[3], qi[0], qi[3]), (0, -1, 0)), ((q[1], q[2], qi[1], qi[2]), (0, 1, 0))):
                face(k, [p0, p1, q1, q0], Z_CBLACK, n)
        # window rubbers along the belt (black strip) except over the driver's door
        segs = [(Y_WS, 2.20)] if s < 0 else [(DOORC['y1'], 2.20)]
        for ya, yb in segs:
            k.box(*sorted((s * (WB - 0.01), s * (WB + 0.012))), ya, yb, ZB - 0.012, ZB + 0.012, Z_RUBBER)
        # side glass: front (right side only - the left one is in the driver's door), rear door, rear quarter
        panes = [(B_PIL[1], C_PIL[0]), (C_PIL[1], D_PIL[0])]
        if s < 0:
            panes.insert(0, ('A', B_PIL[0]))
        for ya, yb in panes:
            if ya == 'A':
                pts = [P(Y_WS + 0.06, 0.04, 0.012), P(yb, 0.04, 0.012), P(yb, 0.97, 0.012), P(Y_RF + 0.05, 0.97, 0.012)]
            else:
                pts = [P(ya, 0.04, 0.012), P(yb, 0.04, 0.012), P(yb, 0.97, 0.012), P(ya, 0.97, 0.012)]
            face(g, pts, ZERO, (s, 0, 0.3))
            face(g, list(reversed(pts)), ZERO, (-s, 0, -0.3))
    # windscreen and rear window (glass) with black rubbers
    ws = [Vector((-0.77, Y_WS + 0.03, ZB + 0.01)), Vector((0.77, Y_WS + 0.03, ZB + 0.01)), Vector((0.655, Y_RF + 0.01, ZR - 0.01)),
          Vector((-0.655, Y_RF + 0.01, ZR - 0.01))]
    face(g, ws, ZERO, (0, -0.6, 0.8))
    face(g, list(reversed(ws)), ZERO, (0, 0.6, -0.8))
    k.strip([ws[0], ws[1]], Vector((0, -0.6, 0.8)), 0.03, 0.02, Z_RUBBER, caps=False)
    k.strip([ws[3], ws[2]], Vector((0, -0.6, 0.8)), 0.03, 0.02, Z_RUBBER, caps=False)
    rw = [Vector((0.69, Y_RR + 0.015, ZR - 0.015)), Vector((-0.69, Y_RR + 0.015, ZR - 0.015)), Vector((-0.80, 2.215, 0.92)),
          Vector((0.80, 2.215, 0.92))]
    face(g, rw, ZERO, (0, 1, 0.1))
    face(g, list(reversed(rw)), ZERO, (0, -1, -0.1))
    for s in (-1, 1):                                                  # the tailgate's frame round the rear window
        k.strip([Vector((s * 0.82, 2.215, 0.90)), Vector((s * 0.705, Y_RR + 0.01, ZR - 0.01))], Vector((0, 1, 0.1)), 0.05, 0.03,
                Z_PAINT)
    k.box(-0.82, 0.82, 2.20, 2.235, 0.885, 0.93, Z_PAINT)
    k.box(-0.08, 0.08, Y_RR - 0.04, Y_RR + 0.02, ZR - 0.06, ZR - 0.02, Z_CBLACK)   # the rear wiper's motor cover
    part('Car_Glass', 'static', (0, 0.6, 1.15), parent='StartCar', note='the fixed windows (dirty glass material)')


def car_door():
    D = reg('IX_CarDoor', 'Start', 'StartCar')
    G = reg('IX_CarDoor_Glass', 'glass', 'StartCar')
    y0, y1, z0, z1 = DOORC['y0'] + 0.006, DOORC['y1'] - 0.006, DOORC['z0'] + 0.006, Z_BELT
    xo, xi = CW, CW - 0.05
    face(D, [(xo, y0, z0), (xo, y1, z0), (xo, y1, z1), (xo, y0, z1)], Z_PAINT, (1, 0, 0))            # the skin
    face(D, [(xi, y0, z0), (xi, y1, z0), (xi, y1, z1), (xi, y0, z1)], Z_CARD, (-1, 0, 0))            # the door card
    for (a, b, n) in ((((xi, y0, z0), (xo, y0, z0), (xo, y0, z1), (xi, y0, z1)), None, (0, -1, 0)),
                      (((xi, y1, z0), (xo, y1, z0), (xo, y1, z1), (xi, y1, z1)), None, (0, 1, 0)),
                      (((xi, y0, z0), (xo, y0, z0), (xo, y1, z0), (xi, y1, z0)), None, (0, 0, -1)),
                      (((xi, y0, z1), (xo, y0, z1), (xo, y1, z1), (xi, y1, z1)), None, (0, 0, 1))):
        face(D, list(a), Z_UNDER if n[2] == 0 else (Z_PAINT if n[2] > 0 else Z_UNDER), n)
    # the window frame (A-pillar edge, top under the roof edge, the B-pillar edge) in the side plane, the glass in it
    def P(y, t, inset=0.0):
        x, z = gh(t, 1)
        return Vector((x - inset, y, z))
    yA0, yA1 = Y_WS + 0.07, Y_RF + 0.06
    D.strip([P(yA0, 0.0), P(yA1, 0.985)], Vector((1, 0, 0.3)), 0.035, 0.03, Z_PAINT)
    D.strip([P(yA1, 0.985), P(B_PIL[0] - 0.01, 0.985)], Vector((1, 0, 0.3)), 0.035, 0.03, Z_PAINT)
    D.strip([P(B_PIL[0] - 0.015, 0.985), P(B_PIL[0] - 0.015, 0.0)], Vector((1, 0, 0.3)), 0.035, 0.03, Z_PAINT)
    D.box(xi + 0.01, xo + 0.008, y0, y1, ZB - 0.012, ZB + 0.012, Z_RUBBER)
    pts = [P(yA0 + 0.03, 0.05, 0.012), P(B_PIL[0] - 0.035, 0.05, 0.012), P(B_PIL[0] - 0.035, 0.96, 0.012), P(yA1 + 0.03, 0.96, 0.012)]
    face(G, pts, ZERO, (1, 0, 0.3))
    face(G, list(reversed(pts)), ZERO, (-1, 0, -0.3))
    # handle outside, pull and lever inside, the mirror on its sail by the A-pillar
    D.box(xo, xo + 0.018, 0.10, 0.24, 0.76, 0.79, Z_CHROME)
    D.box(xi - 0.03, xi, -0.30, -0.05, 0.62, 0.66, Z_CBLACK)
    D.box(xi - 0.012, xi, 0.05, 0.15, 0.70, 0.74, Z_CHROME)
    D.box(xo - 0.01, xo + 0.03, Y_WS + 0.02, Y_WS + 0.10, 0.90, 0.98, Z_CBLACK)
    VM.rbox(D, (xo + 0.11, Y_WS + 0.06, 0.98), (0.16, 0.05, 0.10), Z_CBLACK, r=0.012, ch=0.006, axis='y')
    D.box(xo + 0.03, xo + 0.05, Y_WS + 0.045, Y_WS + 0.075, 0.95, 0.97, Z_CBLACK)
    piv = Vector((CW + 0.01, DOORC['y0'] + 0.02, 0.62))
    part('IX_CarDoor', 'hinge', piv, (0, 0, -1), (0, 70), parent='StartCar', states=['shut', 'open'],
         note='the driver\'s door (front left): hinge at its front edge, opens outward (+) to 70 deg; the window and the mirror '
              'move with it')
    part('IX_CarDoor_Glass', 'static', (CW - 0.03, -0.20, 1.12), parent='IX_CarDoor', note='the door\'s window (glass material)')
    socket('Hand_CarDoor_Out', (xo + 0.05, 0.17, 0.775), 'hand', parent='IX_CarDoor', note='the outside handle')
    socket('Hand_CarDoor_In', (xi - 0.06, 0.10, 0.72), 'hand', parent='IX_CarDoor', note='the inside lever')
    BOXES.append(('CarDoor', 'IX_CarDoor', (xi, xo, DOORC['y0'], DOORC['y1'], DOORC['z0'], ZR), None))


def car_interior():
    k = KBS['StartCar']
    xi = CW - 0.05
    face(k, [(-xi, Y_WS, 0.34), (xi, Y_WS, 0.34), (xi, 1.05, 0.34), (-xi, 1.05, 0.34)], Z_CARPET, (0, 0, 1))
    face(k, [(-xi, 1.05, 0.34), (xi, 1.05, 0.34), (xi, 1.05, 0.56), (-xi, 1.05, 0.56)], Z_CARPET, (0, -1, 0))
    face(k, [(-xi, 1.05, 0.56), (xi, 1.05, 0.56), (xi, 2.22, 0.56), (-xi, 2.22, 0.56)], Z_CARGO, (0, 0, 1))
    face(k, [(-xi, 2.22, 0.56), (xi, 2.22, 0.56), (xi, 2.22, 0.885), (-xi, 2.22, 0.885)], Z_CARD, (0, -1, 0))
    face(k, [(-xi, Y_WS, 0.34), (xi, Y_WS, 0.34), (xi, Y_WS, 0.87), (-xi, Y_WS, 0.87)], Z_CARPET, (0, 1, 0))
    for s in (-1, 1):                                                  # the inner side trims (the left one round the door)
        segs = [(Y_WS, 2.22, 0.34)] if s < 0 else [(Y_WS, DOORC['y0'], 0.34), (DOORC['y1'], 2.22, 0.34)]
        for ya, yb, zz in segs:
            face(k, [(s * xi, ya, zz), (s * xi, yb, zz), (s * xi, yb, 0.885), (s * xi, ya, 0.885)], Z_CARD, (-s, 0, 0))
        if s > 0:
            face(k, [(xi, DOORC['y0'], 0.34), (xi, DOORC['y1'], 0.34), (xi, DOORC['y1'], DOORC['z0']), (xi, DOORC['y0'], DOORC['z0'])],
                 Z_CARD, (-1, 0, 0))
    # dashboard: the main box, the binnacle on the driver's side, the glovebox line is painted
    k.box(-xi, xi, Y_WS, Y_WS + 0.30, 0.60, 0.86, Z_DASH)
    k.box(0.16, 0.60, Y_WS + 0.26, Y_WS + 0.36, 0.72, 0.88, Z_DASH)
    face(k, [(0.20, Y_WS + 0.361, 0.74), (0.56, Y_WS + 0.361, 0.74), (0.56, Y_WS + 0.361, 0.85), (0.20, Y_WS + 0.361, 0.85)], Z_GAUGE,
         (0, 1, 0))
    k.box(-0.10, 0.10, Y_WS + 0.30, Y_WS + 0.62, 0.34, 0.62, Z_DASH)                # the centre console down to the floor
    # the steering wheel on its column
    sc = Vector((0.38, Y_WS + 0.46, 0.80))
    ax = Vector((0, 0.82, 0.57)).normalized()
    k.cyl(sc - ax * 0.30, sc - ax * 0.02, 0.028, Z_CBLACK, 8)
    rim = []
    e1 = Vector((1, 0, 0))
    e2 = ax.cross(e1).normalized()
    for i in range(17):
        a = TAU * i / 16
        rim.append(sc + (e1 * math.cos(a) + e2 * math.sin(a)) * 0.19)
    k.tube(rim, 0.016, Z_CBLACK, 6, caps=False)
    for a in (0, math.pi, -math.pi / 2):
        k.cyl(sc, sc + (e1 * math.cos(a) + e2 * math.sin(a)) * 0.18, 0.012, Z_CBLACK, 5)
    k.cyl(sc - ax * 0.03, sc + ax * 0.03, 0.05, Z_CBLACK, 8)
    # gear stick, pedals (three blocks)
    k.cyl(Vector((0.0, Y_WS + 0.70, 0.62)), Vector((0.0, Y_WS + 0.66, 0.80)), 0.008, Z_CHROME, 5)
    k.box(-0.025, 0.025, Y_WS + 0.635, Y_WS + 0.685, 0.79, 0.84, Z_CBLACK)
    for xp in (0.26, 0.38, 0.50):
        k.box(xp - 0.035, xp + 0.035, Y_WS + 0.12, Y_WS + 0.15, 0.40, 0.48, Z_CBLACK)
    # seats: two buckets and the rear bench (fabric), headrests
    for xc in (-0.38, 0.38):
        VM.rbox(k, (xc, 0.05, 0.47), (0.48, 0.50, 0.12), Z_SEAT, r=0.03, ch=0.015, axis='z')
        k.box(xc - 0.12, xc + 0.12, -0.15, 0.25, 0.34, 0.41, Z_CBLACK)
        VM.obox(k, Vector((xc, 0.33, 0.78)), (0.47, 0.11, 0.60), Z_SEAT, Matrix.Rotation(math.radians(-12), 3, 'X'))
        VM.rbox(k, (xc, 0.40, 1.15), (0.26, 0.09, 0.17), Z_SEAT, r=0.03, ch=0.012, axis='y')
        for xr in (xc - 0.08, xc + 0.08):
            k.cyl(Vector((xr, 0.39, 1.06)), Vector((xr, 0.375, 1.00)), 0.006, Z_CHROME, 4)
    VM.rbox(k, (0.0, 0.84, 0.44), (1.52, 0.44, 0.13), Z_SEAT, r=0.03, ch=0.015, axis='z')
    VM.obox(k, Vector((0.0, 1.04, 0.78)), (1.52, 0.10, 0.56), Z_SEAT, Matrix.Rotation(math.radians(-8), 3, 'X'))
    for s in (-1, 1):                                                  # the rear wheel arches' humps in the load space
        k.box(*sorted((s * 0.60, s * (CW - 0.05))), YRA - RA + 0.04, YRA + RA - 0.04, 0.34, 0.69, Z_CARD)
    # in the back: an old blanket thrown over the cargo floor (a slumped slab)
    VM.obox(k, Vector((0.25, 1.70, 0.60)), (0.70, 0.55, 0.06), Z_SEAT, Matrix.Rotation(math.radians(6), 3, 'Z'))


def tyre(k, c, axis, r=RW, w=RWID, seg=14, zt=Z_TYRE, zr=Z_RIM, rim=True, flat=False):
    """a tyre (lathe about `axis` through c) and a steel wheel with its hub cap; flat: the bottom squashed onto z = 0"""
    ax = Vector(axis).normalized()
    hw = w / 2
    prof = [(-hw, r * 0.62), (-hw * 1.05, r * 0.78), (-hw * 0.95, r * 0.93), (-hw * 0.6, r), (hw * 0.6, r), (hw * 0.95, r * 0.93),
            (hw * 1.05, r * 0.78), (hw, r * 0.62)]
    n0 = len(k.bm.verts)
    k.lathe(c, ax, prof, zt, seg, cap0=False, cap1=False)
    if not rim:                                                       # the tyre's inside, seen through the hole
        k.lathe(c, ax, [(hw, r * 0.62), (-hw, r * 0.62)], zt, seg, cap0=False, cap1=False)
    if rim:
        k.lathe(c + ax * (hw * 0.55), ax, [(0.0, r * 0.62), (0.01, r * 0.50), (0.025, r * 0.30), (0.03, r * 0.20), (0.045, r * 0.19),
                                            (0.05, 0.0)], zr, seg, cap0=False, cap1=False)
        k.lathe(c - ax * (hw * 0.55), -ax, [(0.0, r * 0.62), (0.02, r * 0.35), (0.03, 0.0)], Z_UNDER if zt == Z_TYRE else zt, seg,
                cap0=False, cap1=False)
    if flat:
        k.bm.verts.ensure_lookup_table()
        for v in k.bm.verts[n0:]:
            if v.co.z < 0.07:
                d = (v.co - c).dot(ax)
                bul = 0.035 * (1 - max(v.co.z, 0) / 0.07)
                v.co += ax * (math.copysign(bul, d) if abs(d) > 0.01 else 0.0)
                v.co.z = max(v.co.z, 0.0)


def car_wheels():
    for (x, y), tag in (((TRK, YFA), 'FL'), ((-TRK, YFA), 'FR'), ((TRK, YRA), 'RL'), ((-TRK, YRA), 'RR')):
        k = KBS['StartCar']
        flat = (x, y) == FLAT
        c = Vector((x, y, RW - (DROP if flat else 0.0)))
        tyre(k, c, (1 if x > 0 else -1, 0, 0), flat=flat)


def build_car():
    car_body()
    car_glasshouse()
    car_door()
    car_interior()
    # the body leans to the flat tyre: everything but the wheels turns about the front left wheel (wheels are added after)
    R = body_R()
    for name in ('StartCar', 'Car_Glass', 'IX_CarDoor', 'IX_CarDoor_Glass'):
        transform_kb(KBS[name], R)
    for name in ('IX_CarDoor', 'IX_CarDoor_Glass', 'Car_Glass'):
        p = CONTRACT['parts'][name]
        p['pivot'] = r4(R @ Vector(p['pivot']))
        if p.get('axis'):
            p['axis'] = r4((R.to_3x3() @ Vector(p['axis'])).normalized())
    for sname in ('Hand_CarDoor_Out', 'Hand_CarDoor_In'):
        s = CONTRACT['sockets'][sname]
        s['pos'] = r4(R @ Vector(s['pos']))
    car_wheels()
    triangulate_ngons(KBS['StartCar'])
    part('StartCar', 'static', (0, 0, 0),
         note='the abandoned estate car (root): origin = the middle of its footprint on the ground; the rear right tyre is flat, '
              f'the body leans to it ({ROLL:.1f} deg roll, {PITCHC:.1f} deg pitch); no make, no badges, the plates taken off')
    BOXES.append(('Car', 'StartCar', (-CW - 0.01, CW + 0.01, -2.30, 2.31, 0.12, Z_ROOF), None))


# ================================================================ StartLamp: tripod work light on its own battery
HUB_Z, MAST_TOP = 1.05, 2.02
HEAD_C = Vector((0.0, -0.075, 2.12))
HEAD_TILT = 25.0
BATT = dict(c=Vector((0.48, 0.30, 0.0)), size=(0.46, 0.28, 0.30))


def build_lamp():
    k = reg('StartLamp', 'StartProps', 'StartLamp')
    feet = []
    for a in (90.0, 210.0, 330.0):
        d = Vector((math.cos(math.radians(a)), math.sin(math.radians(a)), 0))
        top = Vector((0, 0, HUB_Z)) + d * 0.045
        foot = d * 0.62 + Vector((0, 0, 0.03))
        k.cyl(top, foot, 0.014, Z_TRIPOD, 6)
        k.box(foot.x - 0.04, foot.x + 0.04, foot.y - 0.04, foot.y + 0.04, 0.0, 0.03, Z_FOOT)
        brace = Vector((0, 0, 0.62)) + d * 0.05
        k.cyl(brace, top.lerp(foot, 0.55), 0.008, Z_TRIPOD, 5)
        feet.append(foot)
    k.cyl(Vector((0, 0, HUB_Z - 0.06)), Vector((0, 0, HUB_Z + 0.06)), 0.05, Z_TRIPOD, 8)
    k.cyl(Vector((0, 0, 0.58)), Vector((0, 0, 1.58)), 0.024, Z_TRIPOD, 8)
    k.cyl(Vector((0, 0, 1.52)), Vector((0, 0, MAST_TOP)), 0.018, Z_GALV if False else Z_TRIPOD, 8)
    k.cyl(Vector((0, 0, 1.55)), Vector((0, 0, 1.61)), 0.032, Z_LHOUSE, 8)
    k.cyl(Vector((0.03, 0, 1.58)), Vector((0.09, 0, 1.58)), 0.008, Z_LHOUSE, 5)
    k.cyl(Vector((0.09, 0, 1.58)), Vector((0.115, 0, 1.58)), 0.02, Z_FOOT, 6)
    # the U bracket on the mast top that holds the head (the head turns in it: tilted down HEAD_TILT)
    k.box(-0.19, 0.19, -0.02, 0.02, MAST_TOP - 0.01, MAST_TOP + 0.015, Z_TRIPOD)
    for sx in (-1, 1):
        k.box(sx * 0.19 - 0.008, sx * 0.19 + 0.008, -0.02, 0.02, MAST_TOP, HEAD_C.z + 0.02, Z_TRIPOD)
        k.cyl(Vector((sx * 0.19, -0.03, HEAD_C.z)), Vector((sx * 0.215, -0.03, HEAD_C.z)), 0.022, Z_FOOT, 8)
    # the battery box on the ground: grey steel, a lid seam, two handles, the terminals under a red / black cap, a switch
    b, (sx, sy, sz) = BATT['c'], BATT['size']
    k.box(b.x - sx / 2, b.x + sx / 2, b.y - sy / 2, b.y + sy / 2, 0.0, sz, Z_BATT)
    k.box(b.x - sx / 2 - 0.006, b.x + sx / 2 + 0.006, b.y - sy / 2 - 0.006, b.y + sy / 2 + 0.006, sz - 0.05, sz - 0.035, Z_BATT)
    for s in (-1, 1):
        k.strip([Vector((b.x + s * (sx / 2 + 0.005), b.y - 0.08, sz - 0.10)), Vector((b.x + s * (sx / 2 + 0.03), b.y - 0.06, sz - 0.10)),
                 Vector((b.x + s * (sx / 2 + 0.03), b.y + 0.06, sz - 0.10)), Vector((b.x + s * (sx / 2 + 0.005), b.y + 0.08, sz - 0.10))],
                Vector((0, 0, 1)), 0.012, 0.012, Z_HANDLE, caps=False)
    for i, xt in enumerate((-0.10, 0.10)):
        k.cyl(Vector((b.x + xt, b.y + 0.02, sz)), Vector((b.x + xt, b.y + 0.02, sz + 0.035)), 0.022, Z_TERM, 8)
    k.box(b.x - 0.03, b.x + 0.03, b.y - sy / 2 - 0.012, b.y - sy / 2, 0.18, 0.24, Z_TERM)
    k.cyl(Vector((b.x, b.y - sy / 2 - 0.012, 0.21)), Vector((b.x, b.y - sy / 2 - 0.035, 0.21)), 0.008, Z_FOOT, 5)
    # the cable: from the box over the ground to the mast foot, clipped up the mast, into the head's back
    pts = [Vector((b.x - 0.10, b.y + 0.02, sz + 0.03)), Vector((b.x - 0.20, b.y + 0.08, sz + 0.05)), Vector((0.30, 0.20, 0.02)),
           Vector((0.12, 0.08, 0.02)), Vector((0.035, 0.02, 0.20)), Vector((0.03, 0.02, 0.60)), Vector((0.03, 0.02, 1.50)),
           Vector((0.025, 0.02, 1.98)), Vector((0.05, 0.03, 2.08)), Vector((0.06, 0.02, HEAD_C.z))]
    k.tube(pts, 0.007, Z_CABLE, 5)
    for zc in (0.70, 1.10, 1.40, 1.85):
        k.cyl(Vector((0.0, 0.0, zc - 0.012)), Vector((0.0, 0.0, zc + 0.012)), 0.034, Z_FOOT, 6)
    # the head: its own part (the light blinks / goes out); housing with fins behind, the lens and the halo its children
    H = reg('Lamp_Flood', 'StartProps', 'StartLamp')
    R = Matrix.Rotation(math.radians(HEAD_TILT), 3, 'X')            # face -y, turned down
    VM.obox(H, HEAD_C, (0.34, 0.12, 0.26), Z_LHOUSE, R)
    for i in range(6):
        fx = -0.13 + i * 0.052
        VM.obox(H, HEAD_C + R @ Vector((fx, 0.075, 0.0)), (0.012, 0.035, 0.22), Z_LHOUSE, R)
    VM.obox(H, HEAD_C + R @ Vector((0.0, -0.064, 0.0)), (0.32, 0.012, 0.24), Z_TRIPOD, R)       # the bezel
    VM.obox(H, HEAD_C + R @ Vector((0.0, -0.02, 0.16)), (0.10, 0.03, 0.05), Z_LHOUSE, R)        # the carry handle's block
    H.strip([HEAD_C + R @ Vector((-0.08, -0.02, 0.13)), HEAD_C + R @ Vector((-0.08, -0.02, 0.19)),
             HEAD_C + R @ Vector((0.08, -0.02, 0.19)), HEAD_C + R @ Vector((0.08, -0.02, 0.13))], R @ Vector((0, 1, 0)), 0.018, 0.018,
            Z_FOOT, caps=False)
    part('Lamp_Flood', 'static', HEAD_C, parent='StartLamp',
         note='the flood head in its U bracket on the mast top, turned down 25 deg: its own part (blink / off in the game); the '
              'lens and the halo are its children; the light LIGHT_Flood')
    Ln = reg('Lens_Flood', 'lens:flood', 'StartLamp')
    lc = HEAD_C + R @ Vector((0.0, -0.071, 0.0))
    quad(Ln, lc, R @ Vector((0, -1, 0)), 0.28, 0.20, uv('l_flood'), up=R @ Vector((0, 0, 1)))
    part('Lens_Flood', 'static', lc, parent='Lamp_Flood', note='the head\'s glass: M_Start_Lamp_Flood (lit) / M_Start_Lamp_Flood_Off')
    Gw = reg('Glow_Flood', 'glow', 'StartLamp')
    quad(Gw, lc + R @ Vector((0, -0.01, 0)), R @ Vector((0, -1, 0)), 0.80, 0.56, uv('glow_white'), up=R @ Vector((0, 0, 1)))
    part('Glow_Flood', 'static', lc, parent='Lamp_Flood', note='the halo card: additive, unlit; shown when the lamp is on')
    fwd = R @ Vector((0, -1, 0))
    LAMPS.append(('Flood', lc + fwd * 0.02, tuple(fwd), 'flood', tuple(R @ Vector((0, 0, 1)))))
    part('StartLamp', 'static', (0, 0, 0), note='the work light on its tripod (root): origin = the tripod\'s middle on the ground; '
                                                 'the battery box stands beside it, the cable runs up the mast')
    socket('Hand_Battery', (b.x, b.y, sz + 0.02), 'hand', parent='StartLamp', note='the battery box\'s lid (a switch on its front)')
    BOXES.extend([('Lamp_Mast', 'StartLamp', (-0.06, 0.06, -0.06, 0.06, 0.0, MAST_TOP), None),
                  ('Lamp_Legs', 'StartLamp', (-0.55, 0.55, -0.32, 0.62, 0.0, 0.50), None),
                  ('Lamp_Head', 'Lamp_Flood', (-0.20, 0.20, -0.16, 0.10, HEAD_C.z - 0.15, HEAD_C.z + 0.16), None),
                  ('Lamp_Battery', 'StartLamp', (b.x - sx / 2, b.x + sx / 2, b.y - sy / 2, b.y + sy / 2, 0.0, sz + 0.035), None)])


# ================================================================ StartTyres
def build_tyres():
    k = reg('StartTyres', 'StartProps', 'StartTyres')
    r, w = 0.30, 0.19
    for i, (dx, dy, yaw) in enumerate(((0.0, 0.0, 0), (0.03, -0.02, 17), (-0.025, 0.02, 41))):
        c = Vector((dx, dy, w / 2 + i * w * 0.97))
        tyre(k, c, (0, 0, 1), r=r, w=w, seg=14, zt=Z_PTYRE, zr=Z_PTYRE, rim=False)
    # the fourth leans on the stack: axis tilted 72 deg from the vertical, toward +x
    tilt = math.radians(72.0)
    ax = Vector((math.sin(tilt), 0.0, math.cos(tilt)))
    c = Vector((0.0, 0.0, 0.0)) + Vector((0.39, 0.05, 0.0))
    c.z = r * math.sin(tilt) + (w / 2) * math.cos(tilt) - 0.02
    c.x = 0.30 + r * math.cos(tilt) + 0.08
    tyre(k, c, ax, r=r, w=w, seg=14, zt=Z_PTYRE, zr=Z_PTYRE, rim=False)
    part('StartTyres', 'static', (0, 0, 0), note='old tyres (root): three stacked, one leaning on them; origin = the stack\'s middle '
                                                  'on the ground')
    BOXES.extend([('Tyres_Stack', 'StartTyres', (-0.32, 0.33, -0.32, 0.32, 0.0, 3 * w * 0.97 + 0.01), None),
                  ('Tyres_Lean', 'StartTyres', (0.30, 0.78, -0.32, 0.36, 0.0, 0.62), None)])


# ================================================================ StartTrash
def hsh(i, s=0):
    x = math.sin(i * 12.9898 + s * 78.233) * 43758.5453
    return x - math.floor(x)


def bag(k, c, r, h, seed):
    """a bin bag: a lumpy lathe tied off at the top, the knot and its ears"""
    n0 = len(k.bm.verts)
    k.lathe(c, (0, 0, 1), [(0.0, r * 0.55), (0.04, r * 0.85), (h * 0.3, r), (h * 0.62, r * 0.92), (h * 0.85, r * 0.55), (h, r * 0.12)],
            Z_BAG, 9, cap0=True, cap1=False)
    k.bm.verts.ensure_lookup_table()
    for j, v in enumerate(k.bm.verts[n0:]):
        d = v.co - c
        f = 1.0 + 0.16 * (hsh(j, seed) - 0.5) + 0.10 * math.sin(d.z * 9 + seed)
        v.co = c + Vector((d.x * f, d.y * f, d.z * (0.92 + 0.12 * hsh(j + 7, seed))))
    top = c + Vector((0, 0, h))
    k.cyl(top - Vector((0, 0, 0.01)), top + Vector((0, 0, 0.05)), 0.03, Z_BAG, 6)
    for a in (0.6, 2.4):
        d = Vector((math.cos(a + seed), math.sin(a + seed), 0.6)).normalized()
        k.cyl(top + Vector((0, 0, 0.04)), top + Vector((0, 0, 0.04)) + d * 0.10, 0.018, Z_BAG, 5, r2=0.006)


def build_trash():
    k = reg('StartTrash', 'StartProps', 'StartTrash')
    for c, r, h, sd in ((Vector((0.0, 0.0, 0.0)), 0.27, 0.52, 1.0), (Vector((0.42, 0.18, 0.0)), 0.24, 0.46, 2.0),
                        (Vector((-0.30, 0.34, 0.0)), 0.22, 0.40, 3.0)):
        bag(k, c, r, h, sd)
    # boards: a broken pallet board and a plank leaning on the bags
    VM.obox(k, Vector((0.10, -0.42, 0.012)), (1.10, 0.10, 0.022), Z_PLANK, Matrix.Rotation(math.radians(12), 3, 'Z'))
    VM.obox(k, Vector((-0.45, -0.20, 0.03)), (0.80, 0.12, 0.025), Z_PLANK,
            Matrix.Rotation(math.radians(-35), 3, 'Z') @ Matrix.Rotation(math.radians(4), 3, 'Y'))
    VM.obox(k, Vector((0.20, 0.42, 0.33)), (0.95, 0.10, 0.022), Z_PLANK,
            Matrix.Rotation(math.radians(70), 3, 'Z') @ Matrix.Rotation(math.radians(38), 3, 'Y'))
    # a flattened box (two panels, a flap up)
    VM.obox(k, Vector((-0.55, 0.15, 0.008)), (0.50, 0.38, 0.012), Z_CARDB, Matrix.Rotation(math.radians(25), 3, 'Z'))
    VM.obox(k, Vector((-0.78, 0.25, 0.10)), (0.40, 0.012, 0.20), Z_CARDB,
            Matrix.Rotation(math.radians(25), 3, 'Z') @ Matrix.Rotation(math.radians(-20), 3, 'X'))
    # cans (one crushed), a bottle, a rusty bucket on its side
    for i, (x, y, lying) in enumerate(((0.62, -0.20, False), (0.48, -0.32, True), (-0.20, -0.45, True))):
        if lying:
            a = math.radians(30 + 50 * i)
            d = Vector((math.cos(a), math.sin(a), 0))
            k.cyl(Vector((x, y, 0.033)) - d * 0.058, Vector((x, y, 0.033)) + d * 0.058, 0.033, Z_CAN, 8)
        else:
            k.cyl(Vector((x, y, 0.0)), Vector((x, y, 0.06)), 0.035, Z_CAN, 8, r2=0.028)
    a = math.radians(-20)
    d = Vector((math.cos(a), math.sin(a), 0))
    b0 = Vector((0.70, 0.30, 0.035))
    k.lathe(b0, d, [(0.0, 0.034), (0.16, 0.034), (0.20, 0.016), (0.25, 0.014)], Z_BOTTLE, 8, cap0=True, cap1=True)
    bk = Vector((-0.62, -0.42, 0.14))
    k.lathe(bk, (1, 0.2, 0), [(-0.14, 0.12), (0.14, 0.15)], Z_RUSTB, 10, cap0=True, cap1=False)
    part('StartTrash', 'static', (0, 0, 0), note='a rubbish heap (root): three bin bags, boards, a flattened box, cans, a bottle, a rusty '
                                                  'bucket; origin = the heap\'s middle on the ground')
    BOXES.append(('Trash', 'StartTrash', (-0.75, 0.80, -0.55, 0.55, 0.0, 0.45), None))


def build_start():
    build_booth()
    build_car()
    build_lamp()
    build_tyres()
    build_trash()

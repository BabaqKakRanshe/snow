"""Generator shed kit, part 1: the registry shared by all gen_* modules (one KB per object + the atlas it bakes into),
the interaction-contract helpers, and the shed itself: concrete slab, board-and-batten walls with the door, window and
louvre openings, gables, rafters, purlins, a corrugated roof with ridge cap, barge boards and fascia; the double door
(outside strap hinges, white Z frames, hasp + padlock, drop bolt and barrel bolt inside), an inside window shutter with
a turn-button, the radiator outlet louvre (six blades driven by one lever), the fixed intake louvre in the gable, the
exhaust stack with a rain flap, the service mast with the LINE and feeder drops, conduits, light switch, outlet, the
lamp fixtures (two caged bulkheads, a barn light over the door), signs, snow on the roof and the drift at the door.
Frame: x east (the louvre end), y north, z up, metres; ground z = 0 at the slab edge, floor FLOOR = 0.10.
Unity: (x, y, z) -> (-y, z, x)."""
import math
import bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix
import van2_kit as VK
from van2_kit import KB, up_axes, rrect, ZERO
import van3_mech as VM
import gen_tex as GT

TAU = 2 * math.pi
FLOOR = 0.10
X0, X1, Y0, Y1 = -2.40, 2.40, -1.70, 1.70           # outer wall faces
T = 0.12                                            # wall thickness
IX0, IX1, IY0, IY1 = X0 + T, X1 - T, Y0 + T, Y1 - T  # inner faces
ZE = 2.45                                           # wall top / rafter seat at the outer face
PITCH = math.radians(25.0)
TP, CP, SP = math.tan(PITCH), math.cos(PITCH), math.sin(PITCH)
OV_E, OV_G = 0.30, 0.20                             # eave / verge overhang
RAFT_D, RAFT_W, PURL, SHEET = 0.14, 0.05, 0.05, 0.03
YE = Y1 + OV_E                                      # eave edge (|y|)
XV = X1 + OV_G                                      # verge edge (|x|)
RAFTERS = [-2.34, -1.56, -0.78, 0.0, 0.78, 1.56, 2.34]
TIES = [-0.73, 0.83]
GY = -0.25                                          # genset centre line (y)
DOOR = dict(y0=-0.80, y1=0.80, z0=FLOOR, z1=2.10)
WIN = dict(x0=-0.40, x1=0.20, z0=1.30, z1=1.80)
LOUV = dict(y0=GY - 0.50, y1=GY + 0.50, z0=0.50, z1=1.40)
INTAKE = dict(y0=-0.35, y1=0.35, z0=2.62, z1=2.92)
EXH_Y, EXH_Z = GY + 0.12, 1.95                      # exhaust through the east wall
STACK_X, STACK_TOP = 2.75, 3.70
BOARD_X = (-1.87, -1.03)                            # switchboard cabinet on the south wall
MAST_X = -1.20


def z_rb(y):
    """rafter bottom line = gable top edge (vertical measure)"""
    return ZE + (Y1 - abs(y)) * TP


def z_rt(y):
    return z_rb(y) + RAFT_D / CP


def z_sb(y):                                        # roof sheet underside
    return z_rb(y) + (RAFT_D + PURL) / CP


def z_st(y):                                        # roof top
    return z_rb(y) + (RAFT_D + PURL + SHEET) / CP


# ---------------------------------------------------------------- zones
# Shed atlas
(Z_SLAB, Z_FLOOR, Z_CLAD, Z_TRIMW, Z_LINING, Z_TIMBER, Z_ROOF, Z_ROOFUNDER, Z_DOOR, Z_DOORIN, Z_STEELBLK, Z_GALV,
 Z_RUSTPIPE, Z_CHEQUER, Z_INSUL, Z_WIRE, Z_ENAMEL, Z_ENAMEL_IN) = range(1, 19)
SHED_ZONES = list(range(1, 19))
Z_SNOW = 90
# Genset atlas
(Z_GPAINT, Z_GIRON, Z_SKID, Z_EXH, Z_ALU2, Z_RAD, Z_RADT, Z_FANB, Z_HOSE2, Z_BELT, Z_STEEL3, Z_COPPER, Z_BRASS2,
 Z_GLASSB, Z_BATTC, Z_LEAD2, Z_WIRER, Z_WIRERB, Z_YEL2, Z_RED2, Z_PANELBOX, Z_RUBBERM, Z_CANVAS2, Z_ALT, Z_FILTERC,
 Z_AIRF, Z_BLACK2, Z_CHROME2) = range(20, 48)
GENSET_ZONES = list(range(20, 48))
# Props atlas
(Z_CAB, Z_CABIN, Z_BRK, Z_MCB, Z_TOGGLE, Z_REDH, Z_TANKP, Z_DRUMB, Z_DRUMR, Z_DRUMTOP, Z_PALLET, Z_PUMP, Z_HOSEP,
 Z_STEELP, Z_BRASSP, Z_WOOD, Z_OILCAN, Z_JUG, Z_BOXC, Z_TOOLBOX, Z_RAG, Z_CABLEJ, Z_EXT, Z_MAT, Z_HARDB, Z_COPPERP,
 Z_CABLER, Z_CABLEB) = range(50, 78)
PROPS_ZONES = list(range(50, 78))

# ---------------------------------------------------------------- registry and contract
KBS, ATLAS = {}, {}
CONTRACT = {'parts': {}, 'sockets': {}}
LAMPS = []                     # (name, pos, dir, type, up) -> lights json
PANEL_N = GT.N


def kb(name, atlas):
    """KB of object `name` (created on first use); atlas: Shed / Genset / Props / Snow (baked) or panel, glass,
    needle, lens:<kind> (explicit materials)"""
    if name not in KBS:
        KBS[name] = KB()
        ATLAS[name] = atlas
    elif ATLAS[name] != atlas:
        raise ValueError(f'{name}: atlas {ATLAS[name]} != {atlas}')
    return KBS[name]


def r4(v):
    return [round(float(a), 4) + 0.0 for a in v]


def part(name, kind, pivot, axis=None, rng=None, rest=0.0, parent=None, unit='deg', states=None, note='', **kw):
    """kind: hinge (rotate about axis through pivot), twist (rotate, then may come off), slide (along axis, metres),
    item (carried), static, spin (free rotation), stretch (scale along axis)"""
    d = dict(kind=kind, pivot=r4(pivot), axis=r4(axis) if axis is not None else None,
             range=list(rng) if rng is not None else None, unit=unit, rest=rest, parent=parent, states=states or [],
             note=note)
    d.update(kw)
    CONTRACT['parts'][name] = d


def socket(name, pos, kind, note='', parent=None, **kw):
    d = dict(pos=r4(pos), kind=kind, parent=parent, note=note)
    for k, v in kw.items():
        d[k] = r4(v) if isinstance(v, (Vector, tuple)) or (isinstance(v, list) and v and isinstance(v[0], (int, float))) else v
    CONTRACT['sockets'][name] = d


def uv(name):
    return GT.uv(name)


# ---------------------------------------------------------------- geometry helpers
def newell(pts):
    n = Vector((0.0, 0.0, 0.0))
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    return n


def face(k, pts, zone, out=None, uvs=None):
    """polygon; reversed when its normal points against `out`"""
    pts = [Vector(p) for p in pts]
    if out is not None and newell(pts).dot(Vector(out)) < 0:
        pts = list(reversed(pts))
        uvs = list(reversed(uvs)) if uvs else uvs
    return k.poly(pts, zone, uvs)


FACES6 = {'-x': ((0, 1, 3, 2), (-1, 0, 0)), '+x': ((4, 6, 7, 5), (1, 0, 0)), '-y': ((0, 4, 5, 1), (0, -1, 0)),
          '+y': ((2, 3, 7, 6), (0, 1, 0)), '-z': ((0, 2, 6, 4), (0, 0, -1)), '+z': ((1, 5, 7, 3), (0, 0, 1))}


def box6(k, lo, hi, zones, skip=()):
    """axis-aligned box lo..hi; zones = {'-x': z, ..., '*': default}; faces in skip are left out (lo / hi are sorted per
    axis: corners given the other way round would otherwise turn the box inside out)"""
    lo, hi = Vector(lo), Vector(hi)
    lo, hi = Vector([min(a, b) for a, b in zip(lo, hi)]), Vector([max(a, b) for a, b in zip(lo, hi)])
    if isinstance(zones, (int, float)):
        zones = {'*': zones}
    c = [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    for key, (q, n) in FACES6.items():
        if key in skip:
            continue
        face(k, [c[i] for i in q], zones.get(key, zones.get('*')), n)


def prism_x(k, poly, x0, x1, z_lo, z_hi, edges=None):
    """polygon in the y-z plane (counter-clockwise, y right z up) extruded from x0 to x1; z_lo = zone of the x0 face,
    z_hi = the x1 face (None skips); edges[i] = zone of the side face from vertex i to i+1 (None skips)"""
    P0 = [Vector((x0, y, z)) for y, z in poly]
    P1 = [Vector((x1, y, z)) for y, z in poly]
    if z_lo is not None:
        face(k, P0, z_lo, (-1, 0, 0))
    if z_hi is not None:
        face(k, P1, z_hi, (1, 0, 0))
    m = len(poly)
    for i in range(m):
        zi = edges[i] if edges is not None else z_hi
        if zi is None:
            continue
        (ya, za), (yb, zb) = poly[i], poly[(i + 1) % m]
        out = Vector((0, zb - za, -(yb - ya)))
        face(k, [P0[i], P0[(i + 1) % m], P1[(i + 1) % m], P1[i]], zi, out)


def prism_y(k, poly, y0, y1, z_lo, z_hi, edges=None):
    """polygon in the x-z plane (counter-clockwise seen from -y: x right z up) extruded from y0 to y1"""
    P0 = [Vector((x, y0, z)) for x, z in poly]
    P1 = [Vector((x, y1, z)) for x, z in poly]
    if z_lo is not None:
        face(k, P0, z_lo, (0, -1, 0))
    if z_hi is not None:
        face(k, P1, z_hi, (0, 1, 0))
    m = len(poly)
    for i in range(m):
        zi = edges[i] if edges is not None else z_hi
        if zi is None:
            continue
        (xa, za), (xb, zb) = poly[i], poly[(i + 1) % m]
        out = Vector((zb - za, 0, -(xb - xa)))
        face(k, [P0[i], P0[(i + 1) % m], P1[(i + 1) % m], P1[i]], zi, out)


def wall_grid(k, axis, a0, a1, us, vs, holes, out_sign, zones, ends=True, top=True):
    """wall slab a0..a1 across `axis` ('x' or 'y'), grid lines us along the wall and vs in z; holes = open cells;
    the outer face is at a1 if out_sign > 0 else at a0.  zones: out, in, reveal, end, top"""
    def P(a, u, v):
        return Vector((a, u, v)) if axis == 'x' else Vector((u, a, v))
    na = Vector((1, 0, 0)) if axis == 'x' else Vector((0, 1, 0))
    nu = Vector((0, 1, 0)) if axis == 'x' else Vector((1, 0, 0))
    ao, ai = (a1, a0) if out_sign > 0 else (a0, a1)
    nI, nJ = len(us) - 1, len(vs) - 1
    for i in range(nI):
        for j in range(nJ):
            if (i, j) in holes:
                continue
            u0, u1, v0, v1 = us[i], us[i + 1], vs[j], vs[j + 1]
            face(k, [P(ao, u0, v0), P(ao, u1, v0), P(ao, u1, v1), P(ao, u0, v1)], zones['out'], na * out_sign)
            face(k, [P(ai, u0, v0), P(ai, u1, v0), P(ai, u1, v1), P(ai, u0, v1)], zones['in'], -na * out_sign)
            for uu, sg, nb, is_end in ((u0, -1, (i - 1, j), i == 0), (u1, 1, (i + 1, j), i == nI - 1)):
                if nb in holes or (is_end and ends):
                    face(k, [P(a0, uu, v0), P(a1, uu, v0), P(a1, uu, v1), P(a0, uu, v1)],
                         zones['reveal'] if nb in holes else zones['end'], nu * sg)
            for vv, sg, nb, is_edge in ((v0, -1, (i, j - 1), False), (v1, 1, (i, j + 1), j == nJ - 1 and top)):
                if nb in holes or is_edge:
                    face(k, [P(a0, u0, vv), P(a1, u0, vv), P(a1, u1, vv), P(a0, u1, vv)],
                         zones['reveal'] if nb in holes else zones['top'], Vector((0, 0, sg)))


def merge_kb(dst, src, M=None):
    """copy the faces of KB src into dst (optionally transformed by the 4x4 M); keeps zones and PSX uvs"""
    vm = {}
    zl, ul = src.zl, src.uvl
    for v in src.bm.verts:
        vm[v] = dst.bm.verts.new((M @ v.co) if M is not None else v.co)
    for f in src.bm.faces:
        nf = dst._f([vm[v] for v in f.verts], f[zl])
        for l0, l1 in zip(f.loops, nf.loops):
            l1[dst.uvl].uv = l0[ul].uv
    src.bm.free()


def transform_kb(k, M):
    bmesh.ops.transform(k.bm, matrix=M, verts=list(k.bm.verts))


def rot_about(p, axis, deg):
    p = Vector(p)
    return Matrix.Translation(p) @ Matrix.Rotation(math.radians(deg), 4, Vector(axis)) @ Matrix.Translation(-p)


def quad(k, c, n, w, h, uvrect, up=(0, 0, 1)):
    return k.quad_uv(Vector(c), Vector(n), w, h, uvrect, up=Vector(up))


def cyl_band(k, c, axis, r, h0, h1, zone, seg=12):
    """open cylinder band (no caps)"""
    k.lathe(Vector(c), Vector(axis), [(h0, r), (h1, r)], zone, seg, cap0=False, cap1=False)


def conduit(k, pts, r=0.011, zone=Z_GALV, clamps=0.6):
    """galvanised conduit along a polyline with saddle clamps every `clamps` metres"""
    pts = [Vector(p) for p in pts]
    k.tube(pts, r, zone, 6)
    for a, b in zip(pts[:-1], pts[1:]):
        L = (b - a).length
        n = int(L / clamps)
        d = (b - a).normalized()
        for i in range(1, n + 1):
            p = a + d * (L * i / (n + 1))
            side = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
            R = VM.frame_R(d, side)
            k.box_m(p, (0.026, 0.004 + 2 * r, 2 * r + 0.006), zone, R)


# ================================================================ the shed shell
def shed_shell():
    k = kb('Shed', 'Shed')
    wz = dict(out=Z_CLAD, reveal=Z_TIMBER, end=Z_CLAD, top=Z_TIMBER)
    # slab: sides, the ring outside the walls, the threshold and the floor
    sx0, sx1, sy0, sy1 = X0 - 0.12, X1 + 0.12, Y0 - 0.12, Y1 + 0.12
    box6(k, (sx0, sy0, 0.0), (sx1, sy1, FLOOR), {'*': Z_SLAB}, skip=('-z', '+z'))
    for lo, hi in (((sx0, Y1, FLOOR), (sx1, sy1, FLOOR)), ((sx0, sy0, FLOOR), (sx1, Y0, FLOOR)),
                   ((sx0, Y0, FLOOR), (X0, Y1, FLOOR)), ((X1, Y0, FLOOR), (sx1, Y1, FLOOR))):
        face(k, [Vector((lo[0], lo[1], FLOOR)), Vector((hi[0], lo[1], FLOOR)), Vector((hi[0], hi[1], FLOOR)),
                 Vector((lo[0], hi[1], FLOOR))], Z_SLAB, (0, 0, 1))
    face(k, [Vector((X0, DOOR['y0'], FLOOR)), Vector((IX0, DOOR['y0'], FLOOR)), Vector((IX0, DOOR['y1'], FLOOR)),
             Vector((X0, DOOR['y1'], FLOOR))], Z_FLOOR, (0, 0, 1))                       # threshold
    xs = [IX0, -1.1, 0.0, 1.1, IX1]                                                      # floor in 4 x 2 quads
    ys = [IY0, 0.0, IY1]
    for i in range(4):
        for j in range(2):
            face(k, [Vector((xs[i], ys[j], FLOOR)), Vector((xs[i + 1], ys[j], FLOOR)),
                     Vector((xs[i + 1], ys[j + 1], FLOOR)), Vector((xs[i], ys[j + 1], FLOOR))], Z_FLOOR, (0, 0, 1))
    # walls below the eave line
    wall_grid(k, 'x', X0, IX0, [Y0, DOOR['y0'], DOOR['y1'], Y1], [FLOOR, DOOR['z1'], ZE], {(1, 0)}, -1,
              dict(wz, **{'in': Z_LINING}), ends=True, top=False)
    wall_grid(k, 'x', IX1, X1, [Y0, LOUV['y0'], LOUV['y1'], Y1], [FLOOR, LOUV['z0'], LOUV['z1'], ZE], {(1, 1)}, 1,
              dict(wz, **{'in': Z_LINING}), ends=True, top=False)
    wall_grid(k, 'y', Y0, IY0, [IX0, WIN['x0'], WIN['x1'], IX1], [FLOOR, WIN['z0'], WIN['z1'], ZE], {(1, 1)}, -1,
              dict(wz, **{'in': Z_LINING}), ends=False, top=True)
    wall_grid(k, 'y', IY1, Y1, [IX0, IX1], [FLOOR, ZE], set(), 1, dict(wz, **{'in': Z_LINING}), ends=False, top=True)
    # gables: east full triangle, west around the intake louvre
    zr = lambda y: z_rb(y)
    prism_x(k, [(Y0, ZE), (Y1, ZE), (0.0, zr(0))], IX1, X1, Z_LINING, Z_CLAD, [None, Z_TIMBER, Z_TIMBER])
    a, b = INTAKE['y0'], INTAKE['y1']
    zl, zh = INTAKE['z0'], INTAKE['z1']
    pieces = [([(Y0, ZE), (a, ZE), (a, zl), (a, zh), (a, zr(a))], [None, None, Z_TIMBER, None, Z_TIMBER]),
              ([(b, ZE), (Y1, ZE), (b, zr(b)), (b, zh), (b, zl)], [None, Z_TIMBER, None, Z_TIMBER, None]),
              ([(a, ZE), (b, ZE), (b, zl), (a, zl)], [None, None, Z_TIMBER, None]),
              ([(a, zh), (b, zh), (b, zr(b)), (0.0, zr(0)), (a, zr(a))], [Z_TIMBER, None, Z_TIMBER, Z_TIMBER, None])]
    for poly, ed in pieces:
        prism_x(k, poly, X0, IX0, Z_CLAD, Z_LINING, ed)
    # corner boards, door / window trims (white)
    cb, ct = 0.14, 0.025
    for sx in (-1, 1):
        for sy in (-1, 1):
            xo, yo = (X1 if sx > 0 else X0), (Y1 if sy > 0 else Y0)
            xa, xb = sorted((xo, xo - sx * cb))
            ya, yb = sorted((yo, yo + sy * ct))
            box6(k, (xa, ya, FLOOR), (xb, yb, ZE), Z_TRIMW)
            xa, xb = sorted((xo, xo + sx * ct))
            ya, yb = sorted((yo + sy * ct, yo - sy * (cb - ct)))
            box6(k, (xa, ya, FLOOR), (xb, yb, ZE), Z_TRIMW)
    for s in (-1, 1):                                                   # door jamb trims and the header
        ya, yb = sorted((s * 0.82, s * 0.96))
        box6(k, (X0 - 0.025, ya, FLOOR), (X0, yb, 2.12), Z_TRIMW, skip=('+x', '-z', '+z'))
    box6(k, (X0 - 0.025, -0.96, 2.12), (X0, 0.96, 2.26), Z_TRIMW, skip=('+x',))
    wx0, wx1, wz0, wz1 = WIN['x0'], WIN['x1'], WIN['z0'], WIN['z1']
    for lo, hi in (((wx0 - 0.07, Y0 - 0.022, wz1), (wx1 + 0.07, Y0, wz1 + 0.07)),
                   ((wx0 - 0.07, Y0 - 0.022, wz0), (wx0, Y0, wz1)), ((wx1, Y0 - 0.022, wz0), (wx1 + 0.07, Y0, wz1))):
        box6(k, lo, hi, Z_TRIMW, skip=('+y',))
    box6(k, (wx0 - 0.09, Y0 - 0.06, wz0 - 0.035), (wx1 + 0.09, Y0, wz0), Z_TRIMW, skip=('+y',))       # outside sill
    box6(k, (wx0 - 0.03, IY0, wz0 - 0.03), (wx1 + 0.03, IY0 + 0.05, wz0), Z_TIMBER, skip=('-y',))     # inside sill
    # window frame in the opening, glazing bars
    fw = 0.045
    for lo, hi in (((wx0, Y0 + 0.02, wz0), (wx1, IY0 - 0.02, wz0 + fw)), ((wx0, Y0 + 0.02, wz1 - fw), (wx1, IY0 - 0.02, wz1)),
                   ((wx0, Y0 + 0.02, wz0 + fw), (wx0 + fw, IY0 - 0.02, wz1 - fw)),
                   ((wx1 - fw, Y0 + 0.02, wz0 + fw), (wx1, IY0 - 0.02, wz1 - fw))):
        box6(k, lo, hi, Z_TRIMW)
    xm, zm = (wx0 + wx1) / 2, (wz0 + wz1) / 2
    box6(k, (xm - 0.015, -1.655, wz0 + fw), (xm + 0.015, -1.625, wz1 - fw), Z_TRIMW)
    box6(k, (wx0 + fw, -1.652, zm - 0.015), (wx1 - fw, -1.628, zm + 0.015), Z_TRIMW)
    g = kb('Window_Glass', 'glass')
    face(g, [Vector((wx0 + fw, -1.64, wz0 + fw)), Vector((wx1 - fw, -1.64, wz0 + fw)), Vector((wx1 - fw, -1.64, wz1 - fw)),
             Vector((wx0 + fw, -1.64, wz1 - fw))], ZERO, (0, -1, 0))
    face(g, [Vector((wx0 + fw, -1.639, wz0 + fw)), Vector((wx1 - fw, -1.639, wz0 + fw)), Vector((wx1 - fw, -1.639, wz1 - fw)),
             Vector((wx0 + fw, -1.639, wz1 - fw))], ZERO, (0, 1, 0))
    part('Window_Glass', 'static', (xm, -1.64, zm), parent='Shed', note='window pane (glass material, both faces)')
    # ---- roof structure
    for xr in RAFTERS + [-(XV - 0.025), XV - 0.025]:
        for s in (1, -1):
            ya, yb = 0.015, YE
            poly = [(s * ya, z_rb(ya)), (s * yb, z_rb(yb)), (s * yb, z_rt(yb)), (s * ya, z_rt(ya))]
            if s < 0:
                poly = list(reversed(poly))
            prism_x(k, poly, xr - RAFT_W / 2, xr + RAFT_W / 2, Z_TIMBER, Z_TIMBER)
    box6(k, (-XV, -0.015, z_rb(0) - 0.03), (XV, 0.015, z_rt(0) + 0.01), Z_TIMBER)                  # ridge board
    for s in (1, -1):                                                   # purlins
        for yp in (0.22, 0.80, 1.38, 1.92):
            w = 0.025
            y0_, y1_ = s * (yp - w), s * (yp + w)
            poly = [(y0_, z_rt(y0_)), (y1_, z_rt(y1_)), (y1_, z_rt(y1_) + PURL / CP), (y0_, z_rt(y0_) + PURL / CP)]
            if s < 0:
                poly = list(reversed(poly))
            prism_x(k, poly, -XV, XV, Z_TIMBER, Z_TIMBER)
    for xt in TIES:                                                     # ceiling ties beside two rafters
        box6(k, (xt - 0.025, IY0, ZE), (xt + 0.025, IY1, ZE + 0.14), Z_TIMBER, skip=('-y', '+y'))
    for s in (1, -1):                                                   # blocking between the rafters over the walls
        ya, yb = (IY1, Y1) if s > 0 else (Y0, IY0)
        for xa, xb in zip(RAFTERS[:-1], RAFTERS[1:]):
            box6(k, (xa + RAFT_W / 2, ya, ZE), (xb - RAFT_W / 2, yb, ZE + 0.17),
                 {('+y' if s > 0 else '-y'): Z_CLAD, '*': Z_TIMBER}, skip=('-x', '+x', '-z'))
    # sheets: top, underside, eave and verge edges (corrugation is in the texture)
    for s in (1, -1):
        ys = [0.0, s * YE]
        top = [Vector((-XV, ys[0], z_st(0))), Vector((XV, ys[0], z_st(0))), Vector((XV, ys[1], z_st(YE))), Vector((-XV, ys[1], z_st(YE)))]
        bot = [Vector((-XV, ys[0], z_sb(0))), Vector((XV, ys[0], z_sb(0))), Vector((XV, ys[1], z_sb(YE))), Vector((-XV, ys[1], z_sb(YE)))]
        nrm = Vector((0, s * SP, CP))
        face(k, top, Z_ROOF, nrm)
        face(k, bot, Z_ROOFUNDER, -nrm)
        face(k, [top[3], top[2], bot[2], bot[3]], Z_GALV, (0, s, 0))
        for xv in (-XV, XV):
            i0, i1 = (0, 3) if xv < 0 else (1, 2)
            face(k, [top[i0], top[i1], bot[i1], bot[i0]], Z_GALV, (1 if xv > 0 else -1, 0, 0))
    for s in (1, -1):                                                   # ridge cap
        y0_, y1_ = 0.0, s * 0.16
        pts = [Vector((-XV - 0.02, y0_, z_st(0) + 0.025)), Vector((XV + 0.02, y0_, z_st(0) + 0.025)),
               Vector((XV + 0.02, y1_, z_st(0.16) + 0.006)), Vector((-XV - 0.02, y1_, z_st(0.16) + 0.006))]
        face(k, pts, Z_GALV, (0, s * SP, CP))
        face(k, [p - Vector((0, 0, 0.004)) for p in pts], Z_GALV, (0, -s * SP, -CP))
        face(k, [pts[3], pts[2], pts[2] - Vector((0, 0, 0.004)), pts[3] - Vector((0, 0, 0.004))], Z_GALV, (0, s, 0))
    for xs_ in (-1, 1):                                                 # barge boards
        xa, xb = sorted((xs_ * XV, xs_ * (XV + 0.025)))
        for s in (1, -1):
            ya, yb = 0.0, s * (YE + 0.025)
            poly = [(ya, z_st(0) + 0.02 - 0.22), (yb, z_st(YE + 0.025) + 0.02 - 0.22), (yb, z_st(YE + 0.025) + 0.02),
                    (ya, z_st(0) + 0.02)]
            if s < 0:
                poly = list(reversed(poly))
            prism_x(k, poly, xa, xb, Z_TRIMW, Z_TRIMW)
    for s in (1, -1):                                                   # fascia
        ya, yb = sorted((s * YE, s * (YE + 0.025)))
        box6(k, (-XV, ya, z_rb(YE) - 0.03), (XV, yb, z_st(YE)), Z_TRIMW, skip=('-x', '+x'))     # between the barge boards
    return k


# ================================================================ door, shutter
def doors():
    zl, zh = 0.11, 2.12
    for name, s in (('Door_L', 1), ('Door_R', -1)):
        k = kb(name, 'Shed')
        yh = s * 0.81
        ya, yb = sorted((yh, 0.0))
        box6(k, (-2.45, ya, zl), (-2.40, yb, zh), {'-x': Z_DOOR, '*': Z_DOORIN})
        xf0, xf1, w = -2.47, -2.45, 0.10
        for lo, hi in (((xf0, ya, zh - w), (xf1, yb, zh)), ((xf0, ya, zl), (xf1, yb, zl + w)),
                       ((xf0, ya, zl + w), (xf1, ya + w, zh - w)), ((xf0, yb - w, zl + w), (xf1, yb, zh - w))):
            box6(k, lo, hi, Z_TRIMW, skip=('+x',))
        yhi = yh - s * w                                                # brace: hinge-side bottom -> meeting-side top
        ymi = s * w
        k.strip([Vector((-2.459, yhi - s * 0.03, zl + w + 0.04)), Vector((-2.459, ymi + s * 0.03, zh - w - 0.04))],
                Vector((1, 0, 0)), 0.10, 0.018, Z_TRIMW)                 # 2 mm behind the rails' faces
        piv = Vector((-2.47, s * 0.815, 0.0))
        for zc in (0.16, 2.07):                                         # strap hinges on the rails + knuckles
            ys0, ys1 = sorted((yh + s * 0.005, yh - s * 0.45))
            box6(k, (-2.476, ys0, zc - 0.025), (-2.47, ys1, zc + 0.025), Z_STEELBLK, skip=('+x',))
            k.cyl(piv + Vector((0, 0, zc - 0.04)), piv + Vector((0, 0, zc + 0.04)), 0.012, Z_STEELBLK, 8)
        # inside D handle
        yhd = s * 0.25
        k.tube([Vector((-2.40, yhd - 0.06, 1.0)), Vector((-2.36, yhd - 0.06, 1.0)), Vector((-2.36, yhd + 0.06, 1.0)),
                Vector((-2.40, yhd + 0.06, 1.0))], 0.009, Z_STEELBLK, 6, caps=False)
        part(name, 'hinge', (piv.x, piv.y, (zl + zh) / 2), (0, 0, -s), (0, 170), parent='Shed',
             states=['shut', 'open'],
             note=('passive leaf: lift Door_DropBolt first; opens after Door_R' if s > 0 else
                   'active leaf: padlock off, hasp open (or Door_Bolt drawn from inside); opens outward'))
        socket('Hand_' + name + '_In', (-2.36, yhd, 1.0), 'hand', parent=name)
    kR, kL = KBS['Door_R'], KBS['Door_L']
    # outside D handle on the active leaf, cover strip over the meeting gap
    kR.tube([Vector((-2.448, -0.31, 1.0)), Vector((-2.50, -0.31, 1.0)), Vector((-2.50, -0.19, 1.0)), Vector((-2.448, -0.19, 1.0))],
            0.010, Z_STEELBLK, 6, caps=False)                            # D pull screwed to the boards
    socket('Hand_Door_R_Out', (-2.51, -0.25, 1.0), 'hand', parent='Door_R')
    # hasp (on Door_R), staple (on Door_L), padlock
    hp = Vector((-2.479, -0.03, 1.10))                                  # hinge on the meeting stile of Door_R
    H = kb('Door_Hasp', 'Shed')
    H.box(-2.484, -2.478, -0.03, 0.085, 1.077, 1.123, Z_STEELBLK)
    H.cyl(hp - Vector((0, 0, 0.024)), hp + Vector((0, 0, 0.024)), 0.006, Z_STEELBLK, 6)
    kR.box(-2.474, -2.47, -0.095, -0.03, 1.07, 1.13, Z_STEELBLK)
    part('Door_Hasp', 'hinge', hp, (0, 0, 1), (0, 170), parent='Door_R', states=['over the staple', 'folded back'],
         note='hasp strap: swings out once the padlock is off the staple')
    socket('Hand_Door_Hasp', (-2.49, 0.07, 1.10), 'hand', parent='Door_Hasp')
    kL.box(-2.474, -2.47, 0.01, 0.075, 1.07, 1.13, Z_STEELBLK)
    st = Vector((-2.47, 0.045, 1.10))
    kL.tube([st + Vector((0, -0.012, 0)), st + Vector((-0.026, -0.012, 0)), st + Vector((-0.032, 0, 0)),
             st + Vector((-0.026, 0.012, 0)), st + Vector((0, 0.012, 0))], 0.0035, Z_GALV, 6, caps=False)
    Pd = kb('Padlock', 'Props')
    pc = Vector((-2.497, 0.045, 1.05))
    VM.rbox(Pd, pc, (0.022, 0.05, 0.045), Z_BRASSP, r=0.006, ch=0.003)
    Pd.tube([pc + Vector((0, -0.016, 0.022)), pc + Vector((0, -0.016, 0.06)), pc + Vector((0, -0.008, 0.07)),
             pc + Vector((0, 0.008, 0.07)), pc + Vector((0, 0.016, 0.06)), pc + Vector((0, 0.016, 0.022))], 0.0042,
            Z_STEELP, 6, caps=False)
    part('Padlock', 'item', pc + Vector((0, 0, 0.065)), (0, 0, 1), parent='Door_L', states=['locked', 'open', 'off'],
         note='brass padlock through the staple and the hasp: Shed_Key opens it, then it lifts off')
    socket('Hand_Padlock', pc, 'hand', parent='Padlock')
    socket('Slot_Padlock', st + Vector((-0.03, 0, 0)), 'item_slot', parent='Door_L', note='the padlock shackle goes through here')
    # inside: drop bolt (Door_L), barrel bolt (Door_R -> keeper on Door_L)
    db = Vector((-2.385, 0.08, 0.05))
    for zc in (0.22, 0.46):
        kL.box(-2.40, -2.372, 0.065, 0.095, zc - 0.02, zc + 0.02, Z_STEELBLK)
    D = kb('Door_DropBolt', 'Shed')
    D.cyl(db, db + Vector((0, 0, 0.56)), 0.0075, Z_STEELBLK, 6)
    D.tube([db + Vector((0, 0, 0.56)), db + Vector((0.035, 0, 0.56)), db + Vector((0.035, 0, 0.60))], 0.006, Z_STEELBLK, 6)
    part('Door_DropBolt', 'slide', db, (0, 0, 1), (0, 0.06), unit='m', parent='Door_L', states=['down (in the slab)', 'up'],
         note='cane bolt inside the passive leaf: drops 5 cm into a hole in the slab; lift it to open Door_L')
    socket('Hand_Door_DropBolt', db + Vector((0.035, 0, 0.58)), 'hand', parent='Door_DropBolt')
    kR.box(-2.40, -2.378, -0.19, -0.07, 1.083, 1.117, Z_STEELBLK)
    kL.box(-2.40, -2.378, 0.02, 0.055, 1.083, 1.117, Z_STEELBLK)
    bb = Vector((-2.389, -0.03, 1.10))
    B = kb('Door_Bolt', 'Shed')
    B.cyl(bb - Vector((0, 0.13, 0)), bb, 0.0065, Z_STEELBLK, 6)
    B.cyl(bb - Vector((0, 0.075, 0)), bb - Vector((-0.03, 0.075, 0)), 0.006, Z_STEELBLK, 6)
    part('Door_Bolt', 'slide', bb, (0, 1, 0), (0, 0.06), unit='m', parent='Door_R', states=['drawn', 'shot'],
         note='barrel bolt inside: shoot it (+0.06) into the keeper on Door_L to lock the doors from inside')
    socket('Hand_Door_Bolt', bb + Vector((0.03, -0.075, 0)), 'hand', parent='Door_Bolt')
    # pintle plates on the jamb trims (shed)
    k = KBS['Shed']
    for s in (1, -1):
        for zc in (0.40, 1.80):
            ya, yb = sorted((s * 0.826, s * 0.90))
            k.box(-2.430, -2.425, ya, yb, zc - 0.05, zc + 0.05, Z_STEELBLK)
            ya, yb = sorted((s * 0.815, s * 0.83))
            k.box(-2.47, -2.43, ya, yb, zc - 0.045, zc - 0.04, Z_STEELBLK)
    # the key on a nail above the door (outside)
    nail = Vector((-2.40, 0.55, 2.30))
    k.cyl(nail, nail + Vector((-0.035, 0, 0.004)), 0.0025, Z_STEELBLK, 5)
    K = kb('Shed_Key', 'Props')
    kc = nail + Vector((-0.022, 0, -0.012))
    K.lathe(kc, (1, 0, 0), [(-0.002, 0.011), (0.002, 0.011)], Z_BRASSP, 8)
    K.box(kc.x - 0.0015, kc.x + 0.0015, kc.y - 0.003, kc.y + 0.003, kc.z - 0.055, kc.z - 0.011, Z_BRASSP)
    K.box(kc.x - 0.0015, kc.x + 0.0015, kc.y, kc.y + 0.008, kc.z - 0.05, kc.z - 0.04, Z_BRASSP)
    part('Shed_Key', 'item', kc, (1, 0, 0), parent='Shed', note='padlock key on a nail above the door, outside (find it with a torch)')
    socket('Nail_Key', nail + Vector((-0.02, 0, 0)), 'item_slot', parent='Shed', note='the key hangs here')


def shutter():
    """inside board shutter, hinged at the window's east edge; modelled open (folded flat on the wall east of the
    window), +180 closes it over the window"""
    k = kb('Window_Shutter', 'Shed')
    x0, x1, z0, z1 = WIN['x0'] - 0.03, WIN['x1'] + 0.03, WIN['z0'] + 0.002, WIN['z1'] + 0.03      # sits on the sill
    yw, yr = -1.575, -1.545                                             # closed: wall side / room side
    box6(k, (x0, yw, z0), (x1, yr, z1), {'+y': Z_DOORIN, '*': Z_DOORIN})
    for zc in (z0 + 0.10, z1 - 0.10):
        box6(k, (x0 + 0.03, yr, zc - 0.035), (x1 - 0.03, yr + 0.02, zc + 0.035), Z_TIMBER, skip=('-y',))
    k.cyl(Vector((x0 + 0.06, yr + 0.02, (z0 + z1) / 2)), Vector((x0 + 0.06, yr + 0.05, (z0 + z1) / 2)), 0.012, Z_TIMBER, 8)
    piv = Vector((x1, yr, (z0 + z1) / 2))
    for zc in (z0 + 0.08, z1 - 0.08):                                   # hinge knuckles
        k.cyl(piv + Vector((0, 0, zc - piv.z - 0.03)), piv + Vector((0, 0, zc - piv.z + 0.03)), 0.008, Z_STEELBLK, 6)
    transform_kb(k, rot_about(piv, (0, 0, 1), 180.0))                    # -> open on the wall east of the window
    part('Window_Shutter', 'hinge', piv, (0, 0, 1), (0, 180), parent='Shed', states=['open (on the wall)', 'closed'],
         note='inside shutter: 0 = folded open on the wall, 180 = over the window; Window_Latch holds it shut')
    socket('Hand_Window_Shutter', piv + Vector((0.60, 0.04, 0.0)), 'hand', parent='Window_Shutter')
    L = kb('Window_Latch', 'Shed')
    lp = Vector((WIN['x0'] - 0.08, -1.545, 1.55))
    box6(KBS['Shed'], (lp.x - 0.012, IY0, lp.z - 0.02), (lp.x + 0.012, lp.y, lp.z + 0.02), Z_TIMBER, skip=('-y',))
    L.box(lp.x - 0.012, lp.x + 0.012, lp.y, lp.y + 0.015, lp.z - 0.08, lp.z + 0.08, Z_TIMBER)
    L.cyl(lp + Vector((0, 0.015, 0)), lp + Vector((0, 0.02, 0)), 0.006, Z_STEELBLK, 6)
    part('Window_Latch', 'hinge', lp, (0, -1, 0), (0, 90), parent='Shed', states=['free', 'holding the shutter'],
         note='turn-button on the wall west of the window: 90 = across the closed shutter edge (+ = clockwise seen from the room)')
    socket('Hand_Window_Latch', lp + Vector((0, 0.02, 0.04)), 'hand', parent='Window_Latch')


# ================================================================ louvres, exhaust stack
BLADE_Z = [LOUV['z0'] + 0.075 + 0.15 * i for i in range(6)]
BLADE_X = (IX1 + X1) / 2
LEVER_P = Vector((IX1 - 0.04, LOUV['y1'] + 0.10, 1.10))


def louvres():
    k = KBS['Shed']
    y0, y1, z0, z1 = LOUV['y0'], LOUV['y1'], LOUV['z0'], LOUV['z1']
    f = 0.04                                                            # inner steel angle frame and outer flashing
    for lo, hi in (((IX1 - f, y0 - f, z1), (IX1, y1 + f, z1 + f)), ((IX1 - f, y0 - f, z0 - f), (IX1, y1 + f, z0)),
                   ((IX1 - f, y0 - f, z0), (IX1, y0, z1)), ((IX1 - f, y1, z0), (IX1, y1 + f, z1))):
        box6(k, lo, hi, Z_STEELBLK, skip=('+x',))
    for lo, hi in (((X1, y0 - 0.05, z1), (X1 + 0.02, y1 + 0.05, z1 + 0.05)), ((X1, y0 - 0.05, z0 - 0.05), (X1 + 0.02, y1 + 0.05, z0)),
                   ((X1, y0 - 0.05, z0), (X1 + 0.02, y0, z1)), ((X1, y1, z0), (X1 + 0.02, y1 + 0.05, z1))):
        box6(k, lo, hi, Z_GALV, skip=('-x',))
    c0 = Vector((math.sin(math.radians(10)), 0, math.cos(math.radians(10))))
    for i, zc in enumerate(BLADE_Z):
        name = f'Louvre_Blade_{i + 1}'
        b = kb(name, 'Shed')
        piv = Vector((BLADE_X, (y0 + y1) / 2, zc))
        R = VM.frame_R(Vector((0, 1, 0)), c0)
        b.box_m(piv, (y1 - y0 - 0.02, 0.16, 0.008), Z_GALV, R)
        b.cyl(Vector((BLADE_X, y0 - 0.005, zc)), Vector((BLADE_X, y1 + 0.005, zc)), 0.006, Z_STEELBLK, 6)
        part(name, 'hinge', piv, (0, 1, 0), (0, 75), parent='Shed', states=['shut', 'open'], links={'from': 'Louvre_Lever', 'ratio': 1.0},
             note='radiator outlet louvre blade: follows Louvre_Lever 1:1 (linkage hidden in the frame)')
    # lever on a quadrant plate standing out from the wall north of the louvre
    p = LEVER_P
    qy = p.y + 0.025
    poly = ([(IX1, p.z + 0.04)] + [(p.x - 0.33 * math.sin(math.radians(a)), p.z - 0.33 * math.cos(math.radians(a)))
                                    for a in range(85, -1, -5)] + [(IX1, p.z - 0.36)])
    # make it counter-clockwise seen from -y (x right, z up)
    area = sum(poly[i][0] * poly[(i + 1) % len(poly)][1] - poly[(i + 1) % len(poly)][0] * poly[i][1] for i in range(len(poly)))
    if area < 0:
        poly = list(reversed(poly))
    prism_y(k, poly, qy, qy + 0.006, Z_STEELBLK, Z_STEELBLK, [Z_STEELBLK] * len(poly))
    k.cyl(p + Vector((0, 0.02, 0)), p + Vector((0, 0.04, 0)), 0.014, Z_STEELBLK, 8)
    L = kb('Louvre_Lever', 'Shed')
    L.box_m(p + Vector((0, 0, -0.15)), (0.03, 0.01, 0.30), Z_STEELBLK)
    L.cyl(p + Vector((0, -0.005, -0.305)), p + Vector((0, -0.005, -0.18)), 0.016, Z_TIMBER, 8)
    L.cyl(p - Vector((0, 0.01, 0)), p + Vector((0, 0.02, 0)), 0.012, Z_STEELBLK, 8)
    part('Louvre_Lever', 'hinge', p, (0, 1, 0), (0, 75), parent='Shed', states=['shut', 'open'],
         note='louvre operator: pull the handle towards the room (0 shut .. 75 open); the six blades follow; '
              'running with it shut overheats the engine (TEMP lamp)')
    socket('Hand_Louvre_Lever', p + Vector((0, -0.005, -0.25)), 'hand', parent='Louvre_Lever')
    # fixed intake louvre in the west gable: white frame outside, four rain blades, steel mesh behind
    a, b, zl, zh = INTAKE['y0'], INTAKE['y1'], INTAKE['z0'], INTAKE['z1']
    for lo, hi in (((X0 - 0.025, a - 0.05, zh), (X0, b + 0.05, zh + 0.05)), ((X0 - 0.025, a - 0.05, zl - 0.05), (X0, b + 0.05, zl)),
                   ((X0 - 0.025, a - 0.05, zl), (X0, a, zh)), ((X0 - 0.025, b, zl), (X0, b + 0.05, zh))):
        box6(k, lo, hi, Z_TRIMW, skip=('+x',))
    for i in range(4):
        zc = zl + 0.045 + i * 0.07
        R = VM.frame_R(Vector((0, 1, 0)), Vector((math.sin(math.radians(45)), 0, math.cos(math.radians(45)))))
        k.box_m(Vector(((X0 + IX0) / 2, (a + b) / 2, zc)), (b - a, 0.085, 0.006), Z_GALV, R)
    face(k, [Vector((IX0 - 0.01, a, zl)), Vector((IX0 - 0.01, b, zl)), Vector((IX0 - 0.01, b, zh)), Vector((IX0 - 0.01, a, zh))],
         Z_STEELBLK, (1, 0, 0))
    # exhaust: thimble plates on both faces, the pipe outside, two stand-off brackets, the stack, the flap
    ey, ez = EXH_Y, EXH_Z
    for xa, xb in ((IX1 - 0.004, IX1), (X1, X1 + 0.004)):
        k.box(xa, xb, ey - 0.13, ey + 0.13, ez - 0.13, ez + 0.13, Z_GALV)
    k.lathe(Vector((IX1 - 0.03, ey, ez)), (1, 0, 0), [(0.0, 0.052), (0.03, 0.052)], Z_GALV, 10, cap0=True, cap1=False)
    sx = STACK_X
    pts = [Vector((IX1 - 0.02, ey, ez)), Vector((sx - 0.07, ey, ez)), Vector((sx - 0.02, ey, ez + 0.02)),
           Vector((sx, ey, ez + 0.07)), Vector((sx, ey, STACK_TOP))]
    k.tube(pts, 0.04, Z_RUSTPIPE, 10, caps=False)
    k.lathe(Vector((sx, ey, STACK_TOP)), (0, 0, 1), [(0.0, 0.04), (0.0, 0.035)], Z_RUSTPIPE, 10, cap0=False, cap1=True)
    for zc in (2.55, 3.05):                                            # stand-offs below the gable edge
        k.box(X1, sx - 0.045, ey - 0.015, ey + 0.015, zc - 0.004, zc + 0.004, Z_STEELBLK)
        k.lathe(Vector((sx, ey, zc - 0.012)), (0, 0, 1), [(0.0, 0.047), (0.024, 0.047)], Z_STEELBLK, 10, cap0=False, cap1=False)
    F = kb('Exhaust_Flap', 'Shed')
    fp = Vector((sx, ey + 0.048, STACK_TOP + 0.006))
    F.lathe(Vector((sx, ey, STACK_TOP + 0.0005)), (0, 0, 1), [(0.0, 0.05), (0.006, 0.05), (0.008, 0.0)], Z_RUSTPIPE, 10, cap0=True)
    k.box(sx - 0.012, sx + 0.012, ey + 0.036, ey + 0.05, STACK_TOP - 0.02, STACK_TOP + 0.004, Z_RUSTPIPE)    # hinge lug
    F.cyl(fp + Vector((-0.02, 0, 0)), fp + Vector((0.02, 0, 0)), 0.006, Z_RUSTPIPE, 6)
    part('Exhaust_Flap', 'hinge', fp, (-1, 0, 0), (0, 70), parent='Shed', states=['shut', 'lifted'],
         note='rain flap on the stack: the exhaust lifts it while the engine runs (flutter 20..60 with the beat), '
              'drops shut when it stops')


# ================================================================ mast, conduits, switch, outlet, floor covers
def services():
    k = KBS['Shed']
    # service entrance on the south wall outside: LB fitting, mast, weatherhead, spool insulators, drops
    mx = MAST_X
    yo = Y0 - 0.04
    k.cyl(Vector((mx, IY0 - 0.02, 2.10)), Vector((mx, yo, 2.10)), 0.02, Z_GALV, 8)
    k.box(mx - 0.035, mx + 0.035, yo - 0.03, yo + 0.03, 2.06, 2.14, Z_GALV)
    k.cyl(Vector((mx, yo, 2.14)), Vector((mx, yo, 2.27)), 0.022, Z_GALV, 8)
    k.lathe(Vector((mx, yo, 2.27)), (0, 0, 1), [(0.0, 0.026), (0.02, 0.05), (0.045, 0.05), (0.06, 0.0)], Z_STEELBLK, 10, cap0=True)
    for zc in (1.95, 2.22):
        k.box(mx - 0.02, mx + 0.02, Y0 - 0.06, Y0, zc - 0.01, zc + 0.01, Z_GALV)
    rack_x = mx + 0.35
    k.box(rack_x - 0.02, rack_x + 0.02, Y0 - 0.01, Y0, 1.98, 2.32, Z_STEELBLK)
    spools = []
    for i, zc in enumerate((2.02, 2.11, 2.20, 2.29)):
        k.box(rack_x - 0.012, rack_x + 0.012, Y0 - 0.07, Y0 - 0.01, zc - 0.006, zc + 0.006, Z_STEELBLK)
        c = Vector((rack_x, Y0 - 0.085, zc))
        k.lathe(c - Vector((0, 0, 0.025)), (0, 0, 1), [(0.0, 0.022), (0.008, 0.026), (0.018, 0.016), (0.032, 0.016),
                                                        (0.042, 0.026), (0.05, 0.022)], Z_INSUL, 10)
        spools.append(c)
    wh = Vector((mx, yo, 2.31))
    for i, c in enumerate(spools):                                      # drip loops from the weatherhead to the spools
        a = wh + Vector((0.01 * (i - 1.5), -0.03, 0.0))
        k.tube([a, a + Vector((0.02, -0.04, -0.12)), (a + c) / 2 + Vector((0, -0.02, -0.18)), c + Vector((-0.03, -0.01, -0.03)),
                c + Vector((0, 0, 0))], 0.006, Z_WIRE, 5, caps=True)
    drop_end = {}
    for name, (i0, i1) in (('LINE', (0, 1)), ('FEED', (2, 3))):
        for i in (i0, i1):
            c = spools[i]
            e = c + Vector((0.10 + 0.05 * i, -0.75, -0.05))
            k.tube([c + Vector((0.01, -0.02, 0)), c + Vector((0.05, -0.30, -0.02)), e], 0.006, Z_WIRE, 5)
        drop_end[name] = spools[i1] + Vector((0.10 + 0.05 * i1, -0.75, -0.05))
    socket('Line_In', drop_end['LINE'], 'item_slot', parent='Shed', dir=(0, -1, 0),
           note='LINE (grid) drop: continue the overhead line from here (rope / line renderer); feeds the board via Board_Brk_Line')
    socket('Feed_Out', drop_end['FEED'], 'item_slot', parent='Shed', dir=(0, -1, 0),
           note='feeder to the base: the eight circuits leave here (draw the cable to the base poles)')
    # conduits inside: board -> mast, board -> board lamp, board -> ceiling lamp, board -> switch -> outlet -> door lamp
    yi = IY0 + 0.025
    conduit(k, [Vector((mx, yi, 2.00)), Vector((mx, yi, 2.10)), Vector((mx, IY0 - 0.01, 2.10))], r=0.02)
    conduit(k, [Vector((-1.45, yi, 2.00)), Vector((-1.45, yi, 2.15))])
    conduit(k, [Vector((-1.62, yi, 2.00)), Vector((-1.62, yi, 2.38)), Vector((TIES[1], yi, 2.38)),
                Vector((TIES[1], yi, ZE - 0.012)), Vector((TIES[1], 0.16, ZE - 0.012))])
    xw = IX0 + 0.025
    conduit(k, [Vector((-1.78, yi, 2.00)), Vector((-1.78, yi, 2.36)), Vector((xw, yi, 2.36)), Vector((xw, -0.98, 2.36)),
                Vector((xw, -0.98, 1.445))])
    conduit(k, [Vector((xw, -0.98, 1.355)), Vector((xw, -0.98, 0.51))])
    conduit(k, [Vector((xw, -0.98, 2.36)), Vector((xw, 0.0, 2.36)), Vector((xw, 0.0, 2.34)), Vector((X0 + 0.01, 0.0, 2.34))])
    # light switch (box on the west wall south of the door) and the outlet under it
    sw = Vector((IX0, -0.98, 1.40))
    box6(k, (IX0, -1.02, 1.355), (IX0 + 0.045, -0.94, 1.445), Z_GALV, skip=('-x',))
    S = kb('Light_Switch', 'Shed')
    sp = sw + Vector((0.045, 0, 0))
    S.cyl(sp, sp + Vector((0.025, 0, -0.012)), 0.005, Z_STEELBLK, 6, r2=0.0035)
    part('Light_Switch', 'hinge', sp, (0, -1, 0), (0, 50), parent='Shed', states=['off (down)', 'on (up)'],
         note='shed lights (both bulkheads) on the SHED circuit (Board_MCB_8): up = on (+50)')
    socket('Hand_Light_Switch', sp + Vector((0.025, 0, 0)), 'hand', parent='Light_Switch')
    box6(k, (IX0, -1.025, 0.39), (IX0 + 0.05, -0.935, 0.51), Z_GALV, skip=('-x',))
    socket('Outlet_Wall', (IX0 + 0.052, -0.98, 0.45), 'item_slot', parent='Shed', dir=(1, 0, 0),
           note='230 V outlet on the SHED circuit: plugs go here (inspection lamp, charger)')
    # floor: chequer plate covers over the cable trench genset -> board
    for lo, hi in (((-0.27, -1.52, FLOOR), (-0.03, -0.62, FLOOR + 0.006)), ((-1.57, -1.52, FLOOR), (-0.27, -1.28, FLOOR + 0.006))):
        box6(k, lo, hi, Z_CHEQUER, skip=('-z',))
    conduit(k, [Vector((-1.45, IY0 + 0.04, FLOOR + 0.006)), Vector((-1.45, IY0 + 0.04, 0.90))], r=0.022, clamps=0.35)
    # extinguisher bracket on the west wall south of the door
    k.box(IX0, IX0 + 0.012, -1.33, -1.27, 0.95, 1.25, Z_STEELBLK)
    k.box(IX0, IX0 + 0.10, -1.36, -1.24, 0.69, 0.70, Z_STEELBLK)


# ================================================================ lamps, signs
def lamps():
    k = KBS['Shed']
    # ceiling bulkhead under the east tie, caged dome
    c = Vector((TIES[1], 0.20, ZE))
    k.box(c.x - 0.045, c.x + 0.045, c.y - 0.045, c.y + 0.045, c.z - 0.01, c.z, Z_STEELBLK)
    k.lathe(c - Vector((0, 0, 0.01)), (0, 0, -1), [(0.0, 0.07), (0.01, 0.085), (0.045, 0.085), (0.05, 0.072)], Z_STEELBLK, 12,
            cap0=True, cap1=False)
    L = kb('Lens_Bulk_Ceiling', 'lens:bulk')
    L.disc_uv(c - Vector((0, 0, 0.058)), Vector((0, 0, -1)), 0.068, uv('l_bulk'), 12, dome=0.04)
    dome_c = c - Vector((0, 0, 0.058))
    for a in (0, 60, 120):                                             # cage bars over the dome
        d = Vector((math.cos(math.radians(a)), math.sin(math.radians(a)), 0))
        pts = [dome_c + d * 0.078 * math.cos(math.radians(t)) - Vector((0, 0, 0.052 * math.sin(math.radians(t))))
               for t in range(0, 181, 30)]
        k.tube(pts, 0.003, Z_STEELBLK, 4, caps=False)
    LAMPS.append(('Bulk_Ceiling', dome_c - Vector((0, 0, 0.03)), (0, 0, -1), 'bulkhead', (0, 1, 0)))   # cookie spokes on the bars
    part('Lens_Bulk_Ceiling', 'static', dome_c - Vector((0, 0, 0.02)), parent='Shed', note='frosted dome of the ceiling bulkhead (lights up with LIGHT_Bulk_Ceiling)')
    # wall bulkhead over the switchboard
    c = Vector((-1.45, IY0, 2.22))
    k.box(c.x - 0.05, c.x + 0.05, c.y, c.y + 0.01, c.z - 0.06, c.z + 0.06, Z_STEELBLK)
    k.lathe(c + Vector((0, 0.01, 0)), (0, 1, 0), [(0.0, 0.07), (0.01, 0.085), (0.045, 0.085), (0.05, 0.072)], Z_STEELBLK, 12,
            cap0=True, cap1=False)
    L = kb('Lens_Bulk_Board', 'lens:bulk')
    dome_c = c + Vector((0, 0.058, 0))
    L.disc_uv(dome_c, Vector((0, 1, 0)), 0.068, uv('l_bulk'), 12, dome=0.04)
    for a in (0, 60, 120):
        d = Vector((math.cos(math.radians(a)), 0, math.sin(math.radians(a))))
        pts = [dome_c + d * 0.078 * math.cos(math.radians(t)) + Vector((0, 0.052 * math.sin(math.radians(t)), 0))
               for t in range(0, 181, 30)]
        k.tube(pts, 0.003, Z_STEELBLK, 4, caps=False)
    LAMPS.append(('Bulk_Board', dome_c + Vector((0, 0.03, 0)), (0, 1, 0), 'bulkhead', (0, 0, 1)))
    part('Lens_Bulk_Board', 'static', dome_c + Vector((0, 0.02, 0)), parent='Shed', note='frosted dome of the wall bulkhead')
    # barn light over the door: gooseneck and an enamel shade
    wp = Vector((X0, 0.0, 2.34))
    k.box(wp.x - 0.012, wp.x, wp.y - 0.05, wp.y + 0.05, wp.z - 0.05, wp.z + 0.05, Z_ENAMEL)
    arm = [wp + Vector((-0.012, 0, 0)), wp + Vector((-0.12, 0, 0.02)), wp + Vector((-0.24, 0, 0.09)),
           wp + Vector((-0.33, 0, 0.13)), wp + Vector((-0.38, 0, 0.11)), wp + Vector((-0.40, 0, 0.085))]
    k.tube(arm, 0.013, Z_ENAMEL, 6)
    sc = Vector((-2.80, 0.0, 2.425))
    k.lathe(sc, (0, 0, -1), [(0.0, 0.03), (0.02, 0.05), (0.06, 0.11), (0.095, 0.165), (0.10, 0.17)], Z_ENAMEL, 14,
            cap0=True, cap1=False)
    k.lathe(sc - Vector((0, 0, 0.002)), (0, 0, -1), [(0.095, 0.162), (0.06, 0.106), (0.02, 0.046), (0.0, 0.026)], Z_ENAMEL_IN, 14,
            cap0=False, cap1=True)
    k.cyl(sc - Vector((0, 0, 0.004)), sc - Vector((0, 0, 0.045)), 0.022, Z_STEELBLK, 8)
    L = kb('Lens_Ext_Door', 'lens:ext')
    bulb_c = sc - Vector((0, 0, 0.045))
    L.disc_uv(bulb_c, Vector((0, 0, -1)), 0.03, uv('l_ext'), 10, dome=0.045)
    LAMPS.append(('Ext_Door', bulb_c - Vector((0, 0, 0.03)), (0, 0, -1), 'barnlight', (-1, 0, 0)))
    part('Lens_Ext_Door', 'static', bulb_c - Vector((0, 0, 0.02)), parent='Shed', note='bulb under the barn-light shade over the door')


def signs():
    S = kb('Shed_Signs', 'panel')
    quad(S, (1.55, IY1 - 0.003, 1.78), (0, -1, 0), 0.20, 0.15, uv('sign_flame'))
    quad(S, (IX1 - 0.003, LEVER_P.y + 0.22, 1.47), (-1, 0, 0), 0.24, 0.06, uv('sign_louvre'))
    part('Shed_Signs', 'static', (0, 0, 0), parent='Shed', note='wall signs (panel atlas)')
    D = kb('Door_L_Sign', 'panel')
    quad(D, (-2.453, 0.47, 1.55), (-1, 0, 0), 0.20, 0.20, uv('sign_danger'))
    part('Door_L_Sign', 'static', (-2.453, 0.47, 1.55), parent='Door_L', note='DANGER 400 V on the passive leaf')
    D = kb('Door_R_Sign', 'panel')
    quad(D, (-2.453, -0.42, 1.86), (-1, 0, 0), 0.36, 0.09, uv('sign_gen'))
    part('Door_R_Sign', 'static', (-2.453, -0.42, 1.86), parent='Door_R', note='GENERATOR on the active leaf')


# ================================================================ snow (optional, starts hidden)
def snow():
    from noise import fbm
    k = kb('Snow_Roof', 'Snow')
    nx, ny = 14, 7
    xs = np.linspace(-XV + 0.04, XV - 0.04, nx + 1)
    for s in (1, -1):
        ys = np.linspace(0.0, YE - 0.06, ny + 1)
        grid_t, grid_b = [], []
        for y in ys:
            rt, rb = [], []
            for x in xs:
                p = np.array([[x, s * y, 0.0]], np.float32)
                edge = min((XV - abs(x)) / 0.25, (YE - y) / 0.30, 1.0)
                th = 0.11 * (0.75 + 0.5 * float(fbm(p, 1.6, 2, seed=91)[0])) * max(0.15, edge) ** 0.5
                zb = z_st(y) + 0.004
                rb.append(Vector((x, s * y, zb)))
                rt.append(Vector((x, s * y, zb + th)))
            grid_t.append(rt)
            grid_b.append(rb)
        for j in range(ny):
            for i in range(nx):
                q = [grid_t[j][i], grid_t[j][i + 1], grid_t[j + 1][i + 1], grid_t[j + 1][i]]
                face(k, q, Z_SNOW, (0, s * SP, CP))
        for j in range(ny):                                            # verge edges
            for i, sg in ((0, -1), (nx, 1)):
                face(k, [grid_b[j][i], grid_b[j + 1][i], grid_t[j + 1][i], grid_t[j][i]], Z_SNOW, (sg, 0, 0))
        for i in range(nx):                                            # eave edge
            face(k, [grid_b[ny][i], grid_b[ny][i + 1], grid_t[ny][i + 1], grid_t[ny][i]], Z_SNOW, (0, s, 0.3))
    part('Snow_Roof', 'static', (0, 0, z_st(0)), parent='Shed', blendshape='Clear', default='hidden',
         states=['0 none', '1 full (~11 cm)'],
         note='snow on the roof: blend shape Clear (100 = no snow: the mesh sinks into the roof sheet, hide the renderer '
              'there); slides off in chunks (VFX) when the stack warms it')
    d = kb('Snow_DoorDrift', 'Snow')
    xs = np.linspace(X0 - 0.95, X0 - 0.075, 9)
    ys = np.linspace(-1.15, 1.15, 13)
    G = []
    for x in xs:
        row = []
        for y in ys:
            u = (x - (X0 - 0.95)) / 0.875
            v = abs(y) / 1.15
            p = np.array([[x, y, 0.0]], np.float32)
            h = 0.70 * u ** 1.4 * max(0.0, 1 - v ** 2.2) ** 0.6 * (0.8 + 0.4 * float(fbm(p, 2.2, 2, seed=92)[0]))
            row.append(Vector((x, y, max(0.006, h))))
        G.append(row)
    for i in range(len(xs) - 1):
        for j in range(len(ys) - 1):
            face(d, [G[i][j], G[i + 1][j], G[i + 1][j + 1], G[i][j + 1]], Z_SNOW, (0, 0, 1))
    for j in range(len(ys) - 1):                                       # the back against the door leaves
        a, b = G[-1][j], G[-1][j + 1]
        face(d, [Vector((a.x, a.y, 0.0)), Vector((b.x, b.y, 0.0)), b, a], Z_SNOW, (1, 0, 0))
    part('Snow_DoorDrift', 'static', (X0 - 0.5, 0.0, 0.0), parent='Shed', blendshape='Clear', default='hidden',
         states=['0 none', '1 drift up to 0.7 m'],
         note='drift against the doors outside: blend shape Clear (100 = dug out, the mesh sinks under the ground: hide '
              'it); the leaves (bottom edge 0.11 m up) sweep over it only from Clear 85, below that they jam after a few '
              'degrees - dig with the van\'s shovel')


def build_shed():
    shed_shell()
    doors()
    shutter()
    louvres()
    services()
    lamps()
    signs()
    snow()
    part('Shed', 'static', (0, 0, 0), parent=None, note='the shed (slab, walls, roof, trims, services); prefab root')

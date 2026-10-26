"""Van v3 mechanics (phase 1): everything a real van has becomes its own part that opens, moves or comes off.
  engine under a hinged cover between the cab seats (inline four: block, head, valve cover with the oil cap, carb and
  the round air filter, distributor and leads, plugs, coil, alternator, fan, belt, dipstick, coolant bottle, fuel pump
  with a sediment bowl); radiator behind a grille that lifts; battery in a box under a folding troop seat with clamps
  and a mass switch; fuse box; ignition key, choke, hand throttle, handbrake; two fuel tanks with filler flaps and caps
  on both sides, tank selector valve; jerrycans and a spout; pedals, gear lever, transfer levers, steering wheel;
  the saloon side door cut free, roof hatch; lighter wheels.
Every moving part is its own object with its pivot at the hinge / axis; CONTRACT collects axes, ranges and sockets.
Body frame (before the 9 cm lift): x forward, y left, z up, metres."""
import math
import bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix
import van2_kit as VK
from van2_kit import KB, up_axes, rrect, fillet, ZERO

# ---------------------------------------------------------------- zones of the Mech atlas
(Z_BAYWALL, Z_IRON, Z_ALU, Z_VCOVER, Z_HOSE, Z_WIRE_B, Z_WIRE_R, Z_BAKELITE, Z_CERAMIC, Z_BRASS, Z_LEAD, Z_BATTCASE,
 Z_FILTER, Z_TANK, Z_JERRY, Z_BOXWOOD, Z_PADRUB, Z_KNOB, Z_REDPLAST, Z_STEEL2, Z_YELLOW, Z_COOLANT, Z_RADCORE,
 Z_GLASSBOWL, Z_FUSEBODY, Z_RUST2) = range(50, 76)
MECH_ZONES = list(range(50, 76))
Z_PAINT, Z_BLACK, Z_RUBBER, Z_LINING = VK.Z_PAINT, VK.Z_BLACK, VK.Z_RUBBER, VK.Z_LINING
Z_CHROME = VK.Z_CHROME

CONTRACT = {'parts': {}, 'sockets': {}}


def part(name, kind, pivot, axis=None, rng=None, rest=0.0, parent=None, unit='deg', states=None, note=''):
    """kind: hinge (rotate about axis through pivot), twist (rotate, then comes off), slide (along axis, metres),
    item (comes off / is carried), static, spin (free rotation)"""
    CONTRACT['parts'][name] = dict(kind=kind, pivot=[round(float(v), 4) for v in pivot],
                                   axis=[round(float(v), 4) for v in axis] if axis is not None else None,
                                   range=list(rng) if rng is not None else None, unit=unit, rest=rest, parent=parent,
                                   states=states or [], note=note)


def socket(name, pos, kind, note='', parent=None):
    CONTRACT['sockets'][name] = dict(pos=[round(float(v), 4) for v in pos], kind=kind, parent=parent, note=note)


# ---------------------------------------------------------------- geometry helpers
def rbox(kb, c, size, zone, r=0.01, ch=0.006, axis='z', caps=True):
    """box with rounded edges along `axis` and chamfered ends; size = (sx, sy, sz) in world axes"""
    c = Vector(c)
    sx, sy, sz = size
    if axis == 'z':
        u, v, n, w, h, L = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)), sx, sy, sz
    elif axis == 'x':
        u, v, n, w, h, L = Vector((0, 1, 0)), Vector((0, 0, 1)), Vector((1, 0, 0)), sy, sz, sx
    else:
        u, v, n, w, h, L = Vector((0, 0, 1)), Vector((1, 0, 0)), Vector((0, 1, 0)), sz, sx, sy
    ch = min(ch, L / 3, w / 3, h / 3)
    rings = [rrect(c - n * (L / 2), u, v, w - 2 * ch, h - 2 * ch, max(0.002, r - ch)),
             rrect(c - n * (L / 2 - ch), u, v, w, h, r), rrect(c + n * (L / 2 - ch), u, v, w, h, r),
             rrect(c + n * (L / 2), u, v, w - 2 * ch, h - 2 * ch, max(0.002, r - ch))]
    kb.loft(rings, [zone] * 3, cap0=zone if caps else None, cap1=zone if caps else None)


def obox(kb, c, size, zone, R):
    """oriented plain box (R columns = local axes); a left-handed R would turn the box inside out (its faces' winding
    follows the frame), so its third axis is flipped - the box is symmetric, only the normals change"""
    R = Matrix(R)
    if R.determinant() < 0:
        R = R.copy()
        for i in range(3):
            R[i][2] = -R[i][2]
    kb.box_m(Vector(c), size, zone, R)


def frame_R(x, y):
    x = Vector(x).normalized()
    y = (Vector(y) - x * Vector(y).dot(x)).normalized()
    return Matrix((x, y, x.cross(y))).transposed()


def cut_box(o, lo, hi, pred=None):
    """bisect the mesh of o along the six planes of the box lo..hi (world = object frame here), then delete the faces
    whose centre lies inside (and pass pred(face) if given); returns the number of faces removed"""
    bm = bmesh.new()
    bm.from_mesh(o.data)
    lo, hi = Vector(lo), Vector(hi)
    pad = 0.02

    def near(f):
        vs = [v.co for v in f.verts]
        return all(min(p[i] for p in vs) < hi[i] + pad and max(p[i] for p in vs) > lo[i] - pad for i in range(3))
    for i in range(3):
        for val in (lo[i], hi[i]):
            no = Vector((0, 0, 0))
            no[i] = 1.0
            co = Vector((0, 0, 0))
            co[i] = val
            geom = [f for f in bm.faces if near(f)]
            geom = list({e for f in geom for e in f.edges}) + geom + list({v for f in geom for v in f.verts})
            if geom:
                bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
    kill = []
    for f in bm.faces:
        c = f.calc_center_median()
        if all(lo[i] < c[i] < hi[i] for i in range(3)) and (pred is None or pred(bm, f)):
            kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    return len(kill)


def split_off(o, pred, name):
    """move the faces of o that pass pred(centre) into a new object (keeps attributes and UVs)"""
    me = o.data.copy()
    new = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(new)
    new.matrix_world = o.matrix_world.copy()
    for ob, keep in ((o, False), (new, True)):
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        dead = [f for f in bm.faces if pred(f.calc_center_median()) != keep]
        bmesh.ops.delete(bm, geom=dead, context='FACES')
        bm.to_mesh(ob.data)
        bm.free()
        ob.data.update()
    new['orig'] = name
    return new


def set_pivot(ob, pivot):
    """move the object origin to pivot without moving the geometry (object transform is identity in the body frame)"""
    p = Vector(pivot)
    ob.data.transform(Matrix.Translation(-p))
    ob.matrix_world = Matrix.Translation(p) @ ob.matrix_world.to_3x3().to_4x4()


# ================================================================ engine bay and engine (between the cab seats)
BAY_LO, BAY_HI = Vector((0.905, -0.265, 0.44)), Vector((1.475, 0.265, 0.91))
COVER = dict(x0=0.905, x1=1.475, hy=0.285, z0=0.91, z1=1.11)


def engine_bay(kb):
    """walls and a skid plate under the hole in the cab floor, a flange round the opening (faces point inwards)"""
    x0, y0, z0 = BAY_LO
    x1, y1, z1 = BAY_HI
    u, v = Vector((1, 0, 0)), Vector((0, 1, 0))
    c0, c1 = Vector(((x0 + x1) / 2, 0, z0)), Vector(((x0 + x1) / 2, 0, z1 - 0.002))
    w, h = x1 - x0, y1 - y0
    rb = [list(reversed(rrect(c0, u, v, w, h, 0.03))), list(reversed(rrect(c1, u, v, w, h, 0.03)))]
    kb.loft(rb, [Z_BAYWALL])                                          # inward walls
    kb.poly(rrect(c0, u, v, w, h, 0.03), Z_BAYWALL)                    # skid plate seen from above
    kb.poly(list(reversed(rrect(c0 - Vector((0, 0, 0.004)), u, v, w, h, 0.03))), Z_BAYWALL)   # and from below
    top = Vector(((x0 + x1) / 2, 0, z1 + 0.003))
    fl = Vector(((x0 + x1 + 0.035) / 2, 0, z1 + 0.003))                # flange: none at the back (partition at 0.888)
    kb.loft([rrect(fl, u, v, w + 0.035, h + 0.07, 0.03), rrect(top, u, v, w, h, 0.03)], [Z_BAYWALL])


def _belt_path(x, pulleys, n=10):
    """closed loop round pulleys [(y, z, r)] (convex, counter-clockwise in the y-z plane), as a polyline at x"""
    P = [np.array(p[:2]) for p in pulleys]
    R = [p[2] for p in pulleys]
    m = len(P)
    pts = []
    for i in range(m):
        a, b = P[i], P[(i + 1) % m]
        d = b - a
        L = np.linalg.norm(d)
        t = d / L
        nrm = np.array([t[1], -t[0]])                                  # outer side (loop counter-clockwise)
        ra, rb = R[i], R[(i + 1) % m]
        s = (ra - rb) / L
        c = math.sqrt(max(0.0, 1 - s * s))
        off = nrm * c + t * s
        pts.append(('t', a + off * ra, b + off * rb, i))
    out = []
    for k in range(m):
        _, p0, p1, i = pts[k]
        _, q0, q1, j = pts[(k + 1) % m]
        out.append(p0)
        out.append(p1)
        cen = P[(i + 1) % m]
        a0 = math.atan2(p1[1] - cen[1], p1[0] - cen[0])
        a1 = math.atan2(q0[1] - cen[1], q0[0] - cen[0])
        while a1 > a0:
            a1 -= 2 * math.pi
        r = R[(i + 1) % m]
        for s_ in range(1, n):
            a = a0 + (a1 - a0) * s_ / n
            out.append(cen + r * np.array([math.cos(a), math.sin(a)]))
    return [Vector((x, float(p[0]), float(p[1]))) for p in out]


def engine(kbs):
    """kbs: dict of KBs by part name (created on demand by the caller); returns nothing, fills CONTRACT"""
    E = kbs('Engine')
    # block, oil pan, head, valve cover
    rbox(E, (1.19, 0.0, 0.62), (0.42, 0.20, 0.28), Z_IRON, r=0.02, ch=0.008)
    rbox(E, (1.19, 0.0, 0.468), (0.40, 0.185, 0.06), Z_IRON, r=0.015, ch=0.006)
    for x in np.linspace(1.03, 1.35, 5):                                # ribs on the block side
        for s in (1, -1):
            E.box(x - 0.008, x + 0.008, s * 0.10 - 0.006, s * 0.10 + 0.006, 0.52, 0.72, Z_IRON)
    rbox(E, (1.19, 0.0, 0.795), (0.43, 0.195, 0.07), Z_ALU, r=0.012, ch=0.006)
    rbox(E, (1.19, 0.0, 0.855), (0.38, 0.150, 0.05), Z_VCOVER, r=0.03, ch=0.012, axis='x')
    for x in (1.03, 1.35):                                              # valve cover bolts
        for s in (1, -1):
            E.cyl((x, s * 0.07, 0.86), (x, s * 0.07, 0.885), 0.006, Z_STEEL2, 6)
    # intake manifold and carburettor (right, -y), exhaust manifold (left, +y)
    rbox(E, (1.19, -0.13, 0.77), (0.32, 0.06, 0.05), Z_ALU, r=0.01, ch=0.004)
    for x in (1.07, 1.15, 1.23, 1.31):
        E.cyl((x, -0.10, 0.78), (x, -0.13, 0.77), 0.016, Z_ALU, 8)
    rbox(E, (1.19, -0.15, 0.84), (0.09, 0.08, 0.08), Z_ALU, r=0.012, ch=0.004)
    E.tube([(1.19, 0.12, 0.73), (1.03, 0.125, 0.73), (0.98, 0.13, 0.69), (0.96, 0.15, 0.58)], 0.028, Z_RUST2, 8)
    for x in (1.07, 1.15, 1.23, 1.31):
        E.cyl((x, 0.10, 0.75), (x, 0.125, 0.73), 0.016, Z_RUST2, 8)
    # air filter: the big round pan on the carburettor (the lid comes off after the wing nut)
    fc = Vector((1.19, -0.13, 0.90))
    E.lathe(fc, (0, 0, 1), [(0.0, 0.03), (0.0, 0.108), (0.008, 0.120), (0.060, 0.120), (0.064, 0.112)], Z_FILTER, 18,
            cap0=True, cap1=False)
    E.cyl((1.19, -0.15, 0.885), (1.19, -0.15, 0.905), 0.04, Z_FILTER, 10)
    E.lathe(fc + Vector((0, 0, 0.004)), (0, 0, 1), [(0.0, 0.112), (0.052, 0.112), (0.052, 0.062), (0.0, 0.062)],
            Z_CERAMIC, 18, cap0=False, cap1=False)                              # pleated paper element
    L = kbs('Engine_AirFilterLid')
    L.lathe(fc, (0, 0, 1), [(0.062, 0.124), (0.068, 0.125), (0.076, 0.114), (0.082, 0.032), (0.082, 0.0)], Z_FILTER, 18,
            cap0=False, cap1=False)
    L.cyl(fc + Vector((0, 0, 0.082)), fc + Vector((0, 0, 0.094)), 0.012, Z_STEEL2, 6)
    L.box_m(fc + Vector((0, 0, 0.098)), (0.05, 0.010, 0.012), Z_STEEL2)          # wing nut
    part('Engine_AirFilterLid', 'twist', fc + Vector((0, 0, 0.088)), (0, 0, 1), (0, 120), note='wing nut, then lift off', parent='Engine')
    # distributor with its cap, coil, leads and plugs (left, +y)
    E.cyl((1.00, 0.14, 0.70), (1.00, 0.14, 0.79), 0.032, Z_ALU, 10)
    dc = Vector((1.00, 0.14, 0.79))
    D = kbs('Engine_DistributorCap')
    D.lathe(dc, (0, 0, 1), [(0.0, 0.040), (0.028, 0.040), (0.042, 0.026), (0.047, 0.0)], Z_BAKELITE, 12, cap0=True)
    towers = []
    for k in range(4):
        a = math.radians(45 + 90 * k)
        t = dc + Vector((0.026 * math.cos(a), 0.026 * math.sin(a), 0.034))
        D.cyl(t, t + Vector((0, 0, 0.014)), 0.0065, Z_BAKELITE, 6)
        towers.append(t + Vector((0, 0, 0.014)))
    D.cyl(dc + Vector((0, 0, 0.046)), dc + Vector((0, 0, 0.060)), 0.007, Z_BAKELITE, 6)
    part('Engine_DistributorCap', 'item', dc, (0, 0, 1), note='two spring clips, then lift (leads stay on)', parent='Engine')
    E.cyl((1.38, 0.215, 0.80), (1.47, 0.215, 0.80), 0.028, Z_KNOB, 10)        # ignition coil on the bay wall
    E.box(1.39, 1.46, 0.24, 0.262, 0.77, 0.83, Z_STEEL2)
    E.tube([(1.38, 0.215, 0.80), (1.25, 0.19, 0.90), (1.05, 0.15, 0.88), dc + Vector((0, 0, 0.06))], 0.005, Z_WIRE_B, 6)
    order = [0, 2, 3, 1]
    for i, x in enumerate((1.05, 1.145, 1.24, 1.335)):
        base = Vector((x, 0.099, 0.80))
        d = Vector((0, 0.6, 0.8)).normalized()
        pk = kbs(f'Engine_Plug_{i + 1}')
        pk.cyl(base, base + d * 0.012, 0.010, Z_STEEL2, 6)
        pk.cyl(base + d * 0.012, base + d * 0.040, 0.0065, Z_CERAMIC, 8)
        part(f'Engine_Plug_{i + 1}', 'item', base, d, (0, 0.06), unit='m', parent='Engine',
             note='unscrew (6 turns) along the axis, then free; the lead must be off')
        ld = kbs(f'Engine_Lead_{i + 1}')
        top = base + d * 0.040
        ld.cyl(top - d * 0.004, top + d * 0.03, 0.0085, Z_WIRE_B, 6)                 # boot
        t = towers[order[i]]
        mid = (top + d * 0.03 + t) / 2 + Vector((0, 0.02, 0.07))
        ld.tube([top + d * 0.03, top + d * 0.06, mid, t + Vector((0, 0, 0.03)), t], 0.0045, Z_WIRE_R, 6)
        part(f'Engine_Lead_{i + 1}', 'slide', top, d, (0, 0.04), unit='m', parent='Engine', note='boot off the plug')
        socket(f'Hand_Plug_{i + 1}', top + d * 0.02, 'hand', parent=f'Engine_Lead_{i + 1}')
    part('Engine', 'static', (1.19, 0, 0.62))
    # front: crank pulley, water pump with the fan, alternator, the belt
    E.cyl((1.41, 0, 0.72), (1.445, 0, 0.72), 0.040, Z_IRON, 10)
    E.cyl((1.41, -0.15, 0.62), (1.43, -0.15, 0.62), 0.055, Z_ALU, 12)
    E.cyl((1.31, -0.15, 0.62), (1.41, -0.15, 0.62), 0.055, Z_ALU, 12)
    for x0_, r_ in ((1.43, 0.07), ):
        E.cyl((x0_, 0, 0.52), (x0_ + 0.022, 0, 0.52), r_, Z_STEEL2, 16)
    E.cyl((1.445, 0, 0.72), (1.462, 0, 0.72), 0.05, Z_STEEL2, 14)
    E.cyl((1.43, -0.15, 0.62), (1.452, -0.15, 0.62), 0.032, Z_STEEL2, 10)
    B = kbs('Engine_Belt')
    path = _belt_path(1.452, [(0.0, 0.52, 0.071), (-0.15, 0.62, 0.033), (0.0, 0.72, 0.051)], n=6)
    B.strip(path + [path[0]], (1, 0, 0), 0.006, 0.014, Z_HOSE, caps=False)
    part('Engine_Belt', 'static', (1.452, 0, 0.62), states=['ok', 'loose', 'broken'],
         note='fan belt: broken = hide the object, loose = slips (sound), the alternator light comes on')
    F = kbs('Engine_Fan')
    fcn = Vector((1.455, 0, 0.72))
    F.cyl(fcn, fcn + Vector((0.012, 0, 0)), 0.03, Z_STEEL2, 8)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        dirv = Vector((0, math.cos(a), math.sin(a)))
        tang = Vector((0, -math.sin(a), math.cos(a)))
        R = Matrix((Vector((1, 0, 0)), dirv, tang)).transposed() @ Matrix.Rotation(math.radians(25), 3, 'Y')
        F.box_m(fcn + Vector((0.006, 0, 0)) + dirv * 0.09, (0.004, 0.12, 0.05), Z_STEEL2, R)
    part('Engine_Fan', 'spin', fcn, (1, 0, 0), note='turns with the engine (rpm x 1.2)')
    # dipstick (front right): the yellow ring pulls the stick out of its tube
    t0, t1 = Vector((1.36, -0.105, 0.56)), Vector((1.42, -0.205, 0.925))
    E.tube([t0, t1], 0.006, Z_STEEL2, 6)
    ax = (t1 - t0).normalized()
    K = kbs('Engine_Dipstick')
    ring_c = t1 + ax * 0.028
    rr = []
    for k in range(13):
        a = 2 * math.pi * k / 12
        rr.append(ring_c + Vector((0, 0, 0)) + (Vector((1, 0, 0)) * math.cos(a) * 0.018 + ax * math.sin(a) * 0.018))
    K.tube(rr, 0.004, Z_YELLOW, 6, caps=False)
    K.tube([t1 + ax * 0.012, t1 - ax * 0.40], 0.0022, Z_STEEL2, 4)
    part('Engine_Dipstick', 'slide', t1, ax, (0, 0.42), unit='m', parent='Engine',
         note='pull out, the oil level is drawn on the stick (texture), wipe, push back')
    socket('Hand_Dipstick', ring_c, 'hand', parent='Engine_Dipstick')
    # oil filler cap on the valve cover
    oc = Vector((1.33, 0.03, 0.88))
    O = kbs('Engine_OilCap')
    O.lathe(oc, (0, 0, 1), [(0.0, 0.024), (0.016, 0.024), (0.020, 0.018)], Z_KNOB, 12)
    O.box_m(oc + Vector((0, 0, 0.026)), (0.04, 0.008, 0.012), Z_KNOB)
    part('Engine_OilCap', 'twist', oc, (0, 0, 1), (0, 90), parent='Engine', note='quarter turn, then lift: oil goes in here')
    socket('Pour_Oil', oc + Vector((0, 0, 0.03)), 'pour', parent='Engine')
    # coolant bottle (rear right) with its cap
    rbox(E, (0.975, -0.205, 0.80), (0.08, 0.07, 0.15), Z_COOLANT, r=0.015, ch=0.008)
    E.cyl((0.975, -0.205, 0.875), (0.975, -0.205, 0.89), 0.014, Z_COOLANT, 8)
    E.tube([(0.975, -0.17, 0.74), (1.05, -0.12, 0.70), (1.12, -0.09, 0.79)], 0.008, Z_HOSE, 6)
    cc = Vector((0.975, -0.205, 0.89))
    Cc = kbs('Engine_CoolantCap')
    Cc.lathe(cc, (0, 0, 1), [(0.0, 0.019), (0.014, 0.019), (0.017, 0.014)], Z_KNOB, 10)
    part('Engine_CoolantCap', 'twist', cc, (0, 0, 1), (0, 270), parent='Engine', note='screw cap; hot engine = steam')
    socket('Pour_Coolant', cc + Vector((0, 0, 0.03)), 'pour', parent='Engine')
    # fuel pump with a glass sediment bowl and the hand primer lever (left front)
    rbox(E, (1.36, 0.135, 0.60), (0.06, 0.05, 0.06), Z_ALU, r=0.01, ch=0.004)
    E.lathe(Vector((1.36, 0.135, 0.57)), (0, 0, -1), [(0.0, 0.026), (0.03, 0.026), (0.045, 0.018), (0.05, 0.0)],
            Z_GLASSBOWL, 10, cap0=False)
    Pl = kbs('Engine_PrimerLever')
    pp = Vector((1.39, 0.165, 0.605))
    Pl.box_m(pp + Vector((0.0, 0.0, -0.025)), (0.008, 0.008, 0.05), Z_STEEL2)
    part('Engine_PrimerLever', 'hinge', pp, (0, 1, 0), (0, 35), parent='Engine', note='hand primer: pump to fill the carb')
    E.tube([(1.36, 0.135, 0.63), (1.30, 0.10, 0.70), (1.21, -0.05, 0.86), (1.19, -0.12, 0.86)], 0.004, Z_STEEL2, 5)
    # starter, upper hose, engine mounts
    E.cyl((0.92, 0.12, 0.52), (1.06, 0.12, 0.52), 0.045, Z_KNOB, 10)
    E.tube([(1.40, 0.0, 0.83), (1.45, 0.0, 0.83), (1.475, 0.0, 0.80)], 0.019, Z_HOSE, 8)
    for x in (1.00, 1.38):
        for s in (1, -1):
            E.box(x - 0.025, x + 0.025, s * 0.10, s * 0.25, 0.50, 0.53, Z_IRON)


def engine_cover(kb):
    """padded cover over the engine, hinged at its rear top edge (towards the partition): two latches in front"""
    x0, x1, hy, z0, z1 = COVER['x0'], COVER['x1'], COVER['hy'], COVER['z0'], COVER['z1']
    u, v, n = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
    c = Vector(((x0 + x1) / 2, 0, 0))
    w, h = x1 - x0, 2 * hy
    rings = [rrect(c + n * z0, u, v, w, h, 0.03), rrect(c + n * (z1 - 0.03), u, v, w, h, 0.03),
             rrect(c + n * (z1 - 0.005), u, v, w - 0.04, h - 0.04, 0.02), rrect(c + n * z1, u, v, w - 0.08, h - 0.08, 0.02)]
    kb.loft(rings, [Z_LINING, Z_LINING, Z_LINING], cap1=Z_LINING)
    inner = [list(reversed(rrect(c + n * z0, u, v, w - 0.03, h - 0.03, 0.02))),
             list(reversed(rrect(c + n * (z1 - 0.02), u, v, w - 0.03, h - 0.03, 0.02)))]
    kb.loft(inner, [Z_BAYWALL])
    kb.poly(list(reversed(rrect(c + n * (z1 - 0.02), u, v, w - 0.03, h - 0.03, 0.02))), Z_BAYWALL)
    kb.loft([rrect(c + n * z0, u, v, w - 0.03, h - 0.03, 0.02), rrect(c + n * z0, u, v, w, h, 0.03)], [Z_BAYWALL])   # rim, faces down
    for s in (1, -1):                                                     # latches on the front face
        kb.box(x1 - 0.004, x1 + 0.012, s * 0.18 - 0.02, s * 0.18 + 0.02, z0 + 0.10, z0 + 0.15, Z_STEEL2)
    piv = Vector((x0, 0.0, z1))
    part('EngineCover', 'hinge', piv, (0, -1, 0), (0, 85), states=['latched', 'unlatched', 'open'],
         note='two latches in front, then the front edge lifts towards the partition')
    socket('Hand_EngineCover', (x1 + 0.01, 0.0, z0 + 0.13), 'hand', parent='EngineCover')
    return piv


# ================================================================ electrics
BATT_BOX = dict(x0=0.24, x1=0.54, y0=0.56, y1=0.86, z0=0.716, z1=0.98)


def battery_box(kbs):
    """wooden box on the floor under the folding seat L4 (fold the seat up to reach the lid); the lid is hinged on the
    aisle side; inside: the battery with its posts, cell caps and strap, two clamps on cables; the mass switch on the
    front face (reachable with the seat down)"""
    b = BATT_BOX
    W = kbs('BatteryBox')
    t = 0.018
    cx, cy = (b['x0'] + b['x1']) / 2, (b['y0'] + b['y1']) / 2
    # four plank walls and a floor (planks drawn by the painter)
    W.box(b['x0'], b['x1'], b['y0'], b['y0'] + t, b['z0'], b['z1'], Z_BOXWOOD)
    W.box(b['x0'], b['x1'], b['y1'] - t, b['y1'], b['z0'], b['z1'], Z_BOXWOOD)
    W.box(b['x0'], b['x0'] + t, b['y0'] + t, b['y1'] - t, b['z0'], b['z1'], Z_BOXWOOD)
    W.box(b['x1'] - t, b['x1'], b['y0'] + t, b['y1'] - t, b['z0'], b['z1'], Z_BOXWOOD)
    W.box(b['x0'] + t, b['x1'] - t, b['y0'] + t, b['y1'] - t, b['z0'], b['z0'] + 0.01, Z_BOXWOOD)
    for x in (b['x0'] + 0.01, b['x1'] - 0.01):                          # corner battens
        for y in (b['y0'] + 0.002, b['y1'] - 0.002):
            W.box(x - 0.012, x + 0.012, y - 0.012, y + 0.012, b['z0'], b['z1'] - 0.005, Z_STEEL2)
    # mass switch on the front face: base plate and the red key
    ms = Vector((b['x1'] - 0.07, b['y0'] - 0.002, b['z0'] + 0.17))
    W.cyl(ms, ms - Vector((0, 0.012, 0)), 0.032, Z_KNOB, 12)
    K = kbs('MassSwitch_Key')
    K.cyl(ms - Vector((0, 0.012, 0)), ms - Vector((0, 0.030, 0)), 0.012, Z_REDPLAST, 8)
    K.box_m(ms - Vector((0, 0.040, 0)), (0.07, 0.016, 0.022), Z_REDPLAST)
    part('MassSwitch_Key', 'twist', ms, (0, 1, 0), (0, 90), parent='BatteryBox', states=['off', 'on'],
         note='battery isolator: 0 = off, 90 = on, clockwise seen from the aisle (the axis points into the box); '
              'the key comes out in the off position')
    socket('Hand_MassSwitch', ms - Vector((0, 0.045, 0)), 'hand', parent='MassSwitch_Key')
    # lid: hinged at the wall, the aisle edge lifts (65 deg: it then leans under the folded seat L4)
    Ld = kbs('BatteryBox_Lid')
    Ld.box(b['x0'] - 0.005, b['x1'] + 0.005, b['y0'] - 0.005, b['y1'] + 0.005, b['z1'], b['z1'] + 0.018, Z_BOXWOOD)
    Ld.box(cx - 0.05, cx + 0.05, b['y0'] + 0.04, b['y0'] + 0.06, b['z1'] + 0.018, b['z1'] + 0.03, Z_STEEL2)   # handle
    for x in (b['x0'] + 0.04, b['x1'] - 0.04):                         # hinge straps at the wall edge
        Ld.box(x - 0.015, x + 0.015, b['y1'] - 0.07, b['y1'] + 0.006, b['z1'] + 0.018, b['z1'] + 0.022, Z_STEEL2)
    piv = Vector((cx, b['y1'] + 0.005, b['z1'] + 0.009))
    part('BatteryBox_Lid', 'hinge', piv, (-1, 0, 0), (0, 65), parent='BatteryBox',
         note='hinged at the wall, the aisle edge lifts; fold seat L4 up first (its pan is above the lid)')
    socket('Hand_BatteryLid', (cx, b['y0'] + 0.05, b['z1'] + 0.03), 'hand', parent='BatteryBox_Lid')
    # the battery: case, lid with six cell caps, two posts, carry strap
    B = kbs('Battery')
    bc = Vector((cx, cy, b['z0'] + 0.01))
    rbox(B, bc + Vector((0, 0, 0.0925)), (0.278, 0.175, 0.185), Z_BATTCASE, r=0.008, ch=0.004)
    B.box(bc.x - 0.135, bc.x + 0.135, bc.y - 0.083, bc.y + 0.083, bc.z + 0.185, bc.z + 0.195, Z_BATTCASE)
    for k in range(6):
        x = bc.x - 0.10 + 0.04 * k
        B.cyl((x, bc.y + 0.035, bc.z + 0.195), (x, bc.y + 0.035, bc.z + 0.203), 0.011, Z_BATTCASE, 8)
    posts = {}
    for name, dx in (('Pos', -0.105), ('Neg', 0.105)):
        p0 = Vector((bc.x + dx, bc.y - 0.04, bc.z + 0.195))
        B.cyl(p0, p0 + Vector((0, 0, 0.022)), 0.0095, Z_LEAD, 8, r2=0.0085)
        posts[name] = p0 + Vector((0, 0, 0.022))
    B.tube([(bc.x - 0.13, bc.y, bc.z + 0.12), (bc.x - 0.12, bc.y, bc.z + 0.26), (bc.x + 0.12, bc.y, bc.z + 0.26),
            (bc.x + 0.13, bc.y, bc.z + 0.12)], 0.006, Z_KNOB, 6)
    part('Battery', 'item', bc, (0, 0, 1), parent='BatteryBox', states=['charge 0..1'],
         note='12 V 60 Ah, 18 kg: lift by the strap when both clamps are off; charge at the base')
    socket('Hand_Battery', (bc.x, bc.y, bc.z + 0.26), 'hand', parent='Battery')
    # clamps on cables (red +, black -): each lifts off its post; the cables leave through the box floor
    for name, zone in (('Pos', Z_WIRE_R), ('Neg', Z_WIRE_B)):
        Cl = kbs(f'Battery_Clamp{name}')
        p = posts[name]
        Cl.cyl(p - Vector((0, 0, 0.018)), p + Vector((0, 0, 0.004)), 0.016, Z_LEAD, 8)
        Cl.box_m(p + Vector((0, -0.028, -0.007)), (0.02, 0.04, 0.012), Z_LEAD)
        Cl.cyl(p + Vector((0, -0.012, -0.007)), p + Vector((0.03, -0.012, -0.007)), 0.0045, Z_STEEL2, 6)   # bolt
        end = p + Vector((0, -0.05, -0.007))
        Cl.tube([end, end + Vector((0, -0.02, -0.02)), Vector((end.x, b['y0'] + 0.03, b['z0'] + 0.06)),
                 Vector((end.x, b['y0'] + 0.03, b['z0'] + 0.012))], 0.007, zone, 6)
        if name == 'Pos':
            Cl.box_m(p + Vector((0, 0, 0.012)), (0.036, 0.036, 0.014), Z_REDPLAST)       # red boot
        part(f'Battery_Clamp{name}', 'slide', p, (0, 0, 1), (0, 0.04), unit='m', parent='BatteryBox',
             note='loosen the bolt, lift off the post (minus first)')
        socket(f'Hand_Clamp{name}', p + Vector((0, 0, 0.01)), 'hand', parent=f'Battery_Clamp{name}')


def fuse_box(kbs, on_face, dash_n):
    """fuse box hanging under the dash on the passenger side: clear lid hinged at the bottom, eight glass fuses"""
    c = Vector((1.715, -0.30, 1.005))
    n = Vector((-1, 0, 0))
    R = Matrix((Vector((0, -1, 0)), Vector((0, 0, 1)), n)).transposed()          # u = across, v = up, n = to the cab
    Fb = kbs('FuseBox')
    Fb.box_m(c, (0.18, 0.10, 0.05), Z_FUSEBODY, R)
    Fb.box(1.73, 1.745, -0.37, -0.23, 1.05, 1.062, Z_FUSEBODY)                      # bracket to the dash
    Ld = kbs('FuseBox_Lid')
    Ld.box_m(c + n * 0.027, (0.184, 0.104, 0.004), Z_FUSEBODY, R)
    piv = c + n * 0.027 - Vector((0, 0, 0.052))
    part('FuseBox_Lid', 'hinge', piv, (0, -1, 0), (0, 100), parent='FuseBox', note='drops down towards the passenger')
    for k in range(8):
        row, col = divmod(k, 4)
        p = c + n * 0.026 + R @ Vector((-0.06 + 0.04 * col, 0.022 - 0.044 * row, 0.0))
        fk = kbs(f'Fuse_{k + 1}')
        a, bb = p - Vector((0, 0.014, 0)), p + Vector((0, 0.014, 0))
        fk.cyl(a, bb, 0.0042, Z_CERAMIC, 6)
        fk.cyl(a, a + Vector((0, 0.005, 0)), 0.0048, Z_STEEL2, 6)
        fk.cyl(bb - Vector((0, 0.005, 0)), bb, 0.0048, Z_STEEL2, 6)
        part(f'Fuse_{k + 1}', 'item', p, (-1, 0, 0), parent='FuseBox', states=['ok', 'blown'],
             note=['HEAD', 'FOG', 'SPOT', 'ROOF', 'WORK', 'CABIN', 'STOVE', 'WIPER'][k] + ' circuit (same order as the switch plate)')
    socket('Hand_FuseBox', c + n * 0.04, 'hand', parent='FuseBox_Lid')


def dash_controls(kbs, on_face, dash_n):
    """ignition lock and key, choke and hand throttle pull knobs, umbrella handbrake under the dash"""
    n = Vector(dash_n)
    D = kbs('DashControls')
    lc = on_face(0.40, 1.112, 0.0)
    D.lathe(lc, n, [(0.0, 0.022), (0.006, 0.022), (0.010, 0.017), (0.012, 0.012)], Z_STEEL2, 12)
    K = kbs('Ignition_Key')
    K.box_m(lc + n * 0.016, (0.008, 0.004, 0.02), Z_STEEL2, frame_R(Vector((0, 0, 1)), Vector((0, 1, 0))))
    K.box_m(lc + n * 0.032, (0.026, 0.006, 0.026), Z_KNOB, frame_R(Vector((0, 1, 0)), Vector((0, 0, 1))))
    part('Ignition_Key', 'twist', lc, -n, (0, 90), parent='DashControls', states=['0 off', 'I acc', 'II on', 'III start'],
         note='positions 0 / 30 / 60 / 90 deg clockwise (the axis points into the panel); III springs back to II; '
              'the key comes out at 0')
    socket('Hand_Ignition', lc + n * 0.035, 'hand', parent='Ignition_Key')
    for name, y in (('Knob_Choke', 0.27), ('Knob_Throttle', 0.325)):
        p = on_face(y, 1.105, 0.0)
        D.lathe(p, n, [(0.0, 0.012), (0.004, 0.012), (0.006, 0.008)], Z_STEEL2, 8)
        Kb = kbs(name)
        Kb.cyl(p, p + n * 0.025, 0.004, Z_STEEL2, 6)
        Kb.lathe(p + n * 0.025, n, [(0.0, 0.006), (0.004, 0.014), (0.016, 0.014), (0.02, 0.0)], Z_KNOB, 10)
        part(name, 'slide', p, n, (0, 0.05), unit='m', parent='DashControls',
             note='choke: pull for a cold start' if 'Choke' in name else 'hand throttle: pull to hold the revs')
    hb = on_face(0.74, 1.085, 0.0)
    D.lathe(hb, n, [(0.0, 0.016), (0.01, 0.016), (0.014, 0.011)], Z_STEEL2, 8)
    H = kbs('Handbrake')
    H.cyl(hb, hb + n * 0.05, 0.006, Z_STEEL2, 6)
    H.box_m(hb + n * 0.055, (0.012, 0.11, 0.02), Z_KNOB, frame_R(n, Vector((0, 1, 0))))
    part('Handbrake', 'slide', hb, n, (0, 0.12), unit='m', parent='DashControls', note='pull the T-handle, twist to release')
    socket('Hand_Handbrake', hb + n * 0.055, 'hand', parent='Handbrake')


# ================================================================ fuel
TANK = dict(x0=-0.15, x1=0.62, y0=0.50, y1=0.84, z0=0.28, z1=0.50)
# fillers (x, z): the left one sits on the stock filler recess of the UAZ (behind the cab door, it hides it); the right
# one between the saloon door and the cab door
FILLERS = {'L': (0.143, 0.842), 'R': (0.55, 0.80)}
FILLER_W = 0.18


def fuel_system(kbs, skin_y=0.961):
    """two tanks under the floor, a filler on each side (housing with a hinged flap, neck and screw cap), the pipes"""
    t = TANK
    T = kbs('FuelTanks')
    for s in (1, -1):
        cy = s * (t['y0'] + t['y1']) / 2
        cx, cz = (t['x0'] + t['x1']) / 2, (t['z0'] + t['z1']) / 2
        rbox(T, (cx, cy, cz), (t['x1'] - t['x0'], t['y1'] - t['y0'], t['z1'] - t['z0']), Z_TANK, r=0.05, ch=0.02, axis='x')
        for x in (t['x0'] + 0.17, t['x1'] - 0.17):                     # straps to the floor
            T.box(x - 0.016, x + 0.016, cy - 0.18, cy + 0.18, t['z0'] - 0.006, t['z0'], Z_STEEL2)
            for yy in (cy - 0.178, cy + 0.178):
                T.box(x - 0.016, x + 0.016, yy - 0.003, yy + 0.003, t['z0'], 0.52, Z_STEEL2)
        T.cyl((0.40, cy, t['z1']), (0.40, cy, t['z1'] + 0.015), 0.04, Z_STEEL2, 10)          # sender unit
        T.cyl((0.10, cy, t['z0']), (0.10, cy, t['z0'] - 0.015), 0.012, Z_STEEL2, 6)          # drain plug
        side = 'L' if s > 0 else 'R'
        fx, fz = FILLERS[side]
        n = Vector((0, s, 0))
        u, v = up_axes(n)
        fc = Vector((fx, s * skin_y, fz))
        H = kbs('FuelFiller_' + side)
        W = FILLER_W
        ring = lambda dy, w, h, r: rrect(fc + n * dy, u, v, w, h, r)
        H.loft([ring(0.0, W, W, 0.03), ring(0.032, W, W, 0.03), ring(0.035, W - 0.008, W - 0.008, 0.026),
                ring(0.035, 0.13, 0.13, 0.02), ring(0.012, 0.13, 0.13, 0.02)], [Z_BLACK] * 4)
        H.poly(ring(0.012, 0.13, 0.13, 0.02), Z_BLACK)
        H.cyl(fc + n * 0.012, fc + n * 0.026, 0.028, Z_STEEL2, 12)                            # neck end
        # filler hose: into the wall cavity, down under the floor, into the tank top
        H.tube([fc + n * 0.012, Vector((fx, s * 0.93, fz - 0.04)), Vector((fx, s * 0.93, 0.60)),       # in the wall cavity
                Vector((fx, s * 0.84, 0.53)), Vector((fx, cy, t['z1'] + 0.02))], 0.018, Z_HOSE, 8)
        cap_c = fc + n * 0.026
        Cp = kbs('FuelCap_' + side)
        Cp.lathe(cap_c, n, [(0.0, 0.034), (0.010, 0.034), (0.014, 0.028), (0.016, 0.0)], Z_STEEL2, 12)
        Cp.box_m(cap_c + n * 0.018, (0.05, 0.010, 0.010), Z_STEEL2, frame_R(Vector((0, 0, 1)), n))
        part('FuelCap_' + side, 'twist', cap_c, n, (0, 360), parent='FuelFiller_' + side,
             note='one turn anticlockwise, then it comes off (on a chain in the game if you like)')
        Fl = kbs('FuelFlap_' + side)
        fl = fc + n * 0.0365
        Fl.loft([rrect(fl, u, v, W - 0.02, W - 0.02, 0.024), rrect(fl + n * 0.004, u, v, W - 0.022, W - 0.022, 0.023)],
                [Z_PAINT], cap0=Z_PAINT, cap1=Z_PAINT)
        Fl.box_m(fl + n * 0.0045 + v * -(W / 2 - 0.03), (0.03, 0.012, 0.002), Z_BLACK, Matrix((u, v, n)).transposed())
        hinge = Vector((fx + (W - 0.02) / 2, fl.y + n.y * 0.002, fz))
        part('FuelFlap_' + side, 'hinge', hinge, (0, 0, -s), (0, 110), parent='FuelFiller_' + side,
             note='hinged at its front edge, opens outwards')
        socket('Hand_FuelFlap_' + side, fl + n * 0.006 - v * (W / 2 - 0.03), 'hand', parent='FuelFlap_' + side)
        socket('Pour_Fuel_' + side, cap_c + n * 0.02, 'pour', note='the spout goes in here, 56 l tank', parent='FuelFiller_' + side)
        part('FuelFiller_' + side, 'static', fc)
    part('FuelTanks', 'static', ((t['x0'] + t['x1']) / 2, 0, (t['z0'] + t['z1']) / 2), states=['left 0..56 l', 'right 0..56 l'])


def tank_valve(kbs):
    """tank selector under the driver's seat front edge: left / off / right"""
    c = Vector((1.481, 0.62, 0.83))
    n = Vector((1, 0, 0))
    V_ = kbs('TankValve')
    V_.box_m(c, (0.006, 0.09, 0.07), Z_STEEL2)
    V_.cyl(c, c + n * 0.03, 0.018, Z_BRASS, 10)
    V_.tube([c + Vector((0.01, 0, -0.03)), c + Vector((0.01, 0, -0.09))], 0.006, Z_STEEL2, 6)
    L = kbs('TankValve_Lever')
    L.cyl(c + n * 0.03, c + n * 0.036, 0.008, Z_BRASS, 6)
    L.box_m(c + n * 0.034 + Vector((0, 0, 0.03)), (0.008, 0.016, 0.06), Z_REDPLAST)
    part('TankValve_Lever', 'twist', c + n * 0.034, n, (-45, 45), parent='TankValve', states=['-45 left', '0 off', '45 right'],
         note='which tank feeds the engine; the fuel gauge reads the selected tank')
    socket('Hand_TankValve', c + n * 0.04 + Vector((0, 0, 0.05)), 'hand', parent='TankValve_Lever')


def jerrycan(kb, M4):
    """20 l jerrycan modelled upright (origin at the bottom centre, height +z), placed by the 4x4 matrix M4"""
    loc = KB()
    rbox(loc, (0, 0, 0.235), (0.165, 0.345, 0.47), Z_JERRY, r=0.02, ch=0.01)
    for sx in (1, -1):                                                   # the X pressed into both faces
        for a in (1, -1):
            p0, p1 = Vector((sx * 0.084, -0.14, 0.05 if a > 0 else 0.42)), Vector((sx * 0.084, 0.14, 0.42 if a > 0 else 0.05))
            loc.strip([p0, p1], (1, 0, 0), 0.018, 0.006, Z_JERRY, caps=True)
    for y in (-0.085, 0.0, 0.085):                                       # three handles
        loc.tube([(-0.05, y, 0.47), (-0.04, y, 0.51), (0.04, y, 0.51), (0.05, y, 0.47)], 0.008, Z_JERRY, 6)
    loc.cyl((0, -0.135, 0.47), (0, -0.135, 0.495), 0.026, Z_STEEL2, 10)                     # spout with the cap
    loc.box_m(Vector((0, -0.105, 0.50)), (0.02, 0.06, 0.01), Z_STEEL2)
    bm = loc.bm
    bmesh.ops.transform(bm, matrix=M4, verts=list(bm.verts))
    # merge into kb (keeps zones)
    zl = bm.faces.layers.float.get('zone')
    vmap = {}
    for v in bm.verts:
        vmap[v] = kb.bm.verts.new(v.co)
    for f in bm.faces:
        kb._f([vmap[v] for v in f.verts], f[zl])
    bm.free()


def jerrycans_and_spout(kbs, zb=2.20):
    for i, y in enumerate((-0.57, -0.20)):
        c = Vector((-1.385, y, zb + 0.02 + 0.0825))
        # lying on the rack: upright z -> vehicle -x (handles and spout at the rear, towards the ladder), depth x -> z
        R = Matrix(((0, 0, 1), (0, 1, 0), (-1, 0, 0))).transposed()
        M4 = Matrix.Translation(c - R @ Vector((0, 0, 0.235))) @ R.to_4x4()
        kb = kbs(f'Jerrycan_{i + 1}')
        jerrycan(kb, M4)
        part(f'Jerrycan_{i + 1}', 'item', c, (1, 0, 0), states=['fuel 0..20 l'],
             note='strapped on the rack; carry by the middle handle, pour with the spout fitted')
        socket(f'Hand_Jerrycan_{i + 1}', M4 @ Vector((0, 0, 0.51)), 'hand', parent=f'Jerrycan_{i + 1}')
        socket(f'Spout_Jerrycan_{i + 1}', M4 @ Vector((0, -0.135, 0.495)), 'item_slot', note='the spout screws on here', parent=f'Jerrycan_{i + 1}')
    S = kbs('Spout')
    p0 = Vector((-1.50, 0.03, zb + 0.04))                  # clear of the spade lying across the rear-left quarter
    pts = [p0, p0 + Vector((0.10, 0, 0)), p0 + Vector((0.18, 0.04, 0)), p0 + Vector((0.32, 0.12, 0))]
    S.cyl(p0 - Vector((0.03, 0, 0)), p0, 0.032, Z_STEEL2, 10)                         # collar
    S.tube(pts, 0.016, Z_JERRY, 8)
    for k in range(4):                                                   # ribbed flexible part
        q = pts[1].lerp(pts[2], k / 3)
        S.cyl(q - Vector((0.006, 0, 0)), q + Vector((0.006, 0, 0)), 0.019, Z_JERRY, 8)
    part('Spout', 'item', p0, (1, 0, 0), note='screws onto a jerrycan; its tip goes into a filler (Pour_Fuel_L / _R)')
    socket('Hand_Spout', pts[1], 'hand', parent='Spout')


# ================================================================ cab controls
PEDAL_Y = {'Pedal_Clutch': 0.665, 'Pedal_Brake': 0.435}      # the steering column comes down at y 0.523 between them
GAS_Y = 0.335                                               # tunnel side wall at y 0.30 (floor) .. 0.25 (top)


def pedals_and_levers(kbs):
    """clutch and brake hang from brackets under the dash (pivots at the front wall, as the stock pedals), an organ
    accelerator hinged at the heel; gear lever and two transfer-case levers on the tunnel in front of the engine cover
    (clear of its opening arc)"""
    Bk = kbs('PedalBox')
    for name, y in PEDAL_Y.items():
        K = kbs(name)
        piv = Vector((1.928, y, 1.040))
        pad = Vector((1.782, y, 0.840))
        K.tube([piv, Vector((1.915, y, 0.97)), Vector((1.835, y, 0.885)), Vector((1.791, y, 0.846))], 0.008, Z_STEEL2, 6)
        R = frame_R(Vector((0, 1, 0)), Vector((0.45, 0, 0.89)))   # pad: across y, its face looks back and up
        K.box_m(pad, (0.065, 0.06, 0.014), Z_PADRUB, R)
        K.cyl(piv - Vector((0, 0.022, 0)), piv + Vector((0, 0.022, 0)), 0.012, Z_STEEL2, 6)
        for dy in (-0.028, 0.028):                                     # bracket cheeks up to the dash underside
            Bk.box(piv.x - 0.03, piv.x + 0.03, y + dy - 0.004, y + dy + 0.004, piv.z - 0.022, 1.062, Z_STEEL2)
        part(name, 'hinge', piv, (0, -1, 0), (0, 18), parent='Cab',
             note='suspended pedal; pressed = 18 deg (the pad goes forward and down)')
        socket('Foot_' + name.split('_')[1], pad + Vector((-0.89, 0, 0.45)) * 0.012, 'foot', parent=name)
    G = kbs('Pedal_Gas')
    gp = Vector((1.700, GAS_Y, 0.752))                                 # heel hinge just above the floor (0.745)
    d = Vector((0.55, 0, 0.835)).normalized()                          # up the pad, leaning forward
    R = frame_R(Vector((0, 1, 0)), d)
    G.box_m(gp + d * 0.115 + R.col[2] * -0.008, (0.06, 0.23, 0.014), Z_PADRUB, R)
    G.cyl(gp - Vector((0, 0.034, 0)), gp + Vector((0, 0.034, 0)), 0.008, Z_STEEL2, 6)
    Bk.box(gp.x - 0.025, gp.x + 0.025, GAS_Y - 0.045, GAS_Y + 0.045, 0.745, 0.749, Z_STEEL2)   # hinge plate
    part('Pedal_Gas', 'hinge', gp, (0, 1, 0), (0, 14), parent='Cab',
         note='organ pedal hinged at the heel; pressed = 14 deg (the top goes forward and down)')
    socket('Foot_Gas', gp + d * 0.16 - R.col[2] * 0.02, 'foot', parent='Pedal_Gas')
    part('PedalBox', 'static', (1.85, 0.5, 0.95))
    # gear lever: rubber boot on the tunnel top (z 0.91) in front of the engine cover, rod leaning back, black knob
    gb = Vector((1.66, 0.16, 0.912))
    S = kbs('GearLever_Boot')
    S.lathe(gb, (0, 0, 1), [(0.0, 0.07), (0.015, 0.065), (0.03, 0.045), (0.05, 0.022), (0.065, 0.014)], Z_HOSE, 10, cap0=False)
    S.box_m(gb + Vector((0, 0, 0.002)), (0.16, 0.16, 0.004), Z_STEEL2)
    part('GearLever_Boot', 'static', gb)
    Gl = kbs('GearLever')
    top = Vector((1.56, 0.16, 1.20))
    Gl.tube([gb + Vector((0, 0, 0.02)), gb + Vector((0, 0, 0.10)), top], 0.008, Z_STEEL2, 6)
    Gl.lathe(top, (top - gb).normalized(), [(-0.01, 0.0), (-0.005, 0.016), (0.012, 0.022), (0.03, 0.018), (0.04, 0.0)],
             Z_KNOB, 10, cap0=False, cap1=False)
    part('GearLever', 'hinge', gb + Vector((0, 0, 0.03)), None, None, parent='Cab',
         states=['N (0, 0)', '1 (-8, -12)', '2 (-8, +12)', '3 (+8, -12)', '4 (+8, +12)', 'R (-14, -12, lift)'],
         note='two-axis: (side deg about +x, fore-aft deg about +y); 4 + reverse, H pattern on the knob')
    socket('Hand_GearLever', top + (top - gb).normalized() * 0.02, 'hand', parent='GearLever')
    for name, y, desc in (('TransferLever_Axle', -0.06, 'front axle: back = engaged'),
                          ('TransferLever_Range', -0.125, 'range: forward = high, back = low')):
        T = kbs(name)
        tp = Vector((1.64, y, 0.912))
        tt = Vector((1.57, y, 1.07))
        T.tube([tp, tt], 0.007, Z_STEEL2, 6)
        T.lathe(tt, (tt - tp).normalized(), [(-0.008, 0.0), (-0.004, 0.013), (0.012, 0.015), (0.024, 0.0)], Z_KNOB, 8,
                cap0=False, cap1=False)
        T.box(tp.x - 0.03, tp.x + 0.03, y - 0.02, y + 0.02, 0.909, 0.918, Z_STEEL2)
        part(name, 'hinge', tp, (0, 1, 0), (-15, 15), parent='Cab', note=desc)
        socket('Hand_' + name, tt, 'hand', parent=name)


# ================================================================ doors, hatches, steering wheel (stock meshes)
WHEEL_C = Vector((1.517, 0.523, 1.412))
WHEEL_N = Vector((-0.441, -0.004, 0.897)).normalized()


def steering_wheel(kz):
    """the stock wheel (rim, spokes, hub) becomes its own object turning about the column"""
    def pred(c):
        d = c - WHEEL_C
        along = d.dot(WHEEL_N)
        rad = (d - WHEEL_N * along).length
        return abs(along) < 0.07 and rad < 0.205
    sw = split_off(kz, pred, 'SteeringWheel')
    set_pivot(sw, WHEEL_C)
    part('SteeringWheel', 'hinge', WHEEL_C, WHEEL_N, (-540, 540), parent='Cab',
         note='1.5 turns each way; the hub is the horn button')
    socket('Hand_Wheel_L', WHEEL_C + Vector((0, 0.18, 0)), 'hand', parent='SteeringWheel')
    socket('Hand_Wheel_R', WHEEL_C + Vector((0, -0.18, 0)), 'hand', parent='SteeringWheel')
    return sw


def _skin_verts(o):
    """vertices of the painted skin of a door (zone Z_PAINT): the kit joined into a door (ladder, spare wheel, flares)
    must not move its hinge"""
    za = np.empty(len(o.data.polygons), np.float32)
    o.data.attributes['zone'].data.foreach_get('value', za)
    ids = {i for p, z in zip(o.data.polygons, za) if int(round(z)) == Z_PAINT for i in p.vertices}
    M = o.matrix_world
    return np.array([tuple(M @ o.data.vertices[i].co) for i in sorted(ids)])


def stock_door_pivots():
    """hinge axes of the stock doors (their origins sit at the world origin): cab doors at the front edge, rear doors
    at the outer edges"""
    out = {}
    for nm, side in (('doorL', 1), ('doorP', -1)):
        o = bpy.data.objects[nm]
        V = _skin_verts(o)
        skin = V[(np.abs(V[:, 1]) < 0.99) & (V[:, 2] > 0.9) & (V[:, 2] < 1.6)]
        xf = float(skin[:, 0].max())
        yo = float(np.abs(skin[skin[:, 0] > xf - 0.04][:, 1]).max())
        piv = Vector((xf - 0.02, side * (yo - 0.01), 1.2))
        set_pivot(o, piv)
        out[nm] = piv
        part(nm, 'hinge', piv, (0, 0, -side), (0, 70), parent='Body',
             note='cab door, front hinged; the window slides in the full plan (phase 2)')
    for nm, side in (('doorZL', 1), ('dooZP', -1)):
        o = bpy.data.objects[nm]
        V = _skin_verts(o)
        yo = float(np.abs(V[:, 1]).max())
        xo = float(V[np.abs(V[:, 1]) > yo - 0.03][:, 0].min())
        piv = Vector((xo + 0.02, side * (yo - 0.01), 1.28))
        set_pivot(o, piv)
        out[nm] = piv
        part(nm, 'hinge', piv, (0, 0, -side), (0, 100), parent='Body',
             note='rear door, hinged at the outer edge' + ('; carries the ladder' if side > 0 else '; carries the spare wheel'))
    return out


SIDE_DOOR = dict(x0=-0.598, x1=0.352, z0=0.675, z1=1.872)


def saloon_door(kz, kb_jamb):
    """cut the right saloon door out of the body (its skin, glass, seals, handle and inner trim) and give the opening
    jambs; the door hangs on its front edge"""
    d = SIDE_DOOR

    def pred(c):
        return d['x0'] < c.x < d['x1'] and c.y < -0.862 and d['z0'] < c.z < d['z1']
    door = split_off(kz, pred, 'doorSalon')
    piv = Vector((d['x1'] - 0.004, -0.955, 1.27))
    set_pivot(door, piv)
    part('doorSalon', 'hinge', piv, (0, 0, 1), (0, 80), parent='Body', note='saloon door, right side, front hinged')
    socket('Hand_doorSalon', (d['x0'] + 0.08, -0.99, 1.21), 'hand', parent='doorSalon')
    # jambs: a frame round the opening from the outer skin to the inner lining
    u, v = Vector((1, 0, 0)), Vector((0, 0, 1))
    c = Vector(((d['x0'] + d['x1']) / 2, 0, (d['z0'] + d['z1']) / 2))
    w, h = d['x1'] - d['x0'], d['z1'] - d['z0']
    outer = rrect(c + Vector((0, -0.962, 0)), u, v, w, h, 0.05)
    inner = rrect(c + Vector((0, -0.872, 0)), u, v, w, h, 0.05)
    kb_jamb.loft([outer, inner], [Z_PAINT])                              # faces look into the opening
    return door


HATCH = dict(x0=-0.58, x1=-0.29, y0=-0.60, y1=-0.20)


def roof_hatch(kz, kb_well, kbs, z_lin, z_roof0, z_roof1):
    """vent hatch in the saloon roof between two rack cross tubes: hole through the skin and the lining, a well, a curb
    with a rubber seal on top and a lid hinged at its front edge.  z_lin: the lining (lowest over the opening),
    z_roof0 / z_roof1: the roof skin lowest / highest over the opening (the roof is crowned)"""
    h = HATCH
    n_cut = cut_box(kz, (h['x0'], h['y0'], z_lin - 0.05), (h['x1'], h['y1'], z_roof1 + 0.05))
    u, v = Vector((1, 0, 0)), Vector((0, 1, 0))
    c = Vector(((h['x0'] + h['x1']) / 2, (h['y0'] + h['y1']) / 2, 0))
    w, hh = h['x1'] - h['x0'], h['y1'] - h['y0']
    zl, zs, zk = z_lin - 0.004, z_roof1 + 0.022, z_roof0 - 0.008     # well bottom, curb top, curb skirt bottom
    R = lambda z, dw, r: rrect(c + Vector((0, 0, z)), u, v, w + dw, hh + dw, r)
    kb_well.loft([list(reversed(R(zl, 0, 0.03))), list(reversed(R(zs, 0, 0.03)))], [Z_BLACK])        # well, faces in
    kb_well.loft([R(zk, 0.07, 0.05), R(zs - 0.007, 0.07, 0.05), R(zs, 0.052, 0.04), R(zs, 0.0, 0.03)],
                 [Z_PAINT, Z_RUBBER, Z_RUBBER])                                                        # curb + seal
    kb_well.loft([R(zl, 0.0, 0.03), R(zl, 0.05, 0.05)], [Z_BLACK])                                   # ceiling trim
    L = kbs('RoofHatch')
    rings = [R(zs + 0.003, 0.05, 0.05), R(zs + 0.021, 0.04, 0.045), R(zs + 0.029, -0.02, 0.03)]
    L.loft(rings, [Z_PAINT, Z_PAINT], cap0=Z_BLACK, cap1=Z_PAINT)
    L.box(c.x - 0.04, c.x + 0.04, c.y - 0.012, c.y + 0.012, zs - 0.035, zs + 0.003, Z_BLACK)         # inner handle
    piv = Vector((h['x1'] + 0.02, c.y, zs + 0.006))
    part('RoofHatch', 'hinge', piv, (0, 1, 0), (0, 35), parent='Body', note='vent hatch, hinged at its front edge; the rear lifts')
    socket('Hand_RoofHatch', (c.x, c.y, zs - 0.03), 'hand', parent='RoofHatch')
    return n_cut


# ================================================================ front: panel with the radiator behind the grille, filler hatch
def _snap_grid(base, extra, tol=0.012):
    vals = list(base)
    for e in extra:
        k = int(np.argmin([abs(v - e) for v in vals]))
        if abs(vals[k] - e) < tol:
            vals[k] = e
        else:
            vals.append(e)
    return sorted(vals)


def front_module3(kb, kbs, lens, S, L):
    """v2 front (black panel, round headlamps, turn signals, grille) with two openings: behind the grille a radiator
    you can see between the slats, above it a filler hatch with the radiator cap"""
    nose = S.get('nose_s', S['nose'])
    ys = _snap_grid(np.linspace(-0.82, 0.82, 35), [-0.41, 0.41, -0.11, 0.11])
    zs = _snap_grid(np.linspace(0.68, 1.22, 13), [0.765, 1.16, 1.168, 1.212])
    P_ = lambda y, z: Vector((nose(y, z) + 0.012, y, z))
    # openings (y0, y1, z0, z1, back x): the grille box ends on a plane just in front of the firewall (x 2.006 at
    # z 0.78..0.90), the filler slot is 6 cm deep
    holes = [(-0.41, 0.41, 0.765, 1.16, 2.012), (-0.11, 0.11, 1.168, 1.212, P_(0.0, 1.19).x - 0.06)]
    inside = lambda y, z: any(a < y < b and c < z < d for a, b, c, d, _ in holes)
    g = [[kb.bm.verts.new(P_(y, z)) for y in ys] for z in zs]
    for i in range(len(zs) - 1):
        for j in range(len(ys) - 1):
            if inside((ys[j] + ys[j + 1]) / 2, (zs[i] + zs[i + 1]) / 2):
                continue
            kb._f([g[i][j], g[i][j + 1], g[i + 1][j + 1], g[i + 1][j]], VK.Z_PANEL)
    edge = [g[0][j] for j in range(len(ys))] + [g[i][-1] for i in range(1, len(zs))] + \
           [g[-1][j] for j in reversed(range(len(ys) - 1))] + [g[i][0] for i in reversed(range(1, len(zs) - 1))]
    back = [kb.bm.verts.new(v.co - Vector((0.03, 0, 0))) for v in edge]
    for k in range(len(edge)):
        a, b = k, (k + 1) % len(edge)
        kb._f([edge[b], edge[a], back[a], back[b]], VK.Z_PANEL)
    # walls of the two openings, facing into them
    for (y0, y1, z0, z1, xb) in holes:
        ring = [(y, z0) for y in ys if y0 <= y <= y1] + [(y1, z) for z in zs if z0 < z < z1] + \
               [(y, z1) for y in reversed(ys) if y0 <= y <= y1] + [(y0, z) for z in reversed(zs) if z0 < z < z1]
        cen = Vector(((nose(0, (z0 + z1) / 2) + 0.012 + xb) / 2, (y0 + y1) / 2, (z0 + z1) / 2))
        for k in range(len(ring)):
            (ya, za), (yb, zb) = ring[k], ring[(k + 1) % len(ring)]
            a, b = P_(ya, za), P_(yb, zb)
            q = [a, b, Vector((xb, b.y, b.z)), Vector((xb, a.y, a.z))]
            nrm = (q[1] - q[0]).cross(q[3] - q[0])
            mid = (q[0] + q[1] + q[2] + q[3]) / 4
            if nrm.dot(cen - mid) < 0:
                q = list(reversed(q))
            kb.poly(q, Z_BLACK)
        kb.poly([Vector((xb, y0, z0)), Vector((xb, y1, z0)), Vector((xb, y1, z1)), Vector((xb, y0, z1))], Z_BLACK)   # back
    # radiator: core (fins between the slats), top tank, filler neck up to the hatch, the cap
    xr = nose(0.0, 0.96) + 0.012 - 0.10
    kb.box(xr - 0.05, xr, -0.40, 0.40, 0.775, 1.075, Z_RADCORE)
    kb.box(xr - 0.06, xr + 0.005, -0.34, 0.34, 1.075, 1.125, Z_IRON)
    kb.box(xr - 0.06, xr + 0.005, -0.42, -0.40, 0.76, 1.09, Z_IRON)
    kb.box(xr - 0.06, xr + 0.005, 0.40, 0.42, 0.76, 1.09, Z_IRON)
    xh = nose(0.0, 1.19) + 0.012
    nk0, nk1 = Vector((xr - 0.02, 0.0, 1.125)), Vector((xh - 0.040, 0.0, 1.190))      # the slot is 4.4 cm tall
    kb.tube([nk0, nk0 + Vector((0, 0, 0.03)), nk1], 0.015, Z_BRASS, 10)
    ax = (nk1 - (nk0 + Vector((0, 0, 0.03)))).normalized()
    Cp = kbs('Radiator_Cap')
    Cp.lathe(nk1, ax, [(0.0, 0.020), (0.007, 0.020), (0.010, 0.016), (0.012, 0.0)], Z_STEEL2, 12)
    Cp.box_m(nk1 + ax * 0.013, (0.034, 0.006, 0.007), Z_STEEL2, frame_R(Vector((0, 1, 0)), ax.cross(Vector((0, 1, 0)))))
    part('Radiator_Cap', 'twist', nk1, ax, (0, 180), parent='Kit_Front', note='half a turn, then off: coolant in here (hot = steam)')
    socket('Pour_Radiator', nk1 + ax * 0.03, 'pour', parent='Kit_Front')
    # filler hatch over the upper opening, hinged at its top edge
    Hh = kbs('RadiatorHatch')
    hc = P_(0.0, 1.19)
    hn = VK.surf_normal(nose, 0.0, 1.19)
    u, v = up_axes(hn)
    Hh.loft([rrect(hc + hn * 0.002, u, v, 0.240, 0.060, 0.012), rrect(hc + hn * 0.007, u, v, 0.236, 0.056, 0.010)],
            [VK.Z_PANEL], cap0=VK.Z_PANEL, cap1=VK.Z_PANEL)
    Hh.box_m(hc + hn * 0.008 - v * 0.022, (0.04, 0.008, 0.004), Z_CHROME, Matrix((u, v, hn)).transposed())
    hpiv = hc + v * 0.030 + hn * 0.004
    part('RadiatorHatch', 'hinge', hpiv, (0, -1, 0), (0, 100), parent='Kit_Front', note='filler hatch above the grille, lifts up')
    socket('Hand_RadiatorHatch', hc + hn * 0.01 - v * 0.022, 'hand', parent='RadiatorHatch')
    # headlamps, turn signals, grille (as v2)
    sockets = []
    up = Vector((0, 0, 1))
    for s in (1, -1):
        side = 'L' if s > 0 else 'R'
        y, z = s * 0.585, 1.005
        c = P_(y, z)
        n = VK.surf_normal(nose, y, z)
        f = VK.round_headlamp(kb, lens('Head_' + side, 'head'), c, n, 0.090, L['head_round'])
        sockets.append(('Head_' + side, f, n, 'head', up))
        c2 = P_(y, 0.842)
        n2 = VK.surf_normal(nose, y, 0.842)
        f2 = VK.rect_lamp(kb, lens('Turn_F' + side, 'amber'), c2, n2, 0.17, 0.050, L['amber'], r=0.012, proud=0.016,
                          border=0.009, chamfer=0.004, seat=0.006, crown=0.008)
        sockets.append(('Turn_F' + side, f2, n2, 'amber', up))
    for i in range(6):
        z = 0.795 + i * 0.062
        kb.strip([Vector((nose(y, z) + 0.028, y, z)) for y in np.linspace(-0.38, 0.38, 9)], (0, 0, 1), 0.032, 0.026, Z_BLACK)
    for z in (0.765, 1.16):
        kb.strip([Vector((nose(y, z) + 0.024, y, z)) for y in np.linspace(-0.41, 0.41, 9)], (0, 0, 1), 0.024, 0.018, Z_CHROME)
    for y in (-0.41, 0.41):
        kb.strip([Vector((nose(y, z) + 0.024, y, z)) for z in np.linspace(0.765, 1.16, 6)], (1, 0, 0), 0.018, 0.024, Z_CHROME)
    return sockets


# ================================================================ wheels: own PSX wheel (about 1.3k triangles instead of 8k)
WHEEL_NUT_R = 0.075                         # bolt circle (before the 1.12 wheel scale)


def wheel_mesh(kb, Z_TYRE=10, Z_RIM=9, seg=20):
    """wheel at the origin, axis +y (outer face +y): tyre with sidewall bulge, two rows of tread blocks, a dished rim
    with a hub and six nuts (the painter draws the rim holes)"""
    ax = Vector((0, 1, 0))
    c = Vector((0, 0, 0))
    kb.lathe(c, ax, [(-0.148, 0.205), (-0.153, 0.245), (-0.150, 0.275), (-0.135, 0.298), (-0.115, 0.307), (0.115, 0.307),
                     (0.135, 0.298), (0.150, 0.275), (0.153, 0.245), (0.148, 0.205)], Z_TYRE, seg, cap0=False, cap1=False)
    for row, a0 in ((0, -0.058), (1, 0.058)):
        for k in range(seg):
            ang = 2 * math.pi * (k + 0.5 * row) / seg
            rad = Vector((math.cos(ang), 0, math.sin(ang)))
            tng = Vector((-math.sin(ang), 0, math.cos(ang)))
            R = Matrix((tng, ax, rad)).transposed()
            kb.box_m(c + ax * a0 + rad * 0.315, (0.068, 0.086, 0.017), Z_TYRE, R)
    kb.lathe(c, ax, [(0.148, 0.205), (0.130, 0.200), (0.072, 0.176), (0.062, 0.160), (0.062, 0.046), (0.080, 0.040),
                     (0.088, 0.030), (0.090, 0.0)], Z_RIM, seg, cap0=False, cap1=False)
    kb.lathe(c, ax, [(-0.148, 0.205), (-0.10, 0.19), (-0.06, 0.16), (-0.06, 0.0)][::-1], Z_TYRE, seg, cap0=False, cap1=False)
    for k in range(6):                      # six hex nuts on the flat of the dish, outside the hub cap (they come off)
        ang = 2 * math.pi * k / 6
        p = c + ax * 0.062 + Vector((math.cos(ang), 0, math.sin(ang))) * WHEEL_NUT_R
        kb.lathe(p, ax, [(0.0, 0.011), (0.018, 0.011), (0.018, 0.006), (0.021, 0.006), (0.0215, 0.0)], Z_RIM, 6,
                 cap0=False, cap1=False)


def replace_wheels(wheels):
    """swap the stock wheel meshes (8k triangles each) for the PSX wheel; right wheels are the same mesh turned 180 deg"""
    kb = KB()
    wheel_mesh(kb)
    src = kb.to_object('tmp_wheel')
    for w in wheels:
        V = np.array([tuple(w.matrix_world @ v.co) for v in w.data.vertices])
        cen = Vector(((V[:, 0].min() + V[:, 0].max()) / 2, (V[:, 1].min() + V[:, 1].max()) / 2, (V[:, 2].min() + V[:, 2].max()) / 2))
        me = src.data.copy()
        if cen.y < 0:                       # right side: turned round, not mirrored (same loop order -> shared UVs)
            me.transform(Matrix.Rotation(math.pi, 4, 'Z'))
        me.transform(Matrix.Translation(cen))
        old = w.data
        w.data = me
        w.matrix_world = Matrix.Identity(4)
        me.name = w.name
        bpy.data.meshes.remove(old)
    bpy.data.objects.remove(src, do_unlink=True)

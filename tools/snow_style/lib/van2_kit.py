"""Geometry kit for the van v2 (built on the reassembled tiunov.se body, before the final lift):
exterior (front module, windscreen pillar, winch bumper + bull bar + spot lamps, roof rack + light bar + load,
roof marker lamps, flares, rub rails, steps, mud flaps, side markers, tail clusters, rear work lamp, plate lamp),
interior (dashboard in the plane's style with gauge faces and needles, folding troop seats like the plane's,
dome lamps, night lamp).  Faces carry a float 'zone' (atlas painter) or explicit 'PSX' UVs (lens / gauge atlases).
Frame: x forward, y left, z up, metres."""
import math
import bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix

TAU = 2 * math.pi
# zones shared with van2_build (atlas assignment + painters)
Z_PAINT, Z_BLACK, Z_RUBBER = 1, 2, 3
Z_TYRE, Z_RIM = 10, 9
Z_LINING = 11
Z_IRON, Z_GLOW, Z_WOOD, Z_LOG, Z_CLOTH, Z_LAMP, Z_BULB, Z_CRATE, Z_CAN, Z_BAG, Z_KIT, Z_FELT, Z_STEEL = range(20, 33)
Z_CHROME, Z_PANEL, Z_REDPAINT, Z_DASHP, Z_DASHPAD = 33, 34, 36, 37, 38
Z_CANVAS, Z_TUBE, Z_BRACKET, Z_WEB, Z_BUCKLE = 39, 40, 41, 42, 43
Z_DOMEBASE, Z_SPARE, Z_FEET = 44, 45, 46
Z_ORANGE, Z_SPARERIM = 47, 48
ZERO = 0.0                                   # explicit-UV faces


def up_axes(n, up=Vector((0, 0, 1))):
    """right / up unit vectors of a face with outward normal n, as seen from in front of it"""
    n = Vector(n).normalized()
    if abs(n.dot(up)) > 0.95:
        up = Vector((1, 0, 0)) if abs(n.x) < 0.9 else Vector((0, 1, 0))
    u = up.cross(n).normalized()
    v = n.cross(u).normalized()
    return u, v


class KB:
    """bmesh accumulator: every face gets a float 'zone'; optional explicit UVs in the 'PSX' layer"""

    def __init__(self):
        self.bm = bmesh.new()
        self.zl = self.bm.faces.layers.float.new('zone')
        self.uvl = self.bm.loops.layers.uv.new('PSX')

    def _f(self, vs, zone, uvs=None):
        f = self.bm.faces.new(vs)
        f[self.zl] = zone
        if uvs:
            for l, t in zip(f.loops, uvs):
                l[self.uvl].uv = t
        return f

    def poly(self, pts, zone, uvs=None):
        return self._f([self.bm.verts.new(Vector(p)) for p in pts], zone, uvs)

    def box_m(self, c, size, zone, R=None):
        """box centred at c with size (sx, sy, sz) in the frame R (3x3, columns = local axes)"""
        R = R or Matrix.Identity(3)
        sx, sy, sz = [s / 2 for s in size]
        corners = [Vector((x, y, z)) for x in (-sx, sx) for y in (-sy, sy) for z in (-sz, sz)]
        vs = [self.bm.verts.new(Vector(c) + R @ p) for p in corners]
        idx = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        return [self._f([vs[i] for i in q], zone) for q in idx]

    def box(self, x0, x1, y0, y1, z0, z1, zone):
        return self.box_m(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (x1 - x0, y1 - y0, z1 - z0), zone)

    def ring_pts(self, c, t, r, seg, ref=None):
        t = Vector(t).normalized()
        ref = ref or (Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0)))
        e1 = (ref - t * ref.dot(t)).normalized()
        e2 = t.cross(e1)
        return [Vector(c) + (e1 * math.cos(TAU * i / seg) + e2 * math.sin(TAU * i / seg)) * r for i in range(seg)], e1

    def tube(self, pts, r, zone, seg=8, caps=True):
        """circular tube along a polyline (parallel-transport frames)"""
        pts = [Vector(p) for p in pts]
        rings = []
        ref = None
        for i, p in enumerate(pts):
            if i == 0:
                t = pts[1] - pts[0]
            elif i == len(pts) - 1:
                t = pts[-1] - pts[-2]
            else:
                t = (pts[i + 1] - pts[i]).normalized() + (pts[i] - pts[i - 1]).normalized()
            ring, e1 = self.ring_pts(p, t, r, seg, ref)
            ref = e1
            rings.append([self.bm.verts.new(q) for q in ring])
        for a, b in zip(rings[:-1], rings[1:]):
            for i in range(seg):
                self._f([a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]], zone)
        if caps:
            self._f(list(reversed(rings[0])), zone)
            self._f(rings[-1], zone)

    def cyl(self, a, b, r, zone, seg=10, r2=None, caps=True):
        a, b = Vector(a), Vector(b)
        ra, _ = self.ring_pts(a, b - a, r, seg)
        rb, _ = self.ring_pts(b, b - a, r if r2 is None else r2, seg)
        va = [self.bm.verts.new(q) for q in ra]
        vb = [self.bm.verts.new(q) for q in rb]
        for i in range(seg):
            self._f([va[i], va[(i + 1) % seg], vb[(i + 1) % seg], vb[i]], zone)
        if caps:
            self._f(list(reversed(va)), zone)
            self._f(vb, zone)

    def strip(self, pts, up, w, h, zone, caps=True):
        """rectangular section (w across, h along `up`) swept along a polyline"""
        pts = [Vector(p) for p in pts]
        up = Vector(up).normalized()
        secs = []
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            s = t.cross(up).normalized()
            u = s.cross(t).normalized()
            q = [p + s * (w / 2) * a + u * (h / 2) * b for a, b in ((-1, -1), (-1, 1), (1, 1), (1, -1))]
            secs.append([self.bm.verts.new(x) for x in q])
        for a, b in zip(secs[:-1], secs[1:]):
            for i in range(4):
                self._f([a[i], a[(i + 1) % 4], b[(i + 1) % 4], b[i]], zone)
        if caps:
            self._f(list(reversed(secs[0])), zone)
            self._f(secs[-1], zone)

    def lathe(self, c, axis, prof, zone, seg=12, cap0=True, cap1=True):
        """profile [(along, radius)] revolved about axis through c"""
        ax = Vector(axis).normalized()
        rings = []
        for a, r in prof:
            ring, _ = self.ring_pts(Vector(c) + ax * a, ax, max(r, 1e-5), seg)
            rings.append([self.bm.verts.new(q) for q in ring])
        for a, b in zip(rings[:-1], rings[1:]):
            for i in range(seg):
                self._f([a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]], zone)
        if cap0 and prof[0][1] > 1e-4:
            self._f(list(reversed(rings[0])), zone)
        if cap1 and prof[-1][1] > 1e-4:
            self._f(rings[-1], zone)

    def quad_uv(self, c, n, w, h, uvrect, zone=ZERO, up=Vector((0, 0, 1))):
        """flat rectangle facing n, UVs filling uvrect (u0, v0, u1, v1)"""
        u, v = up_axes(n, up)
        c = Vector(c)
        pts = [c - u * w / 2 - v * h / 2, c + u * w / 2 - v * h / 2, c + u * w / 2 + v * h / 2, c - u * w / 2 + v * h / 2]
        u0, v0, u1, v1 = uvrect
        return self.poly(pts, zone, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)])

    def disc_uv(self, c, n, r, uvrect, seg=14, zone=ZERO, dome=0.0):
        """disc (optionally domed) facing n, UV mapped radially into uvrect"""
        u, v = up_axes(n)
        n = Vector(n).normalized()
        c = Vector(c)
        u0, v0, u1, v1 = uvrect
        cu, cv, hu, hv = (u0 + u1) / 2, (v0 + v1) / 2, (u1 - u0) / 2, (v1 - v0) / 2
        rings = [(1.0, 0.0), (0.84, 0.48), (0.6, 0.80), (0.32, 0.95), (0.0, 1.0)] if dome > 0 else [(1.0, 0.0), (0.0, 0.0)]
        prev = None
        for k, (rf, hf) in enumerate(rings):
            if rf == 0.0:
                tip = self.bm.verts.new(c + n * dome * hf)
                for i in range(seg):
                    a, b = prev[i], prev[(i + 1) % seg]
                    self._f([a[0], b[0], tip], zone, [a[1], b[1], (cu, cv)])
                break
            ring = []
            for i in range(seg):
                ang = TAU * i / seg
                p = c + (u * math.cos(ang) + v * math.sin(ang)) * r * rf + n * dome * hf
                ring.append((self.bm.verts.new(p), (cu + hu * rf * math.cos(ang), cv + hv * rf * math.sin(ang))))
            if prev is not None:
                for i in range(seg):
                    a0, a1 = prev[i], prev[(i + 1) % seg]
                    b0, b1 = ring[i], ring[(i + 1) % seg]
                    self._f([a0[0], a1[0], b1[0], b0[0]], zone, [a0[1], a1[1], b1[1], b0[1]])
            prev = ring

    def loft(self, rings, zones, closed=True, cap0=None, cap1=None):
        """quads between consecutive rings of equal length (each ring counter-clockwise seen from the front);
        zones[i] = zone of the band ring i -> ring i+1; optional caps on the first / last ring"""
        vr = [[self.bm.verts.new(Vector(p)) for p in ring] for ring in rings]
        n = len(vr[0])
        for i in range(len(vr) - 1):
            a, b = vr[i], vr[i + 1]
            for j in range(n if closed else n - 1):
                j2 = (j + 1) % n
                self._f([a[j], a[j2], b[j2], b[j]], zones[i])
        if cap0 is not None:
            self._f(list(reversed(vr[0])), cap0)
        if cap1 is not None:
            self._f(vr[-1], cap1)
        return vr

    def superdome(self, c, n, up, a, b, h, uvrect, nu=8, nv=6, p=6.0, prof='pillow', zone=ZERO):
        """lens over a rounded-rectangle footprint (superellipse of power p, half sizes a x b) bulging by h along n;
        prof 'pillow' (parabolic crown, glass in a bezel) or 'dome' (capsule); planar UVs over uvrect"""
        n = Vector(n).normalized()
        u, v = up_axes(n, up)
        c = Vector(c)
        u0, v0, u1, v1 = uvrect
        grid = []
        for j in range(nv + 1):
            t = -1 + 2 * j / nv
            row = []
            for i in range(nu + 1):
                s = -1 + 2 * i / nu
                m = max(abs(s), abs(t))
                k = m / (abs(s) ** p + abs(t) ** p) ** (1 / p) if m > 1e-9 else 1.0
                hz = h * (1 - m * m) if prof == 'pillow' else h * math.sqrt(max(0.0, 1 - m ** 2.2))
                pos = c + u * (a * s * k) + v * (b * t * k) + n * hz
                row.append((self.bm.verts.new(pos), (u0 + (u1 - u0) * (s + 1) / 2, v0 + (v1 - v0) * (t + 1) / 2)))
            grid.append(row)
        for j in range(nv):
            for i in range(nu):
                q = [grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]]
                self._f([e[0] for e in q], zone, [e[1] for e in q])

    def slab(self, fn, nfn, ss, ts, thick, zone):
        """thin sheet: mid-surface fn(s, t), normal nfn(s, t), sampled on the grids ss x ts"""
        top, bot = [], []
        for s in ss:
            rt, rb = [], []
            for t in ts:
                p, n = fn(s, t), nfn(s, t)
                rt.append(self.bm.verts.new(p + n * thick / 2))
                rb.append(self.bm.verts.new(p - n * thick / 2))
            top.append(rt)
            bot.append(rb)
        I, J = len(ss), len(ts)
        for i in range(I - 1):
            for j in range(J - 1):
                self._f([top[i][j], top[i + 1][j], top[i + 1][j + 1], top[i][j + 1]], zone)
                self._f([bot[i][j], bot[i][j + 1], bot[i + 1][j + 1], bot[i + 1][j]], zone)
        for i in range(I - 1):
            for j in (0, J - 1):
                q = [top[i][j], bot[i][j], bot[i + 1][j], top[i + 1][j]]
                self._f(q if j == 0 else list(reversed(q)), zone)
        for j in range(J - 1):
            for i in (0, I - 1):
                q = [top[i][j], top[i][j + 1], bot[i][j + 1], bot[i][j]]
                self._f(q if i == 0 else list(reversed(q)), zone)

    def to_object(self, name, coll=None, pivot=None, recalc=False):
        me = bpy.data.meshes.new(name)
        if recalc:
            bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        self.bm.to_mesh(me)
        self.bm.free()
        ob = bpy.data.objects.new(name, me)
        (coll or bpy.context.scene.collection).objects.link(ob)
        if pivot is not None:
            me.transform(Matrix.Translation(-Vector(pivot)))
            ob.location = pivot
        ob['orig'] = name
        return ob


def fillet(pts, r, n=4):
    """round the inner corners of a polyline"""
    pts = [Vector(p) for p in pts]
    out = [pts[0]]
    for i in range(1, len(pts) - 1):
        a, b, c = pts[i - 1], pts[i], pts[i + 1]
        d1, d2 = (a - b).normalized(), (c - b).normalized()
        ang = d1.angle(d2)
        if ang < 1e-3 or abs(ang - math.pi) < 1e-3:
            out.append(b)
            continue
        t = r / math.tan(ang / 2)
        t = min(t, (a - b).length * 0.45, (c - b).length * 0.45)
        p0, p1 = b + d1 * t, b + d2 * t
        for k in range(n + 1):
            s = k / n
            q = (1 - s) ** 2 * p0 + 2 * (1 - s) * s * b + s * s * p1
            out.append(q)
    out.append(pts[-1])
    return out


# ====================================================================== exterior
def surf_normal(f, y, z, d=0.02):
    """outward normal of a surface x = f(y, z) facing +x"""
    fy = (f(y + d, z) - f(y - d, z)) / (2 * d)
    fz = (f(y, z + d) - f(y, z - d)) / (2 * d)
    return Vector((1.0, -fy, -fz)).normalized()


def lamp_box(kb, c, n, w, h, depth, zone_house, lens_kb, uvrect, bezel=0.0, zone_bezel=Z_CHROME, up=Vector((0, 0, 1))):
    """housing box on a surface point c (outward normal n), lens quad on its face; returns the lens centre"""
    n = Vector(n).normalized()
    u, v = up_axes(n, up)
    R = Matrix((u, v, n)).transposed()
    kb.box_m(Vector(c) + n * depth / 2, (w, h, depth), zone_house, R)
    face = Vector(c) + n * (depth + 0.002)
    if bezel > 0:
        for cc, sz in ((face + v * (h / 2 - bezel / 2), (w, bezel, 0.006)), (face - v * (h / 2 - bezel / 2), (w, bezel, 0.006)),
                       (face + u * (w / 2 - bezel / 2), (bezel, h - 2 * bezel, 0.006)),
                       (face - u * (w / 2 - bezel / 2), (bezel, h - 2 * bezel, 0.006))):
            kb.box_m(cc + n * 0.003, sz, zone_bezel, R)
    lens_kb.quad_uv(face + n * 0.001, n, w - 2 * bezel - 0.004, h - 2 * bezel - 0.004, uvrect, up=up)
    return face


# ---------------------------------------------------------------------- lamps in the plane's v13 language
# housings with bevelled bezels and gaskets, lenses with a crown or a dome (never a flat sticker), a mount for
# every lamp; one lens object per lamp so each lamp can be switched (and blink) on its own
class LensKit:
    def __init__(self):
        self.kbs, self.groups = {}, {}

    def __call__(self, sock, grp):
        if sock not in self.kbs:
            self.kbs[sock], self.groups[sock] = KB(), grp
        return self.kbs[sock]


def rrect(c, u, v, w, h, r, k=3):
    """rounded rectangle w x h (corner radius r, k segments per corner), counter-clockwise seen from u x v"""
    r = max(0.002, min(r, w / 2 - 1e-4, h / 2 - 1e-4))
    pts = []
    for (sx, sy, a0) in ((1, -1, -90), (1, 1, 0), (-1, 1, 90), (-1, -1, 180)):
        cx, cy = sx * (w / 2 - r), sy * (h / 2 - r)
        for i in range(k + 1):
            a = math.radians(a0 + 90 * i / k)
            pts.append(Vector(c) + u * (cx + r * math.cos(a)) + v * (cy + r * math.sin(a)))
    return pts


def rect_lamp(kb, kbl, c, n, w, h, uvrect, up=Vector((0, 0, 1)), r=0.014, proud=0.020, border=0.014, chamfer=0.005,
              seat=0.008, crown=0.009, zone_bezel=Z_BLACK, zone_inner=Z_BLACK, body=None, zone_body=Z_BLACK):
    """rectangular lamp on a surface point c (outward normal n): optional can behind (body = [(offset, sw, sh)] from
    the back), a bezel standing proud of the surface with a chamfered front edge, the inner wall down to the glass seat
    and a pillow glass with the reflector pattern.  Returns the glass centre."""
    n = Vector(n).normalized()
    u, v = up_axes(n, up)
    c = Vector(c)
    ring = lambda off, ww, hh, rr: rrect(c + n * off, u, v, ww, hh, rr)
    if body:
        rings = [ring(o, w * sw, h * sh, r * min(sw, sh)) for (o, sw, sh) in body] + [ring(0.0, w, h, r)]
        kb.loft(rings, [zone_body] * (len(rings) - 1), cap0=zone_body)
    wi, hi = w - 2 * border, h - 2 * border
    ri = max(0.003, r - border * 0.6)
    kb.loft([ring(0.0, w, h, r), ring(proud - chamfer * 0.4, w, h, r),
             ring(proud, w - 2 * chamfer, h - 2 * chamfer, max(0.003, r - chamfer)),
             ring(proud, wi, hi, ri), ring(seat, wi, hi, ri)], [zone_bezel, zone_bezel, zone_bezel, zone_inner])
    kbl.superdome(c + n * seat, n, up, wi / 2 + 0.002, hi / 2 + 0.002, crown, uvrect, prof='pillow')
    return c + n * (seat + crown)


def round_lamp(kb, kbl, c, axis, r, uvrect, depth=0.095, dome=0.012, seg=16, zone_body=Z_BLACK, zone_bezel=Z_CHROME):
    """round driving lamp: deep bowl, a chrome bezel ring with a lip, a domed Fresnel glass; c = bezel plane centre"""
    ax = Vector(axis).normalized()
    c = Vector(c)
    kb.lathe(c, ax, [(-depth, 0.16 * r), (-depth * 0.97, 0.45 * r), (-depth * 0.84, 0.73 * r), (-depth * 0.61, 0.91 * r),
                     (-depth * 0.30, 0.99 * r), (0.0, r)], zone_body, seg, cap0=True, cap1=False)
    kb.lathe(c, ax, [(0.0, r), (0.006, r * 1.04), (0.013, r * 0.98), (0.016, r * 0.88), (0.011, r * 0.84)],
             zone_bezel, seg, cap0=False, cap1=False)
    kbl.disc_uv(c + ax * 0.009, ax, r * 0.86, uvrect, seg, dome=dome)
    return c + ax * (0.009 + dome)


def round_headlamp(kb, kbl, c, n, rg, uvrect, seg=20):
    """round 7-inch headlamp set in the panel: chrome bezel with a flange (its skirt runs into the panel) and a
    chamfered lip, black inner wall, domed sealed-beam glass; c = point on the panel, n = panel normal"""
    ax = Vector(n).normalized()
    c = Vector(c)
    kb.lathe(c, ax, [(-0.03, rg * 1.26), (0.0, rg * 1.26), (0.008, rg * 1.24), (0.016, rg * 1.17), (0.021, rg * 1.08),
                     (0.019, rg * 1.02)], Z_CHROME, seg, cap0=False, cap1=False)
    kb.lathe(c, ax, [(0.019, rg * 1.02), (0.006, rg * 1.01)], Z_BLACK, seg, cap0=False, cap1=False)
    kbl.disc_uv(c + ax * 0.006, ax, rg * 1.02, uvrect, seg, dome=0.017)
    return c + ax * 0.023


def marker_lamp(kb, kbl, c, n, along, a, b, h, uvrect, base_t=0.008, zone=Z_BLACK):
    """clearance / cab marker: a rounded base plate and a capsule lens (half sizes a along `along`, b across)"""
    n = Vector(n).normalized()
    along = (Vector(along) - n * Vector(along).dot(n)).normalized()
    up = n.cross(along)
    u, v = up_axes(n, up)
    c = Vector(c)
    kb.loft([rrect(c, u, v, 2 * a + 0.014, 2 * b + 0.012, b + 0.006), rrect(c + n * base_t, u, v, 2 * a + 0.010,
             2 * b + 0.008, b + 0.004)], [zone], cap1=zone)
    kbl.superdome(c + n * base_t, n, up, a, b, h, uvrect, nu=8, nv=4, p=2.4, prof='dome')
    return c + n * (base_t + h * 0.6)


def u_bracket(kb, pivot, side, half, base_z, thick=0.008, width=0.024, bolt=0.011, zone=Z_BLACK):
    """U-bracket: two arms either side of a lamp (along `side`, at +-half) from base_z up to the pivot, a foot bar
    across, pivot bolt heads outside the arms"""
    p = Vector(pivot)
    s = Vector(side).normalized()
    fwd = s.cross(Vector((0, 0, 1))).normalized()
    R = Matrix((fwd, s, Vector((0, 0, 1)))).transposed()
    hgt = p.z - base_z
    for k in (1, -1):
        arm_c = p + s * (k * half) - Vector((0, 0, hgt / 2 - 0.006))
        kb.box_m(arm_c, (width, thick, hgt + 0.012), zone, R)
        kb.cyl(p + s * (k * (half + thick / 2)), p + s * (k * (half + thick / 2 + 0.008)), bolt, zone, 6)
    kb.box_m(Vector((p.x, p.y, base_z + 0.006)) , (width, 2 * half + thick, 0.012), zone, R)


def tail_cluster(kb, lk, c, n, w, h, L, side):
    """three-chamber rear lamp: housing standing proud of the body, black frame with mullions, three pillow glasses
    (tail / stop red, amber turn, clear reverse) with their own patterns; returns sockets"""
    n = Vector(n).normalized()
    up = Vector((0, 0, 1))
    u, v = up_axes(n, up)
    c = Vector(c)
    ring = lambda off, ww, hh, rr: rrect(c + n * off, u, v, ww, hh, rr)
    kb.loft([ring(0.0, w, h, 0.016), ring(0.036, w, h, 0.016), ring(0.042, w - 0.010, h - 0.010, 0.011),
             ring(0.042, w - 0.022, h - 0.022, 0.006), ring(0.028, w - 0.022, h - 0.022, 0.006)],
            [Z_BLACK, Z_BLACK, Z_BLACK, Z_BLACK])
    wi, hin = w - 0.022, h - 0.022
    hs = (hin - 2 * 0.008) / 3
    R = Matrix((u, v, n)).transposed()
    sockets = []
    for i, (key, grp, name) in enumerate((('tail_red', 'tail', 'Tail_'), ('tail_amber', 'amber', 'Turn_R'),
                                          ('reverse', 'reverse', 'Reverse_'))):
        zc = hin / 2 - hs / 2 - i * (hs + 0.008)
        cc = c + v * zc + n * 0.028
        sock = name + side
        lk(sock, grp).superdome(cc, n, up, wi / 2 + 0.001, hs / 2 + 0.001, 0.007, L[key], nu=6, nv=6, p=8)
        sockets.append((sock, cc + n * 0.008, n, grp, up))
        if i < 2:
            kb.box_m(cc - v * (hs / 2 + 0.004) + n * 0.008, (wi + 0.002, 0.008, 0.016), Z_BLACK, R)
    return sockets


def front_module(kb, lens, S, L):
    nose = S.get('nose_s', S['nose'])               # the smoothed nose (the stock lamp rims are filtered out)
    ys = np.linspace(-0.82, 0.82, 35)
    zs = np.linspace(0.68, 1.22, 13)
    g = [[kb.bm.verts.new(Vector((nose(y, z) + 0.012, y, z))) for y in ys] for z in zs]
    for i in range(len(zs) - 1):
        for j in range(len(ys) - 1):
            kb._f([g[i][j], g[i][j + 1], g[i + 1][j + 1], g[i + 1][j]], Z_PANEL)
    # rim back to the body so the panel reads as a part, not a decal
    edge = [g[0][j] for j in range(len(ys))] + [g[i][-1] for i in range(1, len(zs))] + \
           [g[-1][j] for j in reversed(range(len(ys) - 1))] + [g[i][0] for i in reversed(range(1, len(zs) - 1))]
    back = [kb.bm.verts.new(v.co - Vector((0.03, 0, 0))) for v in edge]
    for k in range(len(edge)):
        a, b = k, (k + 1) % len(edge)
        kb._f([edge[b], edge[a], back[a], back[b]], Z_PANEL)
    sockets = []
    up = Vector((0, 0, 1))
    for s in (1, -1):
        side = 'L' if s > 0 else 'R'
        y, z = s * 0.585, 1.005
        c = Vector((nose(y, z) + 0.012, y, z))
        n = surf_normal(nose, y, z)
        # round 7-inch headlamp in a chrome bezel set into the black panel
        f = round_headlamp(kb, lens('Head_' + side, 'head'), c, n, 0.090, L['head_round'])
        sockets.append(('Head_' + side, f, n, 'head', up))
        y2, z2 = s * 0.585, 0.842
        c2 = Vector((nose(y2, z2) + 0.012, y2, z2))
        n2 = surf_normal(nose, y2, z2)
        f2 = rect_lamp(kb, lens('Turn_F' + side, 'amber'), c2, n2, 0.17, 0.050, L['amber'], r=0.012, proud=0.016,
                       border=0.009, chamfer=0.004, seat=0.006, crown=0.008)
        sockets.append(('Turn_F' + side, f2, n2, 'amber', up))
    # grille: six slats and a surround
    for i in range(6):
        z = 0.795 + i * 0.062
        pts = [Vector((nose(y, z) + 0.012 + 0.016, y, z)) for y in np.linspace(-0.38, 0.38, 9)]
        kb.strip(pts, (0, 0, 1), 0.032, 0.026, Z_BLACK)
    for z in (0.765, 1.16):
        pts = [Vector((nose(y, z) + 0.012 + 0.012, y, z)) for y in np.linspace(-0.41, 0.41, 9)]
        kb.strip(pts, (0, 0, 1), 0.024, 0.018, Z_CHROME)
    for y in (-0.41, 0.41):
        pts = [Vector((nose(y, z) + 0.012 + 0.012, y, z)) for z in np.linspace(0.765, 1.16, 6)]
        kb.strip(pts, (1, 0, 0), 0.018, 0.024, Z_CHROME)
    return sockets


def windscreen_pillar(kb):
    p0, p1 = Vector((1.984, 0, 1.392)), Vector((1.592, 0, 1.838))
    d = (p1 - p0).normalized()
    n = Vector((-d.z, 0, d.x))                                      # outward (forward-up)
    if n.x < 0:
        n = -n
    kb.strip([p0 + n * 0.008, p1 + n * 0.008], n, 0.05, 0.02, Z_RUBBER)


def bumper(kb, lens, L):
    """winch bumper with a bull bar, two spot lamps and red tow hooks (replaces the stock front bumper)"""
    kb.box(2.19, 2.33, -0.965, 0.965, 0.50, 0.655, Z_BLACK)
    for s in (1, -1):                                               # chamfered ends
        kb.box_m((2.26, s * 0.99, 0.58), (0.12, 0.06, 0.13), Z_BLACK, Matrix.Rotation(math.radians(s * 25), 3, 'Z'))
    kb.cyl((2.375, -0.17, 0.575), (2.375, 0.17, 0.575), 0.058, Z_CHROME, 12)         # winch drum
    for s in (1, -1):
        kb.cyl((2.375, s * 0.17, 0.575), (2.375, s * 0.19, 0.575), 0.078, Z_BLACK, 12)
    kb.cyl((2.375, 0.19, 0.575), (2.375, 0.34, 0.575), 0.056, Z_BLACK, 12)            # motor
    kb.box(2.425, 2.45, -0.12, 0.12, 0.525, 0.625, Z_CHROME)                           # fairlead
    kb.box(2.45, 2.49, -0.025, 0.025, 0.55, 0.60, Z_REDPAINT)                          # hook
    bar = fillet([(2.33, -0.46, 0.655), (2.355, -0.46, 1.04), (2.36, -0.40, 1.085), (2.36, 0.40, 1.085),
                  (2.355, 0.46, 1.04), (2.33, 0.46, 0.655)], 0.06, 4)
    kb.tube(bar, 0.022, Z_BLACK, 10)
    kb.tube([(2.345, -0.46, 0.86), (2.345, 0.46, 0.86)], 0.019, Z_BLACK, 10)
    sockets = []
    for s in (1, -1):
        y = s * 0.24
        side = 'L' if s > 0 else 'R'
        zc, xf = 1.205, 2.42
        # round driving lamp in a deep bowl, held by a U-bracket bolted to the top tube of the bull bar
        f = round_lamp(kb, lens('Spot_' + side, 'spot'), (xf, y, zc), (1, 0, 0), 0.075, L['spot_round'], depth=0.092,
                       dome=0.013, seg=16)
        u_bracket(kb, (xf - 0.045, y, zc), (0, 1, 0), 0.085, 1.104)
        sockets.append(('Spot_' + side, f, Vector((1, 0, 0)), 'spot', Vector((0, 0, 1))))
        hook = [(2.30, s * 0.72, 0.50), (2.30, s * 0.72, 0.445), (2.35, s * 0.72, 0.425), (2.395, s * 0.72, 0.455)]
        kb.tube(hook, 0.013, Z_REDPAINT, 8)
    return sockets


def visor(kb, lens, S, L):
    """sun visor out of the roof brow over the windscreen, five amber cab markers on top of its front edge"""
    zr = 1.885
    ys = [float(v) for v in np.linspace(-0.80, 0.80, 17)]
    xb = [S['nose'](y, zr) for y in ys]
    brow = lambda y: float(np.interp(y, ys, xb))
    D, drop = 0.20, 0.028
    fn = lambda s, t: Vector((brow(s) - 0.03 + (D + 0.03) * t, s, zr + 0.004 - drop * t))
    nv = Vector((drop, 0, D + 0.03)).normalized()
    kb.slab(fn, lambda s, t: nv, list(reversed(ys)), [0.0, 0.5, 1.0], 0.014, Z_BLACK)
    kb.strip([fn(y, 1.0) + Vector((-0.004, 0, -0.016)) for y in ys], (0, 0, 1), 0.010, 0.034, Z_BLACK)
    for y in (-0.55, 0.0, 0.55):                                     # brackets under the visor
        x0 = brow(y)
        kb.box(x0 - 0.02, x0 + 0.12, y - 0.006, y + 0.006, zr - 0.05, zr - 0.006, Z_BLACK)
    sockets = []
    for i, y in enumerate((-0.62, -0.13, 0.0, 0.13, 0.62)):
        top = fn(y, 0.80) + nv * 0.007                                # on top of the visor, near its front lip
        f = marker_lamp(kb, lens(f'Marker_Cab{i}', 'marker'), top, nv, (0, 1, 0), 0.034, 0.014, 0.020, L['marker'])
        sockets.append((f'Marker_Cab{i}', f, Vector((1, 0, 0)), 'marker', Vector((0, 0, 1))))
    return sockets


def roof_markers(kb, lens, S, L):
    sockets = []
    for i, y in enumerate((-0.30, 0.0, 0.30)):
        zr = S['roof'](1.62, y)
        kb.box(1.585, 1.645, y - 0.045, y + 0.045, zr - 0.01, zr + 0.045, Z_BLACK)
        lens['marker'].quad_uv((1.648, y, zr + 0.019), (1, 0, 0), 0.08, 0.034, L['marker'])
        sockets.append((f'Marker_Roof{i}', Vector((1.65, y, zr + 0.019)), Vector((1, 0, 0)), 'marker'))
    return sockets


def roof_rack(kb, props_kb, lens, S, L):
    zb, zu = 2.20, 2.30
    x0, x1, yw = -1.70, 1.15, 0.80
    for s in (1, -1):
        kb.tube([(x0, s * yw, zb), (x1, s * yw, zb)], 0.016, Z_BLACK, 8)
        kb.tube([(x0 + 0.05, s * yw, zu), (x1 - 0.12, s * yw, zu)], 0.014, Z_BLACK, 8)
        for x in np.linspace(x0 + 0.05, x1 - 0.12, 6):
            kb.tube([(x, s * yw, zb), (x, s * yw, zu)], 0.012, Z_BLACK, 6)
        for x in (-1.55, -0.55, 0.45):                                  # legs to the gutter
            kb.tube([(x, s * yw, zb), (x, s * 0.885, 1.955)], 0.017, Z_BLACK, 8)
            kb.box(x - 0.05, x + 0.05, s * 0.86 - 0.04, s * 0.86 + 0.04, 1.925, 1.95, Z_BLACK)
    kb.tube([(x0, -yw, zb), (x0, yw, zb)], 0.016, Z_BLACK, 8)
    kb.tube([(x0, -yw, zu), (x0, yw, zu)], 0.014, Z_BLACK, 8)
    kb.tube([(x1, -yw, zb), (x1, yw, zb)], 0.016, Z_BLACK, 8)
    for x in np.linspace(x0 + 0.35, x1 - 0.3, 7):
        kb.tube([(x, -yw, zb), (x, yw, zb)], 0.012, Z_BLACK, 6)
    # light bar: four work lamps (cans with a bevelled bezel and a prism glass) on U-brackets along a rail
    sockets = []
    kb.box(x1 - 0.02, x1 + 0.03, -0.62, 0.62, zb + 0.015, zb + 0.035, Z_BLACK)
    zl = zb + 0.105
    for i, y in enumerate((-0.48, -0.16, 0.16, 0.48)):
        f = rect_lamp(kb, lens(f'LightBar{i}', 'work'), (x1 + 0.10, y, zl), (1, 0, 0), 0.15, 0.10, L['work_rect'],
                      r=0.016, proud=0.010, border=0.010, chamfer=0.004, seat=-0.002, crown=0.008,
                      body=[(-0.072, 0.74, 0.64), (-0.040, 0.96, 0.93)])
        u_bracket(kb, (x1 + 0.065, y, zl), (0, 1, 0), 0.083, zb + 0.035)
        sockets.append((f'LightBar{i}', f, Vector((1, 0, 0)), 'work', Vector((0, 0, 1))))
    # load: two orange sand ladders on pins, jerrycans, crate, tarp roll (the spare rides on the rear door now)
    for y0, y1 in ((0.06, 0.38), (0.42, 0.74)):
        props_kb.box(-0.90, 0.20, y0, y1, zb + 0.02, zb + 0.048, Z_ORANGE)
        for x in (-0.80, 0.10):
            props_kb.cyl((x, (y0 + y1) / 2, zb + 0.048), (x, (y0 + y1) / 2, zb + 0.08), 0.013, Z_BLACK, 6)
    for y0, y1 in ((-0.74, -0.40), (-0.37, -0.03)):
        props_kb.box(-1.62, -1.155, y0, y1, zb + 0.02, zb + 0.185, Z_CAN)
    props_kb.box(0.22, 0.76, -0.70, -0.22, zb + 0.02, zb + 0.32, Z_CRATE)
    props_kb.cyl((0.60, 0.05, zb + 0.13), (0.60, 0.74, zb + 0.13), 0.11, Z_BAG, 12)
    return sockets


def flare(kb, xc, zc, sgn, a0=8, a1=172, r0=0.445, r1=0.518, y0=0.972, y1=1.055, n=14):
    """arch flare: rectangular section swept round the wheel centre (x-z plane), outboard of the body side"""
    ring = []
    for k in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * k / n)
        ca, sa = math.cos(a), math.sin(a)
        sec = [(r0, y0), (r1, y0), (r1, y1), (r0, y1)]
        ring.append([kb.bm.verts.new(Vector((xc + r * ca, sgn * yy, zc + r * sa))) for r, yy in sec])
    for a, b in zip(ring[:-1], ring[1:]):
        for i in range(4):
            q = [a[i], b[i], b[(i + 1) % 4], a[(i + 1) % 4]]
            kb._f(q if sgn > 0 else list(reversed(q)), Z_BLACK)
    kb._f(ring[0] if sgn > 0 else list(reversed(ring[0])), Z_BLACK)
    kb._f(list(reversed(ring[-1])) if sgn > 0 else ring[-1], Z_BLACK)


def side_kit(kb, lens, S, L, door_kb, split_front=False):
    """rub rails, steps, mud flaps, rear flares (body); front flares and door rails go to door_kb[side].
    split_front (v3, opening cab doors): only the part of the front flare along the door's arch cut-out rides on the
    door (26..131 deg), the ends below and behind the door stay on the body"""
    sockets = []
    for s in (1, -1):
        segs = [(-2.10, 0.84)] if s > 0 else [(-2.10, -0.63), (0.39, 0.84)]
        for xa, xb in segs:
            kb.strip([(xa, s * 0.995, 1.02), (xb, s * 0.995, 1.02)], (0, 0, 1), 0.016, 0.034, Z_BLACK)
        door_kb[s].strip([(0.93, s * 0.978, 1.02), (1.80, s * 0.978, 1.02)], (0, 0, 1), 0.016, 0.034, Z_BLACK)
        flare(kb, -1.203, 0.323, s)
        if split_front:
            flare(kb, 1.207, 0.323, s, a0=8, a1=26, n=2)
            flare(door_kb[s], 1.207, 0.323, s, a0=26, a1=131, n=9)
            flare(kb, 1.207, 0.323, s, a0=131, a1=172, n=4)
        else:
            flare(door_kb[s], 1.207, 0.323, s)
        kb.box(1.04, 1.70, min(s * 0.88, s * 1.06), max(s * 0.88, s * 1.06), 0.39, 0.42, Z_BLACK)     # cab step
        for x in (1.10, 1.64):
            kb.box(x - 0.02, x + 0.02, min(s * 0.80, s * 0.95), max(s * 0.80, s * 0.95), 0.40, 0.50, Z_BLACK)
        for xf in (-1.695, 0.735):                                        # mud flaps behind the wheels
            kb.box(xf - 0.005, xf + 0.005, min(s * 0.66, s * 0.99), max(s * 0.66, s * 0.99), 0.11, 0.42, Z_RUBBER)
        for i, x in enumerate((-2.03, -0.72 if s > 0 else -0.78)):        # side markers: capsules on a base
            c = Vector((x, s * 0.99, 0.93))
            n = Vector((0, s, 0))
            name = f'Marker_Side{"L" if s > 0 else "R"}{i}'
            f = marker_lamp(kb, lens(name, 'marker'), c, n, (1, 0, 0), 0.028, 0.012, 0.013, L['side_amber'], base_t=0.006)
            sockets.append((name, f, n, 'marker', Vector((0, 0, 1))))
    kb.box(-0.56, 0.32, -1.06, -0.88, 0.39, 0.42, Z_BLACK)               # saloon door step (right)
    return sockets


SPARE_C = []                       # hub plate centre of the spare carrier (filled by rear_kit)


def spare_carrier(kb, S, wheel=True):
    """carrier with the spare wheel on the right rear door (joined into the door, swings with it); wheel=False (v3):
    only the carrier and its hub plate, the spare is a separate item"""
    yc, zc = -0.36, 1.045
    xr = S['rear'](yc, zc) - 0.003
    for yy in (-0.62, -0.10):
        kb.tube([(xr + 0.005, yy, 0.80), (xr - 0.05, yy, 0.80), (xr - 0.05, yy, 1.30), (xr + 0.005, yy, 1.30)],
                0.013, Z_BLACK, 6)
    kb.tube([(xr - 0.05, -0.62, zc), (xr - 0.05, -0.10, zc)], 0.013, Z_BLACK, 6)
    kb.cyl((xr - 0.04, yc, zc), (xr - 0.085, yc, zc), 0.075, Z_BLACK, 12)
    c, ax = Vector((xr - 0.085, yc, zc)), Vector((-1, 0, 0))
    if not wheel:
        kb.cyl(c, c + ax * 0.06, 0.012, Z_BLACK, 8)                   # the stud the spare hangs on
        return c
    kb.lathe(c, ax, [(0.0, 0.305), (0.03, 0.34), (0.21, 0.34), (0.24, 0.305), (0.24, 0.19)], Z_SPARE, 20, cap1=False)
    kb.lathe(c, ax, [(0.24, 0.19), (0.20, 0.19), (0.20, 0.07), (0.215, 0.05)], Z_SPARERIM, 20, cap0=False)
    return c


def rear_kit(kb, lens, S, L, ladder_kb, spare_kb=None, spare_wheel=True):
    sockets = []
    for s in (1, -1):
        y = s * 0.845
        xr = S['rear'](y, 0.90)
        side = 'L' if s > 0 else 'R'
        sockets += tail_cluster(kb, lens, Vector((xr - 0.002, y, 0.905)), Vector((-1, 0, 0)), 0.125, 0.42, L, side)
    if spare_kb is not None:
        SPARE_C[:] = list(spare_carrier(spare_kb, S, spare_wheel))
    # work lamp over the rear doors: a can on an L-bracket, tilted 20 deg down
    xr = S['rear'](0.0, 1.95)
    n = Vector((-math.cos(math.radians(20)), 0, -math.sin(math.radians(20))))
    pc = Vector((xr - 0.085, 0.0, 1.935))
    kb.box(xr - 0.035, xr + 0.004, -0.03, 0.03, 1.955, 1.975, Z_BLACK)
    kb.box(xr - 0.045, xr - 0.025, -0.012, 0.012, 1.93, 1.975, Z_BLACK)
    f = rect_lamp(kb, lens('Work_Rear', 'work'), pc, n, 0.17, 0.085, L['work_rect'], r=0.014, proud=0.010,
                  border=0.009, chamfer=0.004, seat=-0.002, crown=0.008, body=[(-0.06, 0.74, 0.62), (-0.035, 0.96, 0.92)])
    sockets.append(('Work_Rear', f, n, 'work', Vector((0, 0, 1))))
    # number-plate lamp: a small hooded lamp under the step, glass facing down
    f = rect_lamp(kb, lens('Plate', 'plate'), (-2.21, 0, 0.690), (0, 0, -1), 0.10, 0.036, L['plate'], up=Vector((1, 0, 0)),
                  r=0.008, proud=0.006, border=0.006, chamfer=0.002, seat=0.002, crown=0.004,
                  body=[(-0.022, 0.86, 0.80)])
    sockets.append(('Plate', f, Vector((0, 0, -1)), 'plate', Vector((1, 0, 0))))
    # ladder on the left rear door
    for y in (0.20, 0.58):
        ladder_kb.tube([(-2.235, y, 0.62), (-2.235, y, 1.86)], 0.014, Z_BLACK, 8)
        for z in (0.70, 1.78):
            ladder_kb.box(-2.235, -2.195, y - 0.015, y + 0.015, z - 0.015, z + 0.015, Z_BLACK)
    for z in np.arange(0.72, 1.86, 0.22):
        ladder_kb.tube([(-2.235, 0.20, z), (-2.235, 0.58, z)], 0.011, Z_BLACK, 6)
    return sockets


# ====================================================================== interior lamps
def dome_lamps(kb, lens, S, L, spots):
    sockets = []
    for name, (x, y) in spots.items():
        zc = S['ceil'](x, y)
        kb.lathe((x, y, zc + 0.003), (0, 0, -1), [(0, 0.097), (0.004, 0.098), (0.010, 0.093), (0.016, 0.083)],
                 Z_DOMEBASE, 16)
        lens(name, 'dome').disc_uv((x, y, zc - 0.013), (0, 0, -1), 0.081, L['dome'], 16, dome=0.026)
        sockets.append((name, Vector((x, y, zc - 0.045)), Vector((0, 0, -1)), 'dome', Vector((1, 0, 0))))
    return sockets


def night_lamp(kb, lens, L, x=0.884, y=0.0, z=1.72):
    """red night lamp on the partition: a hex boss and a ribbed red dome, like the plane's tail light"""
    c, ax = Vector((x - 0.001, y, z)), Vector((-1, 0, 0))
    kb.lathe(c, ax, [(0, 0.036), (0.012, 0.036), (0.015, 0.031)], Z_BLACK, 6, cap0=False)
    lens('Night_Red', 'night').disc_uv(c + ax * 0.014, ax, 0.029, L['night_red'], 12, dome=0.024)
    return [('Night_Red', c + ax * 0.04, ax, 'night', Vector((0, 0, 1)))]


# ====================================================================== dashboard (the plane's panel language)
DASH_A, DASH_B = (1.745, 1.06), (1.775, 1.33)


def dash_x(z):
    return DASH_A[0] + (DASH_B[0] - DASH_A[0]) * (z - DASH_A[1]) / (DASH_B[1] - DASH_A[1])


DASH_N = Vector((-(DASH_B[1] - DASH_A[1]), 0, DASH_B[0] - DASH_A[0])).normalized()   # towards the driver, a little up


def on_face(y, z, off):
    return Vector((dash_x(z), y, z)) + DASH_N * off


COLUMN_Y = 0.525        # steering column: the two big dials sit either side of the hub, as seen from the driver
GAUGES = {   # name: (y, z, diameter, region)
    'speedo': (0.375, 1.217, 0.105, 'speedo'), 'tacho': (0.675, 1.217, 0.105, 'tacho'),
    'fuel': (0.792, 1.272, 0.052, 'fuel'), 'temp': (0.792, 1.200, 0.052, 'temp'),
    'oil': (0.258, 1.272, 0.052, 'oil'), 'volt': (0.258, 1.200, 0.052, 'volt'),
    'clock': (0.10, 1.305, 0.05, 'clock'),
}
NEEDLE_REST = {'speedo': -120.0, 'tacho': -81.6, 'fuel': 18.0, 'temp': 7.5, 'oil': -12.0, 'volt': 16.0}


TOGGLE_PIVOTS = []


GLOVE = dict(y0=-0.78, y1=-0.34, z0=1.12, z1=1.27, back=1.93)    # glovebox opening on the dash face (v3)


def dashboard(kb, gk, G, toggle_kb=None, toggles_on=None, glove_kb=None):
    """kb: dash body (zones), gk: gauge faces with explicit UVs into the gauges atlas.  Returns needle specs.
    toggle_kb(i) (v3): a KB for each toggle lever, so it can move (pivot in TOGGLE_PIVOTS); toggles_on: lever
    positions (default: alternating, as v2)"""
    prof = [DASH_A, DASH_B, (1.795, 1.37), (1.975, 1.372), (1.975, 1.06)]
    zones = [Z_DASHP, Z_DASHPAD, Z_DASHPAD, Z_DASHP, Z_DASHP]
    y0, y1 = -0.845, 0.845
    va = [kb.bm.verts.new(Vector((x, y0, z))) for x, z in prof]
    vb = [kb.bm.verts.new(Vector((x, y1, z))) for x, z in prof]
    n = len(prof)
    for i in range(n):
        j = (i + 1) % n
        if i == 0 and glove_kb is not None:
            continue                                   # the face is built in pieces round the glovebox opening
        kb._f([va[i], va[j], vb[j], vb[i]], zones[i])
    kb._f(list(reversed(va)), Z_DASHP)
    kb._f(vb, Z_DASHP)
    if glove_kb is not None:
        g = GLOVE
        P = lambda y, z: Vector((dash_x(z), y, z))
        corner = {(y0, DASH_A[1]): va[0], (y0, DASH_B[1]): va[1], (y1, DASH_B[1]): vb[1], (y1, DASH_A[1]): vb[0]}
        cache = {}

        def V(y, z):
            k = (round(y, 5), round(z, 5))
            if (y, z) in corner:
                return corner[(y, z)]
            if k not in cache:
                cache[k] = kb.bm.verts.new(P(y, z))
            return cache[k]
        za, zb = DASH_A[1], DASH_B[1]
        for (ya, yb, z0, z1) in ((y0, g['y0'], za, zb), (g['y1'], y1, za, zb), (g['y0'], g['y1'], za, g['z0']),
                                 (g['y0'], g['y1'], g['z1'], zb)):
            kb._f([V(ya, z0), V(ya, z1), V(yb, z1), V(yb, z0)], Z_DASHP)
        # the bin: walls from the opening back to a flat back, faces looking into the bin
        mid = Vector(((dash_x(1.195) + g['back']) / 2, (g['y0'] + g['y1']) / 2, (g['z0'] + g['z1']) / 2))
        B = lambda y, z: Vector((g['back'], y, z))
        quads = [[P(g['y0'], g['z0']), P(g['y1'], g['z0']), B(g['y1'], g['z0']), B(g['y0'], g['z0'])],
                 [P(g['y0'], g['z1']), P(g['y1'], g['z1']), B(g['y1'], g['z1']), B(g['y0'], g['z1'])],
                 [P(g['y0'], g['z0']), P(g['y0'], g['z1']), B(g['y0'], g['z1']), B(g['y0'], g['z0'])],
                 [P(g['y1'], g['z0']), P(g['y1'], g['z1']), B(g['y1'], g['z1']), B(g['y1'], g['z0'])],
                 [B(g['y0'], g['z0']), B(g['y1'], g['z0']), B(g['y1'], g['z1']), B(g['y0'], g['z1'])]]
        for q in quads:
            c = sum(q, Vector()) / 4
            nrm = (q[1] - q[0]).cross(q[2] - q[0])
            kb.poly(q if nrm.dot(mid - c) > 0 else list(reversed(q)), Z_BLACK)
    # cluster: black backing, hood and cheeks (the plane's instrument panel language)
    R = Matrix((Vector((0, -1, 0)), DASH_N.cross(Vector((0, -1, 0))).normalized(), DASH_N)).transposed()
    c = on_face(0.525, 1.248, 0.002)
    kb.box_m(c, (0.64, 0.17, 0.004), Z_DASHPAD, R)
    kb.box(1.70, 1.80, 0.205, 0.845, 1.335, 1.358, Z_DASHPAD)
    for yy in (0.212, 0.838):
        kb.box(1.715, 1.785, yy - 0.0075, yy + 0.0075, 1.165, 1.35, Z_DASHPAD)
    # centre stack bezel, glovebox, grab handle
    kb.box_m(on_face(0.0, 1.20, 0.004), (0.40, 0.21, 0.008), Z_DASHPAD, R)
    gl = kb if glove_kb is None else glove_kb
    gl.box_m(on_face(-0.56, 1.195, 0.006), (0.44, 0.15, 0.012), Z_DASHP, R)
    gl.box_m(on_face(-0.56, 1.255, 0.016), (0.08, 0.012, 0.012), Z_CHROME, R)
    handle = [on_face(y, 1.31, 0.05) for y in np.linspace(-0.76, -0.36, 7)]
    kb.tube([on_face(-0.76, 1.31, 0.0)] + handle + [on_face(-0.36, 1.31, 0.0)], 0.011, Z_BLACK, 8)
    # panel screws, as on the plane's panels
    for (y, z, off) in ((0.222, 1.172, 0.004), (0.828, 1.172, 0.004), (0.19, 1.105, 0.008), (-0.19, 1.105, 0.008),
                        (0.19, 1.295, 0.008), (-0.19, 1.295, 0.008), (-0.77, 1.132, 0.012), (-0.35, 1.132, 0.012),
                        (-0.77, 1.258, 0.012), (-0.35, 1.258, 0.012)):
        kb.lathe(on_face(y, z, off), DASH_N, [(0, 0.0055), (0.0018, 0.0048), (0.0028, 0.0012)], Z_CHROME, 6)
    # gauge faces (explicit UVs) and their needles
    needles = []
    for name, (y, z, d, reg) in GAUGES.items():
        gk.disc_uv(on_face(y, z, 0.0045), DASH_N, d / 2, G[reg], 20)
        kb.lathe(on_face(y, z, 0.002), DASH_N, [(0, d / 2 + 0.004), (0.006, d / 2 + 0.004), (0.006, d / 2 - 0.001)],
                 Z_CHROME, 20, cap0=False, cap1=False)
        if name in NEEDLE_REST:
            needles.append((name, on_face(y, z, 0.007), d / 2 * 0.82))
    gk.quad_uv(on_face(GAUGES['speedo'][0], GAUGES['speedo'][1] - 0.021, 0.0052), DASH_N, 0.032, 0.006, G['odo'])
    gk.quad_uv(on_face(-0.10, 1.305, 0.0045), DASH_N, 0.06, 0.06, G['compass'])
    gk.quad_uv(on_face(COLUMN_Y, 1.313, 0.0045), DASH_N, 0.22, 0.0275, G['annun'])
    gk.quad_uv(on_face(0.0, 1.240, 0.009), DASH_N, 0.36, 0.045, G['switch'])
    gk.quad_uv(on_face(0.0, 1.172, 0.009), DASH_N, 0.20, 0.05, G['radio'])
    gk.quad_uv(on_face(0.0, 1.110, 0.009), DASH_N, 0.24, 0.045, G['heater'])
    gk.quad_uv(on_face(-0.56, 1.298, 0.005), DASH_N, 0.10, 0.025, G['placard'])
    # toggle switches on the plate: ON leans up, OFF down
    TOGGLE_PIVOTS.clear()
    for i in range(8):
        u = (12 + 24 * i) / 192
        y = 0.18 - u * 0.36
        z = 1.240 + 0.0225 - (14 / 24) * 0.045
        base = on_face(y, z, 0.009)
        kb.lathe(base, DASH_N, [(0, 0.0075), (0.004, 0.0075), (0.005, 0.0045)], Z_CHROME, 8)
        on = (i % 2 == 0) if toggles_on is None else toggles_on[i]
        d = (DASH_N + Vector((0, 0, 0.55 if on else -0.55))).normalized()
        tk = kb if toggle_kb is None else toggle_kb(i)
        tk.cyl(base + DASH_N * 0.004, base + DASH_N * 0.004 + d * 0.022, 0.0032, Z_CHROME, 6, r2=0.0024)
        TOGGLE_PIVOTS.append(base + DASH_N * 0.004)
    return needles


def needle_object(name, centre, length, angle_deg, coll=None):
    """thin needle on a dial; origin at the hub (animate about the dial normal)"""
    kb = KB()
    u, v = up_axes(DASH_N)
    a = math.radians(angle_deg)
    d = v * math.cos(a) + u * math.sin(a)
    s = d.cross(DASH_N).normalized()
    c = Vector(centre)
    tip, tail = c + d * length, c - d * length * 0.18
    w = 0.0022
    pts = [tail - s * w, tail + s * w, c + s * w * 1.4, tip, c - s * w * 1.4]
    kb.poly(list(reversed(pts)) if (pts[1] - pts[0]).cross(pts[2] - pts[1]).dot(DASH_N) < 0 else pts, ZERO)
    kb.lathe(c, DASH_N, [(0, 0.0055), (0.002, 0.0055), (0.003, 0.0)], ZERO, 10, cap0=False)
    return kb.to_object('Needle_' + name, coll, pivot=c)


# ====================================================================== folding troop seats (after the plane's)
FLOOR = 0.716
YW, YH, ZH = 0.900, 0.833, FLOOR + 0.42
DEPTH = 0.36
YF = YH - DEPTH
SY, RT, BR = 0.205, 0.011, 0.025
RODX, BKX = 0.239, 0.232
YR, RR = 0.871, 0.011
ZLB, ZU = FLOOR + 0.461, FLOOR + 0.64
RAILX = 0.236
LEGX, LOFF, RL = 0.167, 0.025, 0.010
SAG = 0.015


def canvas_z(y, dx):
    u = (YH - y) / (YH - YF)
    w = math.sin(math.pi * min(max(u, 0.0), 1.0)) ** 0.7
    return ZH + 0.002 - SAG * (1 - (dx / SY) ** 2) * w


def seat_fixed(kb, xc):
    for s in (-1, 1):
        x = xc + s * BKX
        kb.box(x - 0.003, x + 0.003, YH - 0.024, YW, ZH - 0.035, ZLB + 0.03, Z_BRACKET)
        kb.cyl((x - s * 0.0, YH, ZH), (x - s * 0.013, YH, ZH), 0.0175, Z_BRACKET, 12)
        kb.box(x - 0.02, x + 0.02, YW - 0.006, YW, ZLB - 0.02, ZU + 0.02, Z_BRACKET)          # wall flange
    for zr in (ZU, ZLB):
        kb.tube([(xc - RAILX, YR, zr), (xc + RAILX, YR, zr)], RR, Z_TUBE, 10)
        for s in (-1, 1):
            kb.box(xc + s * RAILX - 0.006, xc + s * RAILX + 0.006, YR, YW, zr - 0.015, zr + 0.015, Z_BRACKET)
    z0, z1 = ZLB + RR + 0.016, ZU - RR - 0.016

    def back_mid(sv, tv):
        bell = 0.006 * math.sin(math.pi * (tv - z0) / (z1 - z0)) * (1 - (sv / 0.198) ** 2)
        return Vector((xc + sv, YR - bell, tv))
    ss = [-0.198 + 0.396 * i / 8 for i in range(9)]
    ts = [z0 + (z1 - z0) * j / 5 for j in range(6)]
    kb.slab(back_mid, lambda a, b: Vector((0, -1, 0)), ss, ts, 0.004, Z_CANVAS)
    for zr, zc in ((ZU, z1), (ZLB, z0)):                                   # lacing: short straps rail -> canvas
        for lx in np.linspace(-0.16, 0.16, 5):
            kb.box(xc + lx - 0.005, xc + lx + 0.005, YR - 0.014, YR + 0.004,
                   min(zr, zc) - 0.002, max(zr, zc) + 0.002, Z_WEB)


def seat_pan(kb, xc):
    path = fillet([(xc - SY, YH, ZH), (xc - SY, YF, ZH), (xc + SY, YF, ZH), (xc + SY, YH, ZH)], BR, 4)
    kb.tube(path, RT, Z_TUBE, 10, caps=False)
    kb.tube([(xc - RODX, YH, ZH), (xc + RODX, YH, ZH)], RT, Z_TUBE, 10)
    for s in (-1, 1):
        kb.lathe((xc + s * (BKX + 0.0065), YH, ZH), (s, 0, 0), [(0, 0.0135), (0.009, 0.0135), (0.011, 0.0115)], Z_BRACKET, 6)
    ya, yb = YF + RT + 0.006, YH - RT - 0.006
    xa = SY - RT - 0.006
    ys = [ya + (yb - ya) * i / 7 for i in range(8)]
    xs = [-xa + 2 * xa * j / 7 for j in range(8)]
    kb.slab(lambda yy, dx: Vector((xc + dx, yy, canvas_z(yy, dx))), lambda a, b: Vector((0, 0, 1)), ys, xs, 0.004, Z_CANVAS)
    for s, kind in ((1, 'buckle'), (-1, 'latch')):                         # lap belt halves on the canvas
        x = xc + s * 0.13
        y_end = YH - (0.21 if kind == 'buckle' else 0.19)
        pts = [Vector((x, YH - 0.02, ZH + 0.012)), Vector((x - s * 0.02, (YH + y_end) / 2, canvas_z((YH + y_end) / 2, x - xc) + 0.004)),
               Vector((xc + s * 0.03, y_end, canvas_z(y_end, s * 0.03) + 0.004))]
        kb.strip(pts, (0, 0, 1), 0.046, 0.002, Z_WEB)
        tip = pts[-1]
        if kind == 'buckle':
            kb.box(tip.x - 0.035, tip.x + 0.035, tip.y - 0.026, tip.y + 0.026, tip.z, tip.z + 0.009, Z_BUCKLE)
        else:
            kb.box(tip.x - 0.028, tip.x + 0.028, tip.y - 0.018, tip.y + 0.018, tip.z, tip.z + 0.003, Z_BUCKLE)
    kb.box(xc - 0.012, xc + 0.012, YF - 0.006, YF - 0.002, ZH - 0.055, ZH + 0.005, Z_WEB)        # pull tab


def seat_leg(kb, xc):
    yl = YF - LOFF
    for s in (-1, 1):
        x = xc + s * LEGX
        kb.tube([(x, yl, ZH - 0.014), (x, yl, FLOOR + 0.015)], RL, Z_TUBE, 8, caps=True)
        kb.lathe((x, yl, FLOOR + 0.015), (0, 0, -1), [(0, 0.0125), (0.010, 0.0135), (0.015, 0.011)], Z_FEET, 10)
        kb.cyl((x - 0.012, YF, ZH), (x + 0.012, YF, ZH), 0.0165, Z_BRACKET, 10)
        kb.box(x - 0.010, x + 0.010, yl - 0.004, YF + 0.004, ZH - 0.027, ZH - 0.007, Z_BRACKET)
    kb.tube([(xc - LEGX + 0.006, yl, FLOOR + 0.16), (xc + LEGX - 0.006, yl, FLOOR + 0.16)], 0.008, Z_TUBE, 8)

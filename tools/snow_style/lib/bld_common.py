"""Shared build steps for the station's buildings (lifted from main_build.py so the garage and the next locations use the
same code): KBs -> objects with their pivots, lit parts facing out, glass doubled, welding, the coplanar separation, the
PSX atlases baked through one joined proxy per group (texel density capped at the generator shed's), the named
materials (Update 3's definitions), the joins of the decal / glass / lens objects into their owners, part boxes."""
import math, json, time
import bpy, bmesh
import numpy as np
from mathutils import Vector
import psxlib as P
import psxbake as PB
import gen_kit as K
import uvpack

SHED_PPM = 51.3


def r4(v):
    return [round(float(a), 4) + 0.0 for a in v]


def nz(o):
    if isinstance(o, float):
        return o + 0.0
    if isinstance(o, dict):
        return {k: nz(v) for k, v in o.items()}
    if isinstance(o, (list, tuple)):
        return [nz(v) for v in o]
    return o


def to_unity(v):
    """building frame -> Unity prefab-local: (x, y, z) -> (-x, z, -y)"""
    return [round(-float(v[0]), 4) + 0.0, round(float(v[2]), 4) + 0.0, round(-float(v[1]), 4) + 0.0]


def is_joined(a):
    return a in ('glass', 'panel') or a.startswith('lens:')


# ---------------------------------------------------------------- KB preparation
def face_out(k):
    """lit parts face out (Unity draws one side): a majority vote of the faces against the centre"""
    k.bm.normal_update()
    vs = [v.co for v in k.bm.verts]
    cc = sum(vs, Vector()) / max(len(vs), 1)
    vote = 0
    for f in k.bm.faces:
        d = f.calc_center_median() - cc
        if d.length > 1e-6:
            dv = f.normal.dot(d.normalized())
            vote += 1 if dv > 0.1 else -1 if dv < -0.1 else 0     # flat lenses side by side: no say
    if vote < 0:
        bmesh.ops.reverse_faces(k.bm, faces=list(k.bm.faces))


def double_glass(k):
    """glass is seen from both sides: each pane twice, back to back"""
    for f in list(k.bm.faces):
        pts = [tuple(v.co) for v in f.verts]
        uvs = [tuple(l[k.uvl].uv) for l in f.loops]
        k.poly(list(reversed(pts)), f[k.zl], list(reversed(uvs)))


def prepare_kbs(no_weld=()):
    for name, k in K.KBS.items():
        a = K.ATLAS[name]
        if a.startswith('lens:'):
            face_out(k)
        elif a == 'glass':
            double_glass(k)
    for name, k in K.KBS.items():
        if not is_joined(K.ATLAS[name]):
            keep = {v for f in k.bm.faces if int(round(f[k.zl])) in no_weld for v in f.verts}
            bmesh.ops.remove_doubles(k.bm, verts=[v for v in k.bm.verts if v not in keep], dist=1e-4)


def _to2d(pts, n):
    u = (Vector((1, 0, 0)) if abs(n.x) < 0.9 else Vector((0, 1, 0))).cross(n).normalized()
    v = n.cross(u)
    P2 = [(p.dot(u), p.dot(v)) for p in pts]
    a = sum(P2[i][0] * P2[(i + 1) % len(P2)][1] - P2[(i + 1) % len(P2)][0] * P2[i][1] for i in range(len(P2)))
    return P2 if a > 0 else list(reversed(P2))


def _clip(subject, clipper):
    out = subject
    for i in range(len(clipper)):
        a, b = clipper[i], clipper[(i + 1) % len(clipper)]
        inp, out = out, []
        if not inp:
            break
        ins = lambda p: (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]) >= -1e-9

        def inter(p, q):
            x1, y1, x2, y2, x3, y3, x4, y4 = p[0], p[1], q[0], q[1], a[0], a[1], b[0], b[1]
            den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4)
            if abs(den) < 1e-12:
                return q
            t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / den
            return (x1 + t * (x2 - x1), y1 + t * (y2 - y1))
        for j in range(len(inp)):
            p, q = inp[j], inp[(j + 1) % len(inp)]
            if ins(q):
                if not ins(p):
                    out.append(inter(p, q))
                out.append(q)
            elif ins(p):
                out.append(inter(p, q))
    return out


def separate_coplanar(names, push=0.0008, info=None):
    """faces in one plane, facing the same way and overlapping, fight for the pixel in the game: taken largest first,
    each face that overlaps faces already placed is lifted off its neighbours and pushed out along its normal one step
    past the highest of them (as a decal is). Runs on the welded KBs before they become objects."""
    Fs, buckets = [], {}
    for name in names:
        k = K.KBS[name]
        k.bm.normal_update()
        k.bm.faces.ensure_lookup_table()
        for f in k.bm.faces:
            a = f.calc_area()
            if a < 1e-6:
                continue
            n = f.normal.copy()
            pts = [v.co.copy() for v in f.verts]
            d = n.dot(f.calc_center_median())
            Fs.append((name, f.index, n, d, pts, int(round(f[k.zl])), a))
            key = (round(n.x * 100), round(n.y * 100), round(n.z * 100), round(d * 1000))
            buckets.setdefault(key, []).append(len(Fs) - 1)
    adj = {}
    for key, ids in buckets.items():
        cand = list(ids)
        for dk in (-1, 1):
            cand += buckets.get((key[0], key[1], key[2], key[3] + dk), [])
        for i in ids:
            a = Fs[i]
            for j in cand:
                if j <= i:
                    continue
                b = Fs[j]
                if a[2].dot(b[2]) < 0.999 or abs(a[3] - b[3]) > 0.001:
                    continue
                inter = _clip(_to2d(a[4], a[2]), _to2d(b[4], a[2]))
                if len(inter) >= 3:
                    ar = abs(sum(inter[q][0] * inter[(q + 1) % len(inter)][1] - inter[(q + 1) % len(inter)][0] *
                                 inter[q][1] for q in range(len(inter)))) / 2
                    if ar > 1e-5:
                        adj.setdefault(i, set()).add(j)
                        adj.setdefault(j, set()).add(i)
    plane = {}
    for i in sorted(adj, key=lambda i: -Fs[i][6]):
        top = max((plane[j] for j in adj[i] if j in plane), default=None)
        plane[i] = Fs[i][3] if top is None else max(Fs[i][3], top + push)
    by_kb, moved = {}, []
    for i, dn in plane.items():
        if dn - Fs[i][3] > 1e-6:
            by_kb.setdefault(Fs[i][0], []).append((Fs[i][1], dn - Fs[i][3]))
            moved.append(dn - Fs[i][3])
    for name, fis in by_kb.items():
        k = K.KBS[name]
        k.bm.faces.ensure_lookup_table()
        old = [(k.bm.faces[fi], dd) for fi, dd in fis]
        for f, dd in old:
            n = f.normal.copy()
            k.poly([v.co + n * dd for v in f.verts], f[k.zl], [tuple(l[k.uvl].uv) for l in f.loops])
        bmesh.ops.delete(k.bm, geom=[f for f, dd in old], context='FACES')
    if info is not None:
        info['coplanar_moved_mm'] = dict(n=len(moved), max=round(max(moved, default=0) * 1000, 2))
    return len(moved)


def make_objects(log=print):
    C = K.CONTRACT
    objs = {}
    for name, k in K.KBS.items():
        if not k.bm.faces:
            log('WARN empty', name)
        objs[name] = k.to_object(name, pivot=Vector(C['parts'][name]['pivot']))
    bpy.context.view_layer.update()
    for name, p in C['parts'].items():
        if name in objs and p.get('parent') and p['parent'] in objs:
            attach(objs[name], objs[p['parent']])
    for name, p in C['parts'].items():
        p['root'] = root_of(name)
    return objs


def attach(child, parent):
    bpy.context.view_layer.update()
    mw = child.matrix_world.copy()
    child.parent = parent
    child.matrix_parent_inverse = parent.matrix_world.inverted()
    child.matrix_world = mw


def root_of(name):
    C = K.CONTRACT
    while C['parts'][name].get('parent'):
        name = C['parts'][name]['parent']
    return name


# ---------------------------------------------------------------- atlases
def prep_atlas(objs_, cap, zone_scale=None, angle=50, tol=0.90, max_size=2048, oversample=2, A=None):
    """psxbake.prepare_atlas with the size chosen from the texel density: the smallest power of two that holds the group
    at >= tol x cap (the islands are then shrunk to cap: the shed's pixel, never finer)"""
    A = A or P.area3d(objs_)
    size = 256
    while size < max_size and size * math.sqrt(0.70 / A) < tol * cap:
        size *= 2
    P.unwrap_group(objs_, margin=2.2 / size, angle=angle, zone_scale=zone_scale)
    fill = max(P.uv_fill(objs_), 1e-6)
    keep = [np.empty(2 * len(o.data.loops), np.float32) for o in objs_]
    for o, b in zip(objs_, keep):
        o.data.uv_layers['PSX'].data.foreach_get('uv', b)
    f2 = uvpack.repack(objs_, size, 2.2)
    if f2 < fill:
        for o, b in zip(objs_, keep):
            o.data.uv_layers['PSX'].data.foreach_set('uv', b)
    else:
        fill = max(f2, 1e-6)
    if size < max_size and size * math.sqrt(fill / A) < tol * cap:
        size *= 2
    if size * math.sqrt(fill / A) > cap:
        kk = cap / (size * math.sqrt(fill / A))
        for o in objs_:
            for l in o.data.uv_layers['PSX'].data:
                l.uv = l.uv * kk
        fill = max(P.uv_fill(objs_), 1e-6)
    hi = size * oversample
    pos, nor, zon = PB.bake_pass(objs_, 'pos', hi), PB.bake_pass(objs_, 'nor', hi), PB.bake_pass(objs_, 'zone', hi)
    cov = P.coverage_mask(objs_, hi)
    cov &= np.abs(np.linalg.norm(nor[..., :3] * 2 - 1, axis=-1) - 1) < 0.1
    return dict(size=size, hi=hi, oversample=oversample, A=A, fill=fill, pos=pos, nor=nor, zon=zon, cov=cov)


def hidden(obs, flag=True):
    for o in obs:
        o.hide_render = flag


def proxy(objs_):
    """one joined copy of a group for the bakes (Cycles syncs the scene once per baked object)"""
    if len(objs_) == 1:
        return objs_[0], []
    sc_ = bpy.context.scene
    cps = []
    for i, o in enumerate(objs_):
        c = o.copy()
        c.data = o.data.copy()
        sc_.collection.objects.link(c)
        mw = o.matrix_world.copy()
        c.parent = None
        c.matrix_world = mw
        n = len(c.data.loops)
        c.data.attributes.new('src_obj', 'INT', 'CORNER').data.foreach_set('value', np.full(n, i, np.int32))
        c.data.attributes.new('src_loop', 'INT', 'CORNER').data.foreach_set('value', np.arange(n, dtype=np.int32))
        cps.append(c)
    P.select_only(cps, active=cps[0])
    bpy.ops.object.join()
    px = cps[0]
    px.name = 'BAKE_PROXY'
    hidden(objs_)
    return px, objs_


def drop_proxy(px, orig):
    """the proxy's UVs and material back onto the parts, the proxy removed"""
    if not orig:
        return
    m_ = px.data.materials[0]
    nl = len(px.data.loops)
    buf = np.empty(2 * nl, np.float32)
    px.data.uv_layers['PSX'].data.foreach_get('uv', buf)
    buf = buf.reshape(-1, 2)
    so, sl = np.empty(nl, np.int32), np.empty(nl, np.int32)
    px.data.attributes['src_obj'].data.foreach_get('value', so)
    px.data.attributes['src_loop'].data.foreach_get('value', sl)
    for i, o in enumerate(orig):
        me = o.data
        m = so == i
        uv = np.zeros((len(me.loops), 2), np.float32)
        uv[sl[m]] = buf[m]
        me.uv_layers['PSX'].data.foreach_set('uv', uv.reshape(-1))
        me.materials.clear()
        me.materials.append(m_)
    hidden(orig, False)
    me = px.data
    bpy.data.objects.remove(px)
    bpy.data.meshes.remove(me)


_draft_cache = {}


def draft(objs_, colour_of):
    """flat zone colours instead of a bake (quick builds)"""
    for o in objs_:
        me = o.data
        zl = [round(a.value) for a in me.attributes['zone'].data]
        me.materials.clear()
        idx = {}
        for z in sorted(set(zl)):
            if z not in _draft_cache:
                m = bpy.data.materials.new(f'draft_{z}')
                m.use_nodes = True
                pb = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
                c = PB.lin(*colour_of(z))
                pb.inputs['Base Color'].default_value = (float(c[0]), float(c[1]), float(c[2]), 1)
                pb.inputs['Roughness'].default_value = 0.75
                _draft_cache[z] = m
            idx[z] = len(me.materials)
            me.materials.append(_draft_cache[z])
        for poly, z in zip(me.polygons, zl):
            poly.material_index = idx[z]


def bake_groups(groups, order, spec, painter, tex_dir, prefix, mat_prefix, prio=(), only=None, colour_of=None,
                hide_always=(), log=print, info=None):
    """bake each group of `order` into its atlas (spec[a]: cap, ao_min, ao_dist, rough, zs, max, ncol); `only` -> the others
    get flat zone colours"""
    info = {} if info is None else info
    hidden(hide_always)
    for a in order:
        if a not in groups:
            continue
        if only is not None and a not in only:
            draft(groups[a], colour_of)
            continue
        sp = spec[a]
        t1 = time.time()
        px, orig = proxy(groups[a])
        prep = prep_atlas([px], sp['cap'], zone_scale=sp.get('zs'), A=P.area3d(groups[a]), max_size=sp.get('max', 2048))
        PB.finish_atlas(a, [px], prep, painter, tex_dir, prefix=prefix, mat_prefix=mat_prefix, ao_min=sp['ao_min'],
                        ao_dist=sp['ao_dist'], rough=sp['rough'], ncol=sp.get('ncol', 64), log=log, info=info,
                        prio_zones=prio)
        drop_proxy(px, orig)
        info[a]['seconds'] = round(time.time() - t1, 1)
        del prep
    hidden(hide_always, False)
    return info


# ---------------------------------------------------------------- materials, joins
def u3_mat(name, matdef):
    """a material the game knows by name (Update 3's Forest_Materials.json values, for the renders here)"""
    m = bpy.data.materials.get(name)
    if m:
        return m
    d = matdef[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    pb = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    pb.inputs['Base Color'].default_value = (*d['color'][:3], 1)
    pb.inputs['Roughness'].default_value = 1.0 - d.get('smoothness', 0.4)
    pb.inputs['Emission Color'].default_value = (*d['emission'][:3], 1)
    pb.inputs['Emission Strength'].default_value = float(d.get('emissionIntensity', 0.0))
    if d.get('type') == 'transparent':
        pb.inputs['Alpha'].default_value = float(d.get('alpha', 0.5))
        try:
            m.surface_render_method = 'BLENDED'
        except Exception:
            pass
    m['u3'] = json.dumps(d)
    return m


def join_all(objs, target_of, mat_for, log=print):
    """the decal / glass / lens objects (and baked splits) into their owners; returns {target: [sources]}"""
    C = K.CONTRACT
    joins = {}
    for name in list(objs):
        t = target_of(name)
        if t:
            joins.setdefault(t, []).append(name)
    for tgt, srcs in joins.items():
        T_ = objs[tgt]
        if not T_.data.materials:
            T_.data.materials.append(None)
        for s in srcs:
            o = objs[s]
            if is_joined(K.ATLAS[s]):
                o.data.materials.clear()
                o.data.materials.append(mat_for(s))
        P.select_only([objs[s] for s in srcs] + [T_], active=T_)
        bpy.ops.object.join()
        for s in srcs:
            del objs[s]
            C['parts'].pop(s, None)
        T_['joined'] = ','.join(srcs)
    log('joined', sum(len(v) for v in joins.values()), 'objects into', len(joins))
    for o in objs.values():                                   # the same material twice on one object -> one slot
        me = o.data
        seen = {}
        remap = []
        for i, m in enumerate(me.materials):
            remap.append(seen.setdefault(m.name if m else None, len(seen)))
        if len(seen) < len(me.materials):
            for poly in me.polygons:
                poly.material_index = remap[poly.material_index]
            mats = list(me.materials)
            me.materials.clear()
            done = set()
            for m in mats:
                if (m.name if m else None) not in done:
                    me.materials.append(m)
                    done.add(m.name if m else None)
    return joins


def part_boxes(objs):
    """grab / hit box of each moving part in its own frame"""
    n = 0
    for k_, p in K.CONTRACT['parts'].items():
        o = objs.get(k_)
        p['object'] = k_ if o else None
        if o is None or p['kind'] == 'static':
            continue
        co = np.array([tuple(v.co) for v in o.data.vertices])
        lo, hi = co.min(0), co.max(0)
        p['box'] = dict(center=r4((lo + hi) / 2), size=r4(np.maximum(hi - lo, 0.004)))
        n += 1
    return n


def psx_uv_only():
    for o in [o for o in bpy.data.objects if o.type == 'MESH']:
        uvs = o.data.uv_layers
        if 'PSX' in uvs:
            for u in [u for u in uvs if u.name != 'PSX']:
                uvs.remove(u)
            uvs['PSX'].active = True
            uvs['PSX'].active_render = True

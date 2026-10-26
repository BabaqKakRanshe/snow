"""PSX pipeline for a ready-made vehicle model (UAZ-452 from Sketchfab): import any format, report, normalise,
re-bake into PSX atlases the way the Utility Plane was made (unique UVs, ~50 px/m, AO in the colour,
1-texel seams, 64-colour palette, Bayer 4x4), point-filtered materials, quick views.
Frame after normalise: x forward, y left, z up, metres, ground at z = 0."""
import bpy, os, math, zipfile, shutil, json
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
BAYER4 = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], np.float32) + 0.5) / 16.0
MODEL_EXT = ('.glb', '.gltf', '.usdz', '.usdc', '.usda', '.usd', '.fbx', '.blend', '.obj', '.dae')


# ------------------------------------------------------------------ import
def unpack(path, dst_root):
    """zip -> its own new empty folder; returns the model file inside (preferring glb, gltf, usdz, fbx, blend, obj)"""
    if not path.lower().endswith('.zip'):
        return path
    name = os.path.splitext(os.path.basename(path))[0]
    dst = os.path.join(dst_root, name)
    if os.path.exists(dst):
        shutil.rmtree(dst)
    os.makedirs(dst)
    with zipfile.ZipFile(path) as z:
        for m in z.infolist():
            p = os.path.normpath(m.filename)
            if p.startswith('..') or os.path.isabs(p):
                continue                                   # no path traversal
            z.extract(m, dst)
    found = []
    for r, _, fs in os.walk(dst):
        for f in fs:
            if f.lower().endswith(MODEL_EXT):
                found.append(os.path.join(r, f))
    if not found:
        raise RuntimeError('no model file in ' + path)
    found.sort(key=lambda p: [e for e in MODEL_EXT].index(os.path.splitext(p)[1].lower()))
    return found[0]


def import_any(path):
    before = set(bpy.data.objects)
    ext = os.path.splitext(path)[1].lower()
    if ext in ('.glb', '.gltf'):
        bpy.ops.import_scene.gltf(filepath=path)
    elif ext in ('.usdz', '.usdc', '.usda', '.usd'):
        bpy.ops.wm.usd_import(filepath=path)
    elif ext == '.fbx':
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext == '.obj':
        bpy.ops.wm.obj_import(filepath=path)
    elif ext == '.dae':
        bpy.ops.wm.collada_import(filepath=path)
    elif ext == '.blend':
        with bpy.data.libraries.load(path, link=False) as (src, dst):
            dst.objects = src.objects
        for o in dst.objects:
            if o is not None:
                bpy.context.scene.collection.objects.link(o)
    else:
        raise RuntimeError('unknown format ' + ext)
    return [o for o in bpy.data.objects if o not in before]


# ------------------------------------------------------------------ report
def tri_count(o):
    dg = bpy.context.evaluated_depsgraph_get()
    me = o.evaluated_get(dg).to_mesh()
    me.calc_loop_triangles()
    n = len(me.loop_triangles)
    o.evaluated_get(dg).to_mesh_clear()
    return n


def world_verts(o):
    n = len(o.data.vertices)
    co = np.empty(n * 3, np.float32)
    o.data.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    M = np.array(o.matrix_world, np.float64)
    return co @ M[:3, :3].T + M[:3, 3]


def world_bbox(objs):
    """exact world bounds from the vertices (rotated local bounding boxes overshoot)"""
    lo, hi = np.full(3, np.inf), np.full(3, -np.inf)
    for o in objs:
        if o.type == 'MESH' and len(o.data.vertices):
            w = world_verts(o)
            lo = np.minimum(lo, w.min(0))
            hi = np.maximum(hi, w.max(0))
    return lo, hi


def socket_image(sock, depth=0):
    """image node feeding a socket (through mix / separate / normal-map nodes)"""
    if not sock.is_linked or depth > 6:
        return None
    n = sock.links[0].from_node
    if n.type == 'TEX_IMAGE':
        return n
    for s in n.inputs:
        r = socket_image(s, depth + 1)
        if r:
            return r
    return None


def material_info(m):
    info = dict(name=m.name, maps={})
    if not m.use_nodes:
        return info
    pb = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if pb:
        for key in ('Base Color', 'Metallic', 'Roughness', 'Normal', 'Alpha', 'Emission Color'):
            if key in pb.inputs:
                t = socket_image(pb.inputs[key])
                if t and t.image:
                    info['maps'][key] = f'{t.image.name} {t.image.size[0]}x{t.image.size[1]}'
        bc = pb.inputs['Base Color']
        if not bc.is_linked:
            info['color'] = [round(c, 3) for c in bc.default_value[:3]]
        info['alpha'] = round(pb.inputs['Alpha'].default_value, 3)
        info['blend'] = getattr(m, 'surface_render_method', '')
    for n in m.node_tree.nodes:                            # glTF occlusion lives in a separate group input
        if n.type == 'GROUP' and n.node_tree and 'glTF' in n.node_tree.name:
            for s in n.inputs:
                t = socket_image(s)
                if t and t.image:
                    info['maps'][s.name] = f'{t.image.name} {t.image.size[0]}x{t.image.size[1]}'
    return info


def report(objs, path=None):
    meshes = [o for o in objs if o.type == 'MESH']
    lo, hi = world_bbox(meshes)
    rows = []
    for o in sorted(meshes, key=lambda o: -tri_count(o)):
        lo_o, hi_o = world_bbox([o])
        rows.append(dict(name=o.name, parent=o.parent.name if o.parent else '', tris=tri_count(o),
                         mats=[s.material.name for s in o.material_slots if s.material],
                         uv=[u.name for u in o.data.uv_layers], bmin=lo_o.round(3).tolist(), bmax=hi_o.round(3).tolist()))
    mats = {}
    for o in meshes:
        for s in o.material_slots:
            if s.material and s.material.name not in mats:
                mats[s.material.name] = material_info(s.material)
    rep = dict(objects=len(objs), meshes=len(meshes), tris=sum(r['tris'] for r in rows),
               bbox_min=lo.round(3).tolist(), bbox_max=hi.round(3).tolist(), size=(hi - lo).round(3).tolist(),
               mesh_rows=rows, materials=list(mats.values()),
               images=[dict(name=i.name, size=list(i.size), file=i.filepath) for i in bpy.data.images if i.size[0]])
    if path:
        json.dump(rep, open(path, 'w'), indent=1, ensure_ascii=False)
    return rep


# ------------------------------------------------------------------ normalise
def normalise(objs, rot_z_deg=0.0, length=None, extra_rot=None, root_name='Vehicle'):
    """parent everything under one root, rotate so the nose is +x, scale to the real length, ground at z = 0,
    then bake the transforms into the meshes (no parent scale left for Unity)"""
    tops = [o for o in objs if o.parent is None]
    root = bpy.data.objects.new(root_name, None)
    bpy.context.scene.collection.objects.link(root)
    for o in tops:
        mw = o.matrix_world.copy()
        o.parent = root
        o.matrix_world = mw
    R = Matrix.Rotation(math.radians(rot_z_deg), 4, 'Z')
    if extra_rot:
        R = extra_rot @ R
    root.matrix_world = R
    bpy.context.view_layer.update()
    lo, hi = world_bbox(objs)
    s = 1.0 if not length else length / float(hi[0] - lo[0])
    root.matrix_world = Matrix.Scale(s, 4) @ R
    bpy.context.view_layer.update()
    lo, hi = world_bbox(objs)
    c = (lo + hi) / 2
    root.matrix_world = Matrix.Translation((-c[0], -c[1], -lo[2])) @ root.matrix_world
    bpy.context.view_layer.update()
    # bake transforms: every mesh gets identity parent chain
    for o in objs:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in [o for o in objs if o.type == 'MESH']:
        if o.data.users > 1:
            o.data = o.data.copy()
        o.data.transform(o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
    for o in [o for o in objs if o.type != 'MESH']:
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.objects.remove(root, do_unlink=True)
    return s


# ------------------------------------------------------------------ quick views (Workbench, texture colours)
def cam_look(name, loc, target, lens=35.0, ortho=None):
    cd = bpy.data.cameras.new(name)
    cam = bpy.data.objects.new(name, cd)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = loc
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    if ortho:
        cd.type = 'ORTHO'
        cd.ortho_scale = ortho
    else:
        cd.lens = lens
    cd.clip_end = 500
    bpy.context.scene.camera = cam
    return cam


def quick_views(objs, out_dir, prefix='v', res=(800, 500)):
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'TEXTURE'
    sh.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = False
    lo, hi = world_bbox(objs)
    c = (lo + hi) / 2
    L = float(max(hi - lo))
    views = {'fl': (1.2, 0.9, 0.45), 'rr': (-1.2, -0.9, 0.55), 'side': (0, 1.6, 0.15), 'front': (1.6, 0, 0.2),
             'top': (0.01, 0, 1.8)}
    out = []
    for k, d in views.items():
        cam = cam_look('cam_' + k, Vector(c) + Vector(d) * L, c, 40)
        sc.render.filepath = os.path.join(out_dir, f'{prefix}_{k}.png')
        bpy.ops.render.render(write_still=True)
        out.append(sc.render.filepath)
        bpy.data.objects.remove(cam, do_unlink=True)
    return out


# ------------------------------------------------------------------ PSX atlases (the Utility Plane recipe)
def select_only(objs, active=None):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]


def split_by_material(objs):
    """objects with several material slots -> one object per material (atlas groups follow materials)"""
    out = []
    for o in objs:
        if o.type != 'MESH':
            continue
        used = {p.material_index for p in o.data.polygons}
        if len(used) <= 1:
            out.append(o)
            continue
        select_only([o])
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.separate(type='MATERIAL')
        bpy.ops.object.mode_set(mode='OBJECT')
        out += [x for x in bpy.context.selected_objects]
    for o in out:                                          # drop empty slots so each part has one material
        used = {p.material_index for p in o.data.polygons}
        if len(used) == 1 and len(o.material_slots) > 1:
            keep = o.material_slots[list(used)[0]].material
            o.data.materials.clear()
            o.data.materials.append(keep)
    return out


def main_material(o):
    for s in o.material_slots:
        if s.material:
            return s.material
    return None


def area3d(objs):
    a = 0.0
    for o in objs:
        for p in o.data.polygons:
            a += p.area
    return a


def unwrap_group(objs, uv_name='PSX', margin=0.003, angle=60, zone_scale=None, custom=None):
    """new unique UV map on all objects of one atlas, packed together; the old map stays the render map so the
    source textures keep sampling through it while the bake writes through the new one.  zone_scale {zone: f}: the
    faces of those zones (whole islands, e.g. an underside nobody sees) get f times the texel density of the rest.
    custom(objs, uv_name): called in object mode after the smart projection, before the packing (own unwraps)"""
    for o in objs:
        me = o.data
        old = me.uv_layers.active
        for u in me.uv_layers:
            u.active_render = (u == old) if old else False
        uv = me.uv_layers.get(uv_name) or me.uv_layers.new(name=uv_name)
        me.uv_layers.active = uv
        if old:
            old.active_render = True
    select_only(objs)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.reveal()
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=margin, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    if zone_scale:
        bpy.ops.object.mode_set(mode='OBJECT')
        for o in objs:
            za = o.data.attributes.get('zone')
            if za is None:
                continue
            uvl = o.data.uv_layers[uv_name].data
            for p in o.data.polygons:
                f = zone_scale.get(int(round(za.data[p.index].value)))
                if f:
                    for li in p.loop_indices:
                        uvl[li].uv = uvl[li].uv * f
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
    if custom is not None:                       # custom(objs, uv_name): re-unwrap some faces (same metric scale) before packing
        bpy.ops.object.mode_set(mode='OBJECT')
        custom(objs, uv_name)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    try:
        bpy.ops.uv.pack_islands(rotate=True, rotate_method='ANY', scale=True, margin_method='FRACTION',
                                margin=margin, shape_method='CONCAVE')
    except TypeError:
        bpy.ops.uv.pack_islands(rotate=True, margin=margin)
    bpy.ops.object.mode_set(mode='OBJECT')


def uv_fill(objs, uv_name='PSX'):
    a = 0.0
    for o in objs:
        uv = o.data.uv_layers[uv_name].data
        for p in o.data.polygons:
            pts = [uv[i].uv for i in p.loop_indices]
            s = 0.0
            for i in range(len(pts)):
                x0, y0 = pts[i]
                x1, y1 = pts[(i + 1) % len(pts)]
                s += x0 * y1 - x1 * y0
            a += abs(s) / 2
    return a


def coverage_mask(objs, size, uv_name='PSX'):
    from PIL import Image, ImageDraw
    im = Image.new('L', (size, size), 0)
    d = ImageDraw.Draw(im)
    for o in objs:
        uv = o.data.uv_layers[uv_name].data
        for p in o.data.polygons:
            pts = [(uv[i].uv[0] * size, (1 - uv[i].uv[1]) * size) for i in p.loop_indices]
            d.polygon(pts, fill=255, outline=255)
    return np.asarray(im) > 127


def bake_material_copy(src, img, mode):
    """copy of a source material whose output is an emission of what we want to bake ('color' = the base colour
    as the source shows it, incl. its vertex colours / factors); the target image node is active"""
    m = src.copy() if src else bpy.data.materials.new('blank')
    m.name = (src.name if src else 'blank') + '__bake_' + mode
    m.use_nodes = True
    nt = m.node_tree
    out = next((n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL' and n.is_active_output), None) or \
        nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    pb = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if mode == 'color':
        if pb and pb.inputs['Base Color'].is_linked:
            nt.links.new(pb.inputs['Base Color'].links[0].from_socket, em.inputs['Color'])
        elif pb:
            em.inputs['Color'].default_value = pb.inputs['Base Color'].default_value
        else:
            em.inputs['Color'].default_value = (0.5, 0.5, 0.5, 1)
    for l in list(out.inputs['Surface'].links):
        nt.links.remove(l)
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    if 'Displacement' in out.inputs:
        for l in list(out.inputs['Displacement'].links):
            nt.links.remove(l)
    ti = nt.nodes.new('ShaderNodeTexImage')
    ti.image = img
    nt.nodes.active = ti
    ti.select = True
    return m


def bake(objs, img, kind, samples=8, margin=4, ao_dist=0.35):
    """kind 'color' -> EMIT bake of the source base colour; 'ao' -> geometric AO"""
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = samples
    sc.render.bake.margin = margin
    sc.render.bake.use_clear = True
    keep = {}
    for o in objs:
        keep[o.name] = [s.material for s in o.material_slots]
        for s in o.material_slots:
            s.material = bake_material_copy(s.material, img, 'color' if kind == 'color' else 'ao')
        if not o.material_slots:
            o.data.materials.append(bake_material_copy(None, img, 'color'))
    if kind == 'ao':
        w = sc.world or bpy.data.worlds.new('W')
        sc.world = w
        w.light_settings.distance = ao_dist
        bpy.ops.object.select_all(action='DESELECT')
        select_only(objs)
        bpy.ops.object.bake(type='AO')
    else:
        select_only(objs)
        bpy.ops.object.bake(type='EMIT')
    for o in objs:                                          # restore the source materials
        tmp = [s.material for s in o.material_slots]
        for s, m in zip(o.material_slots, keep[o.name]):
            s.material = m
        for m in tmp:
            if m and m.name.endswith(('__bake_color', '__bake_ao')) and m.users == 0:
                bpy.data.materials.remove(m)
    a = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4)[::-1]
    return a


def s2l(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def l2s(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def blocks(a, f):
    H, W = a.shape[:2]
    rest = a.shape[2:]
    b = a.reshape(H // f, f, W // f, f, *rest).swapaxes(1, 2)
    return b.reshape(H // f, W // f, f * f, *rest)


def kmeans(x, k, iters=18, seed=0):
    rng = np.random.default_rng(seed)
    k = min(k, len(x))
    c = x[rng.choice(len(x), k, replace=False)].copy()
    for _ in range(iters):
        lab = ((x[:, None, :] - c[None]) ** 2).sum(-1).argmin(1)
        for j in range(k):
            m = lab == j
            c[j] = x[m].mean(0) if m.any() else x[rng.integers(len(x))]
    return c


def psx_quantize(lin_col, ao, cov_hi, f, ncol=64, dither=1.0, ao_min=0.25, line_gain=2.6, line_max=0.55):
    """hi-res linear colour + AO + coverage -> PSX texture (sRGB uint8): block mean, AO in the colour, grooves as
    1-texel dark lines, k-means palette, Bayer 4x4 of one 5-bit step, padding from the nearest covered texel"""
    from scipy import ndimage
    shaded = lin_col * (ao_min + (1 - ao_min) * ao)[..., None]
    col = blocks(shaded, f).mean(2)
    gb = blocks(ao, f)
    line = np.clip((gb.mean(2) - np.percentile(gb, 12, axis=2)) * line_gain, 0, line_max)
    col = col * (1 - line)[..., None]
    cov = blocks(cov_hi.astype(np.float32), f).mean(2) > 0.25
    s = l2s(col).reshape(-1, 3)
    samp = s[cov.reshape(-1)]
    if len(samp) == 0:
        samp = s
    samp = samp[np.random.default_rng(1).choice(len(samp), min(len(samp), 60000), replace=False)]
    pal = kmeans(samp, ncol)
    n = col.shape[0]
    bay = np.tile(BAYER4, (n // 4 + 1, col.shape[1] // 4 + 1))[:n, :col.shape[1]].reshape(-1, 1)
    sd = np.clip(s + (bay - 0.5) * (1 / 24) * dither, 0, 1)
    idx = np.empty(len(sd), np.int32)
    for i in range(0, len(sd), 65536):
        idx[i:i + 65536] = ((sd[i:i + 65536, None, :] - pal[None]) ** 2).sum(-1).argmin(1)
    q = pal[idx].reshape(col.shape)
    if not cov.all() and cov.any():
        ii = ndimage.distance_transform_edt(~cov, return_distances=False, return_indices=True)
        q = q[ii[0], ii[1]]
    return (q * 255 + 0.5).astype(np.uint8), cov


def psx_material(name, img_path, rough=0.8, metal=0.0, spec=0.25):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    pb = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    ti = nt.nodes.new('ShaderNodeTexImage')
    ti.image = bpy.data.images.load(img_path, check_existing=True)
    ti.interpolation = 'Closest'
    uvn = nt.nodes.new('ShaderNodeUVMap')
    uvn.uv_map = 'PSX'
    nt.links.new(uvn.outputs[0], ti.inputs[0])
    nt.links.new(ti.outputs[0], pb.inputs['Base Color'])
    pb.inputs['Roughness'].default_value = rough
    pb.inputs['Metallic'].default_value = metal
    if 'Specular IOR Level' in pb.inputs:
        pb.inputs['Specular IOR Level'].default_value = spec
    return m


def psx_atlas(name, objs, tex_dir, ppm=50.0, max_size=1024, min_size=64, oversample=4, ao_samples=48,
              ao_dist=0.35, ncol=64, rough=0.8, metal=0.0, size=None, log=print):
    """one PSX atlas for a group of objects; returns (material, info)"""
    os.makedirs(tex_dir, exist_ok=True)
    unwrap_group(objs)
    A = area3d(objs)
    fill = max(uv_fill(objs), 1e-6)
    if size is None:
        want = math.sqrt(A / fill) * ppm                   # px per side for the wanted density
        size = int(2 ** round(math.log2(max(want, 1))))
        size = max(min_size, min(max_size, size))
    hi = size * oversample
    img = bpy.data.images.new(name + '_hi', hi, hi, alpha=True, float_buffer=True)
    col = bake(objs, img, 'color', samples=4)[..., :3]
    img_ao = bpy.data.images.new(name + '_ao', hi, hi, alpha=True, float_buffer=True)
    ao = bake(objs, img_ao, 'ao', samples=ao_samples, ao_dist=ao_dist)[..., 0]
    cov = coverage_mask(objs, hi)
    q, cov_lo = psx_quantize(np.clip(col, 0, 1), np.clip(ao, 0, 1), cov, oversample, ncol=ncol)
    from PIL import Image
    p = os.path.join(tex_dir, f'T_{name}_BaseColor.png')
    Image.fromarray(q, 'RGB').save(p)
    np.save(os.path.join(tex_dir, f'{name}_ao_hi.npy'), ao.astype(np.float16))
    bpy.data.images.remove(img)
    bpy.data.images.remove(img_ao)
    m = psx_material('M_' + name, p, rough, metal)
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(m)
    real_ppm = size * math.sqrt(fill / A) if A > 0 else 0
    info = dict(name=name, objects=len(objs), area_m2=round(A, 2), uv_fill=round(fill, 3), size=size,
                ppm=round(real_ppm, 1), colours=int(len(np.unique(q.reshape(-1, 3), axis=0))))
    log('ATLAS', info)
    return m, info


# ------------------------------------------------------------------ preview scene (plane-preview style)
def world(color=(0.56, 0.62, 0.72), strength=0.9):
    w = bpy.context.scene.world or bpy.data.worlds.new('World')
    bpy.context.scene.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes.get('Background')
    bg.inputs[0].default_value = (*color, 1)
    bg.inputs[1].default_value = strength
    return w


def sun(strength=3.4, rot_deg=(52, 0, 30), color=(1, 0.97, 0.92), name='Sun'):
    ld = bpy.data.lights.new(name, 'SUN')
    ld.energy = strength
    ld.color = color
    ld.angle = math.radians(2.0)
    ob = bpy.data.objects.new(name, ld)
    bpy.context.scene.collection.objects.link(ob)
    ob.rotation_euler = [math.radians(a) for a in rot_deg]
    return ob


def ground(img_path=None, size=150, color=(0.82, 0.84, 0.88)):
    bpy.ops.mesh.primitive_plane_add(size=size)
    g = bpy.context.object
    g.name = 'Ground'
    m = bpy.data.materials.new('ground')
    m.use_nodes = True
    pb = m.node_tree.nodes['Principled BSDF']
    pb.inputs['Base Color'].default_value = (*color, 1)
    pb.inputs['Roughness'].default_value = 0.95
    g.data.materials.append(m)
    return g


def setup_cycles(res=(900, 600), spp=48):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = spp
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
    sc.render.film_transparent = False


def render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


# ------------------------------------------------------------------ mode A: keep the source UVs (Substance models)
def faces_by_material(objs):
    """{material: [(object, polygon), ...]}"""
    d = {}
    for o in objs:
        for p in o.data.polygons:
            if p.material_index < len(o.material_slots):
                m = o.material_slots[p.material_index].material
                d.setdefault(m, []).append((o, p))
    return d


def uv_layer_for(o, node):
    """UV map a texture node samples (explicit UV Map node, else the render-active map)"""
    if node and node.inputs['Vector'].is_linked:
        n = node.inputs['Vector'].links[0].from_node
        if n.type == 'UVMAP' and n.uv_map in o.data.uv_layers:
            return o.data.uv_layers[n.uv_map]
    for u in o.data.uv_layers:
        if u.active_render:
            return u
    return o.data.uv_layers.active


def density_and_overlap(faces, node, res=512):
    """texel density (px/m at the image's size) and the share of UV area covered twice"""
    from PIL import Image, ImageDraw
    W, H = node.image.size
    A3 = Auv = 0.0
    acc = np.zeros((res, res), np.uint16)
    for o, p in faces:
        uvl = uv_layer_for(o, node)
        if uvl is None:
            continue
        pts = [uvl.data[i].uv for i in p.loop_indices]
        s = 0.0
        for i in range(len(pts)):
            x0, y0 = pts[i]
            x1, y1 = pts[(i + 1) % len(pts)]
            s += x0 * y1 - x1 * y0
        Auv += abs(s) / 2
        A3 += p.area
        im = Image.new('L', (res, res), 0)
        ImageDraw.Draw(im).polygon([((u % 1.0) * res, (1 - (v % 1.0)) * res) for u, v in pts], fill=1)
        acc += np.asarray(im, np.uint16)
    ppm = math.sqrt(Auv * W * H / A3) if A3 > 0 else 0
    covered = (acc > 0).sum()
    over = (acc > 1).sum() / max(covered, 1)
    return ppm, over, A3


def find_tex(m, socket_name):
    pb = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if pb is None or socket_name not in pb.inputs:
        return None
    return socket_image(pb.inputs[socket_name])


def find_occlusion(m):
    for n in m.node_tree.nodes:
        if n.type == 'GROUP' and n.node_tree and 'glTF' in n.node_tree.name and 'Occlusion' in n.inputs:
            t = socket_image(n.inputs['Occlusion'])
            if t:
                return t
    return None


def img_array(img, size=None):
    """Blender image -> float array (H, W, C) top row first, optionally box-resized"""
    a = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], img.channels)[::-1]
    if size and size != img.size[0]:
        f = img.size[0] // size
        if f >= 1 and img.size[0] % size == 0 and img.size[1] % size == 0:
            a = blocks(a, f).mean(2)
    return a


def psx_from_source(m, faces, tex_dir, ppm_target=50.0, ncol=64, min_size=64, max_size=1024, bake_objs=None,
                    log=print):
    """downsample one source texture set in its own UVs to the PSX density; AO from the source occlusion map
    (or a Cycles AO bake into the same UVs), grooves as 1-texel lines, palette, Bayer; rewires the material"""
    from PIL import Image
    bc = find_tex(m, 'Base Color')
    if bc is None or bc.image is None:
        return None
    ppm, over, A3 = density_and_overlap(faces, bc)
    W = bc.image.size[0]
    size = int(2 ** round(math.log2(max(W * ppm_target / max(ppm, 1e-6), 1))))
    size = max(min_size, min(max_size, size, W))
    f = W // size
    hi = size * min(f, 4)                                   # working resolution: 4 samples per output texel
    col = img_array(bc.image)
    col = col[..., :3]
    if bc.image.colorspace_settings.name == 'sRGB' and not bc.image.is_float:
        col = s2l(col)                                      # Blender pixels are display-referred for 8-bit sRGB
    if hi != W:
        col = blocks(col, W // hi).mean(2)
    occ = find_occlusion(m)
    if occ is not None and occ.image is not None:
        ao = img_array(occ.image)[..., 0]
        if ao.shape[0] != hi:
            ao = blocks(ao, ao.shape[0] // hi).mean(2) if ao.shape[0] > hi else np.kron(ao, np.ones((hi // ao.shape[0],) * 2))
        src_ao = 'map'
    elif bake_objs and over < 0.03:
        img_ao = bpy.data.images.new(m.name + '_aobake', hi, hi, alpha=True, float_buffer=True)
        for o in bake_objs:                                 # bake through the source UVs
            uvl = uv_layer_for(o, bc)
            if uvl is not None:
                o.data.uv_layers.active = uvl
        ao = bake(bake_objs, img_ao, 'ao', samples=32)[..., 0]
        bpy.data.images.remove(img_ao)
        src_ao = 'baked'
    else:
        ao = np.ones((hi, hi), np.float32)
        src_ao = 'none'
    q, _ = psx_quantize(np.clip(col, 0, 1), np.clip(ao, 0, 1), np.ones((hi, hi), bool), hi // size, ncol=ncol)
    os.makedirs(tex_dir, exist_ok=True)
    safe = ''.join(ch if ch.isalnum() else '_' for ch in m.name)
    p = os.path.join(tex_dir, f'T_{safe}_BaseColor.png')
    Image.fromarray(q, 'RGB').save(p)
    # rewire: point-filtered PSX image, no normal map, flat roughness, no metal
    nt = m.node_tree
    pb = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    newimg = bpy.data.images.load(p, check_existing=True)
    bc.image = newimg
    bc.interpolation = 'Closest'
    for key in ('Normal', 'Roughness', 'Metallic'):
        for l in list(pb.inputs[key].links):
            nt.links.remove(l)
    pb.inputs['Roughness'].default_value = 0.8
    pb.inputs['Metallic'].default_value = 0.0
    flatten_bsdf(m)
    info = dict(material=m.name, src=f'{W}px', ppm_src=round(ppm, 1), size=size, ppm=round(ppm * size / W, 1),
                overlap=round(over, 3), area_m2=round(A3, 2), ao=src_ao, colours=int(len(np.unique(q.reshape(-1, 3), axis=0))))
    log('PSX', info)
    return info


def flatten_bsdf(m, spec=0.3):
    """no clear coat, sheen, transmission or anisotropy: PS1 surfaces are plain diffuse with a weak highlight"""
    nt = m.node_tree
    pb = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if pb is None:
        return
    for key, val in (('Coat Weight', 0.0), ('Sheen Weight', 0.0), ('Transmission Weight', 0.0),
                     ('Anisotropic', 0.0), ('Subsurface Weight', 0.0), ('Specular IOR Level', spec)):
        if key in pb.inputs:
            for l in list(pb.inputs[key].links):
                nt.links.remove(l)
            pb.inputs[key].default_value = val


def psx_glass(name='M_PSX_Glass', tint=(0.16, 0.20, 0.22), alpha=0.42, glow=0.0, glow_col=(1.0, 0.62, 0.30)):
    """PS1-style glass: flat tinted, semi-transparent, a hint of reflection; optional warm glow for lit rooms"""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    pb = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    pb.inputs['Base Color'].default_value = (*tint, 1)
    pb.inputs['Roughness'].default_value = 0.5
    pb.inputs['Alpha'].default_value = alpha
    if 'Specular IOR Level' in pb.inputs:
        pb.inputs['Specular IOR Level'].default_value = 0.12
    pb.inputs['Emission Color'].default_value = (*glow_col, 1)
    pb.inputs['Emission Strength'].default_value = glow
    try:
        m.surface_render_method = 'BLENDED'
    except Exception:
        pass
    return m

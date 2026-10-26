"""Shared PSX atlas bake (the van v3 recipe, made reusable for new assets):
geometry carries a float face attribute 'zone'; make_atlas() unwraps a group of objects into one atlas, bakes world
position / normal / zone / AO per texel with Cycles, hands them to a numpy painter, then does the PSX step (AO in the
colour, 1-texel grooves and contact lines, 64-colour k-means palette, Bayer 4x4 dither, point filter).
painter(Pp, Nn, Z, ao, aa) -> (rgb_linear[n, 3], groove[n] or None); aa = texel size in metres."""
import os, math
import bpy
import numpy as np
import psxlib as P

POS_SCALE = 8.0                     # positions are baked as pos / POS_SCALE + 0.5: objects must sit within +-4 m


def lin(*rgb):
    c = np.array(rgb, np.float32) / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4).astype(np.float32)


def boards(s, pitch, aa, seam_w=0.006):
    """board index and a seam mask for coordinate s"""
    from noise import ss
    w = s / pitch
    i = np.floor(w)
    d = np.abs(w - np.round(w)) * pitch
    seam = 1.0 - ss(seam_w - aa, seam_w + aa, d)
    return i, seam


def hash1(i, seed=0):
    x = np.sin(i * 12.9898 + seed * 78.233) * 43758.5453
    return x - np.floor(x)


def data_material(kind, img):
    m = bpy.data.materials.new('bake_' + kind)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    if kind in ('pos', 'nor'):
        geo = nt.nodes.new('ShaderNodeNewGeometry')
        mp = nt.nodes.new('ShaderNodeVectorMath')
        mp.operation = 'MULTIPLY_ADD'
        s = 1 / POS_SCALE if kind == 'pos' else 0.5
        mp.inputs[1].default_value = (s, s, s)
        mp.inputs[2].default_value = (0.5, 0.5, 0.5)
        nt.links.new(geo.outputs['Position' if kind == 'pos' else 'Normal'], mp.inputs[0])
        nt.links.new(mp.outputs[0], em.inputs['Color'])
    else:
        at = nt.nodes.new('ShaderNodeAttribute')
        at.attribute_type = 'GEOMETRY'
        at.attribute_name = 'zone'
        dv = nt.nodes.new('ShaderNodeMath')
        dv.operation = 'DIVIDE'
        dv.inputs[1].default_value = 32.0
        nt.links.new(at.outputs['Fac'], dv.inputs[0])
        nt.links.new(dv.outputs[0], em.inputs['Color'])
    ti = nt.nodes.new('ShaderNodeTexImage')
    ti.image = img
    nt.nodes.active = ti
    ti.select = True
    return m


def bake_pass(objs, kind, size, samples=1, ao_dist=0.35):
    img = bpy.data.images.new(f'{kind}_{size}', size, size, alpha=True, float_buffer=True)
    img.generated_color = (0, 0, 0, 0)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = samples
    sc.render.bake.margin = 0
    sc.render.bake.use_clear = True
    for o in objs:
        if not o.material_slots:
            o.data.materials.append(None)
    keep = {o.name: [s.material for s in o.material_slots] for o in objs}
    m = data_material(kind if kind != 'ao' else 'zone', img)
    for o in objs:
        for s in o.material_slots:
            s.material = m
    P.select_only(objs)
    if kind == 'ao':
        w = sc.world or bpy.data.worlds.new('W')
        sc.world = w
        w.light_settings.distance = ao_dist
        bpy.ops.object.bake(type='AO')
    else:
        bpy.ops.object.bake(type='EMIT')
    for o in objs:
        for s, mm in zip(o.material_slots, keep[o.name]):
            s.material = mm
    a = np.array(img.pixels[:], np.float32).reshape(size, size, 4)[::-1].copy()
    bpy.data.images.remove(img)
    bpy.data.materials.remove(m)
    return a


def psx_step(col_hi, ao_hi, cov_hi, groove_hi, f, ncol=64, ao_min=0.25, dither=1.0, prio_hi=None):
    """prio_hi: texels whose colours must get their own palette entries (small vivid parts: red knobs, blue drums)"""
    from scipy import ndimage
    w = cov_hi.astype(np.float32)
    shaded = col_hi * (ao_min + (1 - ao_min) * ao_hi)[..., None] * (1 - 0.55 * groove_hi)[..., None]
    num = P.blocks(shaded * w[..., None], f).sum(2)
    den = P.blocks(w, f).sum(2)
    col = num / np.maximum(den, 1e-6)[..., None]
    aoc = P.blocks(np.where(cov_hi, ao_hi, 0.0), f)                    # AO spread over the covered sub-samples only
    wc = P.blocks(cov_hi.astype(np.float32), f)
    amin = P.blocks(np.where(cov_hi, ao_hi, np.inf), f).min(2)
    amean = aoc.sum(2) / np.maximum(wc.sum(2), 1e-6)
    line = np.where(wc.sum(2) > 0, np.clip((amean - amin) * 2.4, 0, 0.5), 0.0)   # no dark rims on island borders
    col = col * (1 - line)[..., None]
    cov = den > 0.25 * f * f
    s = P.l2s(col).reshape(-1, 3)
    samp = s[cov.reshape(-1)]
    rng = np.random.default_rng(1)
    samp = samp[rng.choice(len(samp), min(len(samp), 60000), replace=False)]
    if prio_hi is not None:
        pr = (P.blocks(prio_hi.astype(np.float32), f).mean(2) > 0.5) & cov
        ps = s[pr.reshape(-1)]
        if len(ps):
            ps = ps[rng.choice(len(ps), min(len(ps), 6000), replace=False)]
            samp = np.concatenate([samp] + [ps] * 3)                    # weighted: vivid parts claim clusters
    pal = P.kmeans(samp, ncol)
    n = col.shape[0]
    bay = np.tile(P.BAYER4, (n // 4 + 1, n // 4 + 1))[:n, :n].reshape(-1, 1)
    sd = np.clip(s + (bay - 0.5) * (1 / 24) * dither, 0, 1)
    idx = np.empty(len(sd), np.int32)
    for i in range(0, len(sd), 65536):
        idx[i:i + 65536] = ((sd[i:i + 65536, None, :] - pal[None]) ** 2).sum(-1).argmin(1)
    q = pal[idx].reshape(col.shape)
    if not cov.all():
        ii = ndimage.distance_transform_edt(~cov, return_distances=False, return_indices=True)
        q = q[ii[0], ii[1]]
    return (q * 255 + 0.5).astype(np.uint8), cov


def prepare_atlas(objs, size=None, ppm=50.0, oversample=2, angle=62, zone_scale=None, ppm_cap=None, margin_px=None, custom_uv=None):
    """unwrap a group and bake its world position / normal / zone where the objects stand now; finish_atlas() bakes
    the AO and paints later, after the objects may have been moved (painters then still see these positions).
    ppm_cap: the project's pixel size - if the atlas would come out finer, the islands are shrunk to that density and
    the rest of the atlas stays empty"""
    margin = max(0.002, (2.2 if ppm_cap else 1.5) / (size or 512))        # ~2 texels apart (also after a ppm_cap shrink)
    if margin_px is not None:                                           # many thin islands (rails, wires): a tighter margin
        margin = margin_px / (size or 512)
    P.unwrap_group(objs, margin=margin, angle=angle, zone_scale=zone_scale, custom=custom_uv)
    A = P.area3d(objs)
    fill = max(P.uv_fill(objs), 1e-6)
    if size is None:
        want = math.sqrt(A / fill) * ppm
        size = int(2 ** math.ceil(math.log2(max(want * 0.8, 1))))
        size = max(256, min(1024, size))
    if ppm_cap and size * math.sqrt(fill / A) > ppm_cap:
        k = ppm_cap / (size * math.sqrt(fill / A))
        for o in objs:
            for l in o.data.uv_layers['PSX'].data:
                l.uv = l.uv * k
        fill = max(P.uv_fill(objs), 1e-6)
    hi = size * oversample
    pos, nor, zon = bake_pass(objs, 'pos', hi), bake_pass(objs, 'nor', hi), bake_pass(objs, 'zone', hi)
    cov = P.coverage_mask(objs, hi)
    # paint only the sub-samples the bake reached: PIL's outline overshoots oblique island edges by up to a sub-sample, and
    # those carry no position / normal / AO (black, AO 0) - dark teeth along slanted borders (the tanks' dished heads).
    # A baked normal is a unit vector; an unbaked texel decodes to (-1, -1, -1).
    baked = np.abs(np.linalg.norm(nor[..., :3] * 2 - 1, axis=-1) - 1) < 0.1
    cov &= baked
    return dict(size=size, hi=hi, oversample=oversample, A=A, fill=fill, pos=pos, nor=nor, zon=zon, cov=cov)


def finish_atlas(name, objs, prep, painter, tex_dir, prefix='T_Gen_', mat_prefix='M_Gen_', ao_min=0.25, ao_dist=0.35,
                 rough=0.8, ncol=64, log=print, info=None, prio_zones=()):
    """AO bake (with the scene as it stands now) + paint + PSX quantize; assigns a point-filtered material"""
    from PIL import Image
    size, hi, oversample, A, fill = prep['size'], prep['hi'], prep['oversample'], prep['A'], prep['fill']
    pos, nor, zon, cov = prep['pos'], prep['nor'], prep['zon'], prep['cov']
    ao = bake_pass(objs, 'ao', hi, samples=40, ao_dist=ao_dist)[..., 0]
    Pp = (pos[..., :3] - 0.5) * POS_SCALE
    Nn = nor[..., :3] * 2 - 1
    Nn /= np.maximum(np.linalg.norm(Nn, axis=-1, keepdims=True), 1e-6)
    Zz = np.round(zon[..., 0] * 32).astype(np.int32)
    ci = cov.reshape(-1)
    aa = 1.0 / (size * math.sqrt(fill / A)) / oversample
    out, gr = painter(Pp.reshape(-1, 3)[ci].astype(np.float32), Nn.reshape(-1, 3)[ci].astype(np.float32),
                      Zz.reshape(-1)[ci], ao.reshape(-1)[ci], aa)
    col = np.zeros((hi * hi, 3), np.float32)
    col[ci] = out
    groove = np.zeros(hi * hi, np.float32)
    if gr is not None:
        groove[ci] = gr
    prio = np.isin(Zz, list(prio_zones)) & cov if prio_zones else None
    q, _ = psx_step(col.reshape(hi, hi, 3), np.clip(ao, 0, 1), cov, groove.reshape(hi, hi), oversample, ncol=ncol,
                    ao_min=ao_min, prio_hi=prio)
    p = os.path.join(tex_dir, f'{prefix}{name}_BaseColor.png')
    Image.fromarray(q, 'RGB').save(p)
    m = P.psx_material(mat_prefix + name, p, rough=rough)
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(m)
    ppm_real = size * math.sqrt(fill / A)
    rec = dict(size=size, ppm=round(ppm_real, 1), area_m2=round(A, 2), uv_fill=round(fill, 3),
               colours=int(len(np.unique(q.reshape(-1, 3), axis=0))))
    if info is not None:
        info[name] = rec
    log('ATLAS', name, rec)
    return m


def make_atlas(name, objs, painter, tex_dir, prefix='T_Gen_', mat_prefix='M_Gen_', size=None, ppm=50.0, oversample=2,
               ao_min=0.25, ao_dist=0.35, rough=0.8, ncol=64, log=print, info=None, prio_zones=(), zone_scale=None,
               ppm_cap=None, margin_px=None):
    """unwrap + bake + paint + PSX quantize one atlas; assigns a point-filtered material to the objects"""
    prep = prepare_atlas(objs, size=size, ppm=ppm, oversample=oversample, zone_scale=zone_scale, ppm_cap=ppm_cap,
                         margin_px=margin_px)
    return finish_atlas(name, objs, prep, painter, tex_dir, prefix=prefix, mat_prefix=mat_prefix, ao_min=ao_min,
                        ao_dist=ao_dist, rough=rough, ncol=ncol, log=log, info=info, prio_zones=prio_zones)


def tex_mat(name, base, emi, night, rough=0.35, uv_map='PSX'):
    """point-filtered base + emission material (gauges, labels, lamp lenses); night_emit = emission strength at night"""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    pb = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    uvn = nt.nodes.new('ShaderNodeUVMap')
    uvn.uv_map = uv_map
    for img, sock in ((base, 'Base Color'), (emi, 'Emission Color')):
        ti = nt.nodes.new('ShaderNodeTexImage')
        ti.image = bpy.data.images.load(img, check_existing=True)
        ti.interpolation = 'Closest'
        nt.links.new(uvn.outputs[0], ti.inputs[0])
        nt.links.new(ti.outputs[0], pb.inputs[sock])
    pb.inputs['Roughness'].default_value = rough
    pb.inputs['Emission Strength'].default_value = 0.0
    if 'Specular IOR Level' in pb.inputs:
        pb.inputs['Specular IOR Level'].default_value = 0.35
    m['night_emit'] = night
    return m

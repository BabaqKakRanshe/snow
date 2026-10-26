"""forest_start build (start_kit): one object per part with its pivot as the origin, each root in its own collection and
standing at start_kit.DISPLAY in the file (the contract gives everything in the root's own frame); PSX atlases baked with
psxbake + start_paint: Start (the booth and the car, 1024, at the generator shed's pixel density) and StartProps (the light,
the battery, the tyres, the rubbish, 64 px/m); the panel atlas (start_tex) on the sign and the lenses; lenses lit / off,
glow cards, glass; the lamps (start_lights.json), the interaction contract (parts, sockets, colliders, meta).
python3 -I start_build.py [--draft] -> out/start.blend, out/start.json, out/start_contract.json, out/start_lights.json, tex/start/*
--draft: no bakes, flat zone colours -> out/start_draft.blend"""
import sys, os, math, json, time
sys.path.insert(0, '/home/claude/uaz')
import bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix, Euler
import psxlib as P
import psxbake as PB
import start_tex as ST
import gen_kit as K
import start_kit as SK

DRAFT = '--draft' in sys.argv
HERE = P.HERE
TEX = os.path.join(HERE, 'tex', 'start')
OUT = os.path.join(HERE, 'out')
os.makedirs(TEX, exist_ok=True)
os.makedirs(OUT, exist_ok=True)
T0 = time.time()
INFO = {}
SHED_PPM = 51.3                                       # the generator shed's building atlas (px / m): the project's pixel
PB.POS_SCALE = 40.0                                   # bake range +-20 m: the roots stand from x -8.3 to 9.8


def log(*a):
    print(f'[{time.time() - T0:6.1f}s]', *a, flush=True)


def r4(v):
    return [round(float(a), 4) + 0.0 for a in v]


def _nz(o):
    if isinstance(o, float):
        return o + 0.0
    if isinstance(o, dict):
        return {k: _nz(v) for k, v in o.items()}
    if isinstance(o, (list, tuple)):
        return [_nz(v) for v in o]
    return o


def to_unity(v):
    """prefab frame -> Unity (prefab-local): (x, y, z) -> (-x, z, -y)"""
    return [round(-float(v[0]), 4) + 0.0, round(float(v[2]), 4) + 0.0, round(-float(v[1]), 4) + 0.0]


LIGHT_TYPES = {
    'bulkhead': dict(unity='Point', I=1.0, rng=8.0, cct=2700, color=(1.0, 0.72, 0.42), group='post', spot=None,
                     desc='the caged bulkhead over the booth door (the shed\'s), 60 W incandescent; group post: the game switches it '
                          '(on / off / flicker)'),
    'flood': dict(unity='Spot', I=6.0, rng=18.0, cct=3200, color=(1.0, 0.86, 0.66), group='battery', spot=100.0,
                  desc='the tripod work light, 500 W halogen on its battery; group battery: the game blinks it and lets it go out'),
}

bpy.ops.wm.read_factory_settings(use_empty=True)
for c in list(bpy.data.collections):
    bpy.data.collections.remove(c)
PB_BASE, PB_EMI = ST.build(TEX)

# ------------------------------------------------------------------ 1. geometry
SK.build_start()
C = K.CONTRACT
missing = [n for n in K.KBS if n not in C['parts']]
assert not missing, f'objects without a contract entry: {missing}'
colls = {}
for root in SK.ROOTS:
    colls[root] = bpy.data.collections.new(root)
    bpy.context.scene.collection.children.link(colls[root])
objs = {}
for name, k in K.KBS.items():
    root = SK.ROOT_OF[name]
    off = SK.DISPLAY[root]
    if name == 'StartBooth':
        bmesh.ops.remove_doubles(k.bm, verts=list(k.bm.verts), dist=1e-4)
    K.transform_kb(k, Matrix.Translation(off))
    p = C['parts'][name]
    o = k.to_object(name, coll=colls[root], pivot=Vector(p['pivot']) + off)
    o['root'] = root
    objs[name] = o
log('objects', len(objs))
bpy.context.view_layer.update()
groups = {}
for name, o in objs.items():
    groups.setdefault(K.ATLAS[name], []).append(o)

# ------------------------------------------------------------------ 2. atlases (or flat zone colours for the draft)
INFO['atlases'] = {}
import start_paint as SPT
if DRAFT:
    cache = {}

    def zmat(z):
        if z not in cache:
            m = bpy.data.materials.new(f'draft_{z}')
            m.use_nodes = True
            pb = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
            c = PB.lin(*SPT.DRAFT_COL.get(z, (128, 128, 128)))
            pb.inputs['Base Color'].default_value = (float(c[0]), float(c[1]), float(c[2]), 1)
            pb.inputs['Roughness'].default_value = 0.75
            m.diffuse_color = (float(c[0]), float(c[1]), float(c[2]), 1)
            cache[z] = m
        return cache[z]

    for name, o in objs.items():
        if K.ATLAS[name] not in ('Start', 'StartProps'):
            continue
        me = o.data
        zl = [round(a.value) for a in me.attributes['zone'].data]
        me.materials.clear()
        idx = {}
        for z in sorted(set(zl)):
            idx[z] = len(me.materials)
            me.materials.append(zmat(z))
        for poly, z in zip(me.polygons, zl):
            poly.material_index = idx[z]
else:
    PB.make_atlas('Start', groups['Start'], SPT.paint_start, TEX, prefix='T_Start_', mat_prefix='M_Start_', size=1024, ao_min=0.35,
                  ao_dist=0.40, rough=0.85, log=log, info=INFO['atlases'], ppm_cap=SHED_PPM,
                  zone_scale={K.Z_ROOF: 0.75, SK.Z_PADC: 0.75, SK.Z_UNDER: 0.6, SK.Z_LINER: 0.8, SK.Z_CARPET: 0.8},
                  prio_zones=(K.Z_TRIMW, SK.Z_TAIL, SK.Z_HEAD, SK.Z_CHROME))
    PB.make_atlas('StartProps', groups['StartProps'], SPT.paint_props, TEX, prefix='T_Start_', mat_prefix='M_Start_', size=None,
                  ppm=64.0, ao_min=0.40, ao_dist=0.20, rough=0.8, log=log, info=INFO['atlases'], ppm_cap=64.0,
                  prio_zones=(SK.Z_TRIPOD, SK.Z_TERM, SK.Z_CAN, SK.Z_BATT))

# ------------------------------------------------------------------ 3. explicit materials
m_panel = PB.tex_mat('M_Start_Panel', PB_BASE, PB_EMI, 0.0, rough=0.6)
lens_mats = {}
for kname, night in (('booth', 3.0), ('flood', 4.0)):
    for sfx, ne in (('', night), ('_Off', 0.0)):
        m = PB.tex_mat('M_Start_Lamp_' + kname.capitalize() + sfx, PB_BASE, PB_EMI, ne, rough=0.2)
        m['lamp_kind'] = kname
        m.use_fake_user = bool(sfx)                    # the 'off' variants are on no mesh: keep them in the file
        lens_mats[kname + sfx] = m
m_glow = bpy.data.materials.new('M_Start_Glow')               # additive halo: emission from the panel's glow tiles
m_glow.use_nodes = True
nt = m_glow.node_tree
nt.nodes.clear()
o_ = nt.nodes.new('ShaderNodeOutputMaterial')
uvn = nt.nodes.new('ShaderNodeUVMap')
uvn.uv_map = 'PSX'
ti = nt.nodes.new('ShaderNodeTexImage')
ti.image = bpy.data.images.load(PB_EMI, check_existing=True)
ti.interpolation = 'Closest'
nt.links.new(uvn.outputs[0], ti.inputs[0])
em = nt.nodes.new('ShaderNodeEmission')
em.inputs['Strength'].default_value = 0.0
nt.links.new(ti.outputs[0], em.inputs['Color'])
tr = nt.nodes.new('ShaderNodeBsdfTransparent')
add = nt.nodes.new('ShaderNodeAddShader')
nt.links.new(tr.outputs[0], add.inputs[0])
nt.links.new(em.outputs[0], add.inputs[1])
nt.links.new(add.outputs[0], o_.inputs['Surface'])
m_glow['night_emit'] = 2.0
m_glow['blend'] = 'additive'
try:
    m_glow.surface_render_method = 'BLENDED'
except AttributeError:
    pass
m_glass = P.psx_glass('M_Start_Glass', tint=(0.12, 0.13, 0.12), alpha=0.45)
m_glass_car = P.psx_glass('M_Start_GlassDirty', tint=(0.17, 0.16, 0.13), alpha=0.62)
for name, o in objs.items():
    a = K.ATLAS[name]
    if a in ('Start', 'StartProps'):
        continue
    o.data.materials.clear()
    if a == 'panel':
        o.data.materials.append(m_panel)
    elif a.startswith('lens:'):
        o.data.materials.append(lens_mats[a[5:]])
        o['lamp_kind'] = a[5:]
    elif a == 'glow':
        o.data.materials.append(m_glow)
        o.visible_shadow = False
    elif a == 'glass':
        o.data.materials.append(m_glass_car if SK.ROOT_OF[name] == 'StartCar' else m_glass)
    else:
        raise ValueError(a)

# ------------------------------------------------------------------ 4. hierarchy (world transforms kept)
bpy.context.view_layer.update()


def attach(child, parent):
    bpy.context.view_layer.update()
    mw = child.matrix_world.copy()
    child.parent = parent
    child.matrix_parent_inverse = parent.matrix_world.inverted()
    child.matrix_world = mw


for name, p in C['parts'].items():
    if name in objs and p.get('parent') and p['parent'] in objs:
        attach(objs[name], objs[p['parent']])
    p['object'] = name if name in objs else None
    p['root'] = SK.ROOT_OF.get(name, name)


def root_of_part(name):
    while C['parts'][name].get('parent'):
        name = C['parts'][name]['parent']
    return name


for s in C['sockets'].values():
    s['root'] = root_of_part(s['parent']) if s.get('parent') in C['parts'] else s.get('parent')

# ------------------------------------------------------------------ 5. lamps: LIGHT_ empties + lights json
lens_of = {'Booth': 'Lens_Booth', 'Flood': 'Lens_Flood'}
glow_of = {'Booth': 'Glow_Booth', 'Flood': 'Glow_Flood'}
lamp_parent = {'Booth': 'Lamp_Booth', 'Flood': 'Lamp_Flood'}
items = []
for name, pos, d, typ, up in K.LAMPS:
    root = SK.ROOT_OF[lamp_parent[name]]
    off = SK.DISPLAY[root]
    e = bpy.data.objects.new('LIGHT_' + name, None)
    colls[root].objects.link(e)
    e.empty_display_type, e.empty_display_size = 'SINGLE_ARROW', 0.12
    dv = Vector(d).normalized()
    uv_ = Vector(up)
    uv_ = (uv_ - dv * uv_.dot(dv)).normalized()
    xv_ = uv_.cross(dv)                                  # empty: +Z = beam, +Y = the json 'up' (Unity light frame)
    e.matrix_world = Matrix.Translation(Vector(pos) + off) @ Matrix((xv_, uv_, dv)).transposed().to_4x4()
    T = LIGHT_TYPES[typ]
    e['type'], e['group'], e['dir'], e['up'], e['color'] = typ, T['group'], list(dv), list(up), list(T['color'])
    e['intensity_urp'] = T['I']
    e['lens'] = lens_of[name]
    attach(e, objs[lamp_parent[name]])
    it = dict(socket='LIGHT_' + name, root=root, lens=lens_of[name], glow=glow_of[name], parent=lamp_parent[name], type=typ,
              unityLight=T['unity'], position=r4(pos), forward=r4(dv), up=r4(uv_), positionUnity=to_unity(pos),
              forwardUnity=to_unity(dv), upUnity=to_unity(uv_), intensityURP=T['I'], kelvin=T['cct'], colorLinear=list(T['color']),
              range=T['rng'], group=T['group'], shadows=False, desc=T['desc'], startsOn=False)
    if T['spot']:
        it['spotAngle'] = T['spot']
    items.append(it)
    C['sockets']['LIGHT_' + name] = dict(pos=r4(pos), kind='light', parent=lamp_parent[name], root=root, dir=r4(dv),
                                         note=f'{T["unity"]} light ({typ}): start_lights.json')
json.dump(dict(note='start post lamps. position / forward / up: the root\'s own frame (Blender: z up, the front -y, m); *Unity: (-x, z, '
                    '-y) in the prefab. intensityURP at 100 %. The lens (Lens_*) switches M_Start_Lamp_<Kind> (lit) / _Off, the halo '
                    'card (Glow_*) is shown with it. group post: the booth lamp; group battery: the tripod light (blinks, goes out '
                    'when the battery is flat) - the game decides; all start off.',
               lights=items), open(os.path.join(OUT, 'start_lights.json'), 'w'), indent=1)
INFO['lamps'] = len(items)

# ------------------------------------------------------------------ 6. colliders (in each parent's own frame)
coll_c = bpy.data.collections.new('Colliders')
bpy.context.scene.collection.children.link(coll_c)
C['colliders'] = []
for name, par, (x0, x1, y0, y1, z0, z1), rot in SK.BOXES:
    root = SK.ROOT_OF[par]
    off = SK.DISPLAY[root]
    piv = Vector(C['parts'][par]['pivot'])
    c = Vector(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2))
    size = Vector((x1 - x0, y1 - y0, z1 - z0))
    kb_ = K.KB()
    kb_.box(-size.x / 2, size.x / 2, -size.y / 2, size.y / 2, -size.z / 2, size.z / 2, 0)
    ob = kb_.to_object('COL_' + name, coll=coll_c)
    ob.location = c + off
    if rot:
        ob.rotation_euler = Euler([math.radians(a) for a in rot], 'XYZ')
    ob.display_type = 'WIRE'
    ob.hide_render = True
    ob['collider'] = 'box'
    attach(ob, objs[par])
    lc = c - piv
    ent = dict(name='COL_' + name, parent=par, root=root, type='box', center=r4(lc), size=r4(size), center_root=r4(c),
               centerUnity=to_unity(lc), sizeUnity=r4((size.x, size.z, size.y)))
    if rot:
        ent['rotation'] = [float(a) for a in rot]
        ent['rotationUnity'] = [float(rot[0]), -float(rot[2]), float(rot[1])]
    C['colliders'].append(ent)
log('colliders', len(C['colliders']))

# ------------------------------------------------------------------ 7. part boxes (grab / hit box of moving parts)
for k, p in C['parts'].items():
    o = objs.get(k)
    if o is None or p['kind'] == 'static':
        continue
    co = np.array([tuple(v.co) for v in o.data.vertices])
    lo, hi = co.min(0), co.max(0)
    p['box'] = dict(center=r4((lo + hi) / 2), size=r4(np.maximum(hi - lo, 0.004)))

# ------------------------------------------------------------------ 8. Unity values of the parts and sockets, meta, stats, save
for k, p in C['parts'].items():
    p['pivotUnity'] = to_unity(p['pivot'])
    if p.get('axis'):
        p['axisUnity'] = to_unity(p['axis'])
for s in C['sockets'].values():
    s['posUnity'] = to_unity(s['pos'])
    if s.get('dir'):
        s['dirUnity'] = to_unity(s['dir'])
tris, tris_root = 0, {}
for o in bpy.data.objects:
    if o.type != 'MESH' or o.name.startswith('COL_'):
        continue
    t = sum(len(pl.vertices) - 2 for pl in o.data.polygons)
    tris += t
    tris_root[o.get('root', '?')] = tris_root.get(o.get('root', '?'), 0) + t
INFO['tris'] = tris
INFO['tris_by_root'] = tris_root
INFO['objects'] = {r: sorted(o.name for o in colls[r].objects) for r in SK.ROOTS}
INFO['display_offset'] = {r: r4(SK.DISPLAY[r]) for r in SK.ROOTS}
C['meta'] = dict(version='1.0', units='metres, degrees',
                 frame='each root (StartBooth, StartCar, StartLamp, StartTyres, StartTrash) in its own frame: z up, the front -y; '
                       'origin on the ground (z = 0) - the booth: the middle of its pad; the car: the middle of its footprint; the '
                       'light: the tripod\'s middle; the tyres: the stack\'s middle; the rubbish: the heap\'s middle. Every pivot / '
                       'pos here is in its root\'s frame. In start.blend the roots stand apart (display_offset); their FBX is '
                       'exported with the root at the origin',
                 unity='position (x, y, z) -> Unity (-x, z, -y) in the prefab; axis likewise; angle -> -angle (Unity is left-handed); '
                       'box centre likewise, size (sx, sy, sz) -> (sx, sz, sy); the root\'s front (-y) is the prefab\'s +Z',
                 display_offset=INFO['display_offset'],
                 colliders='BoxCollider per entry on its parent: center / size in the parent\'s own frame (its pivot, no rotation), '
                           'centerUnity / sizeUnity the same in Unity axes; center_root: in the root\'s frame; rotation = Blender XYZ '
                           'euler of the box (the booth roof), rotationUnity the Unity euler. The COL_ objects are in the .blend '
                           'for reference (collection Colliders), not in the FBX',
                 lamps='start_lights.json', model='start.blend', textures='tex',
                 materials='M_Start_Start (the booth, the car) / M_Start_StartProps (the light, the battery, the tyres, the rubbish): '
                           'baked PSX atlases (point filter); M_Start_Panel: the sign and the lens tiles; M_Start_Lamp_Booth / _Off, '
                           'M_Start_Lamp_Flood / _Off: lit / off; M_Start_Glow: additive halo; M_Start_Glass (the booth window), '
                           'M_Start_GlassDirty (the car)',
                 ix='IX_Door (the booth door) and IX_CarDoor (the car\'s driver door) - names without numbers: Unity gives them '
                    'their IX numbers when it puts the prefabs on the map')
sfx = '_draft' if DRAFT else ''
for o in bpy.data.objects:
    if o.name.startswith('COL_'):
        o.hide_set(True)
json.dump(_nz(C), open(os.path.join(OUT, f'start_contract{sfx}.json'), 'w'), indent=1, ensure_ascii=False)
kinds = {}
for p in C['parts'].values():
    kinds[p['kind']] = kinds.get(p['kind'], 0) + 1
INFO['contract'] = dict(parts=len(C['parts']), sockets=len(C['sockets']), colliders=len(C['colliders']), kinds=kinds)
log('contract', INFO['contract'], 'tris', tris, tris_root)
for o in [o for o in bpy.data.objects if o.type == 'MESH']:
    uvs = o.data.uv_layers
    if 'PSX' in uvs:
        for u in [u for u in uvs if u.name != 'PSX']:
            uvs.remove(u)
        uvs['PSX'].active = True
    if 'zone' in o.data.attributes and not DRAFT:
        o.data.attributes.remove(o.data.attributes['zone'])
json.dump(_nz(INFO), open(os.path.join(OUT, f'start{sfx}.json'), 'w'), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, f'start{sfx}.blend'))
log('saved', f'start{sfx}.blend')

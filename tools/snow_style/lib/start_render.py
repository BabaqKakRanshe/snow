"""Cycles previews of out/start.blend: day views of each prefab (the doors open where it shows the inside), and a night view
with the booth lamp and the tripod light on (lenses lit, halo cards, the lights of start_lights.json); for the night view the
roots are moved together.  python3 -I start_render.py <views|all> [spp] -> rend/start/<view>.png"""
import sys, os, math, json
sys.path.insert(0, '/home/claude/uaz')
import bpy
from mathutils import Vector, Matrix, Euler
import psxlib as P

HERE = P.HERE
OUT = os.path.join(HERE, 'rend', 'start')
os.makedirs(OUT, exist_ok=True)
want = sys.argv[1].split(',') if len(sys.argv) > 1 and sys.argv[1] != 'all' else None
spp = int(sys.argv[2]) if len(sys.argv) > 2 else 24
bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, 'out', 'start.blend'))
sc = bpy.context.scene
for o in bpy.data.objects:
    if o.name.startswith('COL_'):
        o.hide_render = True
for m in bpy.data.materials:
    if m.node_tree:
        for nd in m.node_tree.nodes:
            if nd.type == 'TEX_IMAGE':
                nd.interpolation = 'Closest'
O = bpy.data.objects
ROOT0 = {r: O[r].matrix_world.copy() for r in ('StartBooth', 'StartCar', 'StartLamp', 'StartTyres', 'StartTrash')}
gme = bpy.data.meshes.new('Ground')
gme.from_pydata([(-40, -40, -0.002), (40, -40, -0.002), (40, 40, -0.002), (-40, 40, -0.002)], [], [(0, 1, 2, 3)])
gob = bpy.data.objects.new('Ground', gme)
gm = bpy.data.materials.new('M_ground')
gm.use_nodes = True
pb = next(n for n in gm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
pb.inputs['Base Color'].default_value = (0.055, 0.050, 0.038, 1)
pb.inputs['Roughness'].default_value = 0.95
gme.materials.append(gm)
sc.collection.objects.link(gob)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
sc.collection.objects.link(cam)
sc.camera = cam
w = bpy.data.worlds.new('W')
sc.world = w
w.use_nodes = True
bg = w.node_tree.nodes['Background']
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
sun.data.angle = math.radians(3.0)
sc.collection.objects.link(sun)
lights = []
LJ = json.load(open(os.path.join(HERE, 'out', 'start_lights.json')))['lights']
for it in LJ:
    e = O[it['socket']]
    kind = 'SPOT' if it['unityLight'] == 'Spot' else 'POINT'
    ld = bpy.data.lights.new('L_' + it['socket'], kind)
    ld.color = it['colorLinear']
    ld.energy = 900.0 if kind == 'SPOT' else 120.0
    ld.shadow_soft_size = 0.06
    if kind == 'SPOT':
        ld.spot_size = math.radians(it.get('spotAngle', 100.0))
        ld.spot_blend = 0.4
    lo = bpy.data.objects.new('L_' + it['socket'], ld)
    sc.collection.objects.link(lo)
    lo.parent = e
    lo.matrix_parent_inverse = Matrix.Identity(4)
    lo.matrix_basis = Matrix.Rotation(math.pi, 4, 'X')               # the empty's +Z is the beam; a light shines down its -Z
    lights.append(lo)
SWAP = {}
for o in O:
    if o.type == 'MESH' and o.get('lamp_kind'):
        SWAP[o.name] = o.data.materials[0]


def night(on):
    for lo in lights:
        lo.hide_render = not on
    for o in O:
        if o.name.startswith('Glow_'):
            o.hide_render = not on
    for name, m_off in SWAP.items():
        kind = O[name]['lamp_kind'].capitalize()
        m = bpy.data.materials['M_Start_Lamp_' + kind if on else 'M_Start_Lamp_' + kind + '_Off']
        O[name].data.materials[0] = m
        pb_ = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if pb_ is not None:
            pb_.inputs['Emission Strength'].default_value = float(m.get('night_emit', 0.0)) if on else 0.0
    g = bpy.data.materials.get('M_Start_Glow')
    for n in g.node_tree.nodes:
        if n.type == 'EMISSION':
            n.inputs['Strength'].default_value = 2.0 if on else 0.0
    if on:
        bg.inputs['Color'].default_value = (0.010, 0.012, 0.020, 1)
        bg.inputs['Strength'].default_value = 0.6
        sun.hide_render = True
    else:
        bg.inputs['Color'].default_value = (0.56, 0.62, 0.72, 1)
        bg.inputs['Strength'].default_value = 0.85
        sun.hide_render = False
        sun.data.energy = 3.0
        sun.rotation_euler = Euler((math.radians(52), 0, math.radians(35)))


def roots(layout):
    for r, M0 in ROOT0.items():
        O[r].matrix_world = M0
    for r, (pos, yaw) in (layout or {}).items():
        O[r].matrix_world = Matrix.Translation(Vector(pos)) @ Matrix.Rotation(math.radians(yaw), 4, 'Z')
    bpy.context.view_layer.update()


def top(o):
    while o.parent:
        o = o.parent
    return o.name


TOP = {o.name: top(o) for o in O}


def show(rs):
    for o in O:
        if TOP[o.name] in ROOT0 and not o.name.startswith('COL_'):
            o.hide_render = TOP[o.name] not in rs
            if o.name.startswith('Glow_') or o.name.startswith('L_'):
                o.hide_render = True


def doors(booth=0.0, car=0.0):
    O['IX_Door'].rotation_euler = (0, 0, math.radians(booth))
    O['IX_CarDoor'].rotation_euler = (0, 0, -math.radians(car))
    bpy.context.view_layer.update()


B, CA, L = Vector((-7, 0, 0)), Vector((0, 0, 0)), Vector((5.5, -2, 0))
NIGHT_LAYOUT = {'StartBooth': ((-3.4, 1.8, 0), -20.0), 'StartCar': ((1.2, 2.9, 0), 18.0), 'StartLamp': ((-0.4, -1.6, 0), 196.0),
                'StartTyres': ((-5.6, 0.2, 0), 0.0), 'StartTrash': ((-5.4, 2.9, 0), 30.0)}
PROPS_LAYOUT = {'StartLamp': ((6.3, -0.9, 0), 20.0), 'StartTyres': ((9.9, 2.4, 0), 0.0), 'StartTrash': ((8.1, 1.1, 0), 0.0)}
ALL = tuple(ROOT0)
VIEWS = {   # name: eye, target, lens, night, layout, (booth door, car door), roots shown
    'booth': (B + Vector((3.0, -4.4, 1.7)), B + Vector((0.15, 0.0, 1.25)), 30, False, None, (0, 0), ('StartBooth',)),
    'booth_in': (B + Vector((0.78, 0.80, 1.58)), B + Vector((-0.30, -0.62, 0.98)), 16, False, None, (100, 0), ('StartBooth',)),
    'car': (CA + Vector((4.4, -5.0, 1.8)), CA + Vector((0.1, -0.2, 0.75)), 30, False, None, (0, 62), ('StartCar',)),
    'car_rear': (CA + Vector((-4.6, 4.6, 1.6)), CA + Vector((-0.2, 0.6, 0.55)), 30, False, None, (0, 0), ('StartCar',)),
    'props': (Vector((10.4, -5.6, 2.5)), Vector((7.6, 0.6, 0.75)), 30, False, PROPS_LAYOUT, (0, 0),
              ('StartLamp', 'StartTyres', 'StartTrash')),
    'night': (Vector((4.4, -5.2, 1.9)), Vector((-1.2, 1.6, 1.0)), 26, True, NIGHT_LAYOUT, (35, 40), ALL),
}
P.setup_cycles((1100, 700), spp)
sc.cycles.max_bounces = 4
for nm, (e, t, lens, nt, layout, (db, dc), rs) in VIEWS.items():
    if want and nm not in want:
        continue
    roots(layout)
    doors(db, dc)
    show(rs)
    night(nt)
    sc.view_settings.exposure = 1.2 if nm == 'booth_in' else 0.0
    cam.location = Vector(e)
    cam.rotation_euler = (Vector(t) - Vector(e)).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens
    cam.data.clip_start = 0.03
    P.render(os.path.join(OUT, nm + '.png'))
    print('rendered', nm, flush=True)

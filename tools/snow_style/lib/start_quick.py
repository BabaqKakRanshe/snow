"""workbench views of out/start[_draft].blend for checking: python3 -I start_quick.py -- <blend> <out_dir> [views,...]"""
import bpy, sys, os, math
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
OUT = a[1]
want = a[2].split(',') if len(a) > 2 else None
os.makedirs(OUT, exist_ok=True)
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'
sh.color_type = 'TEXTURE' if 'draft' not in a[0] else 'MATERIAL'
sh.show_cavity = True
sh.cavity_type = 'BOTH'
for o in bpy.data.objects:
    if o.name.startswith(('COL_', 'Glow_')):
        o.hide_render = True
for m in bpy.data.materials:
    if m.node_tree:
        for nd in m.node_tree.nodes:
            if nd.type == 'TEX_IMAGE':
                nd.interpolation = 'Closest'
bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, -0.001))
cam = bpy.data.objects.new('qcam', bpy.data.cameras.new('qcam'))
sc.collection.objects.link(cam)
sc.camera = cam
B, CA, L, TY, TR = Vector((-7, 0, 0)), Vector((0, 0, 0)), Vector((5.5, -2, 0)), Vector((5.5, 2, 0)), Vector((9, 0, 0))
V = {
    'all': ((1.0, -16.0, 7.0), (1.0, 0.0, 0.8), 28, (1600, 800)),
    'booth_fr': (B + Vector((2.6, -4.2, 1.9)), B + Vector((0.1, 0, 1.2)), 30, (1000, 1000)),
    'booth_bl': (B + Vector((-3.0, 3.8, 2.4)), B + Vector((0.0, 0.0, 1.2)), 30, (1000, 1000)),
    'booth_in': (B + Vector((0.55, 0.70, 1.55)), B + Vector((-0.40, -0.75, 0.95)), 18, (1000, 1000)),
    'car_fl': (CA + Vector((4.2, -5.2, 1.9)), CA + Vector((0.0, -0.3, 0.7)), 30, (1200, 900)),
    'car_rr': (CA + Vector((-4.4, 5.0, 2.0)), CA + Vector((0.0, 0.4, 0.6)), 30, (1200, 900)),
    'car_side': (CA + Vector((7.5, 0.0, 1.0)), CA + Vector((0.0, 0.0, 0.7)), 35, (1400, 700)),
    'car_in': (CA + Vector((1.25, 0.30, 1.30)), CA + Vector((0.0, -0.50, 0.85)), 18, (1000, 1000)),
    'lamp': (L + Vector((2.0, -2.6, 1.9)), L + Vector((0.2, 0.1, 1.0)), 30, (1000, 1000)),
    'props': (Vector((7.3, -2.8, 2.6)), Vector((7.5, 1.2, 0.3)), 30, (1200, 900)),
}
for nm, (e, t, lens, res) in V.items():
    if want and nm not in want:
        continue
    sc.render.resolution_x, sc.render.resolution_y = res
    cam.location = Vector(e)
    cam.rotation_euler = (Vector(t) - Vector(e)).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens
    cam.data.clip_start = 0.02
    sc.render.filepath = os.path.join(OUT, nm + '.png')
    bpy.ops.render.render(write_still=True)
print('ok')

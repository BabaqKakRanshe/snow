"""Ground of context/start_gate under a 0.5 m grid in the ZoneGate's frame (u = its x, v = its y, -25..25 m), heights from the gate,
and the trees / bushes around it (Blender, bpy).  python3 -I start_ctx_grid.py -> /home/claude/start/grid_H.npy (H[iv, iu]),
grid_meta.json (us, vs, trees (u, v, isBush), g0, ax, ay).  context.blend: source/context/start_gate (copied to /home/claude/start)"""
import bpy, math, json
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
bpy.ops.wm.open_mainfile(filepath='/home/claude/start/context.blend')
zg = bpy.data.objects['ZoneGate']; ter = bpy.data.objects['Terrain']
dg = bpy.context.evaluated_depsgraph_get(); bvh = BVHTree.FromObject(ter, dg)
Mt = ter.matrix_world; Mti = Mt.inverted()
def h(x, y):
    loc, n, i, d = bvh.ray_cast(Mti @ Vector((x, y, 300)), (Mti.to_3x3() @ Vector((0, 0, -1))).normalized())
    return (Mt @ loc).z if loc is not None else np.nan
G = zg.matrix_world; g0 = G.translation
ax = (G.to_3x3() @ Vector((1, 0, 0))).normalized(); ay = (G.to_3x3() @ Vector((0, 1, 0))).normalized()
us = np.arange(-25, 25.01, 0.5); vs = np.arange(-25, 25.01, 0.5)
H = np.array([[h(*(g0 + ax * u + ay * v)[:2]) - g0.z for u in us] for v in vs])
trees = []
for o in bpy.data.objects:
    if o.parent is None and o.get('prefab') and any(k in str(o.get('prefab')) for k in ('Pine', 'Fir', 'Birch', 'Spruce', 'Dead', 'Bush')):
        d = o.matrix_world.translation - g0
        trees.append((d.dot(ax), d.dot(ay), 'Bush' in str(o.get('prefab'))))
trees = list(set((round(a, 2), round(b, 2), c) for a, b, c in trees))
np.save('/home/claude/start/grid_H.npy', H)
json.dump(dict(us=us.tolist(), vs=vs.tolist(), trees=trees, g0=list(g0), ax=list(ax), ay=list(ay)), open('/home/claude/start/grid_meta.json', 'w'))
print('ok', H.shape, np.nanmin(H), np.nanmax(H))

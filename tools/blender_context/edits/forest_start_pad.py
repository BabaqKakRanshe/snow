"""Forest, 27.10.2026 (docs/gdd/locations/start_post, QUESTIONS 1 + 8): the start post's lay-by as a real road lay-by.

The old pad was one flat shelf at 28.69 m while the road beside it falls 8.7 % (29.4 m at the gate, 27.5 m at the blocks):
0.6 m below the road at the gate end, a 2.5 m wall at the far end, 40-46 deg edges. Now the pad follows the road: its
height on the road edge is the road's (sampled along the road's middle, smoothed), rising 3 % towards the hill; around
it the ground is cut or filled to at most 1:2 (a natural slope gentler than that stays as it is), the crease softened.

Frame as in Snow/Assets/Snow/Levels/Forest/ForestStartDress.json: u across the road (+u north-east, downhill), v along it
(+v north-west, out of the zone), metres from the gate.

  blender -b --factory-startup map.blend --python forest_start_pad.py -- <out_dir>
Writes <out_dir>/map_edit.blend + before/after views; then export_ground.py carries the ground to Unity.
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'Snow', 'Assets', 'Snow', 'Levels', 'Forest', 'ForestStartDress.json')
PAD = (-13.5, -24.0, -3.5, 3.0)       # u0, v0, u1, v1: the lay-by (u1 = the road's edge)
CORNER = 3.0                          # m: rounded corners
CROSS = 0.03                          # rise per metre from the road edge towards the hill
SLOPE = 0.5                           # cut / fill: 1 m up or down per 2 m out
REACH = 30.0                          # m around the pad that may change
ROAD_SMOOTH = 6.0                     # m: road heights averaged along v
SOFTEN = 1.2                          # m: the crease at the pad's edge


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def render(path, cam_loc, look_at):
    sc = bpy.context.scene
    cam = sc.camera
    if cam is None:
        cam = bpy.data.objects.new('EditCam', bpy.data.cameras.new('EditCam'))
        sc.collection.objects.link(cam)
        sc.camera = cam
    cam.data.lens = 24
    cam.data.clip_end = 3000
    cam.location = cam_loc
    cam.rotation_euler = (Vector(look_at) - Vector(cam_loc)).to_track_quat('-Z', 'Y').to_euler()
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'SINGLE'
    sc.display.shading.show_shadows = True
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.render.filepath = os.path.abspath(path)
    sc.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)


def main():
    out = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else os.path.dirname(bpy.data.filepath)
    os.makedirs(out, exist_ok=True)
    with open(DATA, encoding='utf-8') as f:
        d = json.load(f)
    cx, cz = d['centre']
    c, s = math.cos(math.radians(d['yaw'])), math.sin(math.radians(d['yaw']))

    def world(u, v):                                   # frame -> Unity x, z
        return cx + u * c + v * s, cz - u * s + v * c

    def B(u, v, y):                                    # frame -> Blender location
        x, z = world(u, v)
        return (-x, -z, y)

    ground = bpy.data.objects['Ground']
    me = ground.data
    co = np.zeros(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    ux, uz, H = -co[:, 0].astype(np.float64), -co[:, 1].astype(np.float64), co[:, 2].astype(np.float64)
    dx, dz = ux - cx, uz - cz
    U = dx * c - dz * s
    V = dx * s + dz * c
    near = (U > PAD[0] - REACH) & (U < PAD[2] + REACH) & (V > PAD[1] - REACH) & (V < PAD[3] + REACH)

    # the road's height along v: ground on the road's middle (u = 0), averaged over ROAD_SMOOTH
    on_road = near & (np.abs(U) < 1.0)
    vs = np.arange(PAD[1] - REACH, PAD[3] + REACH + 0.5, 0.5)
    road = np.array([H[on_road & (np.abs(V - v) < ROAD_SMOOTH * 0.5)].mean() for v in vs])

    def pad_height(u, v):
        return np.interp(v, vs, road) + CROSS * (PAD[2] - u)

    u0, v0, u1, v1 = PAD
    cu = np.clip(U, u0 + CORNER, u1 - CORNER)
    cv = np.clip(V, v0 + CORNER, v1 - CORNER)
    dist = np.maximum(np.hypot(U - cu, V - cv) - CORNER, 0.0)
    P = pad_height(np.clip(U, u0, u1), np.clip(V, v0, v1))
    F = H.copy()
    m = near
    F[m] = np.clip(H[m], P[m] - dist[m] * SLOPE, P[m] + dist[m] * SLOPE)
    # soften the crease along the edge: blend towards the plain envelope just outside it
    k = smoothstep(0.0, SOFTEN, dist) * (1 - smoothstep(SOFTEN, SOFTEN * 3, dist))
    F[m] = F[m] * (1 - 0.35 * k[m]) + (P[m] + np.clip(H[m] - P[m], -dist[m] * SLOPE * 0.8, dist[m] * SLOPE * 0.8)) * 0.35 * k[m]

    views = [('arrive', B(-1.0, -40.0, 0) [:2] + (32.0,), B(-7.0, -6.0, 28.0)),
             ('overview', B(10.0, -38.0, 0)[:2] + (48.0,), B(-8.0, -8.0, 28.0))]
    trees = bpy.data.collections.get('TerrainTrees')
    for o in (trees.objects if trees else []):
        o.hide_render = True
    for name, loc, at in views:
        render(os.path.join(out, f'{name}_before.png'), loc, at)
    co[:, 2] = F.astype(np.float32)
    me.vertices.foreach_set('co', co.ravel())
    me.update()
    for name, loc, at in views:
        render(os.path.join(out, f'{name}_after.png'), loc, at)
    for o in (trees.objects if trees else []):
        o.hide_render = False
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, 'map_edit.blend'))
    ch = F - H
    probe = {f'{u},{v}': round(float(pad_height(u, v)), 2) for u, v in ((-4, 0), (-12, 0), (-4, -12), (-12, -12), (-4, -24), (-12, -24))}
    print(f'[Snow] start pad: {int((np.abs(ch) > 0.05).sum())} vertices changed (up {ch.max():.2f} m, down {-ch.min():.2f} m); pad heights {probe}')


main()

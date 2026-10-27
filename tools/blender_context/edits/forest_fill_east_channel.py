"""Forest, 27.10.2026 (user: "засыпать всё"): fill the double channel east of the base.

The package square's east edge has a river channel (~60 m wide, bottom -13..-9 m); the generated land mirrored it, so a
second dry channel ran beside it, ridge between, from the north edge of the map to the south gorge. This fills both up
with new ground between the banks: the height at Unity x = 440 (west) and x = 780 (east), smoothed along the run,
blended across, plus soft hummocks; replaces the band (ridge and wall tops go too); a long slope down to the south
gorge (z -500 .. -400). Structures in the band below the new ground go up onto it, the others keep the ground at their
base; each stands on a flat pad.

Run on a map .blend of build_map.py:
  blender -b --factory-startup map.blend --python forest_fill_east_channel.py -- <out_dir>
Writes <out_dir>/map_edit.blend (Ground edited, structures moved) and before/after views; then
export_ground.py and export_moves.py on map_edit.blend carry it to Unity.
"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector, noise

X_W, X_E = 440.0, 780.0             # Unity x of the banks the fill is levelled between
BAND = (420.0, 460.0, 760.0, 800.0)  # fade in / full / full / fade out (Unity x), each edge wandering by EDGE_WANDER
EDGE_WANDER, EDGE_SCALE = 25.0, 150.0   # m: a straight seam a kilometre long reads as made
SOUTH = (-535.0, -470.0)             # fill fades out between these Unity z: down to the rim of the south gorge
SMOOTH_Z = 60.0                      # m: bank heights averaged along the run
PAD_R, PAD_FADE = 9.0, 14.0          # flat pad under a raised structure (at the mean new height around it), then its fade
HUMMOCKS = ((2.5, 120.0), (1.2, 45.0), (0.4, 15.0), (0.12, 5.0))   # (m, m): hummock height and size
RAISE_FROM = 0.3                     # m below the new ground: the structure goes up


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def grid(ob):
    me = ob.data
    co = np.zeros(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    ux, uz = -co[:, 0], -co[:, 1]
    # build_map's grid: vertex k = iz * cx + ix
    cx = int(np.sum(np.abs(uz - uz[0]) < 1e-3))
    cz = len(co) // cx
    return co, ux.reshape(cz, cx), uz.reshape(cz, cx), co[:, 2].reshape(cz, cx).copy()


def bank(UX, H, x, cell):
    col = int(np.argmin(np.abs(UX[0] - x)))
    h = H[:, col].astype(np.float64)
    k = max(1, int(SMOOTH_Z / cell))
    pad = np.pad(h, k, mode='edge')
    return np.convolve(pad, np.ones(2 * k + 1) / (2 * k + 1), mode='same')[k:-k]


def blur(H, k):
    """Box blur of 2k+1 cells, separable, edges repeated."""
    def run(a, axis):
        p = np.pad(a, [(k + 1, k) if ax == axis else (0, 0) for ax in range(2)], mode='edge')
        c = np.cumsum(p, axis=axis)
        hi = np.take(c, range(2 * k + 1, c.shape[axis]), axis=axis)
        lo = np.take(c, range(0, c.shape[axis] - 2 * k - 1), axis=axis)
        return (hi - lo) / (2 * k + 1)
    return run(run(H.astype(np.float64), 0), 1)


def sample(UX, UZ, H, x, z):
    i = np.interp(z, UZ[:, 0], np.arange(UZ.shape[0]))
    j = np.interp(x, UX[0], np.arange(UX.shape[1]))
    i0, j0 = int(i), int(j)
    i1, j1 = min(i0 + 1, H.shape[0] - 1), min(j0 + 1, H.shape[1] - 1)
    fi, fj = i - i0, j - j0
    return float((H[i0, j0] * (1 - fj) + H[i0, j1] * fj) * (1 - fi) + (H[i1, j0] * (1 - fj) + H[i1, j1] * fj) * fi)


def render(path, cam_loc, look_at):
    sc = bpy.context.scene
    cam = sc.camera
    if cam is None:
        cam = bpy.data.objects.new('EditCam', bpy.data.cameras.new('EditCam'))
        sc.collection.objects.link(cam)
        sc.camera = cam
    cam.data.lens = 24
    cam.data.clip_end = 6000
    cam.location = cam_loc
    cam.rotation_euler = (Vector(look_at) - Vector(cam_loc)).to_track_quat('-Z', 'Y').to_euler()
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'SINGLE'            # the relief, not the paint
    sc.display.shading.show_shadows = True
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.render.filepath = os.path.abspath(path)
    sc.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)


def main():
    out = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else os.path.dirname(bpy.data.filepath)
    os.makedirs(out, exist_ok=True)
    ground = bpy.data.objects['Ground']
    trees = bpy.data.collections.get('TerrainTrees')
    for o in (trees.objects if trees else []):
        o.hide_render = True                       # the relief reads better without the forest
    # Unity (x, y, z) -> Blender (-x, -z, y); a view like the user's shot: north of the base, looking south down the run
    views = [('view_south', (-600.0, 60.0, 140.0), (-560.0, 430.0, 0.0)),
             ('view_north', (-560.0, 470.0, 120.0), (-600.0, -300.0, 0.0))]
    for name, loc, at in views:
        render(os.path.join(out, f'{name}_before.png'), loc, at)

    co, UX, UZ, H = grid(ground)
    cell = float(abs(UX[0, 1] - UX[0, 0]))
    hW, hE = bank(UX, H, X_W, cell), bank(UX, H, X_E, cell)
    t = smoothstep(X_W, X_E, UX)
    T = hW[:, None] * (1 - t) + hE[:, None] * t
    # new ground across the whole band (not only raised: the ridge and wall tops between the channels go too): the bank
    # line plus soft hummocks in four sizes, like the forest floor west of it (copying the land east brought its straight
    # ravines and strips along)
    n = np.zeros_like(T)
    band = (UX > BAND[0] - EDGE_WANDER) & (UX < BAND[3] + EDGE_WANDER) & (UZ > SOUTH[0])
    for i, j in zip(*np.nonzero(band)):
        x, z = float(UX[i, j]), float(UZ[i, j])
        n[i, j] = sum(a * noise.noise(Vector((x / sc, z / sc, k * 3.7))) for k, (a, sc) in enumerate(HUMMOCKS))
    zs = UZ[:, 0]
    wander_w = np.array([EDGE_WANDER * noise.noise(Vector((0.0, z / EDGE_SCALE, 11.3))) for z in zs])[:, None]
    wander_e = np.array([EDGE_WANDER * noise.noise(Vector((5.0, z / EDGE_SCALE, 17.9))) for z in zs])[:, None]
    w = (smoothstep(BAND[0], BAND[1], UX - wander_w) * (1 - smoothstep(BAND[2], BAND[3], UX - wander_e))
         * smoothstep(SOUTH[0], SOUTH[1], UZ))
    F = H * (1 - w) + (T + n) * w

    # structures in the band below the new ground go up, each on a flat pad
    moved = []
    for e in bpy.data.objects:
        if e.type != 'EMPTY' or 'world_id' not in e or not str(e['world_id']).startswith('Structures/'):
            continue
        x, z = -e.matrix_world.translation.x, -e.matrix_world.translation.y
        if not (BAND[0] < x < BAND[3] and z > SOUTH[0]):
            continue
        y = e.matrix_world.translation.z
        d = np.hypot(UX - x, UZ - z)
        new = float(F[d <= PAD_R + PAD_FADE].mean())
        k = 1 - smoothstep(PAD_R, PAD_R + PAD_FADE, d)
        if new - y < RAISE_FROM:
            F = F * (1 - k) + y * k                      # stays: the ground under it stays at its base
            continue
        F = F * (1 - k) + new * k
        e.matrix_world.translation.z = new
        moved.append((e['world_id'], round(y, 2), round(new, 2)))
    co[:, 2] = F.ravel()
    ground.data.vertices.foreach_set('co', co.ravel())
    ground.data.update()

    for name, loc, at in views:
        render(os.path.join(out, f'{name}_after.png'), loc, at)
    for o in (trees.objects if trees else []):
        o.hide_render = False
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, 'map_edit.blend'))
    raised = F - H
    print(f'[Snow] fill east channel: {int((raised > 0.05).sum())} vertices raised (up to {raised.max():.1f} m), {int((raised < -0.05).sum())} lowered (up to {-raised.min():.1f} m); structures moved: {moved}')


main()

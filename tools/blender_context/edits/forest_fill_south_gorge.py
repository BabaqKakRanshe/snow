"""Forest, 27.10.2026 (the user's first complaint of the day, "дыра посреди карты"): fill the south gorge.

The package square's south edge had a gorge (~50 m wide, floor -40..-72 m) as the border of the chat's map; growing the
map to 1800 m put it across the middle, 1.4 km from the west edge to x ~760, just south of the base. Dry (the river's
water ends at -7 m on its rim), only pack rocks in it (they follow the ground).

Filled by shape, not by a straight band: the pit = ground below -20 m (east of x 720, where the land around is low
itself: below -30 m), grown by GROW m so its walls go too; inside it the ground is solved smooth from the edge (each
point the mean of its neighbours, so it meets the land around without a step: a long shallow valley where the banks
differ), then the same soft hummocks as the east channel fill, faded in from the edge.

  blender -b --factory-startup map_now.blend --python forest_fill_south_gorge.py -- <out_dir>
Writes <out_dir>/map_edit.blend + before/after views; export_ground.py then carries it to Unity.
"""
import os
import sys

import bpy
import numpy as np
from mathutils import Vector, noise

REGION = (-610.0, -800.0, 810.0, -420.0)    # Unity x0, z0, x1, z1 searched
PIT, PIT_EAST, EAST_X = -20.0, -24.0, 720.0  # m: below this is pit (east of EAST_X, where the land is low itself, deeper)
CUT = 4.0                                    # m: the most an old rim's crest is cut down
GROW = 35.0                                  # m: the pit grown so its walls are inside
MAP_WEST = -600.0                            # Unity x of the map's west edge
HUMMOCKS = ((2.5, 120.0), (1.2, 45.0), (0.4, 15.0), (0.12, 5.0))
HUMMOCK_IN = 20.0                            # m from the edge until the hummocks are full


def grow(mask, cells):
    """Binary dilation by a disc of radius cells (separable square, then trimmed to a disc by distance)."""
    m = mask.copy()
    for _ in range(cells):
        n = m.copy()
        n[1:, :] |= m[:-1, :]; n[:-1, :] |= m[1:, :]; n[:, 1:] |= m[:, :-1]; n[:, :-1] |= m[:, 1:]
        m = n
    return m


def distance_in(zone, cells):
    """Cells from the zone's edge, inside it (0 at the edge, capped at cells)."""
    d = np.zeros(zone.shape)
    cur = zone.copy()
    for k in range(cells):
        inner = cur.copy()
        inner[1:, :] &= cur[:-1, :]; inner[:-1, :] &= cur[1:, :]; inner[:, 1:] &= cur[:, :-1]; inner[:, :-1] &= cur[:, 1:]
        d[inner] += 1
        cur = inner
    return d


def harmonic(H, zone, iters, free_west=False):
    """Laplace fill: inside zone each cell becomes the mean of its four neighbours; outside stays H. free_west: the first
    column is the map's edge - no land beyond it, so it takes its neighbour's value instead of holding the old floor."""
    F = H.copy()
    west = zone[:, 1].copy()
    for _ in range(iters):
        avg = 0.25 * (np.roll(F, 1, 0) + np.roll(F, -1, 0) + np.roll(F, 1, 1) + np.roll(F, -1, 1))
        F[zone] = avg[zone]
        if free_west:
            F[west, 0] = F[west, 1]
    return F


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
    sc.display.shading.color_type = 'SINGLE'
    sc.display.shading.show_shadows = True
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.render.filepath = os.path.abspath(path)
    sc.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)


def main():
    out = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else os.path.dirname(bpy.data.filepath)
    os.makedirs(out, exist_ok=True)
    ground = bpy.data.objects['Ground']
    me = ground.data
    co = np.zeros(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    ux, uz = -co[:, 0], -co[:, 1]
    cx = int(np.sum(np.abs(uz - uz[0]) < 1e-3))
    cz = len(co) // cx
    UX, UZ, H = ux.reshape(cz, cx), uz.reshape(cz, cx), co[:, 2].reshape(cz, cx).astype(np.float64).copy()
    cell = float(abs(UX[0, 1] - UX[0, 0]))
    # work on the region only
    ix = np.nonzero((UX[0] >= REGION[0]) & (UX[0] <= REGION[2]))[0]
    iz = np.nonzero((UZ[:, 0] >= REGION[1]) & (UZ[:, 0] <= REGION[3]))[0]
    sz, sx = slice(iz.min(), iz.max() + 1), slice(ix.min(), ix.max() + 1)
    h, X, Z = H[sz, sx], UX[sz, sx], UZ[sz, sx]
    pit = np.where(X > EAST_X, h < PIT_EAST, h < PIT)
    zone = grow(pit, int(GROW / cell))
    at_edge = X[0, 0] <= MAP_WEST + 1.0           # the region starts at the map's west edge
    zone[[0, -1], :] = False
    zone[:, [0, -1]] = False

    # coarse solve first (4x), then the fine one from it
    k = 4
    hc = h[::k, ::k]
    zc = zone[::k, ::k]
    Fc = hc.copy()
    Fc[zc] = np.mean(hc[~zc])
    Fc = harmonic(Fc, zc, 4000, at_edge)
    up = np.repeat(np.repeat(Fc, k, 0), k, 1)[:h.shape[0], :h.shape[1]]
    F = h.copy()
    F[zone] = up[zone]
    F = harmonic(F, zone, 1500, at_edge)
    F = np.maximum(F, h - CUT)                     # fills; the old rims' crests may come down CUT m, no more

    d = distance_in(zone, int(HUMMOCK_IN / cell)) * cell
    w = np.clip(d / HUMMOCK_IN, 0.0, 1.0)
    w = w * w * (3 - 2 * w)
    n = np.zeros_like(F)
    for i, j in zip(*np.nonzero(zone)):
        x, z = float(X[i, j]), float(Z[i, j])
        n[i, j] = sum(a * noise.noise(Vector((x / s, z / s, q * 5.3 + 1.0))) for q, (a, s) in enumerate(HUMMOCKS))
    F = F + n * w
    new = H.copy()
    new[sz, sx] = np.where(zone, F, h)

    trees = bpy.data.collections.get('TerrainTrees')
    for o in (trees.objects if trees else []):
        o.hide_render = True
    # Unity (x, y, z) -> Blender (-x, -z, y): from the base looking south-west along the gorge, and from the west end
    views = [('view_base', (-340.0, 470.0, 90.0), (-60.0, 620.0, -10.0)),
             ('view_west', (560.0, 610.0, 110.0), (-200.0, 600.0, -20.0))]
    for name, loc, at in views:
        render(os.path.join(out, f'{name}_before.png'), loc, at)
    co[:, 2] = new.ravel().astype(np.float32)
    me.vertices.foreach_set('co', co.ravel())
    me.update()
    for name, loc, at in views:
        render(os.path.join(out, f'{name}_after.png'), loc, at)
    for o in (trees.objects if trees else []):
        o.hide_render = False
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, 'map_edit.blend'))
    ch = new - H
    print(f'[Snow] fill south gorge: pit {int(pit.sum())} vertices, zone {int(zone.sum())}; raised up to {ch.max():.1f} m, '
          f'lowered up to {-ch.min():.1f} m; deepest now {new[sz, sx][zone].min():.1f} m')


main()

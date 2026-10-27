"""Forest, 27.10.2026 (docs/gdd/locations/relief, all proposals): the map's big form from the real Harz.

The map was nearly flat (61 % under 8 deg, ~40 m over 1.8 km). The big form now comes from a real piece of the Upper
Harz near Clausthal (Copernicus GLO-30, window 3.6 km from 51.740 N / 10.375 E, the source's 90 m smoothing), squeezed
2:1 into the 1.8 km map (Kingdom Come's way: real hills and valleys, shorter distances) and scaled 0.35 in height, so
slopes come out ~10-20 deg and the relief 80-120 m; plus ridges along the map's edges. The plateau of that window lies
under the base.

Zones keep today's ground: the places built on it (base yard with its gate, checkpoint, start post, hut, lake bunker), the
skeleton, the lake (its shore may rise, not sink) and the roads (paint, fixed_mask.u8 from Unity); the relief fades into
them through a smooth correction pinned to zero on them (harmonic between them; a local fade drew cones and stairs). Things
outside the zones (Structures, the forest's fences) move onto the new ground, big ones on a flat pad; the pack rocks and
the mist follow on import.

  blender -b --factory-startup map.blend --python forest_relief_harz.py -- <out_dir> <fixed_mask.u8>
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', '..', 'snow_style', 'pydeps'))
import bpy
import numpy as np
from mathutils import Vector
from scipy import ndimage

DEM = os.path.join(HERE, '..', '..', '..', 'source', 'geo', 'copernicus_glo30', 'N51_E010.npy')   # 1 deg tile, row 0 = 52 N
LAT_N, LON_W, WINDOW = 51.740, 10.375, 3600.0     # the window's north-west corner and size (m)
SQUEEZE = 2.0                                    # real metres per map metre
HEIGHT = 0.42                                    # map metres per real metre
DETREND = 0.5                                    # share of the window's overall tilt taken out
EDGE_RISE, EDGE_WIDTH = 22.0, 160.0              # ridges along the map's edges
ZONES = ((340.0, -360.0, 100.0), (143.5, -64.7, 40.0), (-511.5, 554.2, 45.0), (-474.0, 404.0, 35.0), (-150.0, -34.0, 45.0))
                                                 # base yard + its gate, checkpoint, start post, hut, lake bunker (x, z, r)
ZONE_FADE, ROAD_FADE = 90.0, 100.0               # m over which the relief fades to today's ground at a zone / a road
FOLLOW = ('Structures', 'Rocks')                 # groups whose things outside the zones move onto the new ground (pack groups
                                                 # follow on import by themselves)
PAD_FROM, PAD_FADE = 2.5, 10.0                   # a thing wider than 2 x PAD_FROM gets a flat pad, faded over PAD_FADE
LIMITS = (-80.0, 50.0)                           # the tiles' height range (-84 .. 52) with a margin
SHORE, SHORE_FADE = 80.0, 40.0                   # m round the lake where the ground may rise, not sink (the water is a flat plane)


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def soft_limit(x, lo, hi, knee=12.0):
    y = x.copy()
    top, bot = hi - knee, lo + knee
    m = x > top
    y[m] = top + knee * (1 - np.exp(-(x[m] - top) / knee))
    m = x < bot
    y[m] = bot - knee * (1 - np.exp(-(bot - x[m]) / knee))
    return y


def footprint(e):
    """Unity x0, x1, z0, z1 of an Empty's meshes (its own place when it has none)."""
    xs, zs = [], []
    for c in e.children_recursive:
        if c.type != 'MESH':
            continue
        for v in c.bound_box:
            p = c.matrix_world @ Vector(v)
            xs.append(-p.x); zs.append(-p.y)
    if not xs:
        p = e.matrix_world.translation
        return -p.x, -p.x, -p.y, -p.y
    return min(xs), max(xs), min(zs), max(zs)


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
    args = sys.argv[sys.argv.index('--') + 1:]
    out, mask_path = args[0], args[1]
    os.makedirs(out, exist_ok=True)
    ground = bpy.data.objects['Ground']
    with open(ground['map_json'], encoding='utf-8') as f:
        g = json.load(f)['ground']
    me = ground.data
    co = np.zeros(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    step = int(ground['grid_step'])
    ix = np.arange(0, g['nx'], step); iz = np.arange(0, g['nz'], step)
    if ix[-1] != g['nx'] - 1: ix = np.append(ix, g['nx'] - 1)
    if iz[-1] != g['nz'] - 1: iz = np.append(iz, g['nz'] - 1)
    cz, cx = len(iz), len(ix)
    UX, UZ = -co[:, 0].reshape(cz, cx).astype(np.float64), -co[:, 1].reshape(cz, cx).astype(np.float64)
    H = co[:, 2].reshape(cz, cx).astype(np.float64).copy()
    cell = g['step'] * step

    full = np.fromfile(mask_path, dtype=np.uint8).reshape(g['nz'], g['nx'])
    road = ndimage.maximum_filter((full & 1) > 0, size=step + 1)[np.ix_(iz, ix)]
    water = ndimage.maximum_filter((full & 4) > 0, size=step + 1)[np.ix_(iz, ix)]

    # zones that keep their ground: the places built on it (circles), the skeleton, the lake; roads keep theirs too
    zone = water.copy()
    for (zx, zz, r) in ZONES:
        zone |= np.hypot(UX - zx, UZ - zz) <= r
    # the skeleton lies across half the map: each bone keeps its ground, not the whole box round them
    for o in bpy.data.objects:
        if o.type != 'MESH' or not o.users_collection or o.users_collection[0].name not in ('Giants', 'ForestDecals'):
            continue
        ps = [o.matrix_world @ Vector(v) for v in o.bound_box]
        xs, zs = [-p.x for p in ps], [-p.y for p in ps]
        zone |= (UX >= min(xs) - 10) & (UX <= max(xs) + 10) & (UZ >= min(zs) - 10) & (UZ <= max(zs) + 10)
    w = smoothstep(0.0, ZONE_FADE, ndimage.distance_transform_edt(~zone) * cell) *         smoothstep(0.0, ROAD_FADE, ndimage.distance_transform_edt(~road) * cell)

    # the Harz window on the map
    dem = np.load(DEM).astype(np.float64)
    dem = ndimage.gaussian_filter(dem, 1.5)
    ny, nx = dem.shape
    dlat, dlon = 1.0 / ny, 1.0 / nx
    my = dlat * 111320.0
    mx = dlon * 111320.0 * math.cos(math.radians(LAT_N - WINDOW / 2 / 111320.0))
    x0, z1 = g['x0'], g['z0'] + (g['nz'] - 1) * g['step']
    real_e = (UX - x0) * SQUEEZE                 # metres east of the window's west edge
    real_s = (z1 - UZ) * SQUEEZE                 # metres south of its north edge
    rows = (52.0 - LAT_N) / dlat + real_s / my
    cols = (LON_W - 10.0) / dlon + real_e / mx
    R = ndimage.map_coordinates(dem, [rows.ravel(), cols.ravel()], order=3, mode='nearest').reshape(cz, cx)
    A = np.c_[UX.ravel(), UZ.ravel(), np.ones(UX.size)]
    coef, *_ = np.linalg.lstsq(A, R.ravel(), rcond=None)
    R = R - DETREND * (A @ coef).reshape(cz, cx)
    D = HEIGHT * (R - R.mean())
    edge = np.minimum(np.minimum(UX - g['x0'], g['x0'] + (g['nx'] - 1) * g['step'] - UX), np.minimum(UZ - g['z0'], z1 - UZ))
    D += EDGE_RISE * (1 - smoothstep(0.0, EDGE_WIDTH, edge))

    # pin the relief to zero on zones and roads without local cones: a correction C that equals D there and is smooth
    # (harmonic) everywhere else is taken off D, so the Harz shape stays away from them and eases onto them over the
    # whole distance between them (solved 6x coarser, then a short fade takes up what the coarse grid misses)
    pinned = zone | road
    k = 6
    pc = ndimage.maximum_filter(pinned, size=k)[::k, ::k]
    Dc = ndimage.uniform_filter(D, k)[::k, ::k]
    C = Dc.copy()
    for _ in range(4000):
        P = np.pad(C, 1, mode='edge')
        avg = 0.25 * (P[:-2, 1:-1] + P[2:, 1:-1] + P[1:-1, :-2] + P[1:-1, 2:])
        C = np.where(pc, Dc, avg)
    Cf = ndimage.map_coordinates(C, [np.arange(cz)[:, None] / k + 0 * np.arange(cx)[None, :], 0 * np.arange(cz)[:, None] + np.arange(cx)[None, :] / k], order=1, mode='nearest')
    near = smoothstep(0.0, 25.0, ndimage.distance_transform_edt(~pinned) * cell)
    N = soft_limit(H + (D - Cf) * near, *LIMITS)
    to_water = ndimage.distance_transform_edt(~water) * cell
    shore = 1 - smoothstep(SHORE, SHORE + SHORE_FADE, to_water)
    N = N + shore * np.maximum(0.0, H - N)            # by the lake: never below today's ground

    # things outside the zones stand on the new ground: big ones on a flat pad at the mean height of their footprint
    followers = []
    for e in bpy.data.objects:
        if e.type != 'EMPTY' or 'world_id' not in e:
            continue
        grp = str(e['world_id']).split('/')[0]
        if grp not in FOLLOW:
            continue
        x, z = -e.matrix_world.translation.x, -e.matrix_world.translation.y
        i = int(round((z - UZ[0, 0]) / (UZ[1, 0] - UZ[0, 0]))); j = int(round((x - UX[0, 0]) / (UX[0, 1] - UX[0, 0])))
        if not (0 <= i < cz and 0 <= j < cx) or w[i, j] < 0.02:
            continue
        x0b, x1b, z0b, z1b = footprint(e)
        followers.append((max(x1b - x0b, z1b - z0b) * 0.5, e, x, z, i, j))
    followers.sort(key=lambda f: -f[0])
    for r, e, x, z, i, j in followers:
        if r < PAD_FROM:
            continue
        d = np.hypot(UX - x, UZ - z)
        target = float(N[d <= r].mean()) if (d <= r).any() else float(N[i, j])
        k = (1 - smoothstep(r, r + PAD_FADE, d)) * (w > 0.02)
        N = N * (1 - k) + target * k
    moved = 0
    for r, e, x, z, i, j in followers:
        dz = float(N[i, j] - H[i, j])
        if abs(dz) < 0.02:
            continue
        e.matrix_world.translation.z += dz
        moved += 1

    trees = bpy.data.collections.get('TerrainTrees')
    for o in (trees.objects if trees else []):
        o.hide_render = True
    # Unity (x, y, z) -> Blender (-x, -z, y)
    views = [('aerial_east', (-980.0, 120.0, 90.0), (-600.0, -40.0, 0.0)), ('aerial_square', (-150.0, 180.0, 95.0), (80.0, -120.0, 0.0)),
             ('aerial_south', (-250.0, 820.0, 95.0), (-150.0, 520.0, 0.0))]
    for name, loc, at in views:
        render(os.path.join(out, f'{name}_before.png'), loc, at)
    co[:, 2] = N.ravel().astype(np.float32)
    me.vertices.foreach_set('co', co.ravel())
    me.update()
    for name, loc, at in views:
        render(os.path.join(out, f'{name}_after.png'), loc, at)
    for o in (trees.objects if trees else []):
        o.hide_render = False
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, 'map_edit.blend'))
    gy, gx = np.gradient(N, cell)
    sl = np.degrees(np.arctan(np.hypot(gx, gy)))
    ch = N - H
    print(f'[Snow] relief harz: zones {zone.mean() * 100:.1f} %, roads {road.mean() * 100:.1f} %, {moved} things moved onto the new ground, changed up {ch.max():.1f} / down {-ch.min():.1f} m; '
          f'now p5..p95 {np.percentile(N, 5):.1f}..{np.percentile(N, 95):.1f} m, slope <8 deg {np.mean(sl < 8) * 100:.0f} %, >20 deg {np.mean(sl > 20) * 100:.1f} %')


main()

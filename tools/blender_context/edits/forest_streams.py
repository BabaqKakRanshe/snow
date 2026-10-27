"""Forest, 27.10.2026 (user: "3 rivers and none has water, they are planted with trees" -> "creeks along the valleys").

 1. The two straight ravines of the generated land (ForestRelief 21.10: east x 660..995, south z -660..-990) never had
    water and now cross the Harz relief (their bed would need 40 m cuts): filled - Laplace from their edge + hummocks.
 2. The north-west river of the Blender chat's package keeps its water mesh (Forest_Water), but the bake left the bed a
    little above the water on 70 % of it: the bed under the water goes 0.5 m below it, banks at most 1 : 1.7.
 3. Two creeks along real valleys of the new relief (drainage: priority-flood + D8 on a 4x coarser grid): the long one
    east of the base down to the south-east corner (its top, by the base and across the road, is left out) and a short one
    in the south. Their bed is lowered (at most CUT m) towards a profile that only falls; where the bed still dips, the
    water stands at the next sill's level - a pond. Water: a ribbon along the creek and flat ponds, as data for Unity
    (ForestStreams.json: Editor/ForestStreamsStep.cs builds the water, clears trees and grass, paints the bed).

  blender -b --factory-startup map.blend --python forest_streams.py -- <out_dir> <fixed_mask.u8>
"""
import heapq
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', '..', 'snow_style', 'pydeps'))
import bpy
import numpy as np
from mathutils import Vector, noise
from scipy import ndimage

RAVINES = ([(660, 560), (810, 320), (870, 40), (950, -280), (995, -590), (985, -860)],
           [(-560, -660), (-330, -770), (-60, -835), (250, -900), (560, -960), (860, -990)])
RAVINE_FILL = 24.0                     # m either side of a ravine's line that is solved anew
HUMMOCKS = ((1.5, 90.0), (0.8, 35.0), (0.3, 12.0))
CREEKS = (('SE', (1120.0, -1200.0)), ('South', (-126.0, -1200.0)))   # name, outlet near this map point
MIN_AREA = 0.04                        # km2 of catchment where a creek starts
CUT = 3.0                              # m: the most a creek's bed is lowered
DEPTH, BED_HALF, BANK = 0.35, 2.0, 1.5 # water depth over the bed; half the bed's width; banks 1 : BANK
POND_FROM = 0.6                        # water this much over the bed: a pond
POND_MAX = 120000.0                    # m2: a bigger hollow stays dry (the water would flood the map)
COARSE = 4


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def laplace(F, zone, iters):
    for _ in range(iters):
        P = np.pad(F, 1, mode='edge')
        avg = 0.25 * (P[:-2, 1:-1] + P[2:, 1:-1] + P[1:-1, :-2] + P[1:-1, 2:])
        F = np.where(zone, avg, F)
    return F


def dist_to_line(XX, ZZ, pts):
    d = np.full(XX.shape, 1e9)
    for (ax, az), (bx, bz) in zip(pts[:-1], pts[1:]):
        abx, abz = bx - ax, bz - az
        t = np.clip(((XX - ax) * abx + (ZZ - az) * abz) / (abx * abx + abz * abz), 0, 1)
        d = np.minimum(d, np.hypot(XX - ax - t * abx, ZZ - az - t * abz))
    return d


def drainage(h):
    """Priority-flood fill, D8 receivers and catchment counts on grid h."""
    ny, nx = h.shape
    filled = h.copy()
    done = np.zeros(h.shape, bool)
    pq = []
    for i in range(ny):
        for j in (0, nx - 1):
            heapq.heappush(pq, (filled[i, j], i, j)); done[i, j] = True
    for j in range(1, nx - 1):
        for i in (0, ny - 1):
            heapq.heappush(pq, (filled[i, j], i, j)); done[i, j] = True
    nb = [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)]
    order = []
    while pq:
        v, i, j = heapq.heappop(pq)
        order.append((i, j))
        for di, dj in nb:
            a, b = i + di, j + dj
            if 0 <= a < ny and 0 <= b < nx and not done[a, b]:
                done[a, b] = True
                filled[a, b] = max(filled[a, b], v + 1e-4)
                heapq.heappush(pq, (filled[a, b], a, b))
    rec = -np.ones((ny, nx, 2), int)
    for i in range(ny):
        for j in range(nx):
            best = None
            for di, dj in nb:
                a, b = i + di, j + dj
                if 0 <= a < ny and 0 <= b < nx:
                    sl = (filled[i, j] - filled[a, b]) / math.hypot(di, dj)
                    if sl > 0 and (best is None or sl > best[0]):
                        best = (sl, a, b)
            if best:
                rec[i, j] = (best[1], best[2])
    acc = np.ones(h.shape)
    for i, j in reversed(order):
        a, b = rec[i, j]
        if a >= 0:
            acc[a, b] += acc[i, j]
    return rec, acc


def stem(rec, area, outlet):
    donors = {}
    ny, nx = area.shape
    for i in range(ny):
        for j in range(nx):
            a, b = rec[i, j]
            if a >= 0:
                donors.setdefault((a, b), []).append((i, j))
    path = [outlet]
    while True:
        ds = [d for d in donors.get(path[-1], []) if area[d] >= MIN_AREA]
        if not ds:
            break
        path.append(max(ds, key=lambda d: area[d]))
    return path[::-1]


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
    keep = ndimage.maximum_filter(full > 0, size=step + 1)[np.ix_(iz, ix)]       # roads, things, the lake
    N = H.copy()

    # 1. the two straight ravines
    zone = np.zeros(H.shape, bool)
    for line in RAVINES:
        zone |= dist_to_line(UX, UZ, line) < RAVINE_FILL
    zone &= ~keep
    k = 4
    Fc = laplace(N[::k, ::k].copy(), zone[::k, ::k], 2500)
    up = np.repeat(np.repeat(Fc, k, 0), k, 1)[:cz, :cx]
    N = np.where(zone, up, N)
    N = laplace(N, zone, 800)
    edge_in = ndimage.distance_transform_edt(zone) * cell
    w = smoothstep(0.0, 12.0, edge_in)
    for i, j in zip(*np.nonzero(zone)):
        x, z = float(UX[i, j]), float(UZ[i, j])
        N[i, j] += w[i, j] * sum(a * noise.noise(Vector((x / s, z / s, q * 4.1 + 2.0))) for q, (a, s) in enumerate(HUMMOCKS))

    # 2. the north-west river: its bed under its water
    water_obj = next((o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('Forest_Water')), None)
    nw_line = []
    if water_obj is not None:
        wm = water_obj.data
        target = np.full(H.shape, np.inf)
        for poly in wm.polygons:
            vs = [water_obj.matrix_world @ wm.vertices[v].co for v in poly.vertices]
            pts = [(-v.x, -v.y, v.z) for v in vs]                                  # Unity x, z, y
            if not all(p[0] < -150 and p[1] > 150 for p in pts):
                continue                                                            # the north-west river only
            xs, zs = [p[0] for p in pts], [p[1] for p in pts]
            j0 = max(0, int((min(xs) - UX[0, 0]) / cell)); j1 = min(cx - 1, int((max(xs) - UX[0, 0]) / cell) + 1)
            i0 = max(0, int((min(zs) - UZ[0, 0]) / cell)); i1 = min(cz - 1, int((max(zs) - UZ[0, 0]) / cell) + 1)
            wy = sum(p[2] for p in pts) / len(pts)
            target[i0:i1 + 1, j0:j1 + 1] = np.minimum(target[i0:i1 + 1, j0:j1 + 1], wy - 0.5)
            nw_line.append(((min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2))
        has = np.isfinite(target)
        if has.any():
            d, (ti, tj) = ndimage.distance_transform_edt(~has, return_indices=True)
            env = target[ti, tj] + d * cell / 1.7
            N = np.where(d * cell < 8.0, np.minimum(N, env), N)
    # the river's middle line for tree clearing: the water polygons' centres ordered along the river
    if nw_line:
        pts = np.array(nw_line)
        order = [int(np.argmin(pts[:, 1]))]
        left = set(range(len(pts))) - set(order)
        while left:
            last = pts[order[-1]]
            nxt = min(left, key=lambda q: np.hypot(*(pts[q] - last)))
            order.append(nxt); left.remove(nxt)
        nw_line = [tuple(round(float(v), 1) for v in pts[q]) for q in order[::4]]

    # 3. creeks along the valleys of the new ground
    hc = N[::COARSE, ::COARSE]
    cs = cell * COARSE
    rec, acc = drainage(hc)
    area = acc * cs * cs / 1e6
    keepc = keep[::COARSE, ::COARSE]
    creeks, ponds = [], []
    for name, (ox, oz) in CREEKS:
        ii, jj = np.nonzero(area >= 0.2)
        if len(ii) == 0:
            continue
        o = min(zip(ii, jj), key=lambda q: math.hypot(UX[0, 0] + q[1] * cs - ox, UZ[0, 0] + q[0] * cs - oz) - area[q] * 1e-3)
        while rec[o][0] >= 0:                                   # follow down to its real outlet
            o = tuple(rec[o])
        path = stem(rec, area, o)
        blocked = [n for n, q in enumerate(path) if keepc[q]]
        if blocked:
            path = path[blocked[-1] + 3:]                       # starts below the last road / thing it crosses
        if len(path) < 20:
            continue
        P = np.array([(UX[0, 0] + q[1] * cs, UZ[0, 0] + q[0] * cs) for q in path])
        # smooth the line a little
        P = np.array([P[max(0, n - 2):n + 3].mean(axis=0) for n in range(len(P))])
        zi = np.clip(np.round((P[:, 1] - UZ[0, 0]) / cell).astype(int), 0, cz - 1)
        zj = np.clip(np.round((P[:, 0] - UX[0, 0]) / cell).astype(int), 0, cx - 1)
        z = N[zi, zj]
        # a falling profile: lower the humps, at most CUT
        run = np.minimum.accumulate(z)
        bed = np.maximum(z - CUT, np.minimum(z, run))
        # water: from the outlet up, never below the water downstream (a dip under it is a pond)
        wl = np.empty_like(bed)
        wl[-1] = bed[-1] + DEPTH
        for n in range(len(bed) - 2, -1, -1):
            wl[n] = max(bed[n] + DEPTH, wl[n + 1])
        # carve the bed and its banks (only lower): the line rasterised densely, distances by EDT
        dense = []
        for (ax, az), (bx, bz), ya, yb in zip(P[:-1], P[1:], bed[:-1], bed[1:]):
            n_sub = max(1, int(math.hypot(bx - ax, bz - az) / (cell * 0.5)))
            for t in np.linspace(0.0, 1.0, n_sub, endpoint=False):
                dense.append((ax + (bx - ax) * t, az + (bz - az) * t, ya + (yb - ya) * t))
        dense = np.array(dense)
        di = np.clip(np.round((dense[:, 1] - UZ[0, 0]) / cell).astype(int), 0, cz - 1)
        dj = np.clip(np.round((dense[:, 0] - UX[0, 0]) / cell).astype(int), 0, cx - 1)
        line_mask = np.zeros(H.shape, bool)
        line_mask[di, dj] = True
        bed_grid = np.full(H.shape, np.nan)
        bed_grid[di, dj] = dense[:, 2]
        dl, (bi, bj) = ndimage.distance_transform_edt(~line_mask, return_indices=True)
        dline = dl * cell
        bnear = bed_grid[bi, bj]
        env = bnear + np.maximum(0.0, dline - BED_HALF) / BANK
        near = dline < 12.0
        N = np.where(near, np.minimum(N, env), N)
        # the ribbon runs DEPTH over the bed (a ribbon at a pond's level would hang over the banks; ponds are their own water)
        pts = [v for (x, zz), y in zip(P, bed + DEPTH) for v in (round(float(x), 2), round(float(zz), 2), round(float(y), 3))]   # flat x, z, y
        creeks.append({'name': name, 'width': 4.0, 'points': pts})
        # ponds: runs of the line where the water stands POND_FROM over the bed
        deep = wl - bed > POND_FROM
        n = 0
        while n < len(deep):
            if not deep[n]:
                n += 1
                continue
            m = n
            while m < len(deep) and deep[m]:
                m += 1
            level = float(wl[n:m].max())
            seed = np.zeros(H.shape, bool)
            seed[zi[n:m], zj[n:m]] = True
            # the whole hollow under the level (a cut circle would leave a water edge in the air), merged with a pond of the
            # same level the line ran through before
            region = ndimage.binary_propagation(seed, mask=N < level - 0.05)
            if region.sum() * cell * cell > POND_MAX:
                print(f'[Snow] streams: a pond of {region.sum() * cell * cell:.0f} m2 at {level:.1f} m on {name} is too big, left dry')
                n = m
                continue
            same = next((q for q in ponds if q['creek'] == name and abs(q['level'] - level) < 0.05), None)
            if same is not None:
                same['mask'] |= region
            else:
                ponds.append({'creek': name, 'level': round(level, 3), 'mask': region})
            n = m

    ponds = [{'name': f"{q['creek']}_{n}", 'level': q['level'], 'cell': round(cell, 4),
              'cells': [v for a, b in zip(*np.nonzero(q['mask'])) for v in (round(float(UX[a, b]), 2), round(float(UZ[a, b]), 2))]}   # flat x, z
             for n, q in enumerate(ponds)]
    views = [('creek_se', (-860.0, 760.0, 70.0), (-700.0, 980.0, -10.0)), ('ravines_east', (-1000.0, 300.0, 80.0), (-900.0, -100.0, 0.0))]
    trees = bpy.data.collections.get('TerrainTrees')
    for o in (trees.objects if trees else []):
        o.hide_render = True
    for nm, loc, at in views:
        render(os.path.join(out, f'{nm}_before.png'), loc, at)
    co[:, 2] = N.ravel().astype(np.float32)
    me.vertices.foreach_set('co', co.ravel())
    me.update()
    for nm, loc, at in views:
        render(os.path.join(out, f'{nm}_after.png'), loc, at)
    for o in (trees.objects if trees else []):
        o.hide_render = False
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, 'map_edit.blend'))
    data = {'note': 'Forest creeks and ponds (tools/blender_context/edits/forest_streams.py, 27.10.2026); Unity x, z, water y. '
                    'clear: lines whose trees and grass go (the north-west river keeps the package water mesh).',
            'creeks': creeks, 'ponds': ponds,
            'clear': ([{'name': 'NW river', 'width': 9.0, 'points': [v for p in nw_line for v in p]}] if nw_line else [])}   # flat x, z
    with open(os.path.join(out, 'ForestStreams.json'), 'w', encoding='utf-8') as f:
        json.dump(data, f)
    ch = N - H
    print(f"[Snow] streams: ravines filled ({int(zone.sum())} vertices), NW river bed lowered, creeks {[(c['name'], len(c['points']) // 3) for c in creeks]}, "
          f"ponds {[(p['name'], len(p['cells']) // 2, p['level']) for p in ponds]}; ground up {ch.max():.1f} / down {-ch.min():.1f} m")


main()

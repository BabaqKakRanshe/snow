"""Snow: carry ground edits made in a map .blend back to Unity (docs/gdd/WORLD_SYNC_CONTRACT.md).

Headless:  blender -b map.blend --python export_ground.py -- [out_dir]
In Blender: Scripting -> open this file -> Run Script (writes next to map.json).

The 'Ground' mesh of build_map.py has a vertex on every grid_step-th sample of ground.r16. Its heights now minus the
heights it was built from give a delta on that coarse grid; the delta is spread bilinearly to every sample, so the fine
relief of the export stays and only the edit is added. Writes ground_delta.f32 (float32 metres, rows south -> north,
0 = untouched) + ground_delta.json (grid, changed samples, their box). Unity: Tools / Snow / World Sync / Import Ground
From Blender. Vertices must only move up or down (z); holes stay as they are.
"""
import json
import os
import sys

import bpy
import numpy as np


def main():
    rest = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ob = bpy.data.objects.get('Ground')
    if ob is None or 'grid_step' not in ob:
        raise SystemExit('[Snow] export_ground: no Ground mesh from build_map.py in this file')
    mpath = ob['map_json'] if os.path.exists(ob['map_json']) else os.path.join(os.path.dirname(bpy.data.filepath), 'map.json')
    base = os.path.dirname(mpath)
    out = rest[0] if rest else base
    with open(mpath, encoding='utf-8') as f:
        g = json.load(f)['ground']
    nx, nz, step = g['nx'], g['nz'], int(ob['grid_step'])
    raw = np.fromfile(os.path.join(base, g['file']), dtype='<u2').reshape(nz, nx)
    h0 = g['hmin'] + raw.astype(np.float64) / 65535.0 * (g['hmax'] - g['hmin'])
    ix = np.arange(0, nx, step); iz = np.arange(0, nz, step)
    if ix[-1] != nx - 1: ix = np.append(ix, nx - 1)
    if iz[-1] != nz - 1: iz = np.append(iz, nz - 1)
    me = ob.data
    if len(me.vertices) != len(ix) * len(iz):
        raise SystemExit(f'[Snow] export_ground: Ground has {len(me.vertices)} vertices, the grid needs {len(ix) * len(iz)} (vertices added or removed?)')
    co = np.zeros(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    zb = co.reshape(len(iz), len(ix), 3)[:, :, 2].astype(np.float64)     # Blender z = Unity y; object at the origin
    if any(abs(v) > 1e-4 for v in ob.location) or any(abs(v - 1) > 1e-4 for v in ob.scale):
        raise SystemExit('[Snow] export_ground: the Ground object was moved or scaled; edit its vertices only')
    coarse = zb - h0[np.ix_(iz, ix)]
    coarse[np.abs(coarse) < 0.005] = 0.0
    # bilinear spread: rows first, then columns
    rows = np.empty((len(iz), nx))
    xs = np.arange(nx)
    for k in range(len(iz)):
        rows[k] = np.interp(xs, ix, coarse[k])
    delta = np.empty((nz, nx))
    zs = np.arange(nz)
    for x in range(nx):
        delta[:, x] = np.interp(zs, iz, rows[:, x])
    delta = delta.astype(np.float32)
    changed = np.abs(delta) >= 0.005
    zz, xx = np.nonzero(changed)
    info = {
        'nx': nx, 'nz': nz, 'x0': g['x0'], 'z0': g['z0'], 'step': g['step'], 'file': 'ground_delta.f32',
        'blend': bpy.data.filepath, 'changed': int(changed.sum()),
        'base': os.path.join(base, g['file']), 'hmin': g['hmin'], 'hmax': g['hmax'],      # Unity checks the tiles still match it
        'raised_max': float(delta.max()), 'lowered_max': float(-delta.min()),
        'box': [float(g['x0'] + xx.min() * g['step']), float(g['z0'] + zz.min() * g['step']),
                float(g['x0'] + xx.max() * g['step']), float(g['z0'] + zz.max() * g['step'])] if len(xx) else None,
    }
    os.makedirs(out, exist_ok=True)
    delta.tofile(os.path.join(out, 'ground_delta.f32'))
    with open(os.path.join(out, 'ground_delta.json'), 'w', encoding='utf-8') as f:
        json.dump(info, f, indent=1)
    print(f"[Snow] export_ground: {info['changed']} samples changed (up to +{info['raised_max']:.1f} / -{info['lowered_max']:.1f} m), box {info['box']} -> {out}")


main()

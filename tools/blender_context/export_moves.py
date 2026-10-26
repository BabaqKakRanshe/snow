"""Snow: carry object moves made in a context .blend back to Unity (docs/gdd/WORLD_SYNC_CONTRACT.md).

Headless:  blender -b context.blend --python export_moves.py -- [out.json]
In Blender: Scripting -> open this file -> Run Script (writes next to the .blend).

Every root Empty that build_context.py made for an object with a sync id carries 'world_id' and 'matrix_at_build'. An Empty
whose matrix differs from that (moved or turned; scale is not carried) becomes a move in moves_from_blender.json, in Unity
axes: Unity matrix = C^T * Blender matrix * C, C = (x, y, z) -> (-x, -z, y). Unity: Tools / Snow / World Sync / Import Moves
From Blender. Move the whole object by its Empty (ARROWS), not its meshes.
"""
import json
import os
import sys

import bpy
from mathutils import Matrix

C = Matrix(((-1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
EPS, EPS_ROT = 0.01, 0.0015          # m; matrix entries (~0.1 degree)


def unity_pose(mb):
    mu = C.transposed() @ mb @ C
    loc, rot, _ = mu.decompose()
    return [round(v, 4) for v in loc], [round(rot.x, 6), round(rot.y, 6), round(rot.z, 6), round(rot.w, 6)]


def flat_to_matrix(a):
    return Matrix([a[0:4], a[4:8], a[8:12], a[12:16]])


def moved(now, then):
    if (now.translation - then.translation).length > EPS:
        return True
    r1, r2 = now.to_3x3().normalized(), then.to_3x3().normalized()
    return any(abs(r1[i][j] - r2[i][j]) > EPS_ROT for i in range(3) for j in range(3))


def main():
    rest = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    blend = bpy.data.filepath
    out = rest[0] if rest else os.path.join(os.path.dirname(blend) or '.', 'moves_from_blender.json')
    moves, seen = [], 0
    for ob in bpy.data.objects:
        if 'world_id' not in ob or 'matrix_at_build' not in ob:
            continue
        seen += 1
        then = flat_to_matrix(list(ob['matrix_at_build']))
        now = ob.matrix_world.copy()
        if not moved(now, then):
            continue
        p, q = unity_pose(now)
        fp, fq = unity_pose(then)
        moves.append({'id': ob['world_id'], 'p': p, 'q': q, 'fromP': fp, 'fromQ': fq})
    with open(out, 'w', encoding='utf-8') as f:
        json.dump({'blend': blend, 'moves': moves}, f, indent=1)
    print(f'[Snow] export_moves: {seen} objects with ids, {len(moves)} moved -> {out}')


main()

"""Snow: build a Blender scene from a Blender Context export (Unity: Tools / Snow / Blender Context).

Headless:  blender -b --factory-startup --python build_context.py -- context.json context.blend
In Blender: Scripting -> open this file (it sits next to context.json) -> Run Script: builds into the current scene.

Every placed Unity object becomes an Empty (its root, at the Unity root's matrix) with its meshes as children, so moving
the Empty moves the whole object. Custom properties on roots and meshes: unity_path, prefab, fbx, sources (where the
source model may be in source/), unity_position / unity_euler on roots. Meshes are shared between instances (linked
data). Axes: Blender (x, y, z) = Unity (-x, -z, y); the export already wrote everything in Blender axes and world
coordinates, nothing is moved to the origin.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector


def args():
    if '--' in sys.argv:
        rest = sys.argv[sys.argv.index('--') + 1:]
        return rest[0], (rest[1] if len(rest) > 1 else None)
    here = None
    try:
        here = os.path.dirname(bpy.context.space_data.text.filepath)
    except Exception:
        pass
    if not here:
        here = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(here, 'context.json'), None


def collection(name, parent):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        parent.children.link(c)
    return c


def import_obj(path):
    """One OBJ (already in Blender axes) -> its mesh data; the importer's object is removed."""
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=path, forward_axis='Y', up_axis='Z')
    new = [o for o in bpy.data.objects if o not in before]
    mesh = None
    for o in new:
        if o.type == 'MESH' and mesh is None:
            mesh = o.data
        bpy.data.objects.remove(o, do_unlink=True)
    return mesh


def pixel_textures():
    """The game's textures are pixel art: no filtering."""
    for m in bpy.data.materials:
        if m.node_tree is None:
            continue
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE':
                n.interpolation = 'Closest'


def mat4(a):
    return Matrix([a[0:4], a[4:8], a[8:12], a[12:16]])


def main():
    ctx_path, out = args()
    base = os.path.dirname(os.path.abspath(ctx_path))
    with open(ctx_path, encoding='utf-8') as f:
        ctx = json.load(f)

    if out:  # headless: start from an empty scene
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    scene = bpy.context.scene
    top = collection('Context ' + ctx['name'], scene.collection)

    meshes = {}
    for m in ctx['meshes']:
        data = import_obj(os.path.join(base, m['obj']))
        if data is None:
            print('[context] mesh failed:', m['obj'])
            continue
        data.name = m['id']
        data['fbx'] = m['fbx']
        meshes[m['id']] = (data, m)
    pixel_textures()

    roots = {}
    for r in ctx['roots']:
        col = collection(r['group'], top)
        e = bpy.data.objects.new(r['name'], None)
        e.empty_display_type = 'ARROWS'
        e.empty_display_size = 1.0
        e.matrix_world = mat4(r['matrix'])
        e['unity_path'] = r['path']
        e['prefab'] = r['prefab']
        e['fbx'] = r['fbx']
        e['sources'] = '\n'.join(r['sources'])
        e['unity_position'] = r['unityPosition']
        e['unity_euler'] = r['unityEuler']
        if r.get('worldId'):
            e['world_id'] = r['worldId']                    # WORLD_SYNC_CONTRACT: export_moves.py carries moves back
            e['matrix_at_build'] = [v for row in e.matrix_world for v in row]
        col.objects.link(e)
        e['context_id'] = r['id']
        roots[r['id']] = (e, r)

    placed = 0
    for i in ctx['instances']:
        if i['mesh'] not in meshes:
            continue
        data, m = meshes[i['mesh']]
        ob = bpy.data.objects.new(i['name'], data)
        root = roots.get(i['root'])
        col = collection(i['group'], top)
        col.objects.link(ob)
        if root is not None:
            ob.parent = root[0]
            ob['sources'] = root[0]['sources']
        ob.matrix_world = mat4(i['matrix'])
        ob['unity_path'] = i['path']
        ob['prefab'] = i['prefab']
        ob['fbx'] = m['fbx']
        placed += 1

    trees = collection('TerrainTrees', top)
    for i in ctx.get('trees', []):
        if i['mesh'] not in meshes:
            continue
        data, m = meshes[i['mesh']]
        ob = bpy.data.objects.new(i['name'], data)
        trees.objects.link(ob)
        ob.matrix_world = mat4(i['matrix'])
        ob['prefab'] = i['prefab']
        ob.hide_select = True

    ground = None
    if ctx.get('terrain'):
        data = import_obj(os.path.join(base, ctx['terrain']['obj']))
        if data is not None:
            data.name = 'Terrain'
            ground = bpy.data.objects.new('Terrain', data)
            collection('Terrain', top).objects.link(ground)
            ground.hide_select = True
            pixel_textures()

    lights = collection('Lights', top)
    for l in ctx.get('lights', []):
        kind = 'SPOT' if l['type'] == 'Spot' else 'POINT'
        ld = bpy.data.lights.new(l['name'], kind)
        ld.color = l['color']
        ld.energy = max(1.0, l['intensity'] * 60.0)
        if kind == 'SPOT':
            ld.spot_size = math.radians(max(1.0, l['spotAngle']))
            ld.spot_blend = 0.4
        ob = bpy.data.objects.new(l['name'], ld)
        mw = mat4(l['matrix'])
        d = (mw.to_3x3() @ Vector((0.0, -1.0, 0.0))).normalized()   # Unity's light forward (+z) in Blender axes
        ob.location = mw.translation
        ob.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        ob['unity_path'] = l['path']
        ob['powered'] = l['powered']
        lights.objects.link(ob)

    # cameras: from above and from the north-west, like shots/top.png and shots/view_nw.png
    cams = collection('Cameras', top)
    if ground is not None:
        bb = [ground.matrix_world @ Vector(c) for c in ground.bound_box]
        lo = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
        hi = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
        mid = (lo + hi) * 0.5
        size = max(hi.x - lo.x, hi.y - lo.y)
        cd = bpy.data.cameras.new('Cam_Top')
        cd.type = 'ORTHO'
        cd.ortho_scale = size
        cd.clip_end = size * 4 + 200
        cam = bpy.data.objects.new('Cam_Top', cd)
        cam.location = (mid.x, mid.y, hi.z + size)
        cam.rotation_euler = (0.0, 0.0, 0.0)
        cams.objects.link(cam)
        scene.camera = cam
        cd2 = bpy.data.cameras.new('Cam_View')
        cd2.clip_end = size * 4 + 200
        cam2 = bpy.data.objects.new('Cam_View', cd2)
        cam2.location = (lo.x, hi.y, hi.z + size * 0.35)
        cam2.rotation_euler = (mid - cam2.location).to_track_quat('-Z', 'Y').to_euler()
        cams.objects.link(cam2)

    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type != 'VIEW_3D':
                continue
            for space in area.spaces:
                if space.type == 'VIEW_3D':
                    space.shading.type = 'SOLID'
                    space.shading.color_type = 'TEXTURE'
                    space.clip_end = 5000.0

    print(f"[context] {ctx['name']}: {len(meshes)} meshes, {len(roots)} roots, {placed} instances, "
          f"{len(ctx.get('trees', []))} tree parts, {len(ctx.get('lights', []))} lights, terrain {'yes' if ground else 'no'}")
    if out:
        bpy.ops.wm.save_as_mainfile(filepath=out)
        print('[context] saved', out)


main()

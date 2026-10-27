"""Snow: build the whole location in Blender from a map set (Unity: Tools / Snow / World Sync / Export Whole Map).

Headless:  blender -b --factory-startup --python build_map.py -- map.json map.blend [--ground-step 2] [--no-trees]
In Blender: Scripting -> open this file (it sits next to map.json) -> Run Script: builds into the current scene.

The set (docs/gdd/WORLD_SYNC_CONTRACT.md): the ground as raw heights (ground.r16) + holes (holes.u8) + the paint
picture (paint.png); every mesh once (meshes.bin); placed objects as root Empties (ARROWS; world_id + matrix_at_build
on the ones with a sync id, so export_moves.py carries their moves back) with their meshes as children sharing mesh
data; Terrain trees as points instanced by Geometry Nodes (one point cloud per tree model). Axes: Blender (x, y, z) =
Unity (-x, -z, y); the export wrote meshes and matrices in Blender axes already, ground and trees are converted here.
"""
import json
import math
import os
import sys

import bpy
import numpy as np


def parse_args():
    rest = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    here = None
    if not rest:
        try:
            here = os.path.dirname(bpy.context.space_data.text.filepath)
        except Exception:
            here = os.path.dirname(os.path.abspath(__file__))
    opts = {'map': rest[0] if rest else os.path.join(here, 'map.json'), 'out': None, 'step': 2, 'trees': True}
    pos = [a for a in rest[1:] if not a.startswith('--')]
    if pos:
        opts['out'] = pos[0]
    if '--ground-step' in rest:
        opts['step'] = max(1, int(rest[rest.index('--ground-step') + 1]))
    if '--no-trees' in rest:
        opts['trees'] = False
    return opts


def collection(name, parent):
    c = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if c.name not in parent.children:
        parent.children.link(c)
    return c


def mat4(a):
    from mathutils import Matrix
    return Matrix([a[0:4], a[4:8], a[8:12], a[12:16]])


# ---------------------------------------------------------------------------------------------------------- materials

def build_materials(m, base):
    mats = {}
    for d in m.get('materials', []):
        mat = bpy.data.materials.new(d['name'])
        mat.use_nodes = True
        bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        c = d.get('color') or [1, 1, 1, 1]
        bsdf.inputs['Base Color'].default_value = (c[0], c[1], c[2], 1.0)
        tex = d.get('texture')
        if tex and os.path.exists(os.path.join(base, tex)):
            node = mat.node_tree.nodes.new('ShaderNodeTexImage')
            node.image = bpy.data.images.load(os.path.join(base, tex), check_existing=True)
            node.interpolation = 'Closest'
            mat.node_tree.links.new(node.outputs['Color'], bsdf.inputs['Base Color'])
            if d.get('cutout'):
                mat.node_tree.links.new(node.outputs['Alpha'], bsdf.inputs['Alpha'])
        mat.diffuse_color = (c[0], c[1], c[2], 1.0)
        mats[d['name']] = mat
    return mats


# ---------------------------------------------------------------------------------------------------------- meshes

def build_meshes(m, base, mats):
    """meshes.bin: per mesh float32 positions (v*3, Blender axes), float32 uv (v*2) if any, then per submesh uint32
    triangle indices (tris*3, winding already for Blender)."""
    out = {}
    spec = m.get('meshes') or {}
    path = os.path.join(base, spec.get('file', 'meshes.bin'))
    if not os.path.exists(path):
        return out
    blob = np.fromfile(path, dtype=np.uint8)
    for d in spec.get('list', []):
        off, nv = d['offset'], d['v']
        pos = np.frombuffer(blob, np.float32, nv * 3, off); off += nv * 12
        uv = None
        if d.get('uv'):
            uv = np.frombuffer(blob, np.float32, nv * 2, off).reshape(-1, 2); off += nv * 8
        idx, mi = [], []
        for s, nt in enumerate(d['subs']):
            idx.append(np.frombuffer(blob, np.uint32, nt * 3, off)); off += nt * 12
            mi.append(np.full(nt, s, np.int32))
        idx = np.concatenate(idx) if idx else np.zeros(0, np.uint32)
        mi = np.concatenate(mi) if mi else np.zeros(0, np.int32)
        nt = len(idx) // 3
        me = bpy.data.meshes.new(d['id'])
        me.vertices.add(nv)
        me.vertices.foreach_set('co', pos)
        me.loops.add(nt * 3)
        me.loops.foreach_set('vertex_index', idx.astype(np.int32))
        me.polygons.add(nt)
        me.polygons.foreach_set('loop_start', np.arange(0, nt * 3, 3, dtype=np.int32))
        me.polygons.foreach_set('material_index', mi)
        if uv is not None:
            layer = me.uv_layers.new(name='UVMap')
            layer.data.foreach_set('uv', uv[idx].ravel())
        for name in d.get('materials', []):
            me.materials.append(mats.get(name))
        me.update()
        out[d['id']] = me
    return out


# ---------------------------------------------------------------------------------------------------------- ground

def build_ground(m, base, step, top, map_path):
    g = m['ground']
    nx, nz = g['nx'], g['nz']
    raw = np.fromfile(os.path.join(base, g['file']), dtype='<u2').reshape(nz, nx)
    h = g['hmin'] + raw.astype(np.float32) / 65535.0 * (g['hmax'] - g['hmin'])
    holes = None
    if g.get('holes') and os.path.exists(os.path.join(base, g['holes'])):
        holes = np.fromfile(os.path.join(base, g['holes']), dtype=np.uint8).reshape(nz - 1, nx - 1)
    ix = np.arange(0, nx, step); iz = np.arange(0, nz, step)
    if ix[-1] != nx - 1: ix = np.append(ix, nx - 1)
    if iz[-1] != nz - 1: iz = np.append(iz, nz - 1)
    ux = g['x0'] + ix * g['step']; uz = g['z0'] + iz * g['step']
    X, Z = np.meshgrid(ux, uz)
    Y = h[np.ix_(iz, ix)]
    co = np.stack([-X, -Z, Y], axis=-1).reshape(-1, 3).astype(np.float32)        # Blender = (-x, -z, y)
    cx, cz = len(ix), len(iz)
    a = (np.arange(cz - 1)[:, None] * cx + np.arange(cx - 1)[None, :]).ravel()
    quads = np.stack([a, a + 1, a + cx + 1, a + cx], axis=1)                     # mirrored axes: this order faces up
    if holes is not None:
        # a quad is a hole when its first source cell is one
        keep = holes[np.ix_(iz[:-1], ix[:-1])].ravel() > 0
        quads = quads[keep]
    nq = len(quads)
    me = bpy.data.meshes.new('Ground')
    me.vertices.add(len(co))
    me.vertices.foreach_set('co', co.ravel())
    me.loops.add(nq * 4)
    me.loops.foreach_set('vertex_index', quads.ravel().astype(np.int32))
    me.polygons.add(nq)
    me.polygons.foreach_set('loop_start', np.arange(0, nq * 4, 4, dtype=np.int32))
    p = m.get('paint')
    if p:
        bx = m['box']
        U = ((X - bx[0]) / (bx[2] - bx[0])).ravel(); V = ((Z - bx[1]) / (bx[3] - bx[1])).ravel()
        uv = np.stack([U, V], axis=1).astype(np.float32)
        layer = me.uv_layers.new(name='UVMap')
        layer.data.foreach_set('uv', uv[quads.ravel()].ravel())
        mat = bpy.data.materials.new('Ground_Paint')
        mat.use_nodes = True
        bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        node = mat.node_tree.nodes.new('ShaderNodeTexImage')
        node.image = bpy.data.images.load(os.path.join(base, p['file']), check_existing=True)
        node.interpolation = 'Closest'
        mat.node_tree.links.new(node.outputs['Color'], bsdf.inputs['Base Color'])
        me.materials.append(mat)
    me.update()
    ob = bpy.data.objects.new('Ground', me)
    ob['ground_step_m'] = float(g['step'] * step)
    ob['grid_step'] = int(step)                         # export_ground.py: this mesh's vertices are every step-th sample
    ob['map_json'] = os.path.abspath(map_path)               # the set this ground came from (export_ground.py)
    ob.hide_select = True
    collection('Ground', top).objects.link(ob)
    return ob, nq


# ---------------------------------------------------------------------------------------------------------- objects

def build_objects(m, meshes, top):
    roots = {}
    for r in m.get('roots', []):
        e = bpy.data.objects.new(r['name'], None)
        e.empty_display_type = 'ARROWS'
        e.empty_display_size = 1.0
        e.matrix_world = mat4(r['matrix'])
        e['unity_path'] = r.get('path', '')
        e['prefab'] = r.get('prefab', '')
        if r.get('worldId'):
            e['world_id'] = r['worldId']                     # WORLD_SYNC_CONTRACT: export_moves.py carries moves back
            e['matrix_at_build'] = [v for row in e.matrix_world for v in row]
        collection(r.get('group') or 'Other', top).objects.link(e)
        roots[r['key']] = e
    placed = 0
    for i in m.get('instances', []):
        me = meshes.get(i['mesh'])
        if me is None:
            continue
        root = roots.get(i['root'])
        ob = bpy.data.objects.new(i.get('name') or me.name, me)
        (root.users_collection[0] if root else collection('Other', top)).objects.link(ob)
        if root is not None:
            ob.parent = root
        ob.matrix_world = mat4(i['matrix'])
        placed += 1
    return roots, placed


# ---------------------------------------------------------------------------------------------------------- trees

def tree_node_group(name, coll):
    ng = bpy.data.node_groups.new(name, 'GeometryNodeTree')
    ng.interface.new_socket('Geometry', in_out='INPUT', socket_type='NodeSocketGeometry')
    ng.interface.new_socket('Geometry', in_out='OUTPUT', socket_type='NodeSocketGeometry')
    n = ng.nodes
    gi, go = n.new('NodeGroupInput'), n.new('NodeGroupOutput')
    inst = n.new('GeometryNodeInstanceOnPoints')
    ci = n.new('GeometryNodeCollectionInfo')
    ci.inputs['Collection'].default_value = coll
    ci.inputs['Reset Children'].default_value = True
    rot = n.new('GeometryNodeInputNamedAttribute'); rot.data_type = 'FLOAT_VECTOR'; rot.inputs['Name'].default_value = 'rot'
    scl = n.new('GeometryNodeInputNamedAttribute'); scl.data_type = 'FLOAT_VECTOR'; scl.inputs['Name'].default_value = 'scl'
    L = ng.links
    L.new(gi.outputs['Geometry'], inst.inputs['Points'])
    L.new(ci.outputs[0], inst.inputs['Instance'])
    L.new(rot.outputs['Attribute'], inst.inputs['Rotation'])
    L.new(scl.outputs['Attribute'], inst.inputs['Scale'])
    L.new(inst.outputs['Instances'], go.inputs['Geometry'])
    return ng


def build_trees(m, base, meshes, top):
    """trees.bin: float32 x 7 per tree: prototype, Unity x, y, z, yaw (rad), width scale, height scale. Unity yaw about
    +y is a turn of -yaw about Blender +z (C R_y C^T)."""
    t = m.get('trees')
    if not t or not os.path.exists(os.path.join(base, t['file'])):
        return 0
    a = np.fromfile(os.path.join(base, t['file']), dtype=np.float32).reshape(-1, 7)
    col = collection('TerrainTrees', top)
    count = 0
    for k, proto in enumerate(t['prototypes']):
        rows = a[a[:, 0] == k]
        if len(rows) == 0:
            continue
        src = bpy.data.collections.new('Tree_' + proto['name'])          # not linked to the scene: only instanced
        for part in proto['parts']:
            me = meshes.get(part['mesh'])
            if me is None:
                continue
            ob = bpy.data.objects.new(proto['name'] + '_part', me)
            ob.matrix_world = mat4(part['matrix'])
            src.objects.link(ob)
        pts = bpy.data.meshes.new('TreePoints_' + proto['name'])
        pts.vertices.add(len(rows))
        pts.vertices.foreach_set('co', np.stack([-rows[:, 1], -rows[:, 3], rows[:, 2]], 1).astype(np.float32).ravel())
        rot = pts.attributes.new('rot', 'FLOAT_VECTOR', 'POINT')
        rot.data.foreach_set('vector', np.stack([np.zeros(len(rows)), np.zeros(len(rows)), -rows[:, 4]], 1).astype(np.float32).ravel())
        scl = pts.attributes.new('scl', 'FLOAT_VECTOR', 'POINT')
        scl.data.foreach_set('vector', np.stack([rows[:, 5], rows[:, 5], rows[:, 6]], 1).astype(np.float32).ravel())
        ob = bpy.data.objects.new('Trees_' + proto['name'], pts)
        mod = ob.modifiers.new('Trees', 'NODES')
        mod.node_group = tree_node_group('SnowTrees_' + proto['name'], src)
        ob['prefab'] = proto.get('prefab', '')
        ob.hide_select = True
        col.objects.link(ob)
        count += len(rows)
    return count


# ---------------------------------------------------------------------------------------------------------- lights

def build_lights(m, top):
    col = collection('Lights', top)
    for l in m.get('lights', []):
        kind = {'Spot': 'SPOT', 'Point': 'POINT', 'Area': 'AREA'}.get(l.get('type'), 'POINT')
        ld = bpy.data.lights.new(l['name'], kind)
        ld.color = l.get('color', [1, 1, 1])[:3]
        ld.energy = float(l.get('intensity', 1.0)) * 50.0
        if kind == 'SPOT':
            ld.spot_size = math.radians(float(l.get('spotAngle', 45.0)))
        ob = bpy.data.objects.new(l['name'], ld)
        ob.matrix_world = mat4(l['matrix'])
        ob['unity_path'] = l.get('path', '')
        ob.hide_viewport = not l.get('enabled', True)
        col.objects.link(ob)


def main():
    o = parse_args()
    base = os.path.dirname(os.path.abspath(o['map']))
    with open(o['map'], encoding='utf-8') as f:
        m = json.load(f)
    scene = bpy.context.scene
    top = collection(m.get('location', 'Map'), scene.collection)
    mats = build_materials(m, base)
    meshes = build_meshes(m, base, mats)
    ground, quads = build_ground(m, base, o['step'], top, o['map'])
    roots, placed = build_objects(m, meshes, top)
    trees = build_trees(m, base, meshes, top) if o['trees'] else 0
    build_lights(m, top)
    scene.unit_settings.system = 'METRIC'
    for area in getattr(bpy.context.screen, 'areas', []) if bpy.context.screen else []:
        if area.type == 'VIEW_3D':
            area.spaces[0].clip_end = 10000.0
    print(f"[Snow] build_map: ground {quads} quads (step {o['step']}), {len(meshes)} meshes, {len(roots)} objects, "
          f"{placed} mesh instances, {trees} trees, {len(m.get('lights', []))} lights")
    if o['out']:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(o['out']))
        print('[Snow] build_map: saved', o['out'])


main()

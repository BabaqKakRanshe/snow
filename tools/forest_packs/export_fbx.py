"""A Snow pack's .blend -> one FBX per root for Unity, the steps of tools/forest_start/export_fbx.py: forward -Z, up Y,
Apply Transform; each root at the origin (its display_offset or map place dropped); COL_, HIT_ and LIGHT_ left out (colliders
and lights come from the contract; COLM_ meshes stay: Unity turns them into MeshColliders); every deeper mesh lifted to a
direct child of the root (the exporter's Apply Transform is exact for one level only) - the report keeps its parent so Unity
puts it back under it. Linked library objects and instance empties are not exported. The .blend is not saved.

blender -b --factory-startup <pack>.blend --python tools/forest_packs/export_fbx.py -- <out_dir> <report.json> [Root,Root,...]

Without a root list every top-level local mesh is a root. The report (json) per root: objects, their parents before the
lift, triangles (without COLM_), materials, and each object's pose in its root's frame (Blender axes) - make_unity_data.py
reads it.
"""
import bpy, sys, os, json
from mathutils import Matrix

SKIP_PREFIX = ('COL_', 'HIT_', 'LIGHT_')


def exported_parent(o, root, keep):
    """The nearest ancestor that is exported (a mesh kept in the FBX) or the root."""
    p = o.parent
    while p is not None and p != root and p not in keep:
        p = p.parent
    return p.name if p is not None else root.name


def export_root(root, out_dir):
    root.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    meshes = [o for o in root.children_recursive if o.type == 'MESH' and o.library is None and not o.name.startswith(SKIP_PREFIX)]
    keep_set = set(meshes)
    parents, poses = {}, {}
    for o in meshes:
        parents[o.name] = exported_parent(o, root, keep_set)
        m = o.matrix_world
        poses[o.name] = {'pos': [round(v, 5) for v in m.translation], 'rot_euler': [round(v, 5) for v in m.to_euler()]}
    log = []
    for o in meshes:
        mw = o.matrix_world.copy()
        if o.parent != root:
            log.append(f'{o.name}: lifted from {o.parent.name}')
            o.parent = root
        o.matrix_parent_inverse = Matrix.Identity(4)
        o.matrix_basis = mw
    bpy.context.view_layer.update()
    keep = [root] + meshes
    bpy.ops.object.select_all(action='DESELECT')
    for o in keep:
        o.hide_set(False)
        o.hide_viewport = False
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    fp = os.path.join(out_dir, root.name + '.fbx')
    bpy.ops.export_scene.fbx(filepath=fp, use_selection=True, use_visible=False, object_types={'MESH'},
                             global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type='FACE', use_subsurf=False,
                             use_mesh_edges=False, use_tspace=False, use_triangles=False, use_custom_props=False,
                             colors_type='NONE', add_leaf_bones=False, bake_anim=False,
                             path_mode='STRIP', embed_textures=False, batch_mode='OFF')
    seen = [o for o in keep if not o.name.startswith('COLM_')]
    mats = sorted({s.material.name for o in keep for s in o.material_slots if s.material})
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in seen)
    print(f'[export] {root.name}: {len(keep)} objects, {tris} tris, materials {mats}', flush=True)
    return {'file': os.path.basename(fp), 'objects': [o.name for o in keep], 'parents': parents, 'poses': poses,
            'materials': mats, 'tris': tris, 'log': log}


def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    out_dir, report = argv[0], argv[1]
    os.makedirs(out_dir, exist_ok=True)
    for c in bpy.data.collections:
        c.hide_viewport = False
    for lc in bpy.context.view_layer.layer_collection.children:
        lc.exclude = False
        lc.hide_viewport = False
    if len(argv) > 2 and argv[2]:
        names = argv[2].split(',')
    else:
        names = sorted(o.name for o in bpy.data.objects
                       if o.parent is None and o.type == 'MESH' and o.library is None and not o.name.startswith(SKIP_PREFIX + ('COLM_',)))
    rep = {n: export_root(bpy.data.objects[n], out_dir) for n in names}
    os.makedirs(os.path.dirname(os.path.abspath(report)), exist_ok=True)
    json.dump(rep, open(report, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    print(f'[export] {len(rep)} roots -> {out_dir}; report {report}', flush=True)


main()

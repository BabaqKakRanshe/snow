"""forest_airfield 1.0 (source/assets/forest_airfield/af.blend) -> FBX for Unity, the same steps as
tools/forest_start/export_fbx.py (forward -Z, up Y, Apply Transform; per root at the origin, COL_ and LIGHT_ left out, deeper
meshes lifted to direct children of the root; Lens_* and Glow_* stay separate objects). The .blend is not saved.

blender -b --factory-startup af.blend --python export_fbx.py -- <out_dir>
"""
import bpy, sys, os, json
from mathutils import Matrix

ROOTS = ['RWY_Slab', 'RWY_SlabWorn', 'RWY_Threshold', 'RWY_Num17', 'RWY_Num35', 'AF_EdgeLight', 'AF_ThresholdLight',
         'AF_EdgeMarker', 'AF_Sign', 'AF_HangarPlug']
SKIP_PREFIX = ('COL_', 'LIGHT_')
JOIN_INTO = {}


def export_root(root, out_dir):
    root.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    meshes = [o for o in root.children_recursive if o.type == 'MESH' and not o.name.startswith(SKIP_PREFIX)]
    log = []
    for name, into in JOIN_INTO.items():
        a, b = bpy.data.objects.get(name), bpy.data.objects.get(into)
        if a in meshes and b in meshes:
            bpy.ops.object.select_all(action='DESELECT')
            for o in (a, b):
                o.hide_set(False)
                o.select_set(True)
            bpy.context.view_layer.objects.active = b
            bpy.ops.object.join()
            meshes.remove(a)
            log.append(f'{into} <- {name}')
    for o in meshes:
        if o.parent != root:
            mw = o.matrix_world.copy()
            log.append(f'{o.name}: lifted from {o.parent.name}')
            o.parent = root
            o.matrix_parent_inverse = Matrix.Identity(4)
            o.matrix_basis = mw
        else:
            mw = o.matrix_world.copy()
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
    mats = sorted({s.material.name for o in keep for s in o.material_slots if s.material})
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in keep)
    pivots = {o.name: [round(c, 4) for c in o.matrix_world.translation] for o in keep}
    print(f'[export] {root.name}: {len(keep)} objects, {tris} tris, materials {mats}', flush=True)
    return {'file': os.path.basename(fp), 'objects': [o.name for o in keep], 'materials': mats, 'tris': tris,
            'log': log, 'pivots_blender': pivots}


def main():
    out_dir = sys.argv[sys.argv.index('--') + 1]
    os.makedirs(out_dir, exist_ok=True)
    for c in bpy.data.collections:
        c.hide_viewport = False
    for lc in bpy.context.view_layer.layer_collection.children:
        lc.exclude = False
        lc.hide_viewport = False
    rep = {n: export_root(bpy.data.objects[n], out_dir) for n in ROOTS}
    print('[export] report ' + json.dumps(rep, ensure_ascii=False), flush=True)


main()

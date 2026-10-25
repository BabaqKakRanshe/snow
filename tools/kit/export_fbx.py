"""KPP 2.1 / snow_kit 1.1 / main 1.2 -> FBX for Unity, with the settings of Forest_Update7_src/scripts/u4_export.py
(forward -Z, up Y, Apply Transform; the root and its mesh children; COL_, Glow_, LIGHT_ and the kit instance empties stay out;
grandchildren joined into the direct child, as u4 does: the exporter's Apply Transform is exact for one level only).

blender -b --factory-startup <file.blend> --python export_fbx.py -- <kind> <out_dir>
  kind: kit  -> one FBX per Kit_* collection root (Kit_<name>.fbx)
        kpp  -> Checkpoint, Barrier, Gate, Barrier_Manual
        main -> BaseMain
"""
import bpy, sys, os, json
from mathutils import Matrix

SKIP_PREFIX = ('COL_', 'Glow_', 'LIGHT_')
# kpp: glass as in u4 (M_GlassClear); the lamp lenses keep their own materials and start off: the KPP script swaps them
# with the booth's power (not the base grid, not the always-on flicker of u4's M_Lamp_FluorFlicker)
REMAP = {
    'kpp': {'M_Kpp_Glass': 'M_GlassClear', 'M_Kpp_Lamp_Fluor': 'M_Kpp_Lamp_Fluor_Off', 'M_Kpp_Lamp_Bulk': 'M_Kpp_Lamp_Bulk_Off'},
    'kit': {}, 'main': {},
}
ROOTS = {'kpp': ['Checkpoint', 'Barrier', 'Gate', 'Barrier_Manual'], 'main': ['BaseMain']}


def subtree(root):
    keep, skipped = [root], []
    for o in root.children_recursive:
        if o.type != 'MESH' or o.name.startswith(SKIP_PREFIX):
            skipped.append(o)
        else:
            keep.append(o)
    ks = set(keep)
    for o in keep[1:]:
        p = o.parent
        while p is not None and p != root:
            assert p in ks, f'{o.name}: parent {p.name} is not exported'
            p = p.parent
    return keep, skipped


def clean_parenting(root, objs):
    depth = lambda o: 0 if o == root else 1 + depth(o.parent)
    for o in sorted(objs, key=depth):
        if o == root:
            continue
        mw = o.matrix_world.copy()
        o.matrix_parent_inverse = Matrix.Identity(4)
        o.matrix_basis = o.parent.matrix_world.inverted() @ mw
    bpy.context.view_layer.update()


def join_nested(root, keep, log):
    keep_set = set(keep)
    out = [root]
    for d in [o for o in root.children if o in keep_set]:
        desc = [o for o in d.children_recursive if o in keep_set]
        if desc:
            bpy.ops.object.select_all(action='DESELECT')
            for o in [d] + desc:
                o.hide_set(False)
                o.hide_viewport = False
                o.select_set(True)
            bpy.context.view_layer.objects.active = d
            names = [o.name for o in desc]
            bpy.ops.object.join()
            log.append(f'{d.name} <- {", ".join(names)}')
        out.append(d)
    return out


def remap(objs, table, log):
    for o in objs:
        for slot in o.material_slots:
            m = slot.material
            if m is None:
                raise RuntimeError(f'{o.name}: empty material slot')
            t = table.get(m.name)
            if t:
                tm = bpy.data.materials.get(t) or bpy.data.materials.new(t)
                assert tm.name == t, tm.name
                slot.material = tm
                log.append(f'{o.name}: {m.name} -> {t}')


def images(mat):
    out = []
    if mat and mat.use_nodes:
        for n in mat.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                out.append(n.image.name)
    return out


def export_root(root, kind, out_dir):
    keep, skipped = subtree(root)
    log = []
    src_mats = {s.material.name: images(s.material) for o in keep for s in o.material_slots if s.material}
    remap(keep, REMAP[kind], log)
    saved = root.matrix_world.copy()
    root.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    clean_parenting(root, keep)
    joined = []
    keep = join_nested(root, keep, joined)
    bpy.ops.object.select_all(action='DESELECT')
    for o in keep:
        o.hide_set(False)
        o.hide_viewport = False
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.context.view_layer.update()
    fp = os.path.join(out_dir, root.name + '.fbx')
    bpy.ops.export_scene.fbx(filepath=fp, use_selection=True, use_visible=False, object_types={'MESH'},
                             global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type='FACE', use_subsurf=False,
                             use_mesh_edges=False, use_tspace=False, use_triangles=False, use_custom_props=False,
                             colors_type='NONE', add_leaf_bones=False, bake_anim=False,
                             path_mode='STRIP', embed_textures=False, batch_mode='OFF')
    aabb = {}
    for o in keep:
        vs = [o.matrix_world @ v.co for v in o.data.vertices]
        if vs:
            aabb[o.name] = [[round(min(v[i] for v in vs), 4) for i in range(3)], [round(max(v[i] for v in vs), 4) for i in range(3)]]
    pivots = {o.name: [round(c, 4) for c in o.matrix_world.translation] for o in keep}
    root.matrix_world = saved
    bpy.context.view_layer.update()
    mats = sorted({s.material.name for o in keep for s in o.material_slots})
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in keep)
    print(f'[export] {kind}/{root.name}: {len(keep)} objects ({len(skipped)} left out), {tris} tris, {len(mats)} materials', flush=True)
    return {'file': os.path.basename(fp), 'objects': [o.name for o in keep], 'skipped': [o.name for o in skipped],
            'joined': joined, 'materials': mats, 'source_images': src_mats, 'tris': tris, 'remap': log,
            'bytes': os.path.getsize(fp), 'aabb': aabb, 'pivots': pivots}


def main():
    a = sys.argv[sys.argv.index('--') + 1:]
    kind, out_dir = a[0], a[1]
    os.makedirs(out_dir, exist_ok=True)
    vl = bpy.context.view_layer
    for c in bpy.data.collections:
        c.hide_viewport = False
    for lc in vl.layer_collection.children:
        lc.exclude = False
        lc.hide_viewport = False
        for sub in lc.children:
            sub.exclude = False
            sub.hide_viewport = False
    if kind == 'kit':
        names = [c.name for c in bpy.data.collections if c.name.startswith('Kit_') and c.library is None]
    else:
        names = ROOTS[kind]
    rep = {}
    for n in names:
        root = bpy.data.objects[n]
        rep[n] = export_root(root, kind, out_dir)
    json.dump(rep, open(os.path.join(out_dir, f'export_{kind}.json'), 'w'), indent=1, ensure_ascii=False)


main()

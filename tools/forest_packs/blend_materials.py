"""Materials of a pack's .blend as data for Unity (read by make_unity_data.py): per material the base colour (texture or
value), alpha and how it is used (blend / clip), the emission (colour, strength, texture), back-face culling and whether the
texture is box-projected. Nodes are looked up by type (names are localized). The .blend is not saved.

blender -b --factory-startup <pack>.blend --python blend_materials.py -- <out.json>
"""
import bpy, sys, json, os


def linked_image(sock, seen=None):
    """The image texture node feeding a socket (through mix / math / colour nodes), or None."""
    seen = seen or set()
    if sock is None or not sock.is_linked:
        return None
    n = sock.links[0].from_node
    if n in seen:
        return None
    seen.add(n)
    if n.type == 'TEX_IMAGE':
        return n
    for i in n.inputs:
        r = linked_image(i, seen)
        if r is not None:
            return r
    return None


def value(sock):
    if sock is None:
        return None
    v = sock.default_value
    try:
        return [round(float(x), 4) for x in v]
    except TypeError:
        return round(float(v), 4)


def main():
    out = sys.argv[sys.argv.index('--') + 1]
    mats = {}
    for m in bpy.data.materials:
        d = {'fake_user': m.use_fake_user, 'users': m.users, 'backface_culling': m.use_backface_culling,
             'blend_method': getattr(m, 'blend_method', ''), 'surface_render_method': getattr(m, 'surface_render_method', ''),
             'diffuse': [round(x, 4) for x in m.diffuse_color]}
        if m.use_nodes and m.node_tree:
            bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            emit_node = next((n for n in m.node_tree.nodes if n.type == 'EMISSION'), None)
            d['has_bsdf'] = bsdf is not None
            if bsdf is not None:
                base = linked_image(bsdf.inputs.get('Base Color'))
                d['base_color'] = value(bsdf.inputs.get('Base Color'))
                d['base_image'] = base.image.name if base and base.image else None
                d['base_projection'] = base.projection if base else None
                d['base_extension'] = base.extension if base else None
                d['base_interpolation'] = base.interpolation if base else None
                a = bsdf.inputs.get('Alpha')
                d['alpha'] = value(a)
                ai = linked_image(a)
                d['alpha_image'] = ai.image.name if ai and ai.image else None
                es = bsdf.inputs.get('Emission Strength')
                ec = bsdf.inputs.get('Emission Color') or bsdf.inputs.get('Emission')
                d['emission_strength'] = value(es)
                d['emission_color'] = value(ec)
                ei = linked_image(ec)
                d['emission_image'] = ei.image.name if ei and ei.image else None
                d['metallic'] = value(bsdf.inputs.get('Metallic'))
                d['roughness'] = value(bsdf.inputs.get('Roughness'))
            if emit_node is not None:
                d['emit_node_color'] = value(emit_node.inputs.get('Color'))
                d['emit_node_strength'] = value(emit_node.inputs.get('Strength'))
                ei = linked_image(emit_node.inputs.get('Color'))
                d['emit_node_image'] = ei.image.name if ei and ei.image else None
            d['node_types'] = sorted({n.type for n in m.node_tree.nodes})
            coords = [n for n in m.node_tree.nodes if n.type == 'TEX_COORD']
            d['tex_coord_used'] = sorted({l.from_socket.name for n in coords for o in n.outputs for l in o.links})
        mats[m.name] = d
    uv = {o.name: [l.name for l in o.data.uv_layers] for o in bpy.data.objects if o.type == 'MESH'}
    json.dump({'materials': mats, 'uv_layers': uv}, open(out, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    print('[materials]', len(mats), flush=True)


main()

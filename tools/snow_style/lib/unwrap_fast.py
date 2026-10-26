"""psxlib.unwrap_group with Blender's box packing instead of the concave one (lifted from road_build.py so later builds
share it): the lattice mast and the bridge have thousands of small islands and the concave packer takes tens of minutes;
bld_common.prep_atlas repacks the boxes in shelves (uvpack) right after and keeps the better of the two anyway."""
import math
import bpy
import psxlib as P


def unwrap_fast(objs_, uv_name='PSX', margin=0.003, angle=60, zone_scale=None, custom=None):
    for o in objs_:
        me = o.data
        old = me.uv_layers.active
        for u in me.uv_layers:
            u.active_render = (u == old) if old else False
        uvl = me.uv_layers.get(uv_name) or me.uv_layers.new(name=uv_name)
        me.uv_layers.active = uvl
        if old:
            old.active_render = True
    P.select_only(objs_)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.reveal()
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=margin, area_weight=0.0, correct_aspect=True,
                             scale_to_bounds=False)
    if zone_scale:
        bpy.ops.object.mode_set(mode='OBJECT')
        for o in objs_:
            za = o.data.attributes.get('zone')
            if za is None:
                continue
            uvd = o.data.uv_layers[uv_name].data
            for p_ in o.data.polygons:
                f_ = zone_scale.get(int(round(za.data[p_.index].value)))
                if f_:
                    for li in p_.loop_indices:
                        uvd[li].uv = uvd[li].uv * f_
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, rotate_method='AXIS_ALIGNED', scale=True, margin_method='FRACTION', margin=margin,
                            shape_method='AABB')
    bpy.ops.object.mode_set(mode='OBJECT')

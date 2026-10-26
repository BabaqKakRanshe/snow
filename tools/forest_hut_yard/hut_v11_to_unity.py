"""Hut 1.1 (source/assets/forest_hut_v11, HUT_V11_CONTRACT.md) into the Forest location the way MeteoStation 1.1 went in
(tools/forest_packs/README.md): the three roots exported with tools/forest_packs/export_fbx.py and copied over
Assets/Locations/Forest/Models/{TowerHut,Hochsitz,Sign_Forsthuette}.fbx, the hut atlases over Textures/T_Hut_*_A_px.png /
_E_px.png; every .meta stays (same GUIDs). Before the export (the .blend is not saved) the materials take the names the
location already knows (Forest_Materials.json: M_Lantern - ForestLevelStep finds the lantern glass by it -, M_Hut_LampOil,
M_GlassDirty) and the Glow_* cards go (the layout's Glow_0 is the halo, as with hut 1.0). Then in Unity: Location Builder ->
Forest -> Rebuild, then Integrate Location Level -> Forest.

python tools/forest_hut_yard/hut_v11_to_unity.py [blender.exe]
"""
import os, shutil, subprocess, sys, tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
PKG = os.path.join(ROOT, 'source', 'assets', 'forest_hut_v11')
UNITY = os.path.join(ROOT, 'Snow', 'Assets', 'Locations', 'Forest')
BLENDER = sys.argv[1] if len(sys.argv) > 1 else r'G:\Programms\Steam\steamapps\common\Blender\blender.exe'
ROOTS = ['TowerHut', 'Hochsitz', 'Sign_Forsthuette']
TEX = {'T_Hut_Hut_BaseColor.png': 'T_Hut_Hut_A_px.png', 'T_Hut_HutProps_BaseColor.png': 'T_Hut_HutProps_A_px.png',
       'T_Hut_Panel_BaseColor.png': 'T_Hut_Panel_A_px.png', 'T_Hut_Panel_Emission.png': 'T_Hut_Panel_E_px.png'}
PREP = '''import bpy, runpy, sys
for a, b in {'M_Hut_Lamp_Lantern': 'M_Lantern', 'M_Hut_Lamp_Oillamp': 'M_Hut_LampOil', 'M_Hut_Glass': 'M_GlassDirty'}.items():
    m = bpy.data.materials.get(a)
    if m: m.name = b
for o in [o for o in bpy.data.objects if o.name.startswith('Glow_')]:
    bpy.data.objects.remove(o, do_unlink=True)
runpy.run_path(%r, run_name='__main__')
''' % os.path.join(ROOT, 'tools', 'forest_packs', 'export_fbx.py').replace('\\', '/')

out = tempfile.mkdtemp(prefix='hut11_')
prep = os.path.join(out, 'prep.py')
open(prep, 'w', encoding='utf-8').write(PREP)
subprocess.run([BLENDER, '-b', '--factory-startup', os.path.join(PKG, 'hut.blend'), '--python', prep, '--',
                out, os.path.join(out, 'report.json'), ','.join(ROOTS)], check=True)
for r in ROOTS:
    dst = os.path.join(UNITY, 'Models', r + '.fbx')
    assert os.path.exists(dst + '.meta'), dst + '.meta missing: the GUID would change'
    shutil.copyfile(os.path.join(out, r + '.fbx'), dst)
for a, b in TEX.items():
    dst = os.path.join(UNITY, 'Textures', b)
    assert os.path.exists(dst + '.meta'), dst + '.meta missing'
    shutil.copyfile(os.path.join(PKG, 'tex', a), dst)
print('hut 1.1 copied:', ROOTS, list(TEX.values()), 'report', os.path.join(out, 'report.json'))

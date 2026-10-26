"""Package the start post (forest_start 1.0): start.blend with relative texture paths, the atlases and the panel, the contract
(JSON + FOREST_START_CONTRACT.md), lamps json, stats, layout suggestion, the sheet, renders, the source (self-contained).
python3 -I pack_start.py -> out/forest_start/"""
import os, sys, shutil, glob, ast
sys.path.insert(0, '/home/claude/uaz')
import bpy

HERE = '/home/claude/uaz'
PK = os.path.join(HERE, 'out', 'forest_start')
if os.path.isdir(PK):
    shutil.rmtree(PK)
os.makedirs(os.path.join(PK, 'tex'))
src_tex = os.path.join(HERE, 'tex', 'start')
for f in ('T_Start_Start_BaseColor.png', 'T_Start_StartProps_BaseColor.png', 'T_Start_Panel_BaseColor.png',
          'T_Start_Panel_Emission.png', 'start_panel_regions.json'):
    shutil.copy2(os.path.join(src_tex, f), os.path.join(PK, 'tex'))
bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, 'out', 'start.blend'))
missing = []
for im in bpy.data.images:
    if im.library is not None or im.source != 'FILE' or not im.filepath:
        continue
    name = os.path.basename(bpy.path.abspath(im.filepath))
    if os.path.exists(os.path.join(PK, 'tex', name)):
        im.filepath = '//tex/' + name
    else:
        missing.append(im.filepath)
assert not missing, missing
assert not bpy.data.libraries, [l.filepath for l in bpy.data.libraries]
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PK, 'start.blend'), relative_remap=False)
for f in ('start_contract.json', 'start_lights.json', 'start.json', 'layout_suggestion.json'):
    shutil.copy2(os.path.join(HERE, 'out', f), PK)
shutil.copy2(os.path.join(HERE, 'docs', 'FOREST_START_CONTRACT.md'), PK)
shutil.copy2(os.path.join(HERE, 'rend', 'start', 'forest_start_A.jpg'), PK)
os.makedirs(os.path.join(PK, 'renders'))
for f in ('booth', 'booth_in', 'car', 'car_rear', 'props', 'night', 'layout_plan'):
    shutil.copy2(os.path.join(HERE, 'rend', 'start', f + '.png'), os.path.join(PK, 'renders'))
SRC = ['start_kit.py', 'start_tex.py', 'start_paint.py', 'start_build.py', 'start_quick.py', 'start_render.py', 'start_sheet.py',
       'start_ctx_grid.py', 'start_layout.py', 'start_plan.py', 'pack_start.py']
local = {os.path.splitext(f)[0] for f in os.listdir(HERE) if f.endswith('.py')}
need, todo = set(), list(SRC)
while todo:                                                    # the local modules the scripts import, and theirs
    f = todo.pop()
    for node in ast.walk(ast.parse(open(os.path.join(HERE, f)).read())):
        names = [a.name for a in node.names] if isinstance(node, ast.Import) else (
            [node.module] if isinstance(node, ast.ImportFrom) and node.module else [])
        for n in {n.split('.')[0] for n in names if n.split('.')[0] in local}:
            if n + '.py' not in SRC and n not in need:
                need.add(n)
                todo.append(n + '.py')
LIB = sorted(n + '.py' for n in need)
os.makedirs(os.path.join(PK, 'src'))
for f in SRC + LIB:
    shutil.copy2(os.path.join(HERE, f), os.path.join(PK, 'src'))
open(os.path.join(PK, 'src', 'README_SRC.txt'), 'w', encoding='utf-8').write(
    'Исходники пакета forest_start 1.0 (брошенный пост у ворот SPERRGEBIET, старт Forest). Пакет — папка выше, справочник — '
    '../FOREST_START_CONTRACT.md.\n\n'
    '- Модели целиком строятся скриптами, входных файлов нет. Отделка и покраска — генераторной (gen_kit, gen_paint).\n'
    '- start_kit.py — геометрия пяти корней (будка, машина, фонарь, покрышки, мусор), детали, точки, коллайдеры;\n'
    '  start_tex.py — панель (табличка, линзы, ореолы); start_paint.py — покраска атласов Start и StartProps.\n'
    '- Порядок: start_build.py → out/start.blend, start.json, start_contract.json, start_lights.json, tex/start '
    '(--draft — без запекания, цвета зон);\n'
    '  start_quick.py -- <blend> <папка> [виды] — рабочие виды; start_render.py <виды|all> [spp] → rend/start/*.png (Cycles);\n'
    '  start_sheet.py → rend/start/forest_start_A.jpg.\n'
    '- Раскладка: start_ctx_grid.py (Blender, context.blend из source/context/start_gate) → сетка земли grid_H.npy, '
    'grid_meta.json;\n'
    '  start_layout.py [папка сетки] → out/layout_suggestion.json; start_plan.py → rend/start/layout_plan.png.\n'
    '- pack_start.py → out/forest_start/ (эта папка).\n'
    '- Библиотека: ' + ', '.join(LIB) + '.\n'
    "- Blender 5.0.1 (bpy как модуль Python 3.11). Пути в скриптах абсолютные (HERE = '/home/claude/uaz') — заменить на\n"
    '  путь к этой папке; скрипты пишут в out/, tex/, rend/ внутри неё.\n')
n = sum(len(fs) for _, _, fs in os.walk(PK))
print('packed', PK, n, 'files; lib', LIB)

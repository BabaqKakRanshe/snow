Исходники пакета forest_start 1.0 (брошенный пост у ворот SPERRGEBIET, старт Forest). Пакет — папка выше, справочник — ../FOREST_START_CONTRACT.md.

- Модели целиком строятся скриптами, входных файлов нет. Отделка и покраска — генераторной (gen_kit, gen_paint).
- start_kit.py — геометрия пяти корней (будка, машина, фонарь, покрышки, мусор), детали, точки, коллайдеры;
  start_tex.py — панель (табличка, линзы, ореолы); start_paint.py — покраска атласов Start и StartProps.
- Порядок: start_build.py → out/start.blend, start.json, start_contract.json, start_lights.json, tex/start (--draft — без запекания, цвета зон);
  start_quick.py -- <blend> <папка> [виды] — рабочие виды; start_render.py <виды|all> [spp] → rend/start/*.png (Cycles);
  start_sheet.py → rend/start/forest_start_A.jpg.
- Раскладка: start_ctx_grid.py (Blender, context.blend из source/context/start_gate) → сетка земли grid_H.npy, grid_meta.json;
  start_layout.py [папка сетки] → out/layout_suggestion.json; start_plan.py → rend/start/layout_plan.png.
- pack_start.py → out/forest_start/ (эта папка).
- Библиотека: gen_kit.py, gen_paint.py, gen_tex.py, noise.py, psxbake.py, psxlib.py, pxfont.py, van2_kit.py, van2_tex.py, van3_mech.py.
- Blender 5.0.1 (bpy как модуль Python 3.11). Пути в скриптах абсолютные (HERE = '/home/claude/uaz') — заменить на
  путь к этой папке; скрипты пишут в out/, tex/, rend/ внутри неё.

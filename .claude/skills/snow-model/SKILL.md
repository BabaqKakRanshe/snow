---
name: snow-model
description: Build or rework a Snow 3D model LOCALLY in Blender in the exact project style (PSX atlases, shed-style finish, contract package) — the local alternative to ordering from the Blender chat. Use when the user says "сделай модель сам", "локально в блендере", when the Blender chat is busy or unavailable, or when a small prop / variant / fix is needed fast.
---

# Snow model, locally in Blender

The style is written down in `tools/snow_style/snow_style.json` and lives as CODE in `tools/snow_style/lib`
(the Blender chat's shared library). Read `tools/snow_style/README.md` first: install, run, the 26.10 proof that the
local build reproduces the chat's forest_start pack.

## Rules
- Build through the library (`gen_kit` KB shapes, `psxbake` atlases, `gen_paint` wear, `gen_tex`/`pxfont` panels). Never re-implement the PSX recipe, never use photo textures or PBR maps.
- Pick the class in `snow_style.json#classes` (ppm cap, atlas, AO, colours). Neighbours must share the density.
- Finish like the generator shed (`art_direction`), signs DE then EN, lamps as Lamp_/Lens_/Glow_/LIGHT_ (all off in the file).
- Start from the closest existing pack: copy its `src/<pack>_*.py` into `tools/snow_style/work/<new>/` and adapt. The chat's paths (`/home/claude/uaz`) are not needed: `run_bpy.py` puts `lib/` on sys.path; outputs go next to the script (`out/`, `tex/`, `rend/`).
- Run: `blender -b --factory-startup --python tools/snow_style/run_bpy.py -- <work/new/x_build.py> [--draft]`. Draft first, full bake after.
- Check (`snow_style.json#checks`), then quick views and renders next to the golden sample of the same class (`meta.golden_samples`); look at them with Read before saying it is done.
- Deliver as the chat does: `source/assets/<pack>/` with `<pack>.blend`, `tex/`, `<pack>_contract.json`, `<pack>_lights.json`, `<pack>.json`, `src/`, sheet; the Russian `<PACK>_CONTRACT.md` in `docs/gdd/`. Never change other packs or the Unity scene while building.
- Speed rules of 23.10 apply: only what was asked, rebuild only what changed, draft renders for own checks.
- Open style questions are in `snow_style.json#open_questions` — ask the user, do not decide them silently.
- Bigger or hero models (buildings, vehicles, a new hut version) still go to the Blender chat (skill `blender-agent`) unless the user says to do them here; tell the user which way you chose.

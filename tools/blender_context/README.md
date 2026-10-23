# Blender Context — место карты для агента Blender

Выгружает кусок открытой сцены Unity так, чтобы агент Blender (чат «3D модель Blender для Unity») видел модели
там и так, как они стоят в игре, и знал, из какого исходника каждая сделана. Сцена и ассеты не меняются.

## Как запустить
- Unity: **Tools / Snow / Blender Context** → «Выгрузить» у нужного места, или «Вокруг выделенного» (размер в метрах).
- Из Claude Code (pipeline CLI, через `tools/snow_qa/ue.py`):
  `ue.run_long('return Snow.EditorTools.BlenderContextExport.Export("base_yard", true);')`
  или `ExportAroundSelection(40f, true)`.

Результат — `source/context/<место>/` (не в git: генерируется):
`context.blend` (собирает Blender без окна, ~10–30 с), `context.json`, `meshes/*.obj`, `terrain.obj` + `terrain_paint.png`,
`shots/*.png`, `README.md` для агента, `build_context.py`, `blender_build.log`.

## Места
`areas.json` — список мест. Либо прямоугольник в своей рамке (`centre` x, z в мире Unity, `yaw`, `rect` [u0, v0, u1, v1]),
либо `around`: путь объекта в сцене и `size`. `margin` — запас по краям. `scene` должна быть открыта.
Путь к Blender — поле `blender` или переменная `SNOW_BLENDER`.

## Что попадает
- Все включённые MeshRenderer / SkinnedMeshRenderer, пересекающие место (LOD0), кроме объектов больше 300 м (провода,
  туман, вода на всю карту — список в README выгрузки).
- Деревья, камни и брёвна Terrain (их LOD0), земля участка с покраской, лампы (кроме солнца).
- Для каждого размещённого объекта: префаб, FBX и подсказки исходника в `source/` (файлы с тем же именем и пакеты,
  в контракте которых есть имя префаба).

## Оси
Blender (x, y, z) = Unity (−x, −z, y), как в контрактах пакетов. Матрицы — мировые матрицы Blender, построчно.

## Как устроено
`Snow/Assets/Snow/Editor/BlenderContextExport.cs` (Unity) пишет файлы; `build_context.py` (Blender 4+/5) собирает сцену:
пустышка на каждый объект (корень, у неё свойства `unity_path`, `prefab`, `fbx`, `sources`), меши детьми и общими
данными; коллекции по группам сцены Unity, `TerrainTrees`, `Terrain`, `Lights`, `Cameras` (сверху и с северо-запада).

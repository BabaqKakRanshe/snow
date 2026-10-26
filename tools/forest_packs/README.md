# forest_packs — пакеты Blender-чата в Unity (после forest_start)

Импорт и расстановка пакетов `source/assets/<пакет>` (камни, мусор, устройства, запчасти, снаряжение, мир, системы острова, бункер) по образцу forest_start. `source/` только читается.

## Цепочка (повторный запуск переписывает те же ассеты, GUID те же)

```
blender -b --factory-startup source/assets/<пакет>/<stem>.blend --python tools/forest_packs/export_fbx.py -- <Unity>/Models tools/forest_packs/out/<pack>_export.json [Root,Root]
blender -b --factory-startup source/assets/<пакет>/<stem>.blend --python tools/forest_packs/blend_materials.py -- tools/forest_packs/out/<pack>_materials.json
python tools/forest_packs/make_unity_data.py [<pack> ...]        # <Pack>_Unity.json + текстуры в папку Unity
Unity: Tools / Snow / Packs / Build All Packs                       # префабы (PackPropsBuilder)
Unity: Tools / Snow / Forest / Packs Dress                         # Forest: вложения в префабы forest_far, насос, группы
Unity: Tools / Snow / Forest / Bunker Dress                        # бункер у озера
Unity: Tools / Snow / Sky Island Systems                           # системы острова (сцена острова открывается рядом)
Unity: Tools / Snow / Van / Build Old Van Variant                  # старый фургон (VanOld.prefab)
```

Пакеты (`make_unity_data.py`): rocks, trash, devices, parts, gear, world, sky, bunker → `Assets/Locations/Forest/<Rocks|Trash|Devices|Parts|Gear|World>`, `Assets/SkyIsland/Systems`, `Assets/Locations/Bunker`.

MeteoStation 1.1: `export_fbx.py` из `forest_far/far.blend` с корнем `MeteoStation` во временную папку, затем файл кладётся поверх `Assets/Locations/Forest/Models/MeteoStation.fbx`, атлас `T_Far_Meteo_BaseColor.png` — поверх `Textures/T_Far_Meteo_A_px.png` (.meta не трогать: GUID прежние).

## Что важно

- Экспорт поднимает вложенные меши к корню (Apply Transform точен на один уровень); родителей помнит отчёт, `PackPropsBuilder` возвращает детали под них.
- `centerUnity` коллайдера дан в кадре его детали; генератор берёт `center_root` (кадр корня).
- Лампы, светодиоды, дисплеи: в префабе `_Off`, горящие — отдельные материалы. Свет в префабах выключен.
- Префабы `Dish_1…5`, `MeteoStation`, `RelayMast`, `BaseMain`, `PumpHouse` пересоздаёт ядро при Rebuild: вложения (магнитофоны, будка, наледь) ставит `ForestPacksStep` в цепочке интеграции Forest, после каждой интеграции заново.
- Данные расстановки: `Assets/Snow/Levels/Forest/ForestRocks.json` (россыпь: Tools / Snow / Forest / Generate Rock Scatter, правила `ForestRocks_rules.json`; не перегенерировать после ручных сдвигов), `ForestTrash.json`, `ForestWorld.json`, `ForestParts.json`, `ForestBunker.json`; группы — свои корни под `Forest`, ручные сдвиги хранит World Sync.
- Земля (`ForestGroundEdit`): исходная земля прямоугольников хранится в `<Group>_ground.bytes`; копия тайлов — `snow_archive/backup/forest_<группа>_<дата>`. Если прямоугольники меняются, хранилище перемеряется с уже правленой земли: вернуть его из архива — `ForestGroundEdit.RestoreStore`.

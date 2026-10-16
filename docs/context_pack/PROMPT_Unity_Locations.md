# Задача: обновить ядро Sky Island и собрать четыре новые локации 1 в 1

Ты работаешь в моём Unity-проекте игры. Локация Sky Island уже стоит в проекте (`Assets/SkyIsland`, собрана по прошлому промпту). Теперь нужно:
1. обновить ядро Sky Island (скрипты v2.1, скелет гиганта, общие текстуры, гигант под островом);
2. собрать четыре новые локации: **Town** (город), **CoalPit** (угольный разрез), **Dam** (ГЭС), **AboveClouds** (над облаками);
3. проверить каждую 1 в 1 с эталонными рендерами и встроить в игру.

Снежную и красную (болото) локации не трогай.

## Где что лежит
- Unity-проект: `F:\ClaudeGames\snow\Snow`. Сначала убедись, что это корень проекта (`Assets\`, `ProjectSettings\ProjectVersion.txt`, `Packages\manifest.json`); если нет — найди проект внутри `F:\ClaudeGames\snow`.
- Пакеты, каждая папка — свой пакет из нескольких частей (части одного пакета имеют общие GUID, ставить нужно все):
  - `F:\ClaudeGames\snow\source\locations\SkyIsland\` — обновление ядра Sky Island: `SkyIsland_Update_1.unitypackage`, `SkyIsland_Update_2.unitypackage`
  - `F:\ClaudeGames\snow\source\locations\Town\` — Город → `Assets/Locations/Town`: `Town_1_Core.unitypackage`, `Town_2_Assets.unitypackage`
  - `F:\ClaudeGames\snow\source\locations\CoalPit\` — Угольный разрез → `Assets/Locations/CoalPit`: `CoalPit_1_Core.unitypackage`, `CoalPit_2_Assets.unitypackage`
  - `F:\ClaudeGames\snow\source\locations\Dam\` — ГЭС → `Assets/Locations/Dam`: `Dam_1_Core.unitypackage`
  - `F:\ClaudeGames\snow\source\locations\AboveClouds\` — Над облаками → `Assets/Locations/AboveClouds`: `AboveClouds_1_Core.unitypackage`, `AboveClouds_2_Assets.unitypackage`
- Обновление Sky Island ставится поверх `Assets/SkyIsland` (те же GUID) и меняет только новые и изменённые файлы. Локации ставятся в `Assets/Locations/<Id>`.
- После импорта прочитай `Assets/SkyIsland/Scripts/CHANGES_v2.md` и `README.md` каждой локации целиком. Источник истины для лука — `Data/<Id>_Look.json`, для расстановки — `Data/<Id>_Layout.json`, для материалов — `Data/<Id>_Materials.json`; эталонные кадры — `Previews/<Id>_*.jpg` (1280×720).

## Главные правила
1. Сцены собирает только встроенный сборщик (`Tools → Locations → Location Builder` или методы `SkyIsland.EditorTools.SkyIslandBuilder.*`). Не расставляй и не настраивай руками вместо него.
2. Не придумывай и не «улучшай» значения: цвета, свет, туман, LUT, Volume, материалы, позиции — только из данных пакетов. Если результат не совпадает с эталоном, ищи причину в настройках проекта, а не правь лук.
3. Не трогай чужие ассеты и сцены (снег, болото, всё остальное). Меняй только то, что нужно по шагам ниже, и перечисли все изменения в отчёте.
4. Спроси меня, прежде чем менять render pipeline, Color Space, версию Unity/пакетов, удалять что-либо или менять глобальные настройки, которые влияют на другие уровни (например Shadow Distance в общем URP Asset).
5. Ошибки компиляции в `Assets/SkyIsland/Scripts` исправляй минимально (совместимость API твоей версии), не меняя логику, и сообщи, что поменял. Код не запускался в редакторе: синтаксис проверен парсером, API сверены с исходниками Unity 6 / URP 17.

## Шаг 0. Разведка (без изменений)
Коротко сообщи: версия Unity, URP и какой URP Asset активен (Graphics и каждый уровень Quality), Color Space, Active Input Handling, есть ли уже `Assets/SkyIsland/Scenes/SkyIsland.unity` и в каком она состоянии, какой игрок (префаб/контроллер, камера, тег, радиус CharacterController) используется в игре.

## Шаг 1. Импорт
- Порядок: сначала все части `SkyIsland_Update_*`, потом части каждой локации (все части одной локации подряд).
- Если редактор открыт — попроси меня импортировать пакеты (Assets → Import Package → Custom Package или перетащить все части в окно Project) либо закрыть редактор.
- Если закрыт — импортируй в batchmode по одному, дожидаясь завершения и проверяя лог (без `-nographics`):
  `"<Unity.exe>" -batchmode -projectPath "F:\ClaudeGames\snow\Snow" -importPackage "F:\ClaudeGames\snow\source\locations\SkyIsland\SkyIsland_Update_1.unitypackage" -quit -logFile "%TEMP%\loc_import_1.log"`
- Компиляция должна быть чистой (в логе нет `error CS`).

## Шаг 2. Настройки проекта
Те же, что для Sky Island (если уже выставлены — только проверь): Color Space = Linear; в каждом используемом URP Asset — HDR on, Grading Mode = High Dynamic Range, LUT Size 32, Additional Lights = Per Pixel, Per Object Limit 8; Universal Renderer — Forward+ и Post-processing on.

**Тени.** Эталоны рендерились без ограничения дальности теней. В URP дальность задаёт URP Asset (Shadow Distance, общий для всех уровней). На новых картах дальние тени важны: тени пиков на море облаков (AboveClouds), костей и стен каньона (Dam), уступов (CoalPit). Не меняй общий URP Asset без моего согласия. Предложи вариант: маленький компонент в каждой сцене, который при загрузке ставит `UniversalRenderPipelineAsset.shadowDistance` (и число каскадов 4) и возвращает прежние значения при выгрузке. Рекомендуемые значения: Town 300 м, CoalPit 700 м, Dam 600 м, AboveClouds 2000 м.

## Шаг 3. Сборка
1. Пересобери Sky Island: `Tools → Sky Island → Build Scene` (или `-executeMethod SkyIsland.EditorTools.SkyIslandBuilder.BuildScene -quit`). Теперь под островом есть гигант (кости проросли сквозь скалу, руки свисают) и 10-я точка обзора `UnderTheIsland`. Остальная сцена такая же, как раньше.
2. Каждую локацию: `Tools → Locations → Location Builder` → **Build**, или в batchmode (без `-nographics`, иначе не запекутся отражения):
   `"<Unity.exe>" -batchmode -projectPath "F:\ClaudeGames\snow\Snow" -executeMethod SkyIsland.EditorTools.SkyIslandBuilder.BuildLocationFromCommandLine -location Town -quit -logFile "%TEMP%\build_Town.log"`
   Результат: `Assets/Locations/<Id>/Scenes/<Id>.unity`. Прочитай все предупреждения `[SkyIsland]` в логе. Пропавших моделей, текстур и материалов быть не должно.

Что должно получиться (сверь с логом сборки):

| Локация | Префабов в расстановке | Деревьев | Источников света в префабах | Ореолов | Гигант | Точек обзора |
|---|---|---|---|---|---|---|
| Town | 3748 | 8848 | 285 | 259 | 1 | 9 |
| CoalPit | 100 | 1646 | 63 | 118 | 1 | 9 |
| Dam | 98 | 5239 | 70 | 64 | 1 | 9 |
| AboveClouds | 61 | 0 | 7 | 5 | 1 | 9 |

## Шаг 4. Проверка значений в собранных сценах
Сверь с `Data/<Id>_Look.json` (здесь основные значения; цвета записаны так, как их показывает инспектор Unity, sRGB hex; в JSON они линейные):

| | Town | CoalPit | Dam | AboveClouds |
|---|---|---|---|---|
| Fog (Exponential) | 0.0052, #2B2F4F | 0.0021, #453E39 | 0.0013, #3D6B67 | 0.00011, #BCCBE2 |
| Ambient Sky / Equator / Ground | #343855 / #2A2C44 / #16161F | #4D4A49 / #443E38 / #1F1D1B | #435357 / #384448 / #1F2324 | #7386AA / #8993A0 / #AAADB5 |
| Sun (цвет, Intensity, высота) | #CEDAFF, 0.09, высота 27° | #FFCBA2, 0.85, высота 22° | #D4EAFF, 0.34, высота 35° | #FFF7EA, 0.95, высота 24° |
| Post Exposure / Contrast | +0.65 / +12 | +0.95 / +14 | +1 / +13 | +0 / +14 |
| Bloom threshold / intensity / scatter | 0.8 / 1.35 / 0.74 | 0.85 / 1.25 / 0.72 | 0.85 / 1.3 / 0.74 | 1 / 0.7 / 0.65 |
| Vignette intensity / smoothness | 0.34 / 0.45 | 0.36 / 0.45 | 0.34 / 0.45 | 0.22 / 0.45 |
| Film Grain / Chromatic Aberration | 0.24 / 0.08 | 0.26 / 0.08 | 0.22 / 0.07 | 0.12 / 0.04 |
| Camera Far Clip | 4000 | 4000 | 4000 | 9000 |
| Fog by altitude (air factor) | 0.2 | 0.35 | 0.3 | 1 |

Во всех сценах: Tonemapping Neutral, Color Lookup `<Id>_LUT32` (contribution 1), камера HDR + Post Processing + SMAA High + Dithering. Чужие глобальные Volume не должны перебивать Volume локации.

## Шаг 5. Визуальная проверка 1 в 1
1. Для каждой локации: в окне Location Builder кнопка **Capture**, или
   `-executeMethod SkyIsland.EditorTools.SkyIslandBuilder.CaptureLocationFromCommandLine -location <Id>` (без `-nographics`). Кадры и `report.txt` появятся в `<проект>/SkyIslandCaptures/<Id>/`. Для Sky Island: `Tools → Sky Island → Capture Viewpoints` (появится 10-й кадр `UnderTheIsland`).
2. Открой каждую пару (захват ↔ `Previews/<Id>_<точка>.jpg`) и сравни глазами: композиция, какие огни горят, ореолы, плотность тумана, цвет.
3. Критерий: |ΔL p50| ≤ 0.03, p5 и p95 в пределах ±0.04, тот же оттенок. Допустимые отличия: дым, пар, брызги и пыль есть только в Unity (эмиттеры); отражения мягче (в URP нет SSR); зерно; дальние тени, если Shadow Distance меньше рекомендованного.
4. Если не совпадает, проверь в первую очередь: Grading Mode = LDR; Gamma; выключен пост на камере или рендерере; другой Volume перебивает; Forward вместо Forward+ (пропадают лампы); чужой скрипт меняет `RenderSettings.fog`; уровень Quality с другим URP Asset. Исправь причину и повтори захват.

## Шаг 6. Встраивание в игру
Общее для всех новых сцен:
- Сцена `<Id>.unity` — это уровень. Добавь её в Build Settings.
- Игрок появляется в объекте `PlayerSpawn` (тег `Respawn`, повёрнут лицом в нужную сторону).
- Камера игрока должна получить всё, от чего зависит лук: Post Processing, HDR, SMAA High, Dithering, Far Clip из `camera.far` в Look.json, компонент `SkyIslandFogByAltitude` (скопируй поля с камеры сцены). Тег MainCamera — только у одной камеры; отладочную камеру с `SkyIslandFlyCamera` отключи, когда есть камера игрока.
- По костям гигантов можно ходить (MeshCollider). Коллайдеры рельефа и построек уже есть. Проверь, что игрок не проваливается и проходит в двери: самая узкая — вход в подвал, 1,0 × 2,05 м (радиус CharacterController ≤ 0,4 м, высота ≤ 1,9 м).
- Не удаляй объекты `Emitters`, `FogEvents`, `Giants`, reflection probes и компоненты `SkyIslandFlicker`, `SkyIslandUVScroll`, `SkyIslandWaterLevel` на корне локации.

По локациям:
- **Town.** Событие `SampleTaken` (`SkyIslandFogEvent`, триггер 22 × 7 × 28 м в зале НИИ под комбинатом): туман втрое плотнее за 6 с, один раз. Срабатывает от коллайдера с тегом `Player` (нужен CharacterController или Rigidbody) или из кода: `GetComponent<SkyIslandFogEvent>().Trigger()`. Подвалы, туннель теплотрассы (2,4 × 2,4 м, ~290 м) и пять зданий заходимые. В рельефе вырезаны отверстия под подвалами и шахтой: если будешь заменять меш рельефа на Unity Terrain, повтори их через Terrain Holes. Река без коллайдера.
- **CoalPit.** Старт на платформе узкоколейки (ветка ведёт со стороны города). Спуск в разрез — серпантин от восточного борта. Тлеющие пласты мерцают вместе со своим светом.
- **Dam.** Уровень воды водохранилища — `SkyIslandWaterLevel` на корне: `minY` 98, `maxY` 126, старт 116. Из кода: `GetComponent<SkyIslandWaterLevel>().TargetLevel = 98f;`. События `onDrainStarted/Finished`, `onFillStarted/Finished` повесь на рычаг в турбинном зале или на кране водосброса (спроси меня, если не ясно, что считать рычагом). Поверхности воды без коллайдеров: под ними дно, поэтому для воды нужен свой триггер (плавание или урон/респаун — по правилам игры).
- **AboveClouds.** Финальная карта, попасть можно только на самолёте с Sky Island. Море облаков без коллайдера, ниже отметки −60 м рельефа нет: поставь kill-зону или возврат игрока на высоте Y < −40. ВПП в координатах Unity: торец захода (650, 55, 270), верхний торец (300, 96, 95), ширина 30 м, уклон вверх ~10 %, посадка на восток-северо-восток. Переход Sky Island → AboveClouds сделай так, как в игре устроены переходы между уровнями (спроси меня, если механики самолёта ещё нет).

## Шаг 7. Отчёт
Что сделано; какие настройки проекта изменены (было → стало); таблицы из `report.txt` по всем точкам каждой локации; пути к захватам; что осталось отличаться и почему.

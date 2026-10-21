# Локации ядра в игре: обновление Sky Island, Forest, Town, CoalPit, Dam, AboveClouds — задание для AI-агента

Версия 1.2 от 16.10.2026. Действует вместе с `PLANE_PROMPT_UNITY.md` (v2.1): правила работы, статусы и формат отчёта — оттуда (§0). Выполняется **после** `BASEV2_INTEGRATION_UNITY.md` (v1.1): оттуда ядро локаций v2.7.1 и общий код (`SkyIslandPlayerCamera` с кадром PS1, `SkyIslandLocationHost`, меню интеграции BaseV2). Если пункт здесь расходится с промптом, верно то, что написано здесь.

**Порядок (решение пользователя 16.10): сначала Forest с корпусом BaseV2.** «Нужно встроить все. Пока будем работать над Forest и в него нужно будет перенести BaseV2». Первый запуск — по `F:\ClaudeGames\snow\docs\start\START_FOREST.txt`: шаги L1–L4, L6 и L9 только для Forest. Sky Island, AboveClouds, CoalPit, Dam и Town — следующими запусками по этому же заданию. Если по версии 1.1 работа уже начата (карты импортированы, Forest собран со старым корпусом) — сделанное не переделывать, Forest довести по START_FOREST, как сказано в «Изменениях 1.2».

Изменения 1.2: корпус BaseV2 перенесён на карту Forest вместо `ObsMain` (пакет `Forest_Update2`): таблица пакетов, раздел 0, строки журнала Forest (L3.2), шаг L6, профиль Forest (А.2), проверки QL4–QL6. В том же пакете текстуры Forest переведены на рецепт самолёта (L6.7) и сняты новые превью. Добавлено: после сборки Forest — `Mobile_RPAsset` по ответу на BASEV2 U1.6 (раздел 1, L3.2); свет, который создаёт игра (фонарик), — на все слои рендеринга (L4.8, QL13); различия Capture и превью Forest по питанию (L3.3). Town, CoalPit, Dam и AboveClouds — пакеты `<Id>_Update8`: текстуры тоже по рецепту самолёта (около 64 px на метр, палитра, маски, без карт нормалей) и новые превью; лук, данные расстановки и строки журнала L3.2 не меняются. Карта уже собрана по 1.1 — `<Id>_Update8` поверх, **Rebuild**, Capture и сверка заново. **Если Forest уже импортирован и собран по 1.1** (в `Assets/Locations/Forest/Data/Forest_Layout.json` есть `ObsMain`): первые три пакета заново не ставить — только `Forest_Update2` поверх (L2), затем **Rebuild**, Capture и сверка (L3) с новыми строками журнала; профиль Forest в общем коде заменить на А.2 этой версии; L6 и проверки Forest пройти заново. Корпус `ObsMain` и его 64 части после пересборки из сцены уходят сами (сцену целиком пишет сборщик); `Models/ObsMain.fbx` и `Prefabs/ObsMain.prefab` остаются в папках (пакет файлы не удаляет), в расстановке их нет — не удалять.

Уточнение 17.10 (кости великана на всех картах): у общего скелета великана (558 м) PSX-атлас кости был 512 px — 0,7 px на метр, тексель 1,4 м, и дизеринг вблизи давал крупную шахматку. Теперь у `M_Bone_*` гладкий макро-атлас `T_Giant_Macro_A.jpg` (у Forest `T_Giant_MacroMoss_A.jpg` — с мохом и трещинами) и PSX-деталь кости `T_BoneDetail_A_px` с повтором 160 (около 52 px на метр), маски нет. Изменение приходит в `Forest_Update2`, `<карта>_Update8` и в новом `SkyIsland_Update9` (данные и текстура, без скриптов; ставится поверх `SkyIsland_Update8`, затем остров пересобрать — Build Scene). Импорт настраивает ядро: `_Macro` в имени — трилинейная фильтрация, Clamp, до 8192, со сжатием; ничего не настраивать. `T_Giant_A_px` и `T_Giant_M_px` остаются в папках без ссылок — не удалять. Превью с великаном сняты заново, сверка — по ним.

Изменения 1.1 (после независимой проверки по коду и данным и после переезда папок проекта 16.10): пути — `source\locations`, `docs\…`; точка отката — коммит в git; туман-триггеры Forest (L4.5: контроллер тумана камеры игрока, тег только своему игроку); возврат при падении — не у пассажиров машин, с поворотом спавна (L4.2); группы интерьеров на сервере и проверка дальности (L4.3, L4.4); проверка препятствия у дверей Town при команде; скорость откатных ворот Forest; порядок `parts`; цифры Town; шаг для BaseV2 через общий код (L4.7); строки журнала Sky Island по текущему `SkyIslandLevelBuilder`; QL3, QL5, QL7, QL8, QL11.

Заменяет `F:\ClaudeGames\snow\docs\context_pack\PROMPT_Unity_Locations.md` от 11.10 (ядро v2.1, четыре карты, без сети, Town v1) — его не выполнять: пакеты, данные и ядро с тех пор обновились.

Проект: `F:\ClaudeGames\snow\Snow`, Unity 6000.0.67f1, URP 17.0.4 (`PC_RPAsset`, Forward+), NGO 2.13.3, Multiplayer Play Mode 1.6.3.

| Локация | Пакеты по порядку (`F:\ClaudeGames\snow\source\locations\<папка>\`) | Сцена |
|---|---|---|
| Sky Island | ядро и данные острова пришли цепочкой ядра (BASEV2, U2); сверху `SkyIsland_Update9` (кости великана, в `source\locations\SkyIsland\`) | `Assets/SkyIsland/Scenes/SkyIsland.unity` |
| Forest | `Forest_1_Core`, `Forest_2_Assets`, `Forest_Update1`, `Forest_Update2` (главный корпус BaseV2 вместо `ObsMain`) | `Assets/Locations/Forest/Scenes/Forest.unity` |
| Town | `Town_1_Core`, `Town_2_Assets`, `Town_3_Assets` (это Town v2), `Town_Update6`, `Town_Update7`, `Town_Update8` (текстуры по рецепту самолёта); старый Town v1 (в `F:\ClaudeGames\snow_archive`) не ставить | `Assets/Locations/Town/Scenes/Town.unity` |
| CoalPit | `CoalPit_1_Core`, `CoalPit_2_Assets`, `CoalPit_Update6`, `CoalPit_Update7`, `CoalPit_Update8` | `Assets/Locations/CoalPit/Scenes/CoalPit.unity` |
| Dam | `Dam_1_Core`, `Dam_2_Assets`, `Dam_Update6`, `Dam_Update7`, `Dam_Update8` | `Assets/Locations/Dam/Scenes/Dam.unity` |
| AboveClouds | `AboveClouds_1_Core`, `AboveClouds_2_Assets`, `AboveClouds_3_Assets`, `AboveClouds_Update6`, `AboveClouds_Update7`, `AboveClouds_Update8` | `Assets/Locations/AboveClouds/Scenes/AboveClouds.unity` |

Справка: `Assets/Locations/<Id>/README.md` каждой локации (копии — `F:\ClaudeGames\snow\docs\context_pack\README_<Id>.md`), `Assets/SkyIsland/README.md` и `Scripts/CHANGES_v2.md`, карты и планы — `docs\context_pack\<Id>_Map.jpg`, `<Id>_Plan.png` (и рядом с пакетами), общий контекст карт — `F:\ClaudeGames\snow\docs\context_pack\GAME_CONTEXT.md`. Журнал работы — `F:\ClaudeGames\snow\docs\dev\ImplementationStatus.md`.

Числа в задании посчитаны по данным пакетов (`Data/<Id>_Layout.json`, `_Look.json`) и по коду ядра v2.7.1 и проекта на 16.10.2026 (после интеграции самолёта v12 и зимнего комплекта); данные в `source\locations` совпадают с этими пакетами байт в байт (FBX Forest — по содержимому).

## 0. Что и зачем

Пользователь (16.10): «У нас же было много островов. Где для них промпт? Почему только одно описание? Где промпт для обновления острова в облаках?»

**Решение пользователя — кадр PS1 у игрока во всех локациях (CONFIRMED):** 15.10 «кадр PS1 во всех локациях»; 16.10 «PS1 везде, и в кабине тоже». Реализация — `BASEV2_INTEGRATION_UNITY.md`, U4.1 (`SkyIslandPlayerCamera.copyPsxFrame = true`, экранные координаты через viewport). У всех локаций в `Look.json` есть блок `psx` (240 строк, 5 бит, дизеринг 1), сборщик вешает `SkyIslandPSX` на камеру локации.

Состояние проекта до этого задания: локаций, кроме Sky Island, в проекте нет (если запуск по 1.1 уже что-то поставил — см. абзац «Порядок»). Sky Island стоит в старом виде (ядро v1) и уже является уровнем игры: самолёт, груз, спавны (`Tools/Snow/Integrate Sky Island Level`). После `BASEV2_INTEGRATION_UNITY.md` ядро — v2.7.1, корпус BaseV2 — сетевой уровень.

Цель — каждая локация становится сетевым уровнем, как BaseV2: собрана сборщиком пакета, сверена 1 в 1 с эталонными кадрами, до 4 игроков, камера игрока с луком локации и кадром PS1, всё открываемое — через `SkyIslandLocationHost` по сети, возврат на спавн при падении за край. Sky Island обновляется на месте вместе с самолётом. «Остров в облаках» — это Sky Island (парящий остров над морем облаков, шаг L5); финал над облаками — AboveClouds (L7).

| Id | Что | Объектов в расстановке | Великаны | Точек обзора | Открываемых частей в сцене | Группы интерьеров | Питание | Особое |
|---|---|---|---|---|---|---|---|---|
| SkyIsland | аэродром на парящем острове, жёлто-оливковый туман | 6 178 | 2 (под островом, грудная клетка в лесу) | 10 | — | — | — | самолёт и груз проекта; край острова — обрыв в пустоту |
| Forest | стартовая карта, ночь, законсервированная обсерватория с главным корпусом BaseV2 | 49 021 | 1 | 17 | 161 (корпус — 131) | — | генератор + рубильник контейнера + главный рубильник корпуса, на старте сети нет | свет корпуса по 16 комнатам, туман-триггеры, якоря машины |
| Town | город v2, сумерки | 20 469 | 4 | 10 | 10 399 | 62 (внутри 9 815 частей) | — | имена частей повторяются; 156 заперто |
| CoalPit | угольный разрез, закат | 1 747 | 3 | 9 | — | — | — | дно на 135 м ниже борта |
| Dam | ГЭС в каньоне | 5 337 | 6 | 9 | — | — | — | уровень воды 98–126 м |
| AboveClouds | финал над облаками | 62 | 2 | 9 | — | — | — | море облаков без коллайдера |

Не в задании (**DEFERRED**): переходы между уровнями (самолётом с Sky Island на AboveClouds и т. п.), машина игроков на Forest (якоря `Car_Start`, `Car_Parked`), NavMesh и существа, звук, сохранения. **OPEN** (решает пользователь, не придумывать): правила воды на Dam (плавание, урон), рычаг водосброса, смысл автоматов на щитах, дальность теней (L3.5), какая сцена загрузочная.

## 1. Что затронуто в проекте

| Где | Что |
|---|---|
| `Assets/Locations/Forest`, `Town`, `CoalPit`, `Dam`, `AboveClouds` | новые пакеты; `Scenes/`, `Prefabs/`, `Materials/`, `Lighting/`, `Settings/` делает сборщик (L3); сцену затем дополняет меню интеграции (L6–L8). `Data/`, `Models/`, `Textures/`, README не меняются |
| `Assets/SkyIsland/Scenes/SkyIsland.unity`, `Materials/`, `Prefabs/`, `Lighting/`, `Settings/` | пересборка `Build Scene` (L5) и затем `Integrate Sky Island Level` |
| `Assets/Snow/Editor/LocationLevelBuilder.cs` | новый: обобщённое меню интеграции с профилями локаций; `BaseV2LevelBuilder` становится одним из профилей (L4.1) |
| `Assets/Snow/Levels/LocationFallReset.cs`, `LocationProximity.cs` | новые: возврат при падении (L4.2), группы интерьеров с учётом всех игроков на сервере (L4.3) |
| `Assets/Snow/Levels/SkyIslandLocationHost.cs` | id частей, когда имена повторяются; двери без `NetworkDoor` и проверка препятствия при команде; проверки `lights` / `grid` на null; при необходимости — путь обновления для Town (L4.4) |
| `Assets/Snow/Levels/SkyIslandPlayerCamera.cs` | туман-триггеры: события тумана — на контроллер камеры игрока, тег `Player` — своему игроку во время игры (L4.5) |
| `Assets/Snow/Runtime/Interaction/InteractionHost.cs` | флаг «встроенные зоны» (по умолчанию как сейчас), у хоста локаций — выключен; `InReach` — по коробке зоны, а не `Collider.ClosestPoint` (L4.4) |
| `Assets/Snow/Editor/SkyIslandLevelBuilder.cs` | + возврат при падении (L4.6) |
| `Assets/Snow/Runtime/Aircraft/SurvivalItem.cs`, `Assets/Snow/Runtime/Snow.Runtime.asmdef` | свет фонарика, фальшфейера и ракеты — на все слои рендеринга; ссылка на URP Runtime (L4.8) |
| `ProjectSettings/EditorBuildSettings.asset` | 5 сцен локаций — в конец списка (Sky Island остаётся первой) |
| `docs/dev/ImplementationStatus.md` | разделы по локациям (L9) |

`Player.prefab` и `BenchBuilder.cs` не меняются (правило задания BaseV2 остаётся).

Глобальные настройки: сборщик проверяет у **активного** URP-ассета HDR, LUT 32, Grading HDR, Per Object Limit ≥ 8 (у `PC_RPAsset` уже так); Rendering Layers и тени дополнительных источников включают сборки карт с блоком `lighting` в данных — BaseV2 и с `Forest_Update2` Forest (`SkyIslandLighting.SetupLightingProject`, во всех URP-ассетах). Изменится только `Mobile_RPAsset` (тени дополнительных источников), строка `[SkyIsland] Forest: URP assets updated (1 settings): …`. После каждой сборки Forest вернуть его по ответу пользователя на BASEV2 U1.6 (через `SerializedObject`, как там) и записать; `PC_RPAsset` не меняется. Во время L3 и L5 активно качество PC.

## 2. Шаги

### L1. Подготовка
1. Проверить, что `BASEV2_INTEGRATION_UNITY.md` выполнено до U5 включительно: в `Assets/SkyIsland/Scripts/CHANGES_v2.md` есть «v2.7.1»; есть `SkyIslandLocationHost`, `SkyIslandPlayerCamera.copyPsxFrame`, правки экранных координат в `InteractionOverlay` и `InteractionSensor` и ширины контура и рамки в `HoverOutline`, меню `Tools/Snow/Integrate BaseV2 Level`. Нет — сначала то задание. Для L5 на месте PSX-текстуры острова: `Assets/SkyIsland/Textures/*_px.png` и `Assets/SkyIsland/Previews/PSX240/` (их приносит `SkyIsland_Update6`, 7,4 МБ); нет — остров соберётся без них, остановиться и сообщить.
2. Выйти из Play Mode; перед компиляцией — состояние редактора (R-4).
3. Точка отката — коммит в git до начала (README проекта; `git add -A`, `git commit -m "Before locations update"` в `F:\ClaudeGames\snow`, хэш — в отчёт); `source\` в git не входит. Если коммит недоступен — копия вне репозитория в `F:\ClaudeGames\snow_archive\backup\locations_<дата>\`: `Assets/SkyIsland/Scenes`, `Materials`, `Prefabs`, `Lighting`, `Settings` (их перезапишет пересборка), `Assets/Snow/Editor`, `Assets/Snow/Levels`, `Assets/Snow/Runtime/Interaction`, `ProjectSettings/EditorBuildSettings.asset` — с `.meta`.
4. «До» для отчёта: снимки уровня Sky Island (с ВПП у самолёта и из кресла пилота, 1920 × 1080), время кадра там же; место самолёта — из открытой сцены, без запуска меню интеграции (оно пересохранит сцену): позиция и поворот корня самолёта и имя коллайдера под точкой (370, 100) — лучом вниз с высоты 60 м, маска без Interact, как ищет `Integrate Sky Island Level`.

### L2. Импорт пакетов локаций
1. Способ — как в BASEV2, U2.2: по одному пакету из `F:\ClaudeGames\snow\source\locations\<Id>\` — из кода редактора (`AssetDatabase.ImportPackage(path, false)`, дождаться конца импорта, перезагрузки домена и компиляции), в batchmode при закрытом редакторе или руками пользователя. Порядок внутри локации — строго по таблице в начале; локации между собой независимы. Между пакетами в консоли нет `error CS`.
2. Town: только пакеты v2 и их обновления (таблица). Town v1 убран в архив, его не ставить. Старой папки `Assets/Locations/Town` в проекте нет, удалять ничего не нужно.
3. После каждой локации на месте `Assets/Locations/<Id>/README.md` с разделами «Обновление 6», «Обновление 7» и «Обновление 8» (у Forest — «Обновление 1» и «Обновление 2»).

### L3. Сборка и сверка 1 в 1
1. Активно качество PC; открытую сцену сохранить (сборщик заменяет её без вопроса). `Tools → Locations → Location Builder` → `<Id>` → **Build** (после первой сборки — **Rebuild**) или `SkyIsland.EditorTools.SkyIslandBuilder.BuildLocation("<Id>")`. Тяжёлые Forest (49 тыс. объектов) и Town (20 тыс.) можно собрать в batchmode без `-nographics` (иначе не запекутся отражения): `"<Unity.exe>" -batchmode -projectPath "F:\ClaudeGames\snow\Snow" -executeMethod SkyIsland.EditorTools.SkyIslandBuilder.BuildLocationFromCommandLine -location Town -quit -logFile "%TEMP%\build_Town.log"`.
2. Ожидаемые строки журнала:

| Id | Строки |
|---|---|
| Town | `Town: 4/4 giants placed.`; `Town: 2 particle emitter(s).`; `Town: PlayerSpawn at <…>, yaw -180.`; `Scene built for URP: Assets/Locations/Town/Scenes/Town.unity  (20469 objects)` |
| CoalPit | `CoalPit: 3/3 giants placed.`; `CoalPit: 9 particle emitter(s).`; `PlayerSpawn at <…>, yaw -88.1.`; `(1747 objects)` |
| Dam | `Dam: 6/6 giants placed.`; `Dam: 5 particle emitter(s).`; `Dam: water level 98..126 m (start 116) on 1 surface(s), 0 flow object(s), <k> collider(s) removed.`; `yaw 90.`; `(5337 objects)` |
| AboveClouds | `AboveClouds: 2/2 giants placed.`; `AboveClouds: 5 particle emitter(s).`; `yaw -116.6.`; `(62 objects)` |
| Forest | `Forest: 1/1 giants placed.`; `interactables in BaseMain: 9 drivers, 19 driven parts.`; `Forest: 63 light fixtures; rendering layer masks set on <N> renderers.`; `Forest: URP assets updated (1 settings): Lighting > Use Rendering Layers on, Additional Lights > Cast Shadows on.` (если `Mobile_RPAsset` перед сборкой стоял на 0; после сборки вернуть, раздел 1); `Forest: rendering layers 0..9 used by 16 light zones (<k> layer name(s) added).` (после BaseV2 `<k>` = 0); `Forest: power grid: 16 lights, 16 halos, 6 lamp material pairs, switches: IX_GenStart, IX_MainSwitch.`; `Forest: 16 powered interactables on the grid.`; `Forest: light manager: 63 fixtures (1 Beacon, 12 Emergency, 4 Exit, 23 Fluorescent, 2 Halogen, 21 Incandescent), 63 lights, 10 hero, 16 rooms, 19 switches, grid: yes, shadows: 3 within 14 m.` (порядок видов может отличаться); `Forest: 2 fog event volume(s).`; `Forest: 2 anchor(s).`; `yaw 142.1.`; `(49021 objects)` |

   Все строки начинаются с `[SkyIsland] `; перед скобкой с числом объектов два пробела. Дробные числа ядро печатает в культуре системы (`{yaw:0.#}`): на русской Windows — `yaw -88,1`, `-116,6`, `142,1`; запятая вместо точки — совпадение. Предупреждений `missing world model`, `children not found`, `drives not found`, `textures are missing`, `power material pair … not found` быть не должно; остальные предупреждения `[SkyIsland]` выписать в отчёт.
3. Сверка: окно Location Builder → **Capture** или `-executeMethod SkyIsland.EditorTools.SkyIslandBuilder.CaptureLocationFromCommandLine -location <Id>` (без `-nographics`). Кадры и `report.txt` — в `<проект>/SkyIslandCaptures/<Id>/` (у Sky Island — прямо в `<проект>/SkyIslandCaptures/`); сравнить с `Assets/Locations/<Id>/Previews/<Id>_<точка>.jpg` глазами и по числам: |ΔL p50| ≤ 0,03, p5 и p95 в пределах ±0,04, тот же оттенок. Capture снимает без кадра PS1 и сверяется с чистыми превью. Допустимо: дым, пар, брызги и пыль только в Unity (эмиттеры); отражения мягче (в URP нет SSR); зерно; дальние тени (L3.5). У Forest превью сняты в кадре PS1 (427 × 240, увеличены × 3): сравнивать числа и композицию, мелкие детали отличаются. Питание у Forest разное: Capture снимает сцену в редакторе, как её записал сборщик, — лампы сети карты выключены, а все 63 светильника корпуса горят (менеджер света работает только в Play), вместе с аварийными; превью сняты как в игре — виды снаружи без сети (в корпусе горят только аварийные и табло), интерьеры (`ControlRoom`, `Corridor`, `Kitchen`, `SwitchRoom`, `GenContainer`) с сетью и включёнными комнатами, аварийные погашены. Поэтому на `Base` и `KPP` окна корпуса в Capture светятся, а в превью тёмные, — это не расхождение лука: такие точки сверять по рельефу, лесу, небу и туману, корпус — в Play без сети (снимок в отчёт).
4. Не совпадает — сначала проверить: Grading Mode = LDR, Gamma вместо Linear, выключен пост на камере или рендерере, чужой глобальный Volume, Forward вместо Forward+ (пропадают лампы), чужой скрипт меняет `RenderSettings.fog`, активен другой уровень качества. Исправить причину, лук не трогать, повторить Capture.
5. **Тени — OPEN.** В `PC_RPAsset` Shadow Distance 50 м, общий для всех уровней; эталоны рендерились без ограничения, и на больших картах дальних теней не будет (пики на море облаков, стены каньона, уступы разреза). Предложить пользователю одним сообщением (ответа не ждать): компонент в каждой сцене, который при загрузке ставит `UniversalRenderPipelineAsset.shadowDistance` (и 4 каскада) и возвращает прежнее при выгрузке — Town 300 м, CoalPit 700, Dam 600, AboveClouds 2000, Forest — по снимку. Без согласия не делать; общий ассет не менять.

### L4. Общий код Snow
**L4.1. `LocationLevelBuilder`** (`Snow.Editor`) — обобщение `BaseV2LevelBuilder` из BASEV2, U4.3. Профиль локации (Приложение А): id, путь сцены, какие части — двери под `NetworkDoor`, какие — двери без `NetworkDoor`, подписи и категории, `fallY`. Меню `Tools/Snow/Integrate Location Level/<Id>` для Forest, Town, CoalPit, Dam, AboveClouds; `Tools/Snow/Integrate BaseV2 Level` остаётся и вызывает профиль BaseV2: `BaseV2LevelBuilder.cs` становится тонкой обёрткой над общим кодом, файл не удалять; результат BaseV2 не меняется, кроме добавленного возврата при падении (L4.7, QL9). Отличия от BASEV2, U4.3:
1. Корень — объект `<Id>`, камера — `<Id>/Main Camera`; `NetworkObject` и `SkyIslandLocationHost` — только если в локации есть открываемые части (Forest, Town; у CoalPit, Dam, AboveClouds их нет).
2. Двери. `NetworkDoor` — только у частей вида Hinge с `min = 0` из списка профиля. Двери вида Slide (откатные ворота Forest) и все двери Town (2 216 штук: тысячи `NetworkBehaviour` и сотни переменных на одном объекте — лишняя нагрузка и большое стартовое сообщение) идут общим путём хоста: `SkyIslandInteractable` + бит состояния; их объекты **остаются на своём слое** (закрытая дверь держит игрока). Створку двигает ядро, остановить её на полпути нельзя (бит — только 0 или 1), поэтому препятствие проверяется один раз, при команде (L4.4): игрок в целевой позе створки — отказ «Мешает игрок». Игрока, шагнувшего в проём, пока дверь едет, створка выталкивает восстановлением перекрытия `CharacterController` — в сторону, куда идёт; ядро двигает её на `speed × deltaTime`, и на рывке кадра (до 0,33 с — почти половина хода) она может протолкнуть игрока сквозь себя. Проверить в QL5 (TEMPORARY); если запирает или проталкивает — сообщить с условиями, варианты решает пользователь.
3. Мебель (всё, что не дверь) — на слой Interact, с `RecordPrefabInstancePropertyModifications`, как в BASEV2, U4.3.7.
4. Порядок `parts` и id зоны — одно правило: имена в локации уникальны (BaseV2, Forest) — `parts` в ordinal-порядке имён, id = имя части (как в BASEV2, U4.3.8); имена повторяются (Town: 575 имён встречаются больше одного раза — один и тот же дом стоит много раз) — `parts` в порядке обхода `GetComponentsInChildren<SkyIslandInteractable>(true)` от корня в редакторе, id = `p<индекс в parts>`. На всех пирах порядок одинаков, потому что сцена одна; расхождение ловит `_layoutHash`. Пути в иерархии не годятся: экземпляры одного префаба рядом называются одинаково.
5. Зоны и подписи — как в BASEV2, U4.3.6, по профилю; у дверей без `NetworkDoor` зона по толщине тоже +2 см с каждой стороны.
6. Группы интерьеров (`SkyIslandProximityGroup`, только Town): у каждой группы компонент выключить (`enabled = false`, его `Start` прятал бы детей сам), список групп — в `LocationProximity` на `SnowLevel` (L4.3).
7. `LocationFallReset` на `SnowLevel`: `fallY` из профиля; если в профиле нет — нижняя граница не-триггерных коллайдеров под корнем минус 30 м, только включённых и на активных объектах (`enabled && gameObject.activeInHierarchy`, после `Physics.SyncTransforms()`): у выключенного коллайдера `bounds` пустые, в нуле, а у великанов есть скрытые кости с `MeshCollider`.
8. Скорость частей общего пути. Где в данных нет `speed`, ядро берёт 1,4 хода в секунду. У откатных ворот Forest `IX_Gate_81` ход 6,6 м — 0,7 с, 9 м/с, на слое игрока. Меню ставит им `speed = 0.15` (≈6,7 с, ~1 м/с; TEMPORARY) переопределением сцены с `RecordPrefabInstancePropertyModifications`; данные не трогать.
9. Спавн, `NetworkManager`, `SkyIslandPlayerCamera { copyPsxFrame = true }`, сохранение `SaveNetworkScene(…, first: false)` — как в BASEV2.
10. Журнал: `[Snow] <Id> level integrated: <путь> (<D> doors, <Z> zones, <S> synced parts, <R> rooms, <G> proximity groups, fall reset Y <y>, 4 spawn points), appended to Build Settings.` Ожидаемые числа — в шагах L6–L8.

**L4.2. `LocationFallReset`** (`Snow.Levels`). Только для своего игрока (`PlayerNet.Local`) и только когда он идёт сам: не в кресле (`seatController.Seat == null`) и без родителя (`transform.parent == null`). Игрок, стоящий на полу едущей машины, — её дочерний объект (`PlayerNet.UpdatePlatform`), а самолёт летает и под островом, и над облаками: пассажира в отсеке сбрасывать нельзя.
- Если `y < fallY`: ближайшая точка из `SnowLevel/SpawnPoints`; сначала `view.SetYaw(её yaw)`, потом `motor.Teleport(точка, yaw, Vector3.zero)` — без `SetYaw` следующий шаг движка вернёт прежний поворот взгляда (так же делает `PlayerNet.OnNetworkSpawn`); затем у владельца `NetworkTransform.Teleport(точка, поворот, масштаб)`, чтобы у остальных игрок не «пролетал» к спавну по интерполяции. Строка `BenchLog` `fall_reset` (откуда, куда).
- Сервер во время падения и в момент возврата пишет `implausible_move` (скорость больше 12 м/с) — ожидаемо, не ошибка.
- Известное ограничение (TEMPORARY, §7.6): выпавший из летящего самолёта остаётся его дочерним объектом до приземления (`UpdatePlatform` меняет родителя только на земле), и сброс его не поймает. Записать, если проявится; чинить в `PlayerNet` — по решению пользователя.

**L4.3. `LocationProximity`** (`Snow.Levels`). Включает интерьеры Town вместо `SkyIslandProximityGroup` (у групп компонент выключен, L4.1.6).
- Первая проверка — в `Start` (иначе все 9 815 частей интерьеров видны до неё), дальше раз в 0,25 с. Состояние группы хранить и `SetChildren` вызывать только при смене.
- Группа включена, если внутри `bounds`, расширенных на `margin` (22 м), есть камера этого пира. На сервере — ещё и если там любой подключённый игрок (`NetworkManager.ConnectedClientsList[*].PlayerObject`) или внутри самих `bounds` лежит свободный груз (`CargoItem` в состоянии `Loose`; список грузов обновлять раз в секунду). Причина — физика сервера: груз симулирует сервер (`CargoItem`, вариант A), и предмет, брошенный клиентом в доме, провалился бы сквозь выключенные у хоста лестницу и пол — сразу или когда все уйдут. До старта сессии — только своя камера.
- Включая группу, сразу поставить её части в состояние: `openAmount = target`, `Apply()` (у клиента `target` уже выставлен по битам, L4.4) — иначе двери, открытые давно, распахиваются у подошедшего игрока на глазах.
- Строка `BenchLog` `loc_group` при каждой смене на сервере: группа, вкл/выкл, причина (камера хоста / клиент N / груз) — доказательство для QL7.
- Проверку дальности `InteractionHost.InReach` группы не решают: у выключенного коллайдера `Collider.ClosestPoint` возвращает саму точку, и сервер принял бы нажатие с любого расстояния. Поэтому `InReach` считает по коробке зоны (L4.4).

**L4.4. `SkyIslandLocationHost` и `InteractionHost` для больших локаций.**
- id не разбирать (бывает `p<номер>`); `lights` / `grid` могут отсутствовать (у Forest с корпусом BaseV2 есть оба, у Town — ничего): все проходы по комнатам и сети — под проверкой на null (Приложение В задания BaseV2 в версии 1.1 так и написано).
- Town: 10 399 частей = 163 элемента `_bits` (1,3 КБ). Сначала измерить простой путь — полный проход каждый кадр (`Publish` на сервере, `ApplyReplicated` на клиенте): это около 10 тыс. чтений полей, порядка 0,1 мс; замер — `ProfilerMarker` вокруг обоих, хост и клиент. До 0,2 мс — оставить один путь для всех локаций. Дороже — второй путь, с записью: сервер помечает изменённый элемент в `ServerUse` и раз в секунду делает полную сверку (для изменений из другого кода); клиент слушает `_bits.OnListChanged` — подписка только не на сервере (у хоста событие приходит и на свои записи), после снимка в `OnNetworkSpawn`, отписка в `OnNetworkDespawn`; событие `Value` — применить элемент `Index`, любое другое — полный проход; и раз в секунду полная сверка.
- Двери без `NetworkDoor`: `Describe` — по `openAmount` и `target` («закрыта / открывается / открыта / закрывается»), запертая — «заперта»; `ActionText` — «открыть / закрыть».
- `part.locked` → «Заперто» до всего остального (BASEV2, U4.2): у Town 156 запертых дверей, у Forest — люк шахты.
- **Препятствие у дверей общего пути — при команде** (L4.1.2; ENGINEERING). В `ServerUse`, перед `Toggle`: целевая поза створки (закрыть — поза при `openAmount = 0`, открыть — при 1) должна быть свободна от игроков. Поза при 0 — исходная: меню интеграции записывает в `parts` локальные поворот и позицию створки (в редакторе двери закрыты; `_baseRot` ядра закрыт); поза при 1 — `AngleAxis(max, axis)` к исходному повороту у Hinge, сдвиг `axis * max` у Slide (ось — в пространстве родителя, как у ядра). Для каждого не-триггерного коллайдера створки — `Physics.OverlapBox` по его локальной рамке, перенесённой в целевую позу (та же математика, что в `NetworkDoor.IsFree`); найден `CharacterController` — отказ «Мешает игрок». Чужие игроки на сервере — с включённым `CharacterController` (кроме сидящих).
- `Release` — `true` без действий (BASEV2, U4.2): с 16.10 `InteractionSensor` шлёт его в нажатую зону для всех зон.
- **`InReach` по коробке зоны** (`InteractionHost`). Вместо `col.ClosestPoint(usePoint)` — ближайшая точка коробки `BoxCollider` зоны математикой трансформа: `InverseTransformPoint(usePoint)`, зажать в `center ± size / 2`, `TransformPoint`. У выключенного коллайдера `Collider.ClosestPoint` возвращает саму точку (документация Unity), то есть дальность 0 — сервер принял бы нажатие на зону в выключенном у него доме с любого расстояния. Зоны всех хостов — `BoxCollider`, для самолёта и стендов результат тот же (у неравномерно масштабированных зон — с ничтожной разницей).
- **Встроенные зоны выключить.** `InteractionHost.EmbeddedOnRay` при первом попадании луча в любой твёрдый коллайдер под хостом строит таблицу «зона × выпуклый коллайдер» (`BuildEmbedded`, `ClosestPoint` на каждую пару). Под корнем локации — капсулы всех деревьев (сборщик ставит их растительности, траве — нет): у Forest это 142 зоны × десятки тысяч капсул, у Town — 10 399 зон × 9 733 капсулы, зависание от секунд до минут. Зоны локаций торчат из своих частей, встроенные им не нужны: в `InteractionHost` добавить `protected virtual bool UseEmbeddedZones => true` и ранний выход в начале `EmbeddedOnRay`, в `SkyIslandLocationHost` — `false`. У самолёта и стендов поведение не меняется.

**L4.5. Туман-триггеры Forest** (`FogUnderHand` ×2,6, `FogRibs` ×2,2, оба возвращаются при выходе; ENGINEERING). Два препятствия, оба в `SkyIslandPlayerCamera.Apply` (выполняется на каждом пире для своего игрока):
- **Контроллер тумана.** Сборщик связал каждый `SkyIslandFogEvent` с `SkyIslandFogByAltitude` камеры сцены. После входа игрока эта камера выключена, событие видит контроллер неактивным и пишет `RenderSettings.fogStartDistance/EndDistance` напрямую (туман Forest линейный), а контроллер камеры игрока в каждом `LateUpdate` переписывает их обратно — эффекта нет. После копии тумана: каждому `SkyIslandFogEvent` сцены (`FindObjectsByType<SkyIslandFogEvent>(FindObjectsInactive.Include, FindObjectsSortMode.None)`), у которого `fogController` пуст или равен контроллеру камеры сцены, — контроллер камеры игрока.
- **Тег `Player` — только своему игроку и только во время игры:** `PlayerNet.Local.gameObject.tag = "Player"`, если он `Untagged` (тег встроенный; `CharacterController` — на корне игрока). Не в префаб: у чужих игроков на каждой машине `CharacterController` включён (`PlayerNet.ShowSeated`), и туман сгущался бы у всех, когда в объём входит любой. `Player.prefab` и `BenchBuilder` не менять.
- Туман — эффект, а не состояние, по сети не синхронизируется: каждый пир сгущает свой туман, когда в объём входит его игрок. Доказательство (QL11): `densityScale` контроллера камеры игрока при входе и выходе и снимки.

**L4.6. `SkyIslandLevelBuilder`**: добавить `LocationFallReset` на `SnowLevel` с `fallY = -60` (ВПП и городок на 0–20 м, рельеф от −2,7 м; под островом пусто). Остальное в нём не менять.

**L4.7. BaseV2 через общий код.** После L4.1–L4.6 и компиляции — `Tools/Snow/Integrate BaseV2 Level` (теперь через `LocationLevelBuilder`, профиль BaseV2). Журнал: числа BASEV2, U4.3.13 — 19 doors, 112 zones, 112 synced parts, 16 rooms, 4 spawn points — плюс `0 proximity groups, fall reset Y <y>`. Затем коротко повторить BASEV2 Q5–Q11 на хосте и одном клиенте (QL9).

**L4.8. Свет, который создаёт игра.** На картах с зонами света (блок `lighting`: BaseV2 и с `Forest_Update2` Forest) сборщик переводит рендереры карты со слоя рендеринга 0 на слой 1 (`defaultMask` = 2), комнаты корпуса — на слои 2–9; на слое 0 остаются игроки, груз и всё, что карта не собирала (`dynamicMask` = 1). Свет, созданный кодом, получает `UniversalAdditionalLightData.renderingLayers` = 1 и освещает только слой 0: фонарик аварийного комплекта (`SurvivalItem`, объект `Light_Flashlight`) не светит на землю, деревья и стены, фальшфейер и сигнальная ракета — тоже. Правка: в `SurvivalItem` после каждого `AddComponent<Light>()` (фонарик, фальшфейер, ракета — три места) `GetUniversalAdditionalLightData().renderingLayers = uint.MaxValue` — все слои, как у ламп ядра без комнаты (`LightMask(null)`); для этого в `Snow.Runtime.asmdef` ссылка `Unity.RenderPipelines.Universal.Runtime` (у `Snow.Levels` она уже есть). Так же — любой будущий свет игры (фары машины). Теней у фонарика нет, поэтому его луч проходит сквозь стену в соседнюю комнату, как у ламп без зоны; тени фонарика — отдельное решение (OPEN, не делать). Проверка — QL13.

### L5. Sky Island — обновление острова
1. Сохранить открытую сцену. `Tools → Sky Island → Build Scene`. Ожидается: `[SkyIsland] SkyIsland: 2/2 giants placed.`, `[SkyIsland] Scene built for URP: Assets/SkyIsland/Scenes/SkyIsland.unity  (6178 objects)`. Остров получает PSX-текстуры по рецепту самолёта (`*_px`), великана под островом и грудную клетку маленького великана в лесу к северу от ВПП, 10-ю точку обзора `UnderTheIsland`, на камере острова — `SkyIslandPSX`.
2. `Tools → Sky Island → Capture Viewpoints (compare with previews)` — 10 кадров против `Assets/SkyIsland/Previews/` (они уже с PSX-текстурами, без кадра PS1); кадры и `report.txt` — прямо в `<проект>/SkyIslandCaptures/`; критерии L3.3.
3. Сразу после — `Tools → Snow → Integrate Sky Island Level` (пересборка стёрла самолёт, спавны, `Environment` и `NetworkManager`). Журнал (код на 16.10): `[Snow] Sky Island: plane at <…> on '<коллайдер>', heading 270.` — коллайдер ВПП, как в «до» (L1.4); `[Snow] Sky Island level integrated: Assets/SkyIsland/Scenes/SkyIsland.unity (plane in one piece, 4 spawn points), first in Build Settings.` Самолёт «в сборе»: груз, ключ, стремянку и зимний комплект выкладывает сам вариант (`PlaneLoadout`).
4. Камера игрока получает кадр PS1 (на камере острова теперь есть `SkyIslandPSX`): пешком и в кабине — 240 строк. Надписи приборов в кабине в этом кадре не читаются — это принятое решение пользователя, не чинить.
5. `UtilityPlane_Game` и стенд P03 отдельно не пересобирать: `Integrate Sky Island Level` сам строит вариант самолёта, если его нет.

### L6. Forest (с главным корпусом BaseV2)
С `Forest_Update2` на месте прежнего корпуса `ObsMain` стоит `BaseMain` — тот же префаб, что в локации BaseV2 (те же 131 деталь, имена `IX_MainDoor_1` … `IX_HeatingStop_131`, 16 комнат света, рольставни), в тех же координатах. Отличия от BaseV2: нет фонарей-заглушек двора и огня на заглушке мачты (у карты свои столбы и мачта), сеть — лесная, а не своя. Локация BaseV2 остаётся отдельной тестовой сценой.
1. `Tools/Snow/Integrate Location Level/Forest`. Журнал: `(32 doors, 142 zones, 129 synced parts, 16 rooms, 0 proximity groups, fall reset Y <y>, 4 spawn points)`.
2. Профиль — Приложение А.2: корпус целиком по профилю BaseV2 (19 `NetworkDoor`, мебель, выключатели света по комнатам, ставни: ремни и выключатели — с зонами, полотна `driven` — без), остальная карта — по своим строкам. Одни префиксы у корпуса и карты (`IX_GarageDoor`, `IX_MainSwitch`, `IX_Breaker`, `IX_Drawer`) — различать по модели (`BaseMain` или нет), а не только по префиксу.
3. Хост (`SkyIslandLocationHost` на корне `Forest`): `lights` — `SkyIslandLightManager` с корня (16 комнат корпуса), `grid` — лесной `SkyIslandPowerGrid` с корня. Комната выключателя — по `switchPrefix` комнат (BASEV2, U4.3.8).
4. Питание (старт без сети): свет появляется, когда включены все три — рычаг `IX_GenStart_86` и рубильник `IX_MainSwitch_90` в контейнере генератора, главный рубильник `IX_MainSwitch_75` в щитовой корпуса (подвал); любой выключенный — сети нет. С сетью: 16 ламп `Light_power` (антенны, контейнер, гараж, фонари двора) и светильники корпуса по своим комнатам — выключатели комнат на старте включены. Без сети горят штормовой фонарь у хижины, лампа в будке КПП, в корпусе 12 аварийных светильников и 4 табло NOTAUSGANG на батарее (`SkyIslandLightManager.batteryLife` = 0 — не садится; изменить — по решению пользователя, OPEN), красные огни мачты связи и антенн, свечи в часовне, фонарик в лагере. Красный заградогонь на мачте крыши корпуса (`FX_Obstruction_64`) — от сети, как в BaseV2: без неё не горит. Сервер решает, хост рассылает `_power` и `_rooms` (BASEV2, U4.2).
5. Люк шахты `IX_HatchLid_94` заперт («Заперто»); откатные ворота `IX_Gate_81` — вид Slide, общим путём, на своём слое, `speed` 0,15 (L4.1.8); шлагбаум, ворота контейнера и ворота гаража с квадроциклом (`IX_GarageDoor_92`) — `NetworkDoor` с ходом 3 с, ворота гаража-мастерской корпуса (`IX_GarageGate_18`) — 3 с, остальные двери — 1,2 с (TEMPORARY). Рольставни корпуса на старте подняты (так в данных пакета).
6. Спавн — у машины на дороге (`PlayerSpawn`, поворот 142°).
7. Текстуры (с `Forest_Update2`) — по рецепту самолёта (`docs\gdd\PSX_STYLE_FOR_ENVIRONMENT.md`), как у Sky Island: около 64 px на метр, палитра 64 цвета на текстуру с дизерингом Байера, затенение запечено в цвет, маски металла и гладкости `*_M_px`, блики включены, карт нормалей нет (кроме воды). База — корпус `BaseMain`, КПП, гараж, контейнер генератора и асфальт двора — с новыми текстурами по референсам (уточнение 17.10, README_Forest «Обновление 2»): 64 px на метр, палитра 32 цвета, новые файлы `T_BVPaintMetal_A_px`, `T_BVGalv_A_px`, `T_BVQuarry_A_px`, `T_BVRaisedFloor_A_px`, `T_BVLaminate_A_px`, `T_BVTerrazzo_A_px`, `T_BVCork_A_px`, `T_BVPegboard_A_px`, `T_RoofFelt_A_px` и маски `*_M_px` к ним; материалы корпуса `M_BV_*` в `Forest_Materials.json` теперь свои (новые текстуры, тайлинг, цвета, маски) — у локации BaseV2 остаются прежние, это не расхождение. У материалов с маской гладкость берётся из её канала A (ядро ставит `_Smoothness` = 1), значение `smoothness` в данных для них не используется. Импорт `*_px` (Point, без сжатия) и `*_M_px` (линейные) делает импортёр ядра — настраивать ничего не нужно; макротекстура рельефа и туман остались гладкими. Новые превью сняты с этими текстурами — сверка (L3.3) по ним.

### L7. AboveClouds, CoalPit, Dam — без открываемых частей
1. Меню интеграции для каждой. Журнал: `(0 doors, 0 zones, 0 synced parts, 0 rooms, 0 proximity groups, fall reset Y <y>, 4 spawn points)`; `NetworkObject` на корне не нужен.
2. **AboveClouds:** `fallY = -40` (море облаков без коллайдера, ниже −60 м рельефа нет). Спавн — на высокогорном аэродроме. ВПП для будущей посадки (координаты Unity): торец захода (650, 55, 270), верхний торец (300, 96, 95), ширина 30 м, уклон вверх ~10 %, посадка на восток-северо-восток. Как самолёт попадает сюда с Sky Island — DEFERRED.
3. **CoalPit:** спавн на платформе узкоколейки; спуск в разрез — серпантин от восточного борта; тлеющие пласты мерцают вместе со своим светом. `fallY` — по коллайдерам (дно разреза ниже борта на 135 м).
4. **Dam:** на корне `SkyIslandWaterLevel`: `minY` 98, `maxY` 126, старт 116, из кода `TargetLevel`, события `onDrainStarted/Finished`, `onFillStarted/Finished`. Рычага ещё нет — не делать (OPEN). У поверхности воды нет коллайдера, под ней дно: игрок может уйти под воду — правило воды решает пользователь (OPEN), записать.

### L8. Town — последней
1. `Tools/Snow/Integrate Location Level/Town`. Журнал: `(0 doors, 10399 zones, 10399 synced parts, 0 rooms, 62 proximity groups, fall reset Y <y>, 4 spawn points)` — «doors» здесь число `NetworkDoor`.
2. Состав (Приложение А.3): 2 216 дверей — 584 наружные (подъезды, магазины, бараки; вне групп) и 1 632 в интерьерах (квартиры, комнаты); 8 183 части мебели; 156 запертых: 103 створки подъездов (все 44 у 20 домов без интерьеров и 59 из 454 у 62 домов с интерьерами — генератор запирал каждую створку с вероятностью 15 %), 5 дверей магазинов, все 48 дверей бараков.
3. **Порог нагрузки.** Хост + 3 клиента (MPPM), время кадра на хосте и клиенте:
   - на `MainStreet`, `Square` и в подъезде, когда интерьеры включены;
   - худший кадр при проходе по `MainStreet`, когда группы включаются (около 160 частей, меши, `MeshCollider` и свет на группу; хост включает группы и рядом с клиентами);
   - с зажатой B: подсветка каждый кадр перебирает все 10 399 зон (`InteractionOverlay`: проверка на null, `activeInHierarchy`, рамка) до проверки 4 м; дорого — фильтровать по расстоянию раз в секунду в `Rescan` (с записью);
   - `ProfilerMarker` хоста локации (L4.4).
   Время входа клиента (синхронизация сцены) и отдельно — время открытия сцены Town: виртуальный игрок MPPM уже стоит в той же сцене, и NGO её не грузит, так что время входа загрузку не включает. Если код уровня даёт кадр дольше 33 мс или вход дольше 10 с — остановиться и сообщить с цифрами; варианты для пользователя: по сети только двери, мебель локально; меньше `margin` групп.

### L9. Документы
`docs/dev/ImplementationStatus.md`: раздел на каждую локацию — пакеты, сборка (журнал), сверка (`report.txt`), интеграция (журнал), проверки со статусами; OPEN и DEFERRED из раздела 0; ответ пользователя по L3.5.

## 3. Что проверить

Game View 1920 × 1080. «Проверено» — только с журналом (`BenchLog`, консоль) или снимком. Хост — в редакторе, клиенты — виртуальные игроки MPPM; кнопку Join в их окнах нажимает пользователь (попросить), без него сетевые пункты — NOT_RUN с причиной.

| № | Проверка | Ожидание |
|---|---|---|
| QL1 | импорт и сборка | у каждой локации пакеты по таблице без `error CS`; строки L3.2; сцены сохранены |
| QL2 | сверка 1 в 1 | `report.txt` каждой локации: таблица ΔL p5 / p50 / p95 по всем точкам; что выходит за критерии и почему |
| QL3 | Sky Island | самолёт на ВПП там же, где «до» (L1.4), в сборе: груз, ключ, стремянка, зимний комплект; спавны у двери пилота; великан под островом виден с края; кадр PS1 пешком и в кабине; падение с края — возврат на ближайший спавн, лицом как точка спавна, у клиента без «пролёта»; пассажир, стоящий в отсеке самолёта ниже −60 м, не сбрасывается; руление и взлёт как раньше; клиент видит то же; `implausible_move` при падении — ожидаемо |
| QL4 | Forest: питание | по очереди `IX_GenStart_86`, `IX_MainSwitch_90`, `IX_MainSwitch_75` — свет только после третьего: 16 ламп и ореолов карты, рабочие материалы ламп, экранов, светодиодов, светильники корпуса по комнатам (лампы разгораются по типу), аварийные гаснут; выключить любой — блэкаут: в корпусе 12 аварийных светильников и 4 табло, карточка выключателя света — «нет питания», на карте — лампы без сети (L6.4); выключатели комнат — как BASEV2 Q7 (наружный свет корпуса — второй выключатель в тамбуре; заградогонь на мачте крыши — только от сети); фонари двора Forest — лампы сети карты, без выключателя, зажигаются сразу с сетью (натриевых фонарей с ожиданием и разогревом, как в BASEV2 Q8, здесь нет); ставни — как BASEV2 Q9; у клиента так же, поздний вход — в том же состоянии |
| QL5 | двери | Forest: 31 `NetworkDoor` (все, кроме люка; корпус — 19, как BASEV2 Q5) открываются с обеих сторон, игрок в проёме — «упёрлась» (прошла сквозь игрока — буфер `NetworkDoor`, BASEV2 Q5); откатные ворота едут ~6,7 с; люк — «Заперто»; ворота гаража-мастерской корпуса (`IX_GarageGate_18`) и ворота гаража с квадроциклом (`IX_GarageDoor_92`) — разные двери со своим состоянием (подпись «Ворота гаража» у обеих — так в профилях, это не ошибка). Town: подъезд → лестница → открытая квартира → комната; запертый подъезд — «Заперто»; закрыть дверь, в проёме которой стоит игрок, — «Мешает игрок»; дверь, которая закрывается, когда игрок шагнул в проём, выталкивает его, а не запирает и не проталкивает сквозь себя (L4.1.2, записать условия, если иначе). Известно по данным: 33 из 38 дверей магазинов не заперты и ведут на первый этаж без интерьера (лестницы магазинов начинаются со второго) — записать, не чинить |
| QL6 | мебель и ставни | Forest: корпус — как BASEV2 Q6 и Q9 (ящики, дверцы стоек, шкафчики, тумбы, холодильник, щит, 6 ремней и 3 выключателя ставен, без сети выключатель ставен — «Нет питания»), карта — ящики будки КПП, шкафы антенн, рычаг и автоматы контейнера; Town: ящики, шкафы, гардеробы, стенки, холодильники, духовки, щитки, крышки пианино; дверца, открытая в игрока, его не запирает |
| QL7 | интерьеры Town | интерьеры появляются ближе 22 м к дому, без вспышки при старте сцены; клиент в доме далеко от хоста открывает двери и ящики, в журнале сервера `loc_group` — дом включён из-за клиента N; дальше 2,6 м от зоны сервер отказывает («Слишком далеко»), и для зоны в доме, выключенном у хоста (`InReach` по коробке, L4.4); груз, брошенный клиентом в доме, не проваливается — и после ухода всех игроков; давно открытая дверь у подошедшего уже открыта, без анимации; B показывает части в 4 м |
| QL8 | нагрузка | Town — числа L8.3; Forest (43 тыс. LOD-групп деревьев) — время кадра на `Start`, `Base`, `Woods` у хоста и клиента и заминка при старте сцены (`SkyIslandPowerGrid.Start` обходит все рендереры и трансформы под корнем) |
| QL9 | BaseV2 | L4.7: повторный `Tools/Snow/Integrate BaseV2 Level` через общий код — те же числа, что в BASEV2 U4.3.13, плюс `0 proximity groups, fall reset Y <y>`; проверки BASEV2 Q5–Q11 не сломались |
| QL10 | без интерактива | AboveClouds: спавн на аэродроме, падение в облака — возврат на спавн; CoalPit: спуск по серпантину до дна; Dam: дорога по гребню, вода на 116 м. Везде кадр PS1 и лук как в сверке (SMAA на кадре 240 строк — как в BASEV2 Q15) |
| QL11 | туман-триггеры Forest | у `FogUnderHand` и `FogRibs` туман сгущается при входе своего игрока (`densityScale` 2,6 и 2,2) и возвращается при выходе; у другого пира туман не меняется, пока в объём не войдёт его собственный игрок (L4.5) |
| QL12 | консоль и сеть | в каждом уровне нет ошибок у хоста и клиентов (`implausible_move` при падении — не ошибка); Build Settings: Sky Island первая, затем остальные; в сценах самолёта и стендов P01–P03 изменений нет |
| QL13 | свет игры | Forest ночью и корпус BaseV2: фонарик (`Surv_Flashlight_Net`, заспавнить сервером рядом с игроком, включить) освещает землю, деревья, стены и пол — снимки до и после правки L4.8; у клиента так же; на Sky Island — как раньше |

## 4. Не делать

- Не править то, что пришло в пакетах (скрипты, данные, модели, текстуры, README); сборщик пишет свои папки, меню интеграции — сцены. Не расставлять и не настраивать локации руками вместо сборщика; не менять их лук; не делать Apply переопределений в префабы локаций.
- Не менять общий URP-ассет, Graphics, Quality и Player Settings (тени — только по согласию и только компонентом сцены, L3.5).
- Не делать загрузочной другую сцену, Sky Island остаётся первой.
- Не ставить и не обновлять пакеты Package Manager; ставятся только `.unitypackage` из таблицы; старый Town v1 — никогда.
- Не придумывать геймплей: правила воды, рычаг водосброса, переходы между уровнями, смысл автоматов, топливо генератора.
- Не выключать кадр PS1 (решение пользователя).
- Не менять `Player.prefab` и `BenchBuilder` (тег `Player` — во время игры, L4.5).
- Не удалять файлы без разрешения; не писать руками YAML сцен, префабов, ассетов и `.meta`.

## 5. Отчёт

В формате §0 промпта: хэш коммита-точки отката (L1.3); по каждой локации отдельно: какие пакеты и как поставлены; журнал сборки (L3.2) и предупреждения; таблица из `report.txt`; журнал интеграции; результаты QL1–QL13 со статусами и доказательствами (снимки: Sky Island с ВПП и из кабины, Forest до и после включения сети, Town в подъезде и квартире, AboveClouds с аэродрома, CoalPit со дна, Dam с гребня; строки `BenchLog`); числа нагрузки (L8.3, QL8); изменённые и новые файлы кода с причиной; ответ пользователя по L3.5; что осталось OPEN и DEFERRED.

## Приложение А. Профили локаций

Все имена частей — `<префикс>_<n>`; сравнение — `StartsWith(префикс + "_")`, у Town — регулярными выражениями из А.3. Состояние 0 — `target = 0`.

### А.1 BaseV2
Как `BASEV2_INTEGRATION_UNITY.md`, Приложение А: 19 `NetworkDoor`, 112 зон, 112 частей по сети, 16 комнат света.

### А.2 Forest (161 часть; имена уникальны)

**Корпус (модель `BaseMain`, 131 часть)** — по профилю BaseV2: `BASEV2_INTEGRATION_UNITY.md`, Приложение А (подписи, `kind`, состояния, действия, комнаты выключателей; 19 `NetworkDoor`, 112 зон). Отличия: главный рубильник `IX_MainSwitch_75` на старте выключен (в BaseV2 — включён), он третий шаг туториала питания; включён, а сети нет (генератор или рубильник контейнера выключены) — подпись «включён, сети нет», как у рубильника контейнера.

**Остальная карта (30 частей):**

| Префикс | Модель | Шт. | Как | Подпись | `kind` | Состояние 0 / 1 | Действие |
|---|---|---|---|---|---|---|---|
| `IX_HutDoor` | `TowerHut` | 1 | `NetworkDoor` 1,2 с | Дверь хижины | door | по `DoorState` (как в BASEV2) | открыть / закрыть |
| `IX_CabinDoor` | `Cabin` | 1 | `NetworkDoor` 1,2 с | Дверь избушки | door | то же | то же |
| `IX_PumpDoor` | `PumpHouse` | 1 | `NetworkDoor` 1,2 с | Дверь насосной | door | то же | то же |
| `IX_ChapelDoor` | `Chapel` | 1 | `NetworkDoor` 1,2 с | Дверь часовни | door | то же | то же |
| `IX_BoothDoor` | `Checkpoint` | 1 | `NetworkDoor` 1,2 с | Дверь будки КПП | door | то же | то же |
| `IX_ContSideDoor` | `GenContainer` | 1 | `NetworkDoor` 1,2 с | Дверь контейнера | door | то же | то же |
| `IX_TrafoGate` | `Trafo` | 1 | `NetworkDoor` 1,2 с | Калитка трансформатора | door | то же | то же |
| `IX_GarageSideDoor` | `Garage` | 1 | `NetworkDoor` 1,2 с | Дверь гаража | door | то же | то же |
| `IX_ContDoorL`, `IX_ContDoorR` | `GenContainer` | 2 | `NetworkDoor` 3 с, 170° | Ворота контейнера | door | то же | то же |
| `IX_GarageDoor` | `Garage` | 1 | `NetworkDoor` 3 с, ось горизонтальная | Ворота гаража | door | то же | то же |
| `IX_BarrierArm` | `Barrier` | 1 | `NetworkDoor` 3 с | Шлагбаум | door | опущен / поднят (по `DoorState`) | поднять / опустить |
| `IX_HatchLid` | `Hatch` | 1 | `NetworkDoor`, заперт | Люк шахты | door | заперт | — («Заперто») |
| `IX_Gate` | `Gate` | 1 | общий путь, Slide 6,6 м, на своём слое, `speed` 0,15 (L4.1.8), препятствие при команде (L4.4) | Откатные ворота | door | закрыты / открыты (едут) | открыть / закрыть |
| `IX_CabinetDoor` | `Dish_1` … `Dish_5` | 5 | мебель | Шкаф антенны | hinge | закрыт / открыт | 0 — открыть, 1 — закрыть |
| `IX_Drawer` | `Checkpoint` | 6 | мебель | Выдвижной ящик | pull | задвинут / выдвинут | 0 — выдвинуть, 1 — задвинуть |
| `IX_Breaker` | `GenContainer` | 3 | мебель, без последствий (OPEN) | Автомат | toggle | включён / выключен | 0 — выключить, 1 — включить |
| `IX_MainSwitch` | `GenContainer` | 1 | мебель, сеть | Рубильник контейнера | toggle | выключен / включён (старт 0); включён без сети — «включён, сети нет» | 0 — включить, 1 — выключить |
| `IX_GenStart` | `GenContainer` | 1 | мебель, сеть | Пуск генератора | toggle | остановлен / работает (старт 0) | 0 — запустить, 1 — остановить |

Итого: `NetworkDoor` 32 (корпус 19 + карта 13; все с `min = 0`; люк заперт — открываются 31), общий путь — 1 (откатные ворота), остальное — 128 частей по сети (корпус 112, карта 16); зон 142 (у 19 полотен ставен зон нет). Префиксы `IX_GarageDoor`, `IX_MainSwitch`, `IX_Breaker`, `IX_Drawer` есть и у корпуса, и у карты — категорию и подпись брать по модели.

### А.3 Town (10 399 частей в сцене; id — `p<номер>`)

| Регулярное выражение имени | Категория | Как | Подпись | `kind` | Частей | Заперто | В группах |
|---|---|---|---|---|---|---|---|
| `^IX_Entrance_L\d+_\d+$` | дверь подъезда | общий путь, на своём слое | Дверь подъезда | door | 498 | 103 | 0 |
| `^IX_ShopDoor\d+_L\d+_\d+$` | дверь магазина | то же | Дверь магазина | door | 38 | 5 | 0 |
| `^IX_Door\d+_L\d+_\d+$` | дверь барака | то же | Дверь | door | 48 | 48 | 0 |
| `^IX_AptDoor\d+_\d+$` | дверь квартиры | то же | Дверь квартиры | door | 337 | 0 | 337 |
| `^IX_Door\d+_\d+$` | дверь комнаты | то же | Дверь | door | 1 295 | 0 | 1 295 |
| `^IX_Door\d+_\d+_\d+$` | дверца стенки | мебель | Дверца стенки | hinge | 562 | 0 | 562 |
| `^IX_GlassDoor[LR]\d+_\d+_\d+$` | стеклянная дверца | мебель | Стеклянная дверца | hinge | 562 | 0 | 562 |
| `^IX_Drawer\d*_\d+(_\d+)?$` | ящик | мебель, Slide | Выдвижной ящик | pull | 2 532 | 0 | 2 532 |
| `^IX_PanelDoor_\d+_\d+$` | щиток на лестнице | мебель | Дверца щитка | hinge | 1 159 | 0 | 1 159 |
| `^IX_CabDoor_\d+_\d+$` | шкаф | мебель | Дверца шкафа | hinge | 879 | 0 | 879 |
| `^IX_WallCab[LR]_\d+_\d+$` | навесной шкаф | мебель | Дверца навесного шкафа | hinge | 740 | 0 | 740 |
| `^IX_Wardrobe[LR]_\d+_\d+$` | гардероб | мебель | Дверца гардероба | hinge | 1 092 | 0 | 1 092 |
| `^IX_FridgeDoor_\d+_\d+$` | холодильник | мебель | Холодильник | hinge | 301 | 0 | 301 |
| `^IX_OvenDoor_\d+_\d+$` | духовка | мебель | Духовка | hinge | 301 | 0 | 301 |
| `^IX_PianoLid_\d+_\d+$` | пианино | мебель | Крышка пианино | hinge | 55 | 0 | 55 |

Заперто у подъездов: все 44 створки 20 домов без интерьеров и 59 из 454 у 62 домов с интерьерами.

Каждое имя попадает ровно в одно выражение (проверено по всем 1 176 описаниям частей). `IX_Door\d+_\d+` (дверь комнаты) и `IX_Door\d+_\d+_\d+` (дверца стенки) различаются числом групп цифр — не упрощать до префикса. Тексты: двери — «закрыта / открывается / открыта / закрывается / заперта», «открыть / закрыть»; ящики — «задвинут / выдвинут», «выдвинуть / задвинуть»; остальная мебель — «закрыта / открыта», «открыть / закрыть».

### А.4 Без открываемых частей

| Id | `fallY` | Особое |
|---|---|---|
| SkyIsland | −60 (в `SkyIslandLevelBuilder`, L4.6) | самолёт, груз; через меню проекта, не через `LocationLevelBuilder` |
| AboveClouds | −40 | море облаков без коллайдера; ВПП (L7.2) |
| CoalPit | по коллайдерам | спавн на платформе узкоколейки |
| Dam | по коллайдерам | уровень воды (L7.4) |

## Приложение Б. Ловушки

1. **Имена частей Town повторяются** (один дом стоит много раз): id по имени даст коллизии — только `p<номер>` по порядку обхода; пути иерархии тоже не уникальны.
2. **Группы интерьеров на сервере.** `SkyIslandProximityGroup` смотрит только на свою камеру; хост, стоящий далеко, держал бы дом клиента выключенным, и груз, брошенный клиентом в доме, провалился бы у сервера сквозь пол. Поэтому `LocationProximity` (L4.3).
3. **`Collider.ClosestPoint` у выключенного коллайдера возвращает саму точку** — дальность 0, проверка `InReach` пропустила бы всё. Поэтому `InReach` — по коробке зоны (L4.4).
4. **Откатные ворота — не `NetworkDoor`:** `NetworkDoor` умеет только поворот.
5. **Туман-триггеры:** событие привязано к контроллеру тумана камеры сцены, которую выключают при входе игрока, — перепривязать к камере игрока; тег `Player` — только своему игроку и во время игры (L4.5).
6. **Возврат при падении — только пешком и без родителя:** самолёт пролетает и под островом, и ниже края облаков, пассажир в отсеке — дочерний объект самолёта. Поворот — через `view.SetYaw`, иначе движок вернёт прежний (L4.2).
7. **Размер состояния:** у Town 10 399 частей — поэтому биты, а не байты (BASEV2, U4.2): 1,3 КБ вместо 10,4 КБ в стартовом сообщении каждого входящего; `MaxPayloadSize` транспорта не трогать.
8. **Скорость частей без `speed` в данных** — 1,4 хода в секунду: на длинном ходу (ворота 6,6 м) это 9 м/с; меню задаёт свою скорость (L4.1.8).
9. **`Release` на каждую нажатую зону** (с 16.10, `InteractionSensor`) — у деталей локаций без действий, иначе деталь переключится второй раз.
10. **Пересборка локации стирает интеграцию:** после любого Build / Rebuild — снова меню интеграции этой локации (у Sky Island — `Integrate Sky Island Level`).
11. **Кадр PS1 и экран:** всё, что считает пиксели камеры игрока, — через viewport (BASEV2, U4.1); новых таких мест в коде не добавлять.
12. **Деревья под хостом.** У растительности локаций есть капсулы, и все они лежат под корнем с хостом: любой перебор «все коллайдеры под хостом» (`BuildEmbedded`, поиск в `Awake` и т. п.) на Forest и Town дорогой — встроенные зоны выключены (L4.4), новых таких переборов не писать.

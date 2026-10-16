# Главный корпус BaseV2 в игре: ядро локаций v2.7.1, сборка корпуса, сетевой уровень — задание для AI-агента

Версия 1.1 от 16.10.2026. Действует вместе с `PLANE_PROMPT_UNITY.md` (v2.1): правила работы, статусы и формат отчёта — оттуда (§0). Если пункт здесь расходится с промптом, верно то, что написано здесь.

Изменения 1.1 (после независимой проверки по коду и после переезда папок проекта 16.10): пути — `source\locations`, `docs\dev`; точка отката — коммит в git (U1.2); `HoverOutline` — третий изменяемый файл `Snow.Runtime`, проверка по `targetTexture` и ширина рамки зоны (U4.1); шире поиск экранных координат и проверка других камер (U4.1); обоснование битов и `Release` (U4.2); проверки `lights` / `grid` на null в Приложении В; Q5, Q15. Если U4.1 уже сделан по версии 1.0 — дополнить, переделывать не нужно.

Проект: `F:\ClaudeGames\snow\Snow`, Unity 6000.0.67f1, URP 17.0.4 (`PC_RPAsset`, рендерер Forward+), NGO 2.13.3, Multiplayer Play Mode 1.6.3.
Пакеты (`.unitypackage`, ставятся в U2):
- ядро локаций — `F:\ClaudeGames\snow\source\locations\SkyIsland\`: `SkyIsland_Update_1`, `SkyIsland_Update_2`, `SkyIsland_Update3_1`, `SkyIsland_Update5`, `SkyIsland_Update6`, `SkyIsland_Update7`, `SkyIsland_Update8`;
- корпус — `F:\ClaudeGames\snow\source\locations\BaseV2\BaseV2.unitypackage` → `Assets/Locations/BaseV2`.

Справка после импорта: `Assets/SkyIsland/Scripts/CHANGES_v2.md` (разделы v2.3–v2.7.1: питание, светильники, интерактив, рольставни), `Assets/SkyIsland/README.md` (раздел «Цепочка обновлений с ядра v1»), `Assets/Locations/BaseV2/README.md` (помещения, интерактив, свет, размеры; копия — `F:\ClaudeGames\snow\docs\context_pack\README_BaseV2.md`). Стиль окружения: `F:\ClaudeGames\snow\docs\gdd\PSX_STYLE_FOR_ENVIRONMENT.md`. Картинки: `F:\ClaudeGames\snow\source\locations\BaseV2\*.jpg` (они же в `docs\context_pack`), `Assets/Locations/BaseV2/Previews/`.

Задание составлено по коду проекта на 16.10.2026 (`InteractionHost`, `InteractionSensor`, `HoverOutline`, `InteractionOverlay`, `NetworkDoor`, `DoorModel`, `PlayerNet.UseRpc`, `SnowNetworkBootstrap`, `BenchBuilder`, `SkyIslandLevelBuilder`, `SkyIslandPlayerCamera`, `HLCharacterMotor`) и по данным корпуса; числа сверены с `BaseV2_Layout.json` и `BaseMain.fbx`. Образцы в проекте: уровень — `Editor/SkyIslandLevelBuilder.cs`; зоны — `BenchBuilder.Volume`; хост с дверью — `Runtime/Bench/BenchCabin.cs` и `BenchBuilder.BuildCabin`.

## 0. Что и зачем

Пользователь (16.10), после первого прохода детализации корпуса: «Теперь надо интегрировать её в игру».

BaseV2 — главный корпус обсерватории Hohenmoos: подвал, два этажа, крыша; тамбур, пульт, коридоры, склад, лестница, серверная, гараж-мастерская, казарма на 4, кухня, санузел, кабинет начальника, котельная, щитовая. Корпус — один префаб `BaseMain` в своей сцене (двор, заглушки построек, лес конусами). В нём 131 подвижная деталь (`SkyIslandInteractable`), 70 светильников с поведением (`SkyIslandLightFixture`), свет по комнатам и сеть с главным рубильником (`SkyIslandLightManager`, `SkyIslandPowerGrid` на корне локации), 19 рольставен с ремнями и электроприводами. Сейчас всё это работает только у одного человека с fly-камерой: детали двигает `SkyIslandInteractor`, локально, без сети.

Цель — сетевой уровень BaseV2: до 4 игроков (хост + 3 клиента, CORE-05) появляются во дворе, ходят по всем этажам и делают руками то же, что fly-камера: двери, ящики, шкафчики, выключатели света, главный рубильник (блэкаут → аварийный свет), рольставни, аварийный выключатель отопления. Сервер решает (NET-01, NET-02), все видят одно и то же, включая того, кто подключился позже. Взаимодействие — системой проекта: луч `InteractionSensor`, зоны `UtilityPlaneInteractable` на слое Interact, `PlayerNet.UseRpc` с проверкой дальности, `InteractionHost.ServerUse`, карточка `InteractionOverlay`, контур `HoverOutline` (INPUT-02); двери — `NetworkDoor`, они не закрываются сквозь игрока (DOOR-01).

**Состояние проекта (проверено 16.10, повторить в U1):**
- `Assets/SkyIsland` — ядро v1: в `Scripts/Editor` 8 скриптов и asmdef, в `Scripts/Runtime` 7 и asmdef, `SkyIslandLocation.cs` нет; `Assets/Locations` нет. Уровень `Assets/SkyIsland/Scenes/SkyIsland.unity` собран `Tools/Snow/Integrate Sky Island Level` и стоит первым в Build Settings.
- `Assets/SkyIsland/Scripts/Editor/SkyIslandLook.cs` в проекте изменён 01.10: кубмапа отражения неба переводится из sRGB в линейные значения. Пакеты Update_1, Update5 и Update7 содержат этот файл без правки; правка перенесена в ядро — v2.7.1 в `SkyIsland_Update8`, поэтому после всей цепочки она на месте.
- Активный уровень качества — PC. `PC_RPAsset`: Forward+, Use Rendering Layers вкл., тени дополнительных источников вкл., HDR, Grading HDR, LUT 32, Per Object Limit 8, GPU Resident Drawer выкл. `Mobile_RPAsset` (качество Mobile): тени дополнительных источников **выкл.** Rendering Layers 0–7 названы (`Default`, `Light Layer 1…7`). Слой `Interact` — 8, в матрице столкновений ни с чем не сталкивается. Камеры проекта рисуют все слои (`cullingMask` нигде не задаётся).
- Игрок (`Player.prefab`): капсула радиус 0,34 м, рост 1,80 м, шаг 0,35 м, уклон 45°, движок `HLCharacterMotor`, `collisionMask` без слоя Interact. Корпус проверен на проходимость и с этой капсулой (Q4).
- Клавиши: «использовать» — E; показ интерактивного вокруг — B (Reveal, перенесено с Alt 30.09).

**Решение пользователя — кадр PS1 у игрока (CONFIRMED).** 15.10: «кадр PS1 во всех локациях»; 16.10, на вопрос про читаемость приборов самолёта v12 в 240 строках: «PS1 везде, и в кабине тоже». Камера игрока в уровнях локаций получает `SkyIslandPSX` с камеры локации: 240 строк, 15-битный цвет, дизеринг Байера 4 × 4, увеличение без сглаживания; самолёт и его кабина — тоже в этом кадре (U4.1).

**Объём.** В задании: ядро v2.7.1, сборка корпуса сборщиком пакета, уровень с сетью и взаимодействием, проверки. Не в задании (**DEFERRED**): корпус на карте `Forest` вместо `ObsMain`, NavMesh и существа, звук (события ядра для него уже есть), сохранения, ключи и замки, ночь (лук локации — пасмурный день). Что в игре значат аварийный выключатель отопления, автоматы и поворотные выключатели щитков — **OPEN** (сейчас у них только положение, без последствий).

## 1. Что затронуто в проекте

| Где | Что |
|---|---|
| `Assets/SkyIsland/**` | ядро v1 → v2.7.1 цепочкой из 7 пакетов (те же GUID, меняются только новые и изменённые файлы): все скрипты, шейдеры `LightGlow` и `Skybox`, новые `Smoke`, `InteriorWindow`, `PSXPresent`, данные и текстуры острова, скелет великана. Сцена `SkyIsland.unity` не пересобирается (U2.6) |
| `Assets/Locations/BaseV2/**` | новый пакет. `Scenes/`, `Prefabs/`, `Materials/`, `Lighting/`, `Settings/` делает сборщик (U3); сцену `Scenes/BaseV2.unity` затем дополняет меню интеграции (U4.3). `Data/`, `Models/`, `Textures/`, `README.md` не меняются |
| активный URP-ассет | сборщик (`SkyIslandURP.SetupURPPost`) проверяет HDR, LUT 32, Grading HDR, Per Object Limit ≥ 8 и включает недостающее; у `PC_RPAsset` всё уже так |
| все URP-ассеты | сборщик (`SkyIslandLighting.SetupLightingProject`) включает Use Rendering Layers и тени дополнительных источников: изменится только `Mobile_RPAsset` (U1.6) |
| `ProjectSettings/TagManager.asset` | сборщик назовёт Rendering Layers 8 и 9 (корпусу нужны 0–9) |
| `Assets/Snow/Levels/SkyIslandPlayerCamera.cs` | туман копируется целиком; кадр PS1 на камеру игрока (U4.1) |
| `Assets/Snow/Runtime/Interaction/InteractionOverlay.cs`, `InteractionSensor.cs`, `HoverOutline.cs` | экранные координаты через viewport, ширина контура и рамки зоны — с кадром PS1 камера рисует в RenderTexture (U4.1) |
| `Assets/Snow/Levels/SkyIslandLocationHost.cs` | новый: мост «детали ядра ↔ сетевое взаимодействие проекта» (U4.2) |
| `Assets/Snow/Levels/Snow.Levels.asmdef` | + `UtilityPlane.Runtime` (нужен из-за сигнатур `InteractionHost`); `Unity.Collections` — если потребует компилятор |
| `Assets/Snow/Editor/BaseV2LevelBuilder.cs` | новый: меню `Tools/Snow/Integrate BaseV2 Level` (U4.3) |
| `Assets/Snow/Editor/Snow.Editor.asmdef` | + `SkyIsland.Runtime` |
| `ProjectSettings/EditorBuildSettings.asset` | `BaseV2.unity` — в конец списка, не первой (U4.3.12) |
| `docs/dev/ImplementationStatus.md` (`F:\ClaudeGames\snow\docs\dev\`) | раздел BaseV2 (U6) |

Файлы самолёта, стенды P01–P03, `Player.prefab`, `DefaultNetworkPrefabs.asset`, Volume-профили проекта не меняются; из `Snow.Runtime` меняются только три файла взаимодействия (U4.1).

## 2. Шаги

### U1. Подготовка и аудит (без изменений)
1. Выйти из Play Mode. Перед каждой компиляцией проверять состояние редактора (R-4).
2. Точка отката. С 16.10 `F:\ClaudeGames\snow` — git-репозиторий (README проекта: вместо ручных копий — коммит перед большим изменением; риск R-3 «нет git» в `ImplementationStatus.md` устарел): закоммитить текущее состояние до U2 (`git add -A`, `git commit -m "Before BaseV2 integration"`), хэш — в отчёт. Если коммит агенту недоступен — копия вне репозитория в `F:\ClaudeGames\snow_archive\backup\basev2_<дата>\`: `Assets/SkyIsland` целиком (≈120 МБ), `Assets/Snow/Levels`, `Assets/Snow/Editor`, `Assets/Snow/Runtime/Interaction`, `Assets/Snow/Bench/*.unity`, `Assets/DefaultNetworkPrefabs.asset`, `Assets/Settings/*.asset`, `ProjectSettings/TagManager.asset`, `QualitySettings.asset`, `EditorBuildSettings.asset` — всё с `.meta`.
3. Версия ядра по файлам `Assets/SkyIsland/Scripts`: нет `Editor/SkyIslandLocation.cs` — v1; есть `Runtime/SkyIslandInteractable.cs` — не ниже Update3_1; `Runtime/SkyIslandPowerGrid.cs` — не ниже Update5; `Runtime/SkyIslandLightManager.cs` — не ниже Update7; в `CHANGES_v2.md` есть раздел «v2.7.1» — ядро уже новое, сразу U2.8. Цепочку начинать с первого пакета после стоящей версии (на 16.10 — с `SkyIsland_Update_1`).
4. Локальные правки ядра: сравнить скрипты `Assets/SkyIsland/Scripts` с содержимым пакетов. Известна одна — `SkyIslandLook.cs` (01.10, кубмапа неба), она вошла в v2.7.1. Если есть ещё файлы или правки — записать (diff, имя файла); после цепочки возвращать только то, без чего снова ошибка, и сообщить. Лишний свой файл в `Scripts/Editor` изменит счёт в U2.3 — учесть.
5. Записать «до» для отчёта: значения из раздела 0 (URP-ассеты обоих уровней качества, включая Per Object Limit, рендерер, Rendering Layers, слой Interact), активный уровень качества, порядок сцен в Build Settings, даты изменения `Assets/Snow/Bench/*.unity`, `SkyIsland.unity` и `DefaultNetworkPrefabs.asset` (для U5.3).
6. **Глобальные изменения — сообщить пользователю одним сообщением до U3, ответа не ждать.** При сборке корпуса:
   - `SkyIslandLighting.SetupLightingProject` во всех URP-ассетах (активный, по умолчанию, каждого уровня качества) включает Use Rendering Layers и тени дополнительных источников. В `PC_RPAsset` всё уже так; изменится `Mobile_RPAsset` (`m_AdditionalLightShadowsSupported` 0 → 1). Строка-доказательство: `[SkyIsland] BaseV2: URP assets updated (1 settings): …`;
   - Rendering Layers 8 и 9 получат имена — остаются в любом случае: без имени URP слой не использует, а свет комнат корпуса на них держится;
   - `SetupURPPost` правит **активный** URP-ассет — поэтому во время U3 активным должно быть качество PC (у `PC_RPAsset` изменений не будет).
   Если пользователь против изменения `Mobile_RPAsset` или не ответил — после U3 вернуть его значение 0 через `SerializedObject` (как `SkyIslandLighting.SetAssetBool`) или в инспекторе, не правкой YAML, и записать; на качестве PC ничего не меняется. Сборщик включает его снова при каждой пересборке корпуса. Согласится позже — поставить 1 тем же способом.

### U2. Ядро v2.7.1 и пакет корпуса
1. Порядок строгий: `SkyIsland_Update_1` → `SkyIsland_Update_2` → `SkyIsland_Update3_1` → `SkyIsland_Update5` → `SkyIsland_Update6` → `SkyIsland_Update7` → `SkyIsland_Update8` → `BaseV2`. `Update_2` — вторая часть обновления 1 (только текстуры и превью). Пакета Update4 нет: его версия v2.3 вошла в Update5. Обновление ставится поверх `Assets/SkyIsland` (те же GUID) и меняет только новые и изменённые файлы; корпус ставится в `Assets/Locations/BaseV2`.
2. Импорт — по одному пакету, со следующим только после конца импорта и компиляции:
   - редактор открыт и через доступный канал можно выполнить код редактора — `AssetDatabase.ImportPackage(path, false)`; дождаться `AssetDatabase.importPackageCompleted` (или `importPackageFailed`), затем перезагрузки домена и `!EditorApplication.isCompiling && !EditorApplication.isUpdating` (R-4: связь с редактором на перезагрузке может прерваться — дождаться и проверить состояние); между пакетами в консоли не должно быть `error CS`;
   - редактор закрыт — batchmode, без `-nographics`, с журналом, в котором нет `error CS`:
     `"<Unity.exe>" -batchmode -projectPath "F:\ClaudeGames\snow\Snow" -importPackage "F:\ClaudeGames\snow\source\locations\SkyIsland\SkyIsland_Update_1.unitypackage" -quit -logFile "%TEMP%\basev2_import_1.log"`;
   - ни то ни другое недоступно — попросить пользователя импортировать 8 пакетов по порядку (Assets → Import Package → Custom Package…, всё отмечено, дожидаясь компиляции).
3. После цепочки: в `Assets/SkyIsland/Scripts/Editor` 13 скриптов и asmdef (`SkyIsland` + `Builder, Capture, Data, Emitters, Import, Json, Lighting, Location, LocationWindow, Look, Materials, Prefabs, URP`), в `Scripts/Runtime` 20 и asmdef (`Beacons, BuiltinPost, Flicker, FlyCamera, FogByAltitude, FogEvent, Interactable, Interactor, LightFixture, LightManager, MaterialSwap, PSX, PowerGrid, ProximityGroup, RadarRotator, Rotator, Sky, UVScroll, Viewpoint, WaterLevel`); в `CHANGES_v2.md` есть «v2.7.1»; в `SkyIslandLook.cs` есть строка `decoded to linear`.
4. Консоль без ошибок во всех сборках проекта. `Snow.Levels` берёт у `SkyIslandFogByAltitude` поля `target, groundDensity, airDensity, island, islandTopY, heightRange, distanceRange, applyInEditMode` — в v2.7.1 они на месте. Если код Snow не собирается из-за ядра — минимальная правка в коде Snow с записью. Файлы ядра не править; исключение — минимальная совместимость API установленной версии Unity, без изменения логики, с отчётом.
5. Меню на месте: `Tools → Locations → Location Builder`, `Tools → Sky Island → Build Scene`.
6. Уровень Sky Island: открыть `Assets/SkyIsland/Scenes/SkyIsland.unity` — без Missing Script и ошибок, Play как раньше. **Не пересобирать** Sky Island в этом задании: пересборка стирает интеграцию (самолёт, груз, спавны) и делается по отдельному заданию `LOCATIONS_UPDATE_UNITY.md` (шаг L5: `Tools → Sky Island → Build Scene`, затем `Tools → Snow → Integrate Sky Island Level`).
7. Импорт корпуса: текстуры `Assets/Locations/BaseV2/Textures/*_px.png` сам настраивает постпроцессор ядра — Point, мипы, без сжатия, Aniso 0, маски `*_M_px` линейные (`PSX_STYLE_FOR_ENVIRONMENT.md`, раздел 4). Проверить на 3–4 текстурах; свой постпроцессор не писать.
8. Если ядро уже было v2.7.1 (U1.3) — только импорт `BaseV2`, затем U3.

### U3. Сборка корпуса сборщиком пакета
1. Активное качество — PC (U1.6). Сохранить открытую сцену: сборщик заменяет её без вопроса. `Tools → Locations → Location Builder` → `BaseV2` → **Build** (после первой сборки кнопка называется **Rebuild**) или `SkyIsland.EditorTools.SkyIslandBuilder.BuildLocation("BaseV2")`. Сцена — `Assets/Locations/BaseV2/Scenes/BaseV2.unity`.
2. Ожидаемые строки журнала (порядок видов в скобках может отличаться; `<…>` — числа, которые здесь не посчитать):
   - `[SkyIsland] interactables in BaseMain: 9 drivers, 19 driven parts.`
   - `[SkyIsland] BaseV2: 70 light fixtures; rendering layer masks set on <N> renderers.`
   - `[SkyIsland] BaseV2: URP assets updated (1 settings): Lighting > Use Rendering Layers on, Additional Lights > Cast Shadows on.` — только если `Mobile_RPAsset` был 0;
   - `[SkyIsland] BaseV2: rendering layers 0..9 used by 16 light zones (<k> layer name(s) added).` — при первой сборке `<k>` = 2;
   - `[SkyIsland] BaseV2: light manager: 70 fixtures (23 Fluorescent, 21 Incandescent, 12 Emergency, 6 HPS, 4 Exit, 2 Halogen, 2 Beacon), 70 lights, 16 hero, 16 rooms, 19 switches, grid: yes, shadows: 3 within 14 m.`
   - `[SkyIsland] BaseV2: power grid: 0 lights, 0 halos, 2 lamp material pairs, switches: IX_MainSwitch.` — нули правильные: лампы называются `Light_fx_*` / `Glow_fx_*`, их ведёт менеджер света, а сеть меняет экраны и светодиоды (2 пары материалов);
   - `[SkyIsland] BaseV2: 16 powered interactables on the grid.`
   - `[SkyIsland] BaseV2: PlayerSpawn at <…>, yaw -172.`
   - `[SkyIsland] Scene built for URP: Assets/Locations/BaseV2/Scenes/BaseV2.unity  (<N> objects). Play mode: …` (два пробела перед скобкой);
   - не должно быть предупреждений `children not found`, `drives not found`, `missing world model`, `lights name rooms that layout.lighting does not list`.
3. Проверить сцену скриптом в редакторе (без сохранения): в `BaseMain` 131 `SkyIslandInteractable` — 86 Hinge, 26 Slide, 19 Scale; у 9 непустой `linked` (6 ремней — по одному полотну; выключатели ставен: пульт 6, кухня 4, кабинет 3); 19 `driven`; 16 `needsPower` с заполненным `grid`; на корне `BaseV2` — `SkyIslandLightManager` (16 комнат) и `SkyIslandPowerGrid` (`switches` = `IX_MainSwitch`, `startPowered`); 70 `SkyIslandLightFixture`. Интерактивов 0 — значит, стоит ядро ниже v2.7 (ошибка «фальшивого null», `CHANGES_v2.md`, v2.7).
4. Проверка пакета fly-камерой — Play в `BaseV2.unity` до U4.3: клавиши 1–9 — точки обзора; F или ЛКМ двигает детали; рубильник в щитовой гасит сеть и включает аварийный свет; ставни ездят. По желанию — `Capture` в окне Location Builder (сверка с `Previews/`). Потом сцена станет сетевой, и fly-камера останется только до входа игрока.

### U4. Код Snow
Связанные требования: NET-01, NET-02 (сервер решает, клиенты просят), INPUT-02 (наведение и карточка), DOOR-01 (двери не проходят сквозь игрока и груз), CORE-05 (до 4 игроков).

**Имена деталей.** У каждой детали есть номер: `IX_MainDoor_1`, `IX_ExitDoor_2`, `IX_Door_3` … `IX_Door_16`, `IX_RoofDoor_17`, `IX_GarageGate_18`, `IX_GarageDoor_19`, `IX_FridgeDoor_58`, `IX_MainSwitch_75`, `IX_PanelDoor_83`, `IX_HeatingStop_131` и т. д. Префиксы в этом задании (Приложение А) сравнивать только как `name.StartsWith(prefix + "_", StringComparison.Ordinal)`. Не `Contains("Door")`: так в двери попадут дверцы шкафчиков, стоек, тумб, холодильника и щита.

**U4.1. `SkyIslandPlayerCamera`** (`Snow.Levels`).
- Туман копировать целиком: `JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(fogFrom), fog); fog.target = cam;` — так переходят и поля v2.1–v2.4 (`thinBelow, bottomY, linear, groundStart, groundEnd, airFactor`). У BaseV2 туман линейный (`fog.mode = Linear`, 60–700 м): без этих полей камера игрока водила бы плотность экспоненциального тумана, а линейный не редел бы с высотой и расстоянием.
- **Кадр PS1 (CONFIRMED, раздел 0).** Новое поле `public bool copyPsxFrame = true`: если на камере сцены есть `SkyIslandPSX` — добавить такой же на камеру игрока. `SkyIslandPSX.OnEnable` читает `canvasOrder` и `presentShader`, а `AddComponent` на активной камере вызывает его сразу, поэтому: добавить, выключить, скопировать `height, colorBits, dither, presentShader, canvasOrder`, включить. Поле оставить, чтобы кадр можно было снять одной галкой; по умолчанию — включён. Где на камере сцены `SkyIslandPSX` нет (уровень Sky Island до пересборки под новое ядро, стенды P01–P03), ничего не меняется. `SkyIslandPSX` на камере сцены снимает свой экран в `OnDisable`, когда камеру сцены выключают.
- **Экранные координаты.** С кадром PS1 камера игрока рисует в RenderTexture (~427 × 240), и всё, что считает пиксели камеры, попадает не туда:
  - `InteractionOverlay.ScreenRect`: вместо `cam.WorldToScreenPoint(w)` — `cam.WorldToViewportPoint(w)`, затем x × `Screen.width`, y × `Screen.height` (z — как есть): рамки и карточка встанут на предмет и с кадром, и без него;
  - `InteractionSensor`, луч курсора в кресле: вместо `cam.ScreenPointToRay(f.Pointer)` — `cam.ViewportPointToRay(new Vector3(f.Pointer.x / Screen.width, f.Pointer.y / Screen.height, 0f))`;
  - `HoverOutline` (`Snow.Runtime`; ссылки на `SkyIsland.Runtime` у этой сборки нет, поэтому признак кадра — `cam.targetTexture != null`, а не `SkyIslandPSX`; камера — `sensor.view.cam`): (1) ширина контура силуэта задаётся в пикселях кадра камеры — с кадром 240 строк 3 px станут ~13 px экрана, поэтому 1 px; (2) рамка зоны у больших деталей (двери, ворота, дверцы шкафов, холодильник) — `LineRenderer` шириной `lineWidth` = 0,004 м в мире, а пиксель кадра 240 строк на 1,5 м при угле обзора 60° ≈ 7 мм: рамка рвётся и мерцает. Ширину линии брать не меньше 1,2 пикселя кадра на расстоянии `d` от камеры до центра рамки: `max(lineWidth, 1.2f * 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / cam.targetTexture.height)`. Обе величины TEMPORARY, по снимку.
  Поиск 16.10 шёл только по копиям кода Snow и по четырём словам. Повторить шире — `Assets/Snow` и `Assets/UtilityPlane/Scripts`: `WorldToScreenPoint`, `ScreenToWorldPoint`, `ScreenPointToRay`, `ViewportToScreenPoint`, `pixelRect`, `pixelWidth`, `pixelHeight`, `targetTexture`, ширины `LineRenderer` в метрах; найденное, что считает пиксели камеры игрока, — через viewport или по правилу рамки выше, с записью. IMGUI (`OnGUI`: карточка, точка прицела, меню разработчика, телеметрия F10) и Canvas в режиме Screen Space Overlay рисуются поверх кадра в полном разрешении экрана.
  - Другие камеры. `SkyIslandPSX` создаёт камеру `PSX_Screen` с глубиной «камера игрока + 100», которая очищает экран чёрным: кадр любой другой Base-камеры без `targetTexture` с меньшей глубиной будет стёрт. На экран в уровне должна рисовать только камера игрока (камеры приборов в `RenderTexture` не мешают); нашлась другая (HUD, UI) — сообщить, кадр не выключать.
  - Сглаживание. Сборщик ставит камере локации SMAA High и дизеринг URP, U4.1 копирует их камере игрока; они работают на кадре 427 × 240 и смягчают края против `Previews/PSX240/`. Записать в Q15 со снимком; кадр и эти настройки не менять без решения пользователя.
- Остальное без изменений; уровень Sky Island работает как раньше, пока его не пересобрали.

**U4.2. `SkyIslandLocationHost : InteractionHost`** (`Snow.Levels`, ENGINEERING; общий для локаций ядра, BaseV2 — первая).
Компонент на корне локации (`BaseV2`, корень сцены) рядом с `NetworkObject`: `InteractionSensor` ищет хоста через `GetComponentInParent`, `PlayerNet.UseRpc` — через `TryGetComponent` на объекте `NetworkObject`; менеджер света и сеть уже на этом корне. `base.Awake()` вызывать (он собирает зоны).

Данные (сериализуются, заполняет U4.3):
- `parts[]`: `id` (имя детали = id зоны), `SkyIslandInteractable part`, `NetworkDoor door` (у дверей, иначе null), `Category category` (Приложение А), `string room` (у выключателей света и ставен);
- `SkyIslandLightManager lights`, `SkyIslandPowerGrid grid` (с корня; в других локациях одного или обоих нет — тогда `_rooms` / `_power` не используются, проверки на null).
- id зоны — имя детали, если имена в локации уникальны (BaseV2); иначе — сгенерированный короткий id (`p<номер>`), см. `LOCATIONS_UPDATE_UNITY.md` (Town). Хост id не разбирает, только сравнивает.

Сетевое состояние (пишет только сервер):
- `NetworkList<ulong> _bits` — по биту на каждую деталь без `NetworkDoor` (0 — закрыта / выключена / опущена, 1 — наоборот), деталь `i` — бит `i % 64` элемента `i / 64`, в порядке массива `parts` (ordinal-сортировка имён в U4.3; где имена повторяются — порядок обхода, `LOCATIONS_UPDATE_UNITY.md`, L4.1.4); создать в `Awake`. Биты, а не байт на деталь: у Town больше 10 тыс. деталей — 1,3 КБ вместо 10,4 КБ в стартовом сообщении каждого входящего клиента; один формат для всех локаций. `MaxPayloadSize` транспорта (6144 байта) ограничивает ненадёжные сообщения, надёжные дробятся — его не менять;
- `NetworkVariable<bool> _power` — `grid.Powered`;
- `NetworkVariable<uint> _rooms` — бит `i` = `lights.IsRoomOn(lights.rooms[i].name)` (16 комнат);
- `NetworkVariable<uint> _layoutHash` — FNV-1a по именам деталей и комнат в том же порядке. Клиент сверяет в `OnNetworkSpawn` и при каждом входе пишет строку `BenchLog` `loc_hash` (совпал / нет); при расхождении — ещё и ошибка в консоль «сцены хоста и клиента собраны разными версиями».
Двери синхронизирует сам `NetworkDoor`.

Сервер:
- `OnNetworkSpawn`: `_bits.Clear()` (повторный Host в той же сессии Play заспавнит объект со старым списком), затем заполнить из `part.target` (стартовые положения из данных: выключатели, рубильник и аварийный выключатель включены, ставни подняты, двери и ящики закрыты), `_power`, `_rooms`, `_layoutHash`.
- `ServerUse(clientId, id, phase, out reason)`: `Release` — `true` без действий (с 16.10 `InteractionSensor` после зимнего комплекта шлёт `Release` в нажатую зону для всех зон, не только `momentary`: деталь не должна переключиться второй раз). `part.locked` → «Заперто» (и у дверей: в других локациях есть запертые). Дверь → `door.Toggle(DoorDrive.Manual)`. Иначе по порядку: `driven` → «Управляется изнутри»; `!part.HasPower` → «Нет питания»; иначе `part.Toggle()` — дальше работает логика ядра: ремень и выключатель ведут полотна (`linked` → `Follow`), выключатель света переключает комнату (`onToggled` → менеджер света), рубильник — сеть (`onToggled` → `SetPower`). Затем «опубликовать» (ниже).
- `LateUpdate` (только при `IsSpawned`; до старта сессии объект не заспавнен): «опубликовать» — пройти детали, записать в `_bits` только изменившиеся элементы, обновить `_power` и `_rooms`. Так в сеть уходит и то, что меняет не игрок (будущие сценарные события, генератор). В кадрах без изменений — без выделений памяти. При каждом изменении — строка `BenchLog` `loc_state` (номер элемента и изменённые биты; power; rooms) — доказательство для Q10–Q11.

Клиент (не сервер):
- **Детали двигать только полями, без методов ядра:** `part.target = v; part.enabled = true;`. `Toggle / Open / Close / Follow` поднимают `onToggled` — менеджер света второй раз переключил бы комнату, а сеть — рубильник; `SetTarget` и `Follow` к тому же ведут ведомые детали (`Drive`) и проверяют питание, которое на клиенте может прийти позже.
- `OnNetworkSpawn` (в том числе поздний вход): сначала `grid.startPowered = _power.Value` (это поле; если `Start` сети ещё не прошёл, лампы сразу встанут как у хоста), затем все детали сразу, без анимации — `openAmount = target = v; Apply(); enabled = false;`.
- `LateUpdate` (только при `IsSpawned`): деталь, у которой `target` расходится со своим битом, — выставить (едет ядро); `grid.Powered != _power.Value` — `grid.SetPower(_power.Value)`; комната, где `lights.IsRoomOn` расходится с битом, — `lights.SetRoom(name, bit)`. Сверять каждый кадр, а не по событию: тогда не важно, в каком порядке пришли переменные и успел ли `Start` сети (`SkyIslandPowerGrid.Start` принудительно ставит `startPowered`). `OnListChanged` можно не слушать.
- Полотна с приводом сами ждут при `!HasPower` — это читается из `grid`, который выставлен по `_power`, поэтому у клиентов они стоят и едут так же, как у хоста.
- `NetworkList` создаётся в `Awake`: проверить, что выход из Play без запуска хоста не даёт предупреждения `A Native Collection has not been disposed`; если даёт — освобождать список в `OnDestroy`, когда объект так и не был заспавнен.

Подсказки (клиент и сервер, из реплицированного состояния; тексты — Приложение А):
- `Describe(id)`: двери — по `door.State`, как у `BenchCabin` («закрыта / открывается / открыта / закрывается / упёрлась»); выключатель света — по комнате, а не по клавише (у проходных клавиши стоят по-разному), без сети — «нет питания (включён)» / «нет питания (выключен)»; выключатели и ремни ставен — по своим полотнам (`openAmount` между 0 и 1 — «едут»); выключатель ставен без сети — «нет питания»; остальное — по `target`.
- `ActionText(id)`: двери — как у `BenchCabin` (открыта или открывается → «закрыть», иначе «открыть»); выключатель и ремень ставен — по **своему** `target` (1 → «опустить», 0 → «поднять»): `Toggle` закрывает при `target > 0.5`, и полотна получают `Follow(0)` — опускаются; остальное — по Приложению А.

Схема проверена симуляцией до передачи: код ядра v2.7.1 (`SkyIslandInteractable`, `SkyIslandPowerGrid`, `SkyIslandLightManager`, `SkyIslandLightFixture`) на заглушках Unity, сервер и клиенты с задержкой сети 3 кадра, синхронизация по Приложению В — 22 проверки: комната у клиента переключается ровно один раз (проходные выключатели), блэкаут и аварийный свет, отказ выключателя ставен без сети, полотна стоят в блэкаут и доезжают, поздний вход сразу в том же состоянии, вход во время блэкаута без вспышки ламп (`startPowered` до `Start`), ранний `SetPower` до `Start` сети исправляется в следующем кадре. В Unity не запускалось.

Геймплейный код позже меняет состояние только на сервере и через ядро (`lights.SetGrid`, `lights.SetRoom`, `part.Open/Close`) — хост разошлёт сам. `BreakFixtures` (существо бьёт лампы) — когда понадобится, через ClientRpc (DEFERRED).

**U4.3. `BaseV2LevelBuilder`** (`Snow.Editor`), меню `Tools/Snow/Integrate BaseV2 Level`, по образцу `SkyIslandLevelBuilder`. Сборщик пакета создаёт сцену заново, поэтому меню запускать после каждой сборки корпуса; повторный запуск заменяет, а не дублирует. Компоненты — только через `TryGetComponent`, затем `AddComponent` (Приложение Б, п. 1). Детали лежат внутри экземпляра префаба `BaseMain`: всё, что меняется у его объектов, остаётся переопределениями сцены; **Apply в префаб не делать**.
1. Открыть `Assets/Locations/BaseV2/Scenes/BaseV2.unity`. Найти корень `BaseV2`, `BaseV2/Main Camera`, `PlayerSpawn` (тег Respawn), `SkyIslandLightManager` и `SkyIslandPowerGrid` на корне, все `SkyIslandInteractable`. Нет чего-то — ошибка с подсказкой «сначала Location Builder → BaseV2 → Build», выход.
2. Удалить прежний результат: корни `SnowLevel` и `NetworkManager`; объекты `IV_*` под деталями.
3. Корень `BaseV2`: `NetworkObject` (`SynchronizeTransform = false` — корень неподвижен) и `SkyIslandLocationHost`, если их нет.
4. С `Main Camera` убрать `SkyIslandInteractor` (локальные нажатия без сети). Fly-камера и `SkyIslandPSX` остаются — до входа игрока.
5. **Двери** — 19 деталей: `IX_Door_3` … `IX_Door_16` (14), `IX_MainDoor_1`, `IX_ExitDoor_2`, `IX_RoofDoor_17`, `IX_GarageGate_18`, `IX_GarageDoor_19`. На объект детали (он и есть шарнир) — `NetworkDoor` (есть — взять его и переписать поля):
   - `doorId` = имя; `axis` = `Quaternion.Inverse(transform.localRotation) * part.axis`, нормализовать (у ядра ось в пространстве родителя, у `NetworkDoor` — в своём; у деталей BaseMain поворот единичный, но формулу оставить для других локаций); `openAngle` = `part.max` (у всех дверей `min = 0`, `max` 85–95°; если `min ≠ 0` — ошибка);
   - `panelColliders` — не-триггерные коллайдеры детали и её детей; `vehicleRoot` = корень `BaseV2` (стены, коробки и мебель корпуса створку не блокируют, блокируют игроки и груз); `blockMask = ~(1 << Interact)`;
   - `manualTravelTime` 1,2 с (TEMPORARY, как у дверей проекта), у ворот 3 с (TEMPORARY);
   - `SkyIslandInteractable` на створке оставить: вызывать его некому (`SkyIslandInteractor` убран), а `Awake` сам ставит ему `enabled`, поэтому выключать бесполезно. Створку ставит только `NetworkDoor`; хост такие детали в `_bits` не берёт.
6. **Зоны** — у всех деталей, кроме 19 полотен ставен (они `driven`, ими управляют ремни и выключатели): `IV_<имя>` дочерним объектом детали (едет вместе с ней), слой Interact, `BoxCollider` триггер, `UtilityPlaneInteractable { id = имя детали, kind, label }` по Приложению А. Размер — рамка рендереров детали в её локальных координатах (закрытое положение) плюс по 1 см с каждой стороны по каждой оси, но не меньше 6 см по оси (клавиши выключателей 4 × 5 см); у дверей и ворот по толщине — по 2 см с каждой стороны, чтобы луч с обеих сторон встречал зону раньше створки. С запасом 1 см зоны соседних ящиков (`IX_Drawer_22` … `IX_Drawer_30` стоят стопками) не перекрываются, клавиши ставен и света одной рамки (центры через 8,6 см) — тоже; при 2 см с каждой стороны ящики перекрылись бы. Каждая деталь BaseMain — один меш на своём объекте без детей, поэтому `HoverOutline` обводит силуэт деталей до 0,8 м, у больших рисует рамку зоны. Итого зон: 131 − 19 = 112.
7. **Коллайдеры мебели.** Объекты подвижных деталей, кроме дверей и ворот (ящики, дверцы, клавиши, автоматы, ремни, полотна), — на слой Interact; после каждой смены — `PrefabUtility.RecordPrefabInstancePropertyModifications(go)`, иначе переопределение может не сохраниться и пропасть при повторном открытии сцены в `SaveNetworkScene` (так же записывает свои правки ядро — `SkyIslandLocation.AddPower`). Движок игрока слой Interact не видит, и дверца шкафчика, открытая в игрока, не запирает его в коллайдере; для луча `InteractionSensor` такие коллайдеры остаются стеной (закрытая дверца не пропускает луч внутрь); `renderingLayerMask` у деталей не меняется. TEMPORARY: сквозь открытую дверцу можно пройти, груз через неё проваливается; проверка препятствия, как у `NetworkDoor`, для мебели — если пользователь попросит.
8. Хост: заполнить `parts` в ordinal-порядке имён; категория — по префиксу (Приложение А); комната выключателя света — та из `lights.rooms`, чей `switchPrefix` является началом имени (у `Flur_EG`, `Flur_OG`, `Flur_UG` внутри имени есть «_», делить имя по «_» нельзя); комната выключателя и ремня ставен — имя без `IX_ShutterSwitch_` / `IX_ShutterWinder_` и без конечного `_<n>`; `lights`, `grid`.
9. **Спавн:** `Physics.SyncTransforms()`, затем 4 точки (`SnowNetworkBootstrap.MaxPlayers`) со смещениями −1,8, −0,6, +0,6, +1,8 м вдоль правого вектора `PlayerSpawn`, поворот как у него (лицом ко входу). Каждую — лучом вниз с высоты +3 м на землю (маска без Interact), затем `Physics.CheckCapsule` над точкой ног, как `HLCharacterMotor.CapsuleFree`: низ капсулы `feet + up·(0,02 + r + 0,01)`, верх `feet + up·(0,02 + 1,8 − r)`, `r` = 0,34, маска без Interact. Занято — сдвиг на 0,5 м назад от входа, до 3 раз; не нашлось — ошибка. Корень `SnowLevel/SpawnPoints`.
10. `NetworkManager`: `BenchBuilder.CreateNetworkManager(player, list, spawns)` с `Player.prefab` и `BenchBuilder.EnsurePrefabList(player)`; новых сетевых префабов нет.
11. Корень `SnowLevel` + `SkyIslandPlayerCamera { sceneCamera = BaseV2/Main Camera, copyPsxFrame = true }`.
12. Сохранить через `BenchBuilder.SaveNetworkScene(scene, path, first: false)` — сцена встанет в конец Build Settings, загрузочной останется Sky Island. Какой уровень грузится первым — **OPEN**, решает пользователь.
13. Журнал: `[Snow] BaseV2 level integrated: Assets/Locations/BaseV2/Scenes/BaseV2.unity (19 doors, 112 zones, 112 synced parts, 16 rooms, 4 spawn points), appended to Build Settings.`

### U5. Уровень и проверка
1. Выполнить `Tools/Snow/Integrate BaseV2 Level`; строка журнала — U4.3.13; второй запуск — те же числа, без дублей (один `NetworkDoor` на створке).
2. Проверки раздела 3: хост — в редакторе, клиенты — виртуальные игроки Multiplayer Play Mode (1–3). Play — через `unity command editor_play` (R-2). Host — кнопкой меню разработчика `SnowNetworkBootstrap` или из кода (`SnowNetworkBootstrap.StartHost()` в Play). Кнопку Join в окнах виртуальных игроков (IMGUI в отдельных процессах) канал агента, скорее всего, не нажмёт — попросить пользователя; без него Q10–Q11 — NOT_RUN с причиной. Игроки MPPM открывают ту же сцену, Build Settings для этого менять не нужно.
3. Сцены самолёта, стенды P01–P03, `SkyIsland.unity` и `DefaultNetworkPrefabs.asset` не изменились (даты из U1.5).

### U6. Документы
`docs/dev/ImplementationStatus.md`, новый раздел «BaseV2»: ядро v2.7.1; корпус собран; уровень и сеть — по пунктам со статусами `написано / скомпилировано / запущено / проверено в сети`; временные значения (U4.3.5, U4.3.7); OPEN (раздел 0, U2.6, U4.3.12); ответ пользователя по U1.6.

## 3. Что проверить

Game View 1920 × 1080. «Проверено» — только с журналом (`BenchLog`, консоль) или снимком. Лук локации дневной: свет смотреть в подвале, щитовой и при опущенных ставнях.

| № | Проверка | Ожидание |
|---|---|---|
| Q1 | ядро | цепочка U2.1 без `error CS`; файлы по U2.3; `SkyIsland.unity` открывается без Missing Script, Play как раньше |
| Q2 | сборка корпуса | строки журнала U3.2, числа U3.3; сцена сохранена; импорт текстур по U2.7; `Mobile_RPAsset` — по ответу U1.6; `PC_RPAsset` не изменился |
| Q3 | интеграция | строка U4.3.13; повторный запуск — те же числа; на `BaseV2` один `NetworkObject` и хост; один `NetworkManager`; 4 спавна; на `Main Camera` нет `SkyIslandInteractor`; после повторного открытия сцены 112 подвижных деталей (все, кроме дверей) на слое Interact |
| Q4 | проходимость | появление во дворе лицом ко входу; по лестнице на все этажи и крышу, во все помещения и гараж; в проёмах (дверь 0,9 м, капсула 0,68 м) не застревает. Известно по модели: в кабинки WC, в нишу между двухъярусными кроватями и за бак в котельной не пройти — записать, не чинить |
| Q5 | двери | все 18 дверей и ворота открываются и закрываются с обеих сторон; игрок в проёме — «упёрлась», створка не проходит сквозь него и не толкает; дальше 2 м зона лучом не выбирается (сервер отказывает дальше 2,6 м — «Слишком далеко»). Если створка прошла сквозь игрока: `NetworkDoor.IsFree` берёт до 16 коллайдеров (`OverlapBoxNonAlloc`) и только потом отбрасывает коллайдеры корпуса — буфер могли занять стены и пол; увеличить его до 64 (ENGINEERING, с записью) |
| Q6 | мебель | 26 ящиков, 7 дверец стоек, 6 шкафчиков, 2 тумбы, холодильник, дверца щита; дверца, открытая в игрока, его не запирает |
| Q7 | свет | выключатель каждой комнаты; проходные — лестница (3) и коридор 1-го этажа (2): любой переключает комнату; наружный свет — второй выключатель в тамбуре; фонари двора и заградогни на выключатели не заведены |
| Q8 | блэкаут | `IX_MainSwitch_75` выключен: сетевые лампы, экраны и светодиоды гаснут, загораются 12 аварийных, 4 табло горят, карточка выключателя света — «нет питания»; включён: лампы зажигаются вразброс за ~0,8 с, трубки моргают стартерами; натриевые фонари двора после пропадания сети ждут 60 с, потом разгораются ~90 с |
| Q9 | ставни | 6 ремней — по одному полотну; 3 выключателя (пульт, кухня, кабинет) — все полотна комнаты; ход ~3,3 с; карточка действия — «опустить», когда ставни подняты или едут вверх; без сети выключатель ставен отвечает «Нет питания»; сеть пропала на ходу — полотна стоят и доезжают, когда она вернётся; полотна сами лучом не выбираются |
| Q10 | сеть | хост + 1–3 клиента (MPPM): действие любого игрока видят все; карточка одинаковая у всех; в `BenchLog` — `use` с `ok` и `loc_state` |
| Q11 | поздний вход | открыть 3 двери и ящик, выключить свет в пульте, опустить ставни кухни и дождаться конца хода (~3,3 с), выключить рубильник; подключить клиента: у него сразу то же, детали без анимации (лампы гаснут и аварийные загораются по своему поведению, за доли секунды); в `BenchLog` — `loc_hash` совпал |
| Q12 | B и карточка | B (Reveal) показывает зоны в 4 м в прямой видимости; карточка «название — состояние», «[E] действие» по Приложению А |
| Q13 | нагрузка | хост + 3 клиента в пульте, сеть есть, свет включён везде: время кадра (Profiler, Main Thread), число включённых `Light` (README корпуса: на точках обзора 26–57 из 70), с тенями ≤ 3; у `SkyIslandLocationHost.LateUpdate` 0 B GC Alloc в кадрах без изменений |
| Q14 | консоль и сборка | нет ошибок и предупреждений `NetworkObject`, `NetworkDoor`, хоста ни у хоста, ни у клиентов; нет `A Native Collection has not been disposed` после выхода из Play без хоста; Development Build — по согласию пользователя (загрузочная сцена — OPEN), иначе NOT_RUN с причиной |
| Q15 | кадр PS1 у игрока | после входа: кадр 240 строк, 15 бит, дизеринг (как `Previews/PSX240/`); рамки наведения, карточка и метки B стоят на своих предметах; точка прицела в центре; контур силуэта тонкий; рамка зоны двери с 1–2 м сплошная, не рвётся и не мерцает; на экран рисует только камера игрока; насколько SMAA смягчает края против `Previews/PSX240/` — снимок рядом (U4.1); снимок 1:1 |

## 4. Не делать

- Не править в `Assets/SkyIsland/**` и `Assets/Locations/BaseV2/**` то, что пришло в пакетах: скрипты, данные (`Data/BaseV2_*.json` — источник истины для лука и расстановки), модели, текстуры, README (исключение — минимальная совместимость API с отчётом). Сборщик пишет свои папки (U3), меню интеграции — сцену `BaseV2.unity` (U4.3); больше ничего. Не расставлять и не настраивать локацию руками вместо сборщика; не менять её лук (туман, небо, пост, свет, материалы); не делать Apply переопределений в префаб `BaseMain`.
- Не пересобирать Sky Island, не трогать его уровень, самолёт и стенды P01–P03 (U2.6).
- Не делать BaseV2 загрузочной сценой без согласия; не менять `Player.prefab`.
- Не ставить и не обновлять пакеты Package Manager (UPM); ставятся только `.unitypackage` из U2.1. Не менять URP-ассеты, Graphics, Quality и Player Settings сверх того, что делает сборщик (U1.6).
- Не придумывать смысл аварийному выключателю отопления, автоматам и поворотным выключателям; не связывать автоматы с комнатами (OPEN).
- Не выключать игроку кадр PS1 и не уменьшать его эффект «для читаемости» (решение пользователя, раздел 0); если что-то в нём не работает — чинить код, а не кадр.
- Не переносить корпус на `Forest`, не делать NavMesh, звук, сохранения, замки (DEFERRED).
- Не удалять файлы без разрешения; не писать руками YAML сцен, префабов, ассетов и `.meta`.

## 5. Отчёт

В формате §0 промпта:
- с какой версии ядра обновлено, какие пакеты и как поставлены (U2.2); локальные правки ядра (U1.4) и что с ними стало;
- ответ пользователя по U1.6 и итоговые значения URP-ассетов;
- журнал сборки корпуса (U3.2) и числа U3.3;
- изменённые и новые файлы кода с причиной (U4.1–U4.3), строка журнала интеграции;
- Q1–Q15 со статусами и доказательствами: снимки двора, пульта с сетью и при блэкауте, кухни с опущенными ставнями, двери «упёрлась», карточки у хоста и клиента; строки `BenchLog`;
- что осталось OPEN и DEFERRED.

## Приложение А. Детали корпуса

Все имена — `<префикс>_<n>`; сравнение — `StartsWith(префикс + "_")`. Состояние 0 — `target = 0`; стартовые положения — из данных.

| Префикс | Шт. | Категория | `label` | `kind` | Состояние 0 / 1 | Действие |
|---|---|---|---|---|---|---|
| `IX_Door` | 14 | дверь, `NetworkDoor` | Дверь | door | по `DoorState`: закрыта, открывается, открыта, закрывается, упёрлась | открыта или открывается — закрыть, иначе открыть |
| `IX_MainDoor` | 1 | дверь | Входная дверь | door | то же | то же |
| `IX_ExitDoor` | 1 | дверь | Запасной выход | door | то же | то же |
| `IX_RoofDoor` | 1 | дверь | Дверь на крышу | door | то же | то же |
| `IX_GarageDoor` | 1 | дверь | Дверь гаража | door | то же | то же |
| `IX_GarageGate` | 1 | дверь, ось горизонтальная, 85° | Ворота гаража | door | то же | то же |
| `IX_Drawer` | 26 | мебель | Выдвижной ящик | pull | задвинут / выдвинут | 0 — выдвинуть, 1 — задвинуть |
| `IX_RackDoor` | 7 | мебель | Дверца стойки | hinge | закрыта / открыта | 0 — открыть, 1 — закрыть |
| `IX_LockerDoor` | 6 | мебель | Дверца шкафчика | hinge | закрыта / открыта | 0 — открыть, 1 — закрыть |
| `IX_TumbaDoor` | 2 | мебель | Дверца тумбы | hinge | закрыта / открыта | 0 — открыть, 1 — закрыть |
| `IX_FridgeDoor` | 1 | мебель | Холодильник | hinge | закрыт / открыт | 0 — открыть, 1 — закрыть |
| `IX_PanelDoor` | 1 | мебель | Дверца щита | hinge | закрыта / открыта | 0 — открыть, 1 — закрыть |
| `IX_Breaker` | 18 | положение без последствий (OPEN) | Автомат | toggle | включён / выключен (старт 0) | 0 — выключить, 1 — включить |
| `IX_Isolator` | 2 | положение без последствий (OPEN) | Поворотный выключатель | toggle | включён / выключен (старт 0) | 0 — выключить, 1 — включить |
| `IX_MainSwitch` | 1 | сеть корпуса | Главный рубильник | toggle | выключен / включён (старт 1) | 0 — включить, 1 — выключить |
| `IX_LightSwitch` | 19 | свет комнаты | Выключатель света (<комната>); у `Exterior` — Наружный свет | toggle | по комнате: свет не горит / свет горит; без сети — нет питания (включён / выключен) | горит — выключить, иначе включить |
| `IX_ShutterSwitch` | 3 | ставни, электропривод, нужна сеть | Рольставни (<комната>) | toggle | по полотнам: опущены / подняты / едут; без сети — нет питания | по своему `target`: 1 — опустить, 0 — поднять |
| `IX_ShutterWinder` | 6 | ставни, ремень | Ремень рольставни | pull | по полотну: опущена / поднята / едет | по своему `target`: 1 — опустить, 0 — поднять |
| `IX_Shutter` | 19 | полотно, `driven`, без зоны | — | — | 0 опущено (масштаб 1), 1 поднято (старт 1) | — |
| `IX_HeatingStop` | 1 | без последствий (OPEN) | Аварийный выключатель отопления | toggle | выключен / включён (старт 1) | 0 — включить, 1 — выключить |

`IX_Shutter_` не совпадает с `IX_ShutterSwitch_` и `IX_ShutterWinder_` при сравнении с «_» на конце префикса.

Комнаты (часть имени → подпись): `Windfang` — тамбур, `Leitstand` — пульт, `Flur_EG` — коридор 1-го этажа, `Lager` — склад, `Treppenhaus` — лестница, `Serverraum` — серверная, `Garage` — гараж, `Kaserne` — казарма, `Kueche` — кухня, `Flur_OG` — коридор 2-го этажа, `Waschraum` — санузел, `Stationsleiter` — кабинет начальника, `Heizung` — котельная, `Flur_UG` — коридор подвала, `Schaltraum` — щитовая, `Exterior` — наружный свет.

## Приложение Б. Ловушки

1. **«Фальшивый null» в редакторе.** `GetComponent<T>() ?? AddComponent<T>()` в Editor-коде не добавляет компонент: для отсутствующего компонента `GetComponent` возвращает объект, который равен null только через `==` Unity. Из-за этого у ядра v2.3–v2.6 не было интерактива. Только `TryGetComponent`.
2. **Переопределения экземпляра префаба.** Изменения объектов внутри `BaseMain` из скрипта (слой, поля компонентов префаба) записывать `PrefabUtility.RecordPrefabInstancePropertyModifications`; добавленные компоненты (`NetworkDoor`) и объекты (`IV_*`) сохраняются и так.
3. **Хэш `NetworkObject` в сцене.** Сохранять только `BenchBuilder.SaveNetworkScene` (сохранить, Build Settings, открыть заново, сохранить) — иначе нулевой `GlobalObjectIdHash`, и сервер не спавнит объект (ошибка 29.09).
4. **Методы ядра на клиенте** — двойное переключение комнат и рубильника (U4.2). На клиенте — только поля.
5. **`SkyIslandPowerGrid.Start`** принудительно ставит `startPowered`: значение, пришедшее до `Start`, затрётся. Поэтому клиент ставит `startPowered` в `OnNetworkSpawn` и сверяет состояние каждый кадр.
6. **Имена с номерами и «_» внутри.** `IX_MainDoor_1`, а не `IX_MainDoor`; комнаты `Flur_EG` / `Flur_OG` / `Flur_UG` — с подчёркиванием (U4.3.8).
7. **Ось двери.** У `SkyIslandInteractable` ось в пространстве родителя, у `NetworkDoor` — в своём (`_rest * AngleAxis(…)`); формула U4.3.5 переводит одну в другую.
8. **Порядок деталей.** `_bits` идёт по массиву `parts`, заполненному в редакторе; имена деталей не менять, после пересборки корпуса — заново U4.3. Расхождение ловит `_layoutHash`. Повторный Host — `_bits.Clear()`.
9. **Свет и камера.** LOD света и бюджет теней `SkyIslandLightManager` меряет от `Camera.main`: до входа игрока — камера сцены, после `SkyIslandPlayerCamera` — камера игрока. Отдельный `viewer` не нужен.
10. **Полотна ставен** — масштаб по высоте до 0,03: зона на полотне сплющилась бы вместе с ним. Поэтому у полотен зон нет (U4.3.6).
11. **Капсула на земле.** `CheckCapsule`, опущенная до точки касания, всегда «занята» — поднимать, как `HLCharacterMotor.CapsuleFree` (U4.3.9).

## Приложение В. Набросок синхронизации (ENGINEERING)

```csharp
// оба метода — только при IsSpawned
// сервер: сразу после ServerUse и в каждом LateUpdate
void Publish()
{
    for (int e = 0; e < _bits.Count; e++)                        // детали без NetworkDoor, порядок parts, 64 на элемент
    {
        ulong word = 0;
        for (int i = e * 64; i < Math.Min(_synced.Length, e * 64 + 64); i++)
            if (_synced[i].target > 0.5f) word |= 1UL << (i & 63);
        if (_bits[e] != word) { BenchLog.Event("loc_state", "word", e, "bits", word ^ _bits[e]); _bits[e] = word; }
    }
    if (grid != null && _power.Value != grid.Powered) _power.Value = grid.Powered;
    if (lights != null)                                          // у Forest только сеть, у Town нет ни того ни другого
    {
        uint rooms = 0;
        for (int r = 0; r < lights.rooms.Count; r++) if (lights.IsRoomOn(lights.rooms[r].name)) rooms |= 1u << r;
        if (_rooms.Value != rooms) _rooms.Value = rooms;
    }
}

// клиент: snap = true в OnNetworkSpawn (после grid.startPowered = _power.Value), false в каждом LateUpdate
void ApplyReplicated(bool snap)
{
    for (int i = 0; i < _synced.Length && (i >> 6) < _bits.Count; i++)
    {
        SkyIslandInteractable p = _synced[i];
        float t = ((_bits[i >> 6] >> (i & 63)) & 1UL) != 0 ? 1f : 0f;
        if (snap) { p.openAmount = t; p.target = t; p.Apply(); p.enabled = false; }
        else if (!Mathf.Approximately(p.target, t)) { p.target = t; p.enabled = true; }   // без Toggle/Follow
    }
    if (grid != null && grid.Powered != _power.Value) grid.SetPower(_power.Value);
    if (lights == null) return;
    for (int r = 0; r < lights.rooms.Count; r++)
    {
        bool on = (_rooms.Value & (1u << r)) != 0;
        if (lights.IsRoomOn(lights.rooms[r].name) != on) lights.SetRoom(lights.rooms[r].name, on);
    }
}
```

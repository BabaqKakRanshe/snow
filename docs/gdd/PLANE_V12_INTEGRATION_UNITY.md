# Интеграция самолёта v12 в игру Snow (переход с v4) — задание для AI-агента

Версия 1.1 от 16.10.2026 (1.1: правки после независимой сверки с прежними заданиями и контрактом). Действует вместе с `PLANE_PROMPT_UNITY.md` (версия 2.1): правила работы, статусы (CONFIRMED / ENGINEERING / TEMPORARY / OPEN / DEFERRED), требования с ID и формат отчёта — оттуда (§0). Для перехода с v4 это задание заменяет цепочку `PLANE_V5_UPGRADE_UNITY.md` … `PLANE_V12_UPGRADE_UNITY.md`. Если пункт здесь расходится с промптом или с этими заданиями, верно то, что написано здесь.

Устаревшие места промпта (написаны под модель v5) и что их заменяет:
- §5 и §13 (факты о модели, пробелы M1–M7) — раздел 7;
- §8.2 (TEMPORARY M1: кнопка — внутренняя ручка) и §8.3 (TEMPORARY-меш ключа, скважина — наружная ручка) — тема E;
- §9.6 (горловины — OPEN M5) — тема H;
- §10.3, экран GPS (свой квад 0.0874 × 0.0653 м поверх `Display_Radio`) — тема D. Содержание экрана GPS по §10.3 остаётся в силе.

Проект: `F:\ClaudeGames\snow\Snow`, Unity 6000.0.67f1, URP 17 (Forward+), Netcode for GameObjects 2.13.3.
Пакет модели: `F:\ClaudeGames\snow\assets\plane\UtilityPlane_Unity_v12\Assets\UtilityPlane` — v12, редакция 2. Рендеры для сравнения — `F:\ClaudeGames\snow\assets\plane\UtilityPlane_Unity_v12\previews\`.
Пакет зимнего комплекта: `F:\ClaudeGames\snow\assets\plane\UtilityPlane_WinterKit_v1_2\Assets\UtilityPlane` — 1.2; его задание — `F:\ClaudeGames\snow\docs\gdd\PLANE_WINTERKIT_UNITY.md` (1.2.1) с поправками из И4.

## 0. Что есть и что нужно

### 0.1 Проект на 16.10.2026 (проверено по файлам)

- Модель **v4**: заголовок `Assets/UtilityPlane/README.md` — «v4»; 43 зоны, 12 источников света (у ламп кабины двойной префикс: `Light_Light_Dome_L` …); в `Materials/` есть `M_CargoNet.mat`, в `Textures/` — карты v4 `T_*_AO`, `T_*_MetallicSmoothness`, `T_*_Normal`.
- Код — этап **P03** (`Docs/ImplementationStatus.md` от 29.09; после 1.10 проект не менялся): `PlaneVehicle`, `PlaneState` (`PlaneSwitchState`, `PlaneTelemetry`), `PlanePresentation`, `AircraftPhysics`, `AircraftSystems`, `TurbopropEngine`, `ElectricalSystem`, `Autopilot` (держит курс и высоту на момент включения), `NetworkDoor`, `SeatAnchor` / `SeatedVehicle`, `CargoItem` / `TieDownSocket`, `InteractionSensor` / `InteractionHost` / `InteractionOverlay` / `HoverOutline`. Вариант `Assets/Snow/Plane/UtilityPlane_Game.prefab` и стенд `Assets/Snow/Bench/P03_PlaneBench.unity` собирает `Editor/PlaneBenchBuilder.cs` (`Tools/Snow/Build P03 Plane Bench`), уровень `Assets/SkyIsland/Scenes/SkyIsland.unity` — `Editor/SkyIslandLevelBuilder.cs` (`Tools/Snow/Integrate Sky Island Level`).
- Самолёт ставится горизонтально: `PlaneBenchBuilder.cs` (~202) `SetPositionAndRotation(PlanePosition, Quaternion.identity)`, `SkyIslandLevelBuilder.cs` (~96) `SetPositionAndRotation(planePos, PlaneFacing)`. `AircraftTuning`: `wingIncidenceDeg` 4.5 (обход горизонтальной стоянки, 30.09), `tailWheelRadius` 0.285, `mainTravel` 0.12, `tailTravel` 0.06. `AircraftPhysics` (~158–161): доли веса колёс 0.41 / 0.41 / 0.18 — константы.
- Совместимость с v12 (сверено по коду): все поля Rig, которые пишет `PlanePresentation`, в v12 есть с теми же типами; все 43 id зон v4 в v12 есть без переименований, новых — 36; asmdef пакета не изменились. После замены пакета проект должен собраться без правок кода. Новые зоны до своих итераций отвечают «Нет действия» (`PlaneVehicle.ServerUse`) — это ожидаемо.
- `PlanePresentation.LateUpdate` доводит тумблеры `SW_*`, краны `FuelSel_*` и рычаг `Lever_Flap` после Rig по углам контракта (+40°, +60°, −12° на ступень, по X). В v12 углы и оси те же — доводку оставить, но писать поворот только пока орган движется (J.2): сейчас она пишет каждый кадр.
- `PlaneSwitch` (`PlaneState.cs`) занимает 27 бит из 32. `PlaneTelemetry` уже передаёт `BusVolts` и `GroundSpeed`. Питание авионики в проекте — шина под напряжением и тумблер `SW_Avionics` (`PlaneVehicle.cs` ~111).
- Замка, ключа и своих квадов экранов в коде нет. Нет и поиска `Light_*`, записи `lampGlow`, своего вращения винта, зоны 3 см высотомера (M3), чтения вершин мешей самолёта, ссылок на материалы самолёта. `MaterialPropertyBlock` есть только у `HoverOutline` (на свой `Graphics.RenderMesh`) и у `PlayerNet` (на игроке).
- Сетевые префабы — `Assets/DefaultNetworkPrefabs.asset`. Игровые варианты ящиков делает код: `PhysicsBenchBuilder` → `Cargo_Case{n}_Net` в `Assets/Snow/Bench/Cargo` (Rigidbody, `NetworkObject`, `NetworkTransform`, `CargoItem`).
- Quality, уровень PC: Anisotropic Textures — Forced On.
- Пакеты v5–v12 и комплект доставлены, но не ставились; задания обновления не выполнялись.

Перед правкой каждый файл перечитать: всё выше — на 16.10.

### 0.2 Цель

Игра работает на v12 (редакция 2) с зимним комплектом 1.2. На стенде P03 и на Sky Island самолёт стоит, рулит и летает, груз, двери, кресла и свет работают не хуже, чем сейчас. Всё, что модель дала после v4, подключено к системам игры там, где они есть, а там, где нужен ответ пользователя, стоит на TEMPORARY-решениях из раздела 5.

Пакет ставится один раз. Правки кода всех версий собраны в разделе 3 по темам; у каждого пункта в скобках источник, например «(v7 U4.5)». Прежние задания — только справка, отдельно их не выполнять.

### 0.3 Что модель дала после v4

| Версия | Главное | Контракт |
|---|---|---|
| v5 | стоянка хвостом вниз (`ParkedPose`); шасси совпадает с лучом подвески; салон грузового Caravan; подножки; крепления на полу; имена огней без двойного префикса и вторая лампа отсека; `avionicsPower` | разделы 1, 3, 12 |
| v6 | новая приборная доска: вольтметр, вакуум, бардачок, крышка стартера, 7 экранов `Display_*` под живую картинку | разделы 4, 5, 7, 13 |
| v7 | тумблеры дверей кабины, личинки и ключ, высотомер с окном давления, метка курса и согласование курсового, компас, 14 ручек радио, запотевание стёкол, стремянка, винт с флюгером и размытием, свои экземпляры материалов у Rig | разделы 3–5, 7.1, 9, 14 |
| v8 | обшивка в двух атласах (`M_Airframe` + `M_Wing`), LOD экстерьера и кабины | разделы 8, 8.1, 15 |
| v9 | экстерьер PS1/PSX, 4 откидных кресла в отсеке, места хранения, аварийный комплект, сокеты хоррора и звука, отпечатки на запотевших стёклах | разделы 7.2, 10.1–10.4, 16 |
| v10 | оптимизация: меши без Read/Write, теневые копии `*_Shadow`, Rig пишет только изменения (`ResyncPose`) | раздел 17 |
| v11 | свет: 16 источников с формой (cookie), физика ламп, тени, свечение и лучи в тумане (URP), мигание для хоррора | разделы 7, 7.3–7.7, 18 |
| v12 | интерьер PS1/PSX (атласы 512², 32 цвета), шкалы и надписи пиксельной графикой — читаются из кресла; шрифт `UtilityPlanePixelFont` для экранов | разделы 7, 8, 19 |

Итог v12: 60 выпуклых коллайдеров, 79 зон `IV_*`, 125 сокетов, 16 источников света, LOD экстерьера и кабины, 5 теневых копий. Контракт — `Assets/UtilityPlane/UNITY_CONTRACT.md` пакета: имена, оси, знаки, поля Rig; проверки — раздел 11, пункты 1–64.

### 0.4 Решения пользователя, которые здесь применяются (CONFIRMED)

- 09.10: стоянка хвостом вниз; салон по референсам грузового Caravan; приборная доска v6 (справа бардачок и приборы систем, экраны — квады под живую картинку).
- 10.10: ручки радио подвижные; кнопки радио, АЗС и гнёзда — только изображение, зон им не добавлять; второй штурвал и педали справа синхронные (M6); зимний комплект с канистрой — отдельная поставка. Доступ к горловинам (M5) пользователь оставил на выбор исполнителю — выбрана стремянка (ENGINEERING).
- 12.10: кооп и хоррор; 4 откидных кресла в отсеке, по 2 у борта; экстерьер и комплект в стиле PS1/PSX (вариант B).
- 14.10: оптимизация без видимой разницы. 15.10: свет «как настоящий», а не «лампочки»; интерьер тоже PSX, но текст читаемый. 16.10: PSX сильнее везде (редакция 2).
- Из проекта (30.09): автопилот-прибор, Alt → B, точка в центре экрана, контур при наведении, приближение ПКМ, плавное движение органов в `PlanePresentation`.

## 1. Что затронуто в проекте

| Где | Что меняется |
|---|---|
| `Assets/UtilityPlane/**` | пакет v4 с GUID → v12: новые скрипты (`UtilityPlaneRig.Lights.cs`, `UtilityPlanePixelFont.cs`), шейдеры (`UtilityPlaneGlassFog`, `UtilityPlaneAirlight`, `UtilityPlaneBeam`), `Data/*.json`, `Textures/Cookies/`, модели `SM_StepLadder`, `SM_PlaneKey`, `SM_Surv_*`; меню создаст `Generated/`, новые материалы и префабы `StepLadder`, `PlaneKey`, `Surv_*`; в И4 комплект добавит `WinterKit` и `Prefabs/Kit/KitCarry_*` |
| `Assets/Snow/Editor/PlaneBenchBuilder.cs` → `Assets/Snow/Plane/UtilityPlane_Game.prefab`, стенд P03 | стоянка через `ParkedPose`; 4 кресла отсека; места хранения; игровые варианты ключа, стремянки и предметов; LOD рендереров варианта; комментарий о сетке креплений |
| `Assets/Snow/Editor/SkyIslandLevelBuilder.cs` → `SkyIsland.unity` | стоянка через `ParkedPose`; стартовая стремянка, ключ и вещи комплекта |
| `Runtime/Aircraft/AircraftTuning.cs` + `Assets/Snow/Plane/AircraftTuning.asset` | `tailWheelRadius`, `wingIncidenceDeg`; новые TEMPORARY-числа (вакуум, запотевание, компас, массы) |
| `Runtime/Aircraft/AircraftPhysics.cs` | доли веса колёс из геометрии; масса и центр масс с людьми в отсеке; последствия комплекта |
| `Runtime/Aircraft/PlaneState.cs`, `PlaneVehicle.cs` | реплицируемое состояние: двери кабины, замок, бардачок, установки приборов, ручки радио, кресла отсека, `LadderStowed`, запотевание, комплект; обработчики новых зон |
| `Runtime/Aircraft/PlanePresentation.cs` | новые поля Rig: ход стоек, авионика, вольтметр, вакуум, бардачок, высотомер, курс, компас, ручки радио, ответчик, запотевание, кресла отсека, стремянка, туман сцены; новые тумблеры в `SwitchObjects` |
| `Runtime/Aircraft/AircraftSystems.cs`, `TurbopropEngine.cs` | высотомер от установки, курсовой без автосогласования, компас, вакуум, обогрев и обдув стёкол, блокировки комплекта |
| `Runtime/Seats/*` (`SeatedVehicle.FindExit`, `SeatAnchor.EyeColliders`) | выход из кресел отсека внутрь; глаз вне коллайдеров кресел с v5 |
| `Runtime/Interaction/*` (`InteractionSensor.Usable` / `Pick`, `HoverOutline`, `InteractionOverlay`) | новые kind (`lock`, `ladder`, `stow`, `kit`, `kit_item`); прозрачные зоны; правила перебора рендереров (LOD, `*_Shadow`, `Glow_*` / `Beam_*`) |
| `Runtime/Cargo/*` | крепления на полу v5; места хранения с полной позой; предметы и вещи комплекта |
| `Runtime/Doors/*` | двери кабины от тумблеров; замок |
| `Docs/ImplementationStatus.md`, `Docs/ProjectAudit.md`, `Docs/Tuning/UtilityPlane.md`, `Docs/Tuning/Cargo.md`, `Docs/Controls.md` | статус модели; закрытые пробелы M1–M3, M5–M7 и находки N1–N9; TEMPORARY-числа; Dev-клавиши |
| `Assets/Settings/PC_RPAsset.asset`, `PC_Renderer.asset`, Graphics Settings, Quality, профили постобработки | только проверить и записать (И1.1.5); без разрешения пользователя не менять |

## 2. Итерации

Пять итераций. После каждой: компиляция без ошибок, пересборка (И1.7), проверки этой итерации (раздел 4) со статусами, отчёт по шаблону §0 промпта. Следующая итерация — только после компиляции без ошибок. Прогоны — как договорено в проекте: с 29.09 код пишется без прогонов, «запущено» и «проверено» — только с журналом или снимком; что не проверялось — статус «не проверено».

### И1. Пакет v12 и минимум, чтобы всё стояло и работало

**1. Подготовка**
1. Выйти из Play Mode. Перед каждой компиляцией проверять состояние редактора (R-4 в `Docs/ImplementationStatus.md`: компиляция в Play Mode перезагружает домен).
2. Определить версию модели по заголовку `Assets/UtilityPlane/README.md` (на 16.10 — «v4»). Если стоит не v4 — после пп. 3–5 действовать по разделу 6.
3. Git в проекте нет (R-3). Резервная копия вне `Assets` — в `F:\ClaudeGames\snow\backup\plane_v4_<дата>\`: `Assets/UtilityPlane`, `Assets/Snow`, `Assets/SkyIsland/Scenes/SkyIsland.unity`, `Assets/DefaultNetworkPrefabs.asset` (всё с `.meta`) и `Docs/`.
4. Записать «до»: снимки из кресла пилота днём на P03 и на Sky Island (1920 × 1080); FPS сцены; треугольники кадра из кабины и снаружи с 5 м (Statistics или Frame Debugger); видеопамять текстур самолёта (Profiler → Memory, текстуры `T_*`).
5. Проверить и записать, ничего не меняя (v11 U1.4):
   - `PC_RPAsset`: Depth Texture, Light Cookies, Additional Lights Shadows (атлас 2048, тиры 256 / 512 / 1024), Soft Shadows;
   - `PC_Renderer`: Forward+, Depth Texture Mode — After Opaques (или Force Prepass);
   - Graphics Settings: Lights Use Linear Intensity, Lights Use Color Temperature;
   - Quality, уровень PC: Anisotropic Textures (на 16.10 — Forced On).
   Если что-то из этого выключено — не включать, спросить пользователя: без Depth Texture свечение огней видно сквозь стены, без Color Temperature Rig сам складывает температуру в цвет. Если PSX-текстуры или надписи вблизи окажутся размытыми (I-08), одна из возможных причин — Forced On; записать и спросить, не менять.

**2. Пакет v12**
1. Скопировать содержимое пакета поверх `Assets/UtilityPlane` с заменой файлов, без удаления лишнего: `robocopy <пакет v12>\Assets\UtilityPlane <проект>\Assets\UtilityPlane /E`, без `/PURGE`. `.meta` проекта не трогать: GUID FBX, текстур, материалов, префаба и скриптов должны сохраниться.
2. `AssetDatabase.Refresh`, дождаться импорта и компиляции; ошибок быть не должно.
3. Шейдеры `Shaders/UtilityPlaneGlassFog.shader`, `UtilityPlaneAirlight.shader`, `UtilityPlaneBeam.shader` в Unity ещё не компилировались: выбрать каждый, в Inspector и консоли ошибок нет. Ошибку записать дословно и сообщить пользователю; шейдер не править.
4. Импорт текстур и моделей выставляют постпроцессор пакета (`UtilityPlaneImport` в `UtilityPlaneSetup.cs`) и меню (п. 3). Другой постпроцессор в проекте — только `SkyIsland/Scripts/Editor/SkyIslandImport.cs` (пути с `SkyIsland`), `Assets/UtilityPlane` он не задевает. Если найдутся платформенные переопределения импорта для `Assets/UtilityPlane` — записать и спросить пользователя: сжатие и Bilinear ломают PSX-палитру и пиксельные буквы.

**3. Меню установки модели**
1. `Tools → Utility Plane → Build Materials + Prefabs`. Ожидаемые строки:
   - `[UtilityPlane] Assets/UtilityPlane/Prefabs/UtilityPlane.prefab: 60 convex colliders, 79 interaction volumes, 16 lights (14 cookies, 7 with shadows at the default quality, glow / beam FX: yes), 116 renderers without shadows, LOD groups: Exterior 3, Cockpit 3, 5 shadow proxies, fog meshes copied: 5`
   - `[UtilityPlane] Done (URP), v12. Prefabs in Assets/UtilityPlane/Prefabs; readable collider / fog meshes: 65 in Assets/UtilityPlane/Generated/UtilityPlane_RuntimeMeshes.asset`
   - предупреждений быть не должно:
     - `Shader UtilityPlane/... is missing, has errors or is not supported here` (Airlight, Beam);
     - `Shader UtilityPlane/GlassFog has errors or is not supported here` — с ним стёкла без очистки по маске и без отпечатков;
     - `glow / beam shaders unavailable`, `Depth Texture is off`, `Depth Texture Mode is After Transparents`;
     - `renderers not found`, `shadow proxy ... not found`, `parent ... not found`.
2. В конце меню напишет `[UtilityPlane] Unused files from an older version (safe to delete): …` — от v4 это 11 файлов: `M_CargoNet.mat`, `T_CargoNet_BCA.png`, `T_Airframe_Normal.png`, `T_Cockpit_Normal.png`, `T_Details_Normal.png`, `T_Airframe_MetallicSmoothness.png`, `T_Cockpit_MetallicSmoothness.png`, `T_Details_MetallicSmoothness.png`, `T_Airframe_AO.png`, `T_Cockpit_AO.png`, `T_Details_AO.png`. На них ничего не ссылается; удалять — только с разрешения пользователя. `T_*_MaskMap.png` — рабочие карты v12, не трогать.
3. В `Prefabs/UtilityPlane.prefab`: 16 объектов `Light_*` в сокетах — все Spot с cookie `T_Cookie_<ключ>`, кроме `Light_Fill_Cockpit` и `Light_Fill_Bay` (Point); тени Soft у `Light_Landing`, `Light_Dome_L/R`, `Light_Cargo`, `Light_Cargo_2`, `Light_Flood`, `Light_Flood_2`. У `UtilityPlaneRig`: `glowMaterial` = `M_FX_Airlight`, `beamMaterial` = `M_FX_Beam`, `lightCookies` — 14, `lightsVersion` = 11, `fogPrintHand` и `fogPrintFinger` назначены. Объектов `Glow_*` и `Beam_*` в префабе нет: Rig создаёт их только в игре; если они есть — префаб сохранили из Play Mode, собрать меню заново.
4. Появились префабы `Prefabs/StepLadder`, `PlaneKey`, `Surv_Flashlight`, `Surv_Flare`, `Surv_FlareShell`, `Surv_FlareGun`, `Surv_Shovel`, `Surv_Bag`.
5. `UtilityPlane.prefab` собран заново: GUID тот же, внутренние объекты новые. Вариант и сцены обновляются только пересборкой (п. 7), руками их не править.

**4. Прозрачные зоны** (часть K.4 и L.3, нужна уже в И1). Зоны мест хранения и откидных кресел без правила перехватят луч. `Stow_Ladder` — коробка над центральной и правой частью пола отсека: X −0.22…0.61, Y 1.12…1.77, Z −1.10…1.43 в осях префаба. В неё попадает большая часть ящиков стенда (позиции `cargo_layout.json`) и кольца креплений: без правила их не взять. В `InteractionSensor.Usable`:
- зона kind `stow` прозрачна, пока у игрока в руках нет подходящего предмета (до И3 — всегда);
- зона `PaxSeat_*` (kind `seat`) прозрачна, пока кресло сложено (до И3 — всегда).

**5. Правки кода И1** — темы A, B, J, N, O раздела 3, из темы C — пункты 1–2.

**6. Документы И1** — модель v12 (редакция 2) в `Docs/ImplementationStatus.md`; настройки из п. 1.5; решения по A, B, J, N, O, в том числе значение `KeepMeshesReadable` и почему; список ненужных файлов из п. 3.2 (удалены или нет — по ответу пользователя).

**7. Пересборка** (после каждой итерации)
1. `Tools → Snow → Build P03 Plane Bench` — вариант `UtilityPlane_Game.prefab` и стенд; GUID варианта сохраняется, список сетевых префабов остаётся рабочим.
2. `Tools → Snow → Integrate Sky Island Level`; пересохранить сцену, чтобы хэши `NetworkObject` совпали с вариантом.
3. В сценах нет экземпляров с потерянными переопределениями и Missing Prefab; у экземпляров самолёта нет переопределений на `Light_*` и материалах (если есть — записать какие).

Проверки И1: I-01…I-15, I-30…I-32.

### И2. Кабина

Правки: C (кроме п. 1–2), D, E, F, G, I. Проверки: I-16…I-23, I-30…I-32.

### И3. Отсек и снаряжение

Правки: H, K, L, M. Проверки: I-24…I-32.

### И4. Зимний комплект 1.2

По заданию `docs/gdd/PLANE_WINTERKIT_UNITY.md` (1.2.1): шаги K1–K4 и проверки KX1–KX10, с поправками под v12.
- K1.2: должна стоять модель **v12** (README — «v12»), а не v9 / v10. Вместо проверок заданий v9 / v10 — проверки I-01…I-32 этого задания; Y9 задания v8 входит в KX2. Строки из K1.3 в `UtilityPlaneSetup.cs` v12 есть; установщик комплекта собирается вместе со скриптами v12 без ошибок (проверено 16.10 сборкой скриптов вне Unity).
- K3.2 и KX2: у `T_KitCovers_MaskMap` и `T_KitProps_MaskMap` с v12 сжатие включено — правило v12 для всех PSX-масок; у `_BaseColor` — None.
- K3.4: пересборка — И1.7.
- После любого повторного `Build Materials + Prefabs` снова выполнить `Tools → Utility Plane → Install Winter Kit`: меню модели собирает префаб без вложенного `WinterKit`.
- K4.2: игровые варианты `KitCarry_*` — кодом сборки, как ящики (0.1).
- K4.5: держатель канистры в отсеке — `Socket_Stow_JerryCan` (тема L), стремянка и досягаемость горловины — тема H.
- После установки комплекта повторить I-25 для канистры: `KitCarry_JerryCan` в `Socket_Stow_JerryCan` (пункт 33) и правило кольца `_13` (L.5). В И3 канистры ещё нет.

### И5. Документы и итог

Тема P и итоговый отчёт (раздел 9). Дальше — этапы P04–P08 промпта. Что из них сделано этим заданием, отметить в статусе как частичное закрытие: из P05 — двери кабины, ключ, замок; из P06 — новые приборы, лампы, огни, запотевание, заправка канистрой. Экран GPS (§10.3) и остальное из P06 остаются.

## 3. Правки кода по темам

Каждую правку связать с требованиями промпта (ID). Если правка не нужна — записать «нет» с причиной.

Материалы и префабы пакета (`Assets/UtilityPlane/Materials`, `Prefabs`) не править: меню перезаписывает их при каждом запуске. Всё, что игра добавляет к самолёту, — в варианте через код сборки (`PlaneBenchBuilder`) или на экземплярах во время игры.

Новое состояние решает сервер, а видят все, включая вошедших позже: двери кабины, замок, бардачок, установки приборов, 13 углов ручек радио и режим ответчика, кресла отсека, `LadderStowed`, запотевание. Где хранить:
- в свободные биты `PlaneSwitch` (их 5) — двухпозиционные органы кабины: тумблеры дверей кабины (2), замок, бардачок;
- кресла отсека и `LadderStowed` — отдельными полями (K.2);
- числа — отдельными полями `PlaneSwitchState` или отдельной реплицируемой структурой (ENGINEERING).
Сериализацию и `Equals` дополнить.

Зоны: объект в префабе — `IV_<id>`, в коде — `id` без префикса (`PlaneVehicle.ServerUse` сравнивает `id`). Ниже зоны названы так же, как в контракте: где с префиксом, где по `id`.

### A. Стоянка, шасси, крыло (И1; v5 U4.1–U4.3)
1. Везде, где самолёт ставится на землю, — стояночная поза: `var pose = UtilityPlaneRig.ParkedPose(точкаНаЗемле, курс); plane.transform.SetPositionAndRotation(pose.position, pose.rotation);`
   - Где: `PlaneBenchBuilder` (~202), `SkyIslandLevelBuilder` (~96, курс 270), а также респаун, сброс и перенос самолёта, если они есть.
   - Корень окажется на 0.372 м ниже земли, нос поднят на 9.98° — так и должно быть.
   - `ParkedPose` рассчитан на ровную землю; на уклоне проверить касание всех трёх колёс.
2. `tailWheelRadius` 0.285 → 0.2856 (в `AircraftTuning.cs` и в ассете).
3. Доли веса колёс в `AircraftPhysics`: лучше считать из положений `Socket_WheelContact_*` и текущего центра масс (ENGINEERING); минимум — константы для пустого самолёта с ЦМ в `Socket_CenterOfMass`: 0.44 / 0.44 / 0.12.
4. В `PlanePresentation` один раз: `rig.mainGearTravel = tuning.mainTravel; rig.tailGearTravel = tuning.tailTravel;` — Rig сам переводит долю хода в угол стойки (N4). `AircraftPhysics` (~183) отдаёт именно долю хода, своего пересчёта в угол в проекте нет (16.10); если появится — убрать, иначе пересчёт выполнится дважды.
5. `wingIncidenceDeg` 4.5 → около 2° (TEMPORARY): обход горизонтальной стоянки больше не нужен.
   - На стоянке крыло стоит к потоку около 12°; разбег — с подъёмом хвоста.
   - В `Docs/Tuning/UtilityPlane.md` переоценить скорости отрыва и сваливания и убрать фразу про горизонтальную стоянку.

### B. Груз, коллайдеры, подножки, кресла кабины (И1; v5 U4.5, U4.7–U4.9, v6 U4.7)
1. `Socket_TieDown_01..15` стоят на полу под кольцами рельсов: X −0.42 / 0 / +0.42, Z +1.45 … −1.10, Y 1.074 (верх `COL_Floor`, место pivot ящика). Нумерация прежняя: спереди назад, слева направо. `captureRadius` 0.45 покрывает весь пол (дальняя точка — 0.41 м); комментарий о сетке 0.5 × 0.7 в `PlaneBenchBuilder` (~159) исправить.
2. Пол на стоянке наклонён на 10° назад: свободные ящики не должны сползать, закреплённые — держаться. `BenchCargoSpawner` ставит ящики в осях самолёта (`anchor.TransformPoint`, поворот самолёта) — с наклоном это верно.
3. Коллайдеры:
   - `COL_Roof` → `COL_Roof_Fwd` + `COL_Roof_Aft`: в отсеке под потолком 1.45–1.51 м, персонаж пригибается (N6; высота оставлена решением пользователя);
   - `COL_Barrier_*` — сетка за креслами, прохода из кабины в отсек нет, как и в v4;
   - подножки `COL_Step_Cargo_L/R`, `COL_Step_Pilot_L/R`: подножка грузовой двери 0.50 м над землёй, порог проёма 0.63–0.86 м — по §7.2 в отсек можно забраться пешком.
   - Объём салона для зон `*_In` в варианте (`interiorCenter` (0, 1.85, 0.9), `interiorSize` (1.4, 1.6, 4.5)) подходит.
4. Глаз пилота и пассажира с v5 лежит вне коллайдеров кресел (`COL_Seat_*` — подушка, `COL_SeatBack_*` — спинка). Обход 30.09 «глаз внутри `COL_Seat_P`» больше не нужен: проверить, что `SeatAnchor.EyeColliders` не захватывает лишнего. Единый выбор цели `InteractionSensor.Pick` оставить.
5. Органы и ручки переехали, зоны едут с объектами — проверить досягаемость лучом из кресла; если дальности не хватает — записать, модель не менять.
   - внутренние ручки дверей (`Socket_Handle_*_In`) — на ручках обивки, у дверей кабины — в передней части двери;
   - `Pull_FuelShutoff` — на задней стенке пьедестала;
   - тумблеры — на лайтплейте у левого борта (X ≈ −0.49…−0.64);
   - АЗС — под радио, `Btn_AnnunTest` — слева от радио, CABIN HEAT / DEFROST — выше;
   - бардачок — справа внизу, около 1 м от глаза пилота.

### C. Доска и приборы (п. 1–2 — И1, остальное — И2; v5 U4.4, v6 U4.1–U4.8, v7 U4.3)
1. `rig.avionicsPower` — питание авионики 0…1: шина под напряжением и `SW_Avionics`, с яркостью по напряжению, как у огней (`ElectricalSystem.Brightness`). От него светятся надписи радио (`Display_Radio` покрывает все шесть блоков) и фон экранов.
2. `lampGlow` оставить белым (по умолчанию): цвет лампы табло берётся из карты свечения. Проект его не пишет — записать «нет».
3. `rig.busVolts = t.BusVolts` (уже в `PlaneTelemetry`): около 28 В с генератором, 24 В на аккумуляторе, 0 без питания.
4. `rig.suctionInHg` — разрежение для гироприборов. Модели вакуума нет: **TEMPORARY** 5.0 inHg при работающем двигателе (Ng выше малого газа), плавный спад до 0 при останове; записать в Tuning.
5. Бардачок: зона `IV_Glovebox_Lid` (kind `hinge`, «Бардачок») переключает `rig.mapCase` 0 ↔ 1 плавно — так же, как козырьки (бит в `PlaneSwitch` + `MoveTowards` в `PlanePresentation`). Содержимое (карта, фонарь, РЛЭ) — неподвижная часть модели.
6. Крышка стартера: `autoStarterGuard = true` (по умолчанию) — Rig откидывает `Guard_Starter`, пока `SW_Starter` в `switchesOn`; делать ничего не нужно. Отдельное действие «поднять крышку» — **OPEN** (тогда `autoStarterGuard = false` и `rig.starterGuard` вести самим).
7. Высотомер (M3 закрыт):
   - `rig.altimeterSettingInHg` — 28.6…31.0, по умолчанию 29.92; зона `IV_Knob_ALT_Baro` (rotary) меняет установку шагом 0.01 inHg;
   - в `AircraftSystems` (~161) высоту считать от установки, а не от 101325 Па (TEMPORARY M3): давление уровня отсчёта = установка × 3386.39 Па (§10.3).
8. Курсовой:
   - `rig.headingBugDeg` — метка, зона `IV_Knob_HDG_Bug`, шаг 1°. Автопилот проекта держит курс на момент включения; брать ли курс с метки — **OPEN**, до решения не связывать.
   - Зона `IV_Knob_DG_Set` — согласование курсового. TEMPORARY M3 «курсовой сам согласуется с компасом» (`AircraftSystems` ~184, при включении питания) снять: курсовой уходит на 3–5 °/ч (§10.3), ручка сдвигает его показание шагом 1°. Угол ручки — `rig.dgSetKnobDeg`.
9. Компас: `rig.compassHeadingDeg` — магнитный курс с запаздыванием, **TEMPORARY** фильтр первого порядка τ = 1 с, без девиации и ошибок разворота; записать в Tuning.
10. `rig.autoKnobs = true` (по умолчанию): ручки высотомера и метки крутятся вслед за значениями сами.
11. Если код или тесты перечисляют стрелки — дополнить `Needle_VOLT`, `Needle_VAC`. Список экранов — `UtilityPlaneRig.DisplayNames`.

### D. Экраны (И2; v6 U4.4, v7 U4.8, v12 U4.2)
1. Семь квадов: `Display_COM1`, `Display_NAV1`, `Display_GPS`, `Display_XPDR`, `Display_AP`, `Display_ADF`, `Display_Hobbs`.
   - UV 0…1 на окно, материал `M_Screen`, размеры — `Data/displays.json`.
   - `Display_Hobbs` питается от шины, остальные — от `avionicsPower`. Пока `screensDrivenExternally` = false, Rig сам держит их тёмными без питания и со слабым фоном при питании.
2. На 16.10 проект на квадах ничего не рисует.
   - Экран GPS: содержание — по §10.3 промпта (путевая скорость, путевой угол, высота GPS, трек; только с авионикой и питанием). Это этап P06, в этом задании его не делать. Способ вывода из §10.3 (свой квад 0.0874 × 0.0653 м поверх `Display_Radio`) устарел: рисовать в материал квада модели `Display_GPS` (92 × 50 мм, UV 0…1).
   - Экраны COM, NAV, XPDR, AP, ADF, Hobbs: что показывают — **OPEN**, не рисовать. Радио — главный канал хоррора, решает пользователь.
3. Как рисовать, когда дойдёт:
   - шрифт `UtilityPlanePixelFont` (тот же, что на доске), `Large57`;
   - текстура — 1 тексель на мм окна: `Display_COM1`, `Display_NAV1` — 48 × 17, `Display_XPDR` — 60 × 18, `Display_ADF` — 72 × 17, `Display_AP` — 80 × 13, `Display_GPS` — 92 × 50, `Display_Hobbs` — 30 × 10;
   - `rig.screensDrivenExternally = true`, иначе Rig перезапишет свечение. Флаг общий для всех семи квадов: включив его, игра сама держит и остальные (тёмные без питания, слабый фон с питанием);
   - затем `UtilityPlanePixelFont.SetScreen(` экземпляр материала квада (`renderer.sharedMaterial` после `Awake` Rig) `, текстура, цвет свечения)` — контракт, раздел 7.

### E. Двери кабины, замок, ключ (И2; v7 U4.1–U4.2; M1 и M2 закрыты; §8.2–8.3)
1. Тумблеры дверей кабины:
   - зоны `IV_SW_CabinDoor_L/R` (kind `toggle`) — команды дверей кабины, как `SW_CargoDoor_L/R` в `PlaneVehicle.ServerUse`: вверх — открыть, вниз — закрыть;
   - привод `DoorDrive.Powered` (§8.2, TEMPORARY 70° за 2.5 с); без питания шины тумблер дверь не двигает;
   - биты — в `PlaneSwitch`, имена — в `PlanePresentation.SwitchObjects`: так они попадут в `rig.switchesOn` и в доводку `LateUpdate`.
2. TEMPORARY M1 снять: `Socket_Handle_PilotDoor_*_In` — снова просто внутренняя ручка, `Toggle(DoorDrive.Manual)` всегда. Сейчас там `CabinDoor()` — привод при питании.
3. Замка в проекте нет — сделать по §8.3: состояние `Locked` / `Unlocked` реплицируется. При `Locked` наружные ручки (кабины и грузовых дверей) не открывают — «Заперто»; кнопки и ручки изнутри работают всегда.
4. Ключ:
   - префаб `Prefabs/PlaneKey.prefab` (меш `SM_PlaneKey`, выпуклый `MeshCollider`) вместо TEMPORARY-меша §8.3;
   - игровой вариант — кодом сборки, как ящики: Rigidbody (TEMPORARY 0.05 кг), `NetworkObject`, `NetworkTransform`, захват как у груза, регистрация в `DefaultNetworkPrefabs.asset`;
   - кто держит ключ, решает сервер; «карман» — по §8.3 (TEMPORARY);
   - на старте уровня TEMPORARY: самолёт не заперт, ключ — в «кармане» хоста.
5. Скважины:
   - зоны `IV_Socket_Keyhole_PilotDoor_L/R` (kind `lock`, «Замок») на личинках `Lock_PilotDoor_L/R`, ездят с дверью;
   - «Использовать» с ключом переключает `Locked` / `Unlocked`, без ключа — «Заперто»;
   - TEMPORARY §8.3 «скважина — ручка снаружи» не делать: наружные ручки только открывают незапертую дверь.
   - Запереть изнутри — **OPEN** (по умолчанию нельзя).

### F. Ручки радио (И2; v7 U4.4)
1. 14 зон: `Knob_Audio_Vol`, `Knob_Com_Outer`, `Knob_Com_Inner`, `Knob_Nav_Outer`, `Knob_Nav_Inner`, `Knob_NavCom_Vol`, `Knob_GPS_Pwr`, `Knob_GPS_Outer`, `Knob_GPS_Inner`, `Knob_XPDR_Mode` (kind `selector`), `Knob_AP_Baro`, `Knob_ADF_Vol`, `Knob_ADF_Outer`, `Knob_ADF_Inner`. Ввод — как у реостатов `Rheo_*` (rotary) и кранов `FuelSel_*` (selector); если удобнее — ENGINEERING: курсор на ручке + колесо мыши, шаг 15°.
2. Rig: `rig.radioKnobDeg[i]` в порядке `UtilityPlaneRig.RadioKnobNames` (13 ручек), `rig.xpdrMode` 0…4 = OFF, SBY, ON, ALT, TST. Углы — в реплицируемом состоянии.
3. Что меняет ручка (частота, громкость, режим) — по системам, которые есть; пока систем радио нет, ручка только поворачивается.

### G. Запотевание и отпечатки (И2; v7 U4.5, v9 U4.5, v10 U4.2)
1. `rig.windshieldFog` (лобовое) и `rig.sideWindowFog` (остальные) 0…1 считает сервер и реплицирует. **TEMPORARY**-модель — записать в `Docs/Tuning/UtilityPlane.md`:
   - при наружной температуре ниже +5 °C стёкла запотевают на 0.004 в секунду за каждого человека в кабине и отсеке, до 1;
   - `Pull_Defrost` при работающем двигателе очищает лобовое на 0.05 в секунду × положение тяги;
   - `Pull_CabinHeat` при работающем двигателе очищает все стёкла на 0.02 в секунду × положение тяги и останавливает рост;
   - без двигателя обдув и обогрев не греют.
2. Отпечатки — событие сервера:
   - данные — точка в осях самолёта (`rig.transform.InverseTransformPoint`), кисть, размер, угол, `wipe`, сила; для полосы — две точки и ширина;
   - сервер рассылает `ClientRpc` всем, включая хост; каждый вызывает `rig.StampFogPrint` / `rig.StampFogStroke` с точкой, переведённой обратно (`TransformPoint`);
   - `ClearFogPrints` — тоже событием;
   - вошедшим позже стёртые места (`wipe = true`) не передавать: они затягиваются за `printRegrowSeconds`. Скрытые отпечатки (`wipe = false`) держатся до `ClearFogPrints`; если они важны для сюжета — передать вошедшему списком (контракт 7.2), иначе не передавать и записать это.
3. Кто ставит отпечатки в игре — **OPEN**. До ответа TEMPORARY — Dev-клавиша, выключенная по умолчанию:
   - ладонь там, куда смотрит игрок, если стекло ближе 1 м;
   - точку брать `rig.RaycastFog(луч из глаза, 1 м, out точка)`, а не попаданием физики: коллайдеры кабины стоят на 5–20 см ближе стекла;
   - записать клавишу в `Docs/Controls.md` как Dev.
4. `printRegrowSeconds` 25, `latentPrintVisibility` 0.6 — TEMPORARY. Read/Write у `UtilityPlane.fbx` не нужен: отпечатки читают копии слоёв запотевания из `Generated/`.

### H. Стремянка (И3; v7 U4.6, v9 U4.4; M5 закрыт)
1. Префаб `StepLadder`: `BoxCollider` ступеней, площадки, стоек, поручней и задних ног; триггер `IV_Ladder_Climb` (kind `ladder`, «Стремянка»). Игровой вариант — кодом сборки, как ящики: Rigidbody (TEMPORARY 8 кг), `NetworkObject`, `NetworkTransform`; переносится захватом (§12).
2. Установка у крыла по `Socket_Ladder_Fuel_L` / `_R` (на земле снаружи от основного колеса, на 0.40 м впереди передней кромки):
   - позиция — мировая точка сокета; поворот — только курс самолёта, `Quaternion.Euler(0, курс, 0)`, +Z стремянки к носу;
   - высоту брать лучом вниз с 1 м над сокетом: после осадки сокет на несколько сантиметров под землёй;
   - дверь пилота с той же стороны при стремянке открывается примерно на 50° из 70° — обычный `Blocked` по DOOR-01.
3. Подъём: объём `IV_Ladder_Climb` — лестница в стиле Half-Life (NEW-01).
   - Пока персонаж в объёме и смотрит на ступени, «вперёд» поднимает, «назад» опускает; площадка 1.56 м, на ней один персонаж.
   - Объём лежит на слое `Interact` без столкновений, и `OnTrigger*` не придут: присутствие проверять `Physics.OverlapCapsule` по капсуле персонажа с маской `Interact` и `QueryTriggerInteraction.Collide`.
   - Луч «использовать» эту зону не выбирает: у стремянки нет `InteractionHost`, и `Usable` её пропускает. Так и нужно — подъём работает по присутствию.
4. Досягаемость горловины (`Data/model_facts.json` → `ladder`, глаз 1.65 м над ступнями): глаз на 3.21 м над землёй, крышка бака на 2.78 м, от глаза до крышки 1.13 м. Серверная проверка дистанции для `IV_Socket_FuelCap_*` и заливки канистрой должна пропускать 1.13 м с запасом 0.3 м.
5. Задние ноги `Ladder_RearLegs` складываются поворотом до −38° по локальной X: при переноске — по желанию (ENGINEERING), перед укладкой в отсек — обязательно (тема L).
6. Где стремянка живёт в игре — **OPEN**. До ответа TEMPORARY: одна стремянка при старте уровня на земле у левого крыла; уложить её можно в отсек (L.5).

### I. Винт (И2; v7 U4.7)
1. `rig.propRpm` — фактические обороты винта, как сейчас. Rig сам меняет лопасти на размытие (250–450 об/мин) и на диск (800–1200). Пороги — поля Rig; подобрать на стенде при 60 и 144 Гц (TEMPORARY).
2. Флюгер: при `autoFeather = true` (по умолчанию) от 250 об/мин лопасти выходят в малый шаг, ниже 120 уходят во флюгер. Если `TurbopropEngine` ведёт флюгер рычагом `Lever_Prop` — выключить `autoFeather` и писать `rig.propFeather` 0…1.
3. Своего вращения `Propeller` в проекте нет (16.10) — подтвердить.

### J. Материалы, рендереры, LOD, тени, огни (И1; v5 U4.6, v7 U4.8–U4.9, v8 U4.2–U4.5, v9 U4.2, U4.7, v10 U4.1, U4.3, v11 U4.1–U4.2, v12 U4.1)
1. Rig создаёт свои экземпляры материалов в `Awake` и удаляет в `OnDestroy`. `MaterialPropertyBlock` на рендереры самолёта не писать: он выводит их из SRP Batcher.
2. Код, который перебирает рендереры самолёта (`GetComponentsInChildren<Renderer>` / `<MeshRenderer>`: скрытие модели, снег и иней, подсветка, рамка по bounds), — одно правило:
   - `<часть>_LOD1` / `_LOD2`: включать и выключать как исходные части или пропускать по имени. Видимость модели — не через `Renderer.isVisible` одного рендерера. Эффектам, которые рисуют меши сами (`Graphics.RenderMesh`, `CommandBuffer.DrawRenderer`: контур, подсветка), `LODGroup` не помогает — им только рендереры без `_LOD1` / `_LOD2`;
   - `*_Shadow` (5 теневых копий: `Cockpit_Static_Shadow`, `Cabin_Fittings_Shadow`, `Wheel_L_Shadow`, `Wheel_R_Shadow`, `TailWheel_Shadow`): прятать вместе с остальными, при показе возвращать каждому рендереру прежнее `enabled`; не подсвечивать и не перекрашивать; не менять их `shadowCastingMode` и не включать тень частям с копиями;
   - `Glow_*` (8) и `Beam_*` (3), которые Rig создаёт в игре: пропускать по `UtilityPlaneRig.IsEffectRenderer(r)`. Чтобы спрятать самолёт вместе со светом — ещё `rig.glows = false`, `rig.beams = false`, при показе вернуть прежние значения;
   - ремни `Belt_*`, стропы `Stow_LadderStraps` и слои запотевания Rig включает и выключает сам — не трогать;
   - если код сам двигает части, которые ведёт Rig, или включает их рендереры — после него `rig.ResyncPose()`. Исключение — доводка в `PlanePresentation.LateUpdate` (тумблеры, краны, рычаг закрылков): её конечный угол совпадает с углом Rig, `ResyncPose` после неё не нужен. Но поворот она должна писать только пока орган движется: сейчас пишет каждый кадр, и пункт 45 (на стоянке трансформы не меняются) не пройдёт.
   На 16.10: `HoverOutline` для зоны рисует только рендерер родителя зоны, для предмета — рендереры предмета; `InteractionOverlay` берёт bounds цели. По самолёту целиком код не проходит — подтвердить и записать «нет».
3. Материалы:
   - крыло и стабилизатор (подмеш `Body`), закрылки, элероны, рули высоты — на `M_Wing`; подмеши `Body` по порядку `M_Airframe`, `M_Interior`, `M_Wing`, `M_Details`, но искать их по имени материала, не по индексу;
   - PSX-материалы (экстерьер с v9, интерьер с v12) — без Normal Map и Detail, AO в маске = 1 (затенение запечено в цвет); текстуры Point.
   - Поиск по коду и ассетам проекта: `M_Airframe`, `M_Wing`, `M_Details`, `M_Cockpit`, `M_Interior`, `M_Floor`, `M_Seats`, `M_Fittings`, `M_Webbing`, `_DetailAlbedoMap`, `_DetailNormalMap`, `_BumpMap`, `_OcclusionStrength`. Код, который ставит этим материалам карты или читает их текстуры по старой развёртке (снег, иней, грязь, повреждения), — убрать или перевести на экземпляры. На 16.10 в коде ссылок нет; ассеты (сцены, вариант) проверить по GUID.
4. Тени: у мелочи (стрелки, ручки, тумблеры, лампы, линзы, экраны, ремни, замки, диски винта, стёкла) меню их выключило. Переопределения `shadowCastingMode` в варианте и сценах снять.
5. LOD:
   - рендереры, которые добавляет вариант, остаются вне `LODGroup` — это допустимо; тяжёлые добавить в уровни в коде сборки варианта: `GetLODs` → добавить → `SetLODs` → `RecalculateBounds`;
   - пороги в базовом префабе и в `Data/lod.json` не менять; подбор — в коде сборки варианта, TEMPORARY, в Tuning;
   - плавный переход (`CrossFade`) не включать без решения пользователя.
6. Огни: 16 источников вместо 12 (`Light_Cargo_2` с v5; `Light_Flood_2`, `Light_Fill_Cockpit`, `Light_Fill_Bay` с v11); имена без двойного префикса (`Light_Dome_L/R`, `Light_Cargo`, `Light_Flood` вместо `Light_Light_*`). `cargoLight` ведёт обе лампы отсека.
   - Код, который считает источники или меняет их `color`, `range`, `spotAngle`, `shadows`, `cookie`, — убрать или перевести на поля Rig (`shadowQuality`, `floodRed`, `glowIntensity` …).
   - Яркость регулировать только полями Rig и кодом сборки варианта. `Light.intensity` в префабе — это 100 %; его не трогать: меню пересоздаёт источники при каждом запуске.
   - На 16.10 таких мест нет (поиск `"Light_` и `GetComponentsInChildren<Light>` пуст; `AircraftSystems` считает только амперы) — подтвердить.

### K. Откидные кресла отсека (И3; v9 U4.3; SEAT-01, SEAT-02)
1. В `PlaneBenchBuilder` после `Seat_P` и `Seat_C` добавить четыре `SeatAnchor` (функция `Seat(...)`):
   - `seatId` = `PaxSeat_L1`, `PaxSeat_R1`, `PaxSeat_L2`, `PaxSeat_R2` (= id зон), `role` = `Passenger`, `exitDoor` = null;
   - `pelvis` = `Socket_Seat_PaxL1` …, `eye` = `Socket_PaxEye_L1` …, `exit` = `Socket_PaxExit_L1` …;
   - порядок и имена — `Data/pax_seats.json`.
2. Положение кресел — серверное состояние: `byte PaxSeats` (бит i = кресло i опущено, i — индекс `paxSeatDown`) и `bool LadderStowed` отдельными полями `PlaneSwitchState`. На старте уровня TEMPORARY все кресла сложены.
3. Зона `PaxSeatFold_*` (hinge) — сервер переключает кресло:
   - только если оно пустое; `R1`, `R2` — только если стремянка не уложена;
   - опускает, только если объём опущенного кресла свободен от груза: `Physics.OverlapBox` по коллайдерам сиденья и опоры в опущенной позе с маской груза. Кресла 1 опускаются над кольцами `Socket_TieDown_01` / `_03`, кресла 2 — вплотную к `_13` / `_15`;
   - иначе отказ с подсказкой: «Кресло занято», «Мешает стремянка», «Мешает груз».
4. Зона `PaxSeat_*` (seat): сесть можно только на опущенное кресло (сервер); пока сложено — зона прозрачна (И1.4).
5. Rig на всех пирах: `rig.paxSeatDown[i]` плавно 0 → 1 за TEMPORARY 0.5 с. Это вид, а не логика: сидеть можно сразу по состоянию.
6. Между опущенными креслами напротив — 0.43 м по опорам, капсула персонажа не проходит. К сетке и задней перегородке пройти можно только при сложенных креслах этой пары. Это ожидаемо — записать в Docs.
7. Выход из кресла отсека — **внутрь**. `SeatedVehicle.FindExit` (~121) ищет землю под сокетом, пропуская коллайдеры самолёта, и поставит игрока под самолёт. Для `PaxSeat_*` (или `SeatAnchor` с флагом «внутри», ENGINEERING):
   - ступни — в `Socket_PaxExit_*` на полу отсека;
   - проверка капсулы — присевшей (0.90 м) против всего, кроме самого игрока;
   - если занято — точка выхода другой пары: `Socket_PaxExit_*` пары 1 (`L1`, `R1`) стоят на оси отсека при Z +0.70, пары 2 (`L2`, `R2`) — при Z −0.30; если и она занята — «Проход занят»;
   - при уложенной стремянке точки выхода на ней: `L1`, `L2` выпускают наружу через `Socket_Exit_Cargo_L`, если левая грузовая дверь открыта, иначе отказ.
8. Выход в полёте — **OPEN**: ходьба по салону в движении — DEFERRED (SEAT-02, §7.5–7.6). До ответа TEMPORARY: выход из кресел отсека только при путевой скорости < 0.5 м/с (`PlaneTelemetry.GroundSpeed`), иначе «Самолёт движется».
9. Масса и центр масс: сидящие в отсеке — как люди в кабине (точка — `pelvis`). Ремни кресел отсека — только вид; удержание ремнями — **OPEN** (как O-05). Лампа SEAT BELT — по-прежнему только кресла кабины.

### L. Места хранения и аварийный комплект (И3; v9 U4.4, U4.7; CARGO-01, SECURE-01)
1. В `PlaneBenchBuilder` по `Data/stowage.json` на каждый `Socket_Stow_<id>` — компонент места хранения (ENGINEERING, например `StowPoint`: `stowId`, какой предмет принимает, занятость — серверное состояние). Сокет задаёт полную позу (позиция и поворот), а не центр дна с курсом, как `TieDownSocket`.
2. Предметы — игровые варианты префабов `Surv_*` кодом сборки, как ящики: Rigidbody, `NetworkObject`, `NetworkTransform`, `CargoItem`, регистрация в `DefaultNetworkPrefabs.asset`. Массы — TEMPORARY из `Data/survival_kit.json`, записать в `Docs/Tuning/Cargo.md`. Канистра — `KitCarry_JerryCan` комплекта (И4), стремянка — `StepLadder`.
3. Положить: игрок несёт подходящий предмет и использует зону `Stow_<id>` → сервер ставит корень предмета в позу сокета и держит.
   - Позы пересекают коллайдеры самолёта на 2–7 см — так предмет прижат. Пока предмет на месте, его коллайдеры не должны сталкиваться с самолётом: `Physics.IgnoreCollision` со всеми коллайдерами самолёта или слой без столкновений с ним.
   - Зоны `Stow_*` прозрачны, пока игрок не несёт подходящий предмет (И1.4).
   - Взять — захватом самого предмета. `HoverOutline`: зоны кресел и `Stow_*` рисуются коробкой зоны, зона складывания — контуром сиденья; проверить.
4. Фонарь и ракетница лежат на дверях пилота и едут с дверью. Дверь — не отдельное тело, `FixedJoint` к самолёту не годится. Держать предмет кинематическим и каждый шаг физики ставить в мировую позу сокета (сервер; клиенты — через `NetworkTransform`) или другой способ — ENGINEERING, записать. Сумка: дочерняя `Surv_Bag_Strap` видна, пока сумка на месте.
5. Стремянка в отсеке:
   - задние ноги сложены (−38°); уложить можно, только если `R1` и `R2` сложены и место свободно от груза (`model_facts.json` → `ladderStowed.boxMin/Max`);
   - уложена — `LadderStowed` = true на сервере, `rig.ladderStowed` на всех (стропы и бруски);
   - она закрывает кольца `Socket_TieDown_05`, `_06`, `_08`, `_09`, `_11`, `_12`, стропы держатся за `_04` и `_10` — крепить на них груз сервер не даёт;
   - канистра в держателе закрывает половину кольца `_13`: груз на `_13` — только без канистры.
6. Что предметы делают (свет фонаря из его `Socket_Light`, выстрел ракетницы из `Socket_Muzzle`, горение фальшфейера в `Socket_Flame`, копание), что в сумке, сколько патронов — **OPEN**. До ответа предметы — груз, который носят и кладут на место.

### M. Хоррор (И3; v9 U4.6, v11 U4.4)
1. Сокеты хоррора кода не требуют. Записать в статус, что точки есть (`Data/horror.json`, контракт 10.3):
   - 16 сокетов фигур `Socket_Scare_*` и 12 новых сокетов звука `Socket_Audio_*`;
   - `Socket_Audio_Radio` — главный канал хоррора;
   - фигуры у окон видны, только если стекло не запотело или протёрто;
   - сценарии по этим точкам — **OPEN**.
2. Мигание огней `lightFlicker` / `FlickerBurst()` не подключать: **OPEN** — когда мигать и как рассылать по сети (`FlickerBurst` событием, `flickerSeed` и `flickerClock` общие). Только проверка через Inspector (I-29).

### N. Меши во время игры (И1; v10 U4.2–U4.3)
1. `Models/UtilityPlane.fbx` импортируется без Read/Write. Коллайдеры и слои запотевания — читаемые копии `COL_*_RT`, `*_Fog_RT` из `Generated/UtilityPlane_RuntimeMeshes.asset`.
   - Поиск по коду проекта: `.vertices`, `.normals`, `.triangles`, `.uv`, `GetVertices`, `GetNormals`, `GetTriangles`, `GetIndices`, `AcquireReadOnlyMeshData`, `BakeMesh`, `StaticBatchingUtility.Combine`; запекание NavMesh во время игры с источником Render Meshes; контур со сглаженными нормалями; `MeshCollider` с рендер-мешем самолёта.
   - Если что-то берёт меши `UtilityPlane.fbx` во время игры — перевести на `COL_*` или `Data/*.json`. Если нельзя — `UtilityPlaneSetup.KeepMeshesReadable = true`, меню ещё раз, причину записать.
   - Коллайдеры и слои запотевания ссылаются на копии `COL_*_RT`, `*_Fog_RT`, а не на меши FBX: код, который сравнивает их меши с мешами FBX по ссылке или имени, поправить (контракт 17.3).
   - На 16.10 такого нет: `HoverOutline` берёт `mesh.bounds` и рисует `Graphics.RenderMesh`, `NetworkDoor` и `BenchCargoSpawner` читают bounds — без Read/Write это работает; `SkyIslandPrefabs` строит свой квад; сравнений мешей по имени нет. Подтвердить.
2. Ремни и стропы: если проект сам прячет `Belt_*` или `Stow_LadderStraps` — убрать, это делает Rig по `beltP`, `beltC`, `ladderStowed` (на 16.10 не прячет).

### O. Свет в атмосфере (И1; v11 U4.3)
1. В одном месте (`PlanePresentation` или код сборки варианта) включить `rig.atmosphereFromSceneFog = true` (`atmosphereFogScale` 40 по умолчанию). На Sky Island это даёт `atmosphere` ≈ 0.14 у земли и меньше выше; на стенде P03 без тумана Rig берёт `atmosphere` (0.25). Где поставлено — записать.
2. Bloom профилей не менять. Записать, как выглядят блики с текущим Bloom; если пользователь захочет мягче — предложить порог 1, интенсивность 0.3–0.6, рассеяние 0.65.

### P. Документы (И1–И5)
- `Docs/ImplementationStatus.md`:
  - модель v12 (редакция 2) и комплект 1.2; что подключено по темам A–O;
  - пробелы: M1, M2, M3 закрыты (v7), M5 — стремянка, M6 — синхронные штурвалы (решение пользователя), M7 — канистра комплекта; M4 (руки и тело) открыт;
  - OPEN из раздела 5.
- `Docs/ProjectAudit.md`: N1, N2, N3, N4, N5, N7, N9 закрыты моделью (v5); N6 — высота отсека оставлена по решению пользователя; N8 — без изменений.
- `Docs/Tuning/UtilityPlane.md` и `Cargo.md` — TEMPORARY-числа и правила этого задания: установочный угол, вакуум, запотевание, компас, пороги винта, массы стремянки, ключа и предметов, время складывания кресел, правила выхода из кресел отсека (< 0.5 м/с, точки выхода), отпечатки, правила комплекта.
- `Docs/Controls.md` — Dev-клавиши (отпечаток и др.).

## 4. Что проверить

Ночная сцена — стенд P03 с выключенным солнцем или Sky Island ночью. Камера — игрока. Поля Rig — через Inspector в Play Mode или Dev-команду. Game View — 1920 × 1080. «Пункт N» — раздел 11 контракта v12. Пункты 29 и 40 контракта устарели (интерьер с v12 — PSX 512²): вместо них 58, 62, 64. В скобках — источник в прежних заданиях.

| № | Итер. | Проверка | Ожидание |
|---|---|---|---|
| I-01 | И1 | установка | строки меню И1.3; префаб — как в И1.3.3–И1.3.4; два `LODGroup`; пять `*_Shadow` в LOD0; у `UtilityPlane.fbx` Read/Write выключен, у `SM_*` включён; есть `Generated/UtilityPlane_RuntimeMeshes.asset` (пункты 39, 47) |
| I-02 | И1 | импорт текстур | пункты 27, 47, 58: PSX-атласы и надписи — Point, мипы, Aniso 0; цвет без сжатия, маски и свечение со сжатием; интерьер 512² (Fittings 256²), надписи 1024², экстерьер 512², `T_Webbing_BaseColor` 32 × 8, `T_PixelFont` без мипов; cookie без мипов и сжатия, Clamp |
| I-03 | И1 | стоянка и шасси | пункты 1–2: на стенде и Sky Island стоит на трёх колёсах, оседает на сантиметры без подпрыгивания; нижняя точка колеса на земле при любой нагрузке; хвостовое колесо управляется педалями, стояночный тормоз держит (v5 V1–V3) |
| I-04 | И1 | разбег | хвост поднимается до отрыва; отрыв и сваливание близки к оценкам Tuning; фактические цифры записать (v5 V4) |
| I-05 | И1 | табло и подсветка радио | пункты 5, 6: без питания не горит ничего; тест — горят все 12; погасшая лампа — тёмное цветное стекло с читаемой надписью; `avionicsPower` 0 — окна и надписи радио тёмные, 1 — светятся |
| I-06 | И1 | груз, двери, подножки | закрепление у всех 15 сокетов, стопки; на стоянке ящики не сползают; все ящики стенда берутся лучом (зоны `Stow_*` прозрачны); все 4 двери открываются и закрываются, ручки `_In` работают только изнутри, `_Out` — только снаружи; грузовые двери останавливаются о груз; персонаж встаёт на подножку грузовой двери и заходит в отсек, в отсеке пригибается (v5 V9–V11) |
| I-07 | И1 | кресла кабины | посадка и выход в обоих креслах; из кресла пилота достаются все органы, включая `Pull_FuelShutoff`, тумблеры у левого борта и бардачок; луч на внутреннюю ручку правой двери выбирает `Socket_Handle_PilotDoor_R_In`; подсветка по B без мерцания (v5 V8, v6 W9) |
| I-08 | И1 | вид PSX | снаружи — пункт 27; внутри — пункты 62, 64: из кресла днём и из отсека при плафонах доска, стены, потолок и пол явно пиксельные; надписи чёткие (пункт 59); номера кресел 1–4 читаются |
| I-09 | И1 | LOD и тени | пункты 23, 24, 37, 43: переходы без скачков; треугольники уровней равны `Data/lod.json` (считать `GetIndexCount`, без `*_Shadow`). Пункт 42: тени кресел, доски, штурвалов на месте; у мелочи теней нет, у крупных частей есть (v7 X12) |
| I-10 | И1 | рендереры | контур при наведении рисует одну копию детали, не три уровня LOD; рамка Alt / B не раздувается от `Glow_*` / `Beam_*`; скрытие модели, если есть, прячет и свет и возвращает всё как было |
| I-11 | И1 | огни | пункты 48–53: секторы АНО, двойная вспышка стробов, вращающийся маяк, фара с пятном и тенью, плафоны, заливка доски, трубка отсека; свет кабины снаружи — только через окна; в Play Mode под `Light_*` есть `Glow_*` (8) и `Beam_*` (3), в консоли нет `glow / beam materials missing` (v11 Q1–Q4) |
| I-12 | И1 | атмосфера, тени огней, стоимость | пункты 55–57: на Sky Island огни в дымке видны, не сплошное пятно; `shadowQuality` Off / Low / Medium / High на ходу (0 / 1 / 7 / 9 теней); GPU-время `Airlight` + `Beam` записать (v11 Q5, Q6, Q9) |
| I-13 | И1 | остальное из раздела 11 | пункты 3, 4, 7, 8, 18, 20–22, 25, 26, 28, 41 — одним проходом: каналы и знаки, стрелки, коллайдеры и масштаб, SRP Batcher, экстерьер вблизи, износ, швы UV, салон днём, материалы, нет дыр и мерцания |
| I-14 | И1 | Rig | пункт 45: на стоянке без изменения полей трансформы частей не меняются (Profiler); после `ResyncPose()` всё на местах; тумблеры, краны и рычаг закрылков доводятся плавно, как раньше, и после остановки их трансформы больше не пишутся (J.2) |
| I-15 | И1 | видеопамять и FPS | «до» (И1.1.4) и «после»: текстуры самолёта, треугольники кадра из кабины и снаружи с 5 м, FPS (пункт 46) |
| I-16 | И2 | доска днём и ночью | пункты 9, 59–61: из кресла пилота при 1920 × 1080 читаются цифры приборов, табло, надписи тумблеров, таблички; из кресла второго пилота — приборы систем, OPERATING LIMITATIONS, NO SMOKING; ночью светятся шкалы, окно давления, картушка компаса, надписи DOORS; красная заливка; мелкие подписи радио и АЗС — с 2560 × 1440 или наклонившись (v7 X10, v12 Q2–Q5) |
| I-17 | И2 | бардачок, стартер, вольтметр, вакуум | пункты 10, 11: крышка ходит у хоста и клиента одинаково; при стартере крышка откидывается, лампа STARTER ON; 0 без питания и двигателя, около 28 В и 5 inHg с работающим двигателем (v6 W6–W8) |
| I-18 | И2 | двери кабины, замок, ключ | пункт 15; тумблеры DOORS открывают и закрывают двери при питании, лампа DOOR; без питания — только ручки; снаружи без ключа «Заперто», с ключом замок переключается, изнутри открывается всегда; ключ, выпавший у одного клиента, поднимает и использует другой, дублей нет (v7 X3, X4; AT-K1, AT-K2) |
| I-19 | И2 | высотомер, курсовой, компас | пункты 13, 14: в окне давления читается установка, высота меняется около 1000 ft на 1 inHg; метка едет с карточкой; ручка DG сдвигает курсовой; компас догоняет курс с запаздыванием, при курсе N буква E слева от черты (v7 X5, X6) |
| I-20 | И2 | ручки радио, досягаемость | пункт 16: каждая крутится вокруг своей оси; у соосных пар внешняя и внутренняя выбираются раздельно; ответчик — OFF / SBY / ON / ALT / TST; из кресла пилота луч достаёт тумблеры DOORS, ручки радио, высотомера и курсового (v7 X7, U4.11) |
| I-21 | И2 | винт | пункт 12: флюгер на 0, размытие на 300–600, диск на 1900 без стробоскопа при 60 и 144 Гц; крутится по часовой из кабины (v7 X1, X2) |
| I-22 | И2 | запотевание и отпечатки | пункты 17, 35: на холоде с людьми стёкла запотевают; DEFROST очищает лобовое снизу вверх, CABIN HEAT — все; Dev-отпечаток у хоста и клиента в одном месте, затягивается за 25 с; без мерцания изнутри и снаружи (v7 X8, v9 Z8) |
| I-23 | И2 | экраны | если экраны рисуются — пункт 63; если нет — тёмные без питания, слабый фон с питанием; тестовым скриптом в Play Mode (без сохранения в проект) частота `118.30` через `SetScreen` на `Display_COM1` читается из кресла (v12 Q7) |
| I-24 | И3 | кресла отсека в сети | пункты 30–32: откидываются у хоста и клиента одинаково, на старте сложены; занятое не складывается; при уложенной стремянке `R1` / `R2` не опускаются; сесть — глаз в `Socket_PaxEye_*`, лицом к проходу; выход — на пол отсека, при занятом проходе вторая точка или отказ, при стремянке `L1` / `L2` — через открытую левую грузовую дверь; в движении — отказ (v9 Z4, Z5) |
| I-25 | И3 | места хранения и зоны | пункт 33: каждый предмет точно на месте, без проваливания; фонарь и ракетница едут с дверью; стропа сумки видна, пока сумка на месте; стремянка со сложенными ногами на брусках под стропами; зоны `Stow_*` срабатывают только с подходящим предметом; с ракетницей в руках луч из кресла пилота на внутреннюю ручку правой двери выбирает ручку, а не `Stow_FlareGun` (пункт 38); сложенные кресла луч не перехватывают; канистра и кольцо `_13` — после И4 (v9 Z6, Z7) |
| I-26 | И3 | стремянка | пункт 19: ставится по сокету вертикально; персонаж поднимается на площадку, видит и открывает крышку бака; спускается; переносится захватом (v7 X9) |
| I-27 | И3 | аварийный комплект | пункт 34: шесть `Surv_*` с выпуклыми коллайдерами; переносятся как груз (v9 Z11) |
| I-28 | И3 | сокеты хоррора | пункт 36: `Socket_Scare_Window_*` снаружи против окон на высоте лица, смотрят внутрь; точки на земле при `ParkedPose` лежат на земле (v9 Z9) |
| I-29 | И3 | мигание (только проверка) | пункт 54 через Inspector, без подключения к игре (v11 Q7) |
| I-30 | все | сеть | хост и клиент: свет, двери, замок, бардачок, ручки, кресла, места хранения, запотевание — одинаково у обоих и у вошедшего позже; отпечатки — одинаково у хоста и клиента, вошедшему позже старые стёртые места не приходят (G.2); фазы стробов и маяков могут различаться (v11 Q8) |
| I-31 | все | сборка | Development Build: персонаж ходит по полу кабины и отсека, груз на `COL_Floor`, двери, кресла и места хранения работают; свечение и лучи огней есть; отпечаток Dev-командой ставится; вид как в редакторе (пункт 44; v10 Q4, v11 Q10, v12 Q9) |
| I-32 | все | консоль | ошибок нет; шейдеры `UtilityPlane/GlassFog`, `Airlight`, `Beam` без ошибок; предупреждения импорта записать |
| KX1–KX10 | И4 | зимний комплект | по заданию комплекта, с поправками И4 |

## 5. Решения пользователя, которые ещё нужны (OPEN)

Не решать за пользователя. Пока ответа нет, работает временное решение из второй колонки. Вопросы собрать в отчёт одним списком.

| Вопрос | До ответа (TEMPORARY) |
|---|---|
| Экраны COM, NAV, ответчика, автопилота, ADF, моточасов: что показывают (экран GPS — по §10.3, этап P06) | не рисовать: тёмные без питания, слабый фон с питанием |
| Ключ зажигания (§3, §8.3) | нет: двигатель запускается без ключа, как сейчас |
| Подтвердить TEMPORARY-модели запотевания (G.1) и компаса (C.9) | как в задании |
| Крышка стартера — отдельным действием? | `autoStarterGuard` |
| Метка курса — задатчик для автопилота? | только метка |
| Запереть самолёт изнутри (§8.3); запасной ключ, если ключ потерян снаружи; где ключ при старте уровня | нельзя; нет; не заперт, ключ в «кармане» хоста |
| Где живёт стремянка и как без неё к горловинам | одна у левого крыла при старте уровня |
| Выход из кресел отсека в полёте | только при путевой скорости < 0.5 м/с |
| Ремни кресел отсека как удержание (O-05) | только вид |
| Что делают фонарь, ракетница, фальшфейер, лопата; что в сумке; патроны | только груз |
| Кто ставит отпечатки на стёклах (сценарий, игрок рукой) | Dev-клавиша |
| Сценарии хоррора по сокетам фигур и звука | нет |
| Мигание огней для хоррора: когда и как по сети | не подключено |
| Bloom мягче | профили не менять |
| Форточка (G16) | нет |
| Зимний комплект: где лежит; где наполнять канистру; последствия (отказ стартера или поломка, нужна ли стремянка для чехлов крыльев); питание подогревателя | по заданию комплекта: стартовый набор на самолёте, бочка у стоянки, правила K4.3 |
| Удалить ненужные файлы v4 (И1.3.2) | не удалять |
| Настройки рендера, если что-то выключено или мешает (И1.1.5) | не менять |

## 6. Если в проекте не v4

Признаки версий (по README модели и файлам):
- v5 — `Data/model_facts.json`, метод `UtilityPlaneRig.ParkedPose`;
- v6 — объект `Guard_Starter`;
- v7 — `Data/glass_fog.json`, `PropBlade_1`;
- v8 — `Data/lod.json`, `Body_LOD1`;
- v9 — `Data/pax_seats.json`, `PaxSeat_L1`;
- v10 — `Generated/UtilityPlane_RuntimeMeshes.asset`, объекты `*_Shadow`;
- v11 — `Scripts/UtilityPlaneRig.Lights.cs`, 16 `Light_*`;
- v12 — `Scripts/UtilityPlanePixelFont.cs`: у редакции 2 `T_Cockpit_BaseColor.png` 512 × 512, у редакции 1 — 1024.

Если признаки расходятся с заголовком README, верен заголовок; расхождение записать. Подготовка И1.1 (резервная копия, «до», настройки) нужна всегда. Пакет ставить так же (И1.2–И1.3). Правки тем A–O, которые для стоящей версии уже сделаны, — проверить и не повторять; остальное — как здесь. Если стоит v12 редакции 1 — только И1.1–И1.3, И1.7 и проверки I-01, I-02, I-08, I-15.

## 7. Факты модели v12 вместо §5 и §13 промпта

**§5 — пакет `Assets/UtilityPlane` v12.** Что читать:
- `README.md` — установка и обновление; `COMPONENTS.md` — что за что отвечает, пробелы модели;
- `UNITY_CONTRACT.md` — иерархия, оси и знаки, формулы стрелок, лампы, огни, свет, материалы, сокеты, коллайдеры, зоны; проверки — раздел 11;
- `Data/`: `interaction_volumes.json` (79 зон), `sockets.json` (125 сокетов), `model_facts.json` (стоянка, колёса, стремянка, кресла), `cargo_layout.json` (6 ящиков), `displays.json` (7 экранов), `glass_fog.json`, `pax_seats.json`, `stowage.json`, `horror.json`, `survival_kit.json`, `lod.json`, `lights.json`, `atlas_texel.json`, `pixel_font.json`;
- скрипты: `Scripts/UtilityPlaneRig.cs` + `UtilityPlaneRig.Lights.cs` (слой отображения), `UtilityPlanePixelFont.cs`, `UtilityPlaneInteractable.cs`; `Editor/UtilityPlaneSetup.cs` (меню).

Имена объектов, сокетов, ламп и свойств материалов — только оттуда. Стоянка — только `UtilityPlaneRig.ParkedPose`.

Меню `Build Materials + Prefabs` создаёт:
- материалы URP; слой `Interact` меню только добавляет, если его нет, — в проекте он уже есть (индекс 8), столкновения с ним в Physics Settings выключены (16.10);
- `Prefabs/UtilityPlane.prefab`: 60 выпуклых коллайдеров, 79 зон `IV_*`, 16 источников света, `UtilityPlaneRig`, два `LODGroup`, 5 теневых копий;
- `Cargo_Case1..6`, `StepLadder`, `PlaneKey`, `Surv_*` (6);
- `Generated/UtilityPlane_RuntimeMeshes.asset`.

**§13 — пробелы модели.**

| ID | Пробел | Сейчас |
|---|---|---|
| M1 | кнопки дверей кабины | закрыт v7: `SW_CabinDoor_L/R` (тема E) |
| M2 | меш ключа и скважина | закрыт v7: `PlaneKey`, `Lock_PilotDoor_L/R` (тема E) |
| M3 | кремальера высотомера, ручки курсового, компас | закрыт v7 (тема C) |
| M4 | руки и тело персонажа | открыт: простые руки |
| M5 | горловины на 2.8 м | закрыт v7 стремянкой (тема H); где она хранится — OPEN |
| M6 | вторые штурвал и педали | закрыт: синхронные (решение пользователя) |
| M7 | канистра | закрыт зимним комплектом 1.2 (`KitCarry_JerryCan`) |

## 8. Не делать

- Не менять геометрию, текстуры, шейдеры, cookie и другие файлы модели и комплекта в `Assets/UtilityPlane`. Исключения — `KeepMeshesReadable` (N.1) и удаление ненужных файлов с разрешения пользователя. Если правка модели нужна — запрос пользователю с точным списком.
- Не менять импорт PSX-текстур и надписей (фильтр, сжатие, размер) и не включать Read/Write у `UtilityPlane.fbx` руками.
- Не удалять и не править `Generated/UtilityPlane_RuntimeMeshes.asset`: его пересоздаёт меню (контракт 17.3).
- Не ставить новые пакеты без разрешения пользователя (§1.1), в том числе Memory Profiler.
- Не менять `PC_RPAsset`, `PC_Renderer`, Graphics Settings, Quality и профили постобработки без разрешения пользователя.
- Без решения пользователя не подключать мигание огней, не добавлять зоны кнопкам радио, АЗС и гнёздам, не рисовать экраны.
- Не править `UNITY_CONTRACT.md` под код: если код и контракт расходятся — проверить на стенде, затем спросить.
- Не выполнять задания `PLANE_V5_UPGRADE_UNITY.md` … `PLANE_V12_UPGRADE_UNITY.md` по отдельности: их шаги собраны здесь.

## 9. Отчёт

После каждой итерации — по шаблону §0 промпта. Итоговый отчёт:
- с какой версии обновлено, что заменено и собрано;
- настройки из И1.1.5;
- изменённые файлы кода по темам A–O с причинами и требованиями; где ответ «нет» — почему;
- `KeepMeshesReadable`: false или true и почему;
- проверки I-01…I-32 и KX1–KX10 со статусами и снимками: из обоих кресел днём и ночью, из отсека, снаружи ночью; кроп 1:1 доски;
- цифры до/после: видеопамять, треугольники, FPS, GPU-время эффектов;
- OPEN одним списком (раздел 5) — вопросы пользователю.

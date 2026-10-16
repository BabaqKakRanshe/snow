# Аудит проекта Snow — этап P00

Дата: 29.09.2026. Основание: `docs/gdd/PLANE_PROMPT_UNITY.md` v2.0, §1, §5, §13, §14 (P00), §16.
Проект: `F:\ClaudeGames\snow\Snow`. Все пути ниже — от корня проекта, если не сказано иное.

## 0. Итог

| Пункт §16 | Результат |
|---|---|
| 1. Аудит проекта | выполнен, §1–§3 этого файла |
| 2. Вопрос о пакетах | задан пользователю, ответа пока нет (§4) |
| 3. Модель | установлена, меню установки выполнено, столкновения `Interact` выключены, знаки проверены (§5, §6). Компиляция PASS |
| 4. Матрица SYS-02 | §7 |
| 5. Сверка пробелов с §13 | §8. M1–M7 подтверждены, найдено 9 новых пунктов N1–N9 |

Блокер для P01 один: ответ пользователя о сетевых пакетах (§4).

## 1. Инструменты агента

| Инструмент | Состояние | Возможности и ограничения |
|---|---|---|
| Unity MCP (`com.unity.ai.assistant` 2.20.0-pre.1) | работает | `RunCommand` (C# в редакторе), меню, Play/Stop, консоль, снимки камер, данные Package Manager. **Запрещено:** `System.Reflection` в `RunCommand`. `AssetDatabase.DeleteAsset` через MCP отклоняется (требует подтверждения в редакторе) |
| Unity Pipeline CLI (`com.unity.pipeline` 0.8.0-exp.1, `C:\Users\user\AppData\Local\Unity\bin\unity.exe`) | работает | `unity command --project-path <проект> <команда>`: `recompile`, `recompile_status`, `console`, `console_status`, `run_tests`, `list_tests`, `capture_game_view`, `delete_asset`, `set_physics_settings`, `package_add` и др. **Ограничение:** пути записи привязаны к корню `Assets` (`--save_path` вне `Assets` пишет в `Assets/...`) |
| Терминал | работает | PowerShell 5.1, Git Bash, Python 3 с Pillow |
| Blender MCP | подключён | для P00 не нужен |
| Редакторы Unity | 6000.0.67f1 (используется), 6000.4.5f1, 6000.6.0f1 | другие версии не использовать (CORE-01) |
| Второй процесс/сборка клиента | не проверялось | понадобится в P01–P02 |

## 2. Проект — факты (повторная проверка §1)

| Что | Факт | Совпадает с §1 |
|---|---|---|
| Редактор | 6000.0.67f1 (78a1c2bbeb6a) | да |
| Рендер | URP 17.0.4. Default RP и качество PC → `Assets/Settings/PC_RPAsset.asset`; Mobile → `Mobile_RPAsset.asset`. Активный уровень качества — PC | да |
| Путь рендера | PC_Renderer — Forward+ (`m_RenderingMode: 2`), Mobile_Renderer — Forward (`0`) | да |
| Цвет | Linear | да |
| Графический API (Win64) | Direct3D11 | — |
| Скриптинг | Mono, .NET Standard 2.0 | — |
| Ввод | `com.unity.inputsystem` 1.18.0, Active Input Handling = New (1). `Assets/InputSystem_Actions.inputactions` назначен project-wide actions (`EditorBuildSettings → com.unity.input.settings.actions`). Карты: Player (Move, Look, Attack, Interact, Crouch, Jump, Previous, Next, Sprint), UI | да |
| Тесты | `com.unity.test-framework` 1.6.0; тестов в проекте 0 (`list_tests`) | да |
| Сеть | нет сетевого стека. `com.unity.multiplayer.center` 1.0.0 — только окно выбора | да |
| `com.unity.pipeline` 0.8.0-exp.1 | назначение выяснено: HTTP-API редактора для CLI и агентов (сборка, тесты, play/pause, перекомпиляция). Используется агентом. Не удалять | выяснено |
| Прочие пакеты (прямые) | ai.navigation 2.0.10, ugui 2.0.0, timeline 1.8.10, visualscripting 1.9.7, ai.assistant 2.20.0-pre.1, ai.inference 2.6.1, collab-proxy 2.11.3, ide.rider 3.0.39, ide.visualstudio 2.0.26 | да |
| Физика | Fixed Timestep 0.02 с, Maximum Allowed Timestep 0.3333 с, gravity −9.81, solver 6/1, autoSyncTransforms off, simulationMode FixedUpdate | да. Переход на 1/120 и 0.1 с — в P03 (§9.1), в P00 не менялось |
| Слои | до P00: только встроенные. После P00: `Interact` = 8 | да |
| Сцены | `Assets/Scenes/SampleScene.unity` (Main Camera, Directional Light, Global Volume), единственная в Build Settings. Не изменялась | да |
| Скрипты | до P00 своих скриптов нет. После P00 — только пакет модели (`UtilityPlane.Runtime`, `UtilityPlane.Editor`) | да |
| Enter Play Mode Options | выключены (каждый вход в Play Mode — перезагрузка домена) | — |
| Run In Background | **false**. Для проверок в нескольких процессах (P01) нужно будет включить | — |
| Контроль версий | **git нет** (`F:\ClaudeGames\snow` не репозиторий). Изменения пользователя отделить от изменений агента можно только по датам файлов | риск, см. §10 |
| Изменения пользователя до P00 | по датам: проект создан 29.09 12:21; `ProjectSettings.asset` изменён 12:38, `URPProjectSettings`, `ShaderGraphSettings` — 12:25. Содержимое сверить не с чем (нет VCS), ничего из этого не трогал | — |
| Консоль до P00 | 0 ошибок; 3 предупреждения MCP-моста о подписи `claude.exe` (к проекту не относятся); лог `NoSubscription` генераторов AI | — |

## 3. Что изменено в P00

| Путь | Изменение | Как сделано |
|---|---|---|
| `Assets/UtilityPlane/**` | скопирован пакет модели (35 файлов, 97.9 МБ) из `F:\ClaudeGames\snow_archive\plane\UtilityPlane_Unity_v4\Assets\UtilityPlane`. В пакете не было `.meta` — GUID создал Unity при импорте | `robocopy`, затем `AssetDatabase.Refresh` |
| `Assets/UtilityPlane/Materials/*.mat` (14), `Prefabs/UtilityPlane.prefab`, `Prefabs/Cargo_Case1..6.prefab`, настройки импорта текстур и FBX | созданы меню установки | `Tools/Utility Plane/Build Materials + Prefabs` |
| `ProjectSettings/TagManager.asset` | слой 8 `Interact` | меню установки |
| `ProjectSettings/DynamicsManager.asset` | столкновения `Interact` со всеми 32 слоями выключены. Проверено на диске: строка 8 матрицы = 0, бит 8 снят во всех строках | `Physics.IgnoreLayerCollision` + `AssetDatabase.SaveAssets` |
| `docs/dev/ProjectAudit.md`, `docs/dev/ImplementationStatus.md`, `docs/dev/Audit/*.cs`, `docs/dev/Audit/img/*.png` | документы, скрипты проверки, снимки | созданы агентом |
| `Assets/Docs/**` | создан по ошибке командой `capture_game_view --save_path` (3 PNG) и удалён `delete_asset`. Итог нулевой: папки нет | CLI pipeline |

Не менялись: `SampleScene` (не dirty), шаг физики, пакеты, ввод, рендер, настройки плеера.

### 3.1 Изменения после P00 (P01–P02)

| Что | Было | Стало | Основание |
|---|---|---|---|
| Пакеты | — | `com.unity.netcode.gameobjects` 2.13.3, `com.unity.transport` 2.6.0 (зависимость), `com.unity.multiplayer.playmode` 1.6.3 | разрешение пользователя 29.09 |
| Fixed Timestep | 0.02 с | 1/120 с | §9.1 |
| Maximum Allowed Timestep | 0.333 с | 0.1 с | §9.1 |
| Build Settings | SampleScene | P01_NetBench (первая), SampleScene, P02_PhysicsBench | стенды |
| `Assets/DefaultNetworkPrefabs.asset` | — | создан NGO при установке, содержит Player и 6 ящиков | NGO |
| Run In Background | false | true во время работы (`SnowNetworkBootstrap`), настройка плеера не менялась | несколько процессов на одной машине |

## 4. Пакеты — вопрос пользователю (§1.1)

Задан одним сообщением, ответа нет. Версии — последние, совместимые с 6000.0 по полю `unity` в реестре `packages.unity.com`:

| Пакет | Версия-кандидат | Совместимость | Зачем |
|---|---|---|---|
| `com.unity.netcode.gameobjects` | **2.13.3** (последняя 2.x) | unity ≥ 6000.0. Версия 3.0.0 требует 6000.7 — **не подходит** | хост + до 3 клиентов (ENGINEERING, рекомендация) |
| `com.unity.transport` | подтянется зависимостью (NGO 2.13.3 требует ≥ 2.6.0; последняя 2.7.4) | unity ≥ 2022.3 | транспорт, симулятор задержки и потерь для P02 |
| `com.unity.multiplayer.playmode` | **1.6.3** | unity ≥ 6000.0.48f1. Версия 2.x требует 6000.3 — **не подходит** | до 4 игроков в одном редакторе (по желанию) |

Итоговые версии записать сюда после установки (Package Manager решает зависимость Transport сам).

## 5. Установка модели

| Шаг | Результат | Доказательство |
|---|---|---|
| Копирование в `Assets/UtilityPlane` | 35 файлов | robocopy: Files 35 copied, 0 failed |
| Импорт | компиляция без ошибок (`recompile_status: completed, failed false`). Постпроцессор применил настройки: `_Normal` → NormalMap, маски линейные, FBX без камер, света и анимации, Read/Write on, scale 1 | `RunCommand`, лог импортёров |
| Меню установки | `[UtilityPlane] Added layer "Interact" at index 8`; `UtilityPlane.prefab: 37 convex colliders, 43 interaction volumes, 12 lights`; `Done (URP)` | консоль, записи #22–#24 |
| Состав префаба | 284 transform; 37 выпуклых MeshCollider (30 в `Colliders` + 4 двери + 2 стойки шасси + `COL_Propeller` перенесён в корень); 43 зоны `IV_*`, все на `Interact` и триггеры; 12 `Light` (выключены); `UtilityPlaneRig`; Rigidbody нет; 74 `Socket_*`; 13 `SW_*`, 4 `Pull_*`, 15 `Needle_*`, 12 `Lamp_*`, 10 `Lens_*`; дублей имён нет | `RunCommand` по префабу |
| Материалы | все 14 `M_*` на шейдере `Universal Render Pipeline/Lit`, переназначены на FBX. Единственный стандартный `Lit` — на пустом меше `ATT_Roll` (N1) | `RunCommand` |
| Ящики | `Cargo_Case1..6.prefab` с выпуклым MeshCollider; габариты совпадают с `cargo_layout.json` до мм | `RunCommand` |
| Столкновения `Interact` | выключены со всеми слоями, сохранено в `DynamicsManager.asset` | §3 |

README пакета говорит «30 выпуклых коллайдеров». Фактически 37: README считает только группу `Colliders`. Ошибкой это не является.

## 6. Проверка знаков и геометрии (UNITY_CONTRACT §11)

Методика. Play Mode, экземпляр `Prefabs/UtilityPlane.prefab` в начале координат. Поля `UtilityPlaneRig` менялись, `Update` Rig вызывался напрямую через `SendMessage`, поэтому результат не зависит от фокуса окна. Затем измерялось смещение реальных вершин меша в мировых координатах. Скрипты: `docs/dev/Audit/P00_SignCheck.cs`, `P00_Openings.cs`, `P00_OpeningMap.cs`. Стрелки, лампы и огни дополнительно сняты камерами, снимки лежат в `docs/dev/Audit/img/`.

### 6.1 Подвижные части (§3, §4 контракта) — 57 PASS, 0 FAIL

Всего первый прогон дал 68 PASS: 57 здесь, 3 проверки табло и 4 проверки огней (§6.3), 4 точки выхода (§6.4).

| Канал | Ожидание | Измерено (м) | Итог |
|---|---|---|---|
| `pilotDoorL/R = 1` | наружу | центроид ΔX −0.550 / +0.549 | PASS |
| `cargoDoorL/R = 1` (петля сзади) | наружу | ΔX −0.805 / +0.804, ΔZ −0.88 | PASS |
| `fuelCapL/R = 1` | вверх | ΔY +0.028 | PASS |
| `rudderDeg +25` | задняя кромка вправо | ΔX +0.254 | PASS |
| `elevatorDeg +15` | задняя кромка вверх | ΔY +0.094 (оба) | PASS |
| `aileronDeg +20` (крен вправо) | правый вверх, левый вниз | ΔY +0.240 / −0.236 | PASS |
| `flapDeg 35` | задняя кромка вниз | ΔY −0.320 (оба) | PASS |
| `gearCompressionL/R = 1` | колесо вверх | ΔY +0.175, ΔX ∓0.097 (наружу) | PASS, см. N4 |
| `tailGearCompression = 1` | колесо вверх | ΔY +0.079, ΔZ −0.108 | PASS |
| `tailWheelSteerDeg +30` | перёд колеса вправо | forward.x = 0.500 | PASS |
| `propRpm > 0` | по часовой из кабины | верх лопасти ΔX +0.230 | PASS |
| `mainWheelSpeed > 0` | качение вперёд | верх колеса ΔZ +0.084 | PASS |
| `yokePitch +1` | штурвал от себя | Yoke_P/Yoke_C ΔZ +0.080 | PASS |
| `yokeRoll +1` | правая ручка вниз, левая вверх | ΔY −0.139 / +0.097 | PASS |
| `pedals +1` | правая вперёд, левая назад | Socket_Foot_R ΔZ +0.059, _L −0.055; PedalC повторяют | PASS |
| `powerLever / propLever / conditionLever +1` | вперёд | ΔZ +0.058 / +0.063 / +0.063 | PASS |
| `flapLever 3` | направление в контракте не задано | рукоятка ΔY −0.021, ΔZ −0.064 (вниз и назад) | инфо |
| `trimWheelDeg +20` (на кабрирование) | верх колеса назад | ΔZ −0.027 | PASS |
| 13 × `SW_*` ON | рычажок вверх | ΔY +0.0155 у всех | PASS |
| `annunciatorTestPressed` | кнопка в панель | ΔZ +0.003 | PASS |
| 4 × `Pull_*` = 1 | к пилоту | ΔZ −0.060 у всех | PASS |
| `FuelSel_L/R` покой (ON) | ручка вниз | центроид ниже оси на 0.054 | PASS |
| `fuelSelL = false` (OFF) | поворот | ΔY +0.027, ΔZ −0.046 | PASS |
| `visorP/C = 1` | опущен | ΔY −0.049 / −0.048 | PASS |
| `beltP/C` | видна одна из пары On/Off | да | PASS |

### 6.2 Стрелки (§5 контракта) — визуально, 0 / середина / максимум

Снимки: `docs/dev/Audit/img/{min,mid,max}_P00Cam_{Flight,Engine,Right}.png`.

| Прибор | Минимум | Середина | Максимум | Итог |
|---|---|---|---|---|
| ASI | 0 → вверх, начало шкалы | 100 → у «100» | 200 → у «200» | PASS |
| Высотомер 100 / 1000 | 0 → «0» (на снимке min засвечен бликом тестовой лампы) | 5500 → 5 / между 5 и 6 | 9900 → 9 / почти 10 | PASS |
| VSI | −2000 → DN 20 | 0 → «0» | +2000 → UP 20 | PASS |
| TRQ, PROP | 0 | 12 (×100) | 24 | PASS |
| ITT | 100 (засвечен бликом) | 6 (×100) | 11 | PASS по mid/max |
| NG | 0 (засвечен бликом) | ≈55 | 110 | PASS по mid/max |
| Масло P / T (одна шкала) | 0 / 0 | 100 psi → «10», 75 °C → середина | 200 → «20», 150 → «15» | PASS |
| FF | 0 | 2 | 4 | PASS |
| FUEL L/R | E | ½ | F | PASS |
| AMP | −60 | 0 | +60 | PASS |
| OAT | −40 | ≈5 | 50 | PASS |
| Часы | 0:00 | 3:15 | 9:45 | PASS |
| Авиагоризонт | уровень | — | крен 30° вправо (горизонт поднят справа), тангаж +10° (силуэт у линии 10) | PASS |
| Курсовой | N | — | 90° → E | PASS |
| Указатель поворота | уровень | — | 3 °/с вправо → силуэт на метке R | PASS |
| Шарик скольжения | — | — | `slip 0.5` на снимке не различить | NOT_RUN (разрешение) |

### 6.3 Табло и огни (§6, §7 контракта)

| Проверка | Результат | Итог |
|---|---|---|
| питание + тест | 12/12 ламп излучают (`_EmissionColor` через MPB) | PASS |
| без питания + тест | 0/12 | PASS |
| питание, только `lamps[3]` | горит только Lamp_03 (FUEL LOW R) | PASS |
| `navLights 1` | `Light_Nav_*` включены (2.0), линза светится; при 0 — темно | PASS |
| `landingLight 0.5` | интенсивность 6.0 из 12 | PASS |
| Видимость «горит / не горит» | яркость ячейки выше без питания всего в 1.17–1.32 раза (`state_` против `nobus_P00Cam_Annun.png`) | **замечание N2** |
| Внешние огни на снимках | красная АНО на левой законцовке видна (`state_P00Cam_ExtFrontLeft.png`). Тайминг вспышек стробов и маяка глазами не проверялся | частично |

### 6.4 Коллайдеры и масштаб (§10, §11.4, §11.5)

| Проверка | Результат | Итог |
|---|---|---|
| Пол / порог двери пилота / порог грузовой двери | 1.070 / 1.140 / 1.100 м — как в контракте | PASS |
| Проём двери пилота по плитам стен | Z 1.98…3.12 (1.14 м) × Y 1.14…2.52 (1.38 м) — как в контракте | PASS |
| Проём грузовой двери | Z −0.45…+0.85 (1.30 м) × Y 1.10…2.44 (1.34 м); в плоскости стены чужих коллайдеров нет | PASS |
| Самый широкий ящик (Case2) через грузовой проём | box-cast снаружи до оси свободен, оба борта | PASS |
| Проём двери пилота — занятость | свободно примерно Z 2.03…2.93 м на всю высоту. Передние ≈ 0.2 м занимает `COL_Panel` (до X ±0.66), верхний передний угол — выпуклая оболочка `COL_Windshield` | PASS с замечанием N8 |
| Салон отсека X ±0.60, Y 1.10…2.40, Z −1.25…+1.90 | свободен | PASS |
| Капсула 1.80 × r 0.34 у `Socket_Exit_L/R`, `Socket_Exit_Cargo_L/R` | свободно | PASS |
| Капсула в отсеке | стоя (1.80) — упирается в потолок (низ крыши Y 2.60, 1.53 м над полом); присев (0.90) — свободно | замечание N6 |
| Масштаб | капсула 1.8 м у двери пилота, порог ≈ 0.63 её высоты (`state_P00Cam_Side.png`) | PASS |
| Коллайдеры глазами | `colliders_P00Cam_ColRearLeft.png`, `colliders_P00Cam_ColLeft.png`: плиты салона, проёмы пустые, стены внутрь не торчат | PASS |

Первая версия пробы проёмов дала 4 FAIL: проба зацепила коллайдер собственной открытой двери у петли. Это дефект пробы, а не модели. Уточнённая проба — в `P00_Openings.cs`.

## 7. Матрица применимости SYS-02

«Есть» — объект и зона в модели и префабе есть, поле Rig существует. Система — раздел промпта, где задано поведение.

| Функция | Объекты модели | Зона `IV_*` (kind) | Поле `UtilityPlaneRig` | Система | Модель |
|---|---|---|---|---|---|
| Питание шины | SW_Battery | SW_Battery (toggle) | `switchesOn`, `busPowered` | §9.5 | есть |
| Авионика, GPS | SW_Avionics, Display_GPS | SW_Avionics (toggle) | `switchesOn`; экран — отдельный квад с RenderTexture (§10.3), в Rig нет | §9.5, §10.3 | есть; см. N3 |
| Зажигание | SW_Ignition, Lamp_07 | SW_Ignition (toggle) | `switchesOn`, `lamps[7]` | §9.4 | есть |
| Стартер (держать) | SW_Starter, Lamp_06 | SW_Starter (momentary) | `switchesOn` пока нажат, `lamps[6]` | §9.4 | есть |
| Подкачивающий насос | SW_FuelPump | SW_FuelPump (toggle) | `switchesOn` | §9.4, §9.6 | есть |
| Краны баков | FuelSel_L/R | FuelSel_L/R (selector) | `fuelSelL/R` | §9.6 | есть |
| Перекрывной кран | Pull_FuelShutoff | Pull_FuelShutoff (pull) | `pullFuelShutoff` | §9.6 | есть |
| Останов / малый газ | Lever_Condition | Lever_Condition (lever) | `conditionLever` | §9.4 | есть |
| Шаг винта | Lever_Prop | Lever_Prop (lever) | `propLever` | §9.4 | есть |
| Газ (РУД) | Lever_Power, Socket_Hand_Throttle | Lever_Power (lever) | `powerLever` | INPUT-01 | есть |
| АНО | SW_NavLights, Lens_Nav_L/R/Tail, Light_Nav_* | SW_NavLights | `navLights` | §10.2 | есть |
| Стробы | SW_Strobe, Lens_Strobe_L/R, Light_Strobe_L/R | SW_Strobe | `strobeLights` | §10.2 | есть |
| Маяк | SW_Beacon, Lens_Beacon_Top/Bottom, Light_Beacon_* | SW_Beacon | `beaconLights` | §10.2 | есть |
| Посадочная фара | SW_Landing, Lens_Landing, Light_Landing | SW_Landing | `landingLight` | §10.2 | есть |
| Плафоны кабины | SW_CabinLight, Lens_Dome, Light_Light_Dome_L/R | SW_CabinLight | `cabinLight` | §10.2 | есть; имена см. N9 |
| Плафон отсека | SW_CargoLight, Lens_CargoLight, Light_Light_Cargo | SW_CargoLight | `cargoLight` | §10.2 | есть |
| Подсветка шкал | Rheo_Panel, подматериал M_Gauges | Rheo_Panel (rotary) | `panelLight` | §10.1, §10.2 | есть |
| Заливающий свет | Rheo_Flood, Light_Light_Flood | Rheo_Flood (rotary) | `floodLight` | §10.2 | есть |
| Табло и тест | Lamp_00…11, Btn_AnnunTest | Btn_AnnunTest (momentary) | `lamps[]`, `busPowered`, `annunciatorTestPressed` | §10.1 | есть; см. N2 |
| Грузовые двери из кабины | SW_CargoDoor_L/R, CargoDoor_L/R | SW_CargoDoor_L/R (toggle) | `switchesOn`, `cargoDoorL/R` | §8.2 | есть |
| Грузовые двери, ручки | Socket_Handle_CargoDoor_L/R_In/Out | 4 × door | `cargoDoorL/R` | §8.2 | есть |
| Двери кабины, ручки | PilotDoor_L/R, Socket_Handle_PilotDoor_L/R_In/Out | 4 × door | `pilotDoorL/R` | §8.2 | есть |
| Двери кабины, кнопки в кабине | — | TEMPORARY: `Socket_Handle_PilotDoor_*_In` | `pilotDoorL/R` | NEW-04 | **нет** (M1) |
| Центральный замок, ключ | — | TEMPORARY: `Socket_Handle_PilotDoor_*_Out` | — (в Rig поля нет) | §8.3 | **нет** (M2) |
| Лючки заправки | FuelCap_L/R, Socket_FuelCap_L/R | 2 × cap | `fuelCapL/R` | §9.6, CONTAINER-01 | есть; высота — M5 |
| Стояночный тормоз | Pull_ParkBrake | Pull_ParkBrake (pull) | `pullParkBrake` | §8.4 | есть |
| Ремни | Belt_P/C_On/Off, Socket_Belt_P/C, Lamp_11 | Socket_Belt_P/C (toggle) | `beltP/C`, `lamps[11]` | §8.4, §10.1 | есть |
| Обдув стекла | Pull_Defrost | Pull_Defrost (pull) | `pullDefrost` | SYS-01 | органа хватает; механизма запотевания в модели нет, делать в P06 (параметр стекла `M_Glass`) |
| Обогрев кабины | Pull_CabinHeat | Pull_CabinHeat (pull) | `pullCabinHeat` | SYS-01 | есть; то же про запотевание |
| Закрылки | Lever_Flap, Flap_L/R | Lever_Flap (lever) | `flapLever`, `flapDeg` | §8.4, §9.3 | есть |
| Триммер РВ | TrimWheel | TrimWheel (wheel) | `trimWheelDeg` | §8.4 | есть (отдельного триммера на руле и указателя положения нет — по §8.4 не требуются) |
| Козырьки | Visor_P/C | 2 × hinge | `visorP/C` | только визуально | есть |
| Кресла | Socket_Seat_P/C, Socket_PilotEye/CopilotEye | Seat_P/C (seat) | — | SEAT-01/02 | есть |
| Штурвал, педали | Yoke_P(_Roll), PedalP_L/R; Yoke_C, PedalC_* повторяют | — (клавиатура) | `yokePitch`, `yokeRoll`, `pedals` | INPUT-01 | есть; вторые — M6 |
| Приборы | 15 Needle_*, Clock_*, ATT_*, HDG_Card, TC_* | — | поля показаний | §10.3 | есть; кремальеры и кнопки — M3 |
| Шасси, винт, рули | Gear_*, Wheel_*, TailGear, TailWheel_Fork, Propeller, Rudder, Elevator_*, Aileron_*, Flap_* | — | соответствующие поля | §9 | есть; ход — N4 |
| Звук | Socket_Audio_Engine/Cabin/Gear_L/Gear_R/Tail | — | — | §8.4, §10.1 | точки есть, звуков нет |
| Дворники, звуковой сигнал, аварийка | — | — | — | — | **не применимо** (SYS-02) |
| Форточки | — | — | — | — | **OPEN** (G16), не делать |

## 8. Пробелы модели

### 8.1 Сверка с §13

| ID | §13 | Факт в модели | Статус |
|---|---|---|---|
| M1 | нет `SW_CabinDoor_L/R` | подтверждено: таких объектов нет. Зоны `IV_Socket_Handle_PilotDoor_L/R_In` есть | TEMPORARY из §13 применимо |
| M2 | нет меша ключа и скважины | подтверждено: объектов Key/Keyhole нет. Зоны `..._Out` есть. В `COMPONENTS.md` (G20) предложена скважина у `SW_Battery` — противоречит §8.3. Действует §8.3: скважина — наружные ручки | TEMPORARY из §13 применимо |
| M3 | нет кремальеры высотомера, кнопки курсового, компаса | подтверждено: объектов нет. На снимках ручки видны как часть статичной кабины | TEMPORARY из §13 применимо |
| M4 | нет рук и тела | подтверждено | TEMPORARY |
| M5 | горловины на 2.87 м | `Socket_FuelCap_L/R` на Y 2.848, крышки на крыле | **OPEN** |
| M6 | вторые штурвал и педали | есть, механически повторяют левые (проверено в 6.1) | **OPEN** |
| M7 | нет канистры | подтверждено | отдельный ассет |

### 8.2 Новые находки P00

| ID | Находка | Влияние | Предложение |
|---|---|---|---|
| N1 | `ATT_Roll` содержит пустой меш (0 вершин) с MeshRenderer и стандартным `Lit`. Отсюда предупреждение импорта «Can't calculate tangents… ATT_Roll» | нет, объект невидим | игнорировать. В v5 модели убрать меш |
| N2 | Надписи ламп табло напечатаны в базовом цвете `T_Gauges_BC`. Выключенная лампа почти так же ярка, как горящая: отношение 1.17–1.32 при тестовом освещении | лампы плохо читаются, риск для NEW-06 и AT-L1 | P06: в слое представления затемнять `_BaseColor` выключенной лампы через MPB, либо запросить у автора модели атлас «выключено» |
| N3 | Экран GPS/COM, частоты радио и код ответчика «1200 ALT» напечатаны в статичной текстуре кабины и видны без питания | противоречит «горит только с авионикой и питанием» (§10.3) | P06: квад `Display_GPS` перекрывает экран GPS. Частоты и ответчик — запрос автору модели (тёмный вариант) или оставить декором, решение пользователя |
| N4 | `gearCompression = 1` (12°) поднимает колесо на **0.176 м**, плюс 0.097 м наружу. В контракте — «≈ 12 см». 12 см вертикального хода = compression ≈ 0.69. Хвост: 8° → 0.079 м вверх и 0.108 м назад | колесо на экране разойдётся с лучом подвески | P03/P05: переводить ход подвески в угол по измеренной кривой (0.25 → 0.042, 0.5 → 0.086, 0.69 → 0.120, 1.0 → 0.176 м), не линейно |
| N5 | В `cargo_layout.json` поставленные друг на друга ящики пересекаются на 4.7 см по вертикали: Case1×Case3, Case2×Case4, Case4×Case6 | спавн «как есть» даст расталкивание и импульсы | P02/P04: при спавне ставить ящик на фактический верх нижнего, либо спавнить по очереди с оседанием |
| N6 | Высота отсека над полом 1.53 м (низ `COL_Roof` на Y 2.60), капсула игрока 1.80 м | в отсеке можно только сидя на корточках; касается §7.6 и AT-HL3 | учесть в P01/P05: в отсеке персонаж принудительно в приседе, либо решение пользователя |
| N7 | Порог грузового проёма 1.10 м, порога двери пилота 1.14 м. Коллайдеров ниже 1.0 м (подножек) нет. Подножка под грузовой дверью есть только в меше `Body` (Y 0.83–1.00) | по §7.2 достижимый уступ ≈ 0.85 м (прыжок 0.5 + присед 0.35): пешком в отсек не забраться. Посадка в кресло мгновенная и не страдает | P05: добавить в префаб-вариант коллайдер подножки по её геометрии (коллайдер, не меш — геометрию модели не трогаем). Нужно согласие пользователя |
| N8 | Проём двери пилота свободен примерно на 0.9 м из 1.14 м: спереди его занимают `COL_Panel` и оболочка `COL_Windshield` | посадка мгновенная, не блокер. Для груза через дверь пилота — узко | принять как есть |
| N9 | Источники света плафонов называются `Light_Light_Dome_L/R`, `Light_Light_Cargo`, `Light_Light_Flood` (двойной префикс), в таблице контракта — `Light_Dome_L/R` и т. п. | код, который ищет свет по имени, промахнётся | брать имена из префаба, не из контракта |

## 9. Предупреждения компиляции и импорта

| Когда | Сообщение | Относится к проекту |
|---|---|---|
| до P00 | 3 × `Failed to collect Windows signature for …claude.exe` (Unity.AI.MCP) | нет, MCP-мост |
| импорт и меню установки | 3 × `Can't calculate tangents, because mesh 'ATT_Roll' doesn't contain normals.` | да, N1, безвредно |
| вся сессия | ошибок компиляции 0; `compilationFailed = false`; ошибок консоли 0 | — |

## 10. Блокеры и открытые вопросы

| ID | Что | Блокирует |
|---|---|---|
| B1 | Ответ о пакетах (§4) | сетевую часть P01 |
| R1 | Нет системы контроля версий. Откатить изменения агента или отделить их от изменений пользователя можно только вручную | ничего формально. Рекомендация: git + LFS для `.fbx`/`.png`, решение пользователя |
| R2 | `Run In Background = false` | многопроцессные проверки P01. Включить в P01 и записать сюда |
| N6, N7 | Высота отсека и подножка | не P01; решить до P05 |
| N2, N3 | Читаемость ламп и напечатанные экраны | не P01; решить до P06 |

## 11. Не запускалось

| Что | Причина |
|---|---|
| EditMode/PlayMode-тесты | тестов в проекте нет (0) |
| Шарик указателя скольжения | на снимке 1280×960 не различается |
| Тайминг вспышек стробов и маяка | проверено только состояние, не частота |
| Physics Debugger в Scene view | заменён снимками прокси-коллайдеров и числовыми пробами (§6.4) |
| Сборка плеера, второй процесс | не нужно в P00 |

## 12. Как повторить

1. Открыть проект в 6000.0.67f1 и войти в Play Mode.
2. Выполнить через MCP `RunCommand` содержимое `docs/dev/Audit/P00_SignCheck.cs`, затем `P00_Openings.cs`, затем `P00_OpeningMap.cs`. Ожидаемо: `TOTAL: PASS 68, FAIL 4`. Эти 4 FAIL — первая, неверная версия пробы проёмов; уточнённая проба в `P00_Openings.cs` даёт PASS для грузовых проёмов и карту занятости для дверей пилота.
3. Снимки стрелок: камеры смотрят на панель с Z 2.63–2.78, лампа подсветки стоит на (0, 2.5, 2.4), чтобы блик на стекле приборов не попадал на шкалы. Наборы значений — в §6.2. Снимки делались командой `unity command capture_game_view --source camera` с сохранением через base64 в `docs/dev/Audit/img/`. `--save_path` не использовать: он пишет в `Assets/`.

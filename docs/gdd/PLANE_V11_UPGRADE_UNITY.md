# Обновление модели самолёта до v11 (свет) в проекте Snow — задание для AI-агента

Версия 1.1 от 15.10.2026 (редакция 2 пакета: видимые плафоны заливки доски). Действует вместе с `PLANE_PROMPT_UNITY.md` (v2.1). Правила работы, статусы и формат отчёта — оттуда (§0). Если пункт здесь расходится с промптом, верно то, что написано здесь.

Проект: `F:\ClaudeGames\snow\Snow`, Unity 6000.0.67f1, URP.
Новый пакет модели: `F:\ClaudeGames\snow\assets\plane\UtilityPlane_Unity_v11\Assets\UtilityPlane`.

## 0. Что изменилось и зачем

Пользователь (15.10): «Надо заняться освещением. Внутри и снаружи. Сейчас это просто как будто лампочки».

v11 = v10 + свет. Геометрия, текстуры, имена частей, иерархия, 79 зон, 125 сокетов, 60 коллайдеров, LOD — как в v10. Поля Rig, которыми игра включает свет (`navLights`, `strobeLights`, `beaconLights`, `landingLight`, `cabinLight`, `cargoLight`, `floodLight`, `panelLight`, `busPowered`, `avionicsPower`), и их смысл (фактическая яркость 0…1) — прежние: `PlanePresentation` менять не нужно. Изменилось:

Редакция 2 (15.10, по замечанию пользователя «нет места, откуда светит свет»): у заливки доски появились видимые лампы — в `UtilityPlane.fbx` 4 новых объекта под `Cockpit`: `PanelFlood_L`, `PanelFlood_R` (корпуса на кронштейнах от края козырька, M_Cockpit, только LOD0 кабины) и `Lens_Flood_L`, `Lens_Flood_R` (линзы, M_Light_Cabin, светятся с `floodLight`). Остальные 393 объекта, LOD и теневые копии — как в v10. Если пакет v11 уже ставили до редакции 2: заново скопировать папку пакета и ещё раз выполнить Tools → Utility Plane → Build Materials + Prefabs.
- все огни — прожекторы с формой (cookie) по диаграмме излучения: секторы АНО по FAR (красный — вперёд и влево, зелёный — вперёд и вправо, белый — назад), стробы наружу от законцовок, вращающиеся веера маяков (45 об/мин), фара с ядром и юбкой (на стоянке освещает снег перед самолётом), плафоны и лампы отсека вниз, две красные заливки доски из-под козырька; добавлены источники `Light_Flood_2`, `Light_Fill_Cockpit`, `Light_Fill_Bay` (отражённый свет кабины и отсека) — всего 16;
- физика ламп: нити накаливания разогреваются и остывают (60–250 мс) и краснеют при притушивании и просадке шины; ксеноновые стробы — двойная вспышка раз в 1.05 с, реже при просадке, ниже 25 % нет; маяки замедляются вместе с шиной; лампа отсека — люминесцентная трубка, мигает при включении и не загорается при низком напряжении;
- тени: фара, плафоны, лампы отсека и заливка (`shadowQuality`, по умолчанию Medium — 7 источников с тенями): свет кабины выходит наружу только через окна, фара рисует тени тех, кто перед самолётом;
- в воздухе (только URP): свечение огней в тумане и снеге (шейдер `UtilityPlane/Airlight`) и лучи фары и маяков (`UtilityPlane/Beam`); плотность — поле `atmosphere` или туман сцены (`atmosphereFromSceneFog`);
- хоррор: `lightFlicker` и `FlickerBurst()` — мигание всех огней (подключение — решение пользователя, **OPEN**).

Полный список — `Assets/UtilityPlane/UNITY_CONTRACT.md`: свет — разделы 7, 7.3–7.7, отличия — раздел 18, проверки — раздел 11, пункты 47–57. Рендеры до/после — `F:\ClaudeGames\snow\assets\plane\UtilityPlane_Unity_v11\previews\`.

**Какая версия стоит в проекте.** На 15.10.2026 в проекте стоит v4 (`Assets/UtilityPlane/README.md`: «v4»); пакеты v7–v10 и зимнего комплекта доставлены, но не установлены.
- **v4 … v9:** сначала выполнить `PLANE_V10_UPGRADE_UNITY.md` (а с v4–v8 — и `PLANE_V9_UPGRADE_UNITY.md`, порядок — по разделу 0 задания v10), но **вместо пакетов v9 и v10 ставить пакет v11**: шаги U2 и U3 тех заданий — с пакетом v11 и этим заданием (U2, U3 ниже), потом U4 всех заданий, U5 — один раз. Ожидаемые строки меню — из U3.1 ниже (в них `16 lights` и `v11`, остальное — как в задании v10).
- **v10:** только это задание.
- **Зимний комплект 1.2** подходит к v11 без изменений (свет комплекта не трогает). Ставить после модели; если он уже стоял — шаг U3.4.

## 1. Что затронуто в проекте

Перед правкой перечитать файлы: они могли измениться после 15.10.

| Где | Что зависит от v11 |
|---|---|
| `Assets/UtilityPlane/**` | пакет прежней версии с GUID и собранными `Materials/`, `Prefabs/`; новые файлы: `Scripts/UtilityPlaneRig.Lights.cs`, `Shaders/UtilityPlaneAirlight.shader`, `Shaders/UtilityPlaneBeam.shader`, `Textures/Cookies/T_Cookie_*.png` (14), `Data/lights.json`; меню создаст `Materials/M_FX_Airlight.mat`, `M_FX_Beam.mat` |
| `Assets/Snow/Runtime/Aircraft/PlanePresentation.cs` | поля света те же — правка не нужна; сюда (или в инструмент сборки варианта) ставится `atmosphereFromSceneFog` (U4.3) |
| код, который перебирает рендереры самолёта (`HoverOutline`, скрытие модели, снег и иней, подсветка) | во время игры под источниками появляются рендереры `Glow_<ключ>` и `Beam_<ключ>` (их делает Rig) |
| код, который ищет источники `Light_*` по имени или считает их | 16 вместо 13: новые `Light_Flood_2`, `Light_Fill_Cockpit`, `Light_Fill_Bay` |
| `Assets/Settings/PC_RPAsset.asset`, `PC_Renderer.asset` | нужны: Depth Texture, Light Cookies, тени дополнительных источников (атлас 2048, тиры 256 / 512 / 1024), Soft Shadows; у рендерера Depth Texture Mode After Opaques. На 15.10 всё так — только проверить (U1.4) |
| Graphics Settings | «Lights Use Linear Intensity» и «Lights Use Color Temperature» — на 15.10 включены; проверить |
| профили постобработки (`SampleSceneProfile`, `DefaultVolumeProfile`) | Bloom смягчает блики огней; рекомендация в Q5 — **без изменения** профилей без разрешения пользователя |
| `Docs/ImplementationStatus.md` | статус модели, решения по U4 |

## 2. Шаги

### U1. Подготовка
1. Выйти из Play Mode. Перед компиляцией проверить состояние редактора (R-4).
2. Определить версию модели в проекте по заголовку `Assets/UtilityPlane/README.md` («v10» … «v4»). Признаки v10: `Generated/UtilityPlane_RuntimeMeshes.asset`, объекты `*_Shadow` в префабе. Если стоит ниже v10 — порядок из раздела 0.
3. Git в проекте нет (R-3): резервная копия вне `Assets` в `F:\ClaudeGames\snow\backup\plane_<версия>_<дата>\`: `Assets/UtilityPlane`, `Assets/Snow/Plane`, сцены `P03_PlaneBench.unity` и `SkyIsland.unity` с их `.meta`.
4. Проверить и записать (не менять): у `PC_RPAsset` — Depth Texture, Light Cookies (формат и размер атласа), Additional Lights Shadows (Shadow Atlas Resolution, тиры), Soft Shadows; у `PC_Renderer` — Rendering Path (Forward+) и Depth Texture Mode (на 15.10 — After Opaques; нужен After Opaques или Force Prepass); в Graphics Settings — Lights Use Linear Intensity и Lights Use Color Temperature. Если что-то из этого выключено — не включать, спросить пользователя (без Depth Texture свечение видно сквозь стены; без Color Temperature Rig сам складывает температуру в цвет).

### U2. Замена пакета
1. Скопировать содержимое нового пакета поверх `Assets/UtilityPlane` с заменой файлов, без удаления лишнего (например, `robocopy <v11> <проект>\Assets\UtilityPlane /E`, без `/PURGE`). Файлы `.meta` проекта не трогать: GUID FBX, текстур, материалов, префаба и `UtilityPlaneRig.cs` должны сохраниться. Файлы зимнего комплекта пакет не трогает.
2. `AssetDatabase.Refresh`, дождаться импорта и компиляции. Ошибок в консоли быть не должно. Выбрать `Shaders/UtilityPlaneAirlight.shader` и `UtilityPlaneBeam.shader`: в Inspector нет ошибок компиляции (шейдеры v11 в Unity ещё не компилировались — ошибку записать дословно и сообщить, файл не править).
3. Импорт `Textures/Cookies/T_Cookie_*.png`: sRGB, Alpha Source — Input, Wrap — Clamp, без мипов, без сжатия, Max Size 256 (меню выставит это само; если в проекте импорт переопределён — вернуть значения меню).

### U3. Меню установки модели
1. Выполнить `Tools → Utility Plane → Build Materials + Prefabs`. Ожидаемые строки:
   - `[UtilityPlane] Assets/UtilityPlane/Prefabs/UtilityPlane.prefab: 60 convex colliders, 79 interaction volumes, 16 lights (14 cookies, 7 with shadows at the default quality, glow / beam FX: yes), 114 renderers without shadows, LOD groups: Exterior 3, Cockpit 3, 5 shadow proxies, fog meshes copied: 5`;
   - `[UtilityPlane] Done (URP), v11. Prefabs in Assets/UtilityPlane/Prefabs; readable collider / fog meshes: 65 in Assets/UtilityPlane/Generated/UtilityPlane_RuntimeMeshes.asset`;
   - предупреждений `Shader UtilityPlane/... is missing, has errors`, `glow / beam shaders unavailable`, `Depth Texture is off`, `Depth Texture Mode is After Transparents`, `renderers not found`, `parent ... not found` быть не должно.
2. В `Prefabs/UtilityPlane.prefab`: 16 объектов `Light_*` в сокетах; все Spot с cookie `T_Cookie_<ключ>`, кроме `Light_Fill_Cockpit` и `Light_Fill_Bay` (Point, без cookie); у `Light_Landing`, `Light_Dome_L/R`, `Light_Cargo`, `Light_Cargo_2`, `Light_Flood`, `Light_Flood_2` — тени Soft (у остальных нет); у `UtilityPlaneRig`: `glowMaterial` = `M_FX_Airlight`, `beamMaterial` = `M_FX_Beam`, `lightCookies` — 14, `lightsVersion` = 11.
3. В префабе нет объектов `Glow_*` и `Beam_*`: Rig создаёт их только во время игры. Если они есть — префаб сохранили из Play Mode; собрать меню заново.
4. **Зимний комплект, если уже стоял:** выполнить `Tools → Utility Plane → Install Winter Kit` (меню модели собирает префаб с нуля, вложенный `WinterKit` пропадает). Ожидаемая строка — как в задании комплекта.
5. Список ненужных файлов в конце меню — как в v10; v11 новых не добавляет.

### U4. Правки кода
Каждую правку связать с требованиями промпта (§7–§8 производительность и вид, §11 сеть — если задета).

1. **Рендереры самолёта.** Код, который перебирает рендереры самолёта или префаба (`GetComponentsInChildren<Renderer>`, `GetComponentsInChildren<MeshRenderer>`): `HoverOutline`, скрытие модели (вид из кабины, смерть, отладка), снег и иней, подсветка повреждений. Рендереры `Glow_*` и `Beam_*` пропускать: `UtilityPlaneRig.IsEffectRenderer(r)` — не подсвечивать, не перекрашивать, не включать и не выключать самому. Чтобы спрятать самолёт вместе со светом, кроме рендереров выключить `rig.glows` и `rig.beams` (и вернуть прежние значения при показе). Если такого кода нет — записать «нет».
2. **Источники по имени.** Поиск по коду проекта: `"Light_`, `GetComponentsInChildren<Light>`. Если код считает источники самолёта (13) или меняет их `color`, `range`, `spotAngle`, `shadows`, `cookie` — убрать или перевести на поля Rig (`shadowQuality`, `floodRed`, `glowIntensity` …); причину записать. Если ничего нет — записать «нет».
3. **Туман сцены.** В одном месте, где проект настраивает Rig (`PlanePresentation` или инструмент сборки варианта `UtilityPlane_Game.prefab`), включить `rig.atmosphereFromSceneFog = true` (`atmosphereFogScale` — 40 по умолчанию). На SkyIsland это даёт `atmosphere` ≈ 0.14 у земли и меньше выше (`SkyIslandFogByAltitude`); на стенде P03 без тумана Rig берёт `atmosphere` (0.25). Где поставлено — записать.
4. **Мигание для хоррора** (`lightFlicker`, `FlickerBurst`): не подключать. Записать в статус как **OPEN** — когда мигать (сущность рядом, отказ шины) и как рассылать по сети (`FlickerBurst` событием, `flickerSeed` и `flickerClock` — общие).
5. **Документы.** `Docs/ImplementationStatus.md`: модель v11; результаты U1.4 (настройки URP), решения U4.1–U4.4.

### U5. Пересборка
1. `Tools → Snow → Build P03 Plane Bench` — пересобирает вариант `UtilityPlane_Game.prefab` и стенд. GUID варианта сохраняется.
2. `Tools → Snow → Integrate Sky Island Level` — пересобирает уровень; пересохранить сцену, чтобы хэши `NetworkObject` совпали с новым вариантом.
3. Убедиться, что в сценах нет экземпляров с потерянными переопределениями и предупреждений Missing Prefab; у экземпляров самолёта в сценах нет переопределений на `Light_*` (если есть — записать какие).

## 3. Что проверить

Проверяет пользователь или прогон — как договорено в проекте. Результат записать в `Docs/ImplementationStatus.md`; «проверено» — только с журналом или снимком. Ночная сцена для проверок: стенд P03 с выключенным солнцем (или SkyIsland), камера игрока; поля Rig — через Inspector в Play Mode или Dev-команду. С v4–v9 — сначала проверки прежних заданий (раздел 0).

| № | Проверка | Ожидание |
|---|---|---|
| Q1 | установка | как U3.1–U3.3; в Play Mode под `Light_*` появились `Glow_*` (8) и `Beam_*` (3), в консоли нет `glow / beam materials missing` |
| Q2 | огни снаружи | контракт, раздел 11, пункты 48–50: секторы АНО, цветные пятна под законцовками, двойная вспышка стробов, вращающийся маяк (верхний гаснет над кабиной), ореолы и лучи в тумане; снимки с 5, 30 и 150 м |
| Q3 | фара | пункт 51: пятно на снегу от ~4 м перед крылом, столб света в тумане, тень капсулы 1.8 м перед самолётом, плавное включение; при `landingLight` 0.3 — тусклый оранжевый свет |
| Q4 | кабина и отсек | пункты 52–53: плафоны, заливка доски с тенями штурвалов, подсветка шкал и радио от `panelLight`, люминесцентная трубка отсека (мигание при включении, отказ при 0.3); снаружи сквозь обшивку свет кабины не виден, через окна — пятна на снегу |
| Q5 | атмосфера и блики | пункт 55; на SkyIsland с `atmosphereFromSceneFog` огни в дымке видны, не размыты в сплошное пятно; записать, как выглядят блики с текущим Bloom (интенсивность 0.25) и, если пользователь захочет, предложить значения: порог 1, интенсивность 0.3–0.6, рассеяние 0.65 — профили без разрешения не менять |
| Q6 | тени | пункт 56: `shadowQuality` Off / Low / Medium / High на ходу; Frame Debugger — 0 / 1 / 7 / 9 теней дополнительных источников; без полос и «акне» на полу кабины и снегу |
| Q7 | мигание | пункт 54 через Inspector или Dev-команду; только проверка, без подключения к игре |
| Q8 | сеть | хост и клиент: свет включается у обоих по одним выключателям; фазы стробов и маяков у игроков могут различаться — это нормально; ошибок нет |
| Q9 | стоимость | пункт 57: GPU-время `Airlight` + `Beam` (Profiler / Frame Debugger) при виде спереди в тумане с фарой, кадр со вспышкой строба — записать; FPS сцены до/после (резервная копия U1.3 — «до») |
| Q10 | сборка и консоль | Development Build: свечение и лучи есть (шейдеры попали в сборку через `M_FX_*`); ошибок нет; предупреждения импорта записать |

## 4. Не делать

- Не менять геометрию, текстуры, cookie, шейдеры и другие файлы модели в `Assets/UtilityPlane`; яркость подстраивать в префабе (`Light.intensity` — 100 %) и полями Rig, с записью в статус.
- Не менять `PC_RPAsset`, `PC_Renderer`, Graphics Settings и профили постобработки без разрешения пользователя (U1.4, Q5).
- Не подключать мигание к игре без решения пользователя (U4.4).
- Не ставить новые пакеты без разрешения пользователя (§1.1).
- Не править `UNITY_CONTRACT.md` под код: если код и контракт расходятся, сначала проверить на стенде, затем спросить.

## 5. Отчёт

В формате §0 промпта:
- с какой версии обновлено (v10 … v4), что заменено;
- настройки URP из U1.4;
- какие файлы кода изменены и почему (U4.1–U4.3; и правки прежних заданий — если обновлялись с v4–v9);
- результаты Q1–Q10 со статусами, снимки ночью снаружи и из кабины;
- GPU-время эффектов и FPS до/после (Q9);
- что осталось OPEN (мигание для хоррора, Bloom, из прежних заданий — если выполнялись).

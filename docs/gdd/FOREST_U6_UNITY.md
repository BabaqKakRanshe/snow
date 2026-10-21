# Forest, обновление 6: контейнер генератора, предметы, рация пульта и фигура — задание для AI-агента

Версия 1.0 от 21.10.2026. Действует вместе с `PLANE_PROMPT_UNITY.md` (v2.1): правила работы, статусы (CONFIRMED, TEMPORARY, OPEN, DEFERRED, ENGINEERING, NOT_RUN) и формат отчёта — оттуда (§0). Выполняется **после** сборки карты (`FOREST_ASSEMBLY_UNITY.md`, шаги K): обновления 1–5, Unity Terrain, атмосфера уже встроены. Порядок работы, сборщик, интеграция и проверки — те же, что в обновлении 5 (`FOREST_U5_UNITY.md`, шаги H) и в сборке (интеграция с подшагом «Unity Terrain», K5); здесь — только то, что отличается. Контракты описывают модели; если пункт здесь расходится с ними, верно то, что написано здесь: материалы делает сборщик ядра (URP/Lit по `Forest_Materials.json`), у текстур `*_px` мип-карты включены, коллайдеры контейнера — импортные `MeshCollider`, предметам коллайдеры ставит проект по каталогу (F4.1).

Проект: `F:\ClaudeGames\snow\Snow`, Unity 6000.0.67f1, URP 17.0.4, NGO 2.13.3, Multiplayer Play Mode 1.6.3.

| Что | Где |
|---|---|
| Пакет | `F:\ClaudeGames\snow\source\locations\Forest\Forest_Update6.unitypackage` — 106 записей, 4 032 536 байт, md5 `f1271940f4ff800fe7b577fd00dea332`; ставится поверх `Forest_Update5` |
| Опись пакета | `source\locations\Forest\Forest_Update6_src\reports\Forest_Update6_manifest.json` (пути, GUID, md5 файлов, `base_md5` — что должно лежать в проекте до импорта, `changes` — правки данных, место рации); как собран — `Forest_Update6_src\README_SRC.txt` |
| Описание | `Assets/Locations/Forest/README.md`, раздел «Обновление 6» (копия — `source\locations\Forest\README_Forest.md`) |
| Устройство моделей | `docs\gdd\GENCONT_FOREST_CONTRACT.md` (контейнер на карте), `docs\gdd\ITEMS_CONTRACT.md` (83 предмета и рация), `docs\gdd\APPARITION_CONTRACT.md` (фигура: скелет, клипы, посадка); исходный пакет контейнера — `docs\gdd\GENCONT_CONTRACT.md` |
| Как должно выглядеть | листы пакетов: `source\assets\forest_gencont` (`gcont_A…D.jpg`, рендеры — `renders\`), `source\assets\forest_items` (`items_A…F.jpg`), `source\assets\forest_figure` (`fig_A…C.jpg`); «обновление 3 и теперь» — `gcont_C_u3_vs_now.jpg`, `items_E_u3_vs_now.jpg` |
| Журнал работы | `F:\ClaudeGames\snow\docs\dev\ImplementationStatus.md` |

## 0. Что и зачем

Пользователь (CONFIRMED, 21.10): «давай сделаем … просто потом подгрузим эти новые модели да и все» — про модели, которых не хватало после обновления 5: контейнер генератора (в сборке он ещё модель обновления 3, OPEN), 83 предмета в старом виде, рация на пульт для X08, фигура для X02 и X03.

Что приходит:
- **`GenContainer`** — прежние имя, GUID и место; внутри — сдвоенный контейнер пакета `generator_container` (генератор, щит, дневной бак, бочки, свет). Те же 9 частей `IX_…` (их имена знают данные пролога и замки) + стекло `Window_Glass`. 28 209 треугольников (было 19 994).
- **83 предмета `Item_*`** — прежние имена, GUID, начала координат и размеры: коллайдеры каталога и места в данных остаются верными (кроме записки `Note_GenProcedure`: она переложена на крышу новой панели генератора — Приложение Б). 25 487 треугольников (было 12 356).
- **Новая модель `Item_RadioSet`** — рация на западном пульте Leitstand (для X08): запись в каталоге (84-я) и в обжитом корпусе (`Leitstand_RadioSet_1`); окно канала — свой материал `M_RadioDisplay`, «включённое» состояние — `M_RadioDisplayOn`.
- **Новая модель `Apparition`** — фигура для X02 и X03: скелет из 20 костей, клипы `Stand`, `Walk`, `Sit`, `SitLook`. Пугалки X02, X03, X08 — по-прежнему **DEFERRED** (машина, игровые часы, звук); здесь только ассет фигуры и данные для них.
- 19 правок данных (Приложение Б): свет, ореолы, ходы частей и точка обзора контейнера, горловина (бак 100 л), записка о запуске генератора (место — на крыше новой панели, текст — по новому генератору), каталог предметов (`tris` и рация), обжитой корпус (рация), поля для X02/X03/X08, `noColliderPrefixes` + `Apparition`; 8 текстур, 11 материалов.

Поэтому задание короткое: поставить, пересобрать, пересобрать префабы предметов, встроить, настроить импорт фигуры и сделать её префаб, проверить контейнер, предметы и рацию в Play, записать.

Не в задании:
- **DEFERRED:** пугалки X02 (фигура переходит дорогу), X03 (фигура в фургоне) и X08 (рация включается сама) — как в обновлении 3 (нужны машина, игровые часы и звук); звук и эффекты по точкам контрактов (`Audio_…`, `VFX_…`, `Knock_…` — только в json-контрактах).
- **OPEN** (решает пользователь): бак 100 л вместо 60 л (числа пролога — TEMPORARY; вернуть 60 можно одной правкой данных — `Gen_FillNeck` и текст записки `gen_procedure`, табличка на модели останется «100 L»); новые превью карты; память текстур (QJ9); 3 плафона внутри контейнера без теней (`room: Exterior`, как лампа обновления 3) подсвечивают и землю снаружи у стен — если заметно, записать снимок (решение — тени у одного плафона или меньший `r`).

## 1. Что затронуто в проекте

| Где | Что |
|---|---|
| `Assets/Locations/Forest` | пакет: `Models/` — `GenContainer.fbx` и 83 `Item_*.fbx` заменены (те же GUID), новые `Item_RadioSet.fbx`, `Apparition.fbx`; `Textures/` — 8 новых; `Data/` — `Forest_Layout.json`, `Forest_Materials.json`, `Forest_Opening.json`, `Forest_Items.json`, `Forest_Dressing.json`, `Forest_Notes.json`; `README.md`. `Scenes/`, `Prefabs/`, `Materials/` пишет сборщик (J3) |
| `Assets/Snow/Levels/Forest/Items` | 84 префаба предметов (меню F4.1, J3.4), запись `Item_RadioSet` в `Assets/DefaultNetworkPrefabs.asset` |
| `Assets/Snow/Levels/Forest/Apparition` (новая) | `AC_Apparition.controller`, `Apparition.prefab` (J4) |
| `Assets/Snow/Editor`, `Assets/Snow/Runtime` | ничего (кода в пакете нет; имена частей, материалов и данных — прежние) |
| `docs/dev/ImplementationStatus.md` | раздел «Forest U6» (J6) |

## 2. Шаги

### J1. Подготовка
1. **Состояние проекта.** Сборка сделана, если в `docs\dev\ImplementationStatus.md` есть раздел «Forest — сборка (K)», в `Assets/Locations/Forest/Data/Forest_Layout.json` — блок `u5`, а в `Assets/Locations/Forest/Terrain/` — `Forest_TerrainData.asset`. Иначе — остановиться и сообщить пользователю: обновление 6 ставится после сборки.
2. Выйти из Play Mode; перед компиляцией — состояние редактора (R-4).
3. **Точка отката** — коммит в git (`git add -A`, `git commit -m "Before Forest U6"` в `F:\ClaudeGames\snow`, хэш — в отчёт; если коммитить нечего — хэш текущего `HEAD`). Коммит недоступен — копия в `F:\ClaudeGames\snow_archive\backup\forest_u6_<дата>\`: `Assets/Locations/Forest/` целиком, `Assets/Snow/Levels/Forest`, `Assets/Snow/Editor`, `Assets/DefaultNetworkPrefabs.asset` — всё с `.meta`.
4. **Сверка до импорта** (опись → `files`, `base_md5`):
   - у 84 заменяемых моделей (`GenContainer` и 83 `Item_*`) `guid` в `.fbx.meta` проекта совпадает с описью (это GUID обновления 3); у шести файлов данных (`Data/*.json` из таблицы ниже) и `README.md` — тоже (GUID обновления 3); `Models/Item_RadioSet.fbx` и `Models/Apparition.fbx` в проекте ещё нет;
   - md5 файлов, которые пакет перепишет, совпадают с `base_md5` описи — значит, после своих пакетов их никто не менял:

| Файл | md5 до импорта | Чей |
|---|---|---|
| `Data/Forest_Materials.json` | `76a84cfa1e032c6d3f4cdde2709992ba` | обновление 5 |
| `Data/Forest_Layout.json` | `3ba2a3448d786cd04ce1c26e71c0ec09` | обновление 5 |
| `Data/Forest_Opening.json` | `f6b2734f0d615b14a92e9e1818ccdc27` | обновление 3 |
| `Data/Forest_Items.json` | `a71b7d060cf5638432e4e19bd0dd8a2b` | обновление 3 |
| `Data/Forest_Dressing.json` | `a36ca992dfcc608e2d2daf26be52fafa` | обновление 3 |
| `Data/Forest_Notes.json` | `1d46315c90b1521a782612544cecab76` | обновление 3 |
| `README.md` | `a3e201291196e2ddbf056d633521265e` | обновление 5 |

   Не совпадает — **не импортировать**: пакет затрёт эти изменения. Записать, какие файлы и чем отличаются от файлов пакетов (`source\locations\Forest\Forest_Update3.unitypackage`, `Forest_Update5.unitypackage`), и спросить пользователя. `Forest_Look.json` (его могла править сборка, K6.6) пакет не трогает.
5. «До», в Play на хосте (1920 × 1080): контейнер снаружи с обоих торцов и внутри через открытые грузовые двери (ключ «Generator» — как в игре); полка хижины с воронкой и канистрой; ключи в будке КПП; стол кухни; пульты Leitstand. Время кадра хоста и клиента (клиент — если пользователь нажал Join, иначе NOT_RUN) на точках `Base`, `GenContainer` и в Leitstand.

### J2. Импорт
1. `Forest_Update6.unitypackage` — способом BASEV2, U2.2 (`AssetDatabase.ImportPackage(path, false)`, дождаться импорта и перезагрузки домена). Кода в пакете нет; в консоли нет `error CS`.
2. Проверить: md5 всех 101 файлов пакета совпадают с описью (`files` → `md5`; у 5 папок md5 нет); README с разделом «Обновление 6»; в `Forest_Layout.json` есть блок `u6` (19 строк в `changes`) и блоки `u5`, `u4`; в `Forest_Materials.json` в конце списка 11 новых — `M_GCont_Cont` … `M_Apparition`; в `Textures/` 8 новых файлов `T_GCont_…`, `T_Item_…`, `T_Fig_Figure_A_px`; `Models/GenContainer.fbx` — 695 932 байта, `Models/Apparition.fbx` — 588 396 байт, `Models/Item_RadioSet.fbx` — 45 820 байт.
3. **Без копий.** Модели заменяются на месте, рядом не должно появиться `GenContainer 1.fbx`, `Item_Jerrycan20 1.fbx` и т. п. Появились — GUID пакета в проекте не нашлись (J1.4 пропущен), префабы остались на старых сетках. Остановиться: ничего не удалять и не откатывать, записать, какие файлы появились и какие GUID у моделей в проекте, спросить пользователя.
4. Прежние текстуры (`T_ContainerU3_A_px`, `T_Dress_A_px` и др.) остаются — не удалять.

### J3. Пересборка, префабы предметов, интеграция
1. **Rebuild** — как H3.1 обновления 5 (`Tools → Locations → Location Builder` → `Forest` → **Rebuild**). Строки журнала — те же, кроме сети: `power grid: 19 lights, 19 halos, 6 lamp material pairs` (было 17 / 17: у контейнера 4 лампы вместо 2); `63 light fixtures` — как раньше (светильники — только в `BaseMain`; лампы контейнера — сеть, не светильники; если другое — записать, не чинить); число объектов сцены может отличаться — записать. Нет ни `children not found` (части `IX_…` контейнера), ни `fixture object(s) not found`, ни `power material pair … not found`, ни остальных предупреждений из списка G3.1, ни ошибки `textures are missing`. После сборки — `Mobile_RPAsset` по ответу на BASEV2 U1.6 (как раньше).
2. Импорт (только проверить и записать, настраивает ядро): у `GenContainer.fbx` — `Normals: Import`, `Convert Units`, Generate Colliders включено; у 84 `Item_*.fbx` и `Apparition.fbx` Generate Colliders выключено (`Item_`, `Apparition` в `noColliderPrefixes`); материалы переназначены на `Materials/URP/M_…` с теми же именами (контейнер: `M_GCont_…`, `M_Lamp_BulbOff`, `M_LEDOff`, `M_GlassClear`; предметы: `M_Item_Props`, `M_Item_Panel`, `M_TorchLens`, `M_Water`, `M_RadioDisplay`; фигура: `M_Apparition`); `Materials/URP/M_RadioDisplayOn` создан из данных, как `M_TorchLensOn`; у 8 текстур — фильтр Point, без сжатия, мип-карты включены.
3. **Capture** — сразу после сборки, до интеграции. Сравнить с Capture того же этапа из сборки (H3.3 внутри K2): новый контейнер в кадре только у точек `KPP` и `GenContainer` (пирамида кадра 16 : 9, туман 220 м; предметов при Capture нет — их создаёт игра). `KPP` — числа и кадр без оценки по L3.3 (как в обновлении 5); `GenContainer` — **камера точки перенесена** (прежняя оказалась бы внутри генератора), превью не совпадёт: числа и кадр без оценки; `UnderHand`, `Ribs` — по L3.3; остальные 13 точек — те же числа, что в сборке (в пределах L3.3).
4. **Префабы предметов** — `Tools/Snow/Forest/Build Item Prefabs` (F4.1 обновления 3, повторный запуск заменяет, а не дублирует): 84 префаба (83 + `Item_RadioSet`), все в списке сетевых префабов; правила F4.1 те же (коллайдер из каталога, не меньше 2 см, `CargoItem`, фонарик, канистра). Рация — простой реквизит (`kind: prop`, 6 кг), своего поведения у неё нет. Журнал меню — в отчёт.
5. **Интеграция** — `Tools/Snow/Integrate Location Level/Forest` с подшагом «Unity Terrain» (K5): строки и числа — как после сборки (K5.6); повторный запуск — те же. Capture после интеграции — как в K5.8 сборки: отличаются только `KPP` и `GenContainer`.

### J4. Фигура `Apparition` (ассет для будущих пугалок)
1. **Импорт `Models/Apparition.fbx`** — настройки импортёра (это `.meta` проекта, не файл пакета):
   - Rig: Animation Type **Generic**, Avatar Definition — Create From This Model, Root node — нет (движения корня нет: объект двигает код);
   - Animation: Import Animation включено; в файле 4 дубля `Apparition_Rig|Stand`, `|Walk`, `|Sit`, `|SitLook` — назвать клипы `Stand`, `Walk`, `Sit`, `SitLook`; кадры — как в файле (24 к/с), диапазоны по умолчанию не менять: экспортёр Blender начинает каждый дубль с нуля, Unity показывает Stand 0–1, Walk 0–30, Sit 0–1, SitLook 0–14 (Blender показывает те же кадры с единицы — не переносить); Loop Time: Walk, Stand, Sit — включено, SitLook — выключено (держит последний кадр); Loop Pose — выключено;
   - Model: Bake Axis Conversion — выключено. У `Apparition_Rig` и `Apparition_Body` (дети корня префаба) поворот x −90° — так у любой модели из Blender, не ошибка.
   Если после Rebuild настройки сбросились (ядро переписало импорт): ядро не править; разрешена одна правка кода — пост-процессор импорта в проекте `Assets/Snow/Editor/ApparitionImportRule.cs` (`AssetPostprocessor` только для пути `…/Models/Apparition.fbx`: `OnPreprocessModel` ставит Generic, Create From This Model, Import Animation и клипы с циклами по этому пункту; `GetPostprocessOrder` больше, чем у постпроцессора ядра, чтобы ядро его не перезаписало). Записать в журнал как ENGINEERING с причиной.
2. **Animator Controller** `Assets/Snow/Levels/Forest/Apparition/AC_Apparition.controller`: состояния `Stand` (по умолчанию), `Walk`, `Sit`, `SitLook` с одноимёнными клипами; переходы и параметры не нужны (код включает состояние по имени: `Animator.Play`, `CrossFade`).
3. **Префаб** `Assets/Snow/Levels/Forest/Apparition/Apparition.prefab`: модель + `Animator` (контроллер выше, Apply Root Motion — выключено, Culling Mode — Always Animate: фигуру видят и в зеркале); у `SkinnedMeshRenderer` — Update When Offscreen включено (в `Sit` сетка уходит ниже начала координат), тени — включены, **Rendering Layer Mask = `defaultMask` карты (2)**, как у остальных рендереров (L4.8), иначе фонари её не осветят. Без коллайдеров, без `NetworkObject` (пугалки покажут фигуру на каждом клиенте сами; DEFERRED).
4. **Проверка** — во временной сцене (не сохранять) или в окне превью клипа: `Walk` идёт по кругу без рывка на стыке, стопа опоры не скользит при скорости объекта 1,59 м/с; в `Sit` стопы стоят на полу 0,425 м ниже начала координат, спина — 0,67 м выше; `SitLook` кончается головой, повёрнутой к левому плечу фигуры. Снимки: `Stand`, два кадра `Walk`, `Sit`, конец `SitLook` — рядом с листами `fig_A_poses.jpg`, `fig_B_van.jpg`.

### J5. Проверка в Play
Хост — в редакторе, клиенты — виртуальные игроки MPPM (Join нажимает пользователь — попросить; без него сетевые пункты — NOT_RUN с причиной). Game View 1920 × 1080. Замки, топливо и сеть — как в игре; другим способом не снимать и не включать.
1. **Контейнер генератора** (шаг S7 пролога, как в игре):
   - ключ «Generator» из будки КПП открывает грузовые двери (`IX_ContDoorL_83` до 160°, `IX_ContDoorR_84` до 170°) и боковую дверь в дальнем торце (`IX_ContSideDoor_85`, до 100°); створки ничего не задевают, в проёмы можно войти, внутри пройти между генератором и щитом, сквозь стены не просвечивает;
   - насос двора → канистра → крышка короба горловины в дальнем торце (`IX_GenFillCap_300`, до 110°, табличка DIESEL / DAY TANK 100 L) → налив у горловины (`Gen_FillNeck`: 1 л/с, бак до 100 л; стоять у короба) → ключ пуска `IX_GenStart_86` (0 → 52°) → рубильник `IX_MainSwitch_90` (0 → 60°) → рубильник корпуса `IX_MainSwitch_75`;
   - с сетью: 3 плафона внутри и фонарь над грузовыми дверями горят, ореолы — у ламп; лампы «работа» на панели генератора и щита зелёные (`M_LEDOn`); без сети всё тёмное; автоматы `IX_Breaker_87…89` ходят 0…60°.
2. **Предметы:** строка журнала `[Snow] Forest items spawned: 233 (11 opening, 202 props, 20 notes).` (было 232 и 201); предметы — как на листах `items_A…D.jpg`, не проваливаются и не прыгают при старте; брать, носить, бросать; фонарик светит, стекло меняется (`M_TorchLens` ↔ `M_TorchLensOn`); канистра наливает; воронка на полке хижины стоит на раструбе; бирки ключей в будке читаются; записки читаются (тексты прежние, кроме `gen_procedure`: короб DIESEL, ключ START, бак 100 л на 28 часов — Приложение Б); записка `Note_GenProcedure` лежит на крыше панели генератора (1,75 м), не висит и не падает.
3. **Рация:** на полке западного пульта Leitstand, у его западного конца (справа от оператора, который стоит лицом к пульту), лицевой панелью к оператору; берётся (6 кг); окно канала тёмное. Проверка второго материала: в Play на хосте временно поставить `M_RadioDisplayOn` в слот окна её рендерера (инспектор, без сохранения) — светится оранжевый канал 27.135; вернуть. Сама X08 — DEFERRED.
4. **Ночь без сети.** Снимки без фонарика с 5–10 м: контейнер снаружи, Leitstand, кухня. Модели не темнее соседних в том же кадре, надписи на предметах видны вблизи.
5. **Клиент.** То же у клиента, в том числе после позднего входа: двери и крышка контейнера, свет, предметы и рация там, где их оставили.

### J6. Документы
`docs/dev/ImplementationStatus.md` — раздел «Forest U6»: пакет и как поставлен; сверка до и после импорта; журнал сборки; таблица `report.txt`; журналы меню предметов и интеграции; импорт и префаб фигуры; проверки QJ со статусами; снимки «до / после»; нагрузка и память текстур; изменённые файлы кода (ожидается: нет); что осталось TEMPORARY, OPEN, DEFERRED.

## 3. Что проверить

| № | Проверка | Ожидание |
|---|---|---|
| QJ1 | импорт и пересборка | сверка J1.4 (GUID, `base_md5`) и md5 после импорта (J2.2); без `error CS`; копий нет (J2.3); строки J3.1 (сеть 19 / 19, светильников 63) без предупреждений из списка; сцена сохранена |
| QJ2 | импорт моделей и текстур | J3.2 у всех 86 FBX и 8 текстур (таблица в отчёт) |
| QJ3 | префабы предметов | 84 префаба, `Item_RadioSet` в списке сетевых префабов, дублей нет (J3.4) |
| QJ4 | интеграция | строки и числа — как после сборки (K5.6); повторный запуск — те же |
| QJ5 | сверка | `UnderHand`, `Ribs` — L3.3; 13 точек — как в сборке; `KPP`, `GenContainer` — числа и кадры без оценки (J3.3) |
| QJ6 | фигура | импорт (J4.1), контроллер и префаб (J4.2–J4.3), снимки клипов (J4.4) |
| QJ7 | контейнер | J5.1: двери с ключом, горловина и налив, пуск, рубильник, свет и лампы с сетью и без |
| QJ8 | предметы и рация | J5.2, J5.3: 233 предмета, рация на пульте, второй материал окна |
| QJ9 | нагрузка и память | время кадра хоста и клиента на `Base`, `GenContainer`, в Leitstand — до (J1.5) и после. Треугольники: контейнер 28 209 (было 19 994), предметы 25 487 (было 12 356) + рация 828, фигура 1 084. Новые текстуры без сжатия с мип-картами — 10,2–13,7 МиБ (RGB24 или RGBA32). Записать; ничего не ужимать |
| QJ10 | консоль и сеть | нет ошибок у хоста и клиентов; поздний вход; первая сцена в Build Settings — та же, что до начала |

## 4. Не делать

- Не править то, что пришло в пакете (модели, данные, текстуры, README), не делать Apply переопределений в префабы локации, не переименовывать части. Если что-то выглядит не так — снимок и запись в отчёт; правку пришлю пакетом.
- Не делать пугалки X02, X03, X08 и не менять код пролога — DEFERRED. Не ставить фигуру в сцену карты и не добавлять ей коллайдер.
- Не расставлять предметы руками вместо данных; не возвращать бак 60 л самому (OPEN — решает пользователь).
- Не ставить и не обновлять пакеты Package Manager. URP-ассеты, Graphics, Quality, Player Settings — только проверять; исключение — возврат `Mobile_RPAsset` после сборки по BASEV2 U1.6.
- Не удалять файлы и не откатывать импорт без решения пользователя (J2.3); не писать руками YAML сцен, префабов, ассетов и `.meta` (настройки импорта фигуры — через импортёр).
- Не менять `Player.prefab` и `BenchBuilder.cs`; первую сцену в Build Settings не менять (по решению пользователя 16.10 загрузочной станет `BaseV2` — не в этом задании); кадр PS1 у игрока не выключать.
- Не трогать Terrain и его правки (`Forest_TerrainData.asset`): пакет его не меняет, интеграция ставит его как в сборке.

## 5. Отчёт

В формате §0 промпта: хэш точки отката; сверка GUID и `base_md5` (J1.4) и md5 после импорта (J2.2), копий нет (J2.3); журнал сборки (J3.1); импорт (J3.2); журналы меню предметов (J3.4) и интеграции (J3.5); таблица `report.txt` (J3.3); фигура (J4) со снимками; QJ1–QJ10 со статусами и доказательствами (снимки «до / после» тех же мест, строки `BenchLog`); нагрузка и память (QJ9); изменённые файлы кода с причиной (ожидается: нет); что осталось TEMPORARY, OPEN, DEFERRED.

## Приложение А. Модели

Кадр каждой модели — кадр её префаба (Y вверх). FBX — в соглашении обновления 3 (тот же экспортёр, что в обновлениях 4 и 5): FBX 7400, Y вверх, вперёд +Z, `UnitScaleFactor` 100, корень без поворота, у детей только сдвиг, нормали записаны (`Normals: Import`); у `Apparition` — скелет и анимация, у `Apparition_Rig` и `Apparition_Body` (дети корня) поворот x −90° (J4.1). Проверено обратным импортом: границы и точки поворота объектов — в пределах 1 мм, у фигуры — позы всех клипов совпадают с исходником.

**`GenContainer`** — место `prefabs` (p (393,9669; 16,3300; -335,2701)), поворот прежний. Объекты: корень (всё неподвижное) + 9 частей + `Window_Glass`. Части (данные `interactables`, ось — в кадре префаба Unity, + открывает):

| Часть | Ось | Ход | Точка поворота (префаб) | Что это |
|---|---|---|---|---|
| `IX_ContDoorL_83` | (0; 1; 0) | 0…160° | (-3,015; 1,445; 1,216) | левая створка грузовых дверей; ключ «Generator» |
| `IX_ContDoorR_84` | (0; -1; 0) | 0…170° | (-3,015; 1,445; -1,235) | правая створка; ключ «Generator» |
| `IX_ContSideDoor_85` | (0; 1; 0) | 0…100° | (3,016; 1,320; 1,630) | боковая дверь в дальнем торце северного бокса; ключ «Generator» |
| `IX_GenStart_86` | (-1; 0; 0) | 0…52° | (-0,043; 1,310; 0,405) | ключ пуска на панели генератора: 0 — OFF, 52° — пуск/работа |
| `IX_Breaker_87` | (-1; 0; 0) | 0…60° | (-1,135; 1,360; -0,950) | автомат щита (ряд из трёх) |
| `IX_Breaker_88` | (-1; 0; 0) | 0…60° | (-1,225; 1,360; -0,950) | автомат щита |
| `IX_Breaker_89` | (-1; 0; 0) | 0…60° | (-1,315; 1,360; -0,950) | автомат щита |
| `IX_MainSwitch_90` | (0; 0; 1) | 0…60° | (-1,250; 1,560; -1,005) | перекидной рубильник щита: 0 — OFF, 60° — GEN |
| `IX_GenFillCap_300` | (0; 0; 1) | 0…110° | (3,174; 1,376; 2,945) | крышка короба заливной горловины (петля сверху); табличка DIESEL / DAY TANK 100 L |

Свет (`prefabLights`, все `tag: power`, на старте выключены, `room: Exterior`): point (-0,780; 2,467; 0,220); point (0,780; 2,467; 2,320); point (-1,450; 2,430; -1,037); spot (-3,370; 2,690; 0,000) — 3 плафона и фонарь над грузовыми дверями; ореолы `M_Glow_Bulb` у каждой лампы. Горловина: `Gen_FillNeck` — горловина (397,4793; 17,5000; -332,7898), место игрока (398,2220; 16,3300; -332,8942) (мир), бак 100 л. Подробно — `GENCONT_FOREST_CONTRACT.md`.

**Предметы (84)** — `Models/Item_*.fbx`, начало координат прежнее (низ по центру; у ключей и замка — точка подвеса сверху, у фонарика — центр), по одному мешу на модель, материалы `M_Item_Props` + `M_Item_Panel` (+ `M_TorchLens` у фонарика, `M_Water` у ведра с водой, `M_RadioDisplay` у рации). Треугольники (было): `Item_Ashtray` 458 (308), `Item_Backpack` 502 (268), `Item_BinderBlue` 92 (104), `Item_BinderRed` 92 (104), `Item_Book` 48 (12), `Item_BookOpen` 76 (24), `Item_BookStack` 138 (36), `Item_Boots` 794 (264), `Item_BottleBeer` 356 (188), `Item_BottleBeerLying` 354 (188), `Item_BottleSchnapps` 256 (188), `Item_BottleWater` 272 (168), `Item_BoxLarge` 162 (12), `Item_BoxOpen` 188 (36), `Item_BoxSmall` 162 (12), `Item_BreadMouldy` 222 (104), `Item_Broom` 492 (468), `Item_Bucket` 428 (172), `Item_BucketWater` 448 (216), `Item_Butts` 269 (192), `Item_CableReel` 452 (264), `Item_CanCrushed` 292 (76), `Item_CanGreen` 256 (240), `Item_CanRed` 256 (240), `Item_CardboardFlat` 24 (12), `Item_Cards` 108 (96), `Item_CigPack` 12 (12), `Item_CigPackCrushed` 108 (12), `Item_Clipboard` 236 (26), `Item_Clothes` 1 012 (48), `Item_Dustpan` 60 (76), `Item_Envelope` 136 (12), `Item_Flashlight` 388 (448), `Item_Funnel` 388 (388), `Item_Hammer` 260 (72), `Item_Headphones` 408 (200), `Item_Jar` 258 (228), `Item_Jerrycan20` 502 (692), `Item_Kettle` 580 (240), `Item_Key_Garage` 362 (200), `Item_Key_Generator` 362 (200), `Item_Key_Hauptgebaeude` 362 (200), `Item_Key_Schranke` 362 (200), `Item_Magazine` 88 (12), `Item_Medicine` 12 (12), `Item_Mop` 1 064 (384), `Item_MugGreen` 290 (190), `Item_MugTipped` 290 (190), `Item_MugWhite` 290 (190), `Item_Newspaper` 88 (12), `Item_NewspaperOpen` 360 (24), `Item_Note` 136 (8), `Item_NoteForm` 136 (8), `Item_OilCan` 248 (100), `Item_Padlock` 312 (134), `Item_PaintCan` 296 (212), `Item_Pan` 422 (136), `Item_PaperBall` 80 (24), `Item_PaperBallB` 80 (32), `Item_PaperStack` 192 (96), `Item_PhotoFrame` 520 (58), `Item_PhotoLoose` 60 (12), `Item_Plate` 400 (80), `Item_PlateDirty` 524 (120), `Item_Pot` 488 (304), `Item_Radio` 276 (148), `Item_Rag` 436 (16), `Item_RopeCoil` 1 232 (320), `Item_Rug` 732 (2), `Item_Sack` 316 (36), `Item_Screwdriver` 108 (56), `Item_Soap` 124 (44), `Item_StickyNote` 32 (12), `Item_Thermos` 316 (152), `Item_TinLying` 346 (140), `Item_TinOpen` 384 (140), `Item_ToiletRolls` 372 (312), `Item_Toolbox` 224 (196), `Item_ToothCup` 202 (96), `Item_Towel` 372 (372), `Item_TrashBag` 314 (138), `Item_WoodCrate` 240 (540), `Item_Wrench` 92 (52); `Item_RadioSet` 828 (новая). Подробно — `ITEMS_CONTRACT.md`.

**`Item_RadioSet`** — в каталоге: «Рация», 6 кг, коробка-коллайдер корпуса (0,000; 0,060; 0,001) / (0,324; 0,120; 0,242), `kind: prop`, `display {off: M_RadioDisplay, on: M_RadioDisplayOn}`, `speaker` (0,000; 0,120; -0,060) (точка звука, кадр модели). В обжитом корпусе — `Leitstand_RadioSet_1`, p (339,5660; 17,4893; -354,8535), q (0,000000; -0,997564; 0,000000; 0,069756): полка западного пульта у его западного конца, лицом к оператору, задняя стенка — у ступеньки к наклонной панели (кабель антенны уходит в пульт).

**`Apparition`** — `Models/Apparition.fbx`, мест на карте нет. Скелет: `Root`, `Hips`, `Spine`, `Chest`, `Neck`, `Head`, `Shoulder.L`, `UpperArm.L`, `ForeArm.L`, `Hand.L`, `Shoulder.R`, `UpperArm.R`, `ForeArm.R`, `Hand.R`, `Thigh.L`, `Shin.L`, `Foot.L`, `Thigh.R`, `Shin.R`, `Foot.R`. Клипы (24 к/с): `Stand` 0–1, `Walk` 0–30, `Sit` 0–1, `SitLook` 0–14. Подробно — `APPARITION_CONTRACT.md`.

## Приложение Б. Изменения данных

Блок `u6` в `Forest_Layout.json` перечисляет их дословно (`changes`); всё остальное в шести файлах — байт в байт как в обновлениях 3 и 5 (форматы файлов те же: раскладка — без отступов, материалы — отступ 1, данные пролога и записки — отступ 1 с кириллицей как есть).

- Forest_Materials.json: 11 new materials at the end: M_GCont_Cont, M_GCont_Genset, M_GCont_Props, M_GCont_Panel, M_GCont_Needle, M_GCont_Diesel, M_Item_Props, M_Item_Panel, M_RadioDisplay, M_RadioDisplayOn, M_Apparition
- Forest_Layout.json prefabLights GenContainer: 2 lights -> 4 (3 bulkhead lamps inside, the barn lamp over the cargo doors; tag power, off at the start, room Exterior)
- Forest_Layout.json prefabGlows GenContainer: 2 halos -> 4 (M_Glow_Bulb at each lamp)
- Forest_Layout.json interactables GenContainer/IX_ContDoorL_83: max 170.0 -> 160.0
- Forest_Layout.json interactables GenContainer/IX_GenStart_86: axis [0.0, -0.0, 1.0] -> [-1.0, 0.0, 0.0], max 60.0 -> 52.0
- Forest_Layout.json interactables GenContainer/IX_Breaker_87: axis [0.0, -0.0, -1.0] -> [-1.0, 0.0, 0.0], max 45.0 -> 60.0
- Forest_Layout.json interactables GenContainer/IX_Breaker_88: axis [0.0, -0.0, -1.0] -> [-1.0, 0.0, 0.0], max 45.0 -> 60.0
- Forest_Layout.json interactables GenContainer/IX_Breaker_89: axis [0.0, -0.0, -1.0] -> [-1.0, 0.0, 0.0], max 45.0 -> 60.0
- Forest_Layout.json interactables GenContainer/IX_MainSwitch_90: axis [1.0, -0.0, 0.0] -> [0.0, 0.0, 1.0], max 90.0 -> 60.0
- Forest_Layout.json interactables GenContainer/IX_GenFillCap_300: axis [1.0, -0.0, 0.0] -> [0.0, 0.0, 1.0]
- Forest_Layout.json viewpoints GenContainer: p [396.4528, 18.25, -336.4071] -> [391.3652, 18.18, -335.4599], target [392.5596, 17.33, -335.2238] -> [394.4473, 17.38, -335.0852], fov 50.534 -> 62.0 (the old camera stands inside the new generator set)
- Forest_Layout.json noColliderPrefixes: + Apparition (the figure walks through things, no import collider)
- Forest_Opening.json interactables Gen_FillNeck: neck [396.079, 17.31, -336.7676] -> [397.4793, 17.5, -332.7898], stand [395.9705, 16.33, -337.54] -> [398.222, 16.33, -332.8942], label "Tagestank 60 l" -> "Tagestank 100 l", capacityL 60 -> 100 (the fill box on the far end; the new container's day tank holds 100 l; startL, burnLph unchanged)
- Forest_Opening.json items Note_GenProcedure: p [394.8284, 18.222, -335.3912] -> [394.0422, 18.082, -335.1292], q [0.0, -0.656059, 0.0, 0.754709] -> [0.0, 0.104528, 0.0, 0.994522] (on the top of the new generator panel: container-local (0.055, 1.752, 0.15) at yaw 4.0 deg; Update 3's panel top was 0.14 m higher, the form would hang in the air)
- Forest_Opening.json scares: X02_Crossing.figure, X03_BackSeat.figure (Apparition, clips), X08_Radio.radio (the prop, its display materials, the speaker point) - data for the scares when they are built (DEFERRED)
- Forest_Items.json items: tris of 79 models (the new meshes; colliders, origins, masses unchanged)
- Forest_Items.json items: + Item_RadioSet (label "Рация", 6 kg, the case as the collider, display off / on, speaker)
- Forest_Dressing.json props: + Leitstand_RadioSet_1 (Item_RadioSet) at p [339.566, 17.4893, -354.8535] q [0.0, -0.997564, 0.0, 0.069756] - the west console's writing shelf, its west end, the front to the operator (at the end of the list: the other props keep their indices)
- Forest_Notes.json gen_procedure: the new generator set - the fill box sign DIESEL (was HEIZÖL), the start key on the generator panel (was a START lever), day tank 100 l for about 28 hours (was 60 l / 17 h; burnLph 3.5 unchanged)

## Приложение В. Фигура для будущих пугалок (DEFERRED)

- **X02** (`scares` → `X02_Crossing.figure`): фигуру ставит код на дорогу в точке пугалки, поперёк дороги слева направо относительно машины, клип `Walk`; при скорости объекта 1,59 м/с ноги не скользят; переход за 2 с (около 8–10 м) — `Animator.speed = v / 1,587` (2,5–3,2: шаг частый) или медленнее с проскальзыванием — выбор того, кто будет делать пугалку. Следов нет.
- **X03** (`X03_BackSeat.figure`): фигуру ставят на точку посадки левой скамьи фургона (`Sit_L1` … `Sit_L4`, `VAN3_CONTRACT.md`) началом координат на сиденье, лицом к проходу (передом скамьи); клип `Sit`, при взгляде водителя в зеркало или назад — `SitLook` (0,6 с: голова выходит из плеч и поворачивается к кабине), затем скрыть. Поза рассчитана на скамью фургона: сиденье 0,425 м над полом, потолок 0,709 м над сиденьем, стенка 0,25 м за точкой посадки — фигура 2,3 м сложена под потолком. На правой скамье голова повернётся к стенке, а не к кабине.
- Видна только тем, кому показана пугалка (как решит код пугалок), без сети: префаб без `NetworkObject`.

## Приложение Г. Рация для X08 (DEFERRED)

`scares` → `X08_Radio.radio`: `prop` — `Leitstand_RadioSet_1` (её `NetworkObject` — в таблице id → объект `ForestItems`, F4.2), `displayOff` / `displayOn` — материалы окна канала (слот `M_RadioDisplay` её рендерера), `speaker` — точка звука в кадре модели (сверху, над решёткой). «Включилась сама»: на сервере — событие; у всех — слот окна `M_RadioDisplayOn`, шум и голос из `speaker` 4 с, затем `M_RadioDisplay`. Если рацию унесли — эффект там, где она сейчас.

## Приложение Д. Ловушки

1. **GUID.** Все 84 заменяемые модели — с GUID обновления 3 (как в проекте после F2). Если в проекте другие — копии `… 1.fbx` (J1.4, J2.3).
2. **`base_md5`.** Пакет переписывает шесть файлов данных и README целиком: правки, сделанные в проекте после пакетов 3 и 5, пропали бы — поэтому сверка до импорта (J1.4).
3. **Префабы предметов и интеграция.** Rebuild не трогает префабы предметов (`Assets/Snow/Levels/Forest/Items`), но у новых моделей другой набор материалов — поэтому меню J3.4 после импорта обязательно. Пересборка стирает интеграцию — после Rebuild снова интеграция, и только потом Play.
4. **Нормали** — только `Import`.
5. **Воронка** в модели, как в обновлении 3, — раструбом вверх; на полке хижины её переворачивает поза `Funnel_Hut` (q — 180° вокруг горизонтальной оси). Не «чинить».
6. **Фигура**: поворот x −90° у `Apparition_Rig` и `Apparition_Body` — норма; Generic, не Humanoid; клипы без движения корня (Apply Root Motion — выключено), в `Sit` корень опущен на ~0,96 м (так задумано: начало координат — на сиденье).
7. **Подвижные части двигают трансформ без `Rigidbody`** (ловушка 8 обновления 3): створка, открытая в предмет, проходит сквозь него — TEMPORARY, записать, если мешает.

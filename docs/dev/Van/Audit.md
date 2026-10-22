# Van (УАЗ «буханка», van3) — аудит В0

Дата: 21.10.2026. Задание: `docs/gdd/VAN3_INTEGRATION_UNITY.md` v1. Этап В0: только чтение, файлы проекта не менялись.
Точка отката: git `7d83c3d` «Checkpoint before van3 (UAZ) integration».

Прочитано: `SeatedVehicle`, `SeatAnchor`, `SeatController`, `ControlTypes`, `SeatRegistry`, `PlaneVehicle`, `AircraftSystems`,
`ElectricalSystem`, `PlanePresentation`, `BenchCarrier`, `WheelContact`, `InteractionHost`, `InteractionSensor`, `PlayerNet`,
`NetworkDoor`, `CargoItem`, `StowPoint`, `TieDownSocket`, `PlaneBenchBuilder` (рецепт NetItem), `BenchBuilder`,
`SkyIslandLocationHost` (биты), `ForestOpening`, `FuelCan`, `PlaneLoadout`, `SurvivalItem`, `BenchBot`, `BenchLog`;
пакет `source/vehicles/Van_Unity_v1` (README целиком, `VanRig*.cs`, `VanInteractable`, `VanItemInfo`, `VanSetup` —
меню и сообщения, `Data/*.json` — сводка); `VAN3_CONTRACT.md` (коллайдеры, сценарии).

## 1. Настройки проекта

| Что | Факт | Итог |
|---|---|---|
| Unity | 6000.0.67f1, URP 17.0.4, Forward+ (`PC_Renderer` m_RenderingMode 2) | — |
| Шаг физики | `TimeManager` 1176000 / 141120000 = 1/120 с; Maximum Allowed 0,1 с | OK (задание §3.1) |
| Слой 8 | `Interact` | OK (`VanSetup` берёт слой по имени) |
| Качество | текущее — PC (`m_CurrentQuality: 1`), конвейер по умолчанию — `PC_RPAsset` | — |
| `PC_RPAsset` | Depth Texture **вкл**; доп. источники Per Pixel; тени доп. источников **вкл**, атлас 2048, тиры 256 / 512 / 1024; Light Cookies **вкл** (атлас 2048); Light Layers **вкл** | всё, что нужно ригу, есть |
| `Mobile_RPAsset` | Depth Texture **выкл**, тени доп. источников **выкл** (cookies и Light Layers вкл) | на профиле Mobile ореолы и лучи фар работать не будут, тени фар — тоже. Сообщить пользователю (§6) |
| Камера игрока | в `Player.prefab` нет `UniversalAdditionalCameraData` → «как в конвейере» → глубина есть | OK |
| Сеть | NGO, тик по умолчанию 30 Гц (`BenchBuilder.CreateNetworkManager` его не задаёт) | телеметрия машины 10–15 Гц — проредить самим |

## 2. Переиспользуем как есть

| Система | Как используем |
|---|---|
| `SeatedVehicle` | база `VanVehicle`: места, `ServerSit`, `ServerExit` (дверь своей стороны, место для капсулы, наследование скорости через `rb.GetPointVelocity`), `ServerInsideExit` (места салона, `altExit`), `ServerAxes` (только Pilot), `OnSeatReleased`, сброс при отключении. Ось W/S = `Pitch`, A/D = `Roll`, Space = `Brake`, Q/E = `Yaw` |
| `SeatAnchor` | 8 штук из `seats.json`: `pelvis` = hip, `eye`, `exit`, `exitDoor`, `inside` + `altExit` для салона. Водитель — `SeatRole.Pilot`, остальные — `Passenger` (в `SeatRole` только два значения — салону хватает `Passenger` + `inside`) |
| `SeatController` | без изменений для езды: шлёт оси водителя каждый сетевой тик (ненадёжно), курсор Tab, короткий F = использовать, удержание F = выход |
| `NetworkDoor` | 5 дверей на шарнирах `doorL`, `doorP`, `doorSalon`, `doorZL`, `dooZP`: проверка препятствия в проёме (OverlapBox + ComputePenetration), `DoorModel`, реплика `DoorNet{State, Position}` |
| `InteractionHost` | `InReach`, `IsInside` (`interiorCenter/Size` = `model_facts.json`), `EmbeddedOnRay`, `ZoneOpen`, `Describe`, `ActionText`, `HoldSeconds` |
| `PlayerNet` | `UseRpc` → проверки `InReach` → `SideAllows` → `ZoneOpen` → `ServerUse` с дедупом и ответом-отказом; `AxesRpc`; `PilotCommandRpc`; `ItemActionRpc`; `CargoItem.HeldBy(client)` — что в руках на сервере |
| `CargoItem` | свободный груз в салоне (CARGO-01): захват за точку, переноска, `Loose/Carried/Secured/Stowed`, FixedJoint на `TieDownSocket` |
| Рецепт NetItem | `PlaneBenchBuilder.NetItem(...)`: Rigidbody (Interpolate, ContinuousDynamic) + NetworkObject + серверный NetworkTransform без масштаба + NetworkRigidbody + `CargoItem{label, massKg, itemKey}`; регистрация в `Assets/DefaultNetworkPrefabs.asset` (`BenchBuilder.EnsurePrefabList`). Метод берёт префаб из `Assets/UtilityPlane/Prefabs` — для `VanItem_*` нужен тот же рецепт с другим источником (своя функция в `VanPrefabBuilder`, не правка `PlaneBenchBuilder`) |
| Сборка стенда | `BenchBuilder.BuildPlayerPrefab` / `EnsurePrefabList` / `CreateNetworkManager` / `SaveNetworkScene` — вызывать из `VanBenchBuilder` (сами файлы не менять) |
| `BenchLog` | `BenchLog.Event(ev, k, v, …)` → JSON-строка в файл `-snow-log` и `[Bench]` в консоль |
| Биты состояния | образец `SkyIslandLocationHost`: `NetworkList<ulong>`, бит i = `i & 63` в слове `i >> 6`, запись слова только при изменении |
| Ось и тело | `BenchCarrier` / `PlaneVehicle`: Rigidbody без демпфирования, явные масса / центр / тензор, Interpolate; серверный NetworkTransform + NetworkRigidbody (вариант A) |
| Масса | образец `PlaneVehicle.UpdateMass`: сухая масса + топливо + 85 кг × занятые места в `pelvis`, тензор через параллельный перенос, пересчёт при изменении > 0,5 кг |
| `VanRig` (пакет) | вся презентация: детали, руль, колёса, педали, рычаг КПП, стрелки с динамикой, 41 лампа (напряжение, нить, мигание, бюджет теней, IES-маски, ореолы, лучи), стёкла, дворники, зеркало, рация, `PlacePart` |

## 3. Обобщаем (минимально, самолёт не меняет поведения)

| # | Где | Что | Почему |
|---|---|---|---|
| G1 | `InteractionHost.SideAllows` | сделать `virtual`; у машины сторона — поле `VanInteractable.side` (in / out / both) | сейчас правило только по суффиксу `_In/_Out`, у зон машины суффиксов нет (§3.3) |
| G2 | `InteractionHost` + `InteractionSensor.Pick` | флаг хоста `PreferInnerZones` (по умолчанию `false`): среди зон этого же хоста, в которые луч входит, пока он внутри первой найденной, выигрывает самая маленькая доступная (`Usable`) | 75 из 231 зон вложены (`insideOf`); без правила не открыть бак, не снять гайку, не запереть дверь (AT-V08) |
| G3 | `InteractionSensor` | буфер попаданий 16 → 64 | у машины на одном луче легко > 16 триггеров (кабина, двигатель), зона дальше 16-го попадания терялась бы |
| G4 | `PlayerNet` | серверный вызов «положить NetItem в руки игроку»: сервер создаёт предмет, захватывает его за клиента (`CargoItem.ServerGrab` + `_serverHeld`), клиенту — Rpc «вы держите X на расстоянии d», датчик ставит `Held` | сейчас захват начинает только клиент (`GrabRpc`); модель хранения В6 — NetItem появляется сервером прямо в руках |
| G5 | `ItemActionRpc` | действие предметом в руках через интерфейс (`IHeldItemAction`), `SurvivalItem` — одна из реализаций | ЛКМ предметами машины (фонарь, фальшфейер, тангента, лейка, карта) без копии `SurvivalItem` |
| G6 | `ControlTypes.PilotCommand` + `SnowControls.inputactions` (карта Seat) + `PlayerInputFrame` + `SeatController` | новое действие «Сигнал» (G) → `PilotCommand.HornOn/HornOff`; Z (`Autopilot`) у машины = аварийка; подсказка «Автопилот — только из левого кресла» сделать общей | §4.4; самолёт на новые команды отвечает своим «Здесь этого нет» |
| G7 | `FuelCan` (Snow.Levels) | реализует новый интерфейс `IFuelSource { float FuelL; float ServerAdd(float) }` из Snow.Runtime | машина (Runtime) не видит `FuelCan` (Levels); один серверный вызов заправки для канистры избушки и канистр машины (§В5) |
| G8 | `BenchBot` | шаги `brake:x`, ручник, `pilotcmd:…`, `useall:<host>` (проход по всем зонам для AT-V08) | сейчас нет оси Brake в скрипте бота |
| G9 | `ForestOpening` + `OpeningData` | разбор блока `car`; S1 / S3 / S4 / S5 — от машины через интерфейс из Runtime (`IPlayerCar`: места, литры, двигатель, позиция); убрать пропуск `S1|S3|S4|S5` | в коде шагов машины нет вовсе: блок `car` не читается (`ForestData.OpeningData` без поля `car`), S1/S3/S4/S5 не определяются, при S6 ставится пропуск (`ForestOpening.cs:734–738`) |

Не трогаем: `Player.prefab`, `BenchBuilder.cs`, `PlaneBenchBuilder.cs`, файлы `Snow/Assets/Van/**` (пакет), первую сцену в Build
Settings, кадр PS1. Состояние колеса самолёта (`WheelContact`) — не трогаем, `VanWheel` — копия.

## 4. Новое (Snow/Runtime/Vehicles/Van, Snow/Editor)

`VanVehicle : SeatedVehicle`, `VanMotion` + `VanWheel` (чистый C#), `VanSystems` (электрика, запуск, топливо, тепло, климат),
`VanControls` (таблица §6), `VanState` (биты, байты деталей, телеметрия, сохранение), `VanPresentation`, `VanLoadout`,
`VanTuning.asset`, `VanZone` (рядом с `VanInteractable`: `UtilityPlaneInteractable` с теми же id / kind / label — его видит
`InteractionHost`), `VanPrefabBuilder`, `VanBenchBuilder` → `Snow/Bench/P09_VanBench.unity`, EditMode-тесты `VanMotion` /
`VanSystems` / таблицы зон.

## 5. Риски

| # | Риск | Как закрываем |
|---|---|---|
| R1 | **Два писателя шарнира двери.** `NetworkDoor.Pose` пишет `localRotation` своего объекта; `VanRig.Apply` пишет ту же деталь (`doorL` — hinge в `parts.json`), а `ResyncPose()` помечает все детали «грязными» и перепишет дверь значением рига | один писатель — `NetworkDoor` (он нужен для коллайдера створки на сервере); `VanPresentation` каждый кадр копирует `ShownPosition × max` в `SetPart` двери, так что значение рига всегда совпадает с позой (формулы совпадают: `rest = 0`, ось = `rotAxis`, угол = `max`). Проверка — тест позы после `ResyncPose` (В1) |
| R2 | **Подвеска от статики** (§4.2): поза колеса в модели — статическая | `VanWheel`: начало луча — центр колеса + ход сжатия 0,10 м, длина `0,10 + F₀/k + r`; сжатие от полного отбоя; `SetWheel(i, угол, c − F₀/k)`. EditMode-тест: машина на ровном в покое → `c ≈ F₀/k` по каждому колесу, без отбойников |
| R3 | **Тяга и пробуксовка**: `WheelContact` продольно держит только трением качения и тормозом (якорь), привода нет | в `VanWheel` своя угловая скорость колеса, момент привода / тормоза, продольная сила по скольжению в пределах круга трения; якорь оставить для удержания на месте |
| R4 | **Сцепление-автомат и «охота» передач** | логика §4.3 в `VanMotion` + EditMode-прогон «разгон 0–100 и сброс газа» — число переключений за прогон |
| R5 | Отсечка: `vehicle.json maxRpm = 4000`, `redlineRpm = 4200`; задание — «отсечка 4200» | отсечка = `redlineRpm` 4200; в `PackageIssues.md` как замечание к данным |
| R6 | Моменты инерции: `vehicle.json` — «посчитать по коллайдерам», задание — явные `(4300, 4100, 1600)` | берём задание (TEMPORARY), запись в `Tuning/Van.md` |
| R7 | **Вложенные зоны** — менять датчик, который общий с самолётом и локациями | флаг хоста (G2), по умолчанию выключен; регрессия самолёта: его EditMode / PlayMode тесты |
| R8 | Предмет в руках создаёт сервер (G4) — у клиента `Held` ставится только при собственном захвате | Rpc владельцу; E (удержание) держит предмет как и сейчас: отпустил — предмет падает |
| R9 | Триггеры зон (231) на динамическом теле | как у самолёта: триггеры не меняют массу при явных массе / тензоре; лучи колёс игнорируют слой `Interact` и своё тело |
| R10 | Предметы на местах — детали модели без коллайдеров (контракт: «пока лежат на месте, нужен кинематический коллайдер») | модель хранения В6: на месте — деталь модели; коллайдер не нужен (не физический предмет). Двери не упрутся в предмет на месте — их коллайдеров нет |
| R11 | Общий редактор Unity с другими сессиями (snow-ff занята) | перед копированием пакета (вызывает компиляцию) и Play — спросить сессию, держащую редактор |
| R12 | Руки водителя на руле: в игре нет системы рук (у самолёта только штурвал, рук нет) | сокеты `Hand_Wheel_L/R` есть; руки — OPEN (нет модели рук игрока), записать |
| R13 | Профиль Mobile без Depth Texture и теней доп. источников | ореолы / лучи и тени фар только на PC; сообщить пользователю |
| R14 | Звуков двигателя / дверей нет в проекте | V-05: события и точки есть, звуки — заглушки |
| R15 | Сеть: `NetworkList<byte>` на ~120 деталей + биты; нагрузка 4 игрока | писать элемент только при изменении; телеметрия одной структурой 15 Гц (проредить от тика 30 Гц) |
| R16 | Машина и игрок внутри на клиенте — кинематическая копия; игроки сидят (ходьба по салону в движении — DEFERRED спеца) | `PlayerNet.UpdatePlatform` уже приклеивает стоящего игрока к `SeatedVehicle` с Rigidbody — проверить на машине (AT-V16) |
| R18 | **Найдено в В2 (21.10), исправлено:** выход клиента из места: сервер включал капсулу `CharacterController` копии игрока, пока она ещё стояла на сиденье внутри коллайдеров машины — PhysX выталкивал машину (при выходе на ходу её подбросило на 7 м). Общий код, у самолёта то же | `PlayerNet`: на сервере капсула удалённого игрока включается, когда его копия дошла до точки выхода (≤ 3 с); проверено: 3 выхода — машина не сдвинулась |
| R19 | **Найдено в В2, OPEN:** стоящий игрок для машины — неподвижная стена (капсула `CharacterController` не толкается телом); машина упёрлась в пассажира у двери и 20 с буксовала. Что должно быть (толкать игрока, сбивать, останавливаться) — не решено ни спецом, ни заданием | пока как есть; вопрос пользователю |
| R17 | Тест-прогоны и замеры | память проекта: пользователь 29.09 просил «пока без тестов»; это задание явно требует AT-V и замеры на стенде — делаем прогоны по заданию |

## 6. Сообщить пользователю (URP / Quality)

На профиле качества PC всё нужное включено. На профиле **Mobile** (`Assets/Settings/Mobile_RPAsset`): выключены **Depth
Texture** (Inspector → Rendering → Depth Texture) и **тени дополнительных источников** (Lighting → Additional Lights →
Cast Shadows). Если Mobile — рабочий профиль игры, включить там; если нет — ничего не делать.

## 7. План В1–В8

| Этап | Делаем | Изменённые общие файлы | Проверка |
|---|---|---|---|
| В1 | копия `Assets/Van` → `Snow/Assets/Van`; `UazVan.Runtime` в 3 asmdef; меню сборки, сверка чисел; `VanPrefabBuilder` → `Snow/Van/VanVehicle.prefab` (вариант) + `VanTuning.asset` + 43 `VanItem_*` (NetItem) в `DefaultNetworkPrefabs`; `VanBenchBuilder` → `P09_VanBench.unity` | 3 asmdef, `DefaultNetworkPrefabs.asset`, Build Settings (сцена стенда в конец списка) | AT-V01 |
| В2 | `VanWheel`, `VanMotion`, `VanVehicle` (тело, оси, ввод §4.4), места и двери, столкновения, телеметрия; временная кнопка стенда «завести»; G6, G8 | `ControlTypes`, `SnowControls.inputactions`, `PlayerInputFrame`, `SeatController`, `BenchBot`, `Controls.md` | AT-V02…V07 |
| В3 | `VanControls` по таблице §6, `VanState`, `VanPresentation` → риг, подсказки; G1–G3 | `InteractionHost`, `InteractionSensor` | AT-V08, V09 |
| В4 | `VanSystems.Electrics`, цепь лампы, стартер под нагрузкой | — | AT-V10…V12 |
| В5 | топливо, заправка (G7), запуск, рукоятка, толкач, паяльная лампа, тепло | `FuelCan` | AT-V13, V14 |
| В6 | предметы: деталь ↔ NetItem в руках (G4, G5), домкрат / лампа через `PlacePart`, колесо, цепи, стропа, лебёдка | `PlayerNet`, `SurvivalItem` (только интерфейс) | AT-V15, V16 |
| В7 | климат, стёкла, снег, дворники, отпечатки, зеркало, зацепки ужаса | — | спец AT-21, кнопки стенда |
| В8 | машина в Forest у `Car_Start` (`forestStart`), игроки на местах, шаги S1/S3/S4/S5 (G9), `VanSaveState`, сеть | `ForestOpening`, `ForestData`, сцена Forest | AT-V17, V18 |

После каждого этапа: сборка, регрессия самолёта (его тесты), отчёт, запись в `ImplementationStatus.md`.

## 8. Прочее замеченное

- `Car_Parked` в `Forest_Layout.json`: p `[323.2564, 16.3794, −335.4306]`, q `[0, −0.688355, 0, 0.725374]` → курс −87,0° — как в задании.
- В сцене Forest есть `Anchor_Car_Start` и `Anchor_Car_Parked`.
- `FuelCan` — 0,725 кг/л (бензин машины по заданию 0,74): у канистры избушки своя плотность, при переливе считаем литры, масса — у каждого своя (TEMPORARY, записать).
- `vehicle.json → electrics.batteryOcvEmpty 11,6`, колено ниже 20 % — модель АКБ машины своя (`ElectricalSystem` самолёта: другая кривая, без колена и холода) — по образцу, не повторное использование.
- У самолёта нет предохранителей; цепь «масса × предохранитель × тумблер» — новое у машины.

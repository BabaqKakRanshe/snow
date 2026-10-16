# Самолёт v13: огни как часть самолёта и сдвижные грузовые двери — задание для AI-агента

Версия 1.1 от 16.10.2026 (1.1: правки после независимой сверки с кодом проекта). Действует вместе с `PLANE_PROMPT_UNITY.md` (версия 2.1): правила работы, статусы (CONFIRMED / ENGINEERING / TEMPORARY / OPEN / DEFERRED), требования с ID и формат отчёта — оттуда (§0). Этап **В13** — обновление после интеграции по `PLANE_V12_INTEGRATION_UNITY.md`: на 16.10 она закончена (И1–И5, итог в `F:\ClaudeGames\snow\docs\dev\ImplementationStatus.md`). Если интеграция почему-то не закончена — сначала закончить её текущую итерацию и отчёт.

Проект: `F:\ClaudeGames\snow\Snow`, Unity 6000.0.67f1, URP 17 (Forward+), Netcode for GameObjects 2.13.3.
Пакет модели: `F:\ClaudeGames\snow\source\plane\UtilityPlane_Unity_v13\Assets\UtilityPlane` — v13. Рендеры для сравнения — `F:\ClaudeGames\snow\source\plane\UtilityPlane_Unity_v13\previews\`.
Контракт пакета — `UNITY_CONTRACT.md`: сдвижные двери — раздел 3.1, корпуса огней — 7.8, что изменилось и что задевает код — 20 и 20.1, проверки — раздел 11, пункты 65–72, координаты сокетов — приложение А.
Документы проекта: `F:\ClaudeGames\snow\docs\dev\ImplementationStatus.md`, `F:\ClaudeGames\snow\docs\dev\Tuning\UtilityPlane.md`.

## 0. Что есть и что нужно

### 0.1 Проект на 16.10.2026 (проверено по файлам)

- Модель **v12, редакция 2** и **зимний комплект 1.2** (И4: `Assets/UtilityPlane/Prefabs/WinterKit.prefab`, вложен в `UtilityPlane.prefab`; `PlaneWinterKitNet`, `KitCarry`, `PlaneLoadout`). Самолёт «в сборе»: `UtilityPlane_Game.prefab` + `PlaneLoadout`.
- Грузовые двери — распашные, на петле. `Editor/PlaneBenchBuilder.cs` (~151–169): локальная `Door(name, angle, powered)` ставит `NetworkDoor` на сам объект `CargoDoor_L/R` (его pivot — задняя наружная кромка двери): `openAngle` −100° / +100°, `axis` = up, `poweredTravelTime` = `tuning.cargoDoorTime` (4.5 с), `manualTravelTime` = `tuning.manualDoorTime` (1.5 с), `panelColliders` — нетриггерные коллайдеры внутри двери (`COL_CargoDoor_*`), `vehicleRoot`, `blockMask` = solid.
- `Runtime/Doors/NetworkDoor.cs`: в `Awake` запоминает только `_rest = localRotation`; `Pose()` пишет только `localRotation` (и только при изменении); `IsFree(position)` ставит коллайдеры двери в позу «петля повёрнута на `openAngle · position`» вокруг неподвижной точки петли, `OverlapBoxNonAlloc` + `ComputePenetration`; свои коллайдеры самолёта и уложенные на нём предметы (И3 L.4) не мешают. Тот же компонент — на дверях других стендов (`BenchBuilder`, `PhysicsBenchBuilder`, `openAngle` 80).
- `Runtime/Doors/DoorModel.cs`: `DoorState` (Closed → Opening → Open → Closing → Closed, Blocked), `DoorDrive`; в Blocked модель попадает только через обратный вызов `isFree`; позиция 0…1 линейно во времени.
- `PlanePresentation` пишет `rig.cargoDoorL/R = cargoDoorL/R.ShownPosition`; Rig v12 поворачивает те же двери на ∓100° — значения совпадают с `NetworkDoor`.
- `PlaneVehicle.ServerUse`: `SW_CargoDoor_L/R` — `s.Set(...)`, затем `Command(open, Powered)`; `Socket_Handle_CargoDoor_*_In` → `Toggle(Manual)`; `_Out` → то же, при `Locked` — «Заперто»; подписи — `Describe` / `ActionText` (`DoorText`). Закрылки: `_flap` (градусы), `tuning.flapDetents` 0/10/20/35, `Lever_Flap` ходит по ступеням с `_flapDirection`. Выход из L1/L2 при уложенной стремянке — через левую грузовую дверь в `State == Open` (`ServerInsideExit`).
- Кода, читающего позиции сокетов огней, нет (И1, тема J).

Перед правкой каждый файл перечитать: всё выше — на 16.10.

### 0.2 Цель

Пакет v13 в проекте. Грузовые двери сдвижные, как у минивэна: вид, коллайдер и проверка препятствий — по одной формуле на всех пирах, вошедший позже видит дверь там же; открытую дверь можно закрыть и снаружи, и изнутри. Огни сидят в своих корпусах. Всё остальное работает, как после И5.

### 0.3 Что дала модель v13

- **Огни (контракт 7.8).** У каждой наружной линзы корпус — часть `Body`: верхний маяк на обтекателе законцовки киля, нижний на накладке днища (в v5–v12 линза висела в 9 см под днищем), корпуса на законцовках крыла (навигационная линза — нос, полоса строба), бобышка хвостового огня, колпак фары по передней кромке. 8 сокетов наружных огней сдвинуты в корпуса (приложение А) — источники `Light_*`, `Glow_*`, `Beam_*` Rig ставит туда сам. Линзы с PSX-текстурой `T_Lens_*` (их материалы делает меню); свечение — по-прежнему `_EmissionColor` экземпляра Rig.
- **Двери (контракт 3.1).** `cargoDoorL/R` 0…1 — ход сдвижной двери: до 0.0625 выходит только передняя кромка, к 0.25 вся дверь в 14 см от борта, дальше уезжает назад на 1.30 м; в конце хода задний ролик идёт по сужению борта, дверь поворачивается до 3°. Rig пишет дверям `localPosition` и `localRotation`. Функция позы — `UtilityPlane.UtilityPlaneRig.SlidingDoorPose(string door, float t, Vector3 restLocalPos, Quaternion restLocalRot, out Vector3 localPos, out Quaternion localRot)`; числа — `Data/doors.json`. Pivot двери не менялся. Направляющие — часть `Body`, ролики — часть меша двери. Коллайдеры (все 60) — те же меши, что в v12.
- **Ручки на кромке (контракт 3.1, 9).** На передней кромке каждой грузовой двери — ручка-накладка, сокет и зона `Socket_Handle_CargoDoor_L/R_Edge` (kind `door`, родитель — дверь). У открытой двери кромка стоит у заднего края проёма: изнутри отсека достаётся только она — внутренняя ручка `_In` уходит за стойку проёма.
- **Не изменились:** имена, иерархия, pivot, остальные каналы Rig, прежние 79 зон, 16 источников, LOD-группы, теневые копии, остальные `Data/*.json`, атласы. Стало: 81 зона, 127 сокетов, 399 объектов в FBX; треугольники вблизи — 143.0 тыс. вместо 140.1.

### 0.4 Решения пользователя (CONFIRMED)

- 16.10: огни «выглядят как чуждые, нужно сделать их частью нашего самолёта» — сделано в модели.
- 16.10: грузовые двери «должны уезжать назад с анимацией как в семейных бусиках тип Альфард» — сдвижные двери, как у Toyota Alphard.

## 1. Что затронуто в проекте

| Где | Что меняется |
|---|---|
| `Assets/UtilityPlane/**` | пакет v12 → v13 поверх, без удаления: `Models/UtilityPlane.fbx`, `Data/sockets.json`, `interaction_volumes.json`, `lod.json`, `lights.json`, `atlas_texel.json`, новый `Data/doors.json`, новые `Textures/T_Lens_BaseColor.png`, `T_Lens_Emission.png`, `Scripts/UtilityPlaneRig.cs`, `Editor/UtilityPlaneSetup.cs`, документы |
| `Runtime/Doors/DoorModel.cs`, `NetworkDoor.cs` | режим сдвижной двери; препятствие с учётом позиции; разрешение на шаг (раздел 4) |
| `Editor/PlaneBenchBuilder.cs` → `UtilityPlane_Game.prefab`, стенд P03, Sky Island | грузовые двери — сдвижные |
| `Runtime/Aircraft/PlaneVehicle.cs`, `AircraftTuning.cs` + ассет | ручки `_Edge`; блокировка дверей и закрылков (TEMPORARY); выход L1/L2 |
| `docs/dev/ImplementationStatus.md`, `docs/dev/Tuning/UtilityPlane.md` | модель v13, двери, TEMPORARY-числа |

## 2. Пакет

**1. Подготовка**
1. Выйти из Play Mode, проверить состояние редактора (R-4).
2. Резервная копия (Git нет) — `F:\ClaudeGames\snow_archive\backup\plane_v12_<дата>\`, как в И1: `Assets/UtilityPlane`, весь `Assets/Snow` (код, `Plane/UtilityPlane_Game.prefab`, `Plane/Items`, `Bench/P03_PlaneBench.unity`, `Plane/AircraftTuning.asset`), `Assets/SkyIsland/Scenes/SkyIsland.unity`, `Assets/DefaultNetworkPrefabs.asset` (всё с `.meta`) и `F:\ClaudeGames\snow\docs\dev\`.
3. Снимки «до» (1920 × 1080): снаружи с 2 м — верхний маяк, нижний маяк, законцовка, фара, хвостовой огонь; грузовая дверь открыта.

**2. Установка**
1. `robocopy <пакет v13>\Assets\UtilityPlane <проект>\Assets\UtilityPlane /E` без `/PURGE`; `.meta` проекта не трогать.
2. `AssetDatabase.Refresh`, компиляция без ошибок. Пока не сделан раздел 3, проект собирается, но грузовые двери спорят с Rig (Rig пишет сдвижную позу, `NetworkDoor` — поворот петли): это ожидаемо, двери до раздела 3 не проверять.
3. `Tools → Utility Plane → Build Materials + Prefabs`. Ожидаемые строки:
   - `[UtilityPlane] Assets/UtilityPlane/Prefabs/UtilityPlane.prefab: 60 convex colliders, 81 interaction volumes, 16 lights (14 cookies, 7 with shadows at the default quality, glow / beam FX: yes), 116 renderers without shadows, LOD groups: Exterior 3, Cockpit 3, 5 shadow proxies, fog meshes copied: 5`
   - `[UtilityPlane] Done (URP), v13. Prefabs in Assets/UtilityPlane/Prefabs; readable collider / fog meshes: 65 in Assets/UtilityPlane/Generated/UtilityPlane_RuntimeMeshes.asset`
   - новых ненужных файлов меню не перечислит; список v4 (11 файлов) по-прежнему ждёт разрешения пользователя.
   Если числа другие — записать дословно и сообщить пользователю, модель не править.
4. Зимний комплект: если есть `Assets/UtilityPlane/Prefabs/WinterKit.prefab` (на 16.10 — есть), сразу после п. 3 выполнить `Tools → Utility Plane → Install Winter Kit`: пересобранный префаб самолёта теряет вложенный `WinterKit`. Строка меню — как в И4 (`15 items, 15 zones … nested into UtilityPlane.prefab`).
5. У `M_Light_Red`, `_Green`, `_White`, `_Beacon`, `_Landing` — `_BaseMap` = `T_Lens_BaseColor`, `_EmissionMap` = `T_Lens_Emission`, у `M_Light_Cabin` текстур нет; `T_Lens_*` — 64², Filter Point, мипы (импорт ставит постпроцессор пакета).

Пересборка стенда и уровня — после кода (раздел 6).

## 3. Сдвижные грузовые двери в коде (ENGINEERING)

**3.1 `DoorModel.cs` / `NetworkDoor.cs`: режим Slide.** Шарнирный режим остаётся по умолчанию (двери кабины, другие стенды).

```csharp
// DoorModel.cs, рядом с DoorState и DoorDrive (уровень пространства имён Snow.Doors):
public enum DoorMotion : byte { Hinge, Slide }

// NetworkDoor.cs:
[Tooltip("Hinge: rotation about axis; Slide: v13 sliding cargo door (UtilityPlaneRig.SlidingDoorPose)")]
public DoorMotion motion = DoorMotion.Hinge;
Vector3 _restPos;                       // Awake: _restPos = transform.localPosition; (рядом с _rest)

/// Локальная поза объекта двери в положении 0..1.
void LocalPoseAt(float position, out Vector3 localPos, out Quaternion localRot)
{
    if (motion == DoorMotion.Slide)
        UtilityPlane.UtilityPlaneRig.SlidingDoorPose(doorId, position, _restPos, _rest, out localPos, out localRot);
    else
    {
        localPos = _restPos;
        localRot = _rest * Quaternion.AngleAxis(openAngle * position, axis);
    }
}
```
- `Awake`: в режиме Slide `doorId` должен кончаться на `_L` или `_R` (`CargoDoor_L` / `CargoDoor_R`): иначе `Debug.LogError` — функция молча считала бы дверь правой.
- `Pose(position)`: по-прежнему только при изменении; через `LocalPoseAt`: в режиме Slide писать `localPosition` и `localRotation`, в Hinge — только `localRotation`, как сейчас.
- `IsFree(position)`: поза объекта двери в мире на шаге — `newPos = parent.TransformPoint(lp)`, `newRot = parent.rotation * lr` (без родителя — `lp`, `lr`); `toNew = newRot * Quaternion.Inverse(transform.rotation)`; для каждого коллайдера `pos = newPos + toNew * (panel.transform.position − transform.position)`, `rot = toNew * panel.transform.rotation`. Остальное (`LocalBounds`, `OverlapBoxNonAlloc`, `IsOwn`, `ComputePenetration`, `CharacterController`) — без изменений. В режиме Hinge `newPos` — текущая позиция, поведение прежнее.
- Rig и `NetworkDoor` пишут двери одну и ту же позу (оба запоминают покой в `Awake`, до `OnNetworkSpawn`); `PlanePresentation` продолжает писать `rig.cargoDoorL/R = ShownPosition`. У вошедшего позже возможен один кадр с закрытой дверью, если `Rig.Update` идёт раньше `PlanePresentation.Update` — проверить в V13-05; если заметно — `[DefaultExecutionOrder(-10)]` у `PlanePresentation`.

**3.2 `PlaneBenchBuilder`.** `Door(...)` получает режим: грузовым дверям — `motion = DoorMotion.Slide`, `openAngle = 0`; `poweredTravelTime` = `tuning.cargoDoorTime`, `manualTravelTime` = `tuning.manualDoorTime` — как сейчас.

**3.3 Ручки на кромке (`PlaneVehicle`).** Новые зоны `Socket_Handle_CargoDoor_L_Edge` / `_R_Edge`:
- `ServerUse`: игрок внутри (`ClientInside`) — `Toggle(Manual)`, как `_In`; снаружи — как `_Out` (при `Locked` — «Заперто»);
- `Describe` / `ActionText` — как у остальных ручек грузовой двери (`DoorText`, «открыть» / «закрыть»);
- зона всегда активна; в закрытом положении она лежит в щели 6 мм между кромкой двери и стойкой проёма.

**3.4 Время хода (TEMPORARY).** Оставить 4.5 с от электропривода и 1.5 с рукой. Ориентиры: электропривод сдвижной двери Toyota Alphard (30-я серия) — 4.7 с на открытие и 6.3 с на закрытие, после обновления скорости 3.7 / 4.7 с; вручную такую дверь сдвигают за 1.5–2 с. `DoorModel` ведёт позицию линейно, разгон и торможение даёт формула позы — дополнительно сглаживать не нужно. Разное время на открытие и закрытие — только если пользователь попросит (раздел 5).

**3.5 Препятствия.** Дверь сначала выходит наружу, затем едет назад вдоль борта: мешают ящик или игрок в проёме и у борта за проёмом (полоса до 0.16 м от обшивки, на 1.30 м назад от проёма). Поведение прежнее: шаг не свободен → Blocked; новая команда выводит из Blocked. Реверс при защемлении (у минивэнов — только при закрытии от привода) — не делать до решения пользователя (раздел 5).

**3.6 Выход L1/L2 при уложенной стремянке (TEMPORARY).** Сдвинутая на 0.9 и больше дверь открывает проём почти целиком (остаётся меньше 3 см): `ServerInsideExit` считать открытой дверь с `Position ≥ 0.9` (в том числе Blocked), а не только `State == Open`.

## 4. Закрылки (TEMPORARY, решение пользователя — раздел 5)

При закрылках 35° выходящая дверь проходит сквозь закрылок своего борта (`t` ≈ 0.11–0.30, контракт 3.1); при 0, 10 и 20° зазор от 2.7 см; открытой и закрытой двери закрылки не мешают. Закрылок входит в коллайдер крыла, `IsOwn` его пропускает — проверка препятствий этого не увидит. До решения пользователя:
1. `NetworkDoor`: необязательный делегат `public Func<bool> MoveAllowed` (сервер). В `FixedUpdate` — `_model.Tick(dt, powered, p => (MoveAllowed == null || MoveAllowed()) && IsFree(p))`: так запрет переводит дверь в Blocked, как препятствие, и сама она потом не поедет.
2. `PlaneVehicle` задаёт его обеим грузовым дверям: `FlapAngleForDoors() <= tuning.cargoDoorMaxFlapDeg + 0.5f`, где `FlapAngleForDoors() = max(_flap, flapDetents[s.FlapLever])` (рычаг уже в 35°, а закрылок ещё едет — дверь не трогается); `cargoDoorMaxFlapDeg` — новое поле `AircraftTuning`, TEMPORARY 20.
3. `ServerUse`: для `SW_CargoDoor_*` проверка до `s.Set(...)`, для ручек `_In`, `_Out`, `_Edge` — после проверки замка; при запрете — `reason = "Мешает закрылок"`, `return false` (тумблер не переключается, дверь не трогается).
4. Рычаг: ступень 35° не ставится, пока какая-нибудь грузовая дверь в промежуточном положении (`0 < Position < 1`): `reason = "Мешает грузовая дверь"`, `_flapDirection = −1` (следующий клик убирает закрылки, рычаг не залипает).
5. Подсказки при наведении (клиент) — по реплицируемым `Telemetry.Flap` и `Switches.FlapLever`, а не по `_flap` (его на клиенте нет).
Так делают на настоящих самолётах с грузовой дверью под закрылком (табличка или запрет в РЛЭ). Числа и тексты — TEMPORARY.

## 5. Решения пользователя, которые нужны (OPEN)

Не решать за пользователя. Пока ответа нет, работает вторая колонка. Вопросы — в отчёт одним списком.

| Вопрос | До ответа (TEMPORARY) |
|---|---|
| Время хода грузовых дверей: от привода, рукой; разное на открытие и закрытие | 4.5 с и 1.5 с, одинаковое |
| Реверс при защемлении (дверь от привода при закрытии наткнулась — уехать обратно) | нет: Blocked, как у всех дверей |
| Закрылки 35° и грузовые двери (раздел 4) | дверь не двигается при закрылках больше 20°; рычаг не ставится в 35° при двери в промежуточном положении |
| Открытие грузовых дверей в полёте (GA8 Airvan и вертолёты летают с открытой сдвижной дверью, с ограничением скорости) | как сейчас: без ограничений |
| Хоррор: открытая дверь закрывает заднее окно своего борта — фигура в `Socket_Scare_Window_Aft_L/R` видит обшивку двери | сценарии хоррора — OPEN, как раньше; только записать |

## 6. Пересборка и документы

1. `Tools → Snow → Build P03 Plane Bench`; `Tools → Snow → Integrate Sky Island Level`, пересохранить сцену (хэши `NetworkObject`). В сценах нет экземпляров с потерянными переопределениями и Missing Prefab; у экземпляров самолёта нет переопределений на `CargoDoor_*`, `Lens_*`, `Light_*`; `WinterKit` в варианте есть.
2. `F:\ClaudeGames\snow\docs\dev\ImplementationStatus.md`: модель v13; что сделано по разделам 3–4 с файлами и статусами; TEMPORARY-числа — в `F:\ClaudeGames\snow\docs\dev\Tuning\UtilityPlane.md`.

## 7. Что проверить

Камера — игрока, Game View 1920 × 1080. «Пункт N» — раздел 11 контракта v13. Двери — с тумблера и с ручек, у хоста и клиента. Промежуточное положение двери снимать через `t × 4.5` с после команды тумблером (или Dev-паузой, если она есть).

| № | Проверка | Ожидание |
|---|---|---|
| V13-01 | установка | строки меню п. 2.2.3, комплект п. 2.2.4; материалы и импорт `T_Lens_*` — п. 2.2.5 (пункт 65) |
| V13-02 | огни днём вблизи | пункт 66: маяки на обтекателе киля и на накладке днища без зазора, корпуса законцовок с красным и зелёным носом и полосой строба, бобышка хвостового огня, колпак фары в рамке; на линзах рисунок |
| V13-03 | огни ночью | пункт 67 и пункты 48–51: секторы АНО, вспышки стробов, вращение маяков, пятно фары — как в И1 (I-11); источники и ореолы в корпусах, ничего не висит в воздухе; нижний маяк светит от днища |
| V13-04 | ход двери | пункты 68–70 при `t` = 0; 0.06; 0.25; 0.6; 1: сначала выходит передняя кромка, дверь уезжает назад вдоль борта, проём открыт целиком; коллайдер и зоны ручек едут с дверью; обратно — тот же путь; правая — зеркально |
| V13-05 | сеть | хост и клиент: положение двери совпадает на всём ходу, без рывков; вошедший позже видит дверь открытой, закрытой или остановленной там же, без кадра с закрытой дверью (пункт 71) |
| V13-06 | препятствия | ящик в проёме, ящик и капсула игрока у борта за проёмом: дверь останавливается (Blocked), сквозь не проходит; убрать — новая команда доводит её до конца; уложенные на дверях кабины предметы по-прежнему не мешают |
| V13-07 | ручки на кромке | дверь открыта, питания нет: изнутри отсека, стоя у проёма, луч попадает в `IV_Socket_Handle_CargoDoor_L_Edge` (и правую) — дверь закрывается рукой; снаружи кромка тоже работает, при `Locked` — «Заперто»; в закрытом положении зона не перехватывает луч к другим зонам отсека |
| V13-08 | закрылки | пункт 72 и раздел 4: при 35° (и пока закрылок едет к 35°) дверь не трогается, тумблер не переключается, отказ с текстом; при 0–20° ходит; ступень 35° при двери в промежуточном положении — отказ, следующий клик убирает закрылки; открытой и закрытой двери закрылки не мешают |
| V13-09 | выход из отсека | при уложенной стремянке L1/L2 выходят через левую грузовую дверь при `Position ≥ 0.9` (п. 3.6); игрок проходит в проём при открытой двери; дверь не загораживает `Socket_Exit_Cargo_L/R` |
| V13-10 | электрика | привод грузовой двери без питания стоит; рукой — ходит (как в И2) |
| V13-11 | Rig | на стоянке трансформы дверей не пишутся (Profiler, пункт 45); двери кабины — прежние, на петлях |
| V13-12 | консоль, сборка | ошибок нет; в Development Build двери ходят так же |

## 8. Не делать

- Не менять файлы пакета в `Assets/UtilityPlane` (геометрию, текстуры, `UtilityPlaneRig.cs`, формулу двери, зоны). Если формула, направляющие или ручки не подходят игре — запрос пользователю с точным описанием.
- Не возвращать распашные двери поворотом петли поверх Rig.
- Не ставить новые пакеты; не менять `PC_RPAsset`, `PC_Renderer`, Graphics Settings, Quality, профили постобработки.
- Не удалять файлы v4 без разрешения пользователя.

## 9. Отчёт

По шаблону §0 промпта:
- что заменено и собрано, строки меню (самолёт и комплект);
- изменённые файлы кода (разделы 3–4) с причинами;
- проверки V13-01…V13-12 со статусами и снимками: огни днём и ночью до и после (п. 2.1.3); ход двери при `t` = 0; 0.06; 0.25; 0.6; 1 — у хоста и у клиента; ручка на кромке изнутри;
- OPEN одним списком (раздел 5).

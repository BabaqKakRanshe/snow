# Van_Unity_v1 — замечания к пакету

Файлы пакета в `Snow/Assets/Van` руками не меняются (задание §0 п.4). Здесь — что найдено, где и как обошли в игровом коде.

| # | Дата | Где | Что | Как обошли |
|---|---|---|---|---|
| P-1 | 21.10 | `Data/vehicle.json → engine` | `maxRpm` 4000 и `redlineRpm` 4200; задание §4.3 — «отсечка 4200» | отсечка = `redlineRpm` (4200), `VanTuning` TEMPORARY |
| P-3 | 21.10 | `Models/Van.fbx` импорт + `Editor/VanSetup.cs` (поиск `SkinnedMeshRenderer` на детали) | Unity 6000.0.67 импортирует 7 мешей с блендшейпами (`Wheel_*` Flat, `Snow_Roof/Visor/Bumper` Clear) как `MeshFilter` + `MeshRenderer`; блендшейпы в мешах есть (проверено: по 1 у каждого), но меню пишет 7 предупреждений «blend shape … not found», в отчёте «0 blend shapes» вместо 7 | в игровом варианте (`VanPrefabBuilder.FixBlendShapes`) рендерер этих 7 деталей заменяется на `SkinnedMeshRenderer` с тем же мешем, материалами и тенями, ригу прописываются `skin` и `blendIndex`. Пакет не менялся; предупреждения меню пакета остаются |
| P-2 | 21.10 | `Data/vehicle.json → inertiaNote` | «посчитать по коллайдерам»; задание §4.1 — явные моменты (4300, 4100, 1600) кг·м² | берём задание, `Rigidbody.automaticInertiaTensor = false` |

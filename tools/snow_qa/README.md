# Snow QA — набор для проверок в редакторе

Проверенные скрипты для заданий по картам (Forest и др.): ставить пакеты, запускать Play с клиентами, снимать кадры, проверять физику и состояние. Не писать их заново в каждой сессии. Python 3, Unity Pipeline CLI (`unity command`), проект `F:\ClaudeGames\snow\Snow`.

| Файл | Что делает |
|---|---|
| `ue.py` | C# в редакторе или клоне MPPM (`run`, `run_long` — ответ через файл для долгих операций, `recompile` — строки `error CS`, `wait_ready`) |
| `pkg.py` | пакет обновления: `pre` (base_md5, GUID), `install` (распаковка с проверкой GUID, 2D / InPrefab новым файлам), `post` (md5, GUID, копии «… 1.fbx») |
| `play.py` | `start --clients 2 --log DIR` (хост + клиенты входят сами, Game View 1920 × 1080), `join 3` (поздний вход), `hour 12`, `stop` |
| `game.py` | действия по правилам игры: `go`, `use`, `describe`, `angle`, `grab`/`pocket`/`free_hands`, `carry_to` (держать предмет у точки и жать Use), `bring`, `screenshot` |
| `shots.py` | кадры и время кадра по точкам из `configs/*_points.json`; `frames before.jsonl after.jsonl` — сводка до/после |
| `checks.py` | `reach` (проходимость по коллайдерам: фургон / пешеход), `line` (коробка фургона вдоль линии), `touch` (касания при движении), `probe` (состояние у хоста или клиента) |
| `sheet.py` | лист снимков — один файл вместо десятков |

Примеры:
```
python tools/snow_qa/pkg.py pre  source/locations/Forest/Forest_Update7_src/reports/Forest_Update7_manifest.json
python tools/snow_qa/play.py start --clients 2 --log docs/dev/Audit/<папка>
python tools/snow_qa/shots.py tools/snow_qa/configs/forest_points.json docs/dev/Audit/<папка> after perf
python tools/snow_qa/checks.py reach tools/snow_qa/configs/forest_reach.json van
python tools/snow_qa/checks.py probe 3 --items Item_RadioSet
python tools/snow_qa/play.py stop
```

## Проверки по умолчанию (решение пользователя 22.10.2026)

Полный объём из заданий — только когда пользователь попросит его явно. По умолчанию:

1. **Дешёвое — всегда:** сверка пакета до/после (`pkg.py`), журнал Rebuild и интеграции (строки и предупреждения), импорт (нормали, коллайдеры, материалы, текстуры), проверки по данным (контрольные места, земля), чтение кода, который трогает новые данные.
2. **Play — один прогон:** хост + 1 клиент, поздний вход вторым клиентом — одна проба состояния (`checks.py probe`). Один сценарий «как в игре» на главную механику обновления (например, цепочка питания) и одна физическая проверка, если обновление про проходимость (`checks.py reach`).
3. **Снимки — 10–15 ключевых** (`configs/*_points.json`, группа обновления), «до» и «после» с тех же точек, смотреть листом (`sheet.py`). Без матриц «5 моделей × 3 дистанции × 3 сдвига».
4. **Время кадра — 3 точки, только хост** (группа `perf`), сводка `shots.py frames`.
5. **Если тестовый инструмент ломается дважды** — NOT_RUN с причиной, не чинить его посреди задания. Чинить сюда, в набор, отдельно.
6. Вопросы пользователю, которые точно возникнут (кисти Terrain, спорные данные), — одним пакетом в начале.

## Ловушки (уже учтены в коде)

- Python в этой оболочке пишет stdout в cp1251 — кириллица ломалась; `ue.py` переключает на utf-8.
- CLI бросает ожидание через ~5 с («Main thread operation timed out»), а код продолжает работать: долгие операции — `ue.run_long`.
- `CargoItem.HeldBy(client)` возвращает **первый** предмет с рукой клиента — перед тестом с предметом в руке `game.free_hands()` (вызывается в `grab`).
- Цель руки из CLI устаревает через 0,5 с (`targetTimeout`): нести предмет — `game.carry_to`, нажатие Use — в том же вызове.
- После телепорта предмет в руке остаётся на месте (рука тянет его физикой) — `game.bring`.
- `ForestItems.Find(id)` работает только на сервере; у клиента объекты называются по модели (`Item_RadioSet(Clone)`).
- Материалы, которых нет в сцене, в Play не загружены: `Resources.FindObjectsOfTypeAll` их не видит — брать `AssetDatabase.LoadAssetAtPath`.
- `AssetDatabase.ImportPackage` с пакетами локаций молча ничего не делает — `pkg.py install`.
- Ядро при каждом Rebuild вызывает `SkyIslandImport.FixAll` и сбрасывает импорт моделей и текстур пакета — нестандартные настройки держать правилом `AssetPostprocessor` с порядком > 0 (`Snow/Editor/*ImportRule.cs`).
- Проба фургона шаром проскальзывает между пирамидами надолбов — только коробки (`checks.py`).
- Камера в режиме редактора после `SampleAnimation` рисует старый скиннинг — снимать запечённую сетку (`BakeMesh`).
- Перед компиляцией и записью кода в Assets — никто не в Play (`ue.status()['playMode']`), другие сессии предупредить.

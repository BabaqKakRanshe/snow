# Snow

Кооп-хоррор от первого лица. Контекст игры — [docs/context_pack/GAME_CONTEXT.md](docs/context_pack/GAME_CONTEXT.md).

## Что где лежит

| Папка | Что внутри | В git |
|---|---|---|
| `Snow/` | Unity-проект (6000.0.67f1, URP). В Hub открывать эту папку | да (бинарники через LFS) |
| `docs/context_pack/` | контекст игры: `GAME_CONTEXT.md`, README каждой локации, карты, планы, контакт-листы, `CREDITS.md`. Отдавать ИИ целиком | да |
| `docs/gdd/` | действующие спецификации и задания: `PLANE_PROMPT_UNITY.md`, `PLANE_V12_INTEGRATION_UNITY.md`, `PLANE_WINTERKIT_UNITY.md`, `BASEV2_INTEGRATION_UNITY.md`, `VEHICLE_SYSTEM_AI_SPEC.md`, стиль PSX и свет | да |
| `docs/gdd/superseded/` | задания `PLANE_V5…V12_UPGRADE`, заменённые `PLANE_V12_INTEGRATION_UNITY.md` | да |
| `docs/start/` | стартовые промпты `START_*.txt` для сессий | да |
| `docs/dev/` | журнал Unity-проекта: `ImplementationStatus.md`, `ProjectAudit.md`, `Controls.md`, `Decisions/`, `Tuning/`, `Audit/` | да |
| `docs/notes/` | заметки | да |
| `source/locations/<Карта>/` | входящие пакеты локаций: `*.unitypackage` (Core, Assets, Update…), карта, план, README | нет |
| `source/blend/` | `.blend` локаций с общими текстурами | нет |
| `source/plane/` | `UtilityPlane_Unity_v12` (текущая модель), `UtilityPlane_WinterKit_v1` и `_v1_2` (1.2 ставится поверх 1.0), `bushplane` | нет |
| `source/refs/` | картинки: сгенерированные, локации, референсы | нет |
| `tools/` | скрипты (`psx_texture_tool.py`) | да |

Не в git и генерируются сами: `Snow/Library`, `Snow/Temp`, `Snow/Logs`, `Snow/Builds`, `Snow/SkyIslandCaptures` (туда пишет `Tools → Sky Island → Capture Viewpoints`), `*.csproj`.

## Архив

Старые версии самолёта, резервные копии и устаревшие копии документов лежат **вне проекта**: `F:\ClaudeGames\snow_archive\`. Опись — в его `README.md`.

## Правила

- Новый пакет от генератора кладётся в `source/…`. Предыдущая версия, когда она больше не нужна, уходит в `F:\ClaudeGames\snow_archive\`.
- Документы — только в `docs/`. Копий в других местах не держать.
- Вместо ручных резервных копий — коммит в git перед большим изменением.
- Ассеты внутри `Snow/Assets` переносить только из Unity (или вместе с `.meta`), иначе ломаются ссылки по GUID.

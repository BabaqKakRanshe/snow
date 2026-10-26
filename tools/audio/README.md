# Звук Snow — конвейер

Звуки игры теперь сгенерированы ИИ (решение пользователя 26.10.2026: «убрать из игры все звуки, которые есть сейчас, и заменить на сгенерированные»; раньше, по `docs/gdd/QUESTIONS_4.md`, SFX брали только из бесплатных паков). Генерация — `tools/ai_audio/gen_sfx.py` (описания в `tools/ai_audio/sfx_prompts.json`, дубли в `F:\ClaudeGames\snow_audio_gen\sfx_lib`, корень `gen:` в манифесте); старый источник каждого звука сохранён в `src_pack`. Stable Audio: бесплатно при доходе до $1 млн в год; в Steam нужна пометка об ИИ-звуке. Что звучит и где — `docs/gdd/AUDIO_LIST.csv`, исследование — `docs/gdd/research/RESEARCH_AUDIO.md`, состояние — `docs/dev/ImplementationStatus.md`, раздел «Звук».

## Где что лежит

| Что | Где |
|---|---|
| Скачанные библиотеки (не в git) | `D:\Downloads\!Torrent\!SOUNDS` — использовать можно только Sonniss GDC, Nox Essentials (CC0), 99Sounds; остальные паки там пиратские |
| Индекс законных файлов | `F:\ClaudeGames\snow_audio_library\index.csv` |
| Нарезка из длинных записей | `F:\ClaudeGames\snow_audio_library\cuts\` (корень `cuts:` в манифесте): `letter\key_*` — удары клавиш Olivetti Linea 98, `letter\carriage_*` — ход каретки, `pager\beep_*` — писк (2496SoundEffects «Alarm Clock Electric Beeping»); режет `tools/audio/cut_letter_sounds.py` |
| Отобранное пользователем | `F:\ClaudeGames\snow_audio_library\package1_listen\NN_<категория>\` (удалённое не используем) |
| Сборка перед установкой | `F:\ClaudeGames\snow_audio_library\build\Audio` |
| Клипы в игре | `Snow/Assets/Snow/Audio/Clips/<группа>/<id>_NN.wav` + `sound_cues.json` |
| Библиотека звуков | `Snow/Assets/Snow/Audio/Resources/Audio/SnowSoundLibrary.asset` |
| ffmpeg | `C:\Users\user\tools\ffmpeg_dl\ffmpeg-master-latest-win64-gpl\bin` |

## Как добавить или заменить звук

1. Вписать звук в `audio_manifest.json`: `id` (группа по первому слову: `step`, `amb`, `os`, `forest`, `item`), `type` (`oneshot` — одиночный, `bed` — 2D-фон, `loop` — 3D-петля машины), `src` — файлы по шаблону `listen:` или `nox:`, `proc` — вырезка (`start`, `len`, `maxSec`), `set` — громкость, высота, дальность, шум для существ (только для нового звука).
2. `python tools/audio/build_audio.py --only <id>` — собрать (без `--only` — всё).
3. Редактор не в Play → `python tools/audio/build_audio.py --install --skip-build` — скопировать в Assets.
4. Меню Unity **Snow/Audio/Rebuild Sound Library** (или `return Snow.EditorTools.SoundLibraryBuildStep.Run();` через `tools/snow_qa/ue.py`). Новый звук берёт настройки из манифеста; у существующего меняются только клипы, правки из инспектора остаются.

Обработка: одиночные — обрезка тишины в начале, моно, 22 050 Гц, пик −3 дБ; фоны и петли — вырезка, громкость по LUFS, бесшовная петля через наложение хвоста на начало. Импорт в Unity (22 050 Гц, Vorbis, фоны потоком) держит `Snow/Editor/AudioImportRule.cs`.

## Как это звучит в игре

- `SnowAudio.Play(id, точка)` — только у себя; `SnowAudio.ServerPlay(id, точка)` — у всех, плюс шум для существ (`noise` звука).
- Шаги: `Footsteps` у каждого игрока; поверхность — слой террейна (`terrainLayers`) или слово в имени материала (`keywords`) в библиотеке; ничего не нашлось — `indoorDefault` / `outdoorDefault`. Метка `SurfaceTag` на объекте задаёт поверхность вручную.
- Фон леса: `Levels/Forest/ForestAmbience`.
- Пульт (`~`), страница «Звук»: громкость, поверхность под ногами и почему, последние звуки (и запрошенные, но отсутствующие), кнопка на каждый звук, слои фона леса.

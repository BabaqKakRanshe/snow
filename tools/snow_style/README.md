# snow_style — модели в стиле Snow локально в Blender

Здесь всё, чтобы строить модели на этом ПК так же, как их строит Blender-чат («3D модель Blender для Unity»).

| Что | Где |
|---|---|
| Стиль одним файлом (все числа и правила, действующие на 26.10) | `snow_style.json` |
| Код стиля — общая библиотека чата | `lib/` (psxlib, psxbake, gen_kit, gen_paint, gen_tex, pxfont, noise, van2_kit, van2_tex, van3_mech, bld_common, uvpack, unwrap_fast, post_bloom) |
| Пример пакета целиком | `lib/start_*.py` (forest_start: будка, машина, фонарь, покрышки, мусор) |
| Запуск в Blender без окна | `run_bpy.py` |
| scipy и Pillow для Python 3.13 из Blender | `pydeps/` (не в git, ставится командой ниже) |
| Откуда взяты правила | `docs/art/` (код, документы, переписка чата, исследование) |

## Установка (один раз)

```
"G:/Programms/Steam/steamapps/common/Blender/5.2/python/bin/python.exe" -m pip install --target tools/snow_style/pydeps scipy pillow
```
Потом удалить из `pydeps/` папки `numpy*`: Blender пользуется своим numpy.

## Запуск

```
blender -b --factory-startup --python tools/snow_style/run_bpy.py -- start_build.py --draft   # черновик: без запекания, цвета зон
blender -b --factory-startup --python tools/snow_style/run_bpy.py -- start_build.py           # полная сборка с атласами
```
Результат: `lib/out/*.blend`, `*_contract.json`, `*_lights.json`, `*.json`; текстуры `lib/tex/<pack>/`. Эти папки не в git.

## Проверка 26.10: локальная сборка = пакет чата

- forest_start собран здесь в Blender 5.2.3 за 58 с.
- Треугольники совпали до штуки: 7369 (будка 1202, машина 3740, фонарь 852, покрышки 896, мусор 679).
- Панель надписей и линз совпала пиксель в пиксель.
- Атласы: те же размеры, плотность (51,3 и 52,0 против 51,9) и 64 цвета; палитры отличаются в среднем на 3 из 255.
- Куски развёртки лежат в других местах атласа: упаковщик Blender 5.2 работает иначе, чем 5.0.1. Вид тот же.

## Как сделать новую модель

1. Прочитать `snow_style.json`: класс модели (`classes`), отделка «как в генераторной», запреты (`forbidden`).
2. Взять самый близкий пакет из `source/assets/<pack>/src` как шаблон. Скопировать его `<pack>_kit.py`, `_paint.py`, `_tex.py`, `_build.py` в `work/<новый>/`. Свой код писать через функции библиотеки (`gen_kit` KB, `psxbake`, `gen_paint`), рецепт PSX не переписывать.
3. Черновик (`--draft`) → геометрия → полная сборка → проверки (`snow_style.json#checks`) → виды и рендеры (`*_quick.py`, `*_render.py`) рядом с эталоном того же класса → контракт → пакет `source/assets/<pack>/` в том же формате, что у чата.
4. FBX для Unity делает сторона проекта (`tools/forest_start/export_fbx.py` как образец).

## Открытые вопросы

В `snow_style.json#open_questions`: мипмапы, эффекты кадра PS1, способ ореола ламп, коллайдеры, имена текстур, старые таблички, сжатие текстур, природа рядом с новыми постройками.

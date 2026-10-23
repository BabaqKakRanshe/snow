# Research A: как устроены директоры и пейсинг внутри (для Horror Director в Snow)

Дата: 2026-10-23. Базовые вещи (L4D Booth 2009, Alien two brains + menace, Phasmo hunt thresholds, Lethal power budget, Mr X, Bunker, SotF, Darkwood, VotV) здесь не повторяются.

Метки: **[проверено]** — сам открыл первоисточник (код/слайды/статья разработчика); **[вторичный]** — пресса, фан-вики, разбор третьего лица; **[не проверено]** — по памяти или из сниппета, первоисточник не открыт.

Главная находка: у **Vermintide 2 и Darktide есть публичные декомпилированные Lua-исходники** (репозитории Aussiemon). Там все числа директора видны прямо в коде. По глубине это лучший источник из найденных, ниже он разобран подробнее всего.

---

## 1. Dead Space remake (Motive, 2023) — Intensity Director

**Что это.** По словам Dan Kim (Senior Systems Designer), система «организации контента, спавна и контроля пейсинга». В ней «больше 1200 уникальных событий, смотря как считать» и огромное число комбинаций [проверено, EA blog, ист. 1].

**Механизм** [проверено, ист. 1]:
- Событие собирается из слоёв: звук, смена света, туман/пар, спавн врага. Готовые сценки складываются из «ингредиентов/рецептов». Аудиодиректор Olivier Asselin: на один исход, которого ждёт игрок, приходится 5–7 ингредиентов, которые могут увести в другую сторону. Пример: некроморф слышен в вентиляции → вентиляция ломается → спавн. Варьируя комбинацию, получают либо ожидание, либо jump scare.
- Director следит за интенсивностью игрока при переходах из комнаты в комнату (David Vincent).
- Отслеживает «пики и впадины напряжения», чтобы не ставить два high-intensity события подряд и давать передышки (Steven Ciciola). Пример эскалации: тихая комната с парой звуков → Slasher из вентиляции в стене → гаснет свет → туман.
- Часть «самых страшных» сцен, которые хвалили обзоры, сгенерировал Director, а не руки дизайнера (Eric Baptizat).

**Зачем** [вторичный, NME, ист. 2]: в ремейке убрали загрузки и разрывы глав, и игрок возвращается в пройденные зоны. В оригинале там всё было заскриптовано один раз. Типы событий: мигание или полное отключение света, видеоэффекты, звуки, встречи и **«фейковые встречи»** (трупы лежат, никто не спавнится). Цитируют «сотни сценариев» (Philippe Ducharme). Директор держит игрока на «кривой напряжения».

**Числа.** Кроме 1200+ событий и 5–7 ингредиентов публичных чисел нет: ни кулдаунов, ни порогов. GDC-доклада не нашёл.

**Для Snow:** событие = рецепт из слоёв (звук → свет → туман/снег → сущность или фейк), а не монолит. Дешёвые слои (звук, свет) переиспользуются в десятках рецептов. Фейковые встречи без спавна — штатная часть пула, а не исключение. Обязательное правило: два high-события не идут подряд.

---

## 2. Warhammer Vermintide 2 — Conflict Director (Fatshark) [проверено, декомпилированный код]

Файлы: `scripts/settings/conflict_settings.lua`, `scripts/managers/conflict_director/{pacing,peak_delayer,conflict_director,conflict_utils,level_analysis}.lua` (ист. 3–6).

### 2.1 Интенсивность игрока (`IntensitySettings.default`)
| параметр | значение (normal) | hardest / cataclysm |
|---|---|---|
| max_intensity | 100 | 100 |
| decay_delay (после последнего прироста) | 3 с | 3 с |
| decay_per_second | 2 | 4 / 6 |
| нокдаун | +50 | +50 |
| «pounced» (схватил спец) | +10 | +10 |
| за 1% полученного урона | +1 | +0.5 / +0.2 |
| убийство рядом | +1/дистанция | — |

На высоких сложностях интенсивность растёт **медленнее** и быстрее спадает, поэтому директор реже уходит в relax. Сложность здесь — частота пиков, а не только сила врагов.

### 2.2 Фазы (`pacing.lua`, `PacingSettings`)
Конечный автомат: `build_up → sustain_peak → peak_fade → relax → build_up`.
- **Интенсивность группы = среднее по живым игрокам** (`total_intensity = sum/num_alive`). В L4D, по Booth, — максимум по игрокам. Это существенная разница.
- build_up → sustain_peak, когда среднее > `peak_intensity_threshold`: **45** (normal), 50 hard, 55 harder, 70 hardest, 80–90 cataclysm.
- sustain_peak длится **3–5 с**, затем peak_fade. В peak_fade threat/specials/horde population = 0: новых нет.
- peak_fade → relax, когда среднее < **32.5**.
- relax: **50–70 с** (normal), 35–45 hard/harder, 25–35 hardest+. Выход раньше, если интенсивность упала до 0 (`leave_relax_if_zero_intensity`). Если игроки рвутся вперёд на 70 м (`relax_rushing_distance`), relax тоже прерывается.
- При выходе из relax спешалы задерживаются ещё на 5–10 с (`delay_spawning(t, 10, random(5,10))`).

### 2.3 Таймеры орд
- `horde_frequency` 70–150 с (normal); 60–110 hard; 50–100 harder и выше.
- `multiple_hordes` = 3 волны с интервалом 10–15 с (normal) … 7–10 с (hardest).
- `max_delay_until_next_horde` 160–200 с: жёсткий потолок тишины.
- `horde_startup_time` 40–120 с от старта уровня.
- Орда **откладывается**, если суммарная «угроза» живых врагов выше порога: `delay_horde_threat_value` 40 normal / 50 hard / 60 hardest / 80–100 cataclysm. Спешалы — те же 40/50/60 (на cataclysm 1000, то есть не откладываются).
- Мини-патруль только при интенсивности **1 < I < 15**, частота 15–20 с. Это фон, который живёт в спокойные моменты.

### 2.4 Спешалы (`SpecialsSettings`, метод `specials_by_slots`)
- Слоты: `max_specials` **2** normal, 3 hard/harder, 4 hardest, 5 cataclysm.
- Кулдаун слота **60–90 с** (normal), 60–100 hard, 50–90 harder+.
- `max_of_same` = 1 на normal (2 выше): одновременно не бывает двух одинаковых спешалов.
- `after_safe_zone_delay` 30–60 с: после выхода из safe-зоны спешалы молчат.
- Шанс скоординированной атаки 0 / 0.25 / 0.5 по сложностям.

### 2.5 «Одиночество» и наказание за отрыв (rush intervention)
Формула (`ConflictUtils.cluster_weight_and_loneliness`): для каждого игрока i считается `loneliness_i = sqrt(Σ_j d_ij²) / N`, где N ≤ 4. Самый одинокий — максимум. Если игрок один, функция возвращает 100.
- Спешал на отрывающегося: loneliness > **30**. Засадная орда: > **25** с шансом 0.25.
- Срабатывает только если «ведущий» по main path и есть самый одинокий (или игроков всего 2). Между интервенциями 20 с (+10 с после орды) и минимум 30 м пути. При 1 живом игроке интервенции выключены.
- Пример расчёта: 4 игрока, трое стоят вместе, четвёртый в 70 м → sqrt(3·70²)/4 ≈ 30.3 → спешал. Пара игроков: порог 30 = разрыв > 60 м.
- Отдельная `speed_running_intervention`: если за 10 с пройдено > 40 м и 30 с держится high threat, спавнятся спешалы с задержкой 15–30 → 5–12 с по мере роста пройденной дистанции (пороги 80/160/240/320 м).

### 2.6 Боссы и редкие события раскладываются заранее по пути (`level_analysis.lua`)
- Боссовые события ставятся **каждые 300 м** main path (`recurring_distance`). Первые 150 м от старта безопасны, между событиями минимум 100 м.
- Каждый слот выбирает из `{event_boss, event_patrol, nothing}`. **Подряд одна и та же категория не выпадает.** Максимум 2 босса на уровень.
- Редкие события (loot rat) — каждые 1500 м, safe 50 м.

### 2.7 PeakDelayer: авторские пики против генератора (`peak_delayer.lua`)
Дизайнер размечает на пути «пики» (авторские crescendo, длина 30 м). Если до следующего пика меньше **100 м**, генеративный пейсинг ставится на паузу, максимум на **100 с**. Генератор не тратит игрока перед ручной сценой.

### 2.8 Сегменты по фракциям [вторичный, патчноты 1.04, ист. 7]
Уровень при каждом запуске делится на сегменты, где спавнятся в основном Skaven, Chaos или смесь.

**Для Snow:** VT2 даёт формулу «одинокого игрока» и пороги — это прямой триггер для Холодного. Отсюда же заранее раскатанное расписание с пустыми слотами и запретом повторов, пауза генератора перед авторской сценой, откладывание события при высокой угрозе и разделение на слоты с `max_of_same`.

---

## 3. Warhammer 40K: Darktide — Pacing Manager (Fatshark) [проверено, декомпилированный код]

Файлы: `scripts/managers/pacing/pacing_manager.lua`, `templates/default_pacing_template.lua`, `templates/expedition_pacing_template.lua`, `monster_pacing/templates/default_monster_pacing_template.lua`, `heat_pacing/heat_pacing.lua` (ист. 8–10). Это эволюция директора VT2.

### 3.1 Шесть состояний и белые списки категорий
Tension от 0 до 100 (×1 / 1.2 / 1.35 / 1.5 / 1.75 по сложностям). Пороги для сложности 1:
| состояние | переход дальше | откат назад | спад tension/с | что разрешено спавнить |
|---|---|---|---|---|
| build_up_tension_low | ≥10 | — | 1.25 (задержка 3 с) | всё |
| build_up_tension | ≥60 | ≤5 | 1.5 (задержка 1 с) | всё |
| build_up_tension_high | ≥90 | ≤5 | 2 | всё |
| sustain_tension_peak | через 12–15 с | — | **0** | **только monsters** |
| tension_peak_fade | ≤60 и 30–35 с | — | 2 | только monsters |
| relax | ≤5 или 65–100 с | — | 2 | monsters, roamers, terror_events (без орд и спешалов) |

Главная идея: **у каждого состояния свой белый список категорий спавна**. В relax фон (roamers) и сюжетные terror events разрешены, давление (орды, спешалы) — нет.

### 3.2 Бюджет угрозы (challenge rating)
Суммарный challenge rating живых врагов блокирует категорию, если превышает порог (×1…×2 по сложностям): specials > **40**, hordes > **30**, trickle_hordes > 20, roamers > 90, terror_events > 100. Это тот же «power budget», что в Lethal, только динамический и **разный для каждой категории**.

### 3.3 Рампа со временем
`ramp_up_frequency_modifiers`: пока группа сидит в build-up, частоты растут. На сложности 1 за 1200 с: орды ×1.25, спешалы ×2. На сложности 5 за 200 с: орды ×2, спешалы ×3, terror ×2. Рампа сбрасывается, когда кто-то умер или лёг со второй раны (`wait_for_ramp_clear`). Это защита от «пересидеть».

### 3.4 Милосердие после смерти
`player_died`: если после смерти живых ≤3 (таблица `MAX_OUT_TENSION_BY_CHALLENGE` = {true,true,true,false}), tension ставится в **максимум**. Автомат проходит peak → fade → relax, и выжившие получают передышку.

### 3.5 Монстр глушит остальное
`pause_pacing_on_spawn`: при спавне монстра (plague ogryn и т.п.) орды ставятся на паузу на **60 с**, спешалы на **50 с**, trickle-орды на **40 с** (на средних сложностях 30/20/20). Monsters при этом разрешены даже в relax: босс — отдельный канал.

### 3.6 «Heat» в режиме Expedition (свежий режим в актуальной ветке)
- Heat копится от действий группы и пишется ещё и по каждому игроку отдельно (`record_heat_generation_per_player`). «Возможности»: low +5, medium +10, high +15, escape_event +35. Спад 0.05/с, задержка 3 с.
- Стадии `none → undetected → alert → detected → max` на порогах 0, ¼, ½, ¾ от max_heat. У каждой стадии свои разрешённые спавны, множители таймеров орд и минимальное время на стадии; есть флаг `allowed_to_drop_stage`.
- При heat > 20 шанс «впрыснуть» спешала: 0 / 0 / 0.3 / 0.5 / 0.6 по сложностям.

**Для Snow:** таблица «состояние → разрешённые категории» почти дословно ложится на наши категории (наблюдатели, воры, сущности-события, Холодный). Ещё переносятся раздельный бюджет на категорию, max-tension после смерти, пауза остальных при активации Холодного и heat от «шумных/жадных» действий по стадиям.

---

## 4. Left 4 Dead 2 — скрипты директора, финалы; Back 4 Blood

### 4.1 Официальные VScript-файлы [проверено, декомпиляция официальных .nut, ист. 11]
- **director_onslaught.nut**: MobSpawnMin/MaxTime 8/8 с, MobMin/MaxSize 20/30, SustainPeak 1–3 с, IntensityRelaxThreshold 0.90, Relax 5/5 с, RelaxMaxFlowTravel 600, без боссов. Это «crescendo»-режим.
- **director_gauntlet.nut** (забег по мосту): PanicForever, `PausePanicWhenRelaxing`, IntensityRelaxThreshold 0.99, Relax 25–35 с, RelaxMaxFlowTravel 400, SpecialRespawnInterval 20, моб каждые 5 с. Размер моба линейно растёт с прогрессом по мосту длиной 20000 юнитов: 5→20, плюс до +15 за **скорость** игроков (50→200 ед/с). CommonLimit = 1.5×моб, потолок 30. Таймер движения: если за 5 с пройдено < 500 юнитов, начисляется бонус 2 (максимум 30).
- **director_c4_storm.nut** (шторм в Hard Rain): ZombieSpawnInFog = 1, ZombieSpawnRange 3000, MobRechargeRate 0.001, FallenSurvivor 6 шт. с шансом 0.75. **Погода — это просто другой набор DirectorOptions.**
- **director_quiet.nut**: без боссов, SpecialRespawnInterval 999999, CommonLimit 0. Скрипт тишины.
- **c2m5_concert_finale.nut**: финал как список стадий: PANIC 1 → PANIC 1 → DELAY 15 → TANK → DELAY 15 → PANIC 2 → DELAY 10 → TANK → DELAY 5–10. У каждой стадии свои опции (PanicOptions CommonLimit 25; TankOptions: спешалы с танком разрешены, SpecialRespawnInterval 30).
- **director_base.nut** (update): `MaxSpecials = 2`. Классический финал = `HORDE_ATTACK_1 → HALFTIME_BOSS → HORDE_ATTACK_2 → FINAL_BOSS → HORDE_ESCAPE`, у gauntlet-финала свои стадии.

### 4.2 Значения по умолчанию
- [вторичный, commands.gg, ист. 12]: z_mob_spawn_min/max_interval_expert 90/180 с; z_mob_spawn_min/max_size 10/30; z_special_spawn_interval 45 с; z_mega_mob_spawn_min/max_interval 420/900 с; z_mega_mob_size 50.
- [не проверено, по памяти о Valve wiki, сайт закрыт для бота]: IntensityRelaxThreshold 0.9; RelaxMin/MaxInterval 30/45 с; RelaxMaxFlowTravel 3000; SustainPeakMin/MaxTime 3/5 с; BuildUpMinInterval 15 с.

### 4.3 Back 4 Blood [вторичный, ист. 13]
Game Director разыгрывает против игроков Corruption cards: доп. враги, туман, размер орды. Игроки собирают свою колоду в начале уровня. Classic mode убирает карты как «более доступный» вариант. Публичного разбора, как директор выбирает карты и сколько тянет по сложностям, не нашёл. Фан-источники говорят «тянет в начале каждого уровня, пул больше на Nightmare».

**Для Snow:** «режим директора» удобно делать подменяемым набором опций, а не кодом: Quiet, Storm (метель), Onslaught (финал ночи), Gauntlet (бег к базе). Скорость движения можно учитывать как вход: в Gauntlet быстрые получают **больше** врагов. Финальную ночь или эвакуацию стоит писать списком стадий PANIC/DELAY/BOSS. Корраптор-карты в виде «модификаторов ночи», вытянутых на закате и показанных игрокам, — [предложение].

---

## 5. Lethal Company (vanilla) и R.E.P.O.

### 5.1 Lethal Company — алгоритм спавна
[вторичный: вики Miraheze, описывает декомпилированный код, ист. 14–16; README мода LethalQuantities, ист. 17]
- Цикл спавна: внутренний — один раз на старте раунда, дальше **каждые 2 часа** игрового времени (кулдаун по умолчанию). Час = 60 ед., 18 часов в дне. Кривые сэмплируются по `currentHour`, то есть примерно на час позже фактического времени (вероятно, баг).
- **Сколько спавнить внутри за волну:** база = кривая `enemySpawnChanceThroughoutDay(t)`. Min = (кривая − 1) + |daysUntilDeadline − 3| / 1.6 − deviation, max = (кривая − 1) + deviation, clamp [minEnemiesToSpawn, 20] и не больше числа свободных вентиляций. Ближе к дедлайну минимум растёт: при 0 дней до сдачи +1.875.
- **Снаружи:** min = кривая + |daysUntilDeadline − 3| / 1.6 − 3, max = кривая + 3 (deviation захардкожен = 3), clamp [minOutside, 20]. Спавн мгновенный.
- **Кого:** вес = rarity × curve(t) × falloff(кол-во уже заспавненных этого типа / 10). Вероятность = вес / Σвесов. Перед этим отсев по max count, power cap, diversity cap и «latest spawn time».
- **Power cap:** категория закрывается, когда сумма power достигает максимума луны (равенство допускается). Пример Experimentation: inside 4, outside 8, daytime 5; deviation 4/3/5 [вторичный, ист. 15].
- **Diversity cap:** после N видов за день новые виды не появляются, только копии существующих. Убийства счётчик не уменьшают.
- **Вентиляции:** каждому выбранному врагу назначается свободная вентиляция и задержка внутри окна до следующего цикла. Ползающий звук нарастает до открытия. Это встроенный телеграф.
- **Резиновая лента против «слишком легко»:** minEnemiesToSpawn = 0 по умолчанию. С шансом 70% становится 2 после взятия Apparatus; затмение задаёт свои значения. Если день прошёл, а минимум всё ещё 0, он становится 1 при любом из условий: собран лом ≥ квоты после 11:24; собрано > 65% лома карты; 5 раундов (не подряд) с > 30 кредитов и без смертей. Смерть сбрасывает этот счётчик.
- **Power level врагов** (vanilla): ½ — Gunkfish, Baboon Hawk, Tulip Snake; 1 — Snare Flea, Hoarding Bug, Hygrodere, Spore Lizard, Coil-Head, Nutcracker, Masked, Barber; 2 — Bunker Spider, Butler, Ghost Girl, Maneater, Hornets, Eyeless Dog, Leviathan; 3 — Bracken, Thumper, Jester, Forest Keeper, Old Bird; 4 — Giant Sapsucker [вторичный, ист. 16].
- Веса внутри Experimentation: Spider 58, Snare Flea 51, Hygrodere 31, Spore Lizard 28, Hoarding Bug 28, Thumper 16, Bracken 13 (из 229) [вторичный, ист. 15].

### 5.2 R.E.P.O. (2025) [вторичный, противоречиво, ист. 18–19]
EnemyDirector выбирает врагов по трём тирам (difficulty 1–3) в фиксированные слоты по номеру уровня. Внутри тира шансы равны. Счёт по уровням в источниках расходится: «по одному из каждого тира на уровнях 3–5, удвоение на 6–8» против «уровень 1: один тир-1 и один тир-3». Кода не нашёл. Доверять нельзя, пока не проверено по декомпиляции.

**Для Snow:** готовые правила: «сначала сколько, потом кто», вес = rarity × кривая ночи × falloff, отдельный diversity cap «не больше K видов сущностей за ночь», телеграф за N секунд до появления (как ползание в вентиляции), резиновая лента «если 3 ночи подряд без потерь → минимум +1 событие». Есть перекличка с нашей структурой день/ночь: Lethal привязывает давление к дедлайну через |days − 3| / 1.6.

---

## 6. Alien: Isolation — глубже [вторичный: Tommy Thompson разбирал игру через Alien Config Editor / OpenCAGE, ист. 20]

- Параметры menace gauge в конфиге: **кулдаун до возвращения** чужого, **время до пика** шкалы перед командой «уйти», **максимум «угроз» за один frontstage-заход** (alien считает, сколько раз уже прессовал игрока).
- Шкала растёт с разной скоростью: alien рядом / игрок его слышит / игрок его видит.
- Параметры backstage: сколько длится прочёсывание, как далеко оно уходит, сколько alien простаивает в вентиляции перед следующим. Громкий шум выдёргивает его из backstage.
- Frontstage-прочёсывание ограничено регионом вокруг вентиляции, из которой он вышел. Радиус stalk **сужается с каждым проходом**. Зоны бывают «кругом» или «бубликом» (donut, чтобы не стоять вплотную к рубильникам-целям).
- В Alien Config Editor **≥12 конфигураций** (по участкам кампании; например, Seegson Communications: «только вентиляции, ловушки, не спускается»). В корне behaviour tree **36 веток**.
- Разблокировка поведений: обыск шкафчиков и заползание в мелкие вентиляции включаются только после того, как игрок **несколько раз** ушёл этими способами. Огнемёт alien запоминает и меняет тактику: шипит, устраивает засаду из вентиляции, заходит с фланга.
- Сложность: Novice снижает зрение и реакцию на шаги больше чем на треть, на выстрелы — сильно. На высоких меньше времени в backstage, **дольше** окно menace, меньше регион патруля вокруг игрока.
- Конкретных чисел (секунды кулдауна, пороги) в открытых источниках не нашёл, в доклад Andy Bray (nucl.ai 2016) они не попали. Числа есть только в самих конфигах игры через OpenCAGE [не проверено].

**Для Snow:** для Холодного три параметра — кулдаун возврата, время до пика, максимум «угроз» за заход. Поиск сужается кругами, авторские конфиги по участкам леса, поведение открывается по счётчику «каким способом игроки уходили».

---

## 7. Хоррор-игры с явными шкалами

### 7.1 Phasmophobia — активность призрака (не hunt) [вторичный: разбор кода 2020, автор пишет, что частично устарело, ист. 21]
- Ghost activity стартует с 0. Растёт на +10…25 при распознанной фразе и на +20…30 от смадж-палочки в радиусе 6 м. **Спадает на dt/2 каждый кадр**, то есть примерно на 0.5 в секунду.
- «Random ability value» по сложностям: 100 / 115 / 130. Действие происходит, если `rand(0..value) ≤ activity + мультипликаторы`. В **соло +15%**. Множители: Oni +30, если в комнате люди; Wraith +50 после соли.
- Idle-таймер 2–6 с, после него с шансом 1/2 попытка охоты: при 25 < (sanity + mult) < 50 шанс 1/6, при ≤ 25 шанс 1/4. Иначе 1/2 на взаимодействие, способность или wander.
- Взаимодействия 5/12. Ghost event 10/15 (10/13 на высоких). Варианты события: появиться, погасить свет, хлопнуть дверью. Состояние «способность» 4/12.
- Generic reaction: 1/200 выключить щиток, 1/8 предмет, 1/8 дверь.
- Sanity: дренаж 0.2/с (Phantom 0.4), если призрак виден или охотится в ≤10 м. **В свете и вне дома не дренится.** Синхронизация между игроками раз в 5 с. Для решений берётся средняя sanity команды.
- Hunt cooldown ≥25 с (Demon 20) [вторичный, Phasmo wiki].

### 7.2 Silent Hill: Shattered Memories — психопрофиль [вторичный, ист. 22–23]
Профиль собирается в сессиях с психиатром (true/false-вопросы и т.п.). Дальше игра пишет, куда игрок смотрит, какие маршруты выбирает, сколько решает загадки. Меняются локации, одежда и отношение персонажей, концовка, облик монстров (изначально безликие манекены). По словам Barlow, команда взяла пятифакторную модель (Big Five) и выкинула один фактор, потому что в играх «почти все ведут себя немного как психопаты». Подробной таблицы профиля в открытом доступе нет.

### 7.3 Devour [вторичный, ист. 24]
Эскалация от прогресса цели: с каждым сожжённым козлом (из 10) одержимая быстрее, агрессивнее и спавнит больше демонов. Раскладка козлов и запертых дверей рандомная. Числа по стадиям не нашёл.

### 7.4 Hello Neighbor [вторичный, ист. 25]
По фан-вики сосед ставит медвежьи капканы там, где игрок ходит чаще, а камеры и датчики — где капканы не работают, и запирает двери после визитов. Разработчики не описывали это публично. В превью 2016 года сказано, что на деле «рутин немного».

**Для Snow:** соло-бонус к активности (+15%), свет защищает от дренажа, средняя sanity команды для решений, эскалация от прогресса цели (Devour, Darktide heat), «память о маршрутах» для воров и наблюдателей (Hello Neighbor).

---

## 8. Академия и индустрия: drama manager, биофидбэк, кривые напряжения

### 8.1 Façade (Mateas & Stern) — beat sequencing по кривой [проверено, AIIDE 2005, ист. 26]
- **27 битов**, за прохождение игрок видит около 15. Около 2500 joint dialog behaviors, один бит = 10–100 jdb. Одно прохождение показывает ≤25% контента.
- Каждый бит размечен предусловиями, весами, приоритетами и **эффектом на story tension**. Секвенсор выбирает **неиспользованный бит, чьи предусловия выполнены и чей эффект на tension лучше всего совпадает с ближайшей траекторией авторской кривой** (аристотелевская арка).
- Вывод авторов: из-за дороговизны контента вышло всего 27 битов, и глобальная агентность оказалась ниже ожидаемой.

### 8.2 Sonancia (Lopes, Liapis, Yannakakis) — хоррор по кривой напряжения [проверено, AIIDE 2015, ист. 27]
- Дизайнер задаёт желаемую кривую (DTC) по комнатам критического пути. Уровень даёт фактическую (LTC): **+1 за комнату с монстром, −0.5 за пустую**. Фитнес: `Σ(1 − |L_i − T_i|)`.
- **Кривая саспенса = инвертированная LTC**: напряжённый звук ставится туда, где монстров **нет** (ожидание перед встречей).
- Около 50 звуков по ~4 с с тегом «suspense value», который измеряли на людях. Против повторов: вероятность инструмента **делится пополам** каждый раз, когда его выбирают.

### 8.3 Valve: биофидбэк в L4D2 (Mike Ambinder, GDC 2011) [проверено, слайды, ист. 28]
- Сформулированный алгоритм директора: интенсивность выжившего — одно число, растёт от «травмы», спадает к нулю, создаёт пики и впадины.
- Эксперимент: «оценённое» возбуждение заменили на **измеренное** (проводимость кожи, SCL). Результат на слайдах: «measured arousal produces greater enjoyment than estimated arousal». Цифра «в 2 раза веселее» — только в пересказе PC Gamer [вторичный, ист. 29], на слайдах её нет.
- Alien Swarm: таймер «100 убийств за 240 с» ускорялся от возбуждения. Найденная проблема: **положительная обратная связь** (возбуждение → давление → больше возбуждения). «Decay factor helps.»
- Мультиплеер: показывать тиммейтам чужие физиосигналы — «самое весёлое, что мы делали», но «пока не полезно».

### 8.4 Nevermind (Flying Mollusk, Erin Reynolds) [вторичный, ист. 30–31]
HRV с нагрудного датчика. Чем дольше игрок в стрессе, тем страшнее и сложнее мир; успокоился — сложность возвращается к базовой. Это **обратный** контур: игра учит успокаиваться. Ограничение по словам автора: arousal не даёт валентности, страх не отличить от азарта.

### 8.5 Experience management (Riedl и др.) [не проверено]
«Experience manager» — агент, который следит за состоянием игрока и мира и правит будущие события, чтобы держать задуманную траекторию опыта (Riedl & Bulitko, AI Magazine 2013; PaSSAGE, Thue 2007 — выбор событий по модели типа игрока). Первоисточники не открывал.

**Для Snow:** выбор события = «неиспользованное, предусловия выполнены, эффект на напряжение ближе всего к целевой кривой ночи» (Façade). Звуковое предчувствие ставится там, где угрозы нет (Sonancia). Анти-повтор: вероятность ×0.5 после каждого выбора. У любого контура «страх → давление» нужен спад, иначе положительная обратная связь (Valve).

---

## 9. Синтез: что брать в Snow (с числами-кандидатами) [предложение]

1. **Два слоя.** (а) Групповой автомат фаз в стиле Darktide (low → build → high → peak → fade → relax), у каждой фазы белый список категорий: наблюдатели, воры, сущности-события, Холодный. (б) Личные шкалы по игрокам (VT2), они нужны для выбора цели. Для фазы брать **среднее** (VT2) или **максимум** (L4D): если игроки разошлись, max даёт перекос в одного. Решение — это открытый вопрос.
2. **Триггер Холодного = одиночество.** Loneliness по VT2 `sqrt(Σd²)/N`, кластер 10 м. Кандидатные пороги: пара — разрыв > 60 м; четверо — loneliness > 30. Плюс фаза ≥ build_up_high, игрок вне базы и вне света, кулдаун возврата (Alien). Соло-игра — отдельный режим, у VT2 тут интервенции выключены.
3. **После утаскивания или смерти tension ставится в максимум**, дальше принудительный relax (Darktide). **На время активности Холодного** остальные категории на паузе 40–60 с (Darktide `pause_pacing_on_spawn`).
4. **Ночь раскатывается заранее** (VT2 boss events): слоты каждые N минут, первые M минут после выхода из базы безопасны, в каждом слоте {событие, наблюдатель, ничего}, подряд одно и то же не выпадает, максимум K тяжёлых событий за ночь.
5. **Бюджет по категориям** (Darktide challenge rating + Lethal power): у каждой сущности есть стоимость, у каждой категории свой потолок. Плюс diversity cap «≤K видов за ночь».
6. **Событие = рецепт** (Dead Space): звук → свет → снег/туман → сущность или фейк. Выбор по Façade, анти-повтор ×0.5 (Sonancia). Звуковой саспенс ставится до угрозы, а не на ней.
7. **Против пересиживания и мастерства:** рампа частот за время без потерь (Darktide: ×2 спешалы за 1200 с на лёгкой); Lethal-правило «серия безопасных ночей → минимум +1 событие»; Gauntlet-правило о скорости и стоянии на месте.
8. **Авторские сцены главнее генератора** (PeakDelayer): за 100 м или 100 с до ручного пика генератор молчит.
9. **Режимы как наборы опций** (L4D2 Quiet, Storm, Onslaught, Gauntlet). Метель = другой профиль директора, а не новый код.
10. **Мелочи из Phasmo:** соло +15% к активности сущностей, свет останавливает дренаж страха, для решений берётся среднее значение команды, синхронизация шкал раз в несколько секунд (у нас всё равно host-authoritative).

---

## Источники

1. EA / Motive, «Inside Dead Space #4: The Intensity Director» — https://www.ea.com/technology/news/inside-dead-space-4-the-intensity-director
2. NME, «Dead Space remake has an AI director with "hundreds" of random scares» — https://www.nme.com/news/dead-space-remake-has-an-ai-director-with-hundreds-of-random-scares-3329023
3. Vermintide 2 decompiled, conflict_settings.lua — https://github.com/Aussiemon/Vermintide-2-Source-Code/blob/master/scripts/settings/conflict_settings.lua
4. Vermintide 2 decompiled, pacing.lua / peak_delayer.lua — https://github.com/Aussiemon/Vermintide-2-Source-Code/tree/master/scripts/managers/conflict_director
5. Vermintide 2 decompiled, conflict_utils.lua (loneliness) — https://github.com/Aussiemon/Vermintide-2-Source-Code/blob/master/scripts/managers/conflict_director/conflict_utils.lua
6. Vermintide 2 decompiled, level_analysis.lua (boss events) — https://github.com/Aussiemon/Vermintide-2-Source-Code/blob/master/scripts/managers/conflict_director/level_analysis.lua
7. Vermintide 2 patch 1.04 notes (GameWatcher) — https://www.gamewatcher.com/2018-20-03-warhammer-vermintide-2-patch-notes-patch-1-04-released
8. Darktide decompiled, pacing_manager.lua — https://github.com/Aussiemon/Darktide-Source-Code/blob/master/scripts/managers/pacing/pacing_manager.lua
9. Darktide decompiled, default/expedition pacing templates — https://github.com/Aussiemon/Darktide-Source-Code/tree/master/scripts/managers/pacing/templates
10. Darktide decompiled, monster pacing template — https://github.com/Aussiemon/Darktide-Source-Code/blob/master/scripts/managers/pacing/monster_pacing/templates/default_monster_pacing_template.lua
11. L4D2 official VScripts decompiled (director_onslaught/gauntlet/quiet/c4_storm, c2m5_concert_finale, director_base) — https://github.com/fbef0102/Official-Vscripts-Decompiled
12. commands.gg, L4D2 cvars — https://commands.gg/l4d2
13. Wikipedia, Back 4 Blood — https://en.wikipedia.org/wiki/Back_4_Blood
14. Lethal Company Wiki (Miraheze), Mechanics: Entity Spawning — https://lethal.miraheze.org/wiki/Mechanics
15. Lethal Company Wiki (Miraheze), Experimentation — https://lethal.miraheze.org/wiki/Experimentation
16. Lethal Company Wiki (Miraheze), Entities — https://lethal.miraheze.org/wiki/Entities
17. LethalQuantities README (описание vanilla-логики) — https://github.com/BananaPuncher714/LethalQuantities
18. R.E.P.O. spawn tiers (Substack) — https://haplogamingcook.substack.com/p/repo-monster-spawn-rates-and-tiers
19. TheUncatalogued mod (описание слотов EnemyDirector) — https://thunderstore.io/c/repo/p/Alanspike/TheUncatalogued/
20. Tommy Thompson, «Revisiting the AI of Alien: Isolation» — https://www.gamedeveloper.com/design/revisiting-the-ai-of-alien-isolation
21. azsry, Phasmophobia mechanics (разбор кода, 2020) — https://github.com/azsry/phasmophobia_mechanics/blob/master/versions/stale/README.md
22. Siliconera, Shattered Memories questions — https://www.siliconera.com/silent-hill-shattered-memories-initially-sets-scares-based-on-these-questions/
23. TCRF, Development: Silent Hill: Shattered Memories — https://tcrf.net/Development:Silent_Hill:_Shattered_Memories (содержимое через поисковый сниппет; при прямом открытии страница вернула мусор)
24. Devour, Epic Store / TechRadar — https://store.epicgames.com/en-US/p/devour-d62a36 ; https://www.techradar.com/gaming/pc-gaming/the-scariest-part-of-this-horror-game-isnt-the-demons-its-the-micromanaging
25. Hello Neighbor hands-on (GamePressure) — https://www.gamepressure.com/editorials/hello-neighbor-hands-on-breaking-entering-and-fighting-crafty-ai/z7f1
26. Mateas & Stern, «Structuring Content in the Façade Interactive Drama Architecture», AIIDE 2005 — https://cdn.aaai.org/AIIDE/2005/AIIDE05-016.pdf
27. Lopes, Liapis, Yannakakis, «Targeting Horror via Level and Soundscape Generation», AIIDE 2015 — https://cdn.aaai.org/ojs/12778/12778-52-16295-1-2-20201228.pdf
28. Ambinder, «Biofeedback in Gameplay», GDC 2011 (слайды Valve) — https://cdn.cloudflare.steamstatic.com/apps/valve/2011/ValveBiofeedback-Ambinder.pdf
29. PC Gamer о докладе Ambinder — https://www.pcgamer.com/2011/03/03/breaking-watch-portal-2-eye-tracking-now/
30. Reynolds, Nevermind, SIGGRAPH 2013 poster — https://history.siggraph.org/wp-content/uploads/2023/01/2013-Poster-75-Reynolds_Nevermind-Creating-an-Entertaining-Biofeedback-Enhanced-Game-Experience-to-Train-Users-in-Stress-Management.pdf
31. Reynolds, Affectiva guest blog — https://blog.affectiva.com/finding-the-essence-of-fear-in-nevermind

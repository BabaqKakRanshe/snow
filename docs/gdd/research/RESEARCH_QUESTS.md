# SNOW — исследование референсов: система заданий

Дата: 2026-10-22. Три направления: (A) задания без маркеров и кооп-цели, (B) рутина как каркас хоррора, (C) техническая архитектура. Источники — в конце каждой части, ссылки в тексте как [A3], [B14], [C1].

Пометки:
- **[проверено]** — открыт первоисточник: доклад, блог разработчика, официальная документация, пресс-кит;
- **[вторичный]** — Википедия, пресса, фан-вики, пересказ;
- **[не проверено]** — по памяти, поиском не подтверждено.

Записей докладов GDC по Outer Wilds (2020) и Firewatch (2017) в открытом доступе нет: подтверждены только анонсы. Fandom-вики (Subnautica, Phasmophobia), UESP, Valve Developer Community и часть wiki.gg отдавали 401/402/403 — сведения по ним из выдачи поиска.

Вопросы к разработчику по итогам — `docs\gdd\QUESTIONS_3.md`.

---

## 0. Короткая выжимка

| Тема | Главная мысль |
|---|---|
| Без маркеров | Отключить маркеры можно, только если текст задания написан под их отсутствие: **симптом + цель + срок**, направление от ориентира. Подсказчик знает только то, что может знать в мире (Делайла видит со своей вышки). Метки на карте ставит сам игрок. |
| Чтобы не терялись | Ориентиры с уникальным силуэтом и светом; промежуточная находка между базой и целью (BotW); журнал, который хранит всю переписку; лестница подсказок по таймеру бездействия. Главный провал жанра — сообщения редеют к середине игры (Subnautica). |
| Засчитано | Статус «выполнено» должен быть однозначным и отдельным сигналом. В VotV игроки путают, какое письмо засчитывает задание. |
| Кооп | Цель одна на команду и висит в одном физическом месте базы (доска Phasmophobia, терминал Lethal Company). Сделал один — засчитано всем. Поздний вход — только на базе. |
| Рутина и страх | Рутина должна успеть сложиться: 2–3 одинаковых повтора, потом тихое отклонение одной детали. Рутина — это таймер (генератор Amnesia: The Bunker) и повод отправить игрока в опасность. |
| Лживый заказчик | Обман честен, если его можно проверить внутри мира (номер факса, подпись, время, журнал). Вредный приказ — выбор с ценой, а не ловушка. |
| Архитектура | Не граф квестов, а **база фактов** (имя → число, незаписанное = 0) + задания как условия над фактами. Факты «живые» и «защёлки» — порядок шагов не важен, сделанное до выдачи засчитывается само. Режиссёр читает те же факты тем же языком условий. Состояние — только на хосте. |

---

## A. Задания без маркеров и кооп-цели

### A1. Subnautica / Below Zero
- Классических миссий нет намеренно: Кливленд хотел, чтобы интересна была сама деятельность [вторичный, A1].
- Радио в капсуле сначала надо **починить** — только потом приходят сигналы бедствия, они ведут ранний сюжет. Часть сигналов срабатывает от событий: подобрал PDA, дошёл до биома [вторичный, A2].
- Оговорка: сигнал по радио ставит метку на HUD. Маркеры есть, но игрок получает их через мир и только о том, что узнал. Свои точки ставит маяками [вторичный].
- Где буксует: к середине сообщения редеют, игроки не понимают, куда дальше [вторичный, A2]. В Subnautica 2 обратная жалоба — маркер на каждом шаге убивает открытие [вторичный, A3].
- Below Zero: говорящая героиня и постоянный радиообмен разрушили одиночество; сюжет — «просто фон» [вторичный, A4].
- **Для Snow:** канал связи сначала чинят; голос начальника редкий и короткий; страховка на середину — если долго нет прогресса, приходит новый толчок.

### A2. Outer Wilds — журнал корабля
- Прогресс держится только на знании. Компьютер корабля хранит найденное, по локациям или как сеть связей. Звёздочка помечает недоисследованное, но куда идти — не говорит [вторичный, A5, A8].
- Девблог Mobius 2017: часть подсказок «не считывалась так явно, как хотелось», взаимодействия сглаживали; журнал переделали в «детективную доску» — от того, как он организован, зависит, как игрок понимает найденное [проверено, A6].
- Детализацию окружения снижают там, где искать нечего [вторичный, A5].
- **Для Snow:** журнал записывает факты, а не инструкции («реле молчит с 14:00»). Для исследовательских заданий И — подходит; для сюжетного дня — нет, день должен закрываться надёжно.

### A3. Firewatch
- Делайла по рации даёт мелкие поручения; события привязаны к дням; карта и компас физические, Генри сам дописывает на карту [вторичный, A9].
- Делайла может говорить только о том, что видит со своей вышки — это и рамка подсказок, и их правдоподобие [вторичный, A9].
- Метроидванию с открытием путей урезали: события вне порядка ломали диалоги [вторичный, A9].
- Диалоги стали длинными перезапускаемыми разговорами, которые переживают прерывание [вторичный, A10].
- **Для Snow:** начальник знает только то, что видно с базы и из телеметрии: «датчик давления на насосной показывает ноль», а не «поверни вентиль 3». Реплики выдерживают прерывание и повтор.

### A4. The Long Dark
- Эпизоды Wintermute 1–2 в 2018 году полностью переделали после жалоб — в подаче и структуре миссий [вторичный, A11]. Ван Лиероп: сюжет и выживание ощущались как «две очень разные игры».
- Картография углём с точки обзора [не проверено].
- **Для Snow:** сюжетный шаг дня — продолжение рутины («почини, сними данные»), а не отдельная игра. Картография с пожарной вышки и радара.

### A5. Voices of the Void — ближайший тон
- Каждую полночь письмо от Dr. Bao (Kel — это сам игрок) с заданием дня: сигналы нужного уровня, хэш-коды с тарелок, ленты. Объём растёт со сложностью [вторичный, A12].
- Сдача физическая: кейсы и ленты — в мешок дрона [вторичный, A12].
- Рядом приходят «флейворные» письма; игроки путают, какое засчитывает выполнение [вторичный, A14].
- **Для Snow:** одно утреннее письмо с заданием, физическая сдача (кассета, факс в лоток). Но статус «засчитано» — однозначный.

### A6. Pacific Drive
- База — гараж; карта — граф перекрёстков; перекрёсток дестабилизируется, зона сжимается — надо уходить [вторичный, A15].
- «Причуды» машины диагностируют на Tinker Station: игрок сопоставляет причину и симптом [вторичный, A15].
- Маршрут планируют за столом в гараже [вторичный].
- **Для Snow:** стол с картой на базе; ремонт как диагностика («найди причину», а не «нажми E»); «зона сжимается» = «темнеет».

### A7. Death Stranding
- Заказы на терминалах; маршрут ставят метками на карте рельефа; подготовка груза — часть задачи [вторичный, A16].
- Критика: доставки почти не меняют мир [вторичный, A16].
- **Для Snow:** груз как часть задания (канистры, детали); выполненное задание обязательно что-то меняет в мире.

### A8. Hitman, Obra Dinn, Disco Elysium
- Hitman: Opportunities добавили после жалоб, что уровни «безжизненные». Это добровольные рельсы: когда информации достаточно, игра перестаёт вести. Объём подсказок настраивается [вторичный, A17].
- Obra Dinn: журнал с пустыми полями, верность подтверждается тройками [не проверено].
- Disco Elysium: формулировка задания — голос персонажа [не проверено].
- **Для Snow:** лестница подсказок — слой, за которым игрок идёт сам. Формулировка — часть характера начальника; поэтому подмена заметна по стилю.

### A9. Без маркеров: Morrowind, Elden Ring, BotW, AC Odyssey
- Принципы Skywind [проверено, A18]: «квест должен проходиться полностью без маркеров»; если NPC отправляет к информатору — сначала цель информатор, конечная появляется после его указаний; журнал хранит старую информацию; в детективных квестах нет маркеров на улики.
- Elden Ring: без журнала квесты NPC легко забыть и сломать; игроки просили хотя бы «что NPC сказал последним и где он стоит». Метки NPC после патча PC Gamer назвал «пластырем» [вторичный, A19].
- BotW, CEDEC 2017: «правило треугольников» — большие формы как ориентиры, средние задают маршрут, малые — темп; по тепловой карте плейтестеров все шли одной тропой, пока между башнями не расставили события [вторичный, A20].
- AC Odyssey, Exploration Mode: цель описана через место и направление, квестодатель даёт зону, а не точку [вторичный, A21].
- Общий урок: отключение маркеров работает, только если текст написан под их отсутствие [вторичный, A22].

### A10. Кооп: общие цели
- **Lethal Company:** одна квота на команду; вернуться на корабль до полуночи; терминал показывает позиции; поздний вход в ванилле только до первой посадки [вторичный, A23].
- **Phasmophobia:** доска целей в грузовике — главная цель плюс 3 случайные необязательные [вторичный, A24].
- **Deep Rock Galactic:** основная цель обязательна, второстепенная — бонус; награда в равной копии каждому; провал платит 25%; для успеха достаточно эвакуировать одного [вторичный, A25].
- **Sea of Thieves:** вояж выбирают голосованием у стола капитана, соло стартует сразу; активен один вояж и один Tall Tale; сложность босса растёт с размером команды [вторичный, A26].
- **Valheim:** квестов нет, цель — боссы; трофей на общем алтаре даёт бонус всем [вторичный, A27].
- **Sons of the Forest:** GPS на трупах; Келвину приказывают запиской [вторичный, A28].
- **R.E.P.O.:** квота на точке эвакуации, тележка показывает сумму [вторичный, A29].
- **Content Warning:** три дня, порог просмотров на команду [проверено, A30].
- **Общий паттерн:** цель общая и висит в одном месте базы; сделал один — засчитано всем; поздний вход — только на базе.
- Prospice: Anomalous Logistics по последним анонсам одиночная [вторичный, A31] — кооп остаётся отличием Snow.

### A11. Best practices
1. Задание называет цель и наблюдаемый симптом, не метод.
2. Текст пишется так, будто маркеров нет совсем.
3. Подсказчик знает только то, что может знать в мире.
4. Лестница подсказок — добровольный слой; когда информации хватает, подсказки прекращаются.
5. Заметные ориентиры тянут к целям; между ними — промежуточные события.
6. Журнал хранит факты и историю сообщений, его можно перечитать.
7. Метки на карте ставит игрок.
8. «Засчитано» — однозначный отдельный сигнал.
9. Если сообщения редеют к середине — игрок теряется; нужен таймер бездействия.
10. Болтливый подсказчик ломает изоляцию.
11. Рутина — каркас; сюжет не отдельная игра.
12. Подготовка и маршрут на базе — тоже геймплей.
13. Выполненное задание меняет мир.
14. Кооп: одна цель на команду, в одном месте; поздний вход — на базе.

### Источники A
1. Subnautica — Wikipedia: https://en.wikipedia.org/wiki/Subnautica
2. Giant Bomb: https://giantbomb.com/wiki/Games/Subnautica ; Steam: https://steamcommunity.com/app/264710/discussions/0/1693785669853037572
3. Subnautica 2, жалоба на маркеры: https://subnautica2.nolt.io/13071
4. Below Zero: https://www.nme.com/reviews/subnautica-below-zero-review-frigid-terror-thats-roiling-in-the-deep-2938757 ; https://www.cgmagonline.com/reviews/subnautica-below-zero-switch-review ; https://gamingtrend.com/reviews/subnautica-below-zero-review
5. Outer Wilds — Wikipedia: https://en.wikipedia.org/wiki/Outer_Wilds
6. Mobius Digital, девлог: https://www.mobiusdigitalgames.com/news/archives/12-2017 ; https://www.mobiusdigitalgames.com/news/archives/11-2017
7. GDC 2020, анонс доклада Outer Wilds: https://gdconf.com/article/attend-gdc-and-learn-how-outer-wilds-nailed-curiosity-driven-game-design/
8. Edge об Outer Wilds: https://www.pressreader.com/australia/edge/20190620/282278141843961
9. Firewatch — Wikipedia: https://en.wikipedia.org/wiki/Firewatch
10. GDC 2017 «Do You Copy?»: https://gdconf.com/news/come-gdc-2017-hear-firewatchs-unique-dialog-system-works
11. The Long Dark — Wikipedia: https://en.wikipedia.org/wiki/The_Long_Dark
12. VotV wiki, Gameplay: https://voicesofthevoid.wiki.gg/wiki/Gameplay
13. VotV — Wikipedia: https://en.wikipedia.org/wiki/Voices_of_the_Void
14. VotV, задания дня: https://itch.io/t/3563959/daily-tasks
15. Pacific Drive — Wikipedia: https://en.wikipedia.org/wiki/Pacific_Drive_(video_game)
16. Death Stranding: https://en.wikipedia.org/wiki/Death_Stranding ; https://www.gamedeveloper.com/design/a-design-discussion-on-death-stranding ; https://shacknews.com/article/114831/how-to-plot-a-route-on-the-map-in-death-stranding
17. Hitman (2016): https://en.wikipedia.org/wiki/Hitman_(2016_video_game) ; https://www.hitmanforum.com/t/hidden-opportunities-in-hitman-3/4151
18. Skywind, принципы квест-дизайна: https://handbook.tesrskywind.com/dev/Principles
19. Elden Ring: https://www.pcgamer.com/uk/elden-rings-npc-quest-markers-are-just-a-band-aid/ ; https://exputer.com/features/fromsoftware-to-better-guide-its-players/
20. BotW, CEDEC 2017: https://gigazine.net/gsc_news/en/20171004-breath-of-the-wild-design-secret ; https://www.thefamicast.com/2017/12/cedec-talks-translated-making-of-breath.html
21. AC Odyssey, Exploration Mode: https://www.pushsquare.com/news/2018/09/assassins_creed_odyssey_explorer_mode_turns_off_automatic_objective_markers_for_a_more_organic_experience ; https://techraptor.net/content/assassins-creed-odyssey-guided-exploration-mode
22. Маркеры заданий: https://bugnet.io/blog/how-to-implement-a-quest-marker-and-objective-tracker
23. Lethal Company: https://en.wikipedia.org/wiki/Lethal_Company ; https://steamcommunity.com/app/1966720/discussions/0/4034728215964359213
24. Phasmophobia, Objective Board: https://phasmophobia.fandom.com/wiki/Objective_Board
25. Deep Rock Galactic, Mission: https://deeprockgalactic.wiki.gg/wiki/Mission
26. Sea of Thieves: https://seaofthieves.wiki.gg/wiki/Voyage ; https://seaofthieves.wiki.gg/wiki/Tall_Tales
27. Valheim — Wikipedia: https://en.wikipedia.org/wiki/Valheim
28. Sons of the Forest: https://en.wikipedia.org/wiki/Sons_of_the_Forest ; https://sonsoftheforest.wiki.gg/wiki/GPS_Locator
29. R.E.P.O.: https://dotesports.com/indies/news/how-to-extract-in-r-e-p-o ; https://www.destructoid.com/?p=1035569
30. Content Warning, пресс-кит: https://landfall.se/content-warning-press-kit
31. Prospice: Anomalous Logistics: https://www.gematsu.com/2026/10/prospice-anomalous-logistics-launches-in-2027-for-ps5-xbox-series-and-pc

---

## B. Рутина как каркас хоррора

### B1. Референсы
- **Chilla's Art** (The Convenience Store, The Closing Shift, Night Security): короткая смена в будничном месте; каждый пункт списка отправляет туда, где ждёт испуг; сверхъестественное приходит через рабочие механизмы — автодверь, покупатель у кассы, неожиданная доставка [вторичный, B3–B5]. Минус: жёсткие рельсы, повторно не проходят [не проверено].
- **Nightmare Shift** (критика): главная механика — повторение; игра учит рутине и тихо её ломает, «never announces when something has changed». Провал — смена так коротка, что «a routine barely has time to form before it's disrupted» [проверено, B6].
- **Workplace horror** (Lancaster Leak, Home Safety Hotline): знакомое место «опускает защиту»; халатный работодатель; страх ответственности — ошибка «could hurt someone» [проверено, B7].
- **I'm on Observation Duty, The Exit 8:** работа — замечать отличия; аномалии от тонких до грубых; слишком тонкие автор вырезал [проверено/вторичный, B8, B9].
- **Iron Lung:** рутинное задание «сфотографируй точку X» заставляет смотреть туда, куда страшно [проверено, B10].
- **Voices of the Void:** письмо на день; параллельно ломаются серверы, садятся трансформаторы — упал один, нет света во всей базе, пока не дойдёшь до кнопки. Недосып — Insomniac [вторичный, B2; проверено, B11].
- **Amnesia: The Bunker:** генератор «continuously burns fuel» — это «element of time» (Fredrik Olsson) [проверено, B13]. «If you spend too long hiding under the table… you might end up in pitch black darkness» [проверено, B14]. Без топлива Зверь охотится активнее [вторичный, B15].
- **Alien: Isolation:** menace gauge растёт от близости и видимости; на пике слишком долго — чужой уходит «backstage» [проверено, B16].
- **Lethal Company, Content Warning, Phasmophobia:** квота — внешняя причина идти глубже, чем хочется; безликая Компания — источник чёрного юмора; необязательные задания Phasmophobia удваивают награду [вторичный/проверено, B17–B19].
- **Darkwood, The Long Dark:** днём запасы и баррикады, к темноте — в убежище; игра спрашивает «how far you want to explore on a given day»; «every minute that goes by, you're slowly dying» [вторичный, B20–B22].
- **Mouthwashing:** самозваный «капитан» лжёт; рутина (раздать обезболивающее) становится соучастием [вторичный, B23].
- **Portal:** GLaDOS «gets progressively more expressive», улики раскрываются постепенно [проверено, B24]. Эссе Ben Abraham: обман приятен, если его можно обнаружить и он подчиняется логике игры [проверено, B26].

### B2. Паттерны
1. **Рутина → отклонение.** Эталон 2–3 повтора, потом тихое изменение одной детали без объявления.
2. **Задание как повод отправить в опасность.**
3. **Рутина как таймер.** Ресурс тает непрерывно, прятаться дорого.
4. **Пугает сам рабочий объект** — дверь, касса, посылка, фото.
5. **Безразличный работодатель** — риск навязан; смешно и страшно.
6. **Необязательная жадность** — бонус за риск, игрок выбирает сам.
7. **День — ставка на ночь.**
8. **Пик → гарантированная пауза.**
9. **Шкала заметности** отклонений; слишком тонкие вырезать.
10. **Сбой системы — незапланированное задание.**
11. **Ненадёжный заказчик раскрывается постепенно**, улики проверяемы внутри мира.
12. **Рутина как соучастие.**
13. **Список дел сам пугает** — в привычном списке пункт, которого быть не должно [вывод, как отдельный приём не проверен].

### Источники B
2. VotV wiki, Gameplay: https://voicesofthevoid.wiki.gg/wiki/Gameplay
3. The Convenience Store — Wikipedia: https://en.wikipedia.org/wiki/The_Convenience_Store
4. Automaton, The Closing Shift: https://automaton-media.com/en/news/20220318-10663/
5. Denfaminico Gamer: https://news.denfaminicogamer.jp/gamenewsplus/200326y
6. GameCritics, Nightmare Shift: https://gamecritics.com/brad-bortone/nightmare-shift-review-ready/
7. Dread Central, Workplace Horror: https://dreadcentral.com/editorials/501408/workplace-horror-an-intimate-look-into-the-horror-of-the-mundane
8. Automaton, интервью Kotake Create: https://automaton-media.com/en/interviews/interview-the-exit-8-developer-kotake-create-on-the-perks-of-being-a-solo-dev-and-how-platform-8-came-to-be/
9. The Exit 8 — Wikipedia: https://en.wikipedia.org/wiki/The_Exit_8
10. Iron Lung — Steam: https://store.steampowered.com/app/1846170/Iron_Lung/
11. VotV — itch.io: https://mrdrnose.itch.io/votv
13. GamingBolt, Amnesia: The Bunker: https://gamingbolt.com/amnesia-the-bunker-interview-semi-open-world-structure-stalker-and-more
14. DualShockers, интервью Olsson: https://www.dualshockers.com/amnesia-the-bunker-interview-need-to-know/
15. Amnesia: The Bunker — Wikipedia: https://en.wikipedia.org/wiki/Amnesia:_The_Bunker
16. Game Developer, AI of Alien: Isolation: https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation
17. Lethal Company — Wikipedia: https://en.wikipedia.org/wiki/Lethal_Company
18. Content Warning, пресс-кит: https://www.landfall.se/content-warning-press-kit
19. Phasmophobia — Wikipedia: https://en.wikipedia.org/wiki/Phasmophobia_(video_game)
20. Darkwood, пресс-кит: https://www.darkwoodgame.com/presskit/
21. Stevivor, Darkwood: https://stevivor.com/reviews/darkwood-review-things-go-bump-night
22. Kotaku, Inside The Long Dark: https://kotaku.com/inside-the-long-dark-1840052958
23. Mouthwashing — Wikipedia: https://en.wikipedia.org/wiki/Mouthwashing_(video_game)
24. Portal, комментарии разработчиков: https://combineoverwiki.net/wiki/Developer_commentary/Portal
26. Ben Abraham, Unreliable: https://iam.benabraham.net/2010/03/02/unreliable/
27. Game Developer, Tension in Horror Game Design: https://www.gamedeveloper.com/design/the-balancing-act-of-tension-in-horror-game-design
28. Thomas Grip, 4 Layers: https://gamedev.net/articles/game-design/writing-for-games/4-layers-a-narrative-design-approach-r3707/

Нумерация с пропусками: [B1] и [B12] (сводка по FNAF) исключены как непригодные источники.

Не найдено: интервью Chilla's Art о дизайне заданий, Zeekerss о квоте, разработчики Phasmophobia о необязательных заданиях; Signalis, SOMA, Pathologic, R.E.P.O., Fears to Fathom — не разбирались.

---

## C. Архитектура

### C1. Базы фактов
- **Cyberpunk 2077:** факт — «just a value (signed integer) and a name (string)»; незаписанный равен 0; имена с префиксом квеста (`mq055_…`); сравнение `< <= == >= >` в блоках Pause/Condition. Факты «ничего не делают» — эффект даёт логика, которая их проверяет. Весь прогресс в сейве — набор фактов. Ручная правка факта «almost always cause additional problems much later» [проверено, C1].
- **Witcher 3:** квест-граф REDkit, условия в Pause-блоках, функции в Script-блоках [проверено, C2]; `FactsAdd/FactsQuerySum` с временем жизни факта [не проверено].
- **Valve Response System (Ruskin, GDC 2012):** правило = критерии + ответ; побеждает правило с наибольшим числом совпавших критериев — самое специфичное; общие правила — запасные. Ответ сам пишет факт («уже сказал X») — память без программиста [вторичный, C4–C6]. Лучшая модель для карт режиссёра.
- **Bethesda Creation Kit:** стадии, цели, алиасы; Story Manager запускает квесты по событиям. Уроки: `SetStage` на несуществующую стадию проходит молча; незаполненный алиас — квест молча не работает [вторичный, C7–C9]. Для Snow: несуществующий id — ошибка при загрузке.
- **Subnautica:** `StoryGoalManager` — множество завершённых строковых ключей; составные цели срабатывают, когда завершены все предпосылки; у цели тип доставки (PDA, Radio) и задержка [не проверено, декомпиляция]. Nitrox заявляет синхронизацию целей, но неполную [вторичный, C10].

### C2. Сторилеты
- Сторилет = контент + предусловия + эффекты; «simple, atomic, robust, and recombinable» (Emily Short) [проверено, C12]. Качества Failbetter — числа, которыми описано всё [проверено, C13].
- Выбор по салиентности: Yarn Spinner отсекает недоступные и ранжирует по числу выполненных условий, по умолчанию — Random Best Least Recently Viewed [проверено, C14].
- Ловушка — «time caves»: каждый кусочек требует уникального флага пути [проверено, C12].
- **Для Snow:** главная линия дней — линейная, сторилеты её не заменят. Подходят для рутины, побочных и карт режиссёра.

### C3. Представление задания

| Подход | Плюсы | Минусы для двоих |
|---|---|---|
| Линейные стадии (Bethesda) | просто, сохраняется одним числом | порядок жёсткий, пропуск шагов — хаки |
| Граф фаз (CDPR) | ветвления, параллельность | нужен визуальный редактор, в JSON не читается |
| Иерархический автомат | ясные состояния | взрыв переходов, трудно сохранять |
| Дерево поведения | реактивный ИИ | не помнит, «что уже случилось» |
| Узлы + счётчики (Pixel Crushers Quest Machine) | готовые инструменты [вторичный, C15] | привязка к Unity-ассетам, сети нет |
| **Цели-предикаты над фактами** (Subnautica, сторилеты) | порядок не важен, сделанное заранее засчитывается само, JSON диффится | порядок подачи задаётся отдельным `requires` |

### C4. События или опрос
- Опрос мира устойчив к рассинхрону и переживает загрузку; события теряются, если случились до выдачи задания — классический источник «не засчиталось».
- **Гибрид:** датчики пишут факты; задания и карты читают только факты. Факты **живые** (`gen.running`) и **защёлки** (`gen.started_once`). Завершение — по защёлкам; выдача влияет только на письмо и подсказки.
- Против тупиков: задание, сделанное до выдачи, засчитывается сразу (письмо «вижу, уже сделали»); уникальный предмет, уничтоженный до конца задания, восстанавливается; линтер проверяет, что у каждого читаемого факта есть писатель.

### C5. Сеть (NGO)
- Состояние заданий и фактов — только на хосте; клиентам — реплика.
- `NetworkVariable`/`NetworkList` — для того, что нужно и поздно вошедшим; RPC — для одноразового, и туда нельзя класть то, что должен увидеть поздний клиент [проверено, C16, C17].
- Valheim: глобальные ключи в мире (`defeated_eikthyr`) — убил один, мир изменился для всех; позже моды ввели ключи с суффиксом игрока [вторичный, C18, C19].
- Для Snow: прогресс общий; личные — только восприятие (кто видел событие) и доставка (кто прочитал), с ключом по устойчивому слоту игрока, не по `clientId`.
- Миграции хоста в NGO нет [вторичный, C20] — источник правды хост, автосейвы на нём.

### C6. Сохранение
- В сейве ключи строками; числовые id — только в сети, выдаются при загрузке определений.
- `schemaVersion` и миграции; id не переиспользуются; таблица переименований.
- Состояние устройств сохраняют их системы; в фактах — только сюжетно значимое.

### C7. Отладка
- Читы меняют только данные: «закрыть задание», «перейти к дню N» — применяют эффекты и канонический `cheat`-блок задания (Valheim: `setkey/removekey/listkeys`) [вторичный, C19].
- Инспектор в F1: факты, статусы, разбор ложного условия по пунктам.
- Линтер в snow_qa: битые и дублирующиеся id, факт без писателя, недостижимое задание, **смешение кириллицы и латиницы в id** (`С05` и `C05`).
- Бот «проходит день»: применяет `cheat` заданий дня и проверяет, что день переключился.

### C8. Чего не строить
Визуальный редактор графа; свой скриптовый язык в JSON; полиморфные ScriptableObject под каждое условие; автоматы и деревья поведения для заданий; личный сюжетный прогресс у каждого игрока; процедурные «radiant»-задания; миграцию хоста; передачу определений заданий клиентам.

### Источники C
1. RED Modding Wiki — Quests: facts and files: https://wiki.redmodding.org/cyberpunk-2077-modding/for-mod-creators-theory/files-and-what-they-do/file-formats/quests-.scene-files/quests-facts-and-files
2. W3 REDkit — quest conditions and functions: https://cdprojektred.atlassian.net/wiki/spaces/W3REDkit/pages/135987240/HOW-TO+Use+quest+conditions+and+functions
3. Game Developer — квест-дизайн Cyberpunk 2077 и Witcher 3 (Sasko, GDC 2023): https://www.gamedeveloper.com/marketing/10-key-takeaways-from-the-quest-design-of-cyberpunk-2077-and-the-witcher-3
4. GDC Vault — Ruskin, AI-driven Dynamic Dialog (2012): https://www.gdcvault.com/play/1015528/AI-driven-Dynamic-Dialog-through
5. Valve Developer Community — Response System: https://developer.valvesoftware.com/wiki/Response_System
6. Emily Short о докладе Ruskin: https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/
7. UESP Fallout 4 CK — Quest: https://falloutck.uesp.net/wiki/Quest
8. UESP Skyrim CK — Story Manager: https://ck.uesp.net/wiki/Story_Manager
9. Nexus — SetStage на несуществующих стадиях: https://forums.nexusmods.com/topic/1966394-quests-never-start-can-set-non-existing-stages-ingame
10. Nitrox — синхронизация: https://www.mintlify.com/Papela/Nitrox-Cracked-Mod/features/multiplayer
11. GDC 2017 Firewatch: https://gdconf.com/news/come-gdc-2017-hear-firewatchs-unique-dialog-system-works
12. Emily Short — Storylets: You Want Them: https://emshort.blog/2019/11/29/storylets-you-want-them/
13. Emily Short — Beyond Branching: https://emshort.blog/2016/04/12/beyond-branching-quality-based-and-salience-based-narrative-structures/
14. Yarn Spinner — Storylets and Saliency: https://docs.yarnspinner.dev/write-yarn-scripts/advanced-scripting/storylets-and-saliency-a-primer
15. Quest Machine (Pixel Crushers): https://discussions.unity.com/t/released-quest-machine-hand-written-and-procedurally-generated-quests/677637
16. NGO — Synchronizing states and events: https://mp-docs.dl.it.unity3d.com/netcode/2.3.2/advanced-topics/ways-synchronize/
17. NGO — NetworkVariable, OnSynchronize: https://mp-docs.dl.it.unity3d.com/netcode/2.2.0/basics/networkvariable/ ; https://docs-multiplayer.unity3d.com/netcode/1.11.0/basics/networkbehavior-synchronize
18. Global Keys in Valheim: https://gamever.io/knowledge-base/global-keys-in-valheim-why-one-players-kill-makes-the-world-harder-for-everyone
19. Valheim Enhanced Progress Tracker: https://www.nexusmods.com/valheim/mods/769
20. Host migration в NGO: https://discussions.unity.com/t/host-migration-in-multiplayer-game-which-uses-lobby-relay-and-netcode-for-gameobjects/923073 ; https://docs.unity.com/en-us/mps-sdk/session-host-migration

Не найдено: внутренности FactsDB Witcher 3, классы StoryGoal Subnautica (только декомпиляция), детали Firewatch, полные слайды Ruskin; документированные архитектуры заданий DRG, Grounded, Sea of Thieves, Lethal Company.

---

## D. Как это ложится на Snow (сводка предложений)

Все пункты — **[предложение]**, вопросы по ним — в `QUESTIONS_3.md`.

**Подача:**
- формат задания — симптом + цель + срок: «Ретранслятор не отвечает с 06:40. Восстановить связь до темноты. — K.»;
- направление — от ориентира; у каждого объекта сети свой силуэт и огонь, видимый издалека;
- начальник знает только то, что видит телеметрия базы;
- голос начальника — не больше 1–2 звонков в день вне подсказок; тишина эфира — тоже сигнал.

**Отслеживание:**
- доска дня на базе (лоток факса или пробковая доска), общая для всех; выполненное — штамп «ПРИНЯТО»;
- журнал смены — папка с копиями всех писем, факсов, радиограмм; инструкций «что делать» в нём нет; через него замечают подмену начальника;
- бумажная карта; отметки карандашом рисуют игроки;
- «засчитано» — короткий ответ «Принято. — K.» и звук факса; рабочий бланк визуально отличается от записки.

**Подсказки:**
- три ступени по таймеру бездействия, каждая — новый звонок: место → симптом → устройство;
- таймер сбрасывается от любого прогресса.

**Рутина и страх:**
- рутина дней 1–2 идеально одинакова (звуки, стрелки, текст факса); первое отклонение одной детали — не раньше дня 3;
- генератор — часы дня: топливо видно на механическом счётчике, прятаться дорого;
- рутинное задание сообщает режиссёру «окна» (игрок у генератора, спиной к двери) — режиссёр тратит бюджет в них;
- сбой системы раз в 1–2 дня — незапланированная вылазка;
- необязательное «по возможности» с бонусом за риск;
- в утренней почте пункт, которого не заказывали («проверить жильца в комнате 4»);
- канцелярский юмор днём — пропажа юмора у начальника потом заметнее;
- утро после ночного пика — гарантированная пауза.

**Подмена начальника:**
- каждый признак проверяется в мире: номер исходящего факса, подпись, обороты, время отправки, расхождение с журналом;
- вредный приказ под видом рутины («оставить ворота открытыми для доставки») — выполнить или отказаться, у обоих есть цена.

**Кооп:**
- прогресс общий; сделал один — засчитано всем; деньги — на общий счёт станции;
- поздний вход — только на базу и вне вылазки;
- сверка показаний по рации может разойтись — проверка реальности.

**Код:**
- база фактов на хосте + задания как условия над фактами + тот же язык условий у режиссёра;
- `ForestOpening` переносится в четыре шага без поломки: сначала дублирует свои шаги в факты, потом условия уходят в датчики, пугалки — в карты режиссёра, бит-маски удаляются.

---

## E. Обучение и режимы подсказок (23.10.2026)

Выводы для Snow — в `docs\gdd\QUESTS.md`.

- **Half-Life 2:** новички играли молча, им ничего не подсказывали; уровень готов, когда «смотреть на игрока не больно» [проверено, E1].
- **Super Mario 3D Land:** механика в четыре шага — научить, усложнить, поворот, проверка [проверено, E2].
- **Super Mario Bros. 1-1:** учит без слов, первый гриб и враг поставлены так, что ошибиться трудно [вторичный, E3].
- **Dead Space:** полосок нет, всё показано на костюме и голограммами в мире [вторичный, E4].
- **The Long Dark:** начало сюжета было скучным для опытных и жёстким для новичков; позже добавили режимы [проверено, E5].
- **Subnautica:** направление дают сигналы по радио, цели игрок ставит себе сам [вторичный, E6].
- **Firewatch:** первые дни ведёт голос по рации, потом игрок ходит сам с картой [вторичный, E7].
- **Phasmophobia:** отдельная учебная карта, по шагу на инструмент [вторичный, E8].
- **Lethal Company:** обучения почти нет, учатся у друзей и по стримам [вторичный, E9].
- **AC Odyssey:** два режима на одном содержании — с метками и «исследование» со словесными приметами; переключать можно всегда [вторичный, E10].
- **Mafia: Definitive Edition, «Классика»:** переключать можно, но ачивка — только если так с начала [проверено, E11].
- **Kingdom Come: Deliverance, хардкор:** нет компаса и своей точки на карте [вторичный, E12].
- **Silent Hill 2/3:** сложность загадок выбирается отдельно от боя [вторичный, E13].
- **Pathologic 2:** галочка «задуманная сложность», её можно снять [вторичный, E14].

Не проверено: текст Assist Mode в Celeste, настройки подсказок Hitman, как учат новичков Pacific Drive и Voices of the Void.

### Источники E
1. Valve, GDC 2006, HL2 design process: https://cdn.fastly.steamstatic.com/apps/valve/2006/GDC2006_HL2DesignProcess.pdf
2. Game Developer, Mario level design: https://www.gamedeveloper.com/design/the-secret-to-i-mario-i-level-design
3. MCV, Miyamoto: https://www.mcvuk.com/development-news/video-miyamoto-shares-his-level-design-secrets/
4. Game Developer, Dead Space UI: https://gamedeveloper.com/design/video-designing-i-dead-space-i-s-immersive-user-interface
5. The Long Dark news: https://www.thelongdark.com/news/steam-autumn-sale-and-news-on-the-december-update/
6. GDC Vault, The Design of Subnautica: https://gdcvault.com/play/1025745/The-Design-of-Subnautica
7. Prima, Firewatch: https://primagames.com/news/firewatch-exploration-guide-and-walkthrough
8. Phasmophobia training: https://allthings.how/phasmophobia-how-to-complete-the-training-tutorial/
9. Common Sense Media, Lethal Company: https://www.commonsensemedia.org/game-reviews/lethal-company
10. Push Square, AC Odyssey: https://www.pushsquare.com/news/2018/09/assassins_creed_odyssey_explorer_mode_turns_off_automatic_objective_markers_for_a_more_organic_experience
11. 2K, Mafia Classic: https://mafia.2k.com/news/mafia-definitive-edition-classic-difficulty/
12. KCD hardcore: https://www.gamebanshee.com/k4dk6 ; https://www.videogameschronicle.com/news/kingdom-come-deliverance-2s-gruelling-hardcore-mode-has-arrived
13. Push Square, Silent Hill 2: https://www.pushsquare.com/guides/silent-hill-2-combat-and-puzzle-difficulty-options-explained-what-settings-should-you-choose
14. Game Informer, Pathologic 2: https://www.gameinformer.com/2019/06/07/pathologic-2s-new-update-invites-you-to-adjust-your-level-of-suffering

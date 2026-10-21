# Главный корпус станции — модель и контракт

Версия 1.1 от 20.10.2026. Пакет `source/assets/forest_main/` (архив рядом — `forest_main_package.zip`): `main.blend` (оба префаба — `BaseMain` и `BaseMain_Opening`), атласы и панель в `tex/`, контракт `main_contract.json`, листы A–F, исходники в `src/`.

Главный корпус карты Forest перестроен в виде генераторной: та же плотность пикселя, те же 64 цвета на атлас, AO в цвете, дизеринг и способ покраски (пыль сверху, грязь в углах, сколы до грунта по кромкам, потёки). Фасады — бетон по дощатой опалубке, как в обновлении 3 (доски 12 см, отверстия от стяжек, потёки под парапетом и подоконниками, водоросли и брызги у цоколя); металл — двери, рамы, ворота, козырёк — в серо-зелёной краске станции, выгоревшей снаружи. Планировка, комнаты и их отделка, двери, окна, ставни, имена деталей `IX_*` с номерами, их точки вращения, оси и ходы, светильники `FX_*` на своих местах, зоны света (light layers) и всё, что расставлено поверх корпуса в данных пролога (`Forest_Dressing.json`, `Forest_Opening.json`), — как в обновлении 3.

В Unity ничего не поставлено: замена префабов `BaseMain` и `BaseMain_Opening` на карте — в обновлении 4 вместе с остальными постройками (DEFERRED).

## Что внутри

- Объектов 220: корпус снаружи `BaseMain`, 15 комнат `R_<Room>`, 139 деталей `IX_*` (все 139 деталей обновления 3, лишних нет), 64 светильников `FX_*`, второй префаб `BaseMain_Opening`. По видам: петля — 93, сворачивается — 19, выдвигается — 27, неподвижно — 81.
- Точек 71: звук — 6, рука — 37, стук — 28.
- Треугольников 120 308: `BaseMain` — 118 794 (в обновлении 3 — 164 тыс.), `BaseMain_Opening` — 1 514 (1,1 тыс.).
- Атласы (`M_Main_<имя>`, `T_Main_<имя>_BaseColor.png`): Ext 2048 (51,3 пикс/м), Rm_Flur_EG 1024 (51,3 пикс/м), Rm_Flur_OG 1024 (51,3 пикс/м), Rm_Flur_UG 1024 (51,3 пикс/м), Rm_Garage 2048 (51,3 пикс/м), Rm_Heizung 1024 (48,5 пикс/м), Rm_Kaserne 1024 (51,3 пикс/м), Rm_Kueche 1024 (51,2 пикс/м), Rm_Lager 1024 (51,3 пикс/м), Rm_Leitstand 2048 (51,3 пикс/м), Rm_Schaltraum 1024 (51,3 пикс/м), Rm_Serverraum 1024 (51,3 пикс/м), Rm_Stationsleiter 1024 (51,3 пикс/м), Rm_Treppenhaus 2048 (51,3 пикс/м), Rm_Waschraum 1024 (51,3 пикс/м), Rm_Windfang 1024 (51,3 пикс/м), Parts 2048 (64 пикс/м), Opening 256 (96 пикс/м); панель 1024 (таблички, наклейки, планы эвакуации, бумаги, циферблаты, лицевая панель регулятора). У атласов комнат — плотность генераторной 51,3 пикс/м (меньше она не бывает: острова сжимаются до неё, место в атласе остаётся пустым); у фасада указана средняя с учётом крыши, которую нарочно сжали вдвое (гравий видно только с крыши), стены — около 49; у деталей и светильников — 64, как у мелочей генераторной.

## Система координат и место на карте

Один кадр на оба префаба (кадр `BaseMain.fbx` обновления 3 после импорта): x — на восток, y — на север, z — вверх, метры; начало — чистый пол 1-го этажа в середине основного блока. Уровни: подвал −3,00, 1-й этаж 0,00 (гараж −0,15), 2-й этаж 3,40, крыша 6,60, верх парапета 7,50, лестничная башня до 9,65, двор у входа −0,45.

В Unity (в префабе): позиция (x, y, z) → (−x, z, −y) (`pivotUnity` дан у каждой детали). Ось петли в контракте — ось обновления 3 (X, Y, Z) как (X, Z, −Y), угол тот же; ось выдвижной детали — (−X, −Z, Y). В `u3` у каждой детали обновления 3 — её вид, ось в Unity, min / max и флаги из `Forest_Layout.json`: они остаются верными, данные пролога подходят без правок. Оба префаба стоят в одной точке:

| Префаб | p (Unity) | q (x, y, z, w) | поворот вокруг Y |
|---|---|---|---|
| `BaseMain` | (340, 16.7443, −360) | (0, 0.069756, 0, 0.997564) | 8° |
| `BaseMain_Opening` | (340, 16.7443, −360) | (0, 0.069756, 0, 0.997564) | 8° |

## Корпус снаружи

Основной блок 16 × 12 м в два этажа над подвалом под восточной половиной, плоская крыша с гравием и парапетом 0,9 м (оцинкованный отлив поверху), лестничная башня с выходом на крышу (`IX_RoofDoor_17`), с запада — гараж-мастерская 10 × 10 м. Стены — бетон по опалубке из досок 12 см над тёмным цоколем, межэтажный пояс, кирпичная труба котельной над крышей. Вход с юга: площадка с двумя ступенями, пандус 1:13 вдоль фасада с перилами, бетонный козырёк с серо-зелёным металлическим фризом, звонок и переговорное устройство, табличка RADIOOBSERVATORIUM HOHENMOOS. Запасный выход на востоке (`IX_ExitDoor_2`, площадка со ступенями), у гаража — пандус ворот и дверь `IX_GarageDoor_19`, ввод аварийного питания NOTSTROM-EINSPEISUNG. Шесть водосточных труб, заправочная и дыхательная трубы топлива. На крыше — вентустановка с воздуховодом к башне, решётчатая мачта 12 м с площадкой и заградительным огнём (`FX_Obstruction_64`), спутниковая антенна, дефлекторы.

Окна — в серо-зелёных стальных рамах, с отливами; на 19 окнах — рольставни обновления 3 (`IX_Shutter_*`: полотно сворачивается масштабом по z от короба, на старте поднято — `open`), их ремни (`IX_ShutterWinder_*`) и электрические выключатели (`IX_ShutterSwitch_*`).

## Комнаты

Каждая комната — свой объект `R_<Room>` со своей маской света (как в обновлении 3): пол, потолок, стены с отделкой, откосы, плинтус, мебель и оборудование; стекло её окон, таблички и бумаги, экраны и светодиоды — дополнительные материалы того же объекта.

| Комната | Этаж | Пол | Стены | Потолок | Слой (маска) | Что в ней |
|---|---|---|---|---|---|---|
| `R_Windfang` — тамбур | 1 | плитка 30 см | панель масляной краской до 1,5 м (коричневая), выше побелка | штукатурка | 7 (128) | вешалка с куртками, сапоги, лопата и метла у двери, коврик, шкафчики (двери `IX_LockerDoor_20/21`), огнетушитель, извещатель; входная дверь в нише — в ней лежит засов `IX_MainDoorBar_311` |
| `R_Leitstand` — пультовая | 1 | иглопробивной ковролин | панель масляной краской до 1,35 м (коричневая), выше побелка | подвесной (минплита 62,5 см) | 4 (16) | два пульта под ленточным окном (наклонные панели с лампами, ключами и приборами, мониторы), три рабочих места вдоль коридорной стены (стол, тумба с ящиками `IX_Drawer_22…37`, ЭЛТ-монитор, клавиатура, системный блок), стол с картой, картотеки, настенное табло антенного поля, доска, часы, радиаторы |
| `R_Flur_EG` — коридор 1-го этажа | 1 | линолеум в шашку 30 см | панель масляной краской до 1,5 м (кремовая), полоса 70-х (оранжевая, коричневая, оливковая), выше побелка | подвесной (минплита 62,5 см) | 2 (4) | пожарный кран, звонок и извещатель, телефон, огнетушитель, громкоговоритель, план эвакуации, стеклянная дверь из тамбура |
| `R_Lager` — склад | 1 | бетон (крашеный) | панель масляной краской до 1,5 м (серая), выше побелка | штукатурка | 5 (32) | стеллажи с коробками, папками и брезентом, армейские ящики на поддонах, картон |
| `R_Serverraum` — серверная | 1 | фальшпол 60 × 60 | краска | подвесной (минплита 62,5 см) | 8 (256) | стойки 19″ за дверцами `IX_RackDoor_38…44` (дымчатое стекло), низкая стойка с терминалом у двери, кабельный лоток, щит с автоматами и рубильником `IX_Isolator_51`, остеклённая перегородка в коридор |
| `R_Garage` — гараж-мастерская | 1 | бетон гаража | бетон | фибролит | 6 (64) | квадроцикл, верстак с тисками и перфорированной доской, стеллажи, бочки (красная, синяя), канистры, покрышки, ящики, аптечка, телефон; подъёмно-поворотные ворота `IX_GarageGate_18` |
| `R_Kaserne` — спальня | 2 | оливковый линолеум | панель масляной краской до 1,5 м (оливковая), выше побелка | штукатурка | 8 (256) | двухъярусные койки (зоны сна обновления 3 — на матрасах), тумбочки `IX_TumbaDoor_52/53`, шкафчики `IX_LockerDoor_54…57`, двустворчатый шкаф, стол на козлах с лавками, доска с графиком дежурств |
| `R_Kueche` — кухня и комната отдыха | 2 | плитка (кухня) | панель масляной краской до 1,5 м (коричневая), выше побелка | штукатурка | 7 (128) | кухонный ряд (плита, мойка, кофеварка, чайник, микроволновка), холодильник (дверь `IX_FridgeDoor_58`), стол со стульями, диван, телевизор, кресло, часы, аптечка |
| `R_Flur_OG` — коридор 2-го этажа | 2 | линолеум в шашку 30 см | панель масляной краской до 1,5 м (кремовая), полоса 70-х (оранжевая, коричневая, оливковая), выше побелка | штукатурка | 3 (8) | щит УВ-ОГ, звонок и извещатель, огнетушитель, громкоговоритель |
| `R_Waschraum` — умывальная | 2 | плитка | плитка 15 см до 2 м, выше краска | штукатурка | 9 (512) | оцинкованный умывальник на четыре чаши с зеркалами, две кабинки WC, душ со стеклянной стенкой, полка с полотенцами, стиральная машина, резиновые сапоги |
| `R_Stationsleiter` — кабинет начальника | 2 | лакированная доска | шпон до 1,24 м, выше краска | штукатурка | 4 (16) | стол с тумбой (ящики `IX_Drawer_59…68`), красный телефон, сейф, книжный шкаф, кресло и стул, часы, радиатор |
| `R_Heizung` — котельная | подвал | бетон (крашеный) | краска | штукатурка | 5 (32) | жидкотопливный котёл с горелкой (кнопка сброса `IX_BurnerReset_308`), группа безопасности с манометром, бойлер, насосная группа, коллекторы, склад топлива (бак в оцинкованном поддоне), шкаф регулятора (его лицевая панель — префаб `BaseMain_Opening`), полка, огнетушитель, доска с порядком пуска |
| `R_Flur_UG` — коридор подвала | подвал | бетон (крашеный) | панель масляной краской до 1,5 м (серая), выше побелка | штукатурка | 7 (128) | магистрали отопления под потолком, кабельный лоток, стремянка, аварийный выключатель отопления `IX_HeatingStop_131`, звонок и извещатель |
| `R_Schaltraum` — электрощитовая | подвал | бетон (крашеный) | панель масляной краской до 1,5 м (серая), выше побелка | штукатурка | 9 (512) | главный щит ГРЩ (кремовые панели, шесть рубильников и главный выключатель `IX_MainSwitch_75`), настенный щит с дверцей `IX_PanelDoor_83` и рубильником `IX_Isolator_82`, мнемосхема |
| `R_Treppenhaus` — лестничная клетка | все | бетон (крашеный) | бетон | бетон | 6 (64) | П-образная лестница от подвала до выхода на крышу, перила, поручни, указатели этажей, выключатели |

## Пролог в корпусе

- S7 «Питание»: главный выключатель `IX_MainSwitch_75` на ГРЩ в электрощитовой (подвал). С питанием экраны и светодиоды меняют материал `M_ScreenOff` → `M_ScreenOn`, `M_LEDOff` → `M_LEDOn` (как в обновлении 3).
- S8 «Отопление» (префаб `BaseMain_Opening`): аварийный выключатель `IX_HeatingStop_131` в коридоре подвала, кран подпитки `IX_HeatFill_307` у котла (0…270° — 0…4 бар на манометре `IX_HeatGauge_309`), насос `IX_HeatPump_304` и горелка `IX_HeatBurner_305` — клавиши на лицевой панели регулятора, лампа STÖRUNG (`M_HeatFault` → `M_HeatFaultOn`), кнопка сброса горелки `IX_BurnerReset_308` на её корпусе (колпачок тоже `M_HeatFault`), термостат `IX_HeatThermostat_306` (0…270° — 5…30 °C), термометр котла `IX_HeatTempGauge_310` (0…120 °C).
- S9 «На ночь»: засов `IX_MainDoorBar_311` (0° — через дверь в держателе, на старте поднят), ставни опущены, ворота гаража закрыты. Засов лежит в нише внутренней стены вокруг входной двери (глубина 11 см): в обновлении 3 он проходил сквозь стену.
- S10 «Сон»: зоны сна обновления 3 лежат на матрасах коек спальни.

## Детали

| Деталь | Что | Ось и ход | Точка | Родитель | Маска | Описание |
|---|---|---|---|---|---|---|
| `IX_MainDoor_1` | петля | (0, 0, −1), 0…90 ° | (−6.968, −5.963, 0.01) | BaseMain | 130 | steel_vision door, Windfang <-> Exterior |
| `IX_ExitDoor_2` | петля | (0, 0, −1), 0…90 ° | (7.963, −0.517, 0.01) | BaseMain | 134 | steel_panic door, Flur_EG <-> Exterior |
| `IX_Door_3` | петля | (0, 0, 1), 0…95 ° | (−7.963, −0.517, 0.01) | BaseMain | 68 | steel door, Flur_EG <-> Garage |
| `IX_Door_4` | петля | (0, 0, −1), 0…95 ° | (−7.963, 3.917, 0.01) | BaseMain | 96 | steel door, Lager <-> Garage |
| `IX_Door_5` | петля | (0, 0, −1), 0…95 ° | (−5.928, −1.032, 0.01) | BaseMain | 132 | glass door, Windfang <-> Flur_EG |
| `IX_Door_6` | петля | (0, 0, −1), 0…95 ° | (−3.473, −1.117, 0.01) | BaseMain | 20 | room door, Leitstand <-> Flur_EG |
| `IX_Door_7` | петля | (0, 0, 1), 0…95 ° | (−5.473, 1.117, 0.01) | BaseMain | 36 | room door, Lager <-> Flur_EG |
| `IX_Door_8` | петля | (0, 0, 1), 0…95 ° | (−0.568, 1.113, 0.01) | BaseMain | 196 | fire door, Treppenhaus <-> Flur_EG |
| `IX_Door_9` | петля | (0, 0, −1), 0…95 ° | (3.467, 1.113, 0.01) | BaseMain | 900 | room door, Serverraum <-> Flur_EG |
| `IX_Door_10` | петля | (0, 0, −1), 0…95 ° | (−3.473, −1.117, 3.41) | BaseMain | 284 | room door, Kaserne <-> Flur_OG |
| `IX_Door_11` | петля | (0, 0, 1), 0…95 ° | (−5.473, 1.117, 3.41) | BaseMain | 556 | room door, Waschraum <-> Flur_OG |
| `IX_Door_12` | петля | (0, 0, 1), 0…95 ° | (−0.568, 1.113, 3.41) | BaseMain | 76 | fire door, Treppenhaus <-> Flur_OG |
| `IX_Door_13` | петля | (0, 0, −1), 0…95 ° | (3.473, 1.117, 3.41) | BaseMain | 284 | room door, Stationsleiter <-> Flur_OG |
| `IX_Door_14` | петля | (0, 0, 1), 0…95 ° | (2.033, −1.038, −2.99) | BaseMain | 160 | fire door, Heizung <-> Flur_UG |
| `IX_Door_15` | петля | (0, 0, 1), 0…95 ° | (−0.568, 1.113, −2.99) | BaseMain | 192 | fire door, Treppenhaus <-> Flur_UG |
| `IX_Door_16` | петля | (0, 0, 1), 0…95 ° | (3.632, 1.113, −2.99) | BaseMain | 640 | steel door, Schaltraum <-> Flur_UG |
| `IX_RoofDoor_17` | петля | (0, 0, −1), 0…95 ° | (−0.568, 1.038, 6.62) | BaseMain | 74 | steel door, Treppenhaus <-> Exterior |
| `IX_GarageGate_18` | петля | (1, 0, 0), 0…85 ° | (−16.58, −4.66, 2.85) | BaseMain | 66 | up-and-over garage gate: swings up and in about the top edge to lie under the ceiling |
| `IX_GarageDoor_19` | петля | (0, 0, −1), 0…90 ° | (−11.268, −4.963, −0.14) | BaseMain | 66 | steel door, Garage <-> Exterior |
| `IX_LockerDoor_20` | петля | (0, 0, 1), 0…110 ° | (−5.75, −4.99, 0.02) | BaseMain | 128 | locker door |
| `IX_LockerDoor_21` | петля | (0, 0, 1), 0…110 ° | (−5.75, −4.47, 0.02) | BaseMain | 128 | locker door |
| `IX_Drawer_22` | выдвигается | (0, −1, 0), 0…0,45 м | (−1.55, −1.88, 0.06) | BaseMain | 48 | drawer |
| `IX_Drawer_23` | выдвигается | (0, −1, 0), 0…0,45 м | (−1.55, −1.88, 0.275) | BaseMain | 16 | drawer |
| `IX_Drawer_24` | выдвигается | (0, −1, 0), 0…0,45 м | (−1.55, −1.88, 0.49) | BaseMain | 16 | drawer |
| `IX_Drawer_25` | выдвигается | (0, −1, 0), 0…0,45 м | (0.45, −1.88, 0.06) | BaseMain | 48 | drawer |
| `IX_Drawer_26` | выдвигается | (0, −1, 0), 0…0,45 м | (0.45, −1.88, 0.275) | BaseMain | 16 | drawer |
| `IX_Drawer_27` | выдвигается | (0, −1, 0), 0…0,45 м | (0.45, −1.88, 0.49) | BaseMain | 16 | drawer |
| `IX_Drawer_28` | выдвигается | (0, −1, 0), 0…0,45 м | (2.45, −1.88, 0.06) | BaseMain | 48 | drawer |
| `IX_Drawer_29` | выдвигается | (0, −1, 0), 0…0,45 м | (2.45, −1.88, 0.275) | BaseMain | 16 | drawer |
| `IX_Drawer_30` | выдвигается | (0, −1, 0), 0…0,45 м | (2.45, −1.88, 0.49) | BaseMain | 16 | drawer |
| `IX_Drawer_31` | выдвигается | (0, 1, 0), 0…0,5 м | (−4.75, −5.03, 0.04) | BaseMain | 16 | drawer |
| `IX_Drawer_32` | выдвигается | (0, 1, 0), 0…0,5 м | (−4.75, −5.03, 0.36) | BaseMain | 16 | drawer |
| `IX_Drawer_33` | выдвигается | (0, 1, 0), 0…0,5 м | (−4.75, −5.03, 0.68) | BaseMain | 16 | drawer |
| `IX_Drawer_34` | выдвигается | (0, 1, 0), 0…0,5 м | (−4.75, −5.03, 1) | BaseMain | 16 | drawer |
| `IX_Drawer_35` | выдвигается | (1, 0, 0), 0…0,5 м | (−4.48, −4.55, 0.04) | BaseMain | 16 | drawer |
| `IX_Drawer_36` | выдвигается | (1, 0, 0), 0…0,5 м | (−4.48, −4.55, 0.36) | BaseMain | 16 | drawer |
| `IX_Drawer_37` | выдвигается | (1, 0, 0), 0…0,5 м | (−4.48, −4.55, 0.68) | BaseMain | 16 | drawer |
| `IX_RackDoor_38` | петля | (0, 0, 1), 0…110 ° | (2.89, 4.745, 0.02) | BaseMain | 768 | rack door |
| `IX_RackDoor_39` | петля | (0, 0, 1), 0…110 ° | (3.51, 4.745, 0.02) | BaseMain | 768 | rack door |
| `IX_RackDoor_40` | петля | (0, 0, 1), 0…110 ° | (4.13, 4.745, 0.02) | BaseMain | 768 | rack door |
| `IX_RackDoor_41` | петля | (0, 0, 1), 0…110 ° | (4.75, 4.745, 0.02) | BaseMain | 768 | rack door |
| `IX_RackDoor_42` | петля | (0, 0, 1), 0…110 ° | (5.37, 4.745, 0.02) | BaseMain | 768 | rack door |
| `IX_RackDoor_43` | петля | (0, 0, 1), 0…110 ° | (5.99, 4.745, 0.02) | BaseMain | 768 | rack door |
| `IX_RackDoor_44` | петля | (0, 0, 1), 0…110 ° | (6.61, 4.745, 0.02) | BaseMain | 768 | rack door |
| `IX_Breaker_45` | петля | (0, 1, 0), 0…45 ° | (7.39, 2.82, 1.52) | BaseMain | 256 | circuit breaker toggle, 0..45 deg |
| `IX_Breaker_46` | петля | (0, 1, 0), 0…45 ° | (7.39, 2.932, 1.52) | BaseMain | 256 | circuit breaker toggle, 0..45 deg |
| `IX_Breaker_47` | петля | (0, 1, 0), 0…45 ° | (7.39, 3.044, 1.52) | BaseMain | 256 | circuit breaker toggle, 0..45 deg |
| `IX_Breaker_48` | петля | (0, 1, 0), 0…45 ° | (7.39, 3.156, 1.52) | BaseMain | 256 | circuit breaker toggle, 0..45 deg |
| `IX_Breaker_49` | петля | (0, 1, 0), 0…45 ° | (7.39, 3.268, 1.52) | BaseMain | 256 | circuit breaker toggle, 0..45 deg |
| `IX_Breaker_50` | петля | (0, 1, 0), 0…45 ° | (7.39, 3.38, 1.52) | BaseMain | 256 | circuit breaker toggle, 0..45 deg |
| `IX_Isolator_51` | петля | (−1, 0, 0), 0…90 ° | (7.39, 3.1, 1.05) | BaseMain | 256 | rotary isolator, 0..90 deg |
| `IX_TumbaDoor_52` | петля | (0, 0, 1), 0…100 ° | (−7.25, −4.015, 3.42) | BaseMain | 384 | bedside unit door |
| `IX_TumbaDoor_53` | петля | (0, 0, 1), 0…100 ° | (−7.25, −2.065, 3.42) | BaseMain | 384 | bedside unit door |
| `IX_LockerDoor_54` | петля | (0, 0, 1), 0…110 ° | (−6.96, −1.65, 3.42) | BaseMain | 384 | locker door |
| `IX_LockerDoor_55` | петля | (0, 0, 1), 0…110 ° | (−6.44, −1.65, 3.42) | BaseMain | 384 | locker door |
| `IX_LockerDoor_56` | петля | (0, 0, 1), 0…110 ° | (−5.92, −1.65, 3.42) | BaseMain | 384 | locker door |
| `IX_LockerDoor_57` | петля | (0, 0, 1), 0…110 ° | (−5.4, −1.65, 3.42) | BaseMain | 400 | locker door |
| `IX_FridgeDoor_58` | петля | (0, 0, 1), 0…115 ° | (7.09, −1.785, 3.42) | BaseMain | 144 | fridge door |
| `IX_Drawer_59` | выдвигается | (0, 1, 0), 0…0,45 м | (5.55, 4.28, 3.46) | BaseMain | 272 | drawer |
| `IX_Drawer_60` | выдвигается | (0, 1, 0), 0…0,45 м | (5.55, 4.28, 3.675) | BaseMain | 16 | drawer |
| `IX_Drawer_61` | выдвигается | (0, 1, 0), 0…0,45 м | (5.55, 4.28, 3.89) | BaseMain | 16 | drawer |
| `IX_Drawer_62` | выдвигается | (1, 0, 0), 0…0,5 м | (2.57, 4.95, 3.44) | BaseMain | 272 | drawer |
| `IX_Drawer_63` | выдвигается | (1, 0, 0), 0…0,5 м | (2.57, 4.95, 3.76) | BaseMain | 16 | drawer |
| `IX_Drawer_64` | выдвигается | (1, 0, 0), 0…0,5 м | (2.57, 4.95, 4.08) | BaseMain | 16 | drawer |
| `IX_Drawer_65` | выдвигается | (1, 0, 0), 0…0,5 м | (2.57, 4.95, 4.4) | BaseMain | 16 | drawer |
| `IX_Drawer_66` | выдвигается | (1, 0, 0), 0…0,5 м | (2.57, 4.45, 3.44) | BaseMain | 272 | drawer |
| `IX_Drawer_67` | выдвигается | (1, 0, 0), 0…0,5 м | (2.57, 4.45, 3.76) | BaseMain | 16 | drawer |
| `IX_Drawer_68` | выдвигается | (1, 0, 0), 0…0,5 м | (2.57, 4.45, 4.08) | BaseMain | 16 | drawer |
| `IX_Breaker_69` | петля | (−1, 0, 0), 0…80 ° | (5.2, 5.28, −1.45) | BaseMain | 512 | outgoing lever switch, 0..80 deg |
| `IX_Breaker_70` | петля | (−1, 0, 0), 0…80 ° | (4.96, 5.28, −1.45) | BaseMain | 512 | outgoing lever switch, 0..80 deg |
| `IX_Breaker_71` | петля | (−1, 0, 0), 0…80 ° | (4.72, 5.28, −1.45) | BaseMain | 512 | outgoing lever switch, 0..80 deg |
| `IX_Breaker_72` | петля | (−1, 0, 0), 0…80 ° | (4.48, 5.28, −1.45) | BaseMain | 512 | outgoing lever switch, 0..80 deg |
| `IX_Breaker_73` | петля | (−1, 0, 0), 0…80 ° | (4.24, 5.28, −1.45) | BaseMain | 512 | outgoing lever switch, 0..80 deg |
| `IX_Breaker_74` | петля | (−1, 0, 0), 0…80 ° | (4, 5.28, −1.45) | BaseMain | 512 | outgoing lever switch, 0..80 deg |
| `IX_MainSwitch_75` | петля | (−1, 0, 0), 0…90 ° | (4.6, 5.25, −2.1) | BaseMain | 512 | MAIN SWITCH of the building (the mains on / off), lever 0..90 deg |
| `IX_Breaker_76` | петля | (0, 1, 0), 0…45 ° | (7.39, 2.92, −1.48) | BaseMain | 512 | circuit breaker toggle, 0..45 deg |
| `IX_Breaker_77` | петля | (0, 1, 0), 0…45 ° | (7.39, 3.032, −1.48) | BaseMain | 512 | circuit breaker toggle, 0..45 deg |
| `IX_Breaker_78` | петля | (0, 1, 0), 0…45 ° | (7.39, 3.144, −1.48) | BaseMain | 512 | circuit breaker toggle, 0..45 deg |
| `IX_Breaker_79` | петля | (0, 1, 0), 0…45 ° | (7.39, 3.256, −1.48) | BaseMain | 512 | circuit breaker toggle, 0..45 deg |
| `IX_Breaker_80` | петля | (0, 1, 0), 0…45 ° | (7.39, 3.368, −1.48) | BaseMain | 512 | circuit breaker toggle, 0..45 deg |
| `IX_Breaker_81` | петля | (0, 1, 0), 0…45 ° | (7.39, 3.48, −1.48) | BaseMain | 512 | circuit breaker toggle, 0..45 deg |
| `IX_Isolator_82` | петля | (−1, 0, 0), 0…90 ° | (7.39, 3.2, −1.95) | BaseMain | 512 | rotary isolator, 0..90 deg |
| `IX_PanelDoor_83` | петля | (0, 0, 1), 0…120 ° | (7.41, 4.2, −2.1) | BaseMain | 512 | distribution cabinet door |
| `IX_LightSwitch_Windfang_84` | петля | (−1, 0, 0), −9…9 ° | (−7.22, −1.167, 1.05) | BaseMain | 132 | light switch (Windfang) |
| `IX_LightSwitch_Flur_EG_85` | петля | (−1, 0, 0), −9…9 ° | (0.82, 0.983, 1.05) | BaseMain | 68 | light switch (corridor, ground floor) by the stair door |
| `IX_LightSwitch_Flur_EG_86` | петля | (1, 0, 0), −9…9 ° | (−7.22, −0.983, 1.05) | BaseMain | 132 | light switch (corridor, ground floor) by the lobby door |
| `IX_LightSwitch_Garage_87` | петля | (0, 1, 0), −9…9 ° | (−8.049, 0.77, 0.9) | BaseMain | 100 | light switch (garage), surface box |
| `IX_LightSwitch_Lager_88` | петля | (1, 0, 0), −9…9 ° | (−4.28, 1.199, 1.05) | BaseMain | 36 | light switch (Lager), surface box |
| `IX_LightSwitch_Leitstand_89` | петля | (−1, 0, 0), −9…9 ° | (−2.28, −1.167, 1.05) | BaseMain | 20 | light switch (Leitstand) |
| `IX_LightSwitch_Treppenhaus_90` | петля | (1, 0, 0), −9…9 ° | (0.82, 1.199, 1.05) | BaseMain | 68 | light switch (stair tower), surface box |
| `IX_LightSwitch_Treppenhaus_91` | петля | (1, 0, 0), −9…9 ° | (0.82, 1.199, 4.45) | BaseMain | 72 | light switch (stair tower), surface box |
| `IX_LightSwitch_Treppenhaus_92` | петля | (1, 0, 0), −9…9 ° | (0.82, 1.199, −1.95) | BaseMain | 192 | light switch (stair tower), surface box |
| `IX_LightSwitch_Serverraum_93` | петля | (1, 0, 0), −9…9 ° | (2.38, 1.167, 1.05) | BaseMain | 260 | light switch (Serverraum) by its door |
| `IX_LightSwitch_Kaserne_94` | петля | (−1, 0, 0), −9…9 ° | (−2.28, −1.167, 4.45) | BaseMain | 264 | light switch (Kaserne) |
| `IX_LightSwitch_Flur_OG_95` | петля | (−1, 0, 0), −9…9 ° | (0.82, 0.983, 4.45) | BaseMain | 72 | light switch (corridor, upper floor) |
| `IX_LightSwitch_Kueche_96` | петля | (−1, 0, 0), −9…9 ° | (2.82, −1.167, 4.45) | BaseMain | 136 | light switch (Kueche) |
| `IX_LightSwitch_Waschraum_97` | петля | (1, 0, 0), −9…9 ° | (−4.38, 1.167, 4.45) | BaseMain | 520 | light switch (Waschraum) |
| `IX_LightSwitch_Stationsleiter_98` | петля | (1, 0, 0), −9…9 ° | (2.38, 1.167, 4.45) | BaseMain | 24 | light switch (Stationsleiter) |
| `IX_LightSwitch_Heizung_99` | петля | (−1, 0, 0), −9…9 ° | (3.17, −1.199, −1.95) | BaseMain | 160 | light switch (Heizung), surface box |
| `IX_LightSwitch_Flur_UG_100` | петля | (−1, 0, 0), −9…9 ° | (0.82, 0.951, −1.95) | BaseMain | 192 | light switch (corridor, basement), surface box |
| `IX_LightSwitch_Schaltraum_101` | петля | (1, 0, 0), −9…9 ° | (4.77, 1.199, −1.95) | BaseMain | 640 | light switch (Schaltraum), surface box |
| `IX_LightSwitch_Exterior_102` | петля | (−1, 0, 0), −9…9 ° | (−7.32, −1.167, 1.05) | BaseMain | 132 | outside light (the yard floods, the entrance lamp) |
| `IX_Shutter_Leitstand_103` | сворачивается | z, 1…0,0284 | (−3.044, −5.94, 2.75) | BaseMain | 18 | roller-shutter curtain, 2.69 x 1.94 m: scales along z under the lintel (1 = down, 0.0284 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Leitstand_104` | сворачивается | z, 1…0,0284 | (−0.366, −5.94, 2.75) | BaseMain | 18 | roller-shutter curtain, 2.64 x 1.94 m: scales along z under the lintel (1 = down, 0.0284 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Leitstand_105` | сворачивается | z, 1…0,0284 | (2.289, −5.94, 2.75) | BaseMain | 18 | roller-shutter curtain, 2.64 x 1.94 m: scales along z under the lintel (1 = down, 0.0284 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Leitstand_106` | сворачивается | z, 1…0,0284 | (4.944, −5.94, 2.75) | BaseMain | 18 | roller-shutter curtain, 2.64 x 1.94 m: scales along z under the lintel (1 = down, 0.0284 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Leitstand_107` | сворачивается | z, 1…0,0284 | (6.958, −5.94, 2.75) | BaseMain | 18 | roller-shutter curtain, 1.36 x 1.94 m: scales along z under the lintel (1 = down, 0.0284 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Lager_108` | сворачивается | z, 1…0,05 | (−4.9, 5.94, 2.3) | BaseMain | 34 | roller-shutter curtain, 3.38 x 0.89 m: scales along z under the lintel (1 = down, 0.05 = rolled up; start rolled up) (на старте поднята) |
| `IX_ShutterWinder_Lager_109` | петля | (0, −1, 0), 0…0 ° | (−3.09, 5.646, 1) | BaseMain | 32 | roller-shutter belt winder: pull to lower / raise the curtain(s) it drives (ведёт IX_Shutter_Lager_108) |
| `IX_Shutter_Leitstand_110` | сворачивается | z, 1…0,0284 | (7.94, −3.65, 2.75) | BaseMain | 18 | roller-shutter curtain, 3.68 x 1.94 m: scales along z under the lintel (1 = down, 0.0284 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Kaserne_111` | сворачивается | z, 1…0,0427 | (−5.8, −5.94, 5.6) | BaseMain | 258 | roller-shutter curtain, 2.38 x 1.29 m: scales along z under the lintel (1 = down, 0.0427 = rolled up; start rolled up) (на старте поднята) |
| `IX_ShutterWinder_Kaserne_112` | петля | (0, 1, 0), 0…0 ° | (−7.11, −5.646, 4.4) | BaseMain | 256 | roller-shutter belt winder: pull to lower / raise the curtain(s) it drives (ведёт IX_Shutter_Kaserne_111) |
| `IX_Shutter_Kaserne_113` | сворачивается | z, 1…0,0427 | (−3.2, −5.94, 5.6) | BaseMain | 258 | roller-shutter curtain, 1.98 x 1.29 m: scales along z under the lintel (1 = down, 0.0427 = rolled up; start rolled up) (на старте поднята) |
| `IX_ShutterWinder_Kaserne_114` | петля | (0, 1, 0), 0…0 ° | (−2.09, −5.646, 4.4) | BaseMain | 256 | roller-shutter belt winder: pull to lower / raise the curtain(s) it drives (ведёт IX_Shutter_Kaserne_113) |
| `IX_Shutter_Kueche_115` | сворачивается | z, 1…0,0427 | (0.511, −5.94, 5.6) | BaseMain | 130 | roller-shutter curtain, 2.60 x 1.29 m: scales along z under the lintel (1 = down, 0.0427 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Kueche_116` | сворачивается | z, 1…0,0427 | (3.1, −5.94, 5.6) | BaseMain | 130 | roller-shutter curtain, 2.55 x 1.29 m: scales along z under the lintel (1 = down, 0.0427 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Kueche_117` | сворачивается | z, 1…0,0427 | (5.689, −5.94, 5.6) | BaseMain | 130 | roller-shutter curtain, 2.60 x 1.29 m: scales along z under the lintel (1 = down, 0.0427 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Waschraum_118` | сворачивается | z, 1…0,05 | (−5.4, 5.94, 5.65) | BaseMain | 514 | roller-shutter curtain, 2.38 x 0.64 m: scales along z under the lintel (1 = down, 0.05 = rolled up; start rolled up) (на старте поднята) |
| `IX_ShutterWinder_Waschraum_119` | петля | (0, −1, 0), 0…0 ° | (−4.09, 5.646, 4.4) | BaseMain | 512 | roller-shutter belt winder: pull to lower / raise the curtain(s) it drives (ведёт IX_Shutter_Waschraum_118) |
| `IX_Shutter_Stationsleiter_120` | сворачивается | z, 1…0,0427 | (4.128, 5.94, 5.6) | BaseMain | 18 | roller-shutter curtain, 2.63 x 1.29 m: scales along z under the lintel (1 = down, 0.0427 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Stationsleiter_121` | сворачивается | z, 1…0,0427 | (6.122, 5.94, 5.6) | BaseMain | 18 | roller-shutter curtain, 1.33 x 1.29 m: scales along z under the lintel (1 = down, 0.0427 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Kueche_122` | сворачивается | z, 1…0,0427 | (7.94, −3.5, 5.6) | BaseMain | 130 | roller-shutter curtain, 2.98 x 1.29 m: scales along z under the lintel (1 = down, 0.0427 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Flur_OG_123` | сворачивается | z, 1…0,0427 | (7.94, 0, 5.6) | BaseMain | 10 | roller-shutter curtain, 1.18 x 1.29 m: scales along z under the lintel (1 = down, 0.0427 = rolled up; start rolled up) (на старте поднята) |
| `IX_ShutterWinder_Flur_OG_124` | петля | (−1, 0, 0), 0…0 ° | (7.646, 0.71, 4.4) | BaseMain | 24 | roller-shutter belt winder: pull to lower / raise the curtain(s) it drives (ведёт IX_Shutter_Flur_OG_123) |
| `IX_Shutter_Stationsleiter_125` | сворачивается | z, 1…0,0427 | (7.94, 3.6, 5.6) | BaseMain | 18 | roller-shutter curtain, 2.78 x 1.29 m: scales along z under the lintel (1 = down, 0.0427 = rolled up; start rolled up) (на старте поднята) |
| `IX_Shutter_Flur_OG_126` | сворачивается | z, 1…0,0463 | (−7.94, 0, 5.6) | BaseMain | 10 | roller-shutter curtain, 1.18 x 1.19 m: scales along z under the lintel (1 = down, 0.0463 = rolled up; start rolled up) (на старте поднята) |
| `IX_ShutterWinder_Flur_OG_127` | петля | (1, 0, 0), 0…0 ° | (−7.646, 0.71, 4.4) | BaseMain | 520 | roller-shutter belt winder: pull to lower / raise the curtain(s) it drives (ведёт IX_Shutter_Flur_OG_126) |
| `IX_ShutterSwitch_Leitstand_128` | петля | (−1, 0, 0), −9…9 ° | (−2.28, −1.167, 1.136) | BaseMain | 20 | roller shutters, electric (Leitstand): all six curtains; needs mains (ведёт IX_Shutter_Leitstand_103, IX_Shutter_Leitstand_104, IX_Shutter_Leitstand_105, IX_Shutter_Leitstand_106, IX_Shutter_Leitstand_107, IX_Shutter_Leitstand_110) |
| `IX_ShutterSwitch_Kueche_129` | петля | (−1, 0, 0), −9…9 ° | (2.82, −1.167, 4.536) | BaseMain | 136 | roller shutters, electric (Kueche): all four curtains; needs mains (ведёт IX_Shutter_Kueche_115, IX_Shutter_Kueche_116, IX_Shutter_Kueche_117, IX_Shutter_Kueche_122) |
| `IX_ShutterSwitch_Stationsleiter_130` | петля | (1, 0, 0), −9…9 ° | (2.38, 1.167, 4.536) | BaseMain | 24 | roller shutters, electric (Stationsleiter): three curtains; needs mains (ведёт IX_Shutter_Stationsleiter_120, IX_Shutter_Stationsleiter_121, IX_Shutter_Stationsleiter_125) |
| `IX_HeatingStop_131` | петля | (1, 0, 0), −10…10 ° | (1.55, −0.934, −1.5) | BaseMain | 160 | emergency heating switch (Heizung NOT-AUS) by the boiler room, -10..10 deg |
| `IX_HeatPump_304` | петля | (1, 0, 0), −25…25 ° | (0.76, −1.338, −1.736) | BaseMain_Opening | 160 | heating: rocker switch (pump / burner), -25..25 deg |
| `IX_HeatBurner_305` | петля | (1, 0, 0), −25…25 ° | (0.86, −1.338, −1.736) | BaseMain_Opening | 160 | heating: rocker switch (pump / burner), -25..25 deg |
| `IX_HeatThermostat_306` | петля | (0, 1, 0), 0…270 ° | (1.06, −1.339, −1.734) | BaseMain_Opening | 160 | heating thermostat knob, 0..270 deg |
| `IX_HeatFill_307` | петля | (0, 0, 1), 0…90 ° | (5.56, −1.37, −2.468) | BaseMain_Opening | 160 | heating fill tap lever, 0..90 deg |
| `IX_BurnerReset_308` | выдвигается | (0, 0, −1), 0…0,006 м | (6.17, −2.42, −2.083) | BaseMain_Opening | 160 | burner reset button: pressed 0..6 mm (down); its red cap lights on a burner fault (M_HeatFault) |
| `IX_HeatGauge_309` | петля | (0, 1, 0), 0…270 ° | (6.6, −1.404, −1.22) | BaseMain_Opening | 160 | gauge needle (the game sets 0..270 deg) |
| `IX_HeatTempGauge_310` | петля | (0, 1, 0), 0…270 ° | (5.98, −2.165, −1.62) | BaseMain_Opening | 160 | gauge needle (the game sets 0..270 deg) |
| `IX_MainDoorBar_311` | петля | (0, −1, 0), 0…85 ° | (−7.18, −5.705, 1.055) | BaseMain_Opening | 128 | entrance door bar: 0 = across the door (in the cradle), 85 = raised; turns in the door niche |

## Светильники

64 светильника обновления 3 (`FX_<тип>_<n>`) на своих местах и в своих поворотах, у каждого — корпус в атласе Parts и светящаяся часть с материалом обновления 3 (`M_FX_Tube`, `M_FX_Globe`, `M_FX_Diffuser`, `M_FX_Bulb`, `M_FX_Exit` — указатель выхода, UV 0…1 по лицевой стороне, как было, `M_FX_EmergencyLens`, `M_FX_Halogen`, `M_FX_Red`, `M_LEDOn`). Источники света и ореолы (`prefabLights`, `prefabGlows` в контракте) — те же 63 и 63 записи обновления 3: светильники стоят там же, поэтому координаты, направления, цвета и яркости переносятся без изменений.

| Светильник | Тип | Комната | Свет | Материалы |
|---|---|---|---|---|
| `FX_BulkheadCeiling_1` | BulkheadCeiling | Windfang | incandescent, power, point i 0.8 | M_Main_Parts, M_FX_Globe |
| `FX_Raster_2` | Raster | Leitstand | fluorescent, power, spot i 1.0 | M_Main_Parts, M_FX_Tube |
| `FX_Raster_3` | Raster | Leitstand | fluorescent, power, spot i 1.0 | M_Main_Parts, M_FX_Tube |
| `FX_Raster_4` | Raster | Leitstand | fluorescent, power, spot i 1.0 | M_Main_Parts, M_FX_Tube |
| `FX_Raster_5` | Raster | Leitstand | fluorescent, power, spot i 1.0 | M_Main_Parts, M_FX_Tube |
| `FX_DeskLamp_6` | DeskLamp | Leitstand | incandescent, power, spot i 0.6 | M_Main_Parts, M_FX_Bulb |
| `FX_DeskLamp_7` | DeskLamp | Leitstand | incandescent, power, spot i 0.6 | M_Main_Parts, M_FX_Bulb |
| `FX_Raster_8` | Raster | Flur_EG | fluorescent, power, spot i 1.0 | M_Main_Parts, M_FX_Tube |
| `FX_Raster_9` | Raster | Flur_EG | fluorescent, power, spot i 1.0 | M_Main_Parts, M_FX_Tube |
| `FX_Raster_10` | Raster | Flur_EG | fluorescent, power, spot i 1.0 | M_Main_Parts, M_FX_Tube |
| `FX_Raster_11` | Raster | Flur_EG | fluorescent, power, spot i 1.0 | M_Main_Parts, M_FX_Tube |
| `FX_FluorBatten_12` | FluorBatten | Lager | fluorescent, power, spot i 1.1 | M_Main_Parts, M_FX_Diffuser |
| `FX_FluorBatten_13` | FluorBatten | Lager | fluorescent, power, spot i 1.1 | M_Main_Parts, M_FX_Diffuser |
| `FX_Raster_14` | Raster | — | — | M_Main_Parts |
| `FX_Raster_15` | Raster | Serverraum | fluorescent, power, spot i 1.0 | M_Main_Parts, M_FX_Tube |
| `FX_Pendant_16` | Pendant | Garage | incandescent, power, spot i 1.2 | M_Main_Parts, M_FX_Bulb |
| `FX_Pendant_17` | Pendant | Garage | incandescent, power, spot i 1.2 | M_Main_Parts, M_FX_Bulb |
| `FX_Pendant_18` | Pendant | Garage | incandescent, power, spot i 1.2 | M_Main_Parts, M_FX_Bulb |
| `FX_Pendant_19` | Pendant | Garage | incandescent, power, spot i 1.2 | M_Main_Parts, M_FX_Bulb |
| `FX_FluorBatten_20` | FluorBatten | Garage | fluorescent, power, spot i 1.1 | M_Main_Parts, M_FX_Diffuser |
| `FX_Bulkhead_21` | Bulkhead | Treppenhaus | incandescent, power, point i 0.8 | M_Main_Parts, M_FX_Globe |
| `FX_Bulkhead_22` | Bulkhead | Treppenhaus | incandescent, power, point i 0.8 | M_Main_Parts, M_FX_Globe |
| `FX_Bulkhead_23` | Bulkhead | Treppenhaus | incandescent, power, point i 0.8 | M_Main_Parts, M_FX_Globe |
| `FX_Bulkhead_24` | Bulkhead | Treppenhaus | incandescent, power, point i 0.8 | M_Main_Parts, M_FX_Globe |
| `FX_FluorBatten2_25` | FluorBatten2 | Kaserne | fluorescent, power, spot i 1.5 | M_Main_Parts, M_FX_Diffuser |
| `FX_FluorBatten2_26` | FluorBatten2 | Kaserne | fluorescent, power, spot i 1.5 | M_Main_Parts, M_FX_Diffuser |
| `FX_FluorBatten2_27` | FluorBatten2 | Kueche | fluorescent, power, spot i 1.5 | M_Main_Parts, M_FX_Diffuser |
| `FX_FluorBatten2_28` | FluorBatten2 | Kueche | fluorescent, power, spot i 1.5 | M_Main_Parts, M_FX_Diffuser |
| `FX_FluorBatten_29` | FluorBatten | Kueche | fluorescent, power, spot i 1.1 | M_Main_Parts, M_FX_Diffuser |
| `FX_Pendant_30` | Pendant | Kueche | incandescent, power, spot i 0.9 | M_Main_Parts, M_FX_Bulb |
| `FX_FluorBatten_31` | FluorBatten | Flur_OG | fluorescent, power, spot i 1.1 | M_Main_Parts, M_FX_Diffuser |
| `FX_FluorBatten_32` | FluorBatten | Flur_OG | fluorescent, power, spot i 1.1 | M_Main_Parts, M_FX_Diffuser |
| `FX_FluorBatten_33` | FluorBatten | Waschraum | fluorescent, power, spot i 1.1 | M_Main_Parts, M_FX_Diffuser |
| `FX_BulkheadCeiling_34` | BulkheadCeiling | Waschraum | incandescent, power, point i 0.8 | M_Main_Parts, M_FX_Globe |
| `FX_Pendant_35` | Pendant | Stationsleiter | incandescent, power, spot i 1.0 | M_Main_Parts, M_FX_Bulb |
| `FX_DeskLamp_36` | DeskLamp | Stationsleiter | incandescent, power, spot i 0.6 | M_Main_Parts, M_FX_Bulb |
| `FX_FluorBatten_37` | FluorBatten | Heizung | fluorescent, power, spot i 1.1 | M_Main_Parts, M_FX_Diffuser |
| `FX_FluorBatten_38` | FluorBatten | Heizung | fluorescent, power, spot i 1.1 | M_Main_Parts, M_FX_Diffuser |
| `FX_Bulkhead_39` | Bulkhead | Heizung | incandescent, power, point i 0.8 | M_Main_Parts, M_FX_Globe |
| `FX_Bulkhead_40` | Bulkhead | Flur_UG | incandescent, power, point i 0.8 | M_Main_Parts, M_FX_Globe |
| `FX_Bulkhead_41` | Bulkhead | Flur_UG | incandescent, power, point i 0.8 | M_Main_Parts, M_FX_Globe |
| `FX_FluorBatten_42` | FluorBatten | Schaltraum | fluorescent, power, spot i 1.1 | M_Main_Parts, M_FX_Diffuser |
| `FX_ExitSign_43` | ExitSign | Windfang | exit, battery, point i 0.12 | M_Main_Parts, M_FX_Exit |
| `FX_ExitSign_44` | ExitSign | Flur_EG | exit, battery, point i 0.12 | M_Main_Parts, M_FX_Exit |
| `FX_ExitSign_45` | ExitSign | Flur_EG | exit, battery, point i 0.12 | M_Main_Parts, M_FX_Exit |
| `FX_ExitSign_46` | ExitSign | Garage | exit, battery, point i 0.12 | M_Main_Parts, M_FX_Exit |
| `FX_EmergencyLight_47` | EmergencyLight | Windfang | emergency, battery, spot i 0.45 | M_Main_Parts, M_FX_EmergencyLens, M_LEDOn |
| `FX_EmergencyLight_48` | EmergencyLight | Leitstand | emergency, battery, spot i 0.45 | M_Main_Parts, M_FX_EmergencyLens, M_LEDOn |
| `FX_EmergencyLight_49` | EmergencyLight | Flur_EG | emergency, battery, spot i 0.45 | M_Main_Parts, M_FX_EmergencyLens, M_LEDOn |
| `FX_EmergencyLight_50` | EmergencyLight | Flur_EG | emergency, battery, spot i 0.45 | M_Main_Parts, M_FX_EmergencyLens, M_LEDOn |
| `FX_EmergencyLight_51` | EmergencyLight | Serverraum | emergency, battery, spot i 0.45 | M_Main_Parts, M_FX_EmergencyLens, M_LEDOn |
| `FX_EmergencyLight_52` | EmergencyLight | Garage | emergency, battery, spot i 0.45 | M_Main_Parts, M_FX_EmergencyLens, M_LEDOn |
| `FX_EmergencyLight_53` | EmergencyLight | Treppenhaus | emergency, battery, spot i 0.45 | M_Main_Parts, M_FX_EmergencyLens, M_LEDOn |
| `FX_EmergencyLight_54` | EmergencyLight | Treppenhaus | emergency, battery, spot i 0.45 | M_Main_Parts, M_FX_EmergencyLens, M_LEDOn |
| `FX_EmergencyLight_55` | EmergencyLight | Treppenhaus | emergency, battery, spot i 0.45 | M_Main_Parts, M_FX_EmergencyLens, M_LEDOn |
| `FX_EmergencyLight_56` | EmergencyLight | Flur_OG | emergency, battery, spot i 0.45 | M_Main_Parts, M_FX_EmergencyLens, M_LEDOn |
| `FX_EmergencyLight_57` | EmergencyLight | Flur_UG | emergency, battery, spot i 0.45 | M_Main_Parts, M_FX_EmergencyLens, M_LEDOn |
| `FX_EmergencyLight_58` | EmergencyLight | Schaltraum | emergency, battery, spot i 0.6 | M_Main_Parts, M_FX_EmergencyLens, M_LEDOn |
| `FX_Bulkhead_59` | Bulkhead | Exterior | incandescent, power, point i 0.9 | M_Main_Parts, M_FX_Globe |
| `FX_WallFlood_60` | WallFlood | Exterior | halogen, power, spot i 2.6 | M_Main_Parts, M_FX_Halogen |
| `FX_Bulkhead_61` | Bulkhead | Exterior | incandescent, power, point i 0.9 | M_Main_Parts, M_FX_Globe |
| `FX_WallFlood_62` | WallFlood | Exterior | halogen, power, spot i 3.0 | M_Main_Parts, M_FX_Halogen |
| `FX_Bulkhead_63` | Bulkhead | Exterior | incandescent, power, point i 0.8 | M_Main_Parts, M_FX_Globe |
| `FX_Obstruction_64` | Obstruction | Exterior | beacon, power, point i 0.6 | M_Main_Parts, M_FX_Red |

## Материалы

- Запечённые атласы: `M_Main_Ext` (корпус снаружи), `M_Main_Rm_<Room>` (по комнате), `M_Main_Parts` (двери, окна, ставни, ящики, выключатели, корпуса светильников), `M_Main_Opening` (регулятор, засов). Фильтр «точка», без мипмапов, шейдер PSX проекта.
- `M_Main_Panel` — таблички, бумаги, планы, циферблаты, наклейки на противопожарных дверях; карта свечения для указателя выхода и линз; ночное свечение 0,06 (как `M_ObsSigns`).
- Материалы обновления 3 по имени — их определения остаются в `Forest_Materials.json`: `M_ScreenOff` (экраны пультов, ЭЛТ, терминала, телевизора), `M_LEDOff` (светодиоды пультов и стоек), `M_HeatFault` (лампа STÖRUNG, кнопка сброса), `M_FX_*` и `M_LEDOn` (светильники), `M_GlassClear` (окна, стеклянная дверь, перегородка серверной, душ), `M_GlassDark` (окошко входной двери, дверцы стоек).

## Точки взаимодействия

**Стук** (28):

- `Knock_Shutter_Leitstand_103` (−3.044, −5.951, 1.775) на `IX_Shutter_Leitstand_103` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Leitstand_104` (−0.366, −5.951, 1.775) на `IX_Shutter_Leitstand_104` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Leitstand_105` (2.289, −5.951, 1.775) на `IX_Shutter_Leitstand_105` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Leitstand_106` (4.944, −5.951, 1.775) на `IX_Shutter_Leitstand_106` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Leitstand_107` (6.958, −5.951, 1.775) на `IX_Shutter_Leitstand_107` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Leitstand_110` (7.951, −3.65, 1.775) на `IX_Shutter_Leitstand_110` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Lager_108` (−4.9, 5.951, 1.85) на `IX_Shutter_Lager_108` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Kaserne_111` (−5.8, −5.951, 4.95) на `IX_Shutter_Kaserne_111` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Kaserne_113` (−3.2, −5.951, 4.95) на `IX_Shutter_Kaserne_113` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Kueche_115` (0.511, −5.951, 4.95) на `IX_Shutter_Kueche_115` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Kueche_116` (3.1, −5.951, 4.95) на `IX_Shutter_Kueche_116` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Kueche_117` (5.689, −5.951, 4.95) на `IX_Shutter_Kueche_117` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Kueche_122` (7.951, −3.5, 4.95) на `IX_Shutter_Kueche_122` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Flur_OG_123` (7.951, 0, 4.95) на `IX_Shutter_Flur_OG_123` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Flur_OG_126` (−7.951, 0, 5) на `IX_Shutter_Flur_OG_126` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Waschraum_118` (−5.4, 5.951, 5.325) на `IX_Shutter_Waschraum_118` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Stationsleiter_120` (4.128, 5.951, 4.95) на `IX_Shutter_Stationsleiter_120` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Stationsleiter_121` (6.122, 5.951, 4.95) на `IX_Shutter_Stationsleiter_121` — something knocks / scratches on the shutter (window) from outside
- `Knock_Shutter_Stationsleiter_125` (7.951, 3.6, 4.95) на `IX_Shutter_Stationsleiter_125` — something knocks / scratches on the shutter (window) from outside
- `Knock_W_Garage_N` (−13.9, 4.95, 2.45) на `R_Garage` — something knocks on the glass from outside
- `Knock_W_Garage_W` (−17.95, 0, 2.45) на `R_Garage` — something knocks on the glass from outside
- `Knock_W_Tower_N1` (−0.725, 6.88, 4.75) на `R_Treppenhaus` — something knocks on the glass from outside
- `Knock_W_Tower_N2` (0.725, 6.88, 4.75) на `R_Treppenhaus` — something knocks on the glass from outside
- `Knock_MainDoor_1` (−6.45, −5.99, 1.25) на `IX_MainDoor_1` — fists on the door from outside
- `Knock_ExitDoor_2` (7.99, 0, 1.25) на `IX_ExitDoor_2` — fists on the door from outside
- `Knock_RoofDoor_17` (0, 1.01, 7.86) на `IX_RoofDoor_17` — fists on the door from outside
- `Knock_GarageDoor_19` (−10.825, −4.99, 1.1) на `IX_GarageDoor_19` — fists on the door from outside
- `Knock_GarageGate_18` (−14.85, −4.725, 1.3) на `IX_GarageGate_18` — the gate rattles: hit from outside

**Рука** (37):

- `Hand_MainDoor_1_A` (−5.98, −6.07, 1.05) на `IX_MainDoor_1` — the handle (lever / pull)
- `Hand_MainDoor_1_B` (−5.98, −5.58, 1.05) на `IX_MainDoor_1` — the handle (lever / pull)
- `Hand_ExitDoor_2_A` (7.58, 0.47, 1.05) на `IX_ExitDoor_2` — the handle (lever / pull)
- `Hand_ExitDoor_2_B` (8.07, 0.47, 1.05) на `IX_ExitDoor_2` — the handle (lever / pull)
- `Hand_Door_3_A` (−8.07, 0.47, 1.05) на `IX_Door_3` — the handle (lever / pull)
- `Hand_Door_3_B` (−7.58, 0.47, 1.05) на `IX_Door_3` — the handle (lever / pull)
- `Hand_Door_4_A` (−8.07, 3.08, 1.05) на `IX_Door_4` — the handle (lever / pull)
- `Hand_Door_4_B` (−7.58, 3.08, 1.05) на `IX_Door_4` — the handle (lever / pull)
- `Hand_Door_5_A` (−6.92, −1.22, 1.05) на `IX_Door_5` — the handle (lever / pull)
- `Hand_Door_5_B` (−6.92, −0.93, 1.05) на `IX_Door_5` — the handle (lever / pull)
- `Hand_Door_6_A` (−2.58, −1.22, 1.05) на `IX_Door_6` — the handle (lever / pull)
- `Hand_Door_6_B` (−2.58, −0.93, 1.05) на `IX_Door_6` — the handle (lever / pull)
- `Hand_Door_7_A` (−4.58, 0.93, 1.05) на `IX_Door_7` — the handle (lever / pull)
- `Hand_Door_7_B` (−4.58, 1.22, 1.05) на `IX_Door_7` — the handle (lever / pull)
- `Hand_Door_8_A` (0.52, 0.93, 1.05) на `IX_Door_8` — the handle (lever / pull)
- `Hand_Door_8_B` (0.52, 1.22, 1.05) на `IX_Door_8` — the handle (lever / pull)
- `Hand_Door_9_A` (2.68, 0.93, 1.05) на `IX_Door_9` — the handle (lever / pull)
- `Hand_Door_9_B` (2.68, 1.22, 1.05) на `IX_Door_9` — the handle (lever / pull)
- `Hand_Door_10_A` (−2.58, −1.22, 4.45) на `IX_Door_10` — the handle (lever / pull)
- `Hand_Door_10_B` (−2.58, −0.93, 4.45) на `IX_Door_10` — the handle (lever / pull)
- `Hand_Door_11_A` (−4.68, 0.93, 4.45) на `IX_Door_11` — the handle (lever / pull)
- `Hand_Door_11_B` (−4.68, 1.22, 4.45) на `IX_Door_11` — the handle (lever / pull)
- `Hand_Door_12_A` (0.52, 0.93, 4.45) на `IX_Door_12` — the handle (lever / pull)
- `Hand_Door_12_B` (0.52, 1.22, 4.45) на `IX_Door_12` — the handle (lever / pull)
- `Hand_Door_13_A` (2.68, 0.93, 4.45) на `IX_Door_13` — the handle (lever / pull)
- `Hand_Door_13_B` (2.68, 1.22, 4.45) на `IX_Door_13` — the handle (lever / pull)
- `Hand_Door_14_A` (2.87, −1.22, −1.95) на `IX_Door_14` — the handle (lever / pull)
- `Hand_Door_14_B` (2.87, −0.93, −1.95) на `IX_Door_14` — the handle (lever / pull)
- `Hand_Door_15_A` (0.52, 0.93, −1.95) на `IX_Door_15` — the handle (lever / pull)
- `Hand_Door_15_B` (0.52, 1.22, −1.95) на `IX_Door_15` — the handle (lever / pull)
- `Hand_Door_16_A` (4.47, 0.93, −1.95) на `IX_Door_16` — the handle (lever / pull)
- `Hand_Door_16_B` (4.47, 1.22, −1.95) на `IX_Door_16` — the handle (lever / pull)
- `Hand_RoofDoor_17_A` (0.52, 0.93, 7.66) на `IX_RoofDoor_17` — the handle (lever / pull)
- `Hand_RoofDoor_17_B` (0.52, 1.22, 7.66) на `IX_RoofDoor_17` — the handle (lever / pull)
- `Hand_GarageDoor_19_A` (−10.43, −5.07, 0.9) на `IX_GarageDoor_19` — the handle (lever / pull)
- `Hand_GarageDoor_19_B` (−10.43, −4.63, 0.9) на `IX_GarageDoor_19` — the handle (lever / pull)
- `Hand_GarageGate_18` (−14.85, −5.12, 0.8) на `IX_GarageGate_18` — the gate's handle outside (up-and-over: lift here)

**Звук** (6):

- `Audio_Burner` (6.17, −2.32, −2.22) на `BaseMain` — the oil burner: ignition click, the roar, the fault hum
- `Audio_Boiler` (6.25, −1.65, −2) на `BaseMain` — the boiler ticks as it heats and cools
- `Audio_ServerRacks` (4.13, 5, 1) на `BaseMain` — fans and relays in the racks (with power)
- `Audio_Fridge` (7.09, −1.785, 4.3) на `BaseMain` — the fridge hums and clicks (with power)
- `Audio_Chimney` (7.4, −1.4, 2) на `BaseMain` — wind in the flue
- `Audio_GarageGate` (−14.85, −4.85, 2) на `BaseMain` — the gate: springs, the steel rattles

Стук (`knock`) — снаружи на каждой ставне (точка на полотне, опущенном), на окнах без ставен, на наружных дверях и воротах: для пугалок X06 / X07 пролога. Рука (`hand`) — ручки дверей с обеих сторон.

## Отличия от обновления 3

- Засов входной двери лежит в нише 11 см вокруг двери (в обновлении 3 он проходил сквозь стену); ручной пожарный извещатель тамбура сдвинут к x −5,50 из-за ниши.
- Основание настольной лампы `FX_DeskLamp_36` (кабинет начальника станции) поставлено на стол, к x 5,15, y 3,62 (в обновлении 3 висело за краем стола; на столе не задевает конверт и лекарство из `Forest_Dressing.json`); свет трёх настольных ламп — как в обновлении 3 (направлен к стене).
- Ящики и гружёные поддоны склада и гаража стоят повёрнутыми, как в обновлении 3 (сняты с его модели как повёрнутые коробки): предметы `Forest_Dressing.json` рядом с ними стоят как прежде.
- Синяя бочка гаража вынесена из стеллажа (в обновлении 3 стояла внутри него); аптечка кухни — на y −1,95.
- Табличка с именем станции — текст обновления 3 (без даты; записка chief_safe ссылается на неё — оставлено как было).
- Экраны и светодиоды выделены в материалы `M_ScreenOff` / `M_LEDOff` там же, где они были в обновлении 3 (пультовая, серверная, кухня).
- Основание настольной лампы `FX_DeskLamp_6` (пультовая) сдвинуто к x −0,62, y −1,47: на прежнем месте лежит планшет из `Forest_Dressing.json` (`Leitstand_Clipboard_1`).
- Двери поворачиваются вокруг осей обновления 3 (посередине толщины полотна): коробки сделаны с четвертью — со стороны открывания косяк у петель отступает, с другой стороны упорные планки закрывают щели, так что полотно не уходит в коробку и сквозь закрытую дверь не видно света. Полотна на 3 мм меньше проёма.
- Рычаги главного щита, главный выключатель и автоматы щитков: лицевые панели стоят за осями поворота (в обновлении 3 рычаги при повороте уходили в панель); у рычагов — прорези, у главного выключателя — подшипники в П-образной плите; автоматы с выемкой под клавишу.
- Шкафчики, тумбы, двери стоек: тыльная часть полотна у петель утоплена, дверцы открываются, не задевая корпус; двойной шкаф казармы стал на 6 см уже (дверца шкафчика `IX_LockerDoor_57` открывается на 110°).
- Стрелки манометра и термометра котла укорочены до 0,86 радиуса шкалы (в обновлении 3 задевали ободок).

## Значения обновления 3, которые не сходятся с его же планировкой

Оставлены как в обновлении 3 (имена, оси, диапазоны не менялись); исправлять — в данных игры:

- `IX_Drawer_31` … `IX_Drawer_34`: ящики выдвигаются на 0…0,5 м, но в 0,20 м перед их фасадами стоит тумба ящиков `IX_Drawer_35…37` (угловой стол пультовой) — дальше 0,20 м ящик уходит в неё. Рекомендуемый максимум 0,20.

## Изменения

- 1.1: стекло окон, стеклянной двери тамбура и перегородки серверной — по одной грани на каждую сторону (в 1.0 каждая грань была дважды: стекло в Unity выглядело бы темнее и рисовалось дважды); геометрия и атласы — без изменений.

## Проверки

- Сверка с обновлением 3: деталей 139, расхождений по виду/оси/ходу 0; масок света 213, расхождений 0; источников 63 и ореолов 63 — светильники на месте; лишних объектов нет
- Проходимость (капсула игрока r 0,34, рост 1,80, шаг 0,35): все 14 комнат пройдены от входа и с площадок лестницы; лестница по ступеням: марш -3,0→0,0 м — над ступенями не меньше 2,67 м, от оси марша до перил и стен 0,49 м (капсуле нужно 0,34); марш 0,0→3,4 м — над ступенями не меньше 2,90 м, от оси марша до перил и стен 0,49 м (капсуле нужно 0,34); марш 3,4→6,6 м — над ступенями не меньше 2,80 м, от оси марша до перил и стен 0,49 м (капсуле нужно 0,34); деталей вне досягаемости по правилу игры (2,0 м от глаз, луч не перекрыт): 0
- Ход подвижных деталей: 114 деталей прокручены по всему ходу; заходят во что-то глубже 3 мм: 5 — ящики IX_Drawer_31…34 (ошибка данных обновления 3, см. раздел выше); IX_Door_5: стеклянная дверь при полных 95° заходит на 5 мм в угол стены за наличником (не видно)
- Грани изнанкой к зрителю: 41280 лучей из всех комнат и вокруг здания; изнанка первым попаданием — 6 раз (скользящие касания рёбер: угол направляющей ворот гаража, перекрестья решёток растровых светильников)
- Совпадающие грани (мерцание): перекрытий в одной плоскости: 5, из них надписи/линзы/стекло (отступ 1,5 мм по замыслу) 5, между гранями атласов 0
- Предметы и надписи пролога (Forest_Dressing.json): на поверхностях и со свободным местом: 213; вплотную к мебели или стене: 8 — стоят так же, как в обновлении 3 (сверено с его моделью); надписей на полу не на месте: 0; зоны сна Sleep_1, Sleep_2, Sleep_3, Sleep_4 лежат на матрасах (низ зоны на 5 мм ниже верха матраса)

## Для обновления 4 (Unity)

1. Заменить модели `BaseMain.fbx` и `BaseMain_Opening.fbx` экспортом из `main.blend` (корни `BaseMain` и `BaseMain_Opening`), префабы и их места на карте — прежние. Имена объектов не менять: `_layoutHash` и вся логика пролога идут по именам.
2. Материалы `M_Main_*` — новые (текстуры из `tex/`, фильтр Point, без мипмапов, шейдер PSX). Материалы с именами обновления 3 переназначить на существующие в проекте.
3. Коллайдеры — MeshCollider на каждом объекте, как делал импорт обновления 3.
4. Маски света — `lightMask` у каждой детали; источники и ореолы — из прежних данных (`prefabLights`, `prefabGlows`), без изменений.
5. Проверить в игре: шаги S7–S10 пролога, ставни (масштаб по z), засов в нише, точки `Forest_Dressing.json` (стоят на поверхностях, проверено скриптом), зоны сна.
6. В данных игры поправить значения из раздела «Значения обновления 3, которые не сходятся с его же планировкой» (ящикам `IX_Drawer_31…34` — максимум хода 0,20).


# Полный список звуков игры

26.10.2026. Собран по коду, данным сцен (`Forest_*.json`, `Forest.unity`) и контрактам `docs/gdd/*_CONTRACT.md`, `ENTITIES.md`, `EVENTS.md`, `DAYS.md`, `MONSTERS.md`. Одна строка — один звук: где открыть и закрыть звучат по-разному, это две строки. Короткий обзор прежней версии — `SOUND_LIST.md`; голоса — `VOICE_LIST.md`, здесь их нет (кроме звуков тела и служебных звуков рации).

Значки: ✅ — звучит (cue есть, код играет); 🔇 — код просит звук, файла нет; ⬜ — нет ни файла, ни кода. Шум — на каком расстоянии звук слышат существа (`NoiseEvents`): тихо 5 м, средне 15 м, громко 30 м. `prompt` — описание для генерации (английский: так модели понимают лучше). `when` — крючок в коде, если он есть; «нет кода» — звук надо подключать вместе с самим действием.

| Зона | Звуков | ✅ | 🔇 | ⬜ |
|---|---|---|---|---|
| 1. КПП, лес, хижина, дорога, заборы, погода, хоррор и монстры | 247 | 21 | 1 | 224 |
| 2. База: здания, машины, двери, ставни, мебель, свет | 275 | 12 | 4 | 259 |
| 3. Фургон и самолёт | 228 | 2 | 1 | 225 |
| 4. Персонаж, предметы, ручная рация, интерфейс, музыка | 337 | 36 | 13 | 288 |
| **Итого** | **≈1090** | **71** | **19** | **≈1000** |

Числа ✅ считают и строки «похоже на ✅ …», где подойдёт уже готовый звук. Зоны частично пересекаются (генератор, трансформатор, ветер), повторы уберу при генерации. Под каждой таблицей — открытые вопросы, найденные при сборке.

Порядок работы: генерация дублей по `prompt` → выбор на слух → подключение в коде (почти у всех ⬜ вызова звука нет).


---

## Звуки зоны КПП, леса, хижины, дороги, заборов, погоды и хоррора

Статус: ✅ — cue есть в `tools/audio/audio_manifest.json` и его играет код; 🔇 — код просит id или держит слот, а файла нет; ⬜ — нет ничего (в `when` указан готовый крючок в коде, если он есть). Шум — по `NoiseEvents`: тихо 5 м, средне 15 м, громко 30 м (это поле `noise` у cue в манифесте).

| id | what | when | where | 3D/2D | oneshot/loop | variants | шум | prompt | status |
|---|---|---|---|---|---|---|---|---|---|
| kpp.cabinet.door.open | дверца шкафа питания: петли, отлипает уплотнитель | деталь host `IX_CabinetDoor_314` открывается (ForestKpp.CabinetOpen) | PowerCabinet ×1, Audio_CabinetDoor | 3D | oneshot | 2 | тихо | steel electrical cabinet door opening, rubber seal unsticking, small hinge squeak, cold | ⬜ |
| kpp.cabinet.door.close | дверца шкафа: хлопок по уплотнителю, защёлка | то же, закрытие | PowerCabinet ×1 | 3D | oneshot | 2 | тихо | steel cabinet door shut against rubber seal, latch click | ⬜ |
| kpp.cabinet.switch.batt | переключатель 315 на BATTERIE: тугой поворотный клац | ForestKpp.ServerUse(Switch), _switchBatt→true | IX_CabinetSwitch_315, Audio_CabinetSwitch | 3D | oneshot | 2 | тихо | heavy rotary changeover switch clack, 1970s German switchgear | ⬜ |
| kpp.cabinet.switch.netz | переключатель обратно на NETZ (другой тон) | _switchBatt→false | IX_CabinetSwitch_315 | 3D | oneshot | 2 | тихо | rotary power switch turned back, stiff mechanical clunk | ⬜ |
| kpp.relay.on | в будке щёлкают реле, ток пришёл (каскад 2–4 щелчков) | ForestKpp.Apply: BoothPowered false→true | будка | 3D | oneshot | 2 | тихо | cascade of electromechanical relays clicking on, small hum rising | ⬜ |
| kpp.relay.off | реле отпадают, ток пропал (сняли клемму или аккумулятор) | BoothPowered true→false | будка | 3D | oneshot | 2 | тихо | relays dropping out, power dying, hum collapsing | ⬜ |
| kpp.clamp.on | клемму надели на штырь: металл, затяжка | ServerUse(ClampPos/Neg), c→true | IX_CabinetClamp_Pos_316 / _Neg_317 (×2), Audio_CabinetClamps | 3D | oneshot | 3 | тихо | car battery clamp pushed onto terminal post, metal scrape and snap | ⬜ |
| kpp.clamp.off | клемму сняли | ServerUse, c→false | ×2 | 3D | oneshot | 2 | тихо | battery clamp pulled off terminal, metal scrape | ⬜ |
| kpp.clamp.spark | искра, когда вторую клемму надевают при BATTERIE | вторая клемма при _switchBatt | ×2 | 3D | oneshot | 3 | тихо | short electric arc spark crackle on 12V battery terminal | ⬜ |
| kpp.battery.place | аккумулятор 18 кг ставят на полку | UseSlot → ServerStow | KPP_BatterySlot ×1 | 3D | oneshot | 2 | тихо | heavy car battery set down on steel shelf, plastic thud, scrape | ⬜ |
| kpp.battery.remove | аккумулятор снимают с полки | UseSlot → ServerUnstow | KPP_BatterySlot | 3D | oneshot | 2 | тихо | heavy battery dragged off steel shelf, lifted | ⬜ |
| kpp.doorlock.release | электрозамок отпускает: жужжание и щелчок | Apply: `_door.part.locked` true→false | DoorLock + DoorLockStrike, Audio_DoorLock | 3D | oneshot | 2 | тихо | electric door strike buzz then release click, 1980s | ⬜ |
| kpp.doorlock.engage | замок схватывает, когда ток пропал | locked false→true | DoorLockStrike | 3D | oneshot | 2 | тихо | electric lock bolt snapping shut, short clack | ⬜ |
| kpp.doorlock.rattle | дёрнули запертую дверь: ручка ходит, ригель держит | ForestKpp.TryUnlock → «Заперто электрозамком» | IX_BoothDoor_74 | 3D | oneshot | 3 | тихо | locked door handle rattled, bolt holds, glass pane buzzes | ⬜ |
| kpp.booth.door.open | дверь будки: защёлка, кованые петли | деталь host IX_BoothDoor_74 открывается | IX_BoothDoor_74, Audio_BoothDoor | 3D | oneshot | 3 | тихо | wooden guard booth door latch, iron hinges creak, cold | ⬜ |
| kpp.booth.door.close | закрыть: хлопок, защёлка, дребезг стекла | закрывается | IX_BoothDoor_74 | 3D | oneshot | 3 | средне | booth door slam, latch catch, glass pane rattles | ⬜ |
| kpp.booth.window.open | окошко выдачи: створка едет по полозьям | IX_BoothWindow_302 (Booth_PassWindow, slide) | ×1 | 3D | oneshot | 2 | тихо | sliding glass pass window on dry runners, squeak | ⬜ |
| kpp.booth.window.close | окошко выдачи: стук в упор | то же | ×1 | 3D | oneshot | 2 | тихо | sliding window knocks shut against frame | ⬜ |
| kpp.booth.drawer.desk.open | ящик тумбы стола (ДСП) открыть | IX_Drawer_75…77 | 3 ящика | 3D | oneshot | 3 | тихо | chipboard desk drawer pulled open, pens rolling | ⬜ |
| kpp.booth.drawer.desk.close | ящик тумбы закрыть | то же | 3 | 3D | oneshot | 3 | тихо | desk drawer pushed shut, wooden knock | ⬜ |
| kpp.booth.drawer.file.open | ящик металлической картотеки открыть | IX_Drawer_78…80 | 3 | 3D | oneshot | 3 | тихо | steel filing cabinet drawer rolling open on ball runners | ⬜ |
| kpp.booth.drawer.file.close | ящик картотеки закрыть | то же | 3 | 3D | oneshot | 3 | тихо | steel filing drawer slammed shut, metallic clang | ⬜ |
| kpp.booth.keybox.open | дверца ключницы, ключи звякают на крючках | IX_KeyBoxDoor_303 | ×1 | 3D | oneshot | 2 | тихо | small tin key cabinet door opening, keys jingle on hooks | ⬜ |
| kpp.booth.keybox.close | дверца ключницы закрыть | то же | ×1 | 3D | oneshot | 2 | тихо | tin key box door clicks shut | ⬜ |
| kpp.booth.chair | стул охранника скрипит, когда садятся | Seat_Chair (нет кода) | Kpp_Chair ×1 | 3D | oneshot | 3 | нет | old office chair creak as someone sits | ⬜ |
| kpp.console.button.press | кнопка пульта с током: щелчок и реле | ForestKpp.Press(powered) | 312, 313 (будка), 319, 320 (Leitstand) — 4 | 3D | oneshot | 3 | нет | industrial push button click with relay clack, control desk | ⬜ |
| kpp.console.button.dead | кнопка без тока: пустой глухой щелчок | Press(!powered) → «Нет тока» | ×4 | 3D | oneshot | 2 | нет | dead push button, hollow plastic click, nothing happens | ⬜ |
| kpp.console.lamp.on | лампа над кнопкой загорается: тихий тик | Power(btn…) off→on | ×4 | 3D | oneshot | 1 | нет | tiny indicator lamp relay tick | ⬜ |
| kpp.crt.on | ЭЛТ: писк кинескопа и «бумм» размагничивания | Power(boothMonitors / leitMonitors) off→on | 2 в будке + 2 в Leitstand = 4, Audio_Monitor | 3D | oneshot | 3 | нет | CRT monitor power on, degauss thump, rising high whine | ⬜ |
| kpp.crt.loop | монитор работает: строчный писк ~15 кГц, лёгкий гул | пока горит | ×4 | 3D | loop | 1 | нет | CRT idle flyback whine and faint hum, close | ⬜ |
| kpp.crt.off | монитор гаснет: щелчок, треск статики | on→off | ×4 | 3D | oneshot | 2 | нет | CRT switching off, static crackle on glass, whine fades | ⬜ |
| kpp.camera.on | камера на мачте получила ток: тихий щелчок | Power(cameraHeads) | Cam_Barrier, Cam_Gate (2) | 3D | oneshot | 1 | нет | small CCTV camera relay click, faint buzz | ⬜ |
| kpp.flood.on | прожектор 500 Вт: щелчок, нить тинькает | Power(floods) off→on | Flood_Barrier, Flood_Gate (2) | 3D | oneshot | 2 | нет | halogen floodlight switching on, relay clack, filament ping | ⬜ |
| kpp.flood.loop | прожектор гудит, корпус потрескивает на морозе | пока горит | ×2 | 3D | loop | 1 | нет | halogen floodlight hum, housing ticking in frost | ⬜ |
| kpp.flood.off | прожектор гаснет, металл тикает, остывая | on→off | ×2 | 3D | oneshot | 2 | нет | floodlight off, hot metal ticking as it cools | ⬜ |
| kpp.fluor.start | лампа дневного света: стартер, щелчки, мигание 2,5 с | ForestKpp.BoothLamps (strike) | Lamp_Fluor ×1, Audio_Fluor | 3D | oneshot | 2 | нет | fluorescent tube starter clicking, flickering ignition | ⬜ |
| kpp.fluor.loop | дроссель гудит | пока горит | ×1 | 3D | loop | 1 | нет | fluorescent ballast hum, 50 Hz buzz | ⬜ |
| kpp.fluor.off | лампа гаснет: щелчок, звон трубки | BoothPowered→false | ×1 | 3D | oneshot | 1 | нет | fluorescent tube cutting out, faint tink | ⬜ |
| kpp.heater.tick | конвектор будки тикает, нагреваясь (с током) | нет кода | конвектор ×1 | 3D | oneshot | 3 | нет | electric convector heater ticking as it warms | ⬜ |
| kpp.radio.static | рация в будке шипит (голос — отдельно) | Audio_Radio (нет кода; VoiceEmitter берёт radio.static) | Kpp_Props ×1 | 3D | loop | 1 | тихо | 1980s handheld radio static hiss, squelch tail | 🔇 radio.static |
| kpp.barrier.motor.start | мотор шлагбаума: контактор, разгон | ForestKpp.Drive: target сменился при DrivesPowered | Barrier ×1, Audio_BarrierMotor | 3D | oneshot | 2 | средне | barrier gate motor contactor clunk and spin-up | ⬜ |
| kpp.barrier.motor.loop | редуктор жужжит, пока идёт стрела (~4 с) | openAmount≠target | ×1 | 3D | loop | 1 | средне | electric boom barrier gear motor whirring, steady | ⬜ |
| kpp.barrier.motor.stop | концевик: щелчок в конце | openAmount=target | ×1 | 3D | oneshot | 2 | средне | motor stops, limit switch click | ⬜ |
| kpp.barrier.arm.up | стрела наверху: пружины скрипят, стук | дошла до 80° | IX_BarrierArm_82, Audio_Barrier | 3D | oneshot | 2 | тихо | barrier boom reaching top, springs creak, light knock | ⬜ |
| kpp.barrier.arm.rest | стрела глухо ложится на резину упора | дошла до 0 | ×1 | 3D | oneshot | 2 | средне | aluminium boom dropping onto rubber rest, dull thud | ⬜ |
| kpp.barrier.arm.stall | ток пропал посреди хода: мотор обрывается, стрела дрожит | Drive(!DrivesPowered) во время хода | ×1 | 3D | oneshot | 1 | тихо | motor cuts out mid-travel, boom shudders and stops | ⬜ |
| kpp.barrier.hit | машина бьёт в стрелу | нет кода | ×1 | 3D | oneshot | 2 | средне | vehicle bumper hitting aluminium boom barrier, hollow bang | ⬜ |
| kpp.beacon.loop | маячок: тихо тикает моторчик | beacon.spinning | Beacon_Barrier, Audio_Beacon | 3D | loop | 1 | нет | rotating amber beacon small motor ticking | ⬜ |
| kpp.padlock.unlock | навесной замок шлагбаума: ключ, дужка (остался от v1) | ForestOpening.UsePadlock | Padlock_Barrier | 3D | oneshot | 2 | тихо | key turning in cold padlock, shackle springs open | ⬜ |
| kpp.padlock.drop | замок падает на бетон | после UsePadlock (настоящий замок падает) | ×1 | 3D | oneshot | 2 | тихо | brass padlock dropping onto frozen concrete | ⬜ (подойдёт item.impact.metal ✅) |
| kpp.gate.motor.start | привод ворот: контактор, разгон | ForestKpp.Drive(_gate) | Gate ×1, Audio_GateMotor | 3D | oneshot | 2 | громко | sliding gate operator motor clunk and spin-up | ⬜ |
| kpp.gate.motor.loop | двигатель гудит, шестерня трещит по рейке (~12 с) | ворота едут | ×1 | 3D | loop | 1 | громко | industrial gate drive motor, pinion rattling along steel rack | ⬜ |
| kpp.gate.motor.stop | торможение, щелчок концевика | ворота доехали | ×1 | 3D | oneshot | 2 | громко | gate motor braking to stop, limit switch clack | ⬜ |
| kpp.gate.rail.loop | ролики гремят по рельсу, лист гудит | ворота едут | IX_Gate_81, Audio_Gate | 3D | loop | 1 | громко | heavy steel sliding gate rolling on frozen rail, sheet metal rumble | ⬜ |
| kpp.gate.catch | ловитель лязгает, ворота закрылись | доехали до 0 | Gate, Audio_GateCatch | 3D | oneshot | 2 | громко | steel gate slamming into catch post, loud clang | ⬜ |
| kpp.gate.stop.open | полотно встаёт в упор открытым | доехали до 6,6 м | ×1 | 3D | oneshot | 2 | средне | gate leaf hitting end stop, dull metal knock | ⬜ |
| kpp.gate.wind | сплошной лист ворот на ветру: гул, удары | ветер (EnvironmentProvider) | IX_Gate_81 | 3D | loop | 3 | нет | steel sheet gate booming and buzzing in gusts | ⬜ |
| kpp.trafo.hum | трансформатор на опоре гудит, пока линия под током | host.Powered (после S7) | Pole_1 ×1, Audio_Trafo | 3D | loop | 1 | нет | pole-mounted transformer 50 Hz hum, cold night | ⬜ |
| kpp.line.wind | провода линии КПП–база поют на ветру | ветер | опоры Pole_1, Pole_2, Pole_Gate и опоры Unity через ~40 м (~8) | 3D | loop | 2 | нет | overhead power lines singing in wind, aeolian hum | ⬜ |
| kpp.line.crackle | изоляторы трещат в метель и изморозь | погода | ~8 опор | 3D | oneshot | 3 | нет | power line insulator crackle, corona buzz, frost | ⬜ |
| forest.phone.ring | телефон в будке звонит дважды (X05) | ForestOpening.Scares X05 | Audio_Phone | 3D | oneshot | 1+ | нет | — | ✅ forest.phone.ring |
| forest.phone.bell | звонок снаружи на стене (X05) | X05 | Phone_Bell, Audio_PhoneBell | 3D | oneshot | 1+ | нет | — | ✅ forest.phone.bell |
| forest.phone.breath | в трубке дыхание (X05) | ServerUse(Phone_KPP) | Phone_Receiver | 3D | oneshot | 1+ | нет | — | ✅ forest.phone.breath |
| kpp.phone.pickup | снять трубку: щелчок рычага, шорох шнура | ServerUse(PhoneZone) | Kit_Phone ×1 | 3D | oneshot | 2 | нет | 1970s rotary phone handset lifted, cradle hook click | ⬜ |
| kpp.phone.hangup | положить трубку: клак рычага | нет кода (только Press) | ×1 | 3D | oneshot | 2 | нет | handset dropped onto cradle, plastic clack, bell ting | ⬜ |
| kpp.phone.line.silence | тишина в трубке до дыхания: еле слышный шум линии | после pickup | Audio_PhoneEar | 3D | loop | 1 | нет | dead telephone line faint hiss in earpiece | ⬜ |
| kpp.phone.ring.cut | звонок обрывается на полутрели | трубку сняли во время звонка | ×1 | 3D | oneshot | 1 | нет | phone bell ring cut short, clapper dampened | ⬜ |
| kpp.phone.dial | набор диском: завод и обратный ход с треском | Phone_Dial (нет кода) | ×1 | 3D | oneshot | 3 | нет | rotary dial wound and returning, clicking spring | ⬜ |
| kpp.phone.dialtone | гудок в трубке | нет кода (в замысле «трубка: гудок, голоса») | Audio_PhoneEar | 3D | loop | 1 | нет | old German telephone dial tone, 425 Hz | ⬜ |
| kpp.roof.debris | на жестяную крышу будки падают иглы, шишки, снег | ForestAmbience (нет) | Checkpoint Audio_Roof | 3D | oneshot | 4 | нет | pine needles and cone tapping tin roof, snow clump | ⬜ |
| kpp.roof.steps | шаги по жестяной крыше будки (хоррор) | нет кода | Knock_Roof_1/2 | 3D | oneshot | 4 | нет | slow heavy steps on thin tin roof, metal flexing | ⬜ |
| kpp.knock.wall | стук снаружи по стене будки | нет кода | 10 точек Knock_Front/Back/Road/Left | 3D | oneshot | 4 | нет | knocking on thin booth wall from outside, hollow | ⬜ |
| kpp.knock.glass | стук по стеклу окна | нет кода | Knock_Window_Front/Left (2) | 3D | oneshot | 3 | нет | knuckle tapping cold window glass, slow | ⬜ |
| kpp.knock.door | стук в дверь будки | нет кода | Knock_Door | 3D | oneshot | 3 | нет | knocking on wooden door with glass pane | ⬜ |
| kpp.knock.scratch | царапанье снаружи (иней) | нет кода | 15 точек Knock_* | 3D | oneshot | 4 | нет | slow scratching on wood and glass, frost crackle | ⬜ |
| amb.kpp.indoor | будка внутри: с током (гул) / без тока (ветер в щелях) | SurfaceResolver.Indoors | Checkpoint | 2D | loop | 2 | нет | small guard booth interior, wind through gaps, faint hum | ⬜ (сейчас общий amb.indoor.tone ✅) |
| start.booth.door.open | дверь будки поста: ржавые петли | IX_Door StartBooth (пока статична) | StartBooth ×1 | 3D | oneshot | 2 | тихо | weathered plank booth door, rusty hinges creak | ⬜ |
| start.booth.door.close | дверь будки поста: деревянный хлоп | то же | ×1 | 3D | oneshot | 2 | средне | plank door banging shut, latch | ⬜ |
| start.lamp.flood.loop | прожектор на треноге гудит от аккумулятора | ForestStartLamp включён | StartLamp ×1 | 3D | loop | 1 | нет | battery work light buzzing, faint electrical whine | ⬜ |
| start.lamp.dip | короткий провал света: треск контакта | ForestStartLamp, провалы примерно раз в 7 с | ×1 | 3D | oneshot | 4 | нет | loose contact crackle, lamp flicker buzz | ⬜ |
| start.lamp.dying | 20 с агонии: заикание, треск | t ≥ lifeMinutes (12 мин) | ×1 | 3D | oneshot | 2 | нет | dying battery lamp stuttering, sputtering buzz | ⬜ |
| start.lamp.out | гаснет насовсем: щелчок, тик остывания | dead | ×1 + лампа будки (группа post, 14 мин) | 3D | oneshot | 2 | нет | lamp dies with click, glass ticks cooling | ⬜ |
| start.booth.lamp.out | лампа над дверью будки перегорает: тинь | ForestStartLamp (post) | StartBooth Lamp_Booth ×1 | 3D | oneshot | 2 | нет | incandescent bulb filament ping, burnout | ⬜ |
| start.battery.lid | крышка и выключатель ящика аккумулятора фонаря | нет кода (Hand_Battery) | ×1 | 3D | oneshot | 2 | тихо | plastic battery box lid and toggle switch | ⬜ |
| start.car.door.open | брошенный универсал: дверь, ржавый скрип | IX_CarDoor (статична) | StartCar ×1 | 3D | oneshot | 2 | тихо | old 1980s car door creaking open, rusty hinge | ⬜ |
| start.car.door.close | дверь универсала: тяжёлый хлоп | то же | ×1 | 3D | oneshot | 2 | средне | old car door slam, loose trim rattle | ⬜ |
| road.sign.rattle | щиты и указатели дребезжат на ветру | порывы | Sign_Halt ×2, Sign_NoEntry ×2, 12 указателей | 3D | oneshot | 3 | нет | aluminium road sign vibrating in gust, clamps rattle | ⬜ |
| road.zonegate.chain | цепь и замок ворот SPERRGEBIET стучат о трубы | ветер | ZoneGate ×1, Audio_GateChain | 3D | loop | 3 | нет | chain and padlock knocking steel gate tubes in wind | ⬜ |
| road.zonegate.wire | колючка ворот зоны гудит на ветру | ветер | Audio_GateWire | 3D | loop | 1 | нет | barbed wire humming in cold wind | ⬜ |
| road.zonegate.shake | игрок трясёт закрытые створки: рабица, цепь | нет кода (ворота неподвижны) | ×1 | 3D | oneshot | 3 | средне | chain-link gate shaken, chain rattles taut | ⬜ |
| start.trash.rustle | мешки кучи мусора шуршат на ветру | ветер | StartTrash ×1 | 3D | oneshot | 3 | нет | plastic bin bags rustling in wind | ⬜ |
| hut.door.open | дверь хижины: скрип кованых петель | NetworkDoor IX_HutDoor_1 (1,2 с) | TowerHut, Audio_Door | 3D | oneshot | 3 | тихо | wooden plank cabin door, iron hinges long creak | ⬜ |
| hut.door.close | дверь хижины: деревянный стук, скоба | то же | ×1 | 3D | oneshot | 3 | средне | plank door thumps shut, iron latch | ⬜ |
| hut.door.bang | открытая дверь хлопает на ветру | ветер | ×1 | 3D | oneshot | 3 | средне | cabin door banging in wind, wood on wood | ⬜ |
| hut.lantern.loop | фонарь «летучая мышь»: шипит керосиновое пламя | _lanternOn (до X01) | Lantern, Audio_Lantern | 3D | loop | 1 | нет | kerosene hurricane lantern flame soft hiss | ⬜ |
| hut.lantern.creak | скрипит дужка на крюке, фонарь качается | ForestLanternSway / ветер | Lantern ×1, фонарь HutSign ×1 | 3D | oneshot | 4 | нет | hanging lantern bail creaking on hook, sway | ⬜ |
| forest.lantern.out | фонарь хижины гаснет (X01) | ForestOpening.Scares X01 | lanternGlass | 3D | oneshot | 1+ | нет | — | ✅ forest.lantern.out |
| hut.candle.out | свеча на столе гаснет вместе с фонарём | X01 → x01Lamps.Kill | свеча ×1 | 3D | oneshot | 2 | нет | candle snuffed by draft, tiny puff | ⬜ |
| hut.trail.lantern.out | фонарь на батарейках у тропы гаснет | X01 (группа trail) | HutSign Lamp_Lant ×1 | 3D | oneshot | 1 | нет | battery lantern switches off, faint click | ⬜ |
| hut.window.wind | ветер в раме окна, свист в щелях | ветер | Audio_Window | 3D | loop | 1 | нет | wind whistling through old window frame | ⬜ |
| hut.window.tap | стук по стеклу | нет кода | Knock_Window | 3D | oneshot | 3 | нет | tapping on small cabin window glass | ⬜ |
| hut.shutter.move | внутренний ставень открыть / закрыть | Window_Shutter (нет кода) | ×1 | 3D | oneshot | 2+2 | тихо | wooden shutter swung on hinges, knock on frame | ⬜ |
| hut.shutter.latch | вертушка ставня | Window_Latch (нет кода) | ×1 | 3D | oneshot | 2 | тихо | wooden turn latch scrape and clack | ⬜ |
| hut.stove.door | дверца топки: чугун скрипит, лязгает | Stove_Door (нет кода) | ×1 | 3D | oneshot | 2+2 | тихо | cast iron stove door creak and clank | ⬜ |
| hut.stove.damper | задвижка на трубе | Stove_Damper (нет кода) | ×1 | 3D | oneshot | 2 | тихо | stovepipe damper turned, metal scrape | ⬜ |
| hut.stove.cold.tick | холодная печь тикает металлом на морозе | Audio_Stove | ×1 | 3D | oneshot | 4 | нет | cold iron stove ticking, contracting metal | ⬜ |
| hut.stove.fire.loop | огонь в печке: треск, гул тяги (позже) | QUESTIONS_HUT 16 (нет кода) | ×1 | 3D | loop | 1 | нет | wood stove fire crackling, chimney draw | ⬜ |
| hut.roof.debris | иглы, шишки, ветки скребут по жести | Audio_Roof | TowerHut | 3D | oneshot | 4 | нет | branch scraping corrugated roof, cone drops | ⬜ |
| hut.roof.steps | шаги по крыше хижины | нет кода (Knock_Roof_L/R) | ×1 | 3D | oneshot | 4 | нет | slow steps on corrugated tin roof | ⬜ |
| hut.knock.wood | стук по доскам снаружи | нет кода | 14 точек Knock_Wall*/Door/Roof | 3D | oneshot | 4 | нет | knocking on wooden cabin wall from outside | ⬜ |
| hut.knock.scratch | царапанье по доскам | нет кода | 14 точек | 3D | oneshot | 3 | нет | fingers scratching rough wooden planks, frost | ⬜ |
| hut.drip | капли с крыши | днём или в оттепель | TowerHut | 3D | oneshot | 4 | нет | meltwater drips from roof edge onto snow | ⬜ |
| amb.hut.indoor | хижина внутри: ветер в досках, холод | Indoors | TowerHut | 2D | loop | 1 | нет | cold empty forest cabin interior, wind in boards | ⬜ |
| hut.radio | маленькое радио: музыка с шипением (предложение) | QUESTIONS_HUT_2 №9 — ответа нет | внутри хижины | 3D | loop | 1 | тихо | tiny transistor radio, faint music through static | ⬜ |
| hut.hochsitz.creak | засидка скрипит на ветру, хлопает толь | ветер | Hochsitz ×1 | 3D | oneshot | 3 | нет | wooden hunting stand creaking in wind, tar paper flapping | ⬜ |
| hut.outhouse.door.open | дверь уборной с сердечком | IX_Door HutOuthouse (нет номера IX) | ×1 | 3D | oneshot | 2 | тихо | outhouse plank door creak open | ⬜ |
| hut.outhouse.door.close | дверь уборной хлопает | то же | ×1 | 3D | oneshot | 2 | средне | light outhouse door clapping shut | ⬜ |
| hut.outhouse.latch | вертушка снаружи, крючок изнутри | Hand_Door_Out/In | ×1 | 3D | oneshot | 2 | тихо | wooden turn button and hook latch | ⬜ |
| hut.outhouse.bang | дверь уборной стучит на ветру | ветер | ×1 | 3D | oneshot | 3 | тихо | outhouse door knocking in gusts | ⬜ |
| hut.feeder.hay | сено шуршит в кормушке | ветер | HutFeeder ×1 | 3D | oneshot | 2 | нет | dry hay rustling in wooden feeder | ⬜ |
| road.bridge.planks.foot | доски моста стучат под ногами | Audio_BridgePlanks (шаги — в другом месте) | Bridge30 ×1 | 3D | oneshot | 4 | тихо | loose bridge plank knocking under boots | ⬜ |
| road.bridge.planks.wheel | доски под колёсами, прогоны гудят | машина на мосту | ×1 | 3D | oneshot | 3 | громко | timber bridge planks rumbling under car wheels | ⬜ |
| road.bridge.creek | ручей подо льдом в овраге | Audio_BridgeCreek | ×1 | 3D | loop | 1 | нет | small stream running under ice in ravine | ⬜ |
| road.bridge.timber | брёвна моста трещат на морозе | Audio_BridgeTimber | ×1 | 3D | oneshot | 4 | нет | frozen bridge timbers creaking and cracking | ⬜ |
| road.overlook.wind | ветер над озером на площадке | Audio_OverlookWind | OverlookDeck ×1 | 3D | loop | 1 | нет | open wind over frozen lake, exposed deck | ⬜ |
| road.overlook.cap | дребезжит крышка бинокля | ветер | ×1 | 3D | oneshot | 3 | нет | binocular lens cap rattling on chain | ⬜ |
| road.overlook.scope | бинокль поворачивают: скрип шарнира | Look_OverlookScope (нет кода) | ×1 | 3D | oneshot | 2 | нет | coin binocular swivel creak, stiff | ⬜ |
| road.windfall.wind | ветер в сухих сучьях распиленной ели | Audio_Windfall | FallenTree ×1 | 3D | loop | 1 | нет | wind through dead spruce branches, dry creaks | ⬜ |
| fence.barbed.wind | колючка ПО-2 свистит и поёт на ветру | ветер | 209 плит + 7 стаканов | 3D | loop | 2 | нет | barbed wire on concrete wall whistling in wind | ⬜ |
| fence.oldfence.wind | старый пограничный забор: проволока звенит, столбы скрипят | ветер | OldFence 294 | 3D | oneshot | 3 | нет | old border fence wire twanging, posts creak | ⬜ |
| fence.wicket.creak | сетчатая калитка (открыта на 100°) качается | ветер (модель неподвижна) | ConcreteFence_Wicket ×1 | 3D | oneshot | 3 | нет | chain-link wicket door swaying, hinge squeal | ⬜ |
| fence.wicket.clank | калитка бьётся о раму | ветер | ×1 | 3D | oneshot | 2 | нет | steel wicket frame clanking | ⬜ |
| fence.wire.snag | колючка цепляет одежду | нет кода (касание коллайдера) | WireCoil 6, WireBarricade 3, Abatis 10 | 3D | oneshot | 4 | тихо | razor wire snagging winter jacket fabric | ⬜ |
| fence.hedgehog.hit | машина бьёт в противотанковый ёж | нет кода | Hedgehog 10 | 3D | oneshot | 2 | громко | car hitting steel tank hedgehog, heavy clang | ⬜ |
| fence.concrete.hit | машина в бетон: блок, отбойник, надолб | нет кода | ФБС 4, Jersey 7, Cube 2, TankTooth 276 | 3D | oneshot | 3 | громко | car bumper crunching into concrete block | ⬜ |
| fence.climb | перелезть через ПО-2: бетон, колючка | нет кода (QUESTIONS_KPP №7 открыт) | плиты ПО-2 | 3D | oneshot | 3 | средне | climbing concrete wall, scrape, barbed wire rattle | ⬜ |
| fence.e13.rattle | E13 «движение у ограды»: тряска колючки, хруст у ворот | ForestRadio.E13 (только NoiseEvents.Loud) | IX_Gate_81 | 3D | oneshot | 3 | громко | something heavy shaking barbed wire at gate, snow crunch | ⬜ |
| far.dish.drive | приводы азимута антенн: гул, иногда клак | Audio_Dish_n_Drive | Dish_1…5 (5) | 3D | loop | 1+3 | нет | radio telescope azimuth drive hum, occasional clunk | ⬜ |
| far.dish.cabinet | дверцы шкафов антенн | IX_CabinetDoor_2…6 | 5 | 3D | oneshot | 2+2 | тихо | outdoor steel equipment cabinet door | ⬜ |
| far.radome.ac | кондиционер укрытия радома | Audio_RadomeHum | Radome ×1 | 3D | loop | 1 | нет | old shelter air conditioner drone | ⬜ |
| far.relay.wind | ветер в решётке мачты 42 м, фидеры стучат | Audio_RelayWind | RelayMast ×1 | 3D | loop | 1+3 | нет | wind in lattice radio mast, cables knocking | ⬜ |
| far.meteo.cups | анемометр жужжит, флюгер скрипит | Audio_MeteoCups | MeteoStation ×1 | 3D | loop | 1 | нет | cup anemometer whirring, wind vane creak | ⬜ |
| far.lookout.cab | пожарная вышка 22 м: окна дребезжат, конструкция скрипит | Audio_LookoutCab | LookoutTower ×1 | 3D | loop | 1+3 | нет | fire lookout tower creaking, cab windows rattle | ⬜ |
| far.pump.hum | насосная гудит (только с током сети) | Audio_PumpHum | PumpHouse ×1 | 3D | loop | 1 | нет | small pump house electric pump hum | ⬜ |
| far.door.wood | двери насосной, часовни, избушки | IX_PumpDoor_7, IX_ChapelDoor_8, IX_CabinDoor_9 | 3 | 3D | oneshot | 2+2 | тихо | old wooden door creak in cold | ⬜ |
| far.chapel.bell | колокол часовни стукает на ветру | Audio_ChapelBell | Chapel ×1 | 3D | oneshot | 4 | нет | small chapel bell knocking softly in wind | ⬜ |
| far.cabin.stove | холодная печь избушки тикает | Audio_CabinStove | Cabin ×1 | 3D | oneshot | 3 | нет | cold stove ticking in empty hunting cabin | ⬜ |
| far.camp.tent | полотно палатки хлопает | Audio_CampTent | Campsite ×1 | 3D | loop | 1+3 | нет | abandoned tent fabric flapping | ⬜ |
| world.firebarrel.loop | огонь в бочке: треск, гул тяги | FireBarrel горит (нет кода и мест на карте) | FireBarrel | 3D | loop | 1 | нет | fire in steel drum, crackle and roar | ⬜ |
| world.firebarrel.embers | жар потрескивает | состояние embers | FireBarrel | 3D | loop | 1 | нет | embers ticking in rusty barrel | ⬜ |
| world.firebarrel.feed | бросили дрова: стук, сноп искр | Slot_Fuel | FireBarrel | 3D | oneshot | 3 | тихо | log thrown into fire barrel, sparks whoosh | ⬜ |
| world.firebarrel.ignite | розжиг: вспышка | нет кода | FireBarrel | 3D | oneshot | 2 | тихо | fire catching, soft whump | ⬜ |
| world.car.door | машина прежней смены: водительская и задняя двери | IX_ShiftCarDoor, IX_ShiftCarRearDoor | ShiftCar (Car_Parked) | 3D | oneshot | 2+2 | тихо | 1980s offroader door open and shut | ⬜ |
| world.wreck.door | брошенный седан: ржавая дверь | IX_WreckCarDoor | WreckCar | 3D | oneshot | 2 | тихо | rusted car door grinding open | ⬜ |
| world.wreck.boot | багажник со сломанным замком | IX_WreckCarBoot | WreckCar | 3D | oneshot | 2 | тихо | broken car boot lid creaking up | ⬜ |
| world.ice.chip | сбивать наледь ледорубом или шестом | триггеры HIT_ на Ice_* (нет кода) | RelayMast, Dish 1–5, RoofMast | 3D | oneshot | 4 | средне | ice axe chipping thick rime ice on steel | ⬜ |
| world.ice.fall | куски льда падают с высоты и бьются | сбили наледь | там же | 3D | oneshot | 4 | средне | ice chunks falling and shattering on frozen ground | ⬜ |
| world.icicle.drop | сосулька падает сама | ветер или оттепель | Ice_* | 3D | oneshot | 3 | нет | icicle snapping and tinkling onto snow | ⬜ |
| world.cave.tone | пещера: без ветра, капли, эхо | Zone_CaveInside | ForesterCave | 2D | loop | 1 | нет | small granite cave, dripping, no wind, close echo | ⬜ |
| world.cave.drip | капли в пещере | там же | ForesterCave | 3D | oneshot | 4 | нет | water drop in quiet cave | ⬜ |
| world.cave.candle | свечи пещеры потрескивают | Light_Candle 1–4 горят | 4 | 3D | loop | 1 | нет | candles sputtering softly | ⬜ |
| amb.forest.wind | ветер, ровный слой | ForestAmbience | весь лес | 2D | loop | — | нет | — | ✅ amb.forest.wind |
| amb.forest.gusts | порывы | ForestAmbience (ветер 3–9 м/с) | — | 2D | loop | — | нет | — | ✅ amb.forest.gusts |
| amb.wind.storm | буря | ForestAmbience (ветер 10–16 м/с) | — | 2D | loop | — | нет | — | ✅ amb.wind.storm |
| amb.forest.day | день: птицы | ForestAmbience | — | 2D | loop | — | нет | — | ✅ amb.forest.day |
| amb.forest.night | ночь | ForestAmbience | — | 2D | loop | — | нет | — | ✅ amb.forest.night |
| amb.night.howl | далёкий вой ночью | ForestAmbience | — | 2D | loop | — | нет | — | ✅ amb.night.howl |
| amb.indoor.tone | тон помещения (общий) | ForestAmbience | — | 2D | loop | — | нет | — | ✅ amb.indoor.tone |
| os.tree.creak | скрип дерева рядом | ForestAmbience.ServerOneShots | — | 3D | oneshot | — | нет | — | ✅ os.tree.creak |
| os.branch | ветка трещит | ServerOneShots | — | 3D | oneshot | — | нет | — | ✅ os.branch |
| os.treefall | падает дерево | ServerOneShots | — | 3D | oneshot | — | нет | — | ✅ os.treefall |
| amb.wind.ridge | ветер на гребне, на высоте | нет (A156) | гребни, вышки | 2D | loop | 1 | нет | high exposed ridge wind, roaring steady | ⬜ |
| amb.weather.frost | ясный мороз: звенящая тишина, далёкие звуки | погоды в коде нет | — | 2D | loop | 1 | нет | clear windless arctic night, ringing silence | ⬜ |
| amb.weather.snowfall | снегопад: мягкий шелест | погоды нет | — | 2D | loop | 1 | нет | heavy snowfall soft hiss in spruce forest | ⬜ |
| amb.weather.fog | туман: сырая глухая тишина | погоды нет | — | 2D | loop | 1 | нет | dense fog, damp muffled forest, no wind | ⬜ |
| amb.weather.blizzard.hiss | метель: снег сечёт по одежде | погоды нет | — | 2D | loop | 1 | нет | blizzard snow pelting nylon jacket, roaring | ⬜ |
| amb.weather.blizzard.glass | метель: снег по стёклам изнутри | Indoors + метель | будка, хижина | 3D | loop | 1 | нет | snow lashing window glass from inside, wind | ⬜ |
| amb.weather.blizzard.slam | удары порывов по постройкам | метель | постройки | 3D | oneshot | 4 | нет | blizzard gust slamming wooden wall, boom | ⬜ |
| amb.weather.prestorm | предвестник метели: низкий гул, ветер растёт, птицы молчат | погоды нет | — | 2D | loop | 1 | нет | rising wind, low ominous drone, birds fall silent | ⬜ |
| amb.weather.aurora | «сияние»: статика в воздухе, мёртвая техника гудит | погоды нет | — | 2D | loop | 1 | нет | eerie electrical crackle in air, faint hum | ⬜ |
| amb.weather.rime | изморозь нарастает: тонкий хруст льда | погоды нет | — | 3D | oneshot | 3 | нет | frost crystals crackling on metal | ⬜ |
| forest.snow.slide | снег сходит с еловых лап | можно в ServerOneShots | ели | 3D | oneshot | 6 | нет | snow clump sliding off spruce branch, soft whump | ⬜ |
| forest.frost.crack | мороз трещит в стволах | ниже −25 °C (E11) | ели | 3D | oneshot | 5 | нет | tree trunk frost crack, sharp report | ⬜ |
| forest.bird.crow | вороны днём | ServerOneShots (нет) | — | 3D | oneshot | 4 | нет | single crow cawing in winter forest | ⬜ |
| forest.bird.woodpecker | дятел | днём | — | 3D | oneshot | 3 | нет | distant woodpecker drumming, cold | ⬜ |
| forest.owl | сова ночью | ночью | — | 3D | oneshot | 3 | нет | tawny owl hoot, distant, night forest | ⬜ |
| forest.deer.far | олень вдалеке (лай, рёв) | ночью | — | 3D | oneshot | 3 | нет | distant roe deer bark at night | ⬜ |
| forest.dawn.crows | на рассвете взлетают вороны | ForestDayNight.DayChanged (рассвет) | — | 3D | oneshot | 2 | нет | crows taking off at dawn, wingbeats | ⬜ |
| forest.river | река и ручьи (кроме моста) | зоны (A161) | реки | 3D | loop | 1 | нет | cold river under ice edges, gurgling | ⬜ |
| forest.lake.ice.groan | лёд озера стонет и бухает | днём и ночью | озеро | 3D | oneshot | 4 | нет | frozen lake ice booming and singing, deep | ⬜ |
| forest.lake.ice.crack | лёд трескается под шагами | на льду (E22) | озеро | 3D | oneshot | 4 | средне | lake ice cracking under boots, sharp | ⬜ |
| forest.far.generator | гул генератора базы издалека (ориентир) | генератор работает (QUESTIONS_HORROR 34) | база | 3D | loop | 1 | нет | distant diesel generator drone through forest | ⬜ |
| forest.far.helicopter | Курьер: вертолёт ночью вдалеке | нет кода | — | 3D | loop | 2 | нет | distant helicopter rotor at night, fading | ⬜ |
| giant.bone.creak | кости скрипят на холоде | E11, X04 | рёбра (FogRibs), кисть | 3D | oneshot | 4 | нет | enormous bone creaking in deep frost, low groan | ⬜ |
| giant.ribs.snowslide | снег съезжает с рёбер | E11 | рёбра | 3D | oneshot | 3 | нет | snow sliding off giant curved bone | ⬜ |
| giant.hand.rise | кисть поднимается (E28): далёкий стон кости | нет кода | кисть | 3D | oneshot | 1 | нет | colossal bone grinding, far, subsonic | ⬜ |
| x04.lights.dim | X04: фары тускнеют, электрика проседает | нет кода | фургон у FogRibs | 3D | oneshot | 2 | нет | electrical brownout buzz, relay click | ⬜ |
| x02.crossing | X02: фигура переходит дорогу (предлагаю тишину и приглушение мира) | нет кода (Apparition) | дорога 620 м | 2D | oneshot | 1 | нет | sudden quiet, low pressure drop | ⬜ |
| x03.backseat | X03: скрип сиденья на 0,6 с | нет кода | фургон Sit_L1…4 | 3D | oneshot | 2 | нет | vinyl van seat creak behind you | ⬜ |
| x06.roof.steps | X06: шаги по крыше корпуса | ForestOpening.StartWalk(soundPath) | крыша BaseMain | 3D | oneshot | — | нет | — | ✅ step.gravel.walk (нужен свой) |
| x07.knock | X07: три удара в ставню | ForestOpening.Scares X07 | IX_Shutter_* | 3D | oneshot | — | нет | — | ✅ forest.knock |
| x09.corridor.steps | X09: шаги в коридоре (Лесник) | StartWalk | Flur_OG | 3D | oneshot | — | нет | — | ✅ step.tile.walk (нужен свой) |
| x10.door.creak | X10: дверь казармы открывается сама | ForestOpening.Night | IX_Door_10 | 3D | oneshot | — | нет | — | ✅ forest.door.creak |
| mon.holodny.joint | Холодный: сухой хруст суставов (не подделывается) | монстра нет | лес, двор | 3D | oneshot | 8 | нет | dry cracking bony joints, slow, close, crunch | ⬜ |
| mon.holodny.exhale | Холодный: короткий выдох носом | монстра нет | — | 3D | oneshot | 6 | нет | short sharp nasal exhale, cold air, creature | ⬜ |
| mon.holodny.step.snow | на четырёх конечностях по снегу | монстра нет | — | 3D | oneshot | 8 | нет | four-limbed creature crawling on crusted snow | ⬜ |
| mon.holodny.step.hard | по полу, бетону, металлу (E30) | монстра нет | база, контейнер | 3D | oneshot | 6 | нет | bony hands and feet on linoleum, clicking | ⬜ |
| mon.holodny.rise | встаёт на задние ноги: каскад хруста | монстра нет | — | 3D | oneshot | 2 | нет | creature rising upright, cascade of joint cracks | ⬜ |
| mon.holodny.retreat | уходит в темноту от жёлтого света | монстра нет | — | 3D | oneshot | 3 | нет | fast scrabbling retreat through snow and brush | ⬜ |
| mon.holodny.warn | предупреждение перед захватом: шорох всё ближе | монстра нет | — | 3D | oneshot | 3 | нет | rustling approach behind, joints crunch close | ⬜ |
| mon.holodny.grab | захват: рывок, тело падает в снег | монстра нет | — | 3D | oneshot | 3 | нет | violent lunge, body slammed into snow | ⬜ |
| mon.holodny.drag | волочение по снегу | 60–90 с утаскивания | — | 3D | loop | 2 | нет | body dragged through deep snow, steady | ⬜ |
| mon.holodny.drop | бросает жертву | догнали | — | 3D | oneshot | 2 | нет | body dropped in snow, creature bolts | ⬜ |
| mon.holodny.window | удары в ставню или окно тёмной комнаты, три по нарастающей | нет кода | Knock_* базы | 3D | oneshot | 3 | нет | heavy blow on window shutter, escalating | ⬜ |
| mon.holodny.glass | третий удар: стекло лопается, врывается холод | нет кода | окна | 3D | oneshot | 2 | громко | window glass bursting inward, icy wind rush | ⬜ |
| mon.holodny.climb | лезет по бетону забора или по контейнеру | нет кода | ПО-2, GenContainer | 3D | oneshot | 3 | нет | claws scraping concrete, barbed wire twang | ⬜ |
| mon.holodny.roof | по стальной крыше контейнера в метель | нет кода | GenContainer | 3D | oneshot | 4 | нет | creature crawling on steel container roof | ⬜ |
| mon.lesnik.shuffle.snow | Лесник: шарканье босых ног по снегу | монстра нет | лес, двор | 3D | oneshot | 6 | нет | slow barefoot shuffling in snow, dragging | ⬜ |
| mon.lesnik.shuffle.floor | шарканье по полу базы | монстра нет | база | 3D | oneshot | 6 | нет | slow bare feet shuffling on linoleum corridor | ⬜ |
| mon.lesnik.mumble | тихое бормотание без слов | монстра нет | — | 3D | oneshot | 6 | нет | low wordless old man mumbling, muffled | ⬜ |
| mon.lesnik.neck | шея вытягивается: хрящи, натяжение | монстра нет | — | 3D | oneshot | 3 | нет | neck stretching, cartilage creak, skin tension | ⬜ |
| mon.lesnik.neck.snap | поймали фонарём: голова рывком назад | монстра нет | — | 3D | oneshot | 2 | нет | sudden whip-back of long neck, wet crack | ⬜ |
| mon.lesnik.mouth | щель-рот приоткрывается | монстра нет | — | 3D | oneshot | 2 | нет | slit mouth opening, slow wet click | ⬜ |
| mon.lesnik.fingers | пальцы-спицы постукивают и скребут по полу | монстра нет | — | 3D | oneshot | 4 | нет | long needle fingers tapping and dragging floor | ⬜ |
| mon.lesnik.flee | убегает огромными прыжками | монстра нет | — | 3D | oneshot | 3 | нет | huge loping leaps through snow, receding | ⬜ |
| mon.lesnik.cover | закрывается рукой: шелест лохмотьев | монстра нет | — | 3D | oneshot | 2 | нет | ragged parka rustle, arm raised fast | ⬜ |
| mon.lesnik.loot | звякают украденные вещи в руках | E23 | — | 3D | oneshot | 4 | нет | stolen tins and batteries clinking in hands | ⬜ |
| mon.lesnik.sleep | спит в пещере: медленное дыхание под одеялами | днём | ForesterCave Sleep_Forester | 3D | loop | 1 | нет | slow raspy sleeping breath under blankets | ⬜ |
| mon.lesnik.wake | проснулся и вскочил | нет кода | пещера | 3D | oneshot | 1 | нет | startled scramble, blankets thrown off | ⬜ |
| mon.dvoynik | Двойник беззвучен (проверить, что его шаги не играют) | — | — | — | — | 0 | нет | — | звук не нужен |
| mon.olen.hoof | Неправильный олень: копыта по дороге, сбитый ритм (лишняя нога) | монстра нет | дорога | 3D | oneshot | 4 | нет | deer hooves on frozen gravel, wrong uneven rhythm | ⬜ |
| mon.olen.breath | олень дышит в свете фар | монстра нет | — | 3D | oneshot | 2 | нет | heavy slow animal breath, steam | ⬜ |
| mon.olen.hit | сбили: глухой удар о кузов | монстра нет | фургон | 3D | oneshot | 2 | средне | heavy body hitting van front, dull thud | ⬜ |
| mon.kukla.move | Кукла сдвинулась за спиной: шорох ткани, мягкий шлёп | монстра нет | база | 3D | oneshot | 4 | нет | rag doll flopping softly onto floor, fabric | ⬜ |
| mon.kukla.head | кукла поворачивает голову (конец демо) | нет кода | Item_Doll | 2D | oneshot | 1 | нет | stuffed doll head turning, cloth creak | ⬜ |
| mon.nevesomye.strap | Невесомые (акт 2): скрип ремней, звякают кольца | акт 2 | Sky Island | 3D | oneshot | 3 | нет | leather straps creaking, steel ring clink | ⬜ |
| mon.nevesomye.snap | срывается с ремня и уносит вверх | акт 2 | — | 3D | oneshot | 1 | нет | strap snapping, rushing upward whoosh | ⬜ |
| ent.knock.glass | Стучащий: стук по стеклу | нет кода | окна | 3D | oneshot | 3 | нет | three slow knocks on cold glass | ⬜ (металл — forest.knock ✅) |
| ent.door.drum | барабанит в незапертую входную дверь, всё сильнее | нет кода (BASE_THREATS) | IX_MainDoor_1 | 3D | oneshot | 4 | нет | fists pounding wooden door, escalating | ⬜ |
| ent.lamp.pop | Ламповый: лампочка лопается | E08 (нет кода) | лампы | 3D | oneshot | 4 | нет | incandescent bulb bursting, glass tinkle | ⬜ |
| ent.tube.pop | лопается трубка дневного света | E08 | лампы ДС | 3D | oneshot | 3 | нет | fluorescent tube popping, glass shower | ⬜ |
| ent.frozen.creak | Замёрзшие: тихий ледяной хруст, когда отвернулся | E21 (нет кода) | у костей | 3D | oneshot | 3 | нет | ice crust cracking on a standing figure | ⬜ |
| ent.ice.shadow | тень подо льдом: глухие удары в такт шагам | E22 (нет кода) | озеро | 3D | oneshot | 4 | нет | deep muffled thud beneath thick lake ice | ⬜ |
| ent.halluc.steps | галлюцинация: шаги глухо и без направления | E24 (нет кода) | — | 2D | oneshot | 4 | нет | muffled footsteps, no direction, dreamlike | ⬜ |
| ent.halluc.whisper | галлюцинация: шёпот без слов | E19, E24 | — | 2D | oneshot | 4 | нет | indistinct breathy whisper, muffled | ⬜ |
| e01.gen.strangle | E01: Холодный глушит генератор, дизель захлёбывается | нет кода | GenContainer | 3D | oneshot | 2 | громко | diesel engine choking and dying unnaturally | ⬜ |
| b01.gen.cough | B01 и ложный исход: генератор кашляет и прокашливается | нет кода | GenContainer | 3D | oneshot | 2 | громко | diesel generator coughing then recovering | ⬜ |

- **Падлок остался от v1.** В `ForestOpening.cs` всё ещё есть `Padlock_Barrier`, `UsePadlock` и `padlockProxy`, а в `Forest_Opening.json` — `Barrier_Padlock` и `Key_Schranke`. По решению КПП v2 замка на шлагбауме нет (`QUESTIONS_KPP.md`, п. 4). Звуки замка нужны, только если старый код оставят. Ручной шлагбаум `Barrier_Manual` на карту не ставится, его точку `Audio_Barrier_Manual` я пропустил.
- **В `ForestKpp.cs` нет ни одного вызова звука** (в его комментарии: «sounds wait for clips»). Крючки — смены сетевых переменных (`_switchBatt`, `_clampPos/Neg`, `_battery`, `_barrierUp`, `_gateOpen`, `_pressedUntil*`) и `BoothPowered`. Мотор шлагбаума и ворот надо ловить по `openAmount ≠ target` у детали. Удобно повесить разовые звуки на OnValueChanged у каждого клиента, а петли — через `SoundLoop` на точки `Audio_*`.
- **Ворота стоят в ~292 м от будки.** Мотор ворот слышно только у ворот, в будке видно лишь монитор. Кнопки 319/320 в Leitstand запускают те же моторы. Скорее всего, это и есть «шум у ворот» из `SOUND_LIST`: мотор громкий (`QUESTIONS_KPP`, В2), поэтому я поставил ему «громко».
- **Погоды в коде нет.** `EnvironmentProvider` в сцене Forest не стоит, `ForestAmbience` берёт выдуманный ветер 4–8 м/с. Слоям ясного мороза, снегопада, тумана, метели, предвестника и «сияния» пока нечем управлять. Ветровые звуки (провода, колючка, ворота, вывески) можно сразу привязать к `EnvironmentProvider.WindAt`.
- **X06 и X09 сейчас играют шаги игрока** (`step.gravel.walk`, `step.tile.walk`), а по `FOREST_OPENING.md` это Лесник. Нужны свои cue — шарканье и тяжёлые шаги. Иначе ломается правило «шаги игроков слышат все, монстр звучит по-своему».
- **X02: кто переходит дорогу.** Модель Apparition — прямоходящая худая фигура 2,3 м, а таблица в `FOREST_OPENING.md` называет X02 Холодным, который ходит на четырёх конечностях. От этого зависит, какой у фигуры звук. По «честному» звуковому контракту из едущей машины за 80 м её не слышно, поэтому я предлагаю тишину и приглушение мира, а не отдельный звук.
- **Шум для существ работает не везде.** Монстров в коде пока нет. Шум отправляют только шаги, звуки тела, рация и `SnowAudio.ServerPlay`, когда у cue задано `noise`. E13 (`ForestRadio.E13`) сейчас шлёт только шум `NoiseEvents.Loud` — игрок у ворот ничего не слышит, нужен слышимый звук (строка `fence.e13.rattle`).
- **Пересечения с другими агентами:**
  - трансформатор и гул линии во дворе базы (A022, A029);
  - генератор (E01, B01), сирена и тревога (E06, E07), Ламповый в комнатах, X06/X07/X10 в корпусе — с агентом базы;
  - статика рации в будке — это `radio.static`, его уже просит `VoiceEmitter`, вопрос к агенту рации;
  - X03, X04 и удары по фургону — с агентом машин.
  
  Дальние точки (FAR) и вещи мира (бочка, брошенные машины, пещера, наледь) я отнёс к лесному окружению — это моё решение, в задании их не было.
- **Многое из моделей пока не играется:**
  - будка и машина старт-поста статичны («no interaction until the pack joins the Forest integration»);
  - у хижины ставень, печь и задвижка без кода;
  - у бочки, второй брошенной машины и пещеры нет мест на карте;
  - 213 секций `FenceChain` убраны из данных в обновлении 7.
  
  Звуки для них имеют смысл, только когда появится взаимодействие.
- **Открытые вопросы:**
  - радио в хижине (`QUESTIONS_HUT_2`, №9, ответа нет);
  - можно ли перелезать через колючку (`QUESTIONS_KPP`, №7);
  - что видит игрок, если шлагбаум или ворота не закрываются («Мешает игрок»): звук отказа привода (писк, мотор дёргается) не описан нигде;
  - отказы вроде «дверца шкафа закрыта» и «нет аккумулятора» я считаю UI-звуком `ui.deny` из списка интерфейса и в таблицу не включал.

---

## Звуки базы: полный список

Каждая строка — отдельный звук. Когда открыть и закрыть звучат по-разному, это две строки. Сколько каких объектов, я считал по `Forest_Layout.json`, `Forest.unity` и контрактам.

Колонка «шум» — на каком расстоянии звук слышат существа. Радиусы взяты из `NoiseEvents`: тихо 5 м, средне 15 м, громко 30 м.

| id | what | when | where | 3D/2D | oneshot/loop | variants | шум | prompt | status |
|---|---|---|---|---|---|---|---|---|---|
| base.gen.key.turn | ключ пуска генератора: поворот, щелчок | ForestOpening.ServerUse(GenStart) Press | GenContainer / IX_GenStart_86 ×1 | 3D | oneshot | 3 | тихо | heavy ignition key turned in steel genset panel, stiff click | ⬜ |
| base.gen.diesel.start | дизель: стартер, схватывает | ServerUse(GenStart) → Sound(Sfx.DieselStart); DevPower | IX_GenStart_86 | 3D | oneshot | 2 | громко | old diesel generator cranking, catches, settles to idle inside steel container | ✅ forest.diesel.start |
| base.gen.diesel.loop | дизель: работа 1500 об/мин | ForestOpening.Machines(), через 2,5 с после пуска | IX_GenStart_86 | 3D | loop | 1 | громко | steady diesel genset idling 1500 rpm, rattling container, seamless | ✅ forest.diesel.loop |
| base.gen.diesel.stop | дизель: останов ключом | StopGenerator("lever"/"dev") → Sfx.DieselStop | IX_GenStart_86 | 3D | oneshot | 2 | громко | diesel generator shut down, winds down, last knocks, fan coasting | ✅ forest.diesel.stop |
| base.gen.diesel.nostart | стартер крутит, мотор не схватывает | ServerUse(GenStart), отказ «Бак пуст» (сейчас тишина) | IX_GenStart_86 | 3D | oneshot | 3 | средне | diesel starter cranking but engine won't fire, slows and stops | ⬜ |
| base.gen.diesel.runout | топливо кончилось: чихает и глохнет | StopGenerator("empty"), E03 (сейчас играет общий stop) | IX_GenStart_86 | 3D | oneshot | 2 | громко | diesel engine starving of fuel, sputters, surges, misfires, dies | ⬜ |
| base.gen.diesel.killed | генератор заглушили снаружи (E01) | нет кода (EVENTS E01) | GenContainer | 3D | oneshot | 2 | громко | diesel generator cut abruptly mid-run, metallic clunk, sudden silence | ⬜ |
| base.gen.diesel.cough | кашляет и прокашливается (фейк, B01) | нет кода (BASE_THREATS) | GenContainer | 3D | oneshot | 3 | громко | running diesel coughs and stumbles, recovers to idle | ⬜ |
| base.gen.load.loop | вой альтернатора под нагрузкой | нет кода (Audio_Alternator), слой поверх loop | GenContainer | 3D | loop | 1 | средне | alternator whine under load, generator hum | ⬜ |
| base.gen.fan.loop | вентилятор радиатора и ремень | нет кода (Audio_Fan), пока мотор работает | GenContainer | 3D | loop | 1 | средне | radiator fan roar and belt whir of diesel genset | ⬜ |
| base.gen.belt.squeal | свист ослабшего ремня | нет кода (GENCONT Engine_Belt loose) | Audio_Fan | 3D | oneshot | 3 | средне | slipping fan belt squeal on cold diesel | ⬜ |
| base.gen.exhaust.loop | выхлоп на трубе снаружи, дребезг заслонки | нет кода (Audio_Exhaust) | крыша GenContainer | 3D | loop | 1 | громко | diesel exhaust stack beat outdoors, rain flap rattling | ⬜ |
| base.gen.louvre.rattle | жалюзи дребезжат | нет кода (Audio_Louvre) | GenContainer | 3D | oneshot | 4 | тихо | loose steel louvre blades rattling in gust | ⬜ |
| base.gen.glow.tick | тикают свечи накала | нет кода, деталей нет на карте (GENCONT Gen_GlowButton) | пульт генератора | 3D | loop | 1 | нет | glow plug timer relay ticking, faint buzz | ⬜ |
| base.gen.board.hum | гул шин щита (с током) | нет кода (Audio_Board) | щит GenContainer | 3D | loop | 1 | нет | 50 Hz busbar hum of switchboard | ⬜ |
| base.gen.breaker.on | автомат щита: вкл | host Toggle (Positional) | IX_Breaker_87–89 ×3 | 3D | oneshot | 3 | тихо | circuit breaker lever snapped up, crisp clack | ⬜ |
| base.gen.breaker.off | автомат щита: выкл | то же | ×3 | 3D | oneshot | 3 | тихо | circuit breaker flicked down, dull clack | ⬜ |
| base.gen.breaker.trip | автомат выбивает с искрой | нет кода (VFX_BoardSparks) | щит GenContainer | 3D | oneshot | 2 | средне | breaker tripping under load, loud snap, spark crackle | ⬜ |
| base.gen.changeover.on | перекидной рубильник OFF→GEN | host Toggle PowerSwitch | IX_MainSwitch_90 ×1 | 3D | oneshot | 2 | средне | heavy changeover switch thrown, big thunk, contactor clunk | ⬜ |
| base.gen.changeover.off | рубильник GEN→OFF | то же | ×1 | 3D | oneshot | 2 | средне | heavy rotary switch turned off, clunk, hum drops | ⬜ |
| base.gen.fillcap.open | крышка горловины: открыть | UseFillCap → _capOpen true | IX_GenFillCap_300 ×1 | 3D | oneshot | 2 | тихо | small hinged steel lid flipped open, rusty squeak | ⬜ |
| base.gen.fillcap.close | крышка горловины: закрыть | UseFillCap → _capOpen false | ×1 | 3D | oneshot | 2 | тихо | steel lid snapped shut on fill box, clank | ⬜ |
| base.gen.fuel.pour | заливка из канистры | ServerTick при заливке, раз в 5 с | горловина Gen_FillNeck | 3D | oneshot | 3 | тихо | diesel poured from jerrycan into filler, glugging | ✅ forest.fuel.pour |
| base.gen.fuel.full | бак полон: бульк у горла, перелив | StopFill("tank full") (сейчас тишина) | Gen_FillNeck | 3D | oneshot | 2 | тихо | tank filled to brim, gurgle at neck, small spill | ⬜ |
| base.gen.cargodoor.open | грузовые створки: штанги, кулачки, петли | NetworkDoor Opening | IX_ContDoorL_83 / R_84 ×2 | 3D | oneshot | 3 | средне | shipping container locking bars lifted, cams clank, hinges groan | ⬜ |
| base.gen.cargodoor.close | грузовые створки: удар | NetworkDoor Closed | ×2 | 3D | oneshot | 3 | громко | container door slammed, steel boom, bar dropped | ⬜ |
| base.gen.sidedoor.open | боковая дверь контейнера: открыть | NetworkDoor Opening | IX_ContSideDoor_85 ×1 | 3D | oneshot | 2 | тихо | small steel door latch released, hinge creak | ⬜ |
| base.gen.sidedoor.close | боковая дверь: закрыть | NetworkDoor Closed | ×1 | 3D | oneshot | 2 | средне | small steel door shut, latch clack, thin metal ring | ⬜ |
| base.gen.roof.tick | стальная крыша щёлкает на морозе, падают шишки | нет кода (Audio_Roof) | GenContainer, Garage | 3D | oneshot | 6 | нет | cold steel roof ticking and booming, pine cone on metal | ⬜ |
| base.gen.wind.intake | ветер в воздухозаборниках | нет кода (Audio_Wind) | GenContainer | 3D | loop | 1 | нет | wind moaning through steel intake louvres | ⬜ |
| base.yard.pump.stroke | ручной насос: скрип-всхлип на каждом ходе | Machines(), каждый ход при _pumping | FuelTanks / IX_PumpLever_301 | 3D | oneshot | 3 | средне | cast iron wing hand pump stroke, squeak and suck | ✅ forest.pump.creak |
| base.yard.pump.flow | солярка льётся в канистру, тон растёт | _pumping и CanUnderNozzle (сейчас тишина) | Pump_Nozzle | 3D | loop | 1 | тихо | diesel stream into steel jerrycan, glug pitch rising as it fills | ⬜ |
| base.yard.pump.dry | качают без канистры: плеск в поддон | _pumping, а канистры под пистолетом нет | Pump_Nozzle | 3D | oneshot | 3 | тихо | spurts of diesel splashing onto steel drip tray | ⬜ |
| base.yard.nozzle.drip | капает с пистолета | нет кода (VFX_FuelDrip) | Pump_Nozzle | 3D | oneshot | 3 | нет | single fuel drop into metal tray | ⬜ |
| base.yard.tank1.hit | стук по полному баку 1: глухо, плеск | нет кода (Audio_Tank_T1) | FuelTanks T1 | 3D | oneshot | 3 | средне | knocking on full steel fuel tank, dull thud, slosh | ⬜ |
| base.yard.tank2.hit | стук по пустому баку 2: гулко | нет кода (Audio_Tank_T2) | FuelTanks T2 | 3D | oneshot | 3 | средне | knocking on empty 18000 l tank, hollow ringing boom | ⬜ |
| base.yard.trafo.hum | гул трансформатора, когда линия под током | нет кода (Audio_TrafoHum; в прологе молчит) | Trafo ×1 | 3D | loop | 1 | тихо | oil transformer 100 Hz hum outdoors | ⬜ |
| base.yard.trafogate.open | калитка трансформатора: щеколда, скрип | NetworkDoor Opening | IX_TrafoGate_91 ×1 | 3D | oneshot | 2 | тихо | chain link gate latch lifted, rusty hinge squeak | ⬜ |
| base.yard.trafogate.close | калитка: закрыть | NetworkDoor Closed | ×1 | 3D | oneshot | 2 | тихо | chain link gate swung shut, mesh rattle, latch | ⬜ |
| base.yard.wires.wind | провода гудят на ветру | нет кода (Audio_Wires) | Trafo, PowerPole ×4 | 3D | loop | 2 | нет | overhead power lines singing in cold wind | ⬜ |
| base.yard.line.hum | линия гудит при включённой сети | нет кода (Audio_LineHum, host.Powered) | PowerPole ×4 | 3D | loop | 1 | нет | faint buzz of energised pole insulators | ⬜ |
| base.yard.arc.pop | вспышка и хлопок на предохранителях опоры | нет кода (VFX_Arc_Cutouts) | опора трансформатора | 3D | oneshot | 2 | громко | high voltage fuse arc, sharp bang, sizzle | ⬜ |
| base.yard.windsock | ветроуказатель хлопает, вертлюг скрипит | нет кода (Audio_Windsock) | Helipad ×1 | 3D | loop | 1 | нет | windsock flapping, swivel squeak | ⬜ |
| base.yard.mast.guys | оттяжки мачты гудят | нет кода (Audio_MastGuys) | RadioMast ×1 | 3D | loop | 1 | нет | radio mast guy wires humming in wind | ⬜ |
| base.yard.mast.ice | лёд падает с мачты | нет кода (Audio_MastGuys) | RadioMast | 3D | oneshot | 4 | средне | ice chunks falling from lattice mast, shattering | ⬜ |
| base.yard.hatch.vent | шахта дышит через грибок | нет кода (Audio_ShaftVent) | Hatch ×1 | 3D | loop | 1 | нет | low draught breathing from deep shaft vent | ⬜ |
| base.yard.hatch.knockbelow | стук снизу в шахте | нет кода (Audio_ShaftVent) | Hatch | 3D | oneshot | 3 | средне | distant metallic knock deep in concrete shaft | ⬜ |
| base.yard.hatch.padlock | замок стучит о скобу на ветру | нет кода (Audio_HatchLock) | Hatch | 3D | oneshot | 4 | нет | padlock knocking on steel staple in wind | ⬜ |
| base.yard.hatch.locked | люк заперт: лист дёргается в замке | host ServerUse, part locked («заперт») | IX_HatchLid_94 ×1 | 3D | oneshot | 2 | тихо | chequer plate hatch lid jerked, padlock holds | ⬜ |
| base.yard.hatch.open | крышка люка: поднять | NetworkDoor Opening (если отопрут) | IX_HatchLid_94 | 3D | oneshot | 2 | средне | heavy steel hatch lid lifted, grinding creak | ⬜ |
| base.yard.hatch.close | крышка люка: бросить | NetworkDoor Closed | IX_HatchLid_94 | 3D | oneshot | 2 | громко | steel hatch lid dropped, clang echoing in shaft | ⬜ |
| base.yard.dropbox.flap | заслонка ящика для кассет | нет кода (DropBox_Flap) | DropBox у вертолётной площадки ×1 | 3D | oneshot | 2 | тихо | spring steel flap pushed in, squeak, snaps back | ⬜ |
| base.yard.dropbox.drop | кассета падает в ящик | нет кода (задание Р7) | DropBox | 3D | oneshot | 2 | тихо | cassette dropping into empty steel box, hollow clunk | ⬜ |
| base.yard.lamp.hps.strike | натриевый фонарь: щелчок дросселя, разгорается | onFixtureEvent Starting (HPS) | LightPole ×6, фонари забора ×20, у антенн ×5 | 3D | oneshot | 3 | нет | sodium street lamp ballast click and buzz as it strikes | ⬜ |
| base.yard.lamp.hps.hum | натриевый фонарь гудит | onFixtureEvent On | те же 31 | 3D | loop | 1 | нет | faint buzz of sodium lamp in cold night | ⬜ |
| base.yard.chimney | ветер в трубе котельной | нет кода (Audio_Chimney) | BaseMain, Heizung | 3D | loop | 1 | нет | wind moaning down brick chimney flue | ⬜ |
| base.roof.vent.loop | вентустановка на крыше (с током) | нет кода | крыша BaseMain | 3D | loop | 1 | тихо | rooftop ventilation fan drone, sheet metal duct | ⬜ |
| base.roof.deflector | дефлекторы на крыше гремят и свистят | нет кода | крыша BaseMain | 3D | oneshot | 3 | нет | rooftop vent cowl rattling and whistling in gust | ⬜ |
| base.power.grid.on | ток пошёл по всей базе: каскад реле, гул | host.Powered false→true (S7, DevPower) | все постройки в сети | 2D+3D | oneshot | 2 | средне | power restored, cascade of relays clunking, rising hum | ⬜ |
| base.power.grid.off | ток пропал: гул проседает, реле отпадают | host.Powered true→false (StopGenerator, E01, E03) | все постройки | 2D+3D | oneshot | 2 | средне | power failure, hum winds down, relays drop, silence | ⬜ |
| base.power.mainswitch.on | главный рубильник: вкл (S7) | host Toggle PowerSwitch | IX_MainSwitch_75, Schaltraum ×1 | 3D | oneshot | 2 | средне | big knife main switch lever pulled up, heavy thunk | ⬜ |
| base.power.mainswitch.off | главный рубильник: выкл | то же | ×1 | 3D | oneshot | 2 | средне | main switch lever pushed down, clunk, arc snap | ⬜ |
| base.power.lever.on | рычажный выключатель отходящей линии: вкл | host Toggle Positional | IX_Breaker_69–74 ×6 | 3D | oneshot | 3 | тихо | lever switch on cream switchboard thrown up, solid clack | ⬜ |
| base.power.lever.off | рычажный выключатель: выкл | то же | ×6 | 3D | oneshot | 3 | тихо | switchboard lever thrown down, clack | ⬜ |
| base.power.breaker.on | автомат щитка: вкл | host Toggle Positional | IX_Breaker_45–50 (Serverraum), 76–81 (Schaltraum) ×12 | 3D | oneshot | 4 | тихо | miniature circuit breaker flicked up, crisp snap | ⬜ |
| base.power.breaker.off | автомат щитка: выкл | то же | ×12 | 3D | oneshot | 4 | тихо | miniature circuit breaker flicked down, snap | ⬜ |
| base.power.isolator.turn | поворотный выключатель 0…90° | host Toggle Positional | IX_Isolator_51, _82 ×2 | 3D | oneshot | 2 | тихо | rotary isolator turned, detent, heavy click | ⬜ |
| base.power.paneldoor.open | дверца щита: открыть | host Toggle Furniture | IX_PanelDoor_83 ×1 | 3D | oneshot | 2 | тихо | steel electrical cabinet door opened, hinge creak | ⬜ |
| base.power.paneldoor.close | дверца щита: закрыть | то же | ×1 | 3D | oneshot | 2 | тихо | steel cabinet door shut, sheet clang, latch | ⬜ |
| base.power.board.hum | гул главного щита и щитков (с током) | нет кода (host.Powered) | Schaltraum, щит в Serverraum | 3D | loop | 1 | нет | switchroom hum, contactors buzzing | ⬜ |
| base.power.relay.tick | щелчки реле в щитовой | нет кода (DRESSING_GUIDE 4.4) | Schaltraum | 3D | oneshot | 6 | нет | single relay click inside panel | ⬜ |
| base.light.switch.flush | клавишный выключатель в стене: вкл/выкл | host Toggle LightSwitch (без сети тот же щелчок) | IX_LightSwitch 84–86, 89, 93–98, 102 ×11 | 3D | oneshot | 4 | нет | 1980s German rocker light switch click | ⬜ |
| base.light.switch.surface | накладной выключатель (Aufputz) | то же | IX_LightSwitch 87, 88, 90–92, 99–101 ×8 | 3D | oneshot | 4 | нет | surface-mounted bakelite switch clack, hollow box | ⬜ |
| base.heat.stop.toggle | аварийный выключатель отопления | host Toggle Positional | IX_HeatingStop_131, Flur_UG ×1 | 3D | oneshot | 2 | тихо | emergency heating cut-off rocker, firm click | ⬜ |
| base.lamp.fluor.start | лампа дневного света: тикает стартер, 1–3 вспышки | onFixtureEvent Starting/Blink (сеть, выключатель; X09 — свет в коридоре включается сам) | BaseMain ×21, Garage ×2 | 3D | oneshot | 4 | нет | fluorescent tube starter ticking, flicker ping, ballast catches | ⬜ |
| base.lamp.fluor.hum | лампа дневного света: гул дросселя | onFixtureEvent On…Off | те же 23 | 3D | loop | 2 | нет | steady fluorescent ballast hum | ⬜ |
| base.lamp.fluor.fault | сбойная трубка: треск, провалы, гаснет и снова стартует | onFixtureEvent Fault (faulty, просадка) | FX_Raster_10 в Flur_EG (+любая при просадке) | 3D | oneshot | 3 | нет | failing fluorescent buzzing, crackling dropout, starter clicks | ⬜ |
| base.lamp.fluor.off | лампа дневного света гаснет | onFixtureEvent Off | 23 | 3D | oneshot | 2 | нет | fluorescent switching off, tick, buzz fades | ⬜ |
| base.lamp.bulb.burnout | лампа накаливания перегорает (E05) | onFixtureEvent Broken / Break() | BaseMain ×21, Garage 1, GenContainer 3 | 3D | oneshot | 3 | нет | bulb filament burning out, tiny ping | ⬜ |
| base.lamp.bulb.pop | лампа лопается (Ламповый, E08) | SkyIslandLightManager: Break() у ламп рядом | все лампы накаливания | 3D | oneshot | 4 | средне | light bulb bursting, sharp pop, glass falling | ⬜ |
| base.lamp.tube.burst | трубка лампы дневного света лопается (E08) | то же | 23 трубки | 3D | oneshot | 3 | средне | fluorescent tube exploding, glass shatter | ⬜ |
| base.lamp.emergency.on | аварийный светильник: щелчок реле, когда пропал ток | onFixtureEvent (Emergency) | BaseMain ×12 | 3D | oneshot | 2 | нет | emergency light relay click, inverter whine | ⬜ |
| base.lamp.exit.buzz | табло выхода жужжит, мигает на садящейся батарее | onFixtureEvent (Exit, батарея < 15 %) | BaseMain ×4 | 3D | loop | 1 | нет | faint buzzing exit sign | ⬜ |
| base.lamp.flood.on | галогенный прожектор фасада: щелчок, гул | onFixtureEvent Starting (Halogen) | FX_WallFlood ×2 | 3D | oneshot | 2 | нет | halogen floodlight on, tick, filament hum | ⬜ |
| base.lamp.replace | выкрутить или вкрутить лампочку (Р5) | нет кода | любой светильник | 3D | oneshot | 3 | нет | screwing bulb into ceramic socket, glass squeak | ⬜ |
| base.shutter.switch | клавиша электрических ставен | host Toggle ShutterSwitch | IX_ShutterSwitch_128–130 ×3 | 3D | oneshot | 2 | нет | roller shutter rocker switch click | ⬜ |
| base.shutter.switch.dead | клавиша без тока: щелчок и тишина | onLocked («нет питания») | ×3 | 3D | oneshot | 1 | нет | dead switch click, nothing happens | ⬜ |
| base.shutter.motor.loop | мотор ставен гудит, полотно едет | полотна с needsPower идут к target (~3,3 с) | 13 полотен (Leitstand 6, Kueche 4, Stationsleiter 3) | 3D | loop | 1 | средне | electric roller shutter motor whirring | ⬜ |
| base.shutter.motor.stop | концевик: мотор встал | openAmount == target | 13 | 3D | oneshot | 2 | тихо | shutter motor limit switch click, stop | ⬜ |
| base.shutter.strap.pull | ремень ставни: рывки, трещит барабан | host Toggle ShutterWinder | IX_ShutterWinder ×6 (Lager, Kaserne 2, Waschraum, Flur_OG 2) | 3D | oneshot | 4 | тихо | pulling shutter strap through guide, spring drum ratchet | ⬜ |
| base.shutter.curtain.down | полотно опускается, гремят ламели | SkyIslandInteractable.Update, target 0 | IX_Shutter ×19 | 3D | loop | 2 | средне | PVC roller shutter slats rattling down | ⬜ |
| base.shutter.curtain.up | полотно сворачивается в короб | target 1 | ×19 | 3D | loop | 2 | средне | roller shutter rolling up into box, slats clacking | ⬜ |
| base.shutter.curtain.end | нижняя планка бьёт о подоконник | openAmount → 0 | ×19 | 3D | oneshot | 3 | средне | shutter bottom rail thudding onto sill | ⬜ |
| base.shutter.kitchen.auto | ставни кухни утром поднимаются сами (X11) | ForestOpening.ShuttersUp (Night, t ≥ 45 с, под затемнение) | Kueche ×4 | 3D | oneshot | 1 | средне | roller shutters rising by themselves, motor whir | ⬜ |
| base.shutter.knock | три стука в ставню (X07) | Scares() → Sound(Sfx.Knock) | 19 точек Knock_Shutter_* | 3D | oneshot | 3 | средне | three slow knocks on metal shutter from outside | ✅ forest.knock |
| base.shutter.scratch | царапанье по ставне снаружи | нет кода (точки Knock_*) | ×19 | 3D | oneshot | 3 | тихо | something scratching slowly across shutter slats | ⬜ |
| base.shutter.wind | ставни дребезжат в метель | нет кода | ×19 | 3D | oneshot | 4 | нет | roller shutters rattling in storm gusts | ⬜ |
| base.door.steelext.open | наружная стальная дверь: ручка, защёлка, петли | NetworkDoor Opening (host Door Toggle) | MainDoor_1, RoofDoor_17, GarageDoor_19, GarageSideDoor_93, PumpDoor_7 ×5 | 3D | oneshot | 3 | средне | heavy steel exterior door, lever handle, latch, hinge creak, wind | ⬜ |
| base.door.steelext.close | наружная стальная дверь: захлопнуть | NetworkDoor Closed | те же ×5 + ExitDoor_2 | 3D | oneshot | 3 | громко | heavy steel door slams in frame, latch clack, boom | ⬜ |
| base.door.panic.open | запасный выход: штанга антипаники | NetworkDoor Opening | IX_ExitDoor_2 ×1 | 3D | oneshot | 2 | средне | panic bar pushed, loud clack, steel door swings out | ⬜ |
| base.door.steelint.open | внутренняя стальная дверь: открыть | NetworkDoor Opening | IX_Door_3, _4, _16 ×3 | 3D | oneshot | 3 | тихо | steel interior door handle and latch, hinge squeal | ⬜ |
| base.door.steelint.close | внутренняя стальная дверь: закрыть | NetworkDoor Closed | ×3 | 3D | oneshot | 3 | средне | steel interior door shut, latch thunk | ⬜ |
| base.door.fire.open | противопожарная дверь: тяжело, доводчик | NetworkDoor Opening | IX_Door_8, _12, _14, _15 ×4 | 3D | oneshot | 3 | тихо | heavy fire door pushed, hydraulic closer hiss, stairwell echo | ⬜ |
| base.door.fire.close | противопожарная дверь: закрыть | NetworkDoor Closed | ×4 | 3D | oneshot | 3 | средне | fire door closing on closer, latch clunk, concrete echo | ⬜ |
| base.door.room.open | комнатная дверь: нажать ручку, защёлка, скрип | NetworkDoor Opening | IX_Door_6, _7, _9, _10, _11, _13 ×6 | 3D | oneshot | 4 | тихо | wooden office door lever handle, latch, hinge creak | ⬜ |
| base.door.room.close | комнатная дверь: закрыть | NetworkDoor Closed | ×6 | 3D | oneshot | 4 | тихо | wooden interior door closed, latch click, soft thud | ⬜ |
| base.door.glass.open | стеклянная дверь тамбура: открыть | NetworkDoor Opening | IX_Door_5 ×1 | 3D | oneshot | 2 | тихо | glazed aluminium door opened, pane rattles | ⬜ |
| base.door.glass.close | стеклянная дверь: закрыть | NetworkDoor Closed | ×1 | 3D | oneshot | 2 | средне | glass door shut, pane rattle, latch | ⬜ |
| base.door.barracks.creak | дверь спальни медленно открывается сама (X10) | Night() t ≥ 26 с → ServerPlay | IX_Door_10, Kaserne | 3D | oneshot | 1 | средне | long slow creak of wooden door opening in dark | ✅ forest.door.creak |
| base.door.locked | заперто: ручка дёргается, полотно держит ригель | host ServerUse locked («Заперто», «Заперто на засов») | 9 замков: MainDoor_1, ExitDoor_2, GarageDoor_19, GarageGate_18, Cont 83–85, Garage 92–93 | 3D | oneshot | 3 | тихо | locked steel door handle rattled, bolt holds | ⬜ |
| base.door.unlock.key | ключ в цилиндре: поворот, ригель уходит | ForestOpening.TryUnlock (бит замка снят) | те же 9 | 3D | oneshot | 3 | тихо | key turning in lock cylinder, deadbolt slides back | ⬜ |
| base.door.blocked | дверь упёрлась в игрока | DoorState.Blocked; HostDoor «Мешает игрок» | все двери | 3D | oneshot | 2 | тихо | door bumping into body, soft thud | ⬜ |
| base.door.bar.raise | засов входной двери: поднять | host Toggle Furniture, target 1 | IX_MainDoorBar_311 ×1 | 3D | oneshot | 2 | тихо | heavy steel door bar lifted from cradle, scrape | ⬜ |
| base.door.bar.drop | засов: опустить в держатель (S9) | IX_MainDoorBar_311, target 0 | ×1 | 3D | oneshot | 2 | средне | steel bar dropped into door brackets, loud clunk | ⬜ |
| base.door.knock.fists | кулаки в наружную дверь, всё сильнее | нет кода (BASE_THREATS: засов не опущен) | Knock_MainDoor_1, ExitDoor_2, RoofDoor_17, GarageDoor_19, GarageSideDoor ×5 | 3D | oneshot | 4 | громко | fists pounding on steel door from outside, escalating | ⬜ |
| base.door.doorbell | звонок у входа (BITTE KLINGELN) | нет кода, сценария нет | вход BaseMain ×1 | 3D | oneshot | 2 | средне | old electric door bell ringing inside hallway | ⬜ |
| base.gate.upover.open | подъёмно-поворотные ворота: пружины, лист, тяги | NetworkDoor Opening | IX_GarageGate_18, IX_GarageDoor_92 ×2 | 3D | oneshot | 3 | громко | up-and-over steel garage door lifted, springs groaning | ⬜ |
| base.gate.upover.close | ворота: опустить, удар, тяги в косяки | NetworkDoor Closed | ×2 | 3D | oneshot | 3 | громко | garage door slammed down, steel boom, lock rods clank | ⬜ |
| base.gate.upover.rattle | ворота гремят от удара снаружи | нет кода (Knock_GarageGate_18, Knock_GarageGate) | ×2 | 3D | oneshot | 3 | громко | garage door shaken from outside, sheet metal crash | ⬜ |
| base.furn.drawer.metal.open | металлический ящик тумбы: выдвинуть | host Toggle Furniture | IX_Drawer_22–37, Leitstand ×16 | 3D | oneshot | 4 | тихо | steel desk drawer pulled on rollers, pens rattle | ⬜ |
| base.furn.drawer.metal.close | металлический ящик: задвинуть | то же | ×16 | 3D | oneshot | 4 | тихо | steel drawer pushed shut, bang on stop | ⬜ |
| base.furn.drawer.wood.open | ящик деревянного стола начальника: выдвинуть | host Toggle Furniture | IX_Drawer_59–68, Stationsleiter ×10 | 3D | oneshot | 4 | тихо | wooden desk drawer sliding open | ⬜ |
| base.furn.drawer.wood.close | деревянный ящик: задвинуть | то же | ×10 | 3D | oneshot | 4 | тихо | wooden drawer closed, soft knock | ⬜ |
| base.furn.locker.open | стальной шкафчик: открыть | host Toggle Furniture | IX_LockerDoor_20, 21, 54–57 ×6 | 3D | oneshot | 3 | тихо | metal locker door opened, latch, sheet wobble | ⬜ |
| base.furn.locker.close | шкафчик: закрыть | то же | ×6 | 3D | oneshot | 3 | средне | metal locker door slammed, tinny clang | ⬜ |
| base.furn.tumba.open | тумбочка у койки: открыть | host Toggle Furniture | IX_TumbaDoor_52, 53 ×2 | 3D | oneshot | 2 | тихо | small chipboard cabinet door creak | ⬜ |
| base.furn.tumba.close | тумбочка: закрыть | то же | ×2 | 3D | oneshot | 2 | тихо | small cabinet door wooden click | ⬜ |
| base.furn.rack.open | дверца 19″ стойки (дымчатое стекло) | host Toggle Furniture | IX_RackDoor_38–44 ×7 | 3D | oneshot | 2 | тихо | glass rack door opened, magnetic catch, fans louder | ⬜ |
| base.furn.rack.close | дверца стойки: закрыть | то же | ×7 | 3D | oneshot | 2 | тихо | glass rack door closed, rattle, magnet click | ⬜ |
| base.furn.fridge.open | холодильник: открыть | host Toggle Furniture | IX_FridgeDoor_58 ×1 | 3D | oneshot | 2 | тихо | fridge door seal unsticking, bottles clink | ⬜ |
| base.furn.fridge.close | холодильник: закрыть | то же | ×1 | 3D | oneshot | 2 | тихо | fridge door shut, suction thump | ⬜ |
| base.furn.fridge.hum | компрессор холодильника гудит (с током) | нет кода (Audio_Fridge, host.Powered) | Kueche ×1 | 3D | loop | 1 | нет | old refrigerator compressor hum | ⬜ |
| base.furn.fridge.click | реле пуска и останова, дрожь корпуса | нет кода | ×1 | 3D | oneshot | 3 | нет | fridge thermostat click, compressor shudders on | ⬜ |
| base.furn.bunk.lie | лечь на койку: скрип пружин (S10) | ForestBunks.ServerUse → ServerSit | Kaserne, Sleep_1–4 ×4 | 3D | oneshot | 3 | тихо | lying on old metal bunk bed, springs creak | ⬜ |
| base.furn.bunk.getup | встать с койки | выход с места (держать F) | ×4 | 3D | oneshot | 3 | тихо | getting up from creaky metal bunk | ⬜ |
| base.furn.safe | сейф начальника: замок, тяжёлая дверца | нет кода, нет IX (И5, ключ Stationsleiter) | Stationsleiter ×1 | 3D | oneshot | 2 | тихо | heavy office safe unlocked, bolts retract, thick door swings | ⬜ |
| base.furn.chair.scrape | стул скрежещет по полу | нет кода (стулья — часть меша) | Leitstand 5, Stationsleiter 1, Kueche 4, Kaserne — лавки | 3D | oneshot | 4 | тихо | chair scraped across linoleum | ⬜ |
| base.heat.pump.switch | клавиша насоса отопления | ForestOpening.ServerUse(HeatPump) | IX_HeatPump_304 ×1 | 3D | oneshot | 2 | нет | panel rocker switch flicked | ⬜ |
| base.heat.pump.start | пуск насоса: стук в трубах | _pumpOn false→true | насосная группа, Heizung | 3D | oneshot | 2 | тихо | circulation pump starting, water knocks in pipes | ⬜ |
| base.heat.pump.loop | циркуляционный насос гудит | _pumpOn (сейчас тишина) | Heizung | 3D | loop | 1 | нет | heating circulation pump hum, water flowing | ⬜ |
| base.heat.burner.switch | клавиша горелки | ServerUse(Burner) | IX_HeatBurner_305 ×1 | 3D | oneshot | 2 | нет | panel rocker switch flicked | ⬜ |
| base.heat.burner.ignite | горелка: вентилятор, искра, хлопок пламени | UseBurner → Sound(Sfx.BurnerIgnite); Cue() возвращает null | Audio_Burner, Heizung | 3D | oneshot | 2 | средне | oil burner ignition, fan spin-up, spark buzz, whoomp | 🔇 |
| base.heat.burner.fault | авария горелки STÖRUNG | UseBurner, первый пуск → Sfx.BurnerFault | Audio_Burner | 3D | oneshot | 1 | тихо | burner lockout relay clunk, fault buzzer | ✅ forest.burner.fault |
| base.heat.burner.loop | горелка ревёт, котёл работает | Machines(): _burner == 2 | Audio_Burner | 3D | loop | 1 | средне | oil boiler burner roaring in basement | ✅ forest.boiler.loop |
| base.heat.burner.off | горелка гаснет, вентилятор выбегает | _burner 2→0 (выключили, нет тока, NOT-AUS, давление < 0,8 бар) | Audio_Burner | 3D | oneshot | 2 | тихо | oil burner shutting off, flame dies, fan winds down | ⬜ |
| base.heat.reset.press | кнопка сброса горелки | ServerUse(BurnerReset) | IX_BurnerReset_308 ×1 | 3D | oneshot | 2 | нет | reset button pressed, relay clack | ⬜ |
| base.heat.thermostat.click | ручка термостата: щелчок на градус | ServerUse(Thermostat), колесо ±1 °C | IX_HeatThermostat_306 ×1 | 3D | oneshot | 3 | нет | thermostat knob detent click | ⬜ |
| base.heat.fill.open | кран подпитки: открыть | ServerUse(FillValve) → _fillOpen true | IX_HeatFill_307 ×1 | 3D | oneshot | 2 | тихо | brass tap lever turned, water starts hissing | ⬜ |
| base.heat.fill.close | кран подпитки: закрыть | _fillOpen false | ×1 | 3D | oneshot | 2 | тихо | tap lever closed, flow stops | ⬜ |
| base.heat.fill.flow | вода идёт в систему, давление растёт | ServerTick при _fillOpen (до 2,5 бар) | IX_HeatFill_307 | 3D | loop | 1 | нет | water hissing into heating pipes | ⬜ |
| base.heat.safety.hiss | предохранительный клапан травит при 2,5 бар | ServerTick → Sound(Sfx.Hiss) | группа безопасности | 3D | oneshot | 1 | средне | safety valve releasing steam, loud hiss | ✅ forest.hiss |
| base.heat.safety.loop | клапан травит, пока кран открыт | нет кода (сейчас один раз) | группа безопасности | 3D | loop | 1 | средне | continuous relief valve steam hiss | ⬜ |
| base.heat.boiler.tick | котёл тикает, нагреваясь и остывая | нет кода (Audio_Boiler, по ходу _boilerC) | Heizung | 3D | oneshot | 6 | нет | steel boiler casing ticking with heat | ⬜ |
| base.heat.radiator.tick | батареи щёлкают и булькают | нет кода (прогрев) | радиаторы всех комнат | 3D | oneshot | 6 | нет | cast iron radiator ticking and gurgling | ⬜ |
| base.heat.pipes.knock | гидроудар в магистралях подвала | нет кода | магистрали Flur_UG | 3D | oneshot | 3 | тихо | heating pipes banging, water hammer | ⬜ |
| base.water.tap | кран умывальника или мойки: течёт вода | нет кода, нет IX | Waschraum 4 чаши, мойка Kueche | 3D | loop | 2 | тихо | old tap running into steel sink | ⬜ |
| base.water.wc | смыв унитаза | нет кода, нет IX | Waschraum ×2 | 3D | oneshot | 2 | тихо | old toilet cistern flush and refill | ⬜ |
| base.water.shower | душ | нет кода, нет IX | Waschraum ×1 | 3D | loop | 1 | тихо | shower onto tiled floor | ⬜ |
| base.water.drip | капли (в тишине без тока) | нет кода (DRESSING_GUIDE 4.4) | Waschraum, Kueche, трап гаража, бункер | 3D | oneshot | 6 | нет | single water drip, quiet tiled room | ⬜ |
| base.water.pipes.creak | трубы скрипят на морозе | нет кода | Flur_UG, Waschraum | 3D | oneshot | 4 | нет | cold pipes creaking and ticking in wall | ⬜ |
| base.kitchen.coffee.brew | кофеварка: бульканье, шипение | нет кода | Kueche, Leitstand ×2 | 3D | loop | 1 | тихо | 1980s drip coffee machine gurgling | ⬜ |
| base.kitchen.microwave.run | микроволновка гудит, крутится тарелка (E09) | нет кода (EVENTS E09) | Kueche ×1 | 3D | loop | 1 | тихо | old microwave humming, turntable | ⬜ |
| base.kitchen.microwave.ding | микроволновка: звонок в конце | нет кода (E09) | ×1 | 3D | oneshot | 1 | средне | mechanical microwave bell ding | ⬜ |
| base.kitchen.microwave.door | дверца микроволновки | нет кода | ×1 | 3D | oneshot | 2 | тихо | microwave door latch pop, shut | ⬜ |
| base.kitchen.tv.on | ЭЛТ-телевизор: щелчок, писк, удар размагничивания | нет кода | Kueche ×1 | 3D | oneshot | 2 | тихо | CRT TV switched on, degauss thump, whine | ⬜ |
| base.kitchen.tv.static | снег на экране | нет кода | ×1 | 3D | loop | 1 | тихо | analogue TV static from small speaker | ⬜ |
| base.clock.tick | настенные часы тикают | нет кода | Leitstand, Kueche, Stationsleiter ×3 | 3D | loop | 1 | нет | wall clock ticking in silent room | ⬜ |
| base.pc.power.on | ПК 486: кнопка, писк при загрузке, раскрутка диска | нет кода (писем на ПК нет) | ПК базы в Leitstand (+ObjectPC на объектах) | 3D | oneshot | 2 | тихо | 486 PC power on, fan spins up, BIOS beep, disk whirs | ⬜ |
| base.pc.hum | ПК: вентилятор блока питания, шелест диска | нет кода | ×1 (+3 системных блока пультовой) | 3D | loop | 1 | нет | beige PC fan hum, hard drive whir | ⬜ |
| base.pc.hdd.seek | треск головок диска | нет кода | ×1 | 3D | oneshot | 4 | нет | old hard drive seeking chatter | ⬜ |
| base.pc.keys | клавиатура | нет кода | ×1 | 3D | oneshot | 8 | нет | mechanical keyboard keystroke 1990s | ⬜ |
| base.pc.mouse | щелчок мыши | нет кода | ×1 | 3D | oneshot | 2 | нет | ball mouse button click | ⬜ |
| base.pc.floppy | дискета: вставить, чтение | нет кода | ×1 | 3D | oneshot | 2 | нет | 3.5 floppy inserted, drive grinding | ⬜ |
| base.pc.mail | писк спикера: новое письмо (07:30) | нет кода | ×1 | 3D | oneshot | 1 | тихо | PC speaker double beep | ⬜ |
| base.pc.crt.on | ЭЛТ-монитор: писк, размагничивание | нет кода (с током) | Leitstand 2, терминал Serverraum 1 | 3D | oneshot | 2 | тихо | CRT monitor degauss thunk, rising whine | ⬜ |
| base.pc.crt.whine | писк строчной развёртки | нет кода | ×3 | 3D | loop | 1 | нет | faint 15 kHz CRT whine | ⬜ |
| base.pc.morse | мини-игра Funker: морзянка из спикера | нет кода (VISION §8) | ПК базы | 3D | oneshot | 4 | нет | PC speaker morse beeps | ⬜ |
| base.printer.print | матричный принтер печатает ленту | нет кода (DRESSING_GUIDE) | Leitstand ×1 | 3D | loop | 1 | тихо | dot matrix printer screeching, carriage, paper feed | ⬜ |
| base.printer.tear | оторвать ленту распечатки | нет кода | ×1 | 3D | oneshot | 2 | нет | tearing perforated printer paper | ⬜ |
| base.fax.handset.up | трубка факса: снять | нет кода (Fax_Handset) | Fax ×1 | 3D | oneshot | 2 | нет | handset lifted, hook switch click | ⬜ |
| base.fax.handset.down | трубка факса: положить | нет кода | ×1 | 3D | oneshot | 2 | нет | handset placed in cradle | ⬜ |
| base.fax.tone | гудок в трубке и в линии | нет кода | факс и телефоны базы | 3D | loop | 1 | нет | German 425 Hz dial tone in handset | ⬜ |
| base.fax.key | кнопки факса, тоны набора | нет кода (Fax_Btn_*) | ×1 | 3D | oneshot | 6 | нет | fax keypad press with DTMF beep | ⬜ |
| base.fax.feed | лист уходит в щель | нет кода (Slot_DocIn) | ×1 | 3D | oneshot | 2 | нет | fax motor pulling sheet in | ⬜ |
| base.fax.handshake | модемные свисты, передача | нет кода (С05, Р3) | ×1 | 3D | loop | 2 | тихо | fax handshake tones, screeching transmission | ⬜ |
| base.fax.ring | входящий факс звонит | нет кода (ответ «Принято. — K.») | ×1 | 3D | oneshot | 1 | средне | fax machine incoming electronic ring | ⬜ |
| base.fax.print | печать на термобумаге, отрезка | нет кода (Slot_PaperOut) | ×1 | 3D | loop | 1 | тихо | thermal fax printing, stepper whine, cutter snip | ⬜ |
| base.fax.error | писк ERROR | нет кода (Fax_LED_Error) | ×1 | 3D | oneshot | 1 | нет | fax error beeps | ⬜ |
| base.radio.power | рация на пульте: тумблер, шипение | нет кода (у ForestRadio нет звуков устройства) | рация в Leitstand ×1 | 3D | oneshot | 2 | тихо | old transceiver switched on, click, static burst | ⬜ |
| base.radio.static | шипение эфира | VoiceEmitter.Awake → SnowAudio.Clip("radio.static") | рация пульта (и все VoiceEmitter) | 3D | loop | 1 | тихо | VHF radio static hiss | 🔇 radio.static |
| base.radio.click | щелчок тангенты | VoiceEmitter.Play → "radio.click" | ×1 | 3D | oneshot | 3 | тихо | push-to-talk click with chirp | 🔇 radio.click |
| base.radio.squelch | хвост шумодава | VoiceEmitter → "radio.squelch" | ×1 | 3D | oneshot | 3 | тихо | radio squelch tail noise burst | 🔇 radio.squelch |
| base.radio.tune | ручка настройки: щелчки, свист между станциями | нет кода (С11) | ×1 | 3D | oneshot | 4 | тихо | shortwave tuning knob, heterodyne whistles | ⬜ |
| base.phone.ring | телефоны базы звонят (70-е) | нет кода (у телефона КПП есть ✅ forest.phone.ring) | Leitstand, Flur_EG, Garage ×3 | 3D | oneshot | 2 | средне | 1970s German telephone bell | ⬜ |
| base.phone.red.ring | красный телефон начальника (E30) | нет кода (DAYS, день 7) | Stationsleiter ×1 | 3D | oneshot | 2 | средне | red desk phone ringing in dark office | ⬜ |
| base.phone.handset | снять или положить трубку | нет кода | ×4 | 3D | oneshot | 2 | нет | handset lifted, hook click | ⬜ |
| base.speaker.on | громкоговоритель: щелчок, фон усилителя | нет кода (A145; голос — не здесь) | Flur_EG, Flur_OG ×2 | 3D | oneshot | 2 | средне | PA speaker pop and amplifier hum | ⬜ |
| base.speaker.gong | гонг перед объявлением | нет кода | ×2 | 3D | oneshot | 1 | громко | three-tone PA chime in empty corridor | ⬜ |
| base.alarm.siren | сирена аварийного режима: раскрутка, вой, остановка | нет кода (EVENTS, аварийный режим) | база (Kit_Siren) | 3D | loop | 1 | громко | motor siren winding up, wailing, winding down | ⬜ |
| base.alarm.siren.mute | кнопка «тихо» на пульте | нет кода | Leitstand ×1 | 3D | oneshot | 1 | нет | heavy push button on control desk | ⬜ |
| base.alarm.beacon | маячок над входом: моторчик | нет кода | вход ×1 | 3D | loop | 1 | нет | rotating beacon motor ticking | ⬜ |
| base.alarm.firebell | пожарный звонок в коридорах | нет кода (A139) | Flur_EG, Flur_OG, Flur_UG | 3D | loop | 1 | громко | electric fire alarm bell in corridor | ⬜ |
| base.alarm.zone | зуммер зоны на пульте: датчик окна или двери (E06, E07) | нет кода | пульт Leitstand | 3D | oneshot | 2 | тихо | control panel alarm buzzer | ⬜ |
| base.alarm.callpoint | ручной пожарный извещатель | нет кода | ×6 | 3D | oneshot | 2 | тихо | break glass call point pressed | ⬜ |
| base.leit.console | пульты: реле, лампы, гул (с током) | нет кода | Leitstand: 2 пульта и табло антенн | 3D | loop | 1 | нет | control desk hum, occasional relay clicks | ⬜ |
| base.server.racks | стойки: вентиляторы и реле (с током) | нет кода (Audio_ServerRacks) | Serverraum, 7 стоек | 3D | loop | 1 | тихо | rack fans whirring, relays clicking | ⬜ |
| base.ant.cabinet.open | шкаф антенны: открыть | host Toggle Furniture | IX_CabinetDoor_2–6 ×5 | 3D | oneshot | 2 | тихо | outdoor steel cabinet door, rubber seal peels | ⬜ |
| base.ant.cabinet.close | шкаф антенны: закрыть | то же | ×5 | 3D | oneshot | 2 | средне | steel cabinet door shut, seal thump, latch | ⬜ |
| base.ant.cabinet.hum | модули шкафа: вентилятор, гул (с током) | нет кода | ×5 | 3D | loop | 1 | нет | equipment cabinet fan and electronics hum | ⬜ |
| base.ant.drive.hum | приводы азимута гудят | нет кода (Audio_Dish_n_Drive) | Dish_1–5 ×5 | 3D | loop | 1 | нет | large antenna drive motors humming | ⬜ |
| base.ant.drive.clunk | привод антенны клацает | нет кода | ×5 | 3D | oneshot | 3 | средне | heavy gear clunk in antenna mount | ⬜ |
| base.tape.power | магнитофон TR-4: тумблер POWER | нет кода (TapeDeck) | в шкафах антенн ×5 | 3D | oneshot | 2 | нет | tape deck toggle switch click | ⬜ |
| base.tape.door | дверца отсека кассеты | нет кода (IX_TapeDoor) | ×5 | 3D | oneshot | 2 | нет | cassette door springs open, plastic click | ⬜ |
| base.tape.insert | вставить кассету | нет кода (Slot_Tape, С06) | ×5 | 3D | oneshot | 2 | нет | cassette pushed into deck, snap | ⬜ |
| base.tape.eject | EJECT: кассета выскакивает | нет кода | ×5 | 3D | oneshot | 2 | нет | cassette eject clunk | ⬜ |
| base.tape.key | клавиши REC, PLAY, STOP, REW, FF | нет кода | ×5 | 3D | oneshot | 6 | нет | cassette deck piano key, mechanical clunk | ⬜ |
| base.tape.run | идёт лентопротяжка (запись 3 мин) | нет кода | ×5 | 3D | loop | 1 | нет | cassette transport motor hum, tape hiss | ⬜ |
| base.tape.wind | перемотка | нет кода | ×5 | 3D | loop | 1 | нет | cassette fast rewind whirr | ⬜ |
| base.tape.end | конец ленты: автостоп | нет кода | ×5 | 3D | oneshot | 2 | нет | tape end auto-stop clunk | ⬜ |
| base.meteo.door.open | дверца метеобудки: вертушка, жалюзи | нет кода (IX_ScreenDoor) | MeteoStation ×1 | 3D | oneshot | 2 | тихо | wooden louvred screen door, turn button, creak | ⬜ |
| base.meteo.door.close | дверца метеобудки: закрыть | нет кода | ×1 | 3D | oneshot | 2 | тихо | louvred door shut, light clack | ⬜ |
| base.meteo.cups | чашки анемометра, скрип флюгера | нет кода (Audio_MeteoCups) | ×1 | 3D | loop | 1 | нет | anemometer cups spinning, vane creaking | ⬜ |
| base.meteo.laptop.lid | крышка ноутбука | нет кода (IX_LaptopLid) | ×1 | 3D | oneshot | 2 | нет | laptop lid hinge creak, click | ⬜ |
| base.meteo.laptop.boot | ноутбук: писк, вентилятор, диск | нет кода | ×1 | 3D | oneshot | 1 | нет | 1990s laptop booting, beep, tiny fan | ⬜ |
| base.pumphouse.pump | насос станции с мотором (только с током) | нет кода (Audio_PumpHum) | PumpHouse ×1 | 3D | loop | 1 | средне | electric water pump motor in brick room | ⬜ |
| base.pumphouse.switch | рубильник щита насосной | нет кода, нет IX | ×1 | 3D | oneshot | 2 | тихо | knife switch thrown on small panel | ⬜ |
| base.pumphouse.dosing.valve | краны V1–V3 насоса-дозатора | нет кода (DosingPump_V1–V3) | Item_DosingPump ×3 | 3D | oneshot | 3 | нет | quarter turn ball valve lever | ⬜ |
| base.pumphouse.dosing.switch | переключатель мотора, ручка дозировки | нет кода | ×1 | 3D | oneshot | 2 | нет | rotary switch click on motor box | ⬜ |
| base.pumphouse.dosing.run | мембранный насос-дозатор работает (С08) | нет кода (Audio_Pump) | ×1 | 3D | loop | 1 | тихо | diaphragm dosing pump pulsing, gear motor whine | ⬜ |
| base.pumphouse.dosing.bleed | прокачка: воздух шипит, бульканье | нет кода | ×1 | 3D | oneshot | 2 | нет | air bleeding from pump, sputter then steady flow | ⬜ |
| base.bunker.gate.slide | створка ангара 2,4 т едет по рельсу | нет кода (IX_HangarGate_L/R) | BunkerHangar ×2 | 3D | loop | 2 | громко | huge steel hangar door rolling on rusty rail, rumbling | ⬜ |
| base.bunker.gate.stop | створка бьёт в упор | нет кода | ×2 | 3D | oneshot | 2 | громко | massive sliding door hits end stop, deep boom | ⬜ |
| base.bunker.sidedoor.open | боковая дверь ангара: открыть | нет кода (IX_HangarSideDoor) | ×1 | 3D | oneshot | 2 | средне | octagonal steel bunker door unlatched, hinges groan | ⬜ |
| base.bunker.sidedoor.close | боковая дверь ангара: закрыть | нет кода | ×1 | 3D | oneshot | 2 | громко | steel bunker door slammed, concrete echo | ⬜ |
| base.bunker.panel.key | пульт двери DOOR 3: ключ, лампы | нет кода (акт 3) | BlastDoor ×1 | 3D | oneshot | 2 | нет | key turned in control panel, relays click | ⬜ |
| base.bunker.panel.open | кнопка OPEN под скобой | нет кода | ×1 | 3D | oneshot | 1 | нет | flip guard lifted, heavy button pressed | ⬜ |
| base.bunker.hydraulic | гидравлика набирает давление | нет кода | ×1 | 3D | loop | 1 | средне | hydraulic pump whining, pressure building | ⬜ |
| base.bunker.bolt | засов-гидроцилиндр втягивается | нет кода (IX_BlastDoor_Bolt_1–6) | ×6 | 3D | oneshot | 3 | средне | hydraulic locking bolt retracting, heavy clunk | ⬜ |
| base.bunker.wheel | штурвал двери на 720° | нет кода (IX_BlastDoor_Wheel) | ×1 | 3D | loop | 1 | средне | turning large steel door wheel, grinding ratchet | ⬜ |
| base.bunker.blast.swing | створка 9 т поворачивается | нет кода (IX_BlastDoor) | ×1 | 3D | oneshot | 1 | громко | massive round blast door swinging, deep hinge groan | ⬜ |
| base.bunker.ice.hit | сбить лёд над люком | нет кода (Ice_Hatch_1–3) | BunkerEntrance ×3 | 3D | oneshot | 4 | средне | ice axe striking thick ice, cracking, shards | ⬜ |
| base.bunker.hatch.wheel | штурвал люка на 540° (во льду не идёт) | нет кода (IX_BunkerHatch_Wheel) | ×1 | 3D | oneshot | 2 | тихо | hatch wheel frozen stuck, then grinding free | ⬜ |
| base.bunker.hatch.open | крышка люка бункера: открыть | нет кода (IX_BunkerHatch) | ×1 | 3D | oneshot | 2 | средне | steel escape hatch heaved open, hinges shriek | ⬜ |
| base.bunker.hatch.close | крышка люка бункера: закрыть | нет кода | ×1 | 3D | oneshot | 2 | громко | steel hatch dropped, clang down shaft | ⬜ |
| base.bunker.tarp | снять брезент с самолёта | нет кода (Grab_Tarp) | PlaneTarp ×1 | 3D | oneshot | 2 | тихо | heavy canvas tarp dragged off aircraft | ⬜ |
| base.bunker.lamps.on | лампы ангара загораются (контактор) | нет кода (bunker_lights) | Light_Hangar_1–6, тамбур 2, вход 1 | 3D | oneshot | 1 | тихо | contactor clunk, lamps humming on one by one | ⬜ |
| base.knock.glass | стук в стекло снаружи | нет кода (Knock_W_*) | гараж корпуса 2, Treppenhaus 2, Garage 2, окно контейнера | 3D | oneshot | 3 | средне | knuckles tapping window glass from outside | ⬜ |
| base.knock.container | стук и царапанье по стенке контейнера | нет кода (Knock_Wall*, Knock_Roof*) | GenContainer, 21 точка | 3D | oneshot | 4 | средне | knocking and scraping on corrugated steel wall | ⬜ |
| base.window.hit | удар в ставню или окно тёмной комнаты, каждый громче | нет кода (BASE_THREATS: Холодный) | 19 ставен и окна | 3D | oneshot | 3 | громко | heavy blow on roller shutter, slats buckle | ⬜ |
| base.window.shatter | стекло лопается (третий удар) | нет кода | окна корпуса | 3D | oneshot | 3 | громко | window pane shattering onto floor | ⬜ |
| base.window.draft | ветер в разбитом окне | нет кода | комната с выбитым окном | 3D | loop | 1 | нет | freezing wind howling through broken window | ⬜ |
| base.board.nail | заколотить окно или проход досками | нет кода (BASE_THREATS, И2) | окна, проход Лесника в подвале | 3D | oneshot | 4 | средне | hammering nails into board over window | ⬜ |
| base.roof.steps | шаги по гравию крыши (X06) | Scares() → StartWalk(step.gravel.walk) | крыша BaseMain, путь X06 | 3D | oneshot | 8 | средне | heavy footsteps on gravel roof heard from below | ✅ step.gravel.walk |
| base.room.generic | общий тон помещения | ForestAmbience._indoor (SurfaceResolver.Indoors) | любое место под крышей | 2D | loop | 1 | нет | quiet indoor room tone | ✅ amb.indoor.tone |
| base.room.windfang | тон тамбура | нет кода (зоны комнат есть в Forest_Opening.json rooms) | Windfang | 2D | loop | 2 (с током / без) | нет | entrance lobby tone, draught under steel door, muffled wind | ⬜ |
| base.room.leitstand | тон пультовой | нет кода | Leitstand | 2D | loop | 2 | нет | control room tone, electronics hum, CRT whine | ⬜ |
| base.room.corridor | тон коридоров | нет кода | Flur_EG, Flur_OG, Flur_UG | 2D | loop | 2 | нет | linoleum corridor tone, fluorescent hum, distant boiler | ⬜ |
| base.room.lager | тон склада | нет кода | Lager | 2D | loop | 2 | нет | dead quiet storeroom, cardboard creaks | ⬜ |
| base.room.serverraum | тон серверной | нет кода | Serverraum | 2D | loop | 2 | нет | server room fans, cool air hum | ⬜ |
| base.room.garage | тон гаража корпуса | нет кода | Garage (BaseMain) | 2D | loop | 2 | нет | large concrete garage, reverberant silence, drips | ⬜ |
| base.room.kaserne | тон спальни | нет кода | Kaserne | 2D | loop | 2 | нет | dormitory tone, radiator ticks, wind on shutters | ⬜ |
| base.room.kueche | тон кухни | нет кода | Kueche | 2D | loop | 2 | нет | kitchen tone, fridge hum, clock tick | ⬜ |
| base.room.waschraum | тон умывальной | нет кода | Waschraum | 2D | loop | 2 | нет | tiled washroom, drips, pipe hum, echo | ⬜ |
| base.room.stationsleiter | тон кабинета начальника | нет кода | Stationsleiter | 2D | loop | 2 | нет | quiet wood-panelled office, clock ticking | ⬜ |
| base.room.heizung | тон котельной | нет кода | Heizung | 2D | loop | 2 | нет | basement boiler room, warm pipes ticking | ⬜ |
| base.room.schaltraum | тон электрощитовой | нет кода | Schaltraum | 2D | loop | 2 | нет | switchroom, busbar hum, relay ticks | ⬜ |
| base.room.treppenhaus | тон лестничной клетки | нет кода | Treppenhaus | 2D | loop | 2 | нет | concrete stairwell echo, wind at roof door | ⬜ |
| base.room.garageshed | тон отдельного гаража | нет кода | Garage (отдельный) | 2D | loop | 2 | нет | steel shed interior, wind on sheet walls | ⬜ |
| base.room.gencont | тон контейнера с заглушённым мотором | нет кода | GenContainer | 2D | loop | 1 | нет | cold steel container, silent genset, wind in louvres | ⬜ |
| base.room.pumphouse | тон насосной | нет кода | PumpHouse | 2D | loop | 2 | нет | small brick pump house, river behind wall | ⬜ |
| base.room.bunker.hangar | тон ангара бункера | нет кода | BunkerHangar | 2D | loop | 2 | нет | vast underground concrete hangar, distant drips | ⬜ |
| base.room.bunker.vestibule | тон тамбура бункера | нет кода | BlastDoor, тамбур и туннель | 2D | loop | 1 | нет | bunker vestibule, drips into ice channel, deep hum below | ⬜ |
| base.room.bunker.shaft | тон шахты и прохода | нет кода | BunkerEntrance, шахта | 2D | loop | 1 | нет | narrow concrete shaft, close echo, dripping | ⬜ |
| base.amb.wind.gaps | ветер свистит в оконных щелях | нет кода (DRESSING_GUIDE 4.4) | окна корпуса | 3D | oneshot | 4 | нет | wind whistling through window frame gap | ⬜ |
| base.amb.building.creak | здание трещит на морозе | нет кода | BaseMain, Garage | 3D | oneshot | 5 | нет | concrete building creaking and popping in frost | ⬜ |

## Замечания

- **Числа из прежнего списка не сходятся с данными.** В `SOUND_LIST.md` (и в задании) было 58 ящиков, 54 выключателя, 39 автоматов, 57 штор. В `Forest_Layout.json` и `Forest.unity` на самом деле:
  - 19 выключателей света и 3 клавиши ставен;
  - 21 автомат (18 в корпусе, 3 в контейнере), 2 поворотных выключателя, 2 рубильника;
  - 26 ящиков на базе (и ещё 6 на КПП);
  - 19 полотен ставен: 13 на моторе, 6 на ремне;
  - 27 дверей и ворот.

  В таблице стоят эти числа.
- **Ни одна обычная деталь базы не звучит.** Двери, ящики, выключатели, ставни переключаются через `SkyIslandLocationHost.ServerUse` → `SkyIslandInteractable.Toggle` или `NetworkDoor`, и звука там нет совсем. Нужен один общий обработчик: звук по категории детали и префиксу имени. Клиенты получают только биты состояния (`ApplyWord`), поэтому звук лучше запускать по смене `target` на каждом участнике, а не отдельным RPC.
- **Для ламп событие уже есть, звука нет.** `SkyIslandLightManager.onFixtureEvent` сообщает о пуске, вспышке стартера, сбое, поломке, включении и выключении. При подаче тока на базу разом стартуют 23 лампы дневного света и 31 фонарь двора, поэтому одновременных звуков надо ограничить: несколько ближних плюс один общий «каскад».
- **Чего код просит, но не получает.** Поджиг горелки: `Sound(Sfx.BurnerIgnite)` вызывается, но `Cue()` возвращает null. Рация пульта: `radio.static`, `radio.click`, `radio.squelch`. Ещё одно: предохранительный клапан шипит один раз, хотя при открытом кране давление так и держится на 2,5 бар — возможно, нужен loop.
- **Останов генератора всегда звучит одинаково.** Код знает причину (рычаг, кончилось топливо, пульт), но играет один клип. Нужно решить, отличается ли звук E01 «Холодный заглушил» от E03 «кончилось топливо». В BASE_THREATS кашель от снега (B01) нарочно звучит так же, как E01.
- **Многое есть только в документах.** Полный пульт контейнера, бункер, факс, ПК, принтер, сирена, громкоговоритель, микроволновка, телевизор, сантехника, сейф: кода и деталей нет, в таблице это «нет кода». У сантехники, кофеварки, телевизора и сейфа нет деталей даже в модели. Надо решить, делать их интерактивными или оставить фоном.
- **Существа пока не слышат звуки базы.** Шум для них (`noise`) задан в библиотеке только у ударов предметов. А по R2 Холодный идёт на шум генератора. Для генератора, сирены, хлопков дверей нужно периодически вызывать `NoiseEvents.Emit`; радиусы 5 / 15 / 30 м пока временные.
- **Что я оставил другим агентам.** Ключница (`IX_KeyBoxDoor_303` стоит в будке КПП), кнопки пульта КПП 319/320 в пультовой, шаги игроков (решётка перехода — уже `step.metal`), ледоруб и ключи как предметы, плеск в канистре, шаги Лесника X09 (`step.tile.walk` ✅). Ретранслятор, радар, вышку, часовню и избушку не брал: их нет в списке базы. Для их ПК подойдут строки `base.pc.*`.
- **Ставни кухни X11 поднимаются мгновенно** (`openAmount = 1`), под чёрным экраном. Звук придётся играть под затемнение — или не играть вовсе. Выключатель без тока сейчас щёлкает так же; нужен ли отдельный «мёртвый» щелчок — решать.
- **Тон помещений сейчас один на всё:** `amb.indoor.tone` по признаку «под крышей». Для тона по комнатам нужны зоны: коробки 15 комнат уже есть в `Forest_Opening.json` → `rooms`. И нужно состояние тока (`host.Powered`): правило базы — «без сети тихо, только ветер и капли».

---

## Звуки фургона и самолёта: полный список

Здесь 186 звуков. В коде просит звук только один: `plane.stall.horn`. Сирену создаёт `PlanePresentation.Awake` через `SnowAudio.Clip`, а файла в `tools/audio/audio_manifest.json` нет. Ни один звук этих двух машин сейчас не звучит. Ни один скрипт фургона и самолёта не вызывает `SnowAudio.Play` или `ServerPlay`.

Шум — радиусы из `NoiseEvents`: тихо = 5 м, средне = 15 м, громко = 30 м и больше.

| id | what | when | where | 3D/2D | oneshot/loop | variants | шум | prompt | status |
|---|---|---|---|---|---|---|---|---|---|
| **ФУРГОН — двери** |||||||||
| van.door.cab.open | дверь кабины: ручка, защёлка, петли | VanVehicle.ServerUse → NetworkDoor.Toggle(Manual), Closed→Opening (NetworkDoor.FixedUpdate door_state) | doorL, doorP (2) | 3D | oneshot | 4 | средне | old Soviet van cab door opening, metal latch clack, stiff cold hinge creak | ⬜ |
| van.door.cab.close | дверь кабины захлопнуть | NetworkDoor Closing→Closed | doorL, doorP (2) | 3D | oneshot | 4 | средне | heavy old van steel door slam, rattling thin metal, latch catch | ⬜ |
| van.door.side.open | боковая дверь салона открыть | то же | doorSalon (1) | 3D | oneshot | 3 | средне | old van side passenger door opening, latch, long hinge creak | ⬜ |
| van.door.side.close | боковая дверь салона закрыть | то же | doorSalon (1) | 3D | oneshot | 3 | средне | old van side door slam, hollow steel body boom | ⬜ |
| van.door.rear.open | задняя створка открыть | то же | doorZL, dooZP (2) | 3D | oneshot | 3 | средне | rear barn door of old van opening, latch rod clunk, hinge squeal | ⬜ |
| van.door.rear.close | задняя створка закрыть | то же | doorZL, dooZP (2) | 3D | oneshot | 3 | средне | rear van door slam, double metal clang, loose panel rattle | ⬜ |
| van.door.blocked | дверь упёрлась в препятствие | NetworkDoor → DoorState.Blocked | все 5 | 3D | oneshot | 3 | тихо | car door bumping into obstacle, dull thud, hinge stop | ⬜ |
| van.door.frozen | примёрзший уплотнитель трескается при открытии | нет кода | все 5 | 3D | oneshot | 3 | тихо | frozen rubber door seal cracking open, ice crunch | ⬜ |
| **ФУРГОН — крышки, капот, баки** |||||||||
| van.glovebox.open | бардачок открыть | VanVehicle.ServerControl(Glovebox_Lid 0→1) | Glovebox_Lid (1) | 3D | oneshot | 2 | нет | old car glovebox latch click, lid drops open | ⬜ |
| van.glovebox.close | бардачок закрыть | ServerControl(Glovebox_Lid 1→0) | (1) | 3D | oneshot | 2 | нет | glovebox lid snap shut, plastic and metal | ⬜ |
| van.fusebox.open | крышка блока предохранителей открыть | ServerControl(FuseBox_Lid 0→1) | (1) | 3D | oneshot | 2 | нет | small plastic fuse box cover unclipped | ⬜ |
| van.fusebox.close | крышка блока предохранителей закрыть | ServerControl(FuseBox_Lid 1→0) | (1) | 3D | oneshot | 2 | нет | small plastic cover clipped shut | ⬜ |
| van.toolbox.open | крышка ящика с инструментом открыть | ServerControl(ToolBox_Lid 0→1) | (1) | 3D | oneshot | 2 | тихо | metal tool box lid opening, tools rattle inside | ⬜ |
| van.toolbox.close | крышка ящика с инструментом закрыть | ServerControl(ToolBox_Lid 1→0) | (1) | 3D | oneshot | 2 | тихо | metal tool box lid slammed, wrenches clank | ⬜ |
| van.batterybox.open | крышка ящика АКБ под сиденьем L4 | ServerControl(BatteryBox_Lid 0→1) | (1) | 3D | oneshot | 2 | тихо | wooden battery box lid lifted under van bench seat | ⬜ |
| van.batterybox.close | крышка ящика АКБ закрыть | ServerControl(BatteryBox_Lid 1→0) | (1) | 3D | oneshot | 2 | тихо | wooden box lid dropped shut, dull knock | ⬜ |
| van.enginecover.unlatch | кожух двигателя: открыть 2 защёлки | ServerControl(EngineCover 0→1) | EngineCover (1) | 3D | oneshot | 2 | нет | two spring steel latches unclipped, metallic snap | ⬜ |
| van.enginecover.raise | поднять кожух | ServerControl(EngineCover 1→2) | (1) | 3D | oneshot | 2 | тихо | heavy insulated engine cover lifted, hinge creak | ⬜ |
| van.enginecover.close | опустить и защёлкнуть | ServerControl(EngineCover 2→0); вариант Back 1→0 — только защёлки | (1) | 3D | oneshot | 2 | тихо | engine cover dropped down, latches snap shut | ⬜ |
| van.fuelflap.open | лючок бака открыть | ServerControl(FuelFlap_L/R 0→1) | FuelFlap_L, FuelFlap_R (2) | 3D | oneshot | 2 | нет | small sprung steel fuel flap popped open | ⬜ |
| van.fuelcap.off | открутить крышку бака, вздох воздуха | ServerControl(FuelFlap 1→2) | FuelCap_L/R (2) | 3D | oneshot | 3 | нет | metal fuel cap unscrewed, thread scrape, vapour hiss | ⬜ |
| van.fuelcap.on | закрутить крышку | ServerControl(FuelFlap 2→3) | (2) | 3D | oneshot | 3 | нет | metal fuel cap screwed tight, ratcheting threads | ⬜ |
| van.fuelflap.close | закрыть лючок | ServerControl(FuelFlap 3→0) | (2) | 3D | oneshot | 2 | нет | fuel flap snapped shut | ⬜ |
| van.fuel.pour | заливка бензина из канистры в бак | нет кода (зоны Jerrycan_1/2 «пока не работает»; по плану В5 12 л/мин) | Pour_Fuel_L/R (2) | 3D | loop | 2 | тихо | petrol glugging from jerrycan into car tank neck | ⬜ (похоже на ✅ forest.fuel.pour) |
| van.jerrycan.take | канистру из держателя взять / поставить | нет кода (зоны Jerrycan_1/2 есть) | 2 | 3D | oneshot | 2 | тихо | steel jerrycan pulled from bracket, strap buckle, fuel slosh | ⬜ |
| **ФУРГОН — сиденья, шторки** |||||||||
| van.seat.fold | сложить сиденье L4 (держать 0,6 с) | VanVehicle.ServerFoldSeatUse → ServerControl(Seat_L4 0→1) | Seat_L4 (1) | 3D | oneshot | 2 | тихо | folding van bench seat flipped up, steel frame clunk | ⬜ |
| van.seat.unfold | разложить сиденье L4 | ServerControl(Seat_L4 1→0) | (1) | 3D | oneshot | 2 | тихо | folding seat dropped down onto legs, clank | ⬜ |
| van.seat.sit | сесть / встать: пружины, винил | SeatedVehicle.ServerSit / ServerExit | 8 мест Sit_* | 3D | oneshot | 4 | нет | old vinyl car seat springs creak as person sits | ⬜ |
| van.curtain.close | задёрнуть шторки (12 сразу) | ServerControl(Curtain_L1A 0→1) | 1 зона / 12 шторок | 3D | oneshot | 2 | нет | fabric curtains pulled along metal rod, rings sliding | ⬜ |
| van.curtain.open | открыть шторки | ServerControl(Curtain_L1A 1→0) | (1) | 3D | oneshot | 2 | нет | curtains pulled open, rings rattle on rod | ⬜ |
| **ФУРГОН — ключи, стартер, двигатель** |||||||||
| van.ignition.key.fwd | ключ зажигания 0→I, I→II | ServerControl(Ignition_Key Press) | Ignition_Key (1) | 3D | oneshot | 3 | нет | car ignition key turned one detent, click, keyring jingle | ⬜ |
| van.ignition.key.back | ключ II→I→0 (колесо вниз) | ServerControl(Ignition_Key Back) | (1) | 3D | oneshot | 3 | нет | ignition key turned back, soft detent click | ⬜ |
| van.ignition.key.start | ключ II→III, взвод пружины | ServerControl(Ignition_Key 2→3) | (1) | 3D | oneshot | 2 | нет | ignition key twisted against spring to start position | ⬜ |
| van.ignition.key.return | ключ сам III→II при отпускании | ServerControl(Release 3→2) | (1) | 3D | oneshot | 2 | нет | key springs back from start position, click | ⬜ |
| van.starter.crank | стартер крутит (старт / петля / стоп); скорость по напряжению: свежая / слабая / холодная АКБ | нет кода (В5); сейчас VanVehicle.EngineSwitches заводит мгновенно в III; VanElectrics.StarterAmps и StarterBusVolts есть | Audio_Engine (1) | 3D | loop | 3 скорости | громко | old petrol engine starter motor cranking in freezing cold, slow labouring | ⬜ |
| van.starter.click | только щелчки реле стартера (шина < 8 В, после 3 попыток) | нет кода (StarterBusVolts есть) | (1) | 3D | oneshot | 4 | тихо | dead battery starter solenoid clicking, no cranking | ⬜ |
| van.engine.catch | двигатель схватил: первые вспышки, подхват | VanVehicle.EngineSwitches (_engine false→true) | Audio_Engine | 3D | oneshot | 3 | громко | old 4-cylinder petrol engine catching after crank, sputter then roar | ⬜ |
| van.engine.nostart | прокрутка с чиханием, не схватывает (холод, подсос) | нет кода | (1) | 3D | oneshot | 4 | громко | cold engine cranking with weak coughs and pops, fails to start | ⬜ |
| van.engine.idle.cold | холостые на холодную: неровно, троит | нет кода (температуры двигателя нет) | (1) | 3D | loop | 2 | громко | cold old petrol engine idling rough, misfiring, uneven | ⬜ |
| van.engine.idle | холостые ~600 об/мин | VanTelemetry.Rpm + VanFlags.Engine → VanPresentation | (1), слушать изнутри (кожух в кабине) и снаружи (выхлоп) | 3D | loop | внутри / снаружи; кожух открыт | громко | Soviet UAZ van petrol engine idling, low burble, tappets | ⬜ |
| van.engine.load | обороты под нагрузкой 600–4200 | Telemetry.Rpm, Gas > 0 | (1) | 3D | loop | 3 слоя (низ / середина / верх) | громко | old UAZ engine accelerating under load, rising rpm growl | ⬜ |
| van.engine.offload | без нагрузки / торможение двигателем | Telemetry.Rpm, Gas = 0 | (1) | 3D | loop | 3 слоя | громко | old engine decelerating, overrun crackle, coasting | ⬜ |
| van.engine.limiter | отсечка 4200 об/мин | VanMotion.Step (rpm > cutoffRpm) | (1) | 3D | loop | 2 | громко | engine bouncing off rev limiter, stutter | ⬜ |
| van.engine.stop | выключение (ключ в 0 или I, сняты провода, нет массы): дёрг и остановка | VanVehicle.EngineSwitches (_engine true→false) | (1) | 3D | oneshot | 3 | громко | old petrol engine switched off, shudder, dies | ⬜ |
| van.engine.stall | заглох под нагрузкой (кашель, дёрг) | нет кода (модель не глохнет) | (1) | 3D | oneshot | 3 | громко | old van engine stalling, cough and lurch to silence | ⬜ |
| van.engine.misfire | «чих»: пропуск зажигания (зацепка ужаса ServerMisfire) | нет кода | (1) | 3D | oneshot | 4 | средне | engine single misfire hiccup, exhaust pop | ⬜ |
| van.engine.tick | остывающий металл тикает после остановки | нет кода | (1) | 3D | oneshot | 6 | нет | hot engine metal ticking as it cools in frost | ⬜ |
| van.engine.belt | визг ремня (холодный пуск, порванный ремень) | нет кода | (1) | 3D | oneshot/loop | 2 | средне | fan belt squealing on cold engine start | ⬜ |
| van.engine.boil | перегрев: бульканье, пар | нет кода (В5) | (1) | 3D | loop | 1 | тихо | overheated radiator boiling, steam hissing | ⬜ |
| **ФУРГОН — масса, ручник, раздатка, КПП, тормоз** |||||||||
| van.mass.on | ключ массы включить: поворот и контактор | ServerControl(MassSwitch_Key 0→1) | (1) | 3D | oneshot | 2 | нет | heavy battery isolator key turned, thick relay clunk | ⬜ |
| van.mass.off | ключ массы выключить | ServerControl(MassSwitch_Key 1→0) | (1) | 3D | oneshot | 2 | нет | battery isolator switched off, clunk | ⬜ |
| van.handbrake.pull | ручник затянуть, трещотка | VanVehicle.SetHandbrake(on) (Space или зона) | Handbrake (1) | 3D | oneshot | 3 | тихо | car handbrake lever pulled, ratchet clicks | ⬜ |
| van.handbrake.release | ручник отпустить (кнопка) | SetHandbrake(off) | (1) | 3D | oneshot | 3 | тихо | handbrake button pressed, lever released, clunk | ⬜ |
| van.transfer.axle.on | передний мост включить | ServerControl(TransferLever_Axle 0→1) | (1) | 3D | oneshot | 2 | тихо | heavy 4x4 transfer case lever engaged, gear clunk | ⬜ |
| van.transfer.axle.off | передний мост выключить | ServerControl(TransferLever_Axle 1→0) | (1) | 3D | oneshot | 2 | тихо | transfer lever disengaged, metal clack | ⬜ |
| van.transfer.range.low | ряд H→L | ServerControl(TransferLever_Range 0→1) | (1) | 3D | oneshot | 2 | тихо | transfer case shifted to low range, deep clunk | ⬜ |
| van.transfer.range.high | ряд L→H | ServerControl(TransferLever_Range 1→0) | (1) | 3D | oneshot | 2 | тихо | transfer case shifted to high range | ⬜ |
| van.transfer.grind | отказ на ходу: скрежет шестерён | VanMotion.TrySetRange (reason «не переключается на ходу») | (1) | 3D | oneshot | 2 | тихо | gears grinding, crunching gearbox | ⬜ |
| van.gear.shift.up | переключение вверх (автомат) | VanMotion.SetGear(Gear+1) → Telemetry.Gear | (1) | 3D | oneshot | 3 | тихо | old manual gearbox shift, clutch clunk, driveline knock | ⬜ |
| van.gear.shift.down | вниз и кик-даун | SetGear(Gear−1) | (1) | 3D | oneshot | 3 | тихо | gearbox downshift clunk | ⬜ |
| van.gear.reverse | включение R / выход из R (S или W держать 0,4 с) | SetGear(−1 / 1) | (1) | 3D | oneshot | 2 | тихо | reverse gear engaged, heavy clunk | ⬜ |
| van.gear.whine | вой КПП и раздатки на ходу (в L громче) | Telemetry.SpeedKmh, Gear, LowRange | (1) | 3D | loop | 2 (H / L) | средне | UAZ gearbox transmission whine rising with speed | ⬜ |
| van.brake.squeal | скрип барабанных тормозов при остановке | Telemetry.Brake | 4 колеса | 3D | oneshot | 3 | тихо | old drum brakes squealing at low speed stop | ⬜ |
| **ФУРГОН — сигнал, аварийка, печка, рация** |||||||||
| van.horn | сигнал (старт / петля / стоп; тише и ниже при слабой АКБ) | ServerPilotCommand(HornOn/HornOff, G) и Horn_Button (Momentary) → VanFlags.Horn | Audio_Horn (1) | 3D | loop | 2 (норма / слабая АКБ) | громко (дальше всех) | old Soviet car electric horn, harsh dual tone blast | ⬜ |
| van.hazard.switch | кнопка аварийки | ServerPilotCommand(AutopilotToggle, Z) | (1) | 3D | oneshot | 2 (вкл / выкл) | нет | car hazard switch push click | ⬜ |
| van.hazard.relay | реле: тик (вкл) / так (выкл), 90 в минуту | VanRig.UpdateFlasher → FlasherClicked (на обоих фронтах) | (1) | 3D | oneshot | 2 (тик / так) | нет | old car flasher relay tick tock | ⬜ |
| van.heater.fan | вентилятор печки, скорости 1–3 (высота и громкость по шагу и напряжению) | Pos(Heater_Fan) 1–3 и IgnitionOn (VanVehicle.LoadAmps) | Audio_Heater (1) | 3D | loop | 3 | тихо | car heater blower fan running, motor hum and air rush | ⬜ |
| van.heater.knob | переключатель вентилятора, щелчок 0→1→2→3→0 | ServerControl(Heater_Fan) | (1) | 3D | oneshot | 2 | нет | rotary heater switch detent click | ⬜ |
| van.radio.knob | ручка громкости рации, 10 шагов | ServerControl(Radio_Volume) | (1) | 3D | oneshot | 3 | нет | radio volume knob detent click | ⬜ |
| van.radio.on | включение рации (0→1): щелчок, хлопок динамика | ServerControl(Radio_Volume 0→1) + rig.radioPower | Audio_Radio (1) | 3D | oneshot | 2 | тихо | CB radio power switch click, speaker pop, static burst | ⬜ |
| van.radio.off | выключение (→0): щелчок, хвост шумодава | ServerControl(Radio_Volume →0) | (1) | 3D | oneshot | 2 | нет | CB radio switched off, squelch tail cut | ⬜ |
| van.radio.static | шипение эфира CB (кан. 9), громкость по ручке | rig.radioPower (Pos > 0 и BusVolts > 9) | Audio_Radio (1) | 3D | loop | 2 | тихо (на 9 — средне) | CB radio empty channel static hiss | ⬜ |
| **ФУРГОН — АКБ, провода, трамблёр, лебёдка, тумблеры** |||||||||
| van.battery.clamps.off | снять клеммы + и − | ServerControl(Battery_ClampPos 0→1) | 1 зона / 2 клеммы | 3D | oneshot | 2 | нет | battery terminal clamp loosened and pulled off post | ⬜ |
| van.battery.clamps.on | надеть клеммы, искра | ServerControl(Battery_ClampPos 1→0) | (1) | 3D | oneshot | 2 | нет | battery clamp pushed on terminal, small spark crackle | ⬜ |
| van.battery.out | вынуть АКБ 18 кг | VanVehicle.ServerBattery (out) | Battery (1) | 3D | oneshot | 2 | тихо | heavy car battery lifted out of wooden box, scrape | ⬜ |
| van.battery.in | поставить АКБ | ServerBattery (in) | (1) | 3D | oneshot | 2 | тихо | heavy battery set down into box, thud | ⬜ |
| van.leads.off | снять 4 провода со свечей | ServerControl(Engine_Lead_1 0→1) | 1 зона / 4 провода | 3D | oneshot | 2 | нет | rubber spark plug boots pulled off, four pops | ⬜ |
| van.leads.on | надеть провода | ServerControl(Engine_Lead_1 1→0) | (1) | 3D | oneshot | 2 | нет | spark plug leads pushed on, snap clicks | ⬜ |
| van.distributor.cap | крышка трамблёра снять / поставить (пружинные защёлки) | нет кода (зона Engine_DistributorCap есть, «пока не работает») | (1) | 3D | oneshot | 2 | нет | distributor cap spring clips unclipped, bakelite cap | ⬜ |
| van.winch.clutch.free | муфта лебёдки: свободный ход | ServerControl(Winch_Clutch 0→1) | (1) | 3D | oneshot | 2 | тихо | winch clutch lever thrown to freespool, metal clank | ⬜ |
| van.winch.clutch.engage | муфта включена | ServerControl(Winch_Clutch 1→0) | (1) | 3D | oneshot | 2 | тихо | winch clutch engaged, gear clunk | ⬜ |
| van.winch.unspool | трос разматывается (свободный ход) | нет кода | (1) | 3D | loop | 1 | тихо | steel winch cable unspooling, drum rattle | ⬜ |
| van.winch.motor | мотор лебёдки тянет (~170 А) | нет кода | (1) | 3D | loop | 2 (свободно / под нагрузкой) | громко | electric winch motor pulling cable under strain | ⬜ |
| van.winch.hook | крюк лебёдки снять / закрепить | нет кода (зона Winch_Hook есть) | (1) | 3D | oneshot | 2 | тихо | heavy steel winch hook clanking off bumper | ⬜ |
| van.toggle.on | тумблер включить | ServerControl(Toggle_* 0→1) | Toggle_Head, Fog, Spot, Roof, Work, Cabin, Stove, Wiper (8) | 3D | oneshot | 4 | нет | Soviet bakelite toggle switch flicked on, sharp snap | ⬜ |
| van.toggle.off | тумблер выключить | ServerControl(Toggle_* 1→0) | (8) | 3D | oneshot | 4 | нет | toggle switch flicked off, snap | ⬜ |
| **ФУРГОН — дворники, предохранители** |||||||||
| van.wiper.motor | мотор дворников (40 взмахов в минуту) | VanPresentation.Lights → rig.wiperSpeed (IgnitionOn и CircuitOn(Toggle_Wiper, Fuse_8)) | Audio_Wipers (1) | 3D | loop | 2 (норма / слабая АКБ) | тихо | old car wiper motor whirring with linkage clunk | ⬜ |
| van.wiper.sweep | щётка по стеклу: скрип по сухому или инею, шорох по снегу | VanRig.UpdateWipers (_wiperPhase в крайних точках) | лобовое (2 щётки) | 3D | oneshot | 3 покрытия × 3 | нет | rubber wiper blade squeak across frosty windshield | ⬜ |
| van.wiper.park | дворники дошли до упора и встали | UpdateWipers (_wiperParking → 0) | (1) | 3D | oneshot | 2 | нет | wipers parking, motor stop clunk | ⬜ |
| van.fuse.out | вынуть предохранитель | VanVehicle.ServerFuse (out / blown_out) | Fuse_1–8 (8) | 3D | oneshot | 3 | нет | glass fuse pulled from spring clips, tiny metal click | ⬜ |
| van.fuse.in | вставить предохранитель | ServerFuse (in) | (8) | 3D | oneshot | 3 | нет | fuse pushed into clips, snap | ⬜ |
| van.fuse.throw | выбросить сгоревший | ServerFuse (s == FuseBlown) | (8) | 3D | oneshot | 2 | нет | tiny glass fuse flicked away, light tinkle | ⬜ |
| van.fuse.spare | взять / положить запасной в жестянку | VanVehicle.ServerSpareFuse | Spare_Fuse_1–3 (3) | 3D | oneshot | 2 | нет | small fuses rattling in tin box | ⬜ |
| van.fuse.blow | предохранитель сгорает | нет кода (сгоревший — только на старте леса) | (8) | 3D | oneshot | 2 | нет | car fuse blowing, small pop and fizz | ⬜ |
| **ФУРГОН — шины, подвеска, кузов, ветер** |||||||||
| van.tyre.packedsnow | шины по утоптанному снегу (по скорости) | VanVehicle.FixedUpdate Grip(SurfaceKind.PackedSnow) — весь лес по умолчанию | Audio_Tyres, 4 колеса | 3D | loop | 3 скорости | средне | car tyres rolling on packed snow, squeaky crunch | ⬜ |
| van.tyre.road | дорога (Road) | Grip(Road) | 4 | 3D | loop | 3 | средне | tyres on frozen road, low rumble | ⬜ |
| van.tyre.loosesnow | рыхлый снег | Grip(LooseSnow) | 4 | 3D | loop | 3 | средне | tyres ploughing deep powder snow, whoosh | ⬜ |
| van.tyre.ice | лёд | Grip(Ice) | 4 | 3D | loop | 3 | тихо | tyres on smooth ice, faint hiss | ⬜ |
| van.tyre.sandtrack | колея, посыпанная песком | Grip(SandTrack) | 4 | 3D | loop | 3 | средне | tyres on sanded icy track, gritty crunch | ⬜ |
| van.tyre.spin | пробуксовка | VanWheel.Sliding (только сервер) | 4 | 3D | loop | 3 (снег / лёд / песок) | средне | car wheels spinning in snow, engine strain, spray | ⬜ |
| van.tyre.skid | юз заблокированных колёс | VanWheel.Locked (только сервер) | 4 | 3D | loop | 2 (снег / лёд) | средне | locked tyres skidding on snow, scrape | ⬜ |
| van.tyre.chains | цепи на колёсах звенят | нет кода (ChainBag — только предмет) | 4 | 3D | loop | 2 | средне | snow chains clanking on rolling tyres | ⬜ |
| van.suspension.bump | удар подвески на кочке | VanTelemetry S0–S3 (скорость хода) → VanPresentation | 4 колеса | 3D | oneshot | 6 | тихо | old leaf spring suspension thump over bump | ⬜ |
| van.suspension.bottom | пробой до отбойника | S0–S3 у максимума | 4 | 3D | oneshot | 3 | средне | suspension bottoming out, hard metal bang | ⬜ |
| van.suspension.creak | скрип рессор при раскачке и посадке | S0–S3, смена числа сидящих | 4 | 3D | oneshot | 4 | нет | leaf springs creaking as van rocks | ⬜ |
| van.body.rattle | дребезг кузова и вещей на неровностях | Telemetry.SpeedKmh + S0–S3 | салон | 3D | loop | 2 | тихо | old van body panels and loose tools rattling on rough road | ⬜ |
| van.body.creak.cold | кузов потрескивает на морозе на стоянке | нет кода | кузов | 3D | oneshot | 6 | нет | cold steel van body creaking and ticking in frost | ⬜ |
| van.wind.drive | ветер по кузову на ходу (в кабине) | Telemetry.SpeedKmh | кабина | 2D (в машине) | loop | 2 | нет | wind buffeting boxy van at speed, interior | ⬜ |
| van.wind.seals | свист ветра в уплотнителях на стоянке, порывы | нет кода (погода) | двери | 3D | loop | 2 | нет | wind whistling through old van door seals | ⬜ |
| van.snow.slide | снег сползает с крыши | нет кода (rig.SetSnow) | крыша | 3D | oneshot | 3 | тихо | slab of snow sliding off car roof, soft thump | ⬜ |
| van.knock | стук или скрежет по кузову снаружи (хоррор) | нет кода (сокеты Knock_*) | 19 точек Knock_* | 3D | oneshot | 6 (стук / скрежет) | средне | something knocking on thin steel van wall from outside | ⬜ |
| **ФУРГОН — колёса, домкрат (зоны есть, «пока не работает»)** |||||||||
| van.wheel.nut | гайка колеса: открутить / закрутить баллонником | нет кода | 24 гайки + барашек запаски | 3D | oneshot | 3 | тихо | wheel brace loosening lug nut, metal crack and spin | ⬜ |
| van.wheel.offon | снять / поставить колесо | нет кода | Wheel_FL/FR/RL/RR, SpareWheel (5) | 3D | oneshot | 2 | тихо | heavy car wheel pulled off hub, rubber thud on snow | ⬜ |
| van.jack.place | поставить домкрат под мост | нет кода | Jack_Point_* (4) | 3D | oneshot | 2 | тихо | steel jack set under axle, clank | ⬜ |
| van.jack.pump | качать домкрат / винт штока | нет кода | Jack_Screw (1) | 3D | loop | 2 (вверх / вниз) | тихо | hydraulic bottle jack pumped, van creaks rising | ⬜ |
| **САМОЛЁТ — тумблеры, кнопки, ручки** |||||||||
| plane.switch.on | тумблер включить | PlaneVehicle.ServerUse → Toggles / SW_CargoDoor / SW_CabinDoor → SetSwitches | SW_Battery, Avionics, FuelPump, Ignition, Beacon, Strobe, NavLights, Landing, CabinLight, CargoLight, CargoDoor_L/R, CabinDoor_L/R (14) | 3D | oneshot | 4 | нет | aircraft cockpit toggle switch flicked on, crisp snap | ⬜ |
| plane.switch.off | тумблер выключить | то же | (14) | 3D | oneshot | 4 | нет | aircraft toggle switch flicked off | ⬜ |
| plane.battery.relay.on | контактор АКБ, шина под напряжением | ElectricalSystem.BusPowered false→true | (1) | 3D | oneshot | 2 | нет | aircraft battery master contactor clunk, electric hum rises | ⬜ |
| plane.battery.relay.off | контактор отпал | BusPowered true→false | (1) | 3D | oneshot | 2 | нет | battery relay drops out, hum dies | ⬜ |
| plane.generator.online | генератор в сети, щелчок реле | Electrics.GeneratorOnline false→true | (1) | 3D | oneshot | 1 | нет | generator relay click, electrical tone shifts | ⬜ |
| plane.annun.test | кнопка теста табло нажать / отпустить | ServerUse(Btn_AnnunTest, Press / Release) | (1) | 3D | oneshot | 2 | нет | small press-to-test button click | ⬜ |
| plane.starter.switch | кнопка стартера нажать / отпустить (пружина) | ServerUse(SW_Starter, Press / Release) | (1) | 3D | oneshot | 2 | нет | spring loaded starter switch held and released | ⬜ |
| plane.rheo.step | реостат шаг 25 %, с упора — щелчок в выкл | PlaneVehicle.ServerKnob(Rheo_Panel / Rheo_Flood) | 2 | 3D | oneshot | 3 | нет | rheostat knob turned, soft friction, off click | ⬜ |
| plane.knob.radio | радио-ручки: щелчок через 15° | ServerKnob(RadioKnobNames) | 13 (Audio_Vol, Com/Nav/GPS/ADF Outer+Inner, NavCom_Vol, GPS_Pwr, AP_Baro, ADF_Vol) | 3D | oneshot | 3 (большая / малая / громкость) | нет | avionics concentric knob detent click | ⬜ |
| plane.knob.instrument | ручки приборов, гладкие, шорох | ServerKnob(Knob_ALT_Baro / HDG_Bug / DG_Set) | 3 | 3D | oneshot | 2 | нет | instrument setting knob turned, smooth friction | ⬜ |
| plane.knob.xpdr | переключатель ответчика OFF/SBY/ON/ALT/TST | ServerKnob(Knob_XPDR_Mode) | 1 | 3D | oneshot | 2 | нет | rotary mode selector chunky detent click | ⬜ |
| **САМОЛЁТ — рычаги, закрылки, триммер, тяги, краны** |||||||||
| plane.lever.power | РУД двигают (Shift / Ctrl), трение | изменение Controls.Throttle | Lever_Power (1) | 3D | loop | 1 | нет | throttle lever sliding in quadrant, friction | ⬜ |
| plane.lever.prop | рычаг шага, 3 положения | ServerUse(Lever_Prop) | 1 | 3D | oneshot | 2 | нет | propeller lever moved, quadrant click | ⬜ |
| plane.lever.condition.cutoff | рычаг останова в ОСТАНОВ (через фиксатор) | Toggles(Lever_Condition) on | 1 | 3D | oneshot | 2 | нет | condition lever lifted over gate to cutoff, clunk | ⬜ |
| plane.lever.condition.idle | рычаг останова в МАЛЫЙ ГАЗ | Toggles(Lever_Condition) off | 1 | 3D | oneshot | 2 | нет | condition lever pushed to idle detent | ⬜ |
| plane.lever.flap | рычаг закрылков, фиксатор 0/10/20/35 | ServerUse(Lever_Flap) | 1 | 3D | oneshot | 2 | нет | flap lever into detent, ratchet click | ⬜ |
| plane.flaps.motor | мотор закрылков (старт / петля / стоп, 35° за 10 с) | PlaneVehicle.FixedUpdate flapsMoving | корни крыльев | 3D | loop | 1 | тихо | aircraft electric flap motor whining, jackscrew | ⬜ |
| plane.flaps.stop | закрылки встали | flapsMoving true→false | 2 | 3D | oneshot | 2 | нет | flap motor stops, mechanical clunk | ⬜ |
| plane.trim.wheel | колесо триммера крутят ([ ]) | изменение Controls.Trim | TrimWheel (1) | 3D | loop | 1 | нет | trim wheel turning, ratcheting clicks | ⬜ |
| plane.trim.neutral | клик в нейтраль | ServerUse(TrimWheel) → ServerSetTrim(0) | 1 | 3D | oneshot | 1 | нет | trim wheel spun to neutral, click | ⬜ |
| plane.pull.out | тягу вытянуть | Toggles(Pull_*) on | Pull_CabinHeat, Defrost, ParkBrake, FuelShutoff (4) | 3D | oneshot | 3 (стояночный тормоз — с трещоткой) | нет | cockpit push-pull knob pulled out, cable scrape | ⬜ |
| plane.pull.in | тягу утопить | Toggles(Pull_*) off | (4) | 3D | oneshot | 3 | нет | push-pull control pushed in, thunk | ⬜ |
| plane.fuelsel.open | кран бака открыть (поворот 60°) | Toggles(FuelSel_L / R) on | 2 | 3D | oneshot | 2 | нет | fuel selector valve turned, heavy detent clack | ⬜ |
| plane.fuelsel.close | кран бака закрыть | Toggles(FuelSel_L / R) off | 2 | 3D | oneshot | 2 | нет | fuel valve turned off, clack | ⬜ |
| **САМОЛЁТ — двери, замок** |||||||||
| plane.door.actuator | электропривод двери (кабина 2,5 с, грузовая 4,5 с) | NetworkDoor.ActuatorRunning (Command(open, Powered) из SW_*Door) | 4 двери | 3D | loop | 2 (кабина / грузовая) | тихо | electric door actuator motor whirring, gears | ⬜ |
| plane.door.cabin.open | дверь кабины ручкой открыть | ServerUse(Socket_Handle_PilotDoor_*_In / Out) → Toggle(Manual) | 2 двери / 4 ручки | 3D | oneshot | 3 | тихо | light aircraft door handle, latch pops, door swings | ⬜ |
| plane.door.cabin.close | дверь кабины закрыть | NetworkDoor → Closed | 2 | 3D | oneshot | 3 | средне | light aircraft door closed, thin aluminium thunk, latch | ⬜ |
| plane.door.cargo.open | грузовая дверь открыть | ServerUse(Socket_Handle_CargoDoor_*) | 2 двери / 4 ручки | 3D | oneshot | 3 | тихо | aircraft cargo door unlatched, swings open | ⬜ |
| plane.door.cargo.close | грузовая дверь закрыть | NetworkDoor → Closed | 2 | 3D | oneshot | 3 | средне | cargo door slammed shut, aluminium boom, latch | ⬜ |
| plane.door.endstop | дверь дошла до упора открытой | NetworkDoor Opening→Open | 4 | 3D | oneshot | 2 | тихо | door hits open stop, check strap thud | ⬜ |
| plane.door.blocked | дверь упёрлась | DoorState.Blocked | 4 | 3D | oneshot | 2 | тихо | door bumping obstacle, dull thud | ⬜ |
| plane.door.locked | заперто: ручку дёргают снаружи | ServerUse(Socket_Handle_*_Out) при Locked (locked_refuse) | 4 ручки | 3D | oneshot | 3 | тихо | locked car door handle rattled, won't open | ⬜ |
| plane.lock.lock | ключ: запереть (центральный замок, все двери) | PlaneVehicle.ServerLock (Locked on) | Socket_Keyhole_PilotDoor_L / R (2) | 3D | oneshot | 2 | тихо | key turned in lock, central locking solenoids thunk | ⬜ |
| plane.lock.unlock | ключ: отпереть | ServerLock (Locked off) | 2 | 3D | oneshot | 2 | тихо | key unlocks door, solenoid clunk | ⬜ |
| plane.door.warning | сигнал открытой двери в полёте | нет кода (док §7.1) | кабина | 3D | loop | 1 | нет | cockpit warning chime repeating | ⬜ |
| **САМОЛЁТ — ремни, козырьки, бардачок, кресла** |||||||||
| plane.belt.buckle | ремень застегнуть | Toggles(Socket_Belt_P / C) on | 2 | 3D | oneshot | 3 | нет | aircraft seat belt buckle latched, metal click | ⬜ |
| plane.belt.unbuckle | ремень расстегнуть | Toggles(Socket_Belt_*) off | 2 | 3D | oneshot | 3 | нет | seat belt lift-latch released, webbing slide | ⬜ |
| plane.visor.down | козырёк опустить | Toggles(Visor_P / C) on | 2 | 3D | oneshot | 2 | нет | sun visor flipped down, friction hinge | ⬜ |
| plane.visor.up | козырёк поднять | Toggles(Visor_*) off | 2 | 3D | oneshot | 2 | нет | sun visor pushed up, soft tap | ⬜ |
| plane.glovebox.open | бардачок открыть | Toggles(Glovebox_Lid) on | 1 | 3D | oneshot | 2 | нет | small cockpit map case lid opens | ⬜ |
| plane.glovebox.close | бардачок закрыть | Toggles(Glovebox_Lid) off | 1 | 3D | oneshot | 2 | нет | map case lid snapped shut | ⬜ |
| plane.paxseat.down | откидное кресло опустить | PlaneVehicle.ServerPaxFold (down) | PaxSeatFold_L1, R1, L2, R2 (4) | 3D | oneshot | 2 | тихо | aluminium troop seat unfolded, legs clatter | ⬜ |
| plane.paxseat.fold | откидное кресло сложить | ServerPaxFold (up) | 4 | 3D | oneshot | 2 | тихо | troop seat folded against wall, clank | ⬜ |
| plane.seat.sit | сесть / встать | SeatedVehicle.ServerSit / ServerExit | Seat_P, Seat_C, PaxSeat × 4 (6) | 3D | oneshot | 3 | нет | pilot seat creak, leather and frame as someone sits | ⬜ |
| **САМОЛЁТ — топливо** |||||||||
| plane.fuelcap.open | крышка бака на крыле открыть | Toggles(Socket_FuelCap_L / R) on | 2 | 3D | oneshot | 2 | нет | aircraft wing fuel cap twisted open, vent hiss | ⬜ |
| plane.fuelcap.close | крышка бака закрыть | Toggles(Socket_FuelCap_*) off | 2 | 3D | oneshot | 2 | нет | fuel cap locked, click | ⬜ |
| plane.fuel.pour | заливка из канистры в бак (держать, со стремянки) | PlaneVehicle.ServerStartPour / Pour (_pours) | Socket_FuelCap_L / R (2) | 3D | loop | 2 | тихо | jet fuel pouring from jerrycan into wing tank, glugging | ⬜ (похоже на ✅ forest.fuel.pour) |
| plane.fuel.pour.end | канистра опустела, последние бульки | Pour (FuelLitres ≤ 0 → pour_stop) | 2 | 3D | oneshot | 2 | нет | last gurgles from empty jerrycan | ⬜ |
| plane.barrel.fill | наполнить канистру из бочки | FuelBarrel.ServerUse | FuelBarrel_Net (1) | 3D | oneshot | 2 | тихо | fuel drum hand pump filling jerrycan, gurgle rising | ⬜ |
| plane.jerrycan.slosh | плеск в канистре при переноске (по литрам) | KitCarry.FuelLitres + движение | канистры KitCarry (2 в лоадауте) | 3D | oneshot | 3 (полная / половина / почти пустая) | нет | liquid sloshing inside metal jerrycan | ⬜ |
| plane.tank.slosh | топливо плещется в баках крыла | нет кода | Socket_Audio_FuelTank_L / R (2) | 3D | oneshot | 4 | нет | fuel sloshing inside aircraft wing tank | ⬜ |
| **САМОЛЁТ — автопилот, сирена, электрика, радио** |||||||||
| plane.ap.engage | автопилот включён | ServerPilotCommand → Autopilot.Engage; Telemetry.ApEngaged false→true | кабина | 3D | oneshot | 1 | нет | autopilot engage soft chime, servo clunk | ⬜ |
| plane.ap.disconnect | сигнал отключения (5 причин) | Autopilot.Disengage; ApEngaged true→false | кабина | 3D | oneshot | 1 | нет | autopilot disconnect warning tone, wailer | ⬜ |
| plane.ap.servo | сервоприводы подрабатывают | нет кода (есть apPitch / apRoll) | кабина | 3D | loop | 1 | нет | faint servo motor whir pulses | ⬜ |
| plane.stall.horn | сирена сваливания | PlanePresentation.Awake / Update (_horn loop по t.StallWarning) | cabinAudio (Socket_Audio_Cabin), в данных — Socket_Audio_StallHorn | 3D | loop | 1 | нет | light aircraft stall warning horn, steady reed tone | 🔇 |
| plane.stall.buffet | тряска на срыве | нет кода (WingAttached) | планер | 2D | loop | 1 | нет | airframe buffeting rumble before stall | ⬜ |
| plane.fuelpump | подкачивающий насос жужжит | AircraftSystems.PumpRunning | (1) | 3D | loop | 1 | нет | aircraft electric fuel boost pump buzzing | ⬜ |
| plane.avionics.hum | гул авионики, писк при включении | Telemetry.Avionics > 0 | панель | 3D | loop | 1 | нет | cockpit avionics electric hum, faint whine | ⬜ |
| plane.gyro.spin | авиагоризонт: раскрутка и выбег | FlightInstruments._spin (по шине) | панель | 3D | loop | 1 | нет | electric gyro spinning up, rising whine | ⬜ |
| plane.strobe.whine | блок стробов: нарастающий писк, щелчок вспышки | Telemetry.Strobe > 0 | (1) | 3D | loop | 1 | нет | strobe power supply capacitor whine and tick | ⬜ |
| plane.blower | вентилятор обогрева или обдува | s.On(CabinHeat / Defrost) и шина | (1) | 3D | loop | 2 (обогрев / обдув / оба) | нет | cabin heater blower fan airflow hum | ⬜ |
| plane.radio.static | шипение динамика радио | нет кода (ручки только крутятся) | Socket_Audio_Radio | 3D | loop | 2 | тихо | aircraft radio speaker static, squelch breaks | ⬜ |
| plane.headset.hiss | шипение в висящих гарнитурах | нет кода | Socket_Audio_Headset_P / C (2) | 3D | loop | 1 | нет | faint aviation headset hiss | ⬜ |
| plane.elt | ELT в хвосте: качающийся тон | нет кода | Socket_Audio_ELT (1) | 3D | loop | 1 | нет | emergency locator beacon sweeping tone, muffled | ⬜ |
| **САМОЛЁТ — двигатель (турбовинт)** |||||||||
| plane.starter.whine | стартер-генератор крутит Ng до 20 % (старт / петля / стоп, скорость по напряжению) | TurbopropEngine.Step StarterTurning | Socket_Audio_Engine | 3D | loop | 2 (норма / слабая АКБ) | громко | turboprop electric starter whine spooling up slowly | ⬜ |
| plane.igniter.tick | треск свечей зажигания при запуске | TurbopropEngine (Ignition и StarterTurning, ещё не Combusting) | (1) | 3D | loop | 1 | средне | turbine igniter exciter snapping ticks | ⬜ |
| plane.engine.lightoff | розжиг: хлопок пламени, рост гула | TurbopropEngine Combusting false→true | (1) | 3D | oneshot | 2 | громко | turboprop light-off, whoomp, rising rumble | ⬜ |
| plane.engine.spoolup | раскрутка до малого газа (Ng 52 %) | Combusting и Ng < idleNg | (1) | 3D | oneshot | 2 | громко | turbine engine spooling up to idle, rising whine | ⬜ |
| plane.engine.idle | малый газ: свист турбины | Combusting, Ng ≈ 52 % | (1) | 3D | loop | внутри / снаружи | громко | PT6 turboprop idling, high whine and roar | ⬜ |
| plane.engine.run | работа под газом (Ng 52–101 %) | Ng, Controls.Throttle | (1) | 3D | loop | 3 слоя | громко | turboprop at high power, compressor howl | ⬜ |
| plane.prop | винт: гул лопастей по PropRpm (1600–1900), «чоп» на малых | Telemetry.PropRpm | диск винта | 3D | loop | 2 (малые / полные) | громко | three blade propeller thrum, buzzing blades | ⬜ |
| plane.prop.windmill | авторотация или выбег винта | PropRpm при !Combusting | (1) | 3D | loop | 1 | средне | propeller windmilling, swooshing blades | ⬜ |
| plane.engine.shutdown | останов: выбег турбины ~6 с, винт тормозится | Combusting true→false (Lever_Condition cut-off) | (1) | 3D | oneshot | 2 | громко | turboprop shutdown, whine winding down, prop slowing | ⬜ |
| plane.engine.flameout | срыв пламени без топлива | Combusting → false по топливу (FuelSystem.Supply) | (1) | 3D | oneshot | 2 | громко | turbine flameout, sudden power loss, spooling down | ⬜ |
| plane.engine.hungstart | зависший запуск: стартер отпущен ниже малого газа | Combusting, Ng < idle, !StarterTurning | (1) | 3D | oneshot | 1 | громко | turbine hung start, whine sagging, rumble falling | ⬜ |
| plane.engine.hot | горячий запуск (ITT выше предела) | TurbopropEngine.Hot | (1) | 3D | loop | 1 | громко | turbine running hot, harsh roar, tailpipe torching | ⬜ |
| plane.engine.tick | остывающий двигатель тикает | нет кода | (1) | 3D | oneshot | 4 | нет | hot turbine metal ticking as it cools | ⬜ |
| **САМОЛЁТ — шасси, планер, ветер** |||||||||
| plane.touchdown.main | касание основным колесом (по вертикальной скорости) | Telemetry.GearL / GearR растёт от 0 (AircraftPhysics.Last) | Socket_Audio_Gear_L / R (2) | 3D | oneshot | 3 (мягко / средне / жёстко) | средне | aircraft main wheel touchdown on snowy strip, thump | ⬜ |
| plane.touchdown.tail | касание хвостовым колесом | Telemetry.GearT | Socket_Audio_Tail | 3D | oneshot | 2 | тихо | tailwheel touching down, light bump | ⬜ |
| plane.gear.bottom | пробой амортизатора | сжатие GearL / R / T ≈ 1 | 3 | 3D | oneshot | 2 | средне | landing gear strut bottoming out, metal bang | ⬜ |
| plane.gear.roll | качение и дребезг шасси (по скорости) | Telemetry.WheelMain / WheelTail | 3 колеса | 3D | loop | 2 (снег / полоса) | средне | aircraft wheels rolling on snow, gear rattle | ⬜ |
| plane.gear.skid | юз колеса | WheelContact.Sliding (только сервер) | 3 | 3D | loop | 1 | тихо | aircraft tyre skidding on ice | ⬜ |
| plane.brake | тормоза (Space, стояночный) | Controls.Brake / ParkBrake | 2 | 3D | loop | 1 | тихо | aircraft brakes squealing, groaning disc | ⬜ |
| plane.wind.airframe | обтекание фюзеляжа (в кабине) | Telemetry.TrueAirspeed | кабина | 2D (в кабине) | loop | 2 | нет | wind roar over light aircraft fuselage, interior | ⬜ |
| plane.wind.doorseal | свист уплотнителей дверей | Airspeed / ветер | Socket_Audio_Wind_L / R (2) | 3D | loop | 1 | нет | high whistle from leaking door seal | ⬜ |
| plane.airframe.creak | скрип обшивки (полёт, болтанка, стоянка на морозе) | нет кода | Socket_Audio_Bay | 3D | oneshot | 6 | нет | aluminium airframe creaking and popping | ⬜ |
| plane.turbulence | дребезг в болтанке | EnvironmentProvider TurbulenceMs | кабина | 2D | loop | 1 | нет | light aircraft rattling in turbulence | ⬜ |
| **САМОЛЁТ — зимний комплект, стремянка, хранение** |||||||||
| plane.kit.intake.on | заглушку воздухозаборника вставить (4 с) | PlaneWinterKitNet.ServerUse → Complete (kit_install) | IntakePlug (1) | 3D | oneshot | 2 | тихо | foam plug pushed into engine intake, streamer flutter | ⬜ |
| plane.kit.intake.off | вынуть заглушку | Complete (kit_remove) | (1) | 3D | oneshot | 2 | тихо | intake plug pulled out, squeak | ⬜ |
| plane.kit.exhaust.on | заглушку выхлопа надеть (3 с) | то же | ExhaustCover_L / R (2) | 3D | oneshot | 2 | тихо | cover slipped over exhaust stack, metal scrape | ⬜ |
| plane.kit.exhaust.off | снять заглушку выхлопа | то же | (2) | 3D | oneshot | 2 | тихо | exhaust cover pulled off | ⬜ |
| plane.kit.pitot.on | чехол ПВД надеть (3 с) | то же | PitotCover (1) | 3D | oneshot | 2 | нет | small fabric pitot sleeve slipped on, velcro | ⬜ |
| plane.kit.pitot.off | снять чехол ПВД | то же | (1) | 3D | oneshot | 2 | нет | pitot cover pulled off, ribbon flutter | ⬜ |
| plane.kit.wingcover.on | чехол крыла натянуть (40 с) | ServerUse (hold) … Complete; петля до конца или отмены (kit_cancel) | WingCover_L / R (2) | 3D | loop | 2 | тихо | large nylon wing cover unrolled and strapped, flapping fabric | ⬜ |
| plane.kit.wingcover.off | снять чехол крыла | то же | (2) | 3D | loop | 2 | тихо | wing cover pulled off, snow sliding, fabric rolled | ⬜ |
| plane.kit.windshield.on | чехол лобового стекла (20 с) | то же | WindshieldCover (1) | 3D | loop | 2 | тихо | windshield cover stretched over cockpit, straps | ⬜ |
| plane.kit.windshield.off | снять чехол стекла | то же | (1) | 3D | loop | 2 | тихо | windshield cover peeled off, nylon rustle | ⬜ |
| plane.kit.enginecover.on | утеплённый чехол двигателя (30 с) | то же | EngineCover (1) | 3D | loop | 2 | тихо | quilted engine cover wrapped, velcro and zip | ⬜ |
| plane.kit.enginecover.off | снять чехол двигателя | то же | (1) | 3D | loop | 2 | тихо | insulated cover unzipped and pulled off | ⬜ |
| plane.kit.chocks.on | колодки под колесо (4 с) | то же | Chocks_L / R / Tail (3) | 3D | oneshot | 2 (основные / хвост) | тихо | wheel chocks kicked against tyre in snow | ⬜ |
| plane.kit.chocks.off | убрать колодки | то же | (3) | 3D | oneshot | 2 | тихо | chocks yanked out of snow by rope | ⬜ |
| plane.kit.tiedown.on | швартовка: вкрутить анкер, затянуть трос (20 / 15 с) | то же | TieDown_L / R / Tail (3) | 3D | loop | 2 | тихо | screw anchor twisted into frozen ground, rope cinched | ⬜ |
| plane.kit.tiedown.off | снять швартовку | то же | (3) | 3D | loop | 2 | тихо | rope untied, anchor unscrewed from ice | ⬜ |
| plane.kit.heater.place | подогреватель 26 кг поставить, рукав в воздухозаборник (25 с) | то же (Heater install) | Heater (1) | 3D | loop | 1 | тихо | heavy heater set down in snow, duct unrolled | ⬜ |
| plane.kit.heater.remove | убрать подогреватель | то же (Heater remove) | (1) | 3D | loop | 1 | тихо | heater duct pulled out, coiled, dragged | ⬜ |
| plane.heater.run | подогреватель работает (старт / петля / стоп) | UtilityPlaneWinterKit.Preheating (стоит = работает) | (1) | 3D | loop | 1 | громко | forced-air combustion preheater roaring, blower drone | ⬜ |
| plane.tiedownbag.set | сумка швартовки: взять или вернуть комплект | KitCarry.ServerSetSets | сумка (1, 3 комплекта) | 3D | oneshot | 2 | нет | canvas bag of ropes and metal anchors rummaged | ⬜ |
| plane.ladder.unfold | стремянку поставить, задние ноги разложить | StepLadderItem.Update (не Carried / Stowed) | StepLadder (1) | 3D | oneshot | 2 | тихо | aluminium step ladder legs unfolded, clack | ⬜ |
| plane.ladder.fold | стремянку сложить (взяли или уложили) | StepLadderItem.Update (Carried / Stowed) | (1) | 3D | oneshot | 2 | тихо | step ladder folded shut, rattle | ⬜ |
| plane.ladder.creak | стремянка скрипит под весом | нет кода (PlaneVehicle.OnLadder) | (1) | 3D | oneshot | 4 | нет | aluminium ladder creak under weight | ⬜ |
| plane.stow.clip | в держатель: фонарь, ракетница (взять — то же наоборот) | PlaneVehicle.ServerStowUse | Stow_Flashlight, Stow_FlareGun (2) | 3D | oneshot | 2 | нет | item snapped into spring clip holder | ⬜ |
| plane.stow.strap | под ремень: сумка, канистра, стремянка | ServerStowUse | Stow_SurvivalBag, Stow_JerryCan, Stow_Ladder (3) | 3D | oneshot | 3 | нет | cargo strap buckled tight over item | ⬜ |
| plane.stow.shovel | лопата в зажимы | ServerStowUse | Stow_Shovel (1) | 3D | oneshot | 2 | нет | shovel pushed into metal wall clamps | ⬜ |

- **Где брать события.** Для фургона все органы проходят через `VanVehicle.ServerControl`; переход позиции виден на каждом пире в списке позиций, который читает `VanPresentation`. Самолёт: `PlaneVehicle.SetSwitches`; изменение битов видно в `PlanePresentation` (`_shownBits`). Двери: `NetworkDoor.FixedUpdate` (событие `door_state`).
- **Чего нет в телеметрии.** Пробуксовка и юз колёс (`VanWheel.Sliding`, `Locked`) и тип покрытия под колесом считаются только на сервере, в `VanTelemetry` их нет. У самолёта не реплицируются `StarterTurning` и юз колеса. Табличные `NgPct` и `IttC` обнуляются без питания шины, поэтому звук двигателя нельзя строить по ним. Нужны новые поля телеметрии.
- **Холодный пуск фургона не сделан.** `VanVehicle.EngineSwitches` помечен TEMPORARY: ключ в III сразу заводит. Прокрутка по напряжению, щелчки реле, «3 попытки», подсос, «залило», рукоятка, толкач, перегрев и ремень есть только в `VAN3_INTEGRATION_UNITY.md` В5. Отдельного звука «залило» не заводил: это та же прокрутка без вспышек. Двигатель в коде сам не глохнет.
- **Цифры в задании расходятся с кодом.** Тумблеров у самолёта 14, а не 13: 10 систем и 4 двери. Плюс 2 ремня тоже сделаны как тумблеры. У вентилятора печки фургона 3 скорости и «выкл», а не 4.
- **Непонятно, что делает тумблер `Toggle_Stove`** («печка (вентилятор)»). Ток вентилятора идёт по `Heater_Fan`, а не по нему. Возможно, его звук — вентилятор печки салона (буржуйки).
- **Отключённые зоны фургона.** Зоны замков `Lock_*` (5), стёкол, люка, буржуйки, канала, шумодава и тангенты рации, подсоса, ручного газа, паяльной лампы и заводной рукоятки отключены (`VanPrefabBuilder.Keep`). Их звуков в таблице нет. Поворотники убраны решением пользователя 21.10.
- **Покрытия.** Покрытий для шин в коде 5 (`SurfaceKind`), но `VanSurface` стоит только на стенде (`VanBenchBuilder`). В лесу всё считается утоптанным снегом. Колёса, домкрат, крюк лебёдки, канистры и трамблёр включены как зоны, но отвечают «пока не работает».
- **Грузовые двери самолёта.** В коде они пока на петле (`PlaneBenchBuilder`, ±100°). `PLANE_V13_UPGRADE_UNITY.md` делает их сдвижными — тогда звук `plane.door.cargo.*` будет другим: ролики по направляющей.
- **Что можно взять готовым.** Существующий `forest.fuel.pour` подходит для заливки фургона и самолёта. Для падения канистры и ключа есть `item.impact.metal`, для шагов по стремянке — `step.metal.*`.
- **Ручки и сирена.** Радио-ручки самолёта только крутятся: радиосистем ещё нет, поэтому звуки эфира, гарнитур и ELT пока не к чему подключить. Подогреватель в коде греет, пока установлен (`Preheating` = установлен), отдельного включения нет. Сирена сваливания создаётся на `cabinAudio`, а не на `Socket_Audio_StallHorn` из `horror.json`.

---

## Звуки: персонаж, предметы, ручная рация, интерфейс, музыка

Статус: ✅ — id есть в библиотеке и в `audio_manifest.json`; 🔇 — код просит id или клип, файла нет; ⬜ — нет ни кода, ни файла. «Шум» взят из радиусов `NoiseEvents`: тихо = 5 м, средне = 15 м, громко = 30 м, нет = существа не слышат. Где написано «предлож.» — предлагаю я, в коде этого нет.

#### 1. Шаги по поверхностям
Сейчас `SoundLibrary.steps` знает 11 поверхностей: Forest, Snow, Frozen, Gravel, Wood, Tile, Metal, Dirt, Grass, Leaves, Mud. Слои террейна: TL_ForestFloor и TL_ForestFloorDark → Forest; TL_TrailSoil и TL_TrailTrack → Dirt; TL_RoadRuts → Mud; TL_RoadGravel и TL_Rock → Gravel; TL_Asphalt → Tile. Под открытым небом без совпадения — Forest, под крышей — Tile. В присяде шаг играет звук walk на громкости 0,4, отдельного звука нет. Звука толчка при прыжке в коде нет совсем.

| id | what | when | where | 3D/2D | oneshot/loop | variants | шум | prompt | status |
|---|---|---|---|---|---|---|---|---|---|
| step.forest.walk | шаг по лесной подстилке | Footsteps.Step | TL_ForestFloor, TL_ForestFloorDark, outdoorDefault | 3D сеть | oneshot | 8–10 | тихо | boot step on frozen forest floor, pine needles, twigs, earth | ✅ (собран из leaves/dirt/grass) |
| step.forest.run | бег по подстилке | Footsteps.Step (run) | там же | 3D сеть | oneshot | 8 | средне | running boots on frozen forest floor, needles crunch | ✅ (= dirt.run) |
| step.forest.crouch | шаг в присяде по подстилке | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4–6 | нет | slow careful soft step on forest needles, faint crackle | ⬜ (сейчас walk ×0.4) |
| step.forest.jump | толчок прыжка по подстилке | нет кода (HLCharacterMotor jump) | там же | 3D сеть | oneshot | 3 | тихо | push-off scuff of boots on forest floor | ⬜ |
| step.forest.land | приземление на подстилку | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | heavy landing two boots on forest floor, thud, twigs | ✅ (собран) |
| step.snow.walk | шаг по утоптанному снегу | Footsteps.Step | слова snow, schnee (слоя снега на террейне нет) | 3D сеть | oneshot | 8 | тихо | boot step on packed snow, crisp crunch | ✅ |
| step.snow.run | бег по снегу | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on packed snow, fast crunchy steps | ✅ |
| step.snow.crouch | присяд по снегу | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4–6 | нет | slow soft careful step into snow, muted crunch | ⬜ (сейчас walk ×0.4) |
| step.snow.jump | толчок прыжка по снегу | нет кода | там же | 3D сеть | oneshot | 3 | тихо | push-off crunch in snow | ⬜ |
| step.snow.land | приземление в снег | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing in packed snow, deep crunch thump | ✅ |
| step.frozen.walk | шаг по насту, мёрзлому гравию | Footsteps.Step | слова frozen, glatteis | 3D сеть | oneshot | 8 | тихо | boot on frozen crust, brittle icy crunch | ✅ |
| step.frozen.run | бег по насту | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on icy crust, sharp crunches | ✅ |
| step.frozen.crouch | присяд по насту | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | slow step cracking thin snow crust | ⬜ (сейчас walk ×0.4) |
| step.frozen.jump | толчок по насту | нет кода | там же | 3D сеть | oneshot | 3 | тихо | push-off on icy crust | ⬜ |
| step.frozen.land | приземление на наст | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing breaking icy crust | ✅ |
| step.gravel.walk | шаг по гравию (дорога, крыша X06, камень) | Footsteps.Step; ForestOpening.StartWalk (шаги по крыше X06) | TL_RoadGravel, TL_Rock; слова roof, gravel, kies, rock | 3D сеть | oneshot | 8 | тихо | boot step on frozen gravel | ✅ |
| step.gravel.run | бег по гравию | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on gravel | ✅ |
| step.gravel.crouch | присяд по гравию | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | slow careful step on gravel, few stones | ⬜ (сейчас walk ×0.4) |
| step.gravel.jump | толчок по гравию | нет кода | там же | 3D сеть | oneshot | 3 | тихо | gravel scatter push-off | ⬜ |
| step.gravel.land | приземление на гравий | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing on gravel, stones scatter | ✅ |
| step.wood.walk | шаг по доскам (пол избушки, мост) | Footsteps.Step | слова board, plank, wood, holz, parkett, dielen, crate, rm_stationsleiter; SurfaceTag по умолчанию | 3D сеть | oneshot | 8 | тихо | boot on old wooden floorboards, slight creak | ✅ |
| step.wood.run | бег по доскам | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on wooden floor, hollow knocks | ✅ |
| step.wood.crouch | присяд по доскам | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4–6 | нет | slow step, floorboard creak | ⬜ (сейчас walk ×0.4) |
| step.wood.jump | толчок по доскам | нет кода | там же | 3D сеть | oneshot | 3 | тихо | push-off on wooden floor | ⬜ |
| step.wood.land | приземление на доски | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | heavy landing on wooden floor, boom and creak | ✅ |
| step.tile.walk | шаг по плитке (и сейчас асфальт, бетон, линолеум); шаги в коридоре X09 | Footsteps.Step; ForestOpening.StartWalk (X09) | TL_Asphalt; слова rm_, tile, fliese, linol, concrete, beton, asphalt, floor; indoorDefault | 3D сеть | oneshot | 8 | тихо | boot on tiled floor indoors, hard click | ✅ |
| step.tile.run | бег по плитке | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on tiles, echoing | ✅ |
| step.tile.crouch | присяд по плитке | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | slow quiet rubber sole on tile | ⬜ (сейчас walk ×0.4) |
| step.tile.jump | толчок по плитке | нет кода | там же | 3D сеть | oneshot | 3 | тихо | sole squeak push-off on tile | ⬜ |
| step.tile.land | приземление на плитку | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing on tile floor, slap | ✅ |
| step.metal.walk | шаг по металлу (контейнер, лист, решётка) | Footsteps.Step | слова grating, grate, metal, steel, stahl, blech, container | 3D сеть | oneshot | 8 | тихо | boot on steel floor, hollow clank | ✅ |
| step.metal.run | бег по металлу | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on sheet steel, booming clanks | ✅ |
| step.metal.crouch | присяд по металлу | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | careful soft step on steel, faint ring | ⬜ (сейчас walk ×0.4) |
| step.metal.jump | толчок по металлу | нет кода | там же | 3D сеть | oneshot | 3 | тихо | push-off on steel plate | ⬜ |
| step.metal.land | приземление на металл | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing on steel floor, loud bang | ✅ |
| step.dirt.walk | шаг по земле тропы | Footsteps.Step | TL_TrailSoil, TL_TrailTrack; слова dirt, soil, erde | 3D сеть | oneshot | 8 | тихо | boot on frozen dirt path | ✅ |
| step.dirt.run | бег по земле (им же играют Leaves и Mud) | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on hard dirt | ✅ |
| step.dirt.crouch | присяд по земле | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | slow soft step on dirt | ⬜ (сейчас walk ×0.4) |
| step.dirt.jump | толчок по земле | нет кода | там же | 3D сеть | oneshot | 3 | тихо | scuff push-off on dirt | ⬜ |
| step.dirt.land | приземление на землю (defaultLand, им же играет Mud) | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing thud on hard earth | ✅ |
| step.grass.walk | шаг по сухой траве | Footsteps.Step | слово grass | 3D сеть | oneshot | 7 | тихо | step through dry frozen grass | ✅ |
| step.grass.run | бег по траве | Footsteps.Step (run пуст → walk) | там же | 3D сеть | oneshot | 8 | средне | running through dry grass | ⬜ |
| step.grass.crouch | присяд по траве | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | slow swish in dry grass | ⬜ |
| step.grass.jump | толчок по траве | нет кода | там же | 3D сеть | oneshot | 3 | тихо | push-off in grass | ⬜ |
| step.grass.land | приземление на траву (им же играет Leaves) | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing in dry grass | ✅ |
| step.leaves.walk | шаг по листве | Footsteps.Step | слова leaves, laub | 3D сеть | oneshot | 8 | тихо | step on dry frozen leaves | ✅ |
| step.leaves.run | бег по листве | Footsteps.Step (сейчас dirt.run) | там же | 3D сеть | oneshot | 8 | средне | running through dry leaves | ⬜ |
| step.leaves.crouch | присяд по листве | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | slow crackle of leaves | ⬜ |
| step.leaves.jump | толчок по листве | нет кода | там же | 3D сеть | oneshot | 3 | тихо | leaf rustle push-off | ⬜ |
| step.leaves.land | приземление на листву | Footsteps.Land (сейчас grass.land) | там же | 3D сеть | oneshot | 3 | средне | landing in pile of leaves | ⬜ |
| step.mud.walk | шаг по грязи, колее | Footsteps.Step | TL_RoadRuts; слова mud, schlamm | 3D сеть | oneshot | 8 | тихо | boot in half-frozen mud, slush | ✅ |
| step.mud.run | бег по грязи | Footsteps.Step (сейчас dirt.run) | там же | 3D сеть | oneshot | 8 | средне | running through slushy mud | ⬜ |
| step.mud.crouch | присяд по грязи | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | slow squelch in mud | ⬜ |
| step.mud.jump | толчок по грязи | нет кода | там же | 3D сеть | oneshot | 3 | тихо | suction pull-off from mud | ⬜ |
| step.mud.land | приземление в грязь | Footsteps.Land (сейчас dirt.land) | там же | 3D сеть | oneshot | 3 | средне | landing splat in mud | ⬜ |
| step.snowdeep.walk | шаг по рыхлому глубокому снегу (предлож.) | Footsteps.Step (нужна поверхность) | нужен слой снега на террейне | 3D сеть | oneshot | 8 | тихо | boot sinking deep into fresh powder snow | ⬜ |
| step.snowdeep.run | бег по глубокому снегу | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | wading run through deep snow, heavy swishes | ⬜ |
| step.snowdeep.crouch | присяд по глубокому снегу | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | slow muffled step in powder | ⬜ |
| step.snowdeep.jump | толчок из глубокого снега | нет кода | там же | 3D сеть | oneshot | 3 | тихо | pulling boots out of deep snow | ⬜ |
| step.snowdeep.land | приземление в сугроб | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | body-weight landing in snowdrift, soft whump | ⬜ |
| step.ice.walk | шаг по гладкому льду озера (предлож.) | Footsteps.Step (нужна поверхность) | лёд озера (позже), glatteis | 3D сеть | oneshot | 8 | тихо | boot on thick lake ice, hard tap, faint hollow | ⬜ |
| step.ice.run | бег по льду | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on lake ice, slips and taps | ⬜ |
| step.ice.crouch | присяд по льду | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | slow careful step on ice | ⬜ |
| step.ice.jump | толчок по льду | нет кода | там же | 3D сеть | oneshot | 3 | тихо | scrape push-off on ice | ⬜ |
| step.ice.land | приземление на лёд (с гулом льда) | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing on lake ice, deep booming ice ping | ⬜ |
| step.asphalt.walk | шаг по асфальту (сейчас Tile) | Footsteps.Step | TL_Asphalt | 3D сеть | oneshot | 8 | тихо | boot on cold asphalt with grit | ⬜ |
| step.asphalt.run | бег по асфальту | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on asphalt road | ⬜ |
| step.asphalt.crouch | присяд по асфальту | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | quiet step on asphalt | ⬜ |
| step.asphalt.jump | толчок по асфальту | нет кода | там же | 3D сеть | oneshot | 3 | тихо | grit scuff push-off | ⬜ |
| step.asphalt.land | приземление на асфальт | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing slap on asphalt | ⬜ |
| step.concrete.walk | шаг по бетону: гараж, котельная (сейчас Tile) | Footsteps.Step | слова concrete, beton | 3D сеть | oneshot | 8 | тихо | boot on bare concrete, gritty, reverberant room | ⬜ |
| step.concrete.run | бег по бетону | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on concrete, echo | ⬜ |
| step.concrete.crouch | присяд по бетону | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | quiet gritty step on concrete | ⬜ |
| step.concrete.jump | толчок по бетону | нет кода | там же | 3D сеть | oneshot | 3 | тихо | concrete push-off scuff | ⬜ |
| step.concrete.land | приземление на бетон | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing on concrete, hard thud | ⬜ |
| step.linoleum.walk | шаг по линолеуму комнат (сейчас Tile) | Footsteps.Step | слово linol, комнаты rm_* | 3D сеть | oneshot | 8 | тихо | rubber sole on old linoleum, soft squeak | ⬜ |
| step.linoleum.run | бег по линолеуму | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on linoleum, squeaks | ⬜ |
| step.linoleum.crouch | присяд по линолеуму | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | very quiet step on linoleum | ⬜ |
| step.linoleum.jump | толчок по линолеуму | нет кода | там же | 3D сеть | oneshot | 3 | тихо | squeak push-off | ⬜ |
| step.linoleum.land | приземление на линолеум | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing slap on linoleum over concrete | ⬜ |
| step.rock.walk | шаг по скале, валунам (сейчас Gravel) | Footsteps.Step | TL_Rock | 3D сеть | oneshot | 8 | тихо | boot on frozen solid rock, hard scrape | ⬜ |
| step.rock.run | бег по камню | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on rock | ⬜ |
| step.rock.crouch | присяд по камню | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | careful step on stone | ⬜ |
| step.rock.jump | толчок по камню | нет кода | там же | 3D сеть | oneshot | 3 | тихо | scrape push-off on rock | ⬜ |
| step.rock.land | приземление на камень | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing on rock, hard knock | ⬜ |
| step.grating.walk | шаг по решётке, перелазу (сейчас Metal) | Footsteps.Step | слова grating, grate; Audio_Stile | 3D сеть | oneshot | 8 | тихо | boot on steel mesh grating, rattling ring | ⬜ |
| step.grating.run | бег по решётке | Footsteps.Step | там же | 3D сеть | oneshot | 8 | средне | running on metal grating, loud rattle | ⬜ |
| step.grating.crouch | присяд по решётке | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 4 | нет | slow step on grating, faint rattle | ⬜ |
| step.grating.jump | толчок по решётке | нет кода | там же | 3D сеть | oneshot | 3 | тихо | grating clank push-off | ⬜ |
| step.grating.land | приземление на решётку | Footsteps.Land | там же | 3D сеть | oneshot | 3 | средне | landing on grating, crash and ring | ⬜ |
| step.carpet.walk | шаг по коврику (предлож.) | Footsteps.Step | Item_Rug ×1, ковры (слова нет) | 3D сеть | oneshot | 6 | тихо | muffled boot on rug over wood | ⬜ |
| step.carpet.run | бег по коврику | Footsteps.Step | там же | 3D сеть | oneshot | 6 | тихо | muffled running on rug | ⬜ |
| step.carpet.crouch | присяд по коврику | Footsteps.Step (sneak) | там же | 3D сеть | oneshot | 3 | нет | almost silent step on rug | ⬜ |
| step.carpet.jump | толчок по коврику | нет кода | там же | 3D сеть | oneshot | 2 | нет | soft push-off on rug | ⬜ |
| step.carpet.land | приземление на коврик | Footsteps.Land | там же | 3D сеть | oneshot | 3 | тихо | muffled landing thump on rug | ⬜ |
| step.layer.frostcreak | слой скрипа под шагом в ясный мороз (поверх снега) | нет кода (погода «ясный мороз») | Snow, SnowDeep | 3D сеть | oneshot | 8 | тихо | squeaky creak of snow at minus 30, dry squeal | ⬜ |

#### 2. Тело, одежда, лестница, рэгдолл, кровать, холод
| id | what | when | where | 3D/2D | oneshot/loop | variants | шум | prompt | status |
|---|---|---|---|---|---|---|---|---|---|
| cloth.walk | шорох куртки при ходьбе | нет кода (с Footsteps.Step) | все игроки | 3D сеть | oneshot | 6 | тихо | nylon parka swish per step, heavy winter clothes | ⬜ |
| cloth.run | шорох одежды на бегу | нет кода | все игроки | 3D сеть | oneshot | 6 | тихо | fast heavy jacket swishes while running | ⬜ |
| cloth.crouch.down | присесть: шорох | нет кода (HLCharacterMotor.ChangeStance) | все игроки | 3D сеть | oneshot | 3 | тихо | crouching down in thick winter clothes, fabric creak | ⬜ |
| cloth.crouch.up | встать из присяда | нет кода (ChangeStance) | все игроки | 3D сеть | oneshot | 3 | тихо | standing up, jacket rustle | ⬜ |
| cloth.jump | прыжок: шорох и снаряжение | нет кода | все игроки | 3D сеть | oneshot | 3 | тихо | jacket whoosh and gear jostle on jump | ⬜ |
| cloth.land | приземление: снаряжение подпрыгивает | нет кода (Footsteps.Land) | все игроки | 3D сеть | oneshot | 3 | тихо | gear and clothes thump on landing | ⬜ |
| gear.pockets.jingle | ключи и вещи звякают в карманах на бегу | нет кода (есть ключ в CargoItem карманах) | Item_Key_* и др. | 3D сеть | oneshot | 6 | тихо | keys jingling in coat pocket while running | ⬜ |
| gear.heavy.carry | скрип ноши и одышка с тяжёлым в руках | нет кода (InteractionSensor.HeldHeavy) | ящики, канистра, мешок | 3D сеть | loop | 3 | тихо | strained steps carrying heavy box, creaking load | ⬜ |
| ladder.alu.grab | встать на стремянку, взяться | нет кода (HLCharacterMotor.EnterLadder) | StepLadder_Net (IV_Ladder_Climb) | 3D сеть | oneshot | 2 | тихо | hands grab aluminium ladder, boot on rung | ⬜ |
| ladder.alu.climb | ступенька стремянки | нет кода (HLCharacterMotor.LadderMove) | StepLadder_Net | 3D сеть | oneshot | 6 | тихо | boot on aluminium ladder rung, hollow clank | ⬜ |
| ladder.alu.leave | сойти со стремянки | нет кода (LeaveLadder) | StepLadder_Net | 3D сеть | oneshot | 2 | тихо | stepping off aluminium ladder | ⬜ |
| body.callout.whistle | оклик свистом (C нажать) | BodyVoice.Update → VoiceNet.Body(Callout) | все игроки | 3D сеть | oneshot | 2 | громко | sharp two-finger whistle outdoors | 🔇 vo_body_callout_* |
| body.callout.hey | оклик «Hey!» | то же, тот же набор | все игроки | 3D сеть | oneshot | 2 | громко | man shouting hey in cold air | 🔇 vo_body_callout_* |
| body.land | кряхтение при жёстком приземлении (≥7,5 м/с) | BodyVoice.Update (Land) | все | 3D сеть | oneshot | 4 | средне | man grunt on hard landing | 🔇 vo_body_land_* |
| body.pain.small | слабая боль, ударился | нет триггера (enum PainSmall) | все | 3D сеть | oneshot | 4 | средне | short male pained hiss ouch, no words | ⬜ (enum) |
| body.pain.big | сильная боль, падение с высоты (≥12 м/с) | BodyVoice.Update (PainBig) | все | 3D сеть | oneshot | 4 | громко | male loud pained cry, no words | 🔇 vo_body_painbig_* |
| body.effort.light | лёгкое усилие (поднять, открыть) | нет триггера (enum EffortLight) | все | 3D сеть | oneshot | 4 | тихо | soft male effort exhale | ⬜ (enum) |
| body.effort.heavy | тяжёлое усилие (поднял тяжёлое) | BodyVoice.Update (HeldHeavy) | канистра, ящики, мешок, АКБ | 3D сеть | oneshot | 5 | тихо | male straining grunt lifting heavy | 🔇 vo_body_effortheavy_* |
| body.effort.push | толкнуть или дёрнуть застрявшее | нет кода | — | 3D сеть | oneshot | 3 | тихо | male short strain pushing stuck thing | ⬜ |
| body.cough | кашель на холоде | нет триггера (enum Cough, ждёт систему холода) | все | 3D сеть | oneshot | 5 | средне | dry cough in freezing air, man | ⬜ (enum) |
| body.sneeze | чих | нет кода | все | 3D сеть | oneshot | 3 | средне | man sneezing, muffled by scarf | ⬜ |
| body.shiver | дрожь, стук зубов (стадия «холодно») | нет триггера (enum Shiver) | все | 2D свой / 3D чужой | loop | 4 | тихо | teeth chattering and shivering breath | ⬜ (enum) |
| body.breath.calm | спокойное дыхание на морозе | нет триггера (enum Breath; FirstPersonView дыхание стоя) | все | 2D свой / 3D чужой | loop | 4 | тихо | calm nasal breathing in cold air | ⬜ (enum) |
| body.breath.run | одышка после бега | нет кода (FirstPersonView: насколько запыхался) | все | 2D свой / 3D чужой | loop | 4 | тихо | heavy panting after sprint in cold | ⬜ |
| body.breath.fear | испуганное частое дыхание (страх заразен) | нет кода (VISION: Страх) | все | 3D сеть | loop | 3 | тихо | frightened shaky rapid breathing | ⬜ |
| body.exhale.steam | выдох с паром (стадия «зябко») | нет кода | все | 3D сеть | oneshot | 4 | тихо | soft long exhale in frost | ⬜ |
| body.warm.relief | отогрелся у печки, облегчённый выдох | нет кода | все | 3D сеть | oneshot | 3 | тихо | relieved warm sigh | ⬜ |
| body.hypothermia | переохлаждение: медленное тяжёлое дыхание | нет кода (стадия «замерзание») | все | 2D свой / 3D чужой | loop | 2 | тихо | slow weak labored breathing, freezing | ⬜ |
| body.faint | обморок от холода | нет кода | все | 3D сеть | oneshot | 2 | средне | weak groan and collapse | ⬜ |
| body.wake | пробуждение в кровати, вдох | нет триггера (enum Wake) | ForestBunks | 3D сеть | oneshot | 3 | тихо | waking gasp, deep inhale | ⬜ (enum) |
| body.yawn | зевок (не спал) | нет кода | все | 3D сеть | oneshot | 3 | тихо | tired male yawn | ⬜ |
| body.drink.hot | горячее питьё: глоток и выдох | нет кода | термос, чайник, кружки | 3D сеть | oneshot | 3 | тихо | sip of hot tea, satisfied exhale | ⬜ |
| body.eat | еда | нет кода | консервы, хлеб | 3D сеть | oneshot | 3 | тихо | chewing, crunch of biscuit | ⬜ |
| body.stomach | урчание в животе (голод, предлож.) | нет кода | — | 3D | oneshot | 2 | нет | stomach growl | ⬜ |
| body.death | падение в темноту (смерть) | нет кода (RagdollCause.Death) | все | 3D сеть | oneshot | 3 | громко | dying male gasp then collapse | ⬜ |
| body.dragged.scream | утащили в лес: крик удаляется | нет кода | все | 3D сеть | oneshot | 2 | громко | man screaming being dragged away into distance | ⬜ |
| ragdoll.fall.soft | тело падает в снег | нет кода (PlayerRagdoll.Begin) | все | 3D сеть | oneshot | 3 | средне | body collapsing into snow, heavy whump | ⬜ |
| ragdoll.fall.hard | тело падает на доски, плитку, металл | нет кода (PlayerRagdoll.Begin + SurfaceResolver) | все | 3D сеть | oneshot | 4 | средне | body falling on hard floor, thud | ⬜ |
| ragdoll.limb | удары рук и ног после падения | нет кода (столкновения костей) | все | 3D сеть | oneshot | 6 | тихо | limb knocks on ground, small thumps | ⬜ |
| ragdoll.hit | тело сбито машиной или существом | нет кода (PlayerRagdoll.CheckVehicleHit; PlayerNet сбить с ног) | все | 3D сеть | oneshot | 3 | громко | hard body impact, punch thud | ⬜ |
| ragdoll.getup | подняться с земли | нет кода (PlayerRagdoll.GetUp) | все | 3D сеть | oneshot | 3 | тихо | getting up from snow, clothes rustle, grunt | ⬜ |
| ragdoll.death.blackout | затемнение после смерти: стингер и тишина 1–2 с | нет кода (EndDeath) | свой игрок | 2D | oneshot | 1 | нет | low muffled heartbeat stop, fade to silence | ⬜ |
| player.fallreset | падение с края, сброс позиции | нет кода (LocationFallReset.Update) | все локации | 2D | oneshot | 1 | нет | muffled whoosh into silence | ⬜ |
| bed.lie | лечь на койку (пружины) | нет кода (ForestBunks, Sleep_1…4) | 4 койки казармы | 3D сеть | oneshot | 3 | тихо | old metal bunk springs creak as man lies down | ⬜ |
| bed.getup | встать с койки | нет кода (выход из места, удерживать F) | 4 койки | 3D сеть | oneshot | 3 | тихо | bunk springs creak, getting up | ⬜ |
| bed.sleep.fade | все легли: затемнение сна | нет кода (ForestOpening ~914 SleepSeconds, затемнение ~1344) | свой игрок | 2D | oneshot | 1 | нет | soft low drone fading into sleep | ⬜ |
| cold.frost.creep | иней растёт по краям экрана | нет кода (ScreenFrost.Target растёт) | свой игрок | 2D | loop | 1 | нет | faint crystalline ice crackle spreading | ⬜ |
| cold.frost.melt | иней тает под крышей | нет кода (ScreenFrost, под крышей) | свой игрок | 2D | oneshot | 1 | нет | soft thaw, tiny ice ticks | ⬜ |
| cold.freezing.heart | замерзание: медленное сердце под виньеткой | нет кода | свой игрок | 2D | loop | 1 | нет | slow muffled heartbeat in cold | ⬜ |

#### 3. Предметы
Карманы общие для всех предметов. Удар `ItemImpactSound` вешается на каждый CargoItem (SnowAudio:110), материал угадывается по словам в названии.

| id | what | when | where | 3D/2D | oneshot/loop | variants | шум | prompt | status |
|---|---|---|---|---|---|---|---|---|---|
| ui.pocket.take | взял предмет из мира в руку | PocketsHud.Update | все карманные (≤3 кг) | 2D | oneshot | 3 | нет | soft quick grab, fabric and hand | 🔇 |
| ui.pocket.in | убрал в карман (Q) | PocketsHud.Update | все карманные | 2D | oneshot | 3 | нет | item slipped into coat pocket | 🔇 |
| ui.pocket.switch | сменил слот (1–8, колесо) | PocketsHud.Update | все карманные | 2D | oneshot | 3 | нет | quick pull from pocket | 🔇 |
| ui.pocket.throw | бросил (G); тяжёлое поставил (×0,6) | PocketsHud.Update | все | 2D | oneshot | 3 | нет | short whoosh of release | 🔇 |
| ui.deny | отказ (карманы полны, заперто, нельзя) | PocketsHud.Update (sensor.MessageAt) | все | 2D | oneshot | 3 | нет | dull soft muted denial knock | 🔇 |
| item.throw.whoosh | свист брошенного предмета в мире | нет кода (PlayerNet бросок G → CargoItem.ServerDrop) | все | 3D сеть | oneshot | 4 | нет | object whoosh thrown through air | ⬜ |
| item.pickup.metal | взять металлическое (слышат рядом) | нет кода (CargoItem.ServerTakeInHand) | инструменты, банки, фонарики, ключи | 3D сеть | oneshot | 4 | нет | picking up small metal object, clink | ⬜ |
| item.pickup.glass | взять бутылку, кружку, тарелку | нет кода | бутылки ×16, кружки ×11, тарелки ×7, банка-стекло ×2, пепельница ×5 | 3D сеть | oneshot | 4 | нет | glass bottle lifted, light clink | ⬜ |
| item.pickup.paper | взять бумагу, папку | нет кода | записки, бланки, стикеры, конверты, планшеты, стопки, папки, журналы, газеты, фото | 3D сеть | oneshot | 4 | нет | paper sheet picked up, crisp rustle | ⬜ |
| item.pickup.cloth | взять мягкое | нет кода | полотенце ×5, одежда ×3, тряпка ×5, ботинки ×3, рюкзак ×2, кукла, коврик | 3D сеть | oneshot | 4 | нет | soft cloth lifted, fabric rustle | ⬜ |
| item.pickup.plastic | взять пластик | нет кода | наушники, батарейки, рация, налобник, пейджер, пачки сигарет | 3D сеть | oneshot | 4 | нет | small plastic object picked up | ⬜ |
| item.putdown.heavy.wood | поставить тяжёлый деревянный ящик (E) | нет кода (CargoItem.ServerRelease) | Item_WoodCrate ×2, VanItem_Log | 3D сеть | oneshot | 3 | тихо | heavy wooden crate set down, thud creak | ⬜ |
| item.putdown.heavy.metal | поставить тяжёлое металлическое | нет кода | Toolbox ×4, Jerrycan, CableReel ×2, OilCan ×4, PaintCan ×3, Battery 18 кг | 3D сеть | oneshot | 3 | тихо | heavy metal box set down on floor, clank | ⬜ |
| item.putdown.box | поставить картонную коробку | нет кода | BoxSmall ×5, BoxLarge ×4, BoxOpen ×2 | 3D сеть | oneshot | 3 | тихо | cardboard box set down, hollow thump | ⬜ |
| item.putdown.sack | бросить мешок 25 кг | нет кода | Item_Sack ×2 | 3D сеть | oneshot | 3 | тихо | heavy grain sack dropped, dull thud | ⬜ |
| item.impact.metal | удар металла (по умолчанию для всего остального) | ItemImpactSound.OnCollisionEnter | всё, где нет других слов | 3D сеть | oneshot | 3 | тихо | metal object hits floor, clank | ✅ |
| item.impact.glass | удар стекла, керамики | ItemImpactSound | glass, bottle, jar, mug, plate | 3D сеть | oneshot | 3 | тихо | glass bottle knocks on floor, not breaking | ✅ |
| item.impact.paper | удар бумаги, картона | ItemImpactSound | paper, book, box, card, binder, note, magazine | 3D сеть | oneshot | 3 | тихо | paper bundle slaps floor | ✅ |
| item.impact.wood | удар дерева | ItemImpactSound | crate, wood, ящик, доск | 3D сеть | oneshot | 3 | тихо | wooden object knocks floor | ✅ |
| item.impact.cloth | мягкий удар | нет (сейчас metal) | полотенце, одежда, тряпка, мешки, рюкзак, кукла, коврик, туалетная бумага, хлеб | 3D сеть | oneshot | 3 | тихо | soft fabric bundle falls, muffled flop | ⬜ |
| item.impact.ceramic | удар кружки, тарелки (отдельно от стекла) | нет (сейчас glass) | MugGreen/White/Tipped, Plate, PlateDirty, Ashtray | 3D сеть | oneshot | 3 | тихо | ceramic mug knocks floor | ⬜ |
| item.impact.plastic | удар пластика | нет (сейчас metal) | наушники, стакан для щёток, батарейки, рация, налобник, кассета | 3D сеть | oneshot | 3 | тихо | small plastic object clatters | ⬜ |
| item.impact.tin | дребезг пустой жестянки | нет (сейчас metal) | CanRed/Green ×4, CanCrushed ×3, TinLying ×4, TinOpen ×4 | 3D сеть | oneshot | 4 | тихо | empty tin can clatters and rattles | ⬜ |
| item.impact.heavymetal | глухой удар тяжёлого металла | нет (сейчас metal) | Toolbox, CrankHandle, WheelBrace, Jack, Battery, генератор | 3D сеть | oneshot | 3 | средне | heavy steel object thuds hard | ⬜ |
| item.impact.rubber | удар резины | нет | Boots ×3, VanItem_Wheel, FanBelt, колодки | 3D сеть | oneshot | 3 | тихо | rubber object bounces thud | ⬜ |
| item.impact.liquid | удар полной ёмкости с плеском | нет | BucketWater ×2, Jerrycan полная, OilCan | 3D сеть | oneshot | 3 | тихо | full can hits floor, liquid slosh | ⬜ |
| item.impact.keys | звяканье упавших ключей | нет (сейчас metal) | Item_Key_* ×4, Stationsleiter, PlaneKey, IgnitionKey, WorkerTag | 3D сеть | oneshot | 3 | тихо | keyring dropped, jingle | ⬜ |
| item.glass.break | бутылка или банка разбилась | нет кода | бутылки, банка, лампочки | 3D сеть | oneshot | 3 | средне | glass bottle shatters on floor | ⬜ |
| item.roll.bottle | бутылка катится | нет кода | бутылки | 3D сеть | loop | 2 | тихо | glass bottle rolling on wooden floor | ⬜ |
| item.roll.can | банка катится | нет кода | жестянки | 3D сеть | loop | 2 | тихо | tin can rolling on hard floor | ⬜ |
| item.flashlight.on | фонарик: включить | SurvivalItem.ServerAction (Flashlight); ServerSetOn у стартового | Item_Flashlight ×1 + по одному каждому игроку, Surv_Flashlight_Net, VanItem_Flashlight_Net | 3D сеть | oneshot | 3 | тихо | metal flashlight slide switch click on | ⬜ |
| item.flashlight.off | фонарик: выключить | SurvivalItem.ServerAction | там же | 3D сеть | oneshot | 3 | тихо | flashlight switch click off, lower | ⬜ |
| item.flashlight.dying | фонарик садится: мигание, жужжание | нет кода (батарейки 5 мин – 1 ч, VISION) | фонарики | 3D | loop | 2 | нет | weak bulb flicker buzz | ⬜ |
| item.flashlight.battery | заменить батарейки D (открутить, вставить, закрутить) | нет кода | фонарики + Item_Battery, Item_BatteryPack | 3D сеть | oneshot | 2 | тихо | unscrew flashlight cap, batteries slide in | ⬜ |
| item.lantern.switch | фонарь 6 В: ползунок вкл и выкл | нет кода (Lantern_Switch) | Item_Lantern | 3D сеть | oneshot | 2+2 | тихо | plastic slider switch click | ⬜ |
| item.lantern.door | крышка батарейного отсека | нет кода (Lantern_Door) | Item_Lantern | 3D сеть | oneshot | 2 | тихо | plastic battery door snap open, close | ⬜ |
| item.battery6v.insert | вставить и вынуть батарейку 4R25 | нет кода (Slot_Battery фонаря и датчика) | Item_Battery6V | 3D сеть | oneshot | 2 | тихо | square battery pushed onto spring contacts | ⬜ |
| item.headlamp.twist | налобник: поворот ободка (вкл и выкл) | нет кода (Headlamp_Bezel) | Item_Headlamp | 3D сеть | oneshot | 2 | тихо | ribbed bezel twist click | ⬜ |
| item.headlamp.tilt | налобник: наклон лампы | нет кода | Item_Headlamp | 3D | oneshot | 2 | нет | ratchet click tilting lamp | ⬜ |
| item.headlamp.wear | надеть, снять налобник | нет кода (Wear_Head) | Item_Headlamp | 3D сеть | oneshot | 2 | тихо | elastic strap stretched over hat | ⬜ |
| item.flaregun.fire | ракетница: выстрел | SurvivalItem.ShotRpc | Surv_FlareGun_Net | 3D сеть | oneshot | 3 | громко | flare pistol shot, deep pop and fizz | ⬜ |
| item.flaregun.flight | сигнальная ракета летит и шипит (7 с) | SignalFlareEffect.Update | ракета | 3D сеть | loop | 1 | громко | signal flare hissing rising in sky | ⬜ |
| item.flaregun.dry | щелчок незаряженной | SurvivalItem.ServerAction (не заряжена) | Surv_FlareGun_Net | 3D | oneshot | 2 | нет | empty pistol hammer click | ⬜ |
| item.flaregun.open | переломить ракетницу | нет кода | Surv_FlareGun_Net | 3D сеть | oneshot | 2 | тихо | break-action pistol opening clack | ⬜ |
| item.flaregun.load | вставить патрон и закрыть | SurvivalItem.ServerAction (FlareShell) | Surv_FlareShell_Net | 3D сеть | oneshot | 2 | тихо | flare cartridge slides in, barrel snaps shut | ⬜ |
| item.flare.ignite | фальшфейер: зажечь | SurvivalItem.ServerAction (Flare) | Surv_Flare_Net, VanItem_Flare_Net | 3D сеть | oneshot | 3 | средне | road flare strike, scratch and ignition whoosh | ⬜ |
| item.flare.burn | горение фальшфейера (60 с) | SurvivalItem.Update (Burning) | там же | 3D сеть | loop | 1 | средне | road flare sputtering burn, crackling hiss | ⬜ |
| item.flare.out | фальшфейер догорел | SurvivalItem (Spent) | там же | 3D сеть | oneshot | 2 | тихо | flare fizzles out | ⬜ |
| item.flare.snow | горящий фальшфейер упал в снег | нет кода | там же | 3D сеть | oneshot | 2 | тихо | burning flare hissing in snow | ⬜ |
| item.shovel.dig | лопата: копнуть снег | нет кода (копать пока нечего) | Surv_Shovel_Net, VanItem_Shovel_Net | 3D сеть | oneshot | 4 | тихо | shovel scoops snow | ⬜ |
| item.shovel.toss | лопата: отбросить снег | нет кода | там же | 3D сеть | oneshot | 3 | тихо | snow thrown off shovel | ⬜ |
| item.shovel.scrape | «копать нечего»: лопата скребёт по твёрдому | SurvivalItem.ServerAction (Shovel) | там же | 3D сеть | oneshot | 2 | тихо | shovel scraping hard frozen ground | ⬜ |
| item.iceaxe.swing | ледоруб: замах | нет кода | Item_IceAxe | 3D | oneshot | 2 | нет | tool swing whoosh | ⬜ |
| item.iceaxe.chip | ледоруб: сколоть лёд (П1) | нет кода (Tip_Pick) | Item_IceAxe | 3D сеть | oneshot | 4 | средне | ice pick chipping thick ice, crack and shards | ⬜ |
| item.keys.jingle | ключи звякают при взятии | нет кода | Item_Key_Garage/Generator/Hauptgebaeude/Schranke, Key_Stationsleiter, PlaneKey_Net, VanItem_IgnitionKey, VanItem_MassKey | 3D сеть | oneshot | 4 | нет | brass key on ring jingle | ⬜ |
| item.key.unlock | ключ в замок, поворот, отперто | ForestOpening.TryUnlock | ключи | 3D сеть | oneshot | 3 | тихо | key inserted and turned, lock clicks open | ⬜ |
| item.key.wrong | ключ не подходит, заперто | ForestOpening.TryUnlock (отказ) | ключи | 3D сеть | oneshot | 2 | тихо | key jiggles in lock, won't turn | ⬜ |
| item.padlock.open | навесной замок: открыть ключом | ForestOpening.UsePadlock | Padlock_Barrier (Item_Padlock) | 3D сеть | oneshot | 2 | тихо | padlock shackle springs open | ⬜ |
| item.padlock.fall | замок падает со шлагбаума | ForestOpening.UsePadlock (настоящий замок падает) | Item_Padlock | 3D сеть | oneshot | 2 | тихо | padlock drops into snow | ⬜ (сейчас impact.metal) |
| item.workertag.jingle | жетон на кольце | нет кода | Item_WorkerTag | 3D | oneshot | 2 | нет | brass tag on split ring jingle | ⬜ |
| item.jerrycan.slosh | плеск в канистре при переноске (по уровню) | нет кода (FuelCan) | Item_Jerrycan20 ×2, VanItem_Jerrycan, KitCarry_JerryCan | 3D сеть | loop | 3 | тихо | fuel sloshing inside steel jerrycan | ⬜ |
| item.jerrycan.cap | открутить, закрутить крышку | нет кода | там же | 3D сеть | oneshot | 2 | тихо | jerrycan cap unscrew clack | ⬜ |
| forest.fuel.pour | заливка из канистры | ForestOpening (~637, раз в 5 с) | канистра → бак | 3D сеть | oneshot | 1 | тихо | fuel pouring from jerrycan into tank | ✅ |
| item.jerrycan.empty | канистра опустела: бульканье | нет кода | канистры | 3D сеть | oneshot | 2 | тихо | last glugs of empty can | ⬜ |
| item.jerrycan.spill | пролив мимо воронки | нет кода | канистры | 3D сеть | loop | 1 | тихо | liquid splashing on ground | ⬜ |
| item.jerrycan.fill | наполнение у бочки | нет кода (FuelBarrel, зона Barrel_Fill) | KitCarry_JerryCan | 3D сеть | loop | 1 | тихо | fuel filling jerrycan, rising glug | ⬜ |
| item.funnel.set | вставить воронку | нет кода | Item_Funnel, KitCarry_Funnel | 3D сеть | oneshot | 2 | тихо | metal funnel set into filler neck | ⬜ |
| item.can.slosh | масло или краска в банке | нет кода | OilCan ×4, PaintCan ×3, VanItem_OilCan, AntifreezeCan | 3D сеть | oneshot | 3 | тихо | thick liquid slosh in tin can | ⬜ |
| item.bucket.slosh | вода в ведре при переноске | нет кода | Item_BucketWater ×2 | 3D сеть | loop | 2 | тихо | water sloshing in metal bucket | ⬜ |
| item.bucket.handle | дужка ведра | нет кода | Bucket, BucketWater | 3D сеть | oneshot | 2 | тихо | bucket handle clank | ⬜ |
| item.bucket.splash | ведро упало, вода разлилась | нет кода | BucketWater | 3D сеть | oneshot | 2 | тихо | bucket spills water splash | ⬜ |
| item.rag.scrub | тряпка: оттирать пятно (2 с) | ForestOpening (уборка, держать 2 с) | Item_Rag ×5 | 3D сеть | loop | 2 | тихо | cloth scrubbing floor | ⬜ |
| item.mop.scrub | швабра: мыть пол | ForestOpening (уборка) | Item_Mop ×2 | 3D сеть | loop | 2 | тихо | wet mop scrubbing tiles | ⬜ |
| item.broom.sweep | метла: подметать | нет кода | Item_Broom ×1 | 3D сеть | loop | 2 | тихо | broom sweeping wooden floor | ⬜ |
| item.dustpan.scrape | совок по полу | нет кода | Item_Dustpan ×1 | 3D сеть | oneshot | 2 | тихо | dustpan scraping floor | ⬜ |
| forest.trash.rustle | мусор упал в мешок | ForestOpening (~775); TrashBag.OnTriggerEnter | Item_TrashBag ×2 | 3D сеть | oneshot | 3 | тихо | trash dropped into plastic bin bag | ✅ |
| item.trashbag.carry | мешок шуршит и звякает при переноске | нет кода | Item_TrashBag | 3D сеть | loop | 2 | тихо | full plastic bag rustling, cans clink | ⬜ |
| item.note.open | записка: открыть в NoteReader (T) | нет кода (NoteReader.Update) | Item_Note ×10, NoteForm ×7, StickyNote ×5 и другое читаемое | 2D | oneshot | 3 | нет | paper note unfolded | ⬜ |
| item.note.close | записка: закрыть | нет кода (NoteReader: T или Esc) | там же | 2D | oneshot | 3 | нет | paper folded | ⬜ |
| item.paper.flip | листать длинную записку, книгу | нет кода (прокрутка в NoteReader) | Book ×2, BookOpen ×2, BookStack ×3, Binder ×7, Clipboard ×4 | 2D | oneshot | 3 | нет | page flip | ⬜ |
| item.newspaper.rustle | большая газета или карта шуршит | нет кода | Newspaper, NewspaperOpen ×2, Magazine ×3, VanItem_Map | 3D сеть | oneshot | 3 | нет | large newspaper rustling | ⬜ |
| item.binder.clip | щелчок колец папки или зажима планшета | нет кода | Binder ×7, Clipboard ×4 | 3D | oneshot | 2 | нет | ring binder snap | ⬜ |
| item.envelope.open | открыть конверт | нет кода | Item_Envelope ×4 | 2D | oneshot | 2 | нет | envelope flap torn open | ⬜ |
| item.waybill.peel | снять накладную с бака | нет кода (DosingPump_Waybill) | Item_DosingPump | 3D | oneshot | 1 | нет | paper peeled off tape | ⬜ |
| item.medicine.rattle | таблетки гремят | нет кода | Item_Medicine ×2 | 3D | oneshot | 2 | нет | pills rattling in small box | ⬜ |
| item.bottle.slosh | плеск в бутылке | нет кода | BottleSchnapps ×2, BottleWater ×4, BottleBeer ×10 | 3D | oneshot | 2 | нет | liquid sloshing in glass bottle | ⬜ |
| item.pot.lid | крышка кастрюли дребезжит | нет кода | Item_Pot ×2, Item_Pan | 3D | oneshot | 2 | нет | pot lid rattle | ⬜ |
| item.kettle.pour | налить из чайника | нет кода (еда и питьё) | Item_Kettle, VanItem_Kettle | 3D сеть | oneshot | 2 | тихо | pouring hot water into mug | ⬜ |
| item.thermos.open | открутить крышку термоса | нет кода | Item_Thermos | 3D | oneshot | 2 | нет | thermos cap unscrew, steam puff | ⬜ |
| item.thermos.pour | налить из термоса | нет кода | Item_Thermos | 3D | oneshot | 2 | нет | hot tea poured into thermos cup | ⬜ |
| item.cards.handle | колода карт | нет кода | Item_Cards | 3D | oneshot | 2 | нет | deck of cards shuffled | ⬜ |
| item.backpack.handle | рюкзак: лямки, молния | нет кода | Item_Backpack ×2 | 3D сеть | oneshot | 2 | тихо | canvas backpack straps, zipper | ⬜ |
| item.bag.canvas | брезентовая сумка, мешки комплекта | нет кода (stowedMarker) | Surv_Bag_Net, KitCarry_*CoverBag ×3, TieDownBag | 3D сеть | oneshot | 3 | тихо | canvas bag handled, buckle clink | ⬜ |
| item.crate.carry | деревянный ящик скрипит в руках | нет кода | WoodCrate ×2 | 3D сеть | loop | 2 | тихо | wooden crate creaking while carried | ⬜ |
| item.toolbox.rattle | инструменты гремят в ящике | нет кода | Item_Toolbox ×4, VanItem_ToolBox | 3D сеть | oneshot | 3 | тихо | tools rattling inside steel toolbox | ⬜ |
| item.tool.pickup | взять ручной инструмент | нет кода | Hammer, Screwdriver ×3 (+Van), Wrench ×2 (+Van), Pliers, PlugSpanner, WheelBrace, CrankHandle | 3D сеть | oneshot | 4 | нет | steel hand tool picked up, clink | ⬜ |
| item.chain.clink | цепи в сумке | нет кода | VanItem_ChainBag | 3D сеть | oneshot | 3 | тихо | snow chains clinking in bag | ⬜ |
| item.rope.handle | верёвка, кабель (катушка) | нет кода | RopeCoil, CableReel ×2, TowRope | 3D | oneshot | 2 | нет | coiled rope rustle | ⬜ |
| item.smallparts.rattle | мелкие железки | нет кода | SparkPlug, WheelNut, WingNut, крышки (Radiator, Coolant, OilFiller, Fuel), DistributorCap | 3D | oneshot | 3 | нет | small metal parts rattle in hand | ⬜ |
| item.fuse.tin | коробка предохранителей: открыть, гремит | нет кода | VanItem_FuseTin, VanItem_Fuse | 3D | oneshot | 2 | нет | tin of fuses rattling, lid pop | ⬜ |
| item.fuse.glass | стеклянный предохранитель 5×20 (колпачок открутить) | нет кода (RelayUnit_FuseCap) | Item_RelayUnit | 3D | oneshot | 2 | нет | tiny glass fuse clinks, cap unscrew | ⬜ |
| item.bulb.tinkle | перегоревшая лампочка звенит нитью | нет кода | Item_Bulb (+BulbBox) | 3D | oneshot | 2 | нет | burnt bulb filament tinkle | ⬜ |
| item.bulb.screw | вкрутить лампочку E27 | нет кода (Screw_E27) | Item_Bulb | 3D | oneshot | 2 | нет | glass bulb screwed into socket, squeak | ⬜ |
| item.sodium.glass | натриевая лампа: стекло звенит | нет кода | Item_SodiumLamp (+Box) | 3D | oneshot | 2 | нет | long glass lamp tube clinks | ⬜ |
| item.battery.heavy | АКБ 18 кг: поставить | нет кода | VanItem_Battery_Net (КПП) | 3D сеть | oneshot | 2 | тихо | heavy car battery set down, plastic thud | ⬜ |
| item.wheel.bounce | колесо: удар и качение | нет кода | VanItem_Wheel | 3D сеть | oneshot | 2 | тихо | spare tyre bounce thud | ⬜ |
| item.sandladder.clang | песочные трапы | нет кода | VanItem_SandLadder | 3D сеть | oneshot | 2 | тихо | steel sand ladder clangs | ⬜ |
| item.snowbrush | щётка-скребок | нет кода | VanItem_SnowBrush | 3D | oneshot | 2 | нет | bristle brush swish | ⬜ |
| item.blowtorch.ignite | паяльная лампа: поджиг | нет кода | VanItem_Blowtorch | 3D сеть | oneshot | 2 | средне | blowtorch pump and ignition pop | ⬜ |
| item.blowtorch.burn | паяльная лампа горит | нет кода | VanItem_Blowtorch | 3D сеть | loop | 1 | средне | blowtorch roaring flame | ⬜ |
| item.worklamp.switch | переносная лампа: вкл и выкл | нет кода | VanItem_WorkLamp | 3D | oneshot | 2 | нет | rubber toggle switch click | ⬜ |
| item.chocks.thunk | колодки, заглушки, чехлы самолёта | нет кода | KitCarry_Chocks, ChocksTail, IntakePlug, PitotCover, ExhaustCover, HeaterStowed | 3D сеть | oneshot | 3 | тихо | rubber chock dropped, dull thunk | ⬜ |
| item.doll.handle | кукла: мягкая, взять | нет кода | Item_Doll | 3D | oneshot | 2 | нет | rag doll lifted, soft fabric | ⬜ |
| item.doll.headturn | кукла поворачивает голову (конец демо) | нет кода (Doll_Head) | Item_Doll | 3D | oneshot | 1 | нет | slow creak of doll head turning | ⬜ |
| item.postcard | открытка: взять и перевернуть | нет кода | Item_Postcard | 2D | oneshot | 2 | нет | stiff card flipped | ⬜ |
| item.cassette | кассета: взять и уронить (предлож.) | нет кода (задания С06, С11, Р7) | кассеты (модели нет) | 3D | oneshot | 2 | нет | audio cassette clatter in case | ⬜ |
| item.cart.roll | тележка катится | нет кода | Item_Cart | 3D сеть | loop | 2 | тихо | hand cart rolling on snow and gravel, wheels | ⬜ |
| item.cart.caster | поворотные колёса дребезжат | нет кода | Item_Cart | 3D сеть | oneshot | 3 | тихо | caster wheel rattle | ⬜ |
| item.cart.load | груз на платформу тележки | нет кода (Cargo_Cart) | Item_Cart | 3D сеть | oneshot | 2 | тихо | box set on plywood cart deck | ⬜ |
| item.sled.drag | волокуша по снегу | нет кода | Item_Sled | 3D сеть | loop | 2 | тихо | plastic sled dragged over snow hiss | ⬜ |
| item.sled.scrape | волокуша по твёрдому | нет кода | Item_Sled | 3D сеть | loop | 1 | средне | plastic sled scraping gravel | ⬜ |
| item.sled.rope | взять, бросить буксирную верёвку | нет кода (Sled_Tow) | Item_Sled | 3D | oneshot | 2 | нет | tow rope tug, wooden handle knock | ⬜ |
| item.pump.putdown | насос-дозатор 45 кг поставить (вдвоём) | нет кода | Item_DosingPump | 3D сеть | oneshot | 2 | средне | heavy pump frame set down, clank and slosh | ⬜ |
| item.crt.putdown | кинескоп 14 кг поставить | нет кода | Item_RadarCRT | 3D сеть | oneshot | 2 | тихо | heavy glass tube set down, careful thunk | ⬜ |
| item.relay.putdown | блок ретранслятора поставить | нет кода | Item_RelayUnit | 3D сеть | oneshot | 2 | тихо | metal rack chassis set down | ⬜ |
| item.generator.putdown | резервный генератор 46 кг поставить | нет кода | Item_BackupGenerator | 3D сеть | oneshot | 2 | средне | heavy generator frame dropped, clank | ⬜ |
| item.generator.pull | генератор: рывок шнура стартера | нет кода (Gen_StartGrip) | Item_BackupGenerator | 3D сеть | oneshot | 3 | средне | recoil starter rope pull | ⬜ |
| item.ladder.fold | стремянка складывается и раскладывается | StepLadderItem.Update (задние ноги ±38°) | StepLadder_Net | 3D сеть | oneshot | 2 | тихо | aluminium step ladder folding clack | ⬜ |
| cargo.secure | закрепить груз ремнём (R) | нет кода (CargoItem.ServerSecure) | груз на точках крепления (самолёт) | 3D сеть | oneshot | 3 | тихо | ratchet strap tightening clicks | ⬜ |
| cargo.unsecure | отцепить ремень | нет кода (ServerUnsecure) | там же | 3D сеть | oneshot | 2 | тихо | strap buckle released | ⬜ |
| cargo.strap.snap | крепление рвётся | нет кода (CargoItem.OnJointBreak) | там же | 3D сеть | oneshot | 2 | средне | cargo strap snapping, buckle ping | ⬜ |
| cargo.stow | вещь встала на своё место (кронштейн) | нет кода (CargoItem.ServerStow) | StowPoint самолёта | 3D сеть | oneshot | 2 | тихо | item clicks into bracket | ⬜ |
| cargo.unstow | снять вещь с места | нет кода (ServerUnstow) | там же | 3D сеть | oneshot | 2 | тихо | item pulled from clip | ⬜ |

#### 4. Ручная рация и служебные звуки эфира
| id | what | when | where | 3D/2D | oneshot/loop | variants | шум | prompt | status |
|---|---|---|---|---|---|---|---|---|---|
| radio.click | щелчок начала передачи (чужая тангента) | VoiceEmitter.Play | Leitstand_RadioSet_1; потом Item_Walkie | 3D | oneshot | 3 | тихо | radio PTT key-up click with brief noise burst | 🔇 |
| radio.squelch | хвост шумодава в конце передачи | VoiceEmitter.End / Cut | там же | 3D | oneshot | 3 | тихо | radio squelch tail, short noise burst | 🔇 |
| radio.static | шум эфира под голосом и в паузах | VoiceEmitter.Awake (петля) | там же | 3D | loop | 1 | тихо | AM radio static hiss, steady | 🔇 |
| radio.static.strong | сильный шум, слабый сигнал (метель) | нет кода (одна петля radio.static) | там же | 3D | loop | 1 | тихо | heavy crackling radio interference | ⬜ |
| radio.dropout | выпадения сигнала 50–200 мс | нет кода | там же | 3D | oneshot | 5 | нет | radio signal dropout, chopped noise | ⬜ |
| radio.interference | помеха, вой гетеродина | нет кода | там же | 3D | oneshot | 3 | тихо | radio heterodyne whine and squeal | ⬜ |
| radio.ptt.press | своя тангента нажата | нет кода (Walkie_PTT) | Item_Walkie, VanItem_RadioMic | 3D сеть | oneshot | 3 | тихо | rubber PTT button press click | ⬜ |
| radio.ptt.release | своя тангента отпущена (+подтверждающий писк) | нет кода | там же | 3D сеть | oneshot | 3 | тихо | PTT release click, short roger beep | ⬜ |
| radio.power.on | включение: щелчок ручки громкости + писк | нет кода (Walkie_Knob_Volume, первые 30°) | Item_Walkie | 3D | oneshot | 2 | тихо | knob switch click, radio power-on chirp and hiss | ⬜ |
| radio.power.off | выключение | нет кода | Item_Walkie | 3D | oneshot | 2 | нет | knob click off, hiss cut | ⬜ |
| radio.volume.detent | ручка громкости поворачивается | нет кода | Item_Walkie | 3D | oneshot | 3 | нет | small knob turning, soft ratchet | ⬜ |
| radio.channel.click | щелчок ручки канала (16 положений) | нет кода (Walkie_Knob_Channel) | Item_Walkie | 3D | oneshot | 4 | нет | rotary channel selector detent click | ⬜ |
| radio.channel.beep | писк смены канала | нет кода | Item_Walkie | 3D | oneshot | 2 | нет | short digital channel beep | ⬜ |
| radio.call | вызывной тон (CALL) | нет кода | Item_Walkie | 3D сеть | oneshot | 2 | средне | walkie-talkie call tone burst | ⬜ |
| radio.scan | сканирование каналов | нет кода | Item_Walkie | 3D | loop | 1 | тихо | radio scanning, bursts of noise and beeps | ⬜ |
| radio.button | кнопки LAMP и LOCK | нет кода | Item_Walkie | 3D | oneshot | 2 | нет | tiny rubber button click beep | ⬜ |
| radio.battery.low | батарейка садится, писк | нет кода (VISION) | Item_Walkie | 3D | oneshot | 2 | тихо | low battery warning beeps | ⬜ |
| radio.feedback | вой двух раций рядом (предлож.) | нет кода | Item_Walkie | 3D | oneshot | 2 | средне | radio feedback squeal | ⬜ |
| radio.clip | рация на клипсе ремня | нет кода | Item_Walkie | 3D | oneshot | 2 | нет | plastic belt clip snap | ⬜ |

#### 5. Интерфейс
| id | what | when | where | 3D/2D | oneshot/loop | variants | шум | prompt | status |
|---|---|---|---|---|---|---|---|---|---|
| ui.pager.beep | пейджер: новое сообщение | Pager (~143, режим «звук») | Item_Pager / SnowPager | 2D | oneshot | 2 | нет | 90s pager beeping | ✅ |
| ui.letter.key | удар машинки при печати сообщения | Pager (~124) | экран, слева вверху | 2D | oneshot | 8 | нет | typewriter key strike | ✅ |
| ui.letter.return | возврат каретки или звонок в конце сообщения (предлож.) | нет кода | Pager | 2D | oneshot | 2 | нет | typewriter carriage bell ding | ⬜ |
| ui.pager.raise | поднять пейджер (P) | Pager.Raise (сейчас letter.key ×0,8) | Pager | 2D | oneshot | 2 | нет | plastic pager unclipped from belt | ⬜ |
| ui.pager.lower | опустить пейджер | Pager.Raise (сейчас letter.key ×0,7) | Pager | 2D | oneshot | 2 | нет | pager clipped back on belt | ⬜ |
| ui.pager.button | кнопки ▲ ▼ ■, листание | Pager (~248, сейчас letter.key ×1,15) | Pager | 2D | oneshot | 3 | нет | tiny rubber pager button click | ⬜ |
| ui.pager.open | открыть сообщение (E) | нет кода | Pager | 2D | oneshot | 2 | нет | pager select beep | ⬜ |
| ui.pager.mode | смена режима звук / вибро / тихо (держать E) | нет кода | Pager | 2D | oneshot | 3 | нет | pager mode toggle beep | ⬜ |
| ui.pager.vibrate | жужжание вибрации | Pager (~144, сейчас только тряска камеры) | Pager | 2D | oneshot | 2 | нет | pager vibrating buzz | ⬜ |
| ui.task.new | новое задание или новая запись в журнале | нет кода (TaskHud.OnGiven) | TaskHud | 2D | oneshot | 2 | нет | soft paper and pen tick | ⬜ |
| ui.task.done | задание выполнено | нет кода (TaskHud.OnCompleted) | TaskHud | 2D | oneshot | 2 | нет | pen check mark on paper | ⬜ |
| ui.journal.open | открыть журнал J | нет кода (TaskHud, клавиша J) | TaskHud | 2D | oneshot | 2 | нет | notebook opened | ⬜ |
| ui.journal.close | закрыть журнал | нет кода | TaskHud | 2D | oneshot | 2 | нет | notebook closed | ⬜ |
| ui.journal.scroll | листать журнал | нет кода | TaskHud | 2D | oneshot | 3 | нет | page turn | ⬜ |
| ui.hint | подсказка внизу экрана | нет кода (TaskBoard.Hinted → OnHinted) | TaskHud | 2D | oneshot | 2 | нет | subtle soft notification tick | ⬜ |
| ui.hover | навели на предмет (по желанию) | нет кода (InteractionOverlay.Describe) | InteractionOverlay | 2D | oneshot | 3 | нет | very faint soft tick | ⬜ |
| ui.confirm | действие принято | нет кода (InteractionSensor) | все интерактивы | 2D | oneshot | 3 | нет | soft confirm click | ⬜ |
| ui.reveal | Tab: показать, что рядом | нет кода (InteractionOverlay.RevealList) | InteractionOverlay | 2D | oneshot | 1 | нет | low soft whoosh | ⬜ |
| ui.reply.open | меню ответа по рации (T) | нет кода (VoiceHud, ReplyOffered) | Item_RadioSet | 2D | oneshot | 1 | нет | radio panel click | ⬜ |
| ui.reply.select | выбрать ответ 1–9 | нет кода (VoiceNet.Choose) | там же | 2D | oneshot | 2 | нет | select click | ⬜ |
| ui.reply.close | закрыть меню ответа | нет кода | там же | 2D | oneshot | 1 | нет | soft close click | ⬜ |
| ui.ping | метка лучом (держать C) | нет кода (VoiceHud.BodyHeard, Ping) | все | 2D | oneshot | 2 | нет | faint soft marker blip | ⬜ |
| ui.heard | значок «меня слышно» (по желанию) | нет кода (NoiseEvents.LocalHeardUntil) | все | 2D | oneshot | 1 | нет | muted low thump | ⬜ |
| ui.menu.crt.on | меню старого ПК: включение ЭЛТ, размагничивание | нет кода (меню нет) | главное меню | 2D | oneshot | 1 | нет | CRT monitor power on, degauss thunk and whine | ⬜ |
| ui.menu.crt.hum | гул ЭЛТ | нет кода | меню | 2D | loop | 1 | нет | CRT high-pitched hum, monitor buzz | ⬜ |
| ui.menu.boot | загрузка ПК, жёсткий диск | нет кода | меню | 2D | oneshot | 1 | нет | 90s PC boot, hard drive seek chatter | ⬜ |
| ui.menu.key | клавиатура | нет кода | меню | 2D | oneshot | 6 | нет | old mechanical keyboard key | ⬜ |
| ui.menu.click | щелчок мыши | нет кода | меню | 2D | oneshot | 3 | нет | ball mouse click | ⬜ |
| ui.menu.back | назад, отмена | нет кода | меню | 2D | oneshot | 2 | нет | soft lower click | ⬜ |
| ui.volume.tick | ползунок громкости | нет кода (будущее меню звука F2) | меню, F2 | 2D | oneshot | 2 | нет | small tick | ⬜ |
| ui.dev.toggle | пульт ~ открыть и закрыть (по желанию) | нет кода (DevPult) | DevPult | 2D | oneshot | 1 | нет | short click | ⬜ |
| ui.demo.end | конец демо: открытка | нет кода | Item_Postcard | 2D | oneshot | 1 | нет | card flipped, quiet | ⬜ |

#### 6. Музыка (только список)
| id | what | when | where | 3D/2D | oneshot/loop | variants | шум | prompt | status |
|---|---|---|---|---|---|---|---|---|---|
| music.menu | тема меню | нет кода | меню | 2D | loop | 1 | нет | sparse cold analog synth drone, lonely 1980s, slow | ⬜ |
| music.base.calm | тихий эмбиент на базе в спокойное время, не навязчивый | нет кода | база | 2D | loop | 2 | нет | quiet warm ambient pad with faint unease | ⬜ |
| music.danger.stem1 | слой опасности 1: глухой дрон | нет кода (одно число «опасность», как в Dead Space) | везде | 2D | loop | 1 | нет | low dark drone, subtle | ⬜ |
| music.danger.stem2 | слой 2: пульс | нет кода | везде | 2D | loop | 1 | нет | slow low pulse, sync 60 bpm | ⬜ |
| music.danger.stem3 | слой 3: напряжение | нет кода | везде | 2D | loop | 1 | нет | tense scraped strings and metal | ⬜ |
| music.danger.stem4 | слой 4: пик | нет кода | везде | 2D | loop | 1 | нет | dissonant high clusters, intense | ⬜ |
| music.radio.songs | песни и передачи в приёмнике (если решим) | нет кода | Item_Radio | 3D | loop | n | тихо | 1980s German schlager on AM radio | ⬜ |
| music.demo.end | финал демо, титры | нет кода | конец демо | 2D | oneshot | 1 | нет | melancholic music box over drone | ⬜ |

#### Неясности
- **Ошибка в ударах.** `ItemImpactSound` подбирает материал по первому совпавшему слову в порядке списка. В `item_toolbox` и `vanitem_toolbox` есть «box», поэтому ящики с инструментами звучат как бумага. Мягкие и пластиковые вещи (одежда, тряпка, мешки, рюкзак, ботинки, коврик, конверт, фото) звучат как металл.
- **Нет снега на террейне.** `SoundLibrary` уже умеет Snow и Frozen, но слоя снега у террейна нет, а вне зданий по умолчанию стоит Forest. Новые поверхности SnowDeep, Ice, Asphalt, Concrete, Linoleum, Rock, Grating, Carpet — мои предложения. Каждой нужны запись в `steps`, а также слой террейна или ключевое слово.
- **Присяд и прыжок.** Тихий шаг сейчас — тот же walk на громкости 0,4, существа его не слышат. Нужен ли отдельный набор шагов в присяде, решать пользователю. Звука толчка при прыжке в коде нет. Сейчас приземление звучит дважды: шаг `Footsteps.Land` и, при скорости от 7,5 м/с, ещё кряхтение `BodyVoice`.
- **Звуки тела с enum без триггера.** PainSmall, EffortLight, Cough, Shiver, Breath и Wake уже есть в enum, сеть и воспроизведение готовы (`vo_body_<имя>_1…8` в `Resources/Voice/`), но их никто не вызывает. Чих, зевок, еда, питьё и смерть в enum нет — их надо дописывать в конец (порядок enum — это формат сети).
- **Рация.** Код просит только три служебных звука: `radio.static` (одна петля), `radio.click` и `radio.squelch`. Модели `Item_Walkie` в Unity ещё нет — нет ни FBX, ни префаба, — поэтому ручки, тангента и кнопки взяты из `GEAR_CONTRACT.md`. Рация на пульте (`Item_RadioSet`) — переносной объект весом 6 кг, у неё те же служебные звуки.
- **Карманы.** Сейчас это только 2D-звуки интерфейса для владельца. Звуков «взять» и «положить» в мире, которые слышат другие (`item.pickup.*`, `item.putdown.*`), в коде нет. Добавлять ли их, решает пользователь.
- **Предметы фургона и самолёта** (VanItem_*, KitCarry_*, Surv_*) внесены как переносные вещи, по материалу. Установку в машину и действия транспорта я не брал. Тестовые ящики стенда Cargo_Case1–6 опущены.
- **Граница с устройствами.** Генератор `Item_BackupGenerator`, натриевая лампа, кинескоп и блок ретранслятора пока без префабов. Их установка на место — это работа устройств; работу генератора (мотор) и вкручивание натриевой лампы я не включал.
- **Глушение мира в стадии «холодно».** Это снапшот микшера, а не звук (`QUESTIONS_4` №15: да). Иней по краям экрана (`ScreenFrost`) пока выключен (`Enabled = false`), шкалы холода нет — все звуки холода ждут систему холода.
- **Музыка.** По `QUESTIONS_4` №17–19: меню, тихий эмбиент на базе, слои опасности как в Dead Space, делает ИИ. Музыкального кода и шины Music пока нет. Песни в приёмнике (A204) и финальная тема — только если решат делать.

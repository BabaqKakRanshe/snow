# Покрытие игры звуками

Генерирует `tools/ai_audio/sound_coverage.py` (не править руками). Звук «подключён», если его вызывает код или `Snow_LocationSounds.json` (детали, свет, сеть, гул приборов локаций).

| | звуков |
|---|---|
| в библиотеке | 1074 |
| подключено | 812 |
| ждут системы, которой нет | 262 |
| есть крючок, не подключено | 0 |

## Ждут систему

**устройства базы без взаимодействия** — 77: `base.gen.glow.tick`, `base.gen.breaker.trip`, `base.yard.pump.dry`, `base.yard.tank1.hit`, `base.yard.tank2.hit`, `base.yard.windsock`, `base.yard.hatch.padlock`, `base.yard.dropbox.flap`, `base.yard.dropbox.drop`, `base.lamp.replace`, `base.furn.bunk.lie`, `base.furn.bunk.getup`, `base.furn.safe`, `base.furn.chair.scrape`, `base.water.tap`, `base.water.wc`, `base.water.shower`, `base.kitchen.coffee.brew`, `base.kitchen.microwave.run`, `base.kitchen.microwave.ding`, `base.kitchen.microwave.door`, `base.kitchen.tv.on`, `base.kitchen.tv.static`, `base.pc.power.on`, `base.pc.keys`, `base.pc.mouse`, `base.pc.floppy`, `base.pc.mail`, `base.pc.crt.on`, `base.pc.morse`, `base.printer.print`, `base.printer.tear`, `base.fax.handset.up`, `base.fax.handset.down`, `base.fax.tone`, `base.fax.key`, `base.fax.feed`, `base.fax.handshake`, `base.fax.ring`, `base.fax.print`, `base.fax.error`, `base.radio.power`, `base.radio.click`, `base.radio.squelch`, `base.radio.tune`, `base.phone.ring`, `base.phone.red.ring`, `base.phone.handset`, `base.speaker.on`, `base.speaker.gong`, `base.alarm.siren`, `base.alarm.siren.mute`, `base.alarm.beacon`, `base.alarm.firebell`, `base.alarm.zone`, `base.alarm.callpoint`, `base.ant.drive.hum`, `base.tape.power`, `base.tape.door`, `base.tape.insert`, `base.tape.eject`, `base.tape.key`, `base.tape.run`, `base.tape.wind`, `base.tape.end`, `base.meteo.door.open`, `base.meteo.door.close`, `base.meteo.cups`, `base.meteo.laptop.lid`, `base.meteo.laptop.boot`, `base.pumphouse.pump`, `base.pumphouse.switch`, `base.pumphouse.dosing.valve`, `base.pumphouse.dosing.switch`, `base.pumphouse.dosing.run`, `base.pumphouse.dosing.bleed`, `base.board.nail`

**монстры и события ужаса (кода существ и режиссёра нет)** — 61: `kpp.roof.steps`, `kpp.knock.wall`, `kpp.knock.glass`, `kpp.knock.door`, `kpp.knock.scratch`, `hut.roof.steps`, `hut.knock.wood`, `hut.knock.scratch`, `giant.hand.rise`, `x04.lights.dim`, `x02.crossing`, `x03.backseat`, `mon.holodny.joint`, `mon.holodny.exhale`, `mon.holodny.step.snow`, `mon.holodny.step.hard`, `mon.holodny.rise`, `mon.holodny.retreat`, `mon.holodny.warn`, `mon.holodny.grab`, `mon.holodny.drag`, `mon.holodny.drop`, `mon.holodny.window`, `mon.holodny.glass`, `mon.holodny.climb`, `mon.holodny.roof`, `mon.lesnik.neck`, `mon.lesnik.neck.snap`, `mon.lesnik.mouth`, `mon.lesnik.flee`, `mon.lesnik.cover`, `mon.lesnik.loot`, `mon.lesnik.wake`, `mon.olen.hoof`, `mon.olen.breath`, `mon.olen.hit`, `mon.kukla.move`, `mon.kukla.head`, `mon.nevesomye.strap`, `mon.nevesomye.snap`, `ent.door.drum`, `ent.lamp.pop`, `ent.tube.pop`, `ent.frozen.creak`, `ent.ice.shadow`, `ent.halluc.steps`, `ent.halluc.whisper`, `e01.gen.strangle`, `b01.gen.cough`, `base.gen.diesel.killed`, `base.gen.diesel.cough`, `base.yard.hatch.knockbelow`, `base.shutter.scratch`, `base.door.knock.fists`, `base.gate.upover.rattle`, `base.knock.glass`, `base.knock.container`, `base.window.hit`, `base.window.shatter`, `base.window.draft`, `van.knock`

**предметы мира без взаимодействия или вне карты** — 31: `kpp.booth.chair`, `kpp.phone.hangup`, `kpp.phone.dial`, `kpp.phone.dialtone`, `start.booth.door.open`, `start.booth.door.close`, `start.battery.lid`, `start.car.door.open`, `start.car.door.close`, `start.trash.rustle`, `hut.window.tap`, `hut.shutter.move`, `hut.shutter.latch`, `hut.stove.door`, `hut.stove.damper`, `hut.stove.fire.loop`, `hut.radio`, `hut.outhouse.door.open`, `hut.outhouse.door.close`, `hut.outhouse.latch`, `road.overlook.scope`, `fence.climb`, `world.firebarrel.loop`, `world.firebarrel.embers`, `world.firebarrel.feed`, `world.firebarrel.ignite`, `world.car.door`, `world.wreck.door`, `world.wreck.boot`, `forest.river`, `music.radio.songs`

**предметы без своего действия (держать можно, использовать нечем)** — 30: `item.flashlight.dying`, `item.flashlight.battery`, `item.lantern.switch`, `item.lantern.door`, `item.battery6v.insert`, `item.headlamp.twist`, `item.headlamp.tilt`, `item.shovel.dig`, `item.shovel.toss`, `item.iceaxe.swing`, `item.iceaxe.chip`, `item.padlock.open`, `item.jerrycan.spill`, `item.jerrycan.fill`, `item.bucket.splash`, `item.kettle.pour`, `item.thermos.open`, `item.thermos.pour`, `item.bulb.screw`, `item.snowbrush`, `item.blowtorch.ignite`, `item.blowtorch.burn`, `item.worklamp.switch`, `item.doll.headturn`, `item.cart.caster`, `item.cart.load`, `item.sled.scrape`, `item.sled.rope`, `item.generator.pull`, `item.ladder.fold`

**бункер: двери, люк, лампы и брезент — простые объекты (правила звука в `Snow_LocationSounds.json` готовы, заиграют, когда задача интерактивности сделает их частями хоста)** — 15: `base.bunker.gate.slide`, `base.bunker.gate.stop`, `base.bunker.sidedoor.open`, `base.bunker.sidedoor.close`, `base.bunker.panel.key`, `base.bunker.panel.open`, `base.bunker.hydraulic`, `base.bunker.bolt`, `base.bunker.wheel`, `base.bunker.blast.swing`, `base.bunker.hatch.wheel`, `base.bunker.hatch.open`, `base.bunker.hatch.close`, `base.bunker.tarp`, `base.bunker.lamps.on`

**фургон: зоны «пока не работает» или серверные данные** — 11: `van.jerrycan.take`, `van.engine.boil`, `van.distributor.cap`, `van.winch.unspool`, `van.winch.motor`, `van.winch.hook`, `van.tyre.chains`, `van.wheel.nut`, `van.wheel.offon`, `van.jack.place`, `van.jack.pump`

**ручная рация как предмет (ручек, каналов и батареи в коде нет)** — 10: `radio.power.on`, `radio.power.off`, `radio.volume.detent`, `radio.channel.click`, `radio.channel.beep`, `radio.scan`, `radio.button`, `radio.battery.low`, `radio.feedback`, `radio.clip`

**меню и конец демо (нет)** — 10: `ui.menu.crt.on`, `ui.menu.crt.hum`, `ui.menu.boot`, `ui.menu.key`, `ui.menu.click`, `ui.menu.back`, `ui.volume.tick`, `ui.demo.end`, `music.menu`, `music.demo.end`

**самолёт: нет реплицированных данных или системы** — 6: `plane.generator.online`, `plane.lever.power`, `plane.trim.wheel`, `plane.trim.neutral`, `plane.jerrycan.slosh`, `plane.elt`

**звуки тела без триггера (холода, еды, сна в коде нет)** — 5: `body.faint`, `body.drink.hot`, `body.eat`, `body.stomach`, `body.dragged.scream`

**погода (EnvironmentProvider в лесу не стоит)** — 3: `amb.weather.snowfall`, `amb.weather.fog`, `amb.weather.aurora`

**не нужен: дубль подключённого звука (`base.ant.cabinet.*`, `far.dish.drive`, `far.meteo.cups`, `far.pump.hum`, `bed.*`, `item.jerrycan.slosh`, `kpp.padlock.unlock`, `plane.ladder.fold`) или у каждого действия свой звук** — 2: `far.dish.cabinet`, `ui.confirm`

**дверной звонок: сценария нет** — 1: `base.door.doorbell`

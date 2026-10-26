"""Which sound cues of the library the game actually calls, and why the rest wait.

    python tools/ai_audio/sound_coverage.py        -> docs/gdd/SOUND_COVERAGE.md

A cue counts as called when its id appears in C# code or in Snow_LocationSounds.json, or matches a pattern the code
builds at run time (van.door.<kind>.<open|close>, step.<surface>.<gait>, …). The rest are grouped by the missing system
that would play them (WAITING below); a cue in no group is listed as "to wire" — a hook exists, nobody calls it yet.
"""
import collections
import json
import re
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
MANIFEST = REPO / "tools/audio/audio_manifest.json"
RULES = REPO / "Snow/Assets/Snow/Audio/Resources/Audio/Snow_LocationSounds.json"
OUT = REPO / "docs/gdd/SOUND_COVERAGE.md"
SURFACES = ["forest", "snow", "snowdeep", "frozen", "ice", "gravel", "rock", "wood", "tile", "linoleum", "concrete",
            "asphalt", "metal", "grating", "carpet", "dirt", "grass", "leaves", "mud"]

# prefix -> the system that does not exist yet (the cue is generated and waits in the library)
WAITING = [
    (("mon.", "ent.", "e01.", "b01.", "x02.", "x03.", "x04.", "giant.hand", "kpp.knock", "kpp.roof.steps", "hut.knock",
      "hut.roof.steps", "base.door.knock", "base.knock", "base.gate.upover.rattle", "base.shutter.scratch", "van.knock",
      "base.gen.diesel.killed", "base.gen.diesel.cough", "base.window", "base.yard.hatch.knockbelow"), "монстры и события ужаса (кода существ и режиссёра нет)"),
    (("amb.weather", "amb.wind.ridge", "kpp.line.crackle", "fence.barbed", "fence.oldfence"), "погода (EnvironmentProvider в лесу не стоит)"),
    (("base.bunker", "base.room.bunker"), "бункер: двери, люк, лампы и брезент — простые объекты (правила звука в "
     "`Snow_LocationSounds.json` готовы, заиграют, когда задача интерактивности сделает их частями хоста)"),
    (("base.fax", "base.pc", "base.printer", "base.tape", "base.meteo", "base.radio", "base.phone", "base.speaker",
      "base.alarm", "base.leit", "base.kitchen", "base.water", "base.clock", "base.board", "base.furn.safe",
      "base.furn.chair", "base.furn.fridge.click", "base.furn.bunk", "base.lamp.replace", "base.pumphouse", "base.yard",
      "base.ant.cabinet.hum", "base.ant.drive.hum", "base.gen.glow", "base.gen.breaker.trip", "base.roof.deflector"), "устройства базы без взаимодействия"),
    (("ui.menu", "ui.volume", "music.menu", "ui.demo", "music.demo"), "меню и конец демо (нет)"),
    (("radio.",), "ручная рация как предмет (ручек, каналов и батареи в коде нет)"),
    (("item.",), "предметы без своего действия (держать можно, использовать нечем)"),
    (("world.", "start.booth", "start.car", "start.battery", "start.trash", "hut.stove", "hut.shutter", "hut.outhouse.door",
      "hut.outhouse.latch", "hut.window.tap", "hut.radio", "music.radio", "road.zonegate.shake", "road.overlook.scope",
      "road.bridge.planks", "fence.wire", "fence.hedgehog", "fence.concrete", "fence.climb", "kpp.booth.chair",
      "kpp.barrier.hit", "kpp.phone.hangup", "kpp.phone.dial", "kpp.phone.dialtone", "forest.river"), "предметы мира без взаимодействия или вне карты"),
    (("van.winch", "van.wheel", "van.jack", "van.distributor", "van.jerrycan", "van.tyre.spin", "van.tyre.skid",
      "van.tyre.chains", "van.transfer.grind", "van.engine.boil"), "фургон: зоны «пока не работает» или серверные данные"),
    (("plane.generator", "plane.lever.power", "plane.trim", "plane.fuel.pour.end", "plane.jerrycan", "plane.gear.skid",
      "plane.brake", "plane.turbulence", "plane.tiedownbag", "plane.ladder.creak", "plane.elt", "plane.door.warning"), "самолёт: нет реплицированных данных или системы"),
    (("body.", "cold.", "ragdoll.limb"), "звуки тела без триггера (холода, еды, сна в коде нет)"),
    (("ui.letter", "ui.pager"), "пейджер (файл сессии snow-01)"),
    (("far.dish.cabinet", "ui.confirm", "base.ant.drive.hum", "base.meteo.cups", "base.pumphouse.pump", "base.furn.bunk",
      "plane.jerrycan.slosh", "item.padlock.open", "item.ladder.fold", "base.radio.static"),
     "не нужен: дубль подключённого звука (`base.ant.cabinet.*`, `far.dish.drive`, `far.meteo.cups`, `far.pump.hum`, `bed.*`, "
     "`item.jerrycan.slosh`, `kpp.padlock.unlock`, `plane.ladder.fold`) или у каждого действия свой звук"),
    (("base.door.doorbell",), "дверной звонок: сценария нет"),
]


# rules in Snow_LocationSounds.json for parts that are not host parts yet: referenced there, still silent in the game
NOT_LIVE = ("base.bunker.",)


def called(text):
    used = set(re.findall(r'"([a-z0-9_]+(?:\.[a-z0-9_]+)+)"', text))
    used |= {f"van.door.{k}.{a}" for k in ("cab", "side", "rear") for a in ("open", "close")}
    used |= {f"plane.door.{k}.{a}" for k in ("cabin", "cargo") for a in ("open", "close")}
    used |= {f"plane.kit.{k}.{a}" for k in ("intake", "exhaust", "pitot", "wingcover", "windshield", "enginecover", "chocks", "tiedown") for a in ("on", "off")}
    used |= {f"step.{s}.{g}" for s in SURFACES for g in ("walk", "run", "land", "crouch", "jump")}
    used |= {f"item.pickup.{m}" for m in ("metal", "glass", "paper", "cloth", "plastic")}
    used |= {f"item.putdown.{m}" for m in ("heavy.wood", "heavy.metal", "box", "sack")}
    used |= {f"van.tyre.{m}" for m in ("packedsnow", "ice", "loosesnow", "road", "sandtrack")}
    return used


def main():
    cues = [c["id"] for c in json.loads(MANIFEST.read_text(encoding="utf-8"))["cues"]]
    text = "".join(p.read_text(encoding="utf-8", errors="ignore") for p in (REPO / "Snow/Assets").rglob("*.cs"))
    rules = RULES.read_text(encoding="utf-8")
    used = called(text + rules)
    used -= {c for c in called(rules) - called(text) if c.startswith(NOT_LIVE)}
    waiting, wire = collections.defaultdict(list), []
    for c in cues:
        if c in used:
            continue
        for prefixes, why in WAITING:
            if c.startswith(prefixes):
                waiting[why].append(c)
                break
        else:
            wire.append(c)
    n_called = sum(1 for c in cues if c in used)
    lines = ["# Покрытие игры звуками", "",
             "Генерирует `tools/ai_audio/sound_coverage.py` (не править руками). Звук «подключён», если его вызывает код или "
             "`Snow_LocationSounds.json` (детали, свет, сеть, гул приборов локаций).", "",
             f"| | звуков |", "|---|---|", f"| в библиотеке | {len(cues)} |", f"| подключено | {n_called} |",
             f"| ждут системы, которой нет | {sum(len(v) for v in waiting.values())} |", f"| есть крючок, не подключено | {len(wire)} |", ""]
    if wire:
        lines += ["## Не подключено", "", ", ".join(f"`{c}`" for c in wire), ""]
    lines += ["## Ждут систему", ""]
    for why, ids in sorted(waiting.items(), key=lambda kv: -len(kv[1])):
        lines += [f"**{why}** — {len(ids)}: " + ", ".join(f"`{c}`" for c in ids), ""]
    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"{len(cues)} cues, {n_called} called, {sum(len(v) for v in waiting.values())} waiting, {len(wire)} to wire -> {OUT}")
    if wire:
        print("to wire:", ", ".join(wire))


if __name__ == "__main__":
    main()

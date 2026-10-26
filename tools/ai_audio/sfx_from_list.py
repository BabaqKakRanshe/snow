"""Turn docs/gdd/SOUND_LIST_FULL.md into generation entries (sfx_prompts.json) and
library cues (tools/audio/audio_manifest.json), so every sound of the list gets
generated, built and installed by the usual tools.

    python tools/ai_audio/sfx_from_list.py          # add what is missing (existing entries stay as they are)

Rules (user 26.10.2026: "cover the whole game with sounds"):
- a ✅ row keeps its cue; a row "похоже на / подойдёт ✅ X" becomes an alias (aliases in sfx_prompts.json),
  not a new sound;
- footsteps walk/run on every surface: long walking takes sliced into single steps (the approach the user liked),
  on Stable Audio Medium; existing step cues are re-rendered that way too;
- body sounds (cough, breath, pain) keep voice-like content: no "voice" in their negative prompt;
- music rows use the music model;
- duration from the row's type and words; takes = needed variants + 2 to choose from.
"""
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).parent
LIST = HERE.parents[1] / "docs" / "gdd" / "SOUND_LIST_FULL.md"
PROMPTS = HERE / "sfx_prompts.json"
MANIFEST = HERE.parent / "audio" / "audio_manifest.json"
COLS = ["id", "what", "when", "where", "dim", "type", "variants", "noise", "prompt", "status"]
NOISE_M = {"тихо": 5, "средне": 15, "громко": 30}
SURFACE_WORDS = {
    "snow": "fresh dry snow, boots crunching", "frozen": "frozen gravel and ice crust, crunchy crackle",
    "gravel": "loose gravel, crunch", "wood": "old wooden floorboards indoors, hollow knock and creak",
    "tile": "a tiled floor in a concrete corridor, hard clicks with short echo",
    "metal": "a steel grating floor, metallic clank", "dirt": "hard frozen dirt ground, dull scuff",
    "grass": "dry frosted grass, soft rustle", "leaves": "dry frozen fallen leaves, brittle crunch",
    "mud": "cold wet mud and slush, squelch", "forest": "frozen forest floor with pine needles and twigs",
    "linoleum": "worn linoleum floor indoors, soft squeak", "concrete": "a bare concrete floor, gritty scuff",
    "ice": "smooth lake ice, sharp cold clicks", "carpet": "an old carpet, soft muffled thud",
    "deepsnow": "deep powder snow, heavy plunging crunch", "asphalt": "frozen asphalt road, gritty tap",
}


def rows():
    out = []
    for line in LIST.read_text(encoding="utf-8").splitlines():
        if not line.startswith("| ") or line.startswith(("| id", "| **", "| Зона")) or re.match(r"\|\s*-", line):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) == len(COLS) and re.match(r"^[a-z0-9_]+(\.[a-z0-9_]+)+$", cells[0]):
            out.append(dict(zip(COLS, cells)))
    return out


def count(variants):
    v = variants.replace("–", "-")
    if "×" in v:
        nums = [int(n) for n in re.findall(r"\d+", v)[:2]]
        return max(1, nums[0] * nums[1]) if len(nums) == 2 else 3
    if "+" in v and re.match(r"^\d+\+\d+", v):
        a, b = re.findall(r"\d+", v)[:2]
        return int(a) + int(b)
    m = re.search(r"\d+", v)
    if m:
        return int(m.group())
    return 2 if "/" in v else 1


def duration(r, loop, bed):
    if r["id"].startswith("music."):
        return 90.0
    if bed:
        return 60.0
    if loop:
        return 20.0
    p = (r["prompt"] + " " + r["what"]).lower()
    if re.search(r"start(ing|s)? up|spool|shut ?down|winding down|cranking|catch(es|ing)|falling|collaps|dies|stalling|boil", p):
        return 6.0
    if re.search(r"creak|slow|long|groan|drag|unroll|rolling|pour|gurgl|breath", p):
        return 4.0
    if re.search(r"click|snap|tick|button|knob|switch|detent|beep|key press|clack|pop|tap|tink", p):
        return 1.2
    return 2.5


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    prompts = json.loads(PROMPTS.read_text(encoding="utf-8"))
    cues = prompts["cues"]
    aliases = prompts.setdefault("aliases", {})
    man_text = MANIFEST.read_text(encoding="utf-8")
    have = {c["id"] for c in json.loads(man_text)["cues"]}
    new_lines, added, alias_n = [], 0, 0

    for r in rows():
        cid, st = r["id"], r["status"]
        m = re.search(r"(похоже|подойд)[^✅]*✅\s*([a-z0-9_.]+)", st)
        if m:
            aliases[cid] = m.group(2)
            alias_n += 1
            continue
        step = re.match(r"^step\.([a-z]+)\.(walk|run)$", cid)
        if ("✅" in st and not step) or r["prompt"] in ("—", "-", "") or "не нужен" in st:
            continue
        loop = r["type"].startswith("loop")
        bed = loop and r["dim"].startswith("2D")
        n = count(r["variants"])
        entry = {"prompt": r["prompt"], "dur": duration(r, loop, bed), "from": "list"}
        if step:
            surface, gait = step.groups()
            words = SURFACE_WORDS.get(surface, surface.replace("_", " "))
            pace = "walking slowly, steady even pace" if gait == "walk" else "running fast, quick heavy strides"
            entry.update(prompt=f"one person {pace} on {words}, heavy winter boots, close microphone, quiet, no wind, no music, no voice",
                         dur=12, model="medium", takes=4, keep=8,
                         slice={"len": 0.55 if gait == "walk" else 0.4, "count": 8, "gap": 0.35 if gait == "walk" else 0.25},
                         negative="music, voice, wind")
        elif r["id"].startswith("music."):
            entry.update(model="music", takes=3, keep=1, negative="speech, voice")
        elif bed:
            entry.update(model="medium", takes=3, keep=1, proc={"start": 2, "len": entry["dur"] - 4})
        elif loop:
            entry.update(takes=3, keep=1, proc={"start": 1, "len": entry["dur"] - 2})
        else:
            keep = min(max(n, 1), 6)
            entry.update(takes=min(keep + 2, 8), keep=keep)
        if re.match(r"^(body|cold)\.", cid) or "breath" in r["prompt"].lower() or "cough" in r["prompt"].lower():
            entry["negative"] = "music, singing, words, speech"
        if cid not in cues or (step and cues[cid].get("from") != "list"):
            cues[cid] = {**cues.get(cid, {}), **entry}
            added += 1
        if cid in have:
            continue
        kind = "bed" if bed else "loop" if loop else "oneshot"
        s = {"volume": [0.75, 0.9], "pitch": [0.95, 1.05]}
        if cid.startswith(("ui.", "music.")):
            s["bus"] = "Ui" if cid.startswith("ui.") else "Music"
            s["pitch"] = [1, 1]
        elif bed or cid.startswith("amb."):
            s["bus"] = "Ambience"
            s["volume"] = [0.5, 0.5]
        noise = NOISE_M.get(r["noise"].split()[0] if r["noise"] else "", 0)
        if noise:
            s["noise"] = noise
        s["maxDistance"] = {0: 18, 5: 22, 15: 35, 30: 60}[noise] if not bed else 30
        line = {"id": cid, "type": kind, "src": [f"gen:{cid}/pick/*.wav"]}
        if "proc" in entry:
            line["proc"] = entry["proc"]
        line["set"] = s
        new_lines.append("    " + json.dumps(line, ensure_ascii=False).replace("{", "{ ").replace("}", " }")
                         .replace("[", "[").replace("{  ", "{ ").replace("  }", " }"))
        have.add(cid)

    # sfx_prompts.json: one cue per line, like the hand-made part
    lines = ['{', f' "note": {json.dumps(prompts["note"], ensure_ascii=False)},',
             f' "defaults": {json.dumps(prompts["defaults"])},',
             f' "aliases": {json.dumps(aliases, ensure_ascii=False)},', ' "cues": {']
    items = list(cues.items())
    for i, (k, v) in enumerate(items):
        lines.append(f'  {json.dumps(k)}: {json.dumps(v, ensure_ascii=False)}' + ("," if i < len(items) - 1 else ""))
    lines += [' }', '}', '']
    PROMPTS.write_text("\n".join(lines), encoding="utf-8")

    if new_lines:
        head, tail = man_text.rstrip().rsplit("]", 1)
        man_text = head.rstrip() + ",\n" + ",\n".join(new_lines) + "\n  ]" + tail + "\n"
        json.loads(man_text)
        MANIFEST.write_text(man_text, encoding="utf-8")
    print(f"{added} generation entries added/updated, {len(new_lines)} new library cues, {alias_n} aliases; "
          f"{len(cues)} cues to generate in total")


if __name__ == "__main__":
    main()

"""Generated replacements for the game's sound cues (tools/audio/audio_manifest.json).

    python tools/ai_audio/gen_sfx.py                 # render missing takes for every cue (Soundbox must run)
    python tools/ai_audio/gen_sfx.py --only step.    # cues starting with step.
    python tools/ai_audio/gen_sfx.py --pick os.branch=2,4,5 forest.knock=3
    python tools/ai_audio/gen_sfx.py --switch        # point the manifest at the generated picks

Prompts, take counts and defaults live in sfx_prompts.json. Takes go to
F:/ClaudeGames/snow_audio_gen/sfx_lib/<cue>/vNN.wav with one index.html for all
cues; the first `keep` takes are copied to <cue>/pick/ unless picked by hand.
--switch rewrites each cue's src to gen:<cue>/pick/*.wav (the old source is kept
in src_pack) and applies the prompt file's proc; then build and install with
tools/audio/build_audio.py as usual.
"""
import argparse
import html
import json
import re
import shutil
import sys
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).parent))
import snowgen  # noqa: E402

HERE = Path(__file__).parent
PROMPTS = HERE / "sfx_prompts.json"
MANIFEST = HERE.parent / "audio" / "audio_manifest.json"
LIB = snowgen.OUT_ROOT / "sfx_lib"
NEGATIVE = "music, melody, speech, voice, singing, talking"


def load():
    p = json.loads(PROMPTS.read_text(encoding="utf-8"))
    return {k: {**p["defaults"], **v} for k, v in p["cues"].items()}


def takes_of(folder):
    return sorted(folder.glob("v[0-9][0-9].wav"))


def render(cues, only):
    for cid, c in cues.items():
        if only and not cid.startswith(only):
            continue
        folder = LIB / cid
        folder.mkdir(parents=True, exist_ok=True)
        have = len(takes_of(folder))
        if have < c["takes"]:
            print(f"{cid}: {c['takes'] - have} takes", flush=True)
            ns = SimpleNamespace(kind="sfx", prompt=c["prompt"], takes=c["takes"] - have, duration=c["dur"],
                                 seed=None, negative=NEGATIVE, lyrics=None)
            new = snowgen.soundbox_takes(ns, snowgen.MODELS["sfx"][c["model"]], folder, have)
            index = folder / "takes.json"
            old = json.loads(index.read_text(encoding="utf-8")) if index.exists() else []
            for t in new:
                t["prompt"] = c["prompt"]
            index.write_text(json.dumps(old + new, ensure_ascii=False, indent=1), encoding="utf-8")
        pick = folder / "pick"
        if not pick.exists() or not any(pick.glob("*.wav")):
            set_pick(cid, list(range(1, c["keep"] + 1)))
        write_page(cues)


def set_pick(cid, numbers):
    folder = LIB / cid
    pick = folder / "pick"
    pick.mkdir(exist_ok=True)
    for old in pick.glob("*.wav"):
        old.unlink()
    for n in numbers:
        src = folder / f"v{n:02d}.wav"
        if not src.exists():
            sys.exit(f"{cid}: no take v{n:02d}")
        shutil.copyfile(src, pick / src.name)
    print(f"{cid}: picked {', '.join(f'v{n:02d}' for n in numbers)}")


def slice_hits(cid, c):
    """Cut the takes of a footstep-like cue into single hits and fill pick/ with them.

    A take asked for one footstep often holds two to ten. Each strong onset (peak
    within 50 ms above 30 % of the take's loudest) starts a hit that runs to just
    before the next onset or `len` seconds; hits much quieter than the cue's
    loudest are dropped. `count` hits are taken round-robin across takes, so they
    come from different takes; with `strongest`, one hit per take (landings)."""
    import numpy as np
    import librosa
    import soundfile as sf

    sl = c["slice"]
    per_take = []
    for t in takes_of(LIB / cid):
        y, sr = sf.read(str(t), dtype="float32")
        mono = y.mean(axis=1) if y.ndim > 1 else y
        env = librosa.onset.onset_strength(y=mono, sr=sr)
        onsets = list(librosa.onset.onset_detect(onset_envelope=env, sr=sr, units="samples", backtrack=True, delta=0.3))
        rms = librosa.feature.rms(y=mono, frame_length=512, hop_length=128)[0]

        def peak(a, b):
            return float(rms[a // 128:max(a // 128 + 1, b // 128)].max())

        strong = [o for o in onsets if peak(o, o + int(0.05 * sr)) > 0.3 * rms.max()] or [0]
        gap = int(sl.get("gap", 0.22) * sr)   # heel and toe of one step are two onsets: keep the first
        merged = []
        for o in strong:
            if not merged or o - merged[-1] >= gap:
                merged.append(o)
        strong = merged
        hits = []
        for i, o in enumerate(strong):
            a = max(0, o - int(0.01 * sr))
            nxt = strong[i + 1] - int(0.01 * sr) if i + 1 < len(strong) and not sl.get("strongest") else len(mono)
            b = min(nxt, a + int(sl["len"] * sr))
            if b - a > int(0.08 * sr):
                hits.append((peak(a, b), y[a:b], sr))
        if sl.get("strongest"):
            hits = sorted(hits, key=lambda h: -h[0])[:1]
        per_take.append(hits)
    loudest = max((h[0] for hits in per_take for h in hits), default=0)
    per_take = [[h for h in hits if h[0] > 0.35 * loudest] for hits in per_take]
    chosen = []
    while len(chosen) < sl["count"] and any(per_take):
        for hits in per_take:
            if hits and len(chosen) < sl["count"]:
                chosen.append(hits.pop(0))
    pick = LIB / cid / "pick"
    pick.mkdir(exist_ok=True)
    for old in pick.glob("*.wav"):
        old.unlink()
    for i, (_, seg, sr) in enumerate(chosen, 1):
        fade = min(len(seg) // 4, int(0.02 * sr))
        seg = seg.copy()
        seg[-fade:] *= np.linspace(1, 0, fade)[:, None] if seg.ndim > 1 else np.linspace(1, 0, fade)
        sf.write(str(pick / f"s{i:02d}.wav"), seg, sr)
    print(f"{cid}: {len(chosen)} single hits")


def write_page(cues):
    h = ["<!doctype html><meta charset='utf-8'><title>Snow SFX</title><style>body{font:15px system-ui;margin:24px;"
         "background:#111;color:#ddd}td{padding:5px 8px;vertical-align:top;border-top:1px solid #333}"
         "audio{height:28px;width:190px}.p{outline:2px solid #6c6}small{color:#999}</style>"
         "<h2>Snow — сгенерированные звуки</h2><p><small>зелёная рамка — в игре; поменять: gen_sfx.py --pick id=2,5</small></p><table>"]
    for cid, c in cues.items():
        folder = LIB / cid
        if not folder.exists():
            continue
        picked = {p.name for p in (folder / "pick").glob("*.wav")} if (folder / "pick").exists() else set()
        cells = "".join(f"<td{' class=p' if t.name in picked else ''}>{t.stem[1:]}<br>"
                        f"<audio controls preload='none' src='{cid}/{t.name}'></audio></td>" for t in takes_of(folder))
        sliced = sorted(n for n in picked if n.startswith("s"))
        if sliced:
            cells += ("<td class=p>в игре: нарезка<br>" + "".join(
                f"<audio controls preload='none' src='{cid}/pick/{n}' style='width:120px'></audio>" for n in sliced) + "</td>")
        h.append(f"<tr><td><b>{cid}</b><br><small>{html.escape(c['prompt'])}</small></td>{cells}</tr>")
    h.append("</table>")
    (LIB / "index.html").write_text("".join(h), encoding="utf-8")


def switch(cues):
    text = MANIFEST.read_text(encoding="utf-8")
    man = json.loads(text)
    if "gen" not in man["roots"]:
        text = re.sub(r'("cuts": "[^"]*")', r'\1,\n    "gen": "' + LIB.as_posix() + '"', text, count=1)
    out, n = [], 0
    for line in text.splitlines(keepends=True):
        m = re.match(r'(\s*\{ "id": "([^"]+)", "type": "[^"]+", )"src": (\[[^\]]*\])(, "proc": \{[^}]*\})?(.*)$', line)
        if m and m.group(2) in cues and '"gen:' not in m.group(3):
            cid = m.group(2)
            proc = cues[cid].get("proc")
            proc_s = (", \"proc\": " + json.dumps(proc).replace("{", "{ ").replace("}", " }")) if proc else (m.group(4) or "")
            line = f'{m.group(1)}"src": ["gen:{cid}/pick/*.wav"]{proc_s}, "src_pack": {m.group(3)}{m.group(5)}'
            if not line.endswith("\n"):
                line += "\n"
            n += 1
        out.append(line)
    new = "".join(out)
    json.loads(new)  # still valid JSON
    MANIFEST.write_text(new, encoding="utf-8")
    print(f"switched {n} cues to generated sounds")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", default="")
    ap.add_argument("--pick", nargs="+", help="cue=N,N,...")
    ap.add_argument("--switch", action="store_true")
    ap.add_argument("--slice", action="store_true", help="cut sliced cues into single hits (needs librosa, soundfile)")
    a = ap.parse_args()
    cues = load()
    if a.slice:
        for cid, c in cues.items():
            if "slice" in c and cid.startswith(a.only):
                slice_hits(cid, c)
        write_page(cues)
    elif a.pick:
        for item in a.pick:
            cid, nums = item.split("=")
            set_pick(cid, [int(x) for x in nums.split(",")])
        write_page(cues)
    elif a.switch:
        switch(cues)
    else:
        render(cues, a.only)
        print(f"listen: {LIB / 'index.html'}")


if __name__ == "__main__":
    main()

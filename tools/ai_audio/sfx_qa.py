"""Throw silent, clipped or too-short takes out of the default picks (CPU, no listening needed).

    uv run --python 3.12 --with soundfile --with numpy python tools/ai_audio/sfx_qa.py   # every cue with picks
    ... sfx_qa.py --dry                                                                  # report only

A take is bad when its loudest 50 ms is under -45 dBFS (silence), more than 2 % of its samples sit at full scale
(clipping), or it is shorter than 0.15 s. A bad pick is replaced by the next good take of the same cue; sliced
cues (footsteps) are left to gen_sfx.py --slice. The report goes to sfx_lib/qa.json.
"""
import argparse
import json
import shutil
import sys
from pathlib import Path

LIB = Path("F:/ClaudeGames/snow_audio_gen/sfx_lib")
PROMPTS = Path(__file__).with_name("sfx_prompts.json")


def measure(path):
    """Duration, the loudest 50 ms in dBFS, the share of samples at full scale (any sample format)."""
    import numpy as np
    import soundfile as sf
    y, rate = sf.read(str(path), dtype="float32", always_2d=True)
    if y.size == 0:
        return {"dur": 0.0, "peak_db": -120.0, "clip": 0.0}
    mono = y.mean(axis=1)
    win = max(1, int(rate * 0.05))
    n = len(mono) // win * win
    frames = mono[:n].reshape(-1, win) if n else mono.reshape(1, -1)
    loud = float(np.sqrt((frames ** 2).mean(axis=1)).max())
    clip = float((np.abs(y) >= 0.999).mean())
    return {"dur": len(mono) / rate, "peak_db": 20 * float(np.log10(max(loud, 1e-6))), "clip": clip}


def bad(m, short_ok=False):
    return m["peak_db"] < -45 or m["clip"] > 0.02 or (m["dur"] < 0.15 and not short_ok)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry", action="store_true")
    a = ap.parse_args()
    cues = json.loads(PROMPTS.read_text(encoding="utf-8"))["cues"]
    report, swapped, flagged = {}, 0, 0
    for cid, c in cues.items():
        folder = LIB / cid
        pick = folder / "pick"
        if "slice" in c or not pick.exists():
            continue
        takes = sorted(folder.glob("v[0-9][0-9].wav"))
        ms = {t.name: measure(t) for t in takes}
        report[cid] = {k: {kk: round(vv, 3) for kk, vv in v.items()} for k, v in ms.items()}
        picked = sorted(p.name for p in pick.glob("v*.wav"))
        short_ok = c.get("dur", 3) <= 2                                   # a beep or a click may be that short
        good_spare = [t for t in ms if not bad(ms[t], short_ok) and t not in picked]
        for name in picked:
            if name in ms and bad(ms[name], short_ok):
                flagged += 1
                if good_spare and not a.dry:
                    (pick / name).unlink()
                    rep = good_spare.pop(0)
                    shutil.copyfile(folder / rep, pick / rep)
                    swapped += 1
                    print(f"{cid}: {name} {ms[name]} -> {rep}")
                else:
                    print(f"{cid}: {name} bad {ms[name]}, no spare")
    (LIB / "qa.json").write_text(json.dumps(report, indent=1), encoding="utf-8")
    print(f"{len(report)} cues checked, {flagged} bad picks, {swapped} replaced")


if __name__ == "__main__":
    main()

"""Pick the best take of every voiced line and put it into the game (CPU only).

    uv run --python 3.12 --with faster-whisper --with soundfile --with scipy --with librosa ^
        python tools/ai_audio/voice_pick.py radio_voice --lang en [--promote]

Score per take: how close Whisper's words are to the subtitle (digits read as words), how sure Whisper is of the
language (an accent lowers it), and a male voice (median pitch under 165 Hz). The ranking goes to
F:/ClaudeGames/snow_audio_gen/vo_<speaker>_<lang>/picks.json; --promote hands the winners to voice_batch.py
--promote. A take chosen by hand afterwards: voice_batch.py <speaker> --lang .. --promote <id>=N.
"""
import argparse
import difflib
import json
import re
import subprocess
import sys
from pathlib import Path

import librosa
import numpy as np
import soundfile as sf
from faster_whisper import WhisperModel
from scipy.signal import resample_poly

sys.path.insert(0, str(Path(__file__).parent))
import voice_batch  # noqa: E402

DIGITS = {"en": ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"],
          "ru": ["ноль", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять"]}


def words(text, lang):
    text = re.sub(r"\[[^\]]*\]", " ", text.lower()).replace("ё", "е")
    text = re.sub(r"\d", lambda m: " " + DIGITS[lang][int(m.group())] + " ", text)
    return re.sub(r"[^\w]+", " ", text).split()


def load16k(path):
    a, sr = sf.read(str(path), dtype="float32")
    if a.ndim > 1:
        a = a.mean(axis=1)
    return resample_poly(a, 16000, sr).astype(np.float32)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("speaker")
    ap.add_argument("--lang", choices=["en", "ru"], default="en")
    ap.add_argument("--promote", action="store_true")
    args = ap.parse_args()

    rows, _ = voice_batch.table()
    subs = {r["id"]: r[args.lang] for r in rows if r["speaker"] == args.speaker}
    out = Path("F:/ClaudeGames/snow_audio_gen") / f"vo_{args.speaker}_{args.lang}"
    model = WhisperModel("small", device="cpu", compute_type="int8")
    whisper_lang = args.lang if args.speaker != "radio_voice" or args.lang == "ru" else "en"
    picks, report = {}, {}
    for line in sorted(p for p in out.iterdir() if p.is_dir() and p.name in subs):
        want = words(subs[line.name], args.lang)
        best, rank = None, []
        for take in sorted(line.glob("v[0-9][0-9].wav")):
            y = load16k(take)
            seg, info = model.transcribe(y, beam_size=1, language=whisper_lang)
            heard = " ".join(s.text for s in seg)
            sim = difflib.SequenceMatcher(None, want, words(heard, args.lang)).ratio()
            f0, voiced, _ = librosa.pyin(y, fmin=60, fmax=500, sr=16000)
            f0 = f0[voiced]
            med = float(np.median(f0)) if f0.size else 0.0
            score = sim + 0.2 * float(info.language_probability) - (0.5 if med > 165 else 0.0)
            rank.append({"take": take.name, "score": round(score, 3), "sim": round(sim, 2), "hz": round(med),
                         "lang_p": round(float(info.language_probability), 2), "heard": heard.strip()})
            if best is None or score > best[0]:
                best = (score, int(take.stem[1:]))
        if best:
            picks[line.name] = best[1]
            report[line.name] = sorted(rank, key=lambda r: -r["score"])
            print(f"{line.name}: v{best[1]:02d} ({report[line.name][0]['score']}) {report[line.name][0]['heard']}", flush=True)
    (out / "picks.json").write_text(json.dumps({"picks": picks, "ranking": report}, ensure_ascii=False, indent=1), encoding="utf-8")
    weak = [k for k, r in report.items() if r[0]["sim"] < 0.6]
    print(f"{len(picks)} lines picked; weak (words differ): {', '.join(weak) or 'none'}")
    if args.promote and picks:
        spec = ",".join(f"{k}={v}" for k, v in picks.items())
        subprocess.run([sys.executable, str(Path(__file__).with_name("voice_batch.py")), args.speaker,
                        "--lang", args.lang, "--promote", spec], check=True)


if __name__ == "__main__":
    main()

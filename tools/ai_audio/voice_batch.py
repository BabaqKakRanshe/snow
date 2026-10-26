"""Voice a whole speaker from the game's line table, several takes per line, then
put the chosen takes into the game.

    python tools/ai_audio/voice_batch.py kessler --lang en --voice F:/ClaudeGames/ai_audio/voices/kessler_alive_a.wav -n 3
    python tools/ai_audio/voice_batch.py kessler --lang ru --voice ... -n 3 --ids kes_hello_01,kes_hello_02
    python tools/ai_audio/voice_batch.py kessler --lang en --promote kes_hello_01=2,kes_hello_02=1
    python tools/ai_audio/voice_batch.py kessler --lang en --promote all=1

Text comes from tools/ai_audio/tts_<speaker>.csv (en_tts / ru_tts: direction
tags, Russian stress as capitals; made by ru_stress.py), falling back to the en
column of Snow_VoiceLines.csv. Takes go to
F:/ClaudeGames/snow_audio_gen/vo_<speaker>_<lang>/<id>/ with one index.html for
the whole speaker; a rerun only fills lines that have fewer than -n takes.

--promote converts take N of each line to 48 kHz mono 24-bit into
Runtime/Voice/Resources/Voice/ under the name in the table's file column
(_en -> _ru for Russian) and marks the line 'tts' in Snow_VoiceLines.csv.
"""
import argparse
import csv
import html
import io
import json
import re
import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).parent))
import snowgen  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
LINES = REPO / "Snow/Assets/Snow/Runtime/Voice/Resources/Snow_VoiceLines.csv"
VOICE_DIR = REPO / "Snow/Assets/Snow/Runtime/Voice/Resources/Voice"


def table():
    raw = LINES.read_bytes().decode("utf-8")
    newline = "\r\n" if "\r\n" in raw else "\n"
    rows = list(csv.DictReader(io.StringIO(raw)))
    return rows, newline


def tts_text(speaker):
    p = Path(__file__).with_name(f"tts_{speaker}.csv")
    return {r["id"]: r for r in csv.DictReader(p.open(encoding="utf-8"))} if p.exists() else {}


def line_text(row, tts, lang):
    t = tts.get(row["id"], {})
    if lang == "ru":
        return t.get("ru_tts") or row["ru"]
    return t.get("en_tts") or row["en"]


def game_file(row, lang):
    name = row["file"]
    return name.replace("_en.wav", "_ru.wav") if lang == "ru" else name


def words(text):
    """Spoken words without direction tags, case and punctuation."""
    return re.sub(r"[^\w]+", " ", re.sub(r"\[[^\]]*\]", " ", text)).lower().split()


def generate(args, rows, tts, out):
    sub = "ru" if args.lang == "ru" else "en"
    off = [r["id"] for r in rows if words(line_text(r, tts, args.lang)) != words(r[sub])]
    if off:
        sys.exit(f"TTS text differs from the subtitle in {', '.join(off)}: fix tts_{args.speaker}.csv")
    for k, row in enumerate(rows, 1):
        folder = out / row["id"]
        folder.mkdir(parents=True, exist_ok=True)
        have = len([p for p in folder.glob("v*.wav") if not p.stem.endswith("_radio")])
        if have >= args.takes:
            continue
        text = line_text(row, tts, args.lang)
        print(f"[{k}/{len(rows)}] {row['id']}: {text}", flush=True)
        ns = SimpleNamespace(prompt=text, takes=args.takes - have, seed=None, voice=args.voice, ref_text=None,
                             instruct=None, temperature=args.temperature, top_p=args.top_p)
        new = snowgen.voice_takes(ns, "fish", folder, have)
        snowgen.radio_copy(folder, new)
        index = folder / "takes.json"
        takes = json.loads(index.read_text(encoding="utf-8")) if index.exists() else []
        for t in new:
            t.update(prompt=text, voice=args.voice)
        index.write_text(json.dumps(takes + new, ensure_ascii=False, indent=1), encoding="utf-8")
        write_page(out, rows, args.lang)
    write_page(out, rows, args.lang)


def write_page(out, rows, lang):
    h = [f"<!doctype html><meta charset='utf-8'><title>{out.name}</title><style>body{{font:15px system-ui;margin:24px;"
         "background:#111;color:#ddd}td{padding:5px 8px;vertical-align:top;border-top:1px solid #333}"
         "audio{height:28px;vertical-align:middle}small{color:#999}</style>"
         f"<h2>{out.name}</h2><p><small>номер дубля → «--promote id=N»; верхний — чистый, нижний — через рацию</small></p><table>"]
    for row in rows:
        index = out / row["id"] / "takes.json"
        if not index.exists():
            continue
        cells = "".join(
            f"<td>{t['file'][1:3]}<br><audio controls preload='none' src='{row['id']}/{t['file']}'></audio>"
            f"<br><audio controls preload='none' src='{row['id']}/{t['radio']}'></audio></td>"
            for t in json.loads(index.read_text(encoding="utf-8")))
        sub = row["ru"] if lang != "ru" else row["en"]
        h.append(f"<tr><td><b>{row['id']}</b><br>{html.escape(line_text(row, {}, lang))}<br>"
                 f"<small>{html.escape(sub)}</small></td>{cells}</tr>")
    h.append("</table>")
    (out / "index.html").write_text("".join(h), encoding="utf-8")


def promote(args, all_rows, newline, rows, out):
    wanted = dict(p.split("=") for p in args.promote.split(","))
    default = wanted.pop("all", None)
    picks = {r["id"]: int(wanted.get(r["id"], default)) for r in rows if wanted.get(r["id"], default)}
    VOICE_DIR.mkdir(parents=True, exist_ok=True)
    for row in rows:
        if row["id"] not in picks:
            continue
        src = out / row["id"] / f"v{picks[row['id']]:02d}.wav"
        if not src.exists():
            print(f"  missing {src}")
            continue
        dst = VOICE_DIR / game_file(row, args.lang)
        subprocess.run([snowgen.FFMPEG, "-y", "-loglevel", "error", "-i", str(src),
                        "-ar", "48000", "-ac", "1", "-c:a", "pcm_s24le", str(dst)], check=True)
        if row["status"] == "draft":
            row["status"] = "tts"
        print(f"  {row['id']} v{picks[row['id']]:02d} -> {dst.name}")
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=list(all_rows[0].keys()), lineterminator=newline)
    w.writeheader()
    w.writerows(all_rows)
    LINES.write_bytes(buf.getvalue().encode("utf-8"))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("speaker", help="speaker column of Snow_VoiceLines.csv (kessler, radio_voice)")
    ap.add_argument("--lang", choices=["en", "ru"], default="en")
    ap.add_argument("--voice", help="reference WAV (its words in <same name>.txt)")
    ap.add_argument("-n", "--takes", type=int, default=3)
    ap.add_argument("--ids", help="comma-separated line ids; default all lines of the speaker")
    ap.add_argument("--temperature", type=float)
    ap.add_argument("--top-p", type=float)
    ap.add_argument("--promote", help="id=N,... or all=N: put take N into the game")
    args = ap.parse_args()

    all_rows, newline = table()
    rows = [r for r in all_rows if r["speaker"] == args.speaker and (r["en"] if args.lang == "en" else r["ru"])]
    if args.ids:
        ids = set(args.ids.split(","))
        rows = [r for r in rows if r["id"] in ids]
    out = snowgen.OUT_ROOT / f"vo_{args.speaker}_{args.lang}"
    out.mkdir(parents=True, exist_ok=True)

    if args.promote:
        promote(args, all_rows, newline, rows, out)
    else:
        if not args.voice:
            sys.exit("--voice is needed to generate")
        generate(args, rows, tts_text(args.speaker), out)
        print(f"listen: {out / 'index.html'}")


if __name__ == "__main__":
    main()

"""Generate several takes of one sound, music cue or voice line locally, and lay
them out in a folder with a listening page.

    python tools/ai_audio/snowgen.py sfx   "wooden door slam, small hut" -n 6 -d 3
    python tools/ai_audio/snowgen.py music "slow dark drone, frozen forest" -n 3 -d 60 --model medium
    python tools/ai_audio/snowgen.py song  "russian bard song, acoustic guitar" --lyrics lyrics.txt -n 2 -d 90
    python tools/ai_audio/snowgen.py voice "Приём. Говорит пост." -n 4 --model design --instruct "old hoarse man"
    python tools/ai_audio/snowgen.py voice "Приём. Говорит пост." -n 4 --voice voices/radio_man.wav

Every take lands in F:/ClaudeGames/snow_audio_gen/<date>/<kind>_<name>/ as
v01.wav, v02.wav ... with takes.json (prompt, model, seed per take) and
index.html to listen and compare.

sfx/music/song go through the Soundbox server (start_soundbox.cmd must be
running). voice runs audio.cpp directly: Soundbox does not pass the reference
transcript Fish needs.
"""
import argparse
import datetime
import html
import json
import random
import re
import shutil
import subprocess
import sys
import time
import urllib.request
import wave
from pathlib import Path

HOST = "http://127.0.0.1:17494"
OUT_ROOT = Path("F:/ClaudeGames/snow_audio_gen")
SB_DATA = Path("F:/ClaudeGames/ai_audio/sbdata")
FFMPEG = "C:/Users/user/tools/ffmpeg_dl/ffmpeg-master-latest-win64-gpl/bin/ffmpeg.exe"

MODELS = {
    "sfx": {"sfx": "audiocpp-stable-audio-3-sfx", "medium": "audiocpp-stable-audio-3-medium"},
    "music": {"small": "audiocpp-stable-audio-3-small-music", "medium": "audiocpp-stable-audio-3-medium",
              "ace": "audiocpp-ace-step", "minimax": "audiocpp-minimax-music3"},
    "song": {"ace": "audiocpp-ace-step", "minimax": "audiocpp-minimax-music3"},
    # audio.cpp family and weights file, run through audiocpp_cli
    "voice": {"fish": ("fish_audio", "Fish-Audio-S2-Pro-GGUF/fish-audio-s2-pro-q8_0.gguf"),
              "design": ("qwen3_tts", "Qwen3-TTS-12Hz-1.7B-VoiceDesign-GGUF/qwen3-tts-12hz-1.7b-voicedesign-q8_0.gguf")},
}
DEFAULT_MODEL = {"sfx": "sfx", "music": "small", "song": "ace", "voice": "fish"}
MAX_PER_JOB = 4  # Soundbox caps `variations` at 4


def call(method, path, body=None, timeout=60):
    headers = {"X-Soundbox": "1"}
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(HOST + path, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        sys.exit(f"Soundbox {method} {path} -> HTTP {e.code}: {e.read().decode('utf-8', 'replace')[:500]}")
    except urllib.error.URLError:
        sys.exit("Soundbox is not running. Start F:/ClaudeGames/ai_audio/start_soundbox.cmd first.")


def wait(job_id):
    last = ""
    while True:
        job = call("GET", f"/api/jobs/{job_id}")
        msg = f"{job['status']} {job.get('phase') or ''} {job.get('message') or ''}".strip()
        if msg != last:
            print("   ", msg, flush=True)
            last = msg
        if job["status"] == "done":
            return job["result"]["sounds"]
        if job["status"] in ("error", "cancelled"):
            sys.exit(f"job {job['status']}: {job.get('error')}")
        time.sleep(1.5)


def soundbox_takes(args, model, folder, start):
    """Render through Soundbox, `variations` per job, and download the WAVs."""
    lyrics = Path(args.lyrics).read_text(encoding="utf-8") if args.lyrics else None
    sound_ids = []
    left = args.takes
    while left > 0:
        n = min(left, MAX_PER_JOB)
        print(f"  {n} takes")
        body = {"model": model, "prompt": args.prompt, "variations": n,
                "duration_s": args.duration or (3.0 if args.kind == "sfx" else 30.0),
                "seed": args.seed if left == args.takes else None,
                "negative_prompt": args.negative, "lyrics": lyrics, "tags": ["snowgen"]}
        sound_ids += wait(call("POST", "/api/generate", body)["job_id"])
        left -= n

    takes = []
    for k, sid in enumerate(sound_ids, start=start + 1):
        s = call("GET", f"/api/sounds/{sid}")
        name = f"v{k:02d}.wav"
        with urllib.request.urlopen(HOST + s["file"], timeout=120) as r, open(folder / name, "wb") as f:
            shutil.copyfileobj(r, f)
        takes.append({"file": name, "model": s["model"], "seed": s.get("seed"),
                      "duration_s": s.get("duration_s") or 0})
    return takes


def audiocpp_cli():
    found = sorted(SB_DATA.glob("bin/audiocpp/*/audiocpp_cli.exe"), key=lambda p: p.stat().st_mtime)
    if not found:
        sys.exit(f"audiocpp_cli.exe not found under {SB_DATA / 'bin/audiocpp'}")
    return found[-1]


def voice_takes(args, short, folder, start):
    """Speak one line per take with audiocpp_cli, a fresh seed each time."""
    family, weights = MODELS["voice"][short]
    cmd = [str(audiocpp_cli()), "--task", "tts", "--family", family,
           "--model", str(SB_DATA / "models/audiocpp" / weights), "--backend", "cuda",
           "--text", args.prompt]
    if args.voice:
        ref = Path(args.voice)
        transcript = args.ref_text
        if transcript is None and ref.with_suffix(".txt").exists():
            transcript = ref.with_suffix(".txt").read_text(encoding="utf-8").strip()
        if not ref.exists() or not transcript:
            sys.exit("--voice needs a WAV and its words: --ref-text, or a .txt with the same name next to it")
        cmd += ["--voice-ref", str(ref), "--reference-text", transcript]
    elif short == "fish":
        sys.exit("fish clones a voice: pass --voice (WAV), or make one first with --model design --instruct")
    if args.instruct:
        cmd += ["--instruct", args.instruct]
    if args.temperature is not None:
        cmd += ["--request-option", f"temperature={args.temperature}"]
    if args.top_p is not None:
        cmd += ["--request-option", f"top_p={args.top_p}"]

    takes = []
    for i in range(args.takes):
        seed = args.seed if (i == 0 and args.seed is not None) else random.randrange(2 ** 31)
        name = f"v{start + i + 1:02d}.wav"
        print(f"  take {i + 1}/{args.takes} seed {seed}", flush=True)
        run = subprocess.run(cmd + ["--seed", str(seed), "--out", str(folder / name)],
                             capture_output=True, text=True, encoding="utf-8", errors="replace")
        if run.returncode != 0 or not (folder / name).exists():
            sys.exit(f"audiocpp_cli failed:\n{(run.stdout + run.stderr)[-1500:]}")
        with wave.open(str(folder / name)) as w:
            duration = w.getnframes() / w.getframerate()
        takes.append({"file": name, "model": f"{family}:{Path(weights).stem}", "seed": seed,
                      "duration_s": duration, "temperature": args.temperature})
    return takes


def radio_copy(folder, takes):
    """A '_radio' copy of each take for listening as it will roughly sound over the
    game's radio: telephone band, squashed, slightly driven, hiss. The game applies
    its own radio DSP to the clean file, so only the clean take goes into the game."""
    chain = ("highpass=f=300,lowpass=f=3400,acompressor=threshold=0.1:ratio=6:attack=5:release=80,"
             "volume=6dB,asoftclip=type=tanh,volume=-3dB")
    for t in takes:
        src = folder / t["file"]
        dst = folder / (src.stem + "_radio.wav")
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-i", str(src),
                        "-f", "lavfi", "-i", "anoisesrc=color=pink:amplitude=0.015",
                        "-filter_complex", f"[0:a]{chain}[v];[1:a]highpass=f=300,lowpass=f=4000[n];"
                        "[v][n]amix=inputs=2:duration=first:normalize=0", str(dst)], check=True)
        t["radio"] = dst.name


def slug(text):
    s = re.sub(r"[^\w]+", "_", text.lower(), flags=re.UNICODE).strip("_")
    return s[:40] or "take"


def write_page(folder, takes):
    def players(t):
        out = f"<audio controls preload='none' src='{t['file']}'></audio>"
        if t.get("radio"):
            out += f"<br>radio <audio controls preload='none' src='{t['radio']}'></audio>"
        return out

    rows = "\n".join(
        f"<tr><td>{t['file']}</td><td>{players(t)}</td>"
        f"<td>seed {t['seed']}</td><td>{t['duration_s']:.1f} s</td><td>{html.escape(t['model'])}</td>"
        f"<td>{html.escape(t['prompt'])}</td></tr>"
        for t in takes)
    page = f"""<!doctype html><meta charset="utf-8"><title>{html.escape(folder.name)}</title>
<style>body{{font:15px system-ui;margin:24px;background:#111;color:#ddd}}td{{padding:6px 12px}}</style>
<h2>{html.escape(folder.name)}</h2><table>{rows}</table>"""
    (folder / "index.html").write_text(page, encoding="utf-8")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("kind", choices=MODELS)
    ap.add_argument("prompt", help="description (sfx/music/song) or the line to speak (voice)")
    ap.add_argument("-n", "--takes", type=int, default=4)
    ap.add_argument("-d", "--duration", type=float, default=None, help="seconds (not for voice)")
    ap.add_argument("--model", help="short name, see MODELS in this file")
    ap.add_argument("--name", help="folder name; default is made from the prompt")
    ap.add_argument("--seed", type=int, help="seed of the first take; the rest are random")
    ap.add_argument("--negative", help="negative prompt (Stable Audio)")
    ap.add_argument("--lyrics", help="lyrics text file (song)")
    ap.add_argument("--voice", help="WAV of the voice to clone; its words in --ref-text or <same name>.txt")
    ap.add_argument("--ref-text", help="words spoken in the --voice WAV")
    ap.add_argument("--instruct", help="voice description for --model design, e.g. 'old hoarse man, slow'")
    ap.add_argument("--temperature", type=float, help="voice: sampling temperature (Fish default 0.7; higher = livelier, less stable)")
    ap.add_argument("--top-p", type=float, help="voice: nucleus sampling (Fish default 0.7)")
    ap.add_argument("--radio", action="store_true", help="also write a vNN_radio.wav copy to hear it over a radio")
    args = ap.parse_args()

    short = args.model or DEFAULT_MODEL[args.kind]
    if short not in MODELS[args.kind]:
        sys.exit(f"--model for {args.kind}: one of {', '.join(MODELS[args.kind])}")

    folder = OUT_ROOT / datetime.date.today().isoformat() / f"{args.kind}_{args.name or slug(args.prompt)}"
    folder.mkdir(parents=True, exist_ok=True)
    start = len(list(folder.glob("v*.wav")))
    print(f"{args.takes} takes of {args.kind} on {short} -> {folder}")

    if args.kind == "voice":
        new = voice_takes(args, short, folder, start)
    else:
        new = soundbox_takes(args, MODELS[args.kind][short], folder, start)
    if args.radio:
        radio_copy(folder, new)

    index = folder / "takes.json"
    takes = json.loads(index.read_text(encoding="utf-8")) if index.exists() else []
    for t in new:
        t.update(prompt=args.prompt, voice=args.voice, instruct=args.instruct, lyrics=args.lyrics)
    takes += new
    index.write_text(json.dumps(takes, ensure_ascii=False, indent=1), encoding="utf-8")
    write_page(folder, takes)
    print(f"done: {len(new)} takes, listen: {folder / 'index.html'}")


if __name__ == "__main__":
    main()

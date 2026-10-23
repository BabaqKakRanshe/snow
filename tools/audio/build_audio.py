"""
Builds the game's sound clips from the curated libraries (tools/audio/audio_manifest.json) with ffmpeg.

  python tools/audio/build_audio.py                 # build into the staging folder (outside Unity)
  python tools/audio/build_audio.py --install       # then copy into Snow/Assets/Snow/Audio (editor must not be in Play)
  python tools/audio/build_audio.py --only step.    # only cues whose id starts with this

oneshot: trim the leading silence (or start at proc.start), cut to maxSec, mono, 22050 Hz, 5 ms fade out, peak to peakDb.
bed/loop: cut [start, start+len], downmix, 22050 Hz, loudness to lufs, then a seamless loop: the last xfade seconds are
mixed into the first ones (length = len - xfade). Writes sound_cues.json next to Clips/ for SoundLibraryBuildStep.
"""
import argparse, glob, json, os, re, shutil, subprocess, sys, tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
MANIFEST = REPO / "tools" / "audio" / "audio_manifest.json"
STAGE = Path("F:/ClaudeGames/snow_audio_library/build/Audio")
UNITY_AUDIO = REPO / "Snow" / "Assets" / "Snow" / "Audio"
ASSET_PREFIX = "Assets/Snow/Audio"
FFMPEG_DIR = Path("C:/Users/user/tools/ffmpeg_dl/ffmpeg-master-latest-win64-gpl/bin")
FFMPEG = str(FFMPEG_DIR / "ffmpeg.exe") if (FFMPEG_DIR / "ffmpeg.exe").exists() else "ffmpeg"
FFPROBE = str(FFMPEG_DIR / "ffprobe.exe") if (FFMPEG_DIR / "ffprobe.exe").exists() else "ffprobe"


def run(args):
    p = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if p.returncode != 0:
        raise RuntimeError(" ".join(args[:6]) + " ...\n" + p.stderr[-1500:])
    return p.stderr


def probe(path):
    out = subprocess.run([FFPROBE, "-v", "error", "-select_streams", "a:0", "-show_entries",
                          "stream=channels:format=duration", "-of", "json", path],
                         capture_output=True, text=True, encoding="utf-8").stdout
    j = json.loads(out)
    return int(j["streams"][0]["channels"]), float(j["format"]["duration"])


def downmix(ch_in, ch_out):
    """Filter that brings ch_in channels to ch_out (unknown layouts: the first two channels)."""
    f = []
    if ch_in > 2:
        f.append("pan=stereo|c0=c0|c1=c1")
        ch_in = 2
    if ch_out == 1 and ch_in == 2:
        f.append("pan=mono|c0=0.5*c0+0.5*c1")
    elif ch_out == 2 and ch_in == 1:
        f.append("pan=stereo|c0=c0|c1=c0")
    return f


def peak_gain(path, target_db):
    err = run([FFMPEG, "-hide_banner", "-i", path, "-af", "volumedetect", "-f", "null", "-"])
    m = re.search(r"max_volume:\s*(-?[\d.]+) dB", err)
    return target_db - float(m.group(1)) if m else 0.0


def oneshot(src, dst, p):
    ch, dur = probe(src)
    pre = []
    if p.get("start") is not None:
        pre = ["-ss", str(p["start"])]
        trim = []
    else:
        trim = ["silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.01"]
    filters = trim + downmix(ch, p["channels"]) + [
        f"atrim=0:{p['maxSec']}", f"aresample={p['rate']}",
        "areverse", "afade=t=in:d=0.005", "areverse"]
    tmp = dst.with_suffix(".tmp.wav")
    run([FFMPEG, "-hide_banner", "-y", *pre, "-i", src, "-af", ",".join(filters), "-c:a", "pcm_s16le", str(tmp)])
    if tmp.stat().st_size < 2000 and trim:                              # a quiet take (a breath) is all "silence"
        run([FFMPEG, "-hide_banner", "-y", *pre, "-i", src, "-af", ",".join(filters[len(trim):]), "-c:a", "pcm_s16le", str(tmp)])
    if tmp.stat().st_size < 2000:
        raise RuntimeError("empty after processing")
    g = peak_gain(str(tmp), p["peakDb"])
    run([FFMPEG, "-hide_banner", "-y", "-i", str(tmp), "-af", f"volume={g:.2f}dB", "-c:a", "pcm_s16le", str(dst)])
    tmp.unlink()


def loop(src, dst, p):
    ch, dur = probe(src)
    start, length, x = float(p["start"]), float(p["len"]), float(p["xfade"])
    if start + length > dur:
        start = max(0.0, dur - length)
        length = min(length, dur - start)
    x = min(x, length / 4)
    seg = dst.with_suffix(".seg.wav")
    filters = downmix(ch, p["channels"]) + [f"aresample={p['rate']}", f"loudnorm=I={p['lufs']}:TP=-3:LRA=15",
                                             f"aresample={p['rate']}"]
    run([FFMPEG, "-hide_banner", "-y", "-ss", str(start), "-t", str(length), "-i", src, "-af", ",".join(filters),
         "-c:a", "pcm_s16le", str(seg)])
    _, L = probe(str(seg))
    fc = (f"[0]atrim=0:{x},asetpts=PTS-STARTPTS,afade=t=in:d={x}[h];"
          f"[0]atrim={x}:{L - x},asetpts=PTS-STARTPTS[m];"
          f"[0]atrim={L - x}:{L},asetpts=PTS-STARTPTS,afade=t=out:d={x}[t];"
          f"[t][h]amix=inputs=2:normalize=0[xf];[xf][m]concat=n=2:v=0:a=1[o]")
    run([FFMPEG, "-hide_banner", "-y", "-i", str(seg), "-filter_complex", fc, "-map", "[o]", "-c:a", "pcm_s16le", str(dst)])
    seg.unlink()


def resolve(roots, pattern):
    root, rel = pattern.split(":", 1)
    hits = sorted(glob.glob(str(Path(roots[root]) / rel), recursive=True))
    return [h for h in hits if h.lower().endswith(".wav")]


def build(only, out_root):
    man = json.loads(MANIFEST.read_text(encoding="utf-8"))
    roots, defaults = man["roots"], man["defaults"]
    clips_root = out_root / "Clips"
    index = {"cues": []}
    old_index = out_root / "sound_cues.json"
    if only and old_index.exists():
        index = json.loads(old_index.read_text(encoding="utf-8"))
        index["cues"] = [c for c in index["cues"] if not c["id"].startswith(only)]
    problems = []
    for cue in man["cues"]:
        if only and not cue["id"].startswith(only):
            continue
        kind = cue["type"]
        p = dict(defaults["bed" if kind == "bed" else "loop" if kind == "loop" else "oneshot"])
        p.update(cue.get("proc", {}))
        files = []
        for pat in cue["src"]:
            found = resolve(roots, pat)
            if not found:
                problems.append(f"{cue['id']}: nothing matches {pat}")
            files += found
        group = cue["id"].split(".")[0]
        d = clips_root / group
        d.mkdir(parents=True, exist_ok=True)
        for old in d.glob(cue["id"] + "_*.wav"):
            old.unlink()
        clips = []
        for i, f in enumerate(dict.fromkeys(files), 1):
            dst = d / f"{cue['id']}_{i:02d}.wav"
            try:
                (oneshot if kind == "oneshot" else loop)(f, dst, p)
                clips.append(f"{ASSET_PREFIX}/Clips/{group}/{dst.name}")
            except Exception as e:                                       # keep going, report at the end
                problems.append(f"{cue['id']}: {Path(f).name}: {str(e)[:300]}")
        index["cues"].append({"id": cue["id"], "type": kind, "clips": clips, "set": cue.get("set", {}),
                              "sources": [Path(f).name for f in dict.fromkeys(files)]})
        print(f"{cue['id']:24s} {len(clips):3d} clips")
    index["cues"].sort(key=lambda c: c["id"])
    out_root.mkdir(parents=True, exist_ok=True)
    (out_root / "sound_cues.json").write_text(json.dumps(index, ensure_ascii=False, indent=1), encoding="utf-8")
    return problems


def install(out_root):
    dst = UNITY_AUDIO
    (dst / "Clips").mkdir(parents=True, exist_ok=True)
    n = 0
    for f in (out_root / "Clips").rglob("*.wav"):
        t = dst / "Clips" / f.relative_to(out_root / "Clips")
        t.parent.mkdir(parents=True, exist_ok=True)
        if not t.exists() or t.read_bytes() != f.read_bytes():
            shutil.copy2(f, t)
            n += 1
    shutil.copy2(out_root / "sound_cues.json", dst / "sound_cues.json")
    print(f"installed {n} changed clips into {dst}")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--install", action="store_true")
    ap.add_argument("--skip-build", action="store_true")
    a = ap.parse_args()
    probs = [] if a.skip_build else build(a.only, STAGE)
    for pr in probs:
        print("PROBLEM", pr)
    if a.install:
        install(STAGE)

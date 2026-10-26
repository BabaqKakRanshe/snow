"""Cut the pager sounds out of long Sonniss recordings into F:/ClaudeGames/snow_audio_library/cuts (22 050 Hz mono
16 bit, peak -3 dB), the `cuts:` root of audio_manifest.json:
- letter/key_NN.wav: single typewriter strikes, each starting right at the typebar hit (Sonniss GDC 2023, Dramatic Cat -
  Olivetti Linea 98, slow typing, contact mic); letter/carriage_NN.wav: backspace + carriage movement; letter/chatter_01:
  3 s of the fast typebar mechanism;
- pager/beep_NN.wav: 85 ms bips in groups of 3 and 2 (Sonniss GDC 2019, 2496SoundEffects - Alarm Clock Electric Beeping).
Run: python tools/audio/cut_letter_sounds.py, then build_audio.py --only ui. --install and Snow/Audio/Rebuild Sound Library."""
import os
import subprocess
import sys
import tempfile

import numpy as np
import soundfile as sf

FF = "C:/Users/user/tools/ffmpeg_dl/ffmpeg-master-latest-win64-gpl/bin/ffmpeg.exe"
LIB = "D:/Downloads/!Torrent/!SOUNDS"
OUT = "F:/ClaudeGames/snow_audio_library/cuts/letter"
TMP = os.path.join(tempfile.gettempdir(), "snow_cut_letter")
RATE = 22050
os.makedirs(OUT, exist_ok=True)
os.makedirs(TMP, exist_ok=True)


def find(name):
    for root, _, files in os.walk(LIB):
        if name in files:
            return os.path.join(root, name)
    sys.exit("missing " + name)


def load(name):
    src = find(name)
    dst = os.path.join(TMP, "src.wav")
    subprocess.run([FF, "-y", "-loglevel", "error", "-i", src, "-ac", "1", "-ar", str(RATE), "-c:a", "pcm_f32le", dst], check=True)
    x, _ = sf.read(dst, dtype="float32")
    return x


def envelope(x, win=0.004):
    n = int(win * RATE)
    return np.sqrt(np.convolve(x * x, np.ones(n) / n, mode="same"))


def save(x, path, fade_ms=15):
    x = x - np.mean(x)
    f = int(fade_ms / 1000 * RATE)
    if len(x) > f:
        x[-f:] *= np.linspace(1, 0, f)
    x[: int(0.002 * RATE)] *= np.linspace(0, 1, int(0.002 * RATE))
    peak = np.max(np.abs(x)) or 1.0
    x = x / peak * 10 ** (-3 / 20)
    sf.write(path, x, RATE, subtype="PCM_16")


def onsets(x, rise=6.0, gap=0.07):
    env = envelope(x)
    floor = np.percentile(env, 20) + 1e-6
    hits, last = [], -1e9
    above = env > floor * rise
    i = 1
    while i < len(env):
        if above[i] and not above[i - 1] and (i - last) / RATE > gap:
            hits.append(i)
            last = i
        i += 1
    return hits, env


# --- key strikes: slow typing, contact mic
x = load("COMType_Typewriter Olivetti Typing on keys without cover slow speed_CSND_OLTY_Contact.wav")
hits, env = onsets(x)
def strikes(seg):
    n = int(0.006 * RATE)
    e = np.convolve(np.abs(seg), np.ones(n) / n, "same")
    pk, cnt, low = e.max(), 0, True
    for v in e:
        if low and v > pk * 0.45:
            cnt, low = cnt + 1, False
        elif v < pk * 0.15:
            low = True
    return cnt


cands = []
LEN = int(0.11 * RATE)
used = set()
for k, h in enumerate(hits):
    # the typebar hit is the loudest moment within 150 ms after the key starts moving: start just before it
    win = x[h:h + int(0.15 * RATE)]
    n = int(0.003 * RATE)
    env3 = np.convolve(np.abs(win), np.ones(n) / n, "same")
    peak_i = h + int(np.argmax(env3))
    start = max(0, peak_i - int(0.006 * RATE))
    if start in used or start + LEN > len(x):
        continue
    seg = x[start:start + LEN]
    if strikes(seg) != 1:
        continue                                   # a second strike inside: skip
    used.add(start)
    tail = np.sqrt(np.mean(seg[-int(0.02 * RATE):] ** 2))
    peak = np.max(np.abs(seg))
    cands.append((tail / (peak + 1e-9), peak, start, LEN))
# clean strikes: decayed by the end; then the louder ones, skipping the very loudest
cands.sort(key=lambda c: c[0])
clean = cands[: max(30, len(cands) // 2)]
clean.sort(key=lambda c: -c[1])
pick = clean[2:26]
for i, (_, _, s, n) in enumerate(sorted(pick, key=lambda c: c[2])):
    save(x[s:s + n].copy(), os.path.join(OUT, f"key_{i + 1:02d}.wav"))
print("keys: onsets", len(hits), "candidates", len(cands), "saved", len(pick))

# --- carriage: backspace + carriage movement, events split by silence
x = load("COMType_Typewriter Olivetti Backspace key press with carriage movement_CSND_OLTY_Mono.wav")
env = envelope(x, 0.02)
floor = np.percentile(env, 15) + 1e-6
active = env > floor * 4
events, i = [], 0
while i < len(active):
    if active[i]:
        j = i
        while j < len(active) and (active[j] or np.any(active[j:j + int(0.15 * RATE)])):
            j += int(0.01 * RATE)
        events.append((i, min(j, len(x))))
        i = j
    i += int(0.005 * RATE)
saved = 0
for s, e in events:
    if (e - s) / RATE < 0.25 or (e - s) / RATE > 3.0:
        continue
    s = max(0, s - int(0.01 * RATE))
    save(x[s:e].copy(), os.path.join(OUT, f"carriage_{saved + 1:02d}.wav"), fade_ms=40)
    saved += 1
print("carriage events", len(events), "saved", saved, [round((e - s) / RATE, 2) for s, e in events])

# --- typebars fast: a 3 s chatter burst for the teletype variant
x = load("COMType_Typewriter Olivetti Typebars mechanism fast speed_CSND_OLTY_Mix.wav")
env = envelope(x, 0.05)
best = int(np.argmax(np.convolve(env, np.ones(int(3 * RATE)), mode="valid")))
seg = x[best:best + int(3 * RATE)].copy()
seg[: int(0.2 * RATE)] *= np.linspace(0, 1, int(0.2 * RATE))
save(seg, os.path.join(OUT, "chatter_01.wav"), fade_ms=300)
print("chatter from", round(best / RATE, 2))

# --- pager: 85 ms bips out of a buzzer alarm clock (one beep of 0.3 s every 0.5 s), in groups of 3 and 2
x = load("Alarm Clock Electric Beeping.wav")
POUT = os.path.join(os.path.dirname(OUT), "pager")
os.makedirs(POUT, exist_ok=True)


def bip(start, dur=0.085):
    s = x[int(start * RATE):int((start + dur) * RATE)].copy()
    f = int(0.005 * RATE)
    s[:f] *= np.linspace(0, 1, f)
    s[-f:] *= np.linspace(1, 0, f)
    return s


gap = np.zeros(int(0.065 * RATE), dtype=np.float32)
for k, (count, at) in enumerate([(3, 0.08), (2, 0.58)]):
    parts = []
    for i in range(count):
        parts.append(bip(at + 0.5 * i))
        if i < count - 1:
            parts.append(gap)
    y = np.concatenate(parts + [np.zeros(int(0.05 * RATE), dtype=np.float32)])
    sf.write(os.path.join(POUT, f"beep_{k + 1:02d}.wav"), y / np.max(np.abs(y)) * 10 ** (-3 / 20), RATE, subtype="PCM_16")
print("pager beeps saved")

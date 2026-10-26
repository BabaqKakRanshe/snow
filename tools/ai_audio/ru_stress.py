"""First pass of Russian stress for TTS: RUAccent marks the stressed vowel, and it
is written as a capital letter (прОсекой), the only marking Fish reads without
breaking the word (´ and + are spoken or mangled). One-syllable words and words
with ё are left alone. The result still needs a human read-through: RUAccent
misses some homographs (замок) and case forms (пОчту).

    uv run --python 3.12 --with ruaccent --with "transformers<4.46" python tools/ai_audio/ru_stress.py kessler

Writes tools/ai_audio/tts_<speaker>.csv (id, en, en_tts, ru, ru_tts): the text
voice_batch.py speaks. en_tts starts as en; direction tags ([sigh], [pause]) go
into en_tts/ru_tts by hand. Rows already in the file keep their hand edits; only
new ids get a first pass.
"""
import csv
import re
import sys
from pathlib import Path

LINES = Path(__file__).resolve().parents[2] / "Snow/Assets/Snow/Runtime/Voice/Resources/Snow_VoiceLines.csv"
VOWELS = "аеёиоуыэюяАЕЁИОУЫЭЮЯ"


def caps_stress(original, marked):
    """Stress from RUAccent's 'пр+осекой' onto the original text as 'прОсекой'.

    Only the stress position is taken from RUAccent: it also turns е into ё
    ('Все говорят' -> 'Всё') and drops '…', so letters and punctuation stay the
    original's. One-syllable words and words with ё are left alone."""
    stressed = {}
    for i, w in enumerate(re.findall(r"[\w+]+", marked)):
        if "+" in w:
            stressed[i] = w.index("+")

    def word(m, counter=iter(range(10 ** 6))):
        w = m.group(0)
        i = next(counter)
        pos = stressed.get(i)
        if pos is None or sum(ch in VOWELS for ch in w) < 2 or "ё" in w.lower() or pos >= len(w):
            return w
        if pos == 1 and w[0].isupper():
            return w[0].lower() + w[1].upper() + w[2:]  # 'ВЫйди' is spelled out as an abbreviation; 'вЫйди' is read
        return w[:pos] + w[pos].upper() + w[pos + 1:]

    if len(re.findall(r"\w+", original)) != len(re.findall(r"[\w+]+", marked)):
        print(f"  word count differs, check by hand: {original}")
        return original
    return re.sub(r"\w+", word, original)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    speaker = sys.argv[1]
    out = Path(__file__).with_name(f"tts_{speaker}.csv")
    done = {}
    if out.exists():
        done = {r["id"]: r for r in csv.DictReader(out.open(encoding="utf-8"))}

    rows = [r for r in csv.DictReader(LINES.open(encoding="utf-8")) if r["speaker"] == speaker]
    todo = [r for r in rows if r["id"] not in done and r["ru"]]
    if todo:
        from ruaccent import RUAccent
        acc = RUAccent()
        acc.load(omograph_model_size="turbo3.1", use_dictionary=True, tiny_mode=False)
        for r in todo:
            done[r["id"]] = {"id": r["id"], "en": r["en"], "en_tts": r["en"], "ru": r["ru"],
                            "ru_tts": caps_stress(r["ru"], acc.process_all(r["ru"]))}

    with out.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["id", "en", "en_tts", "ru", "ru_tts"])
        w.writeheader()
        for r in rows:
            w.writerow(done.get(r["id"]) or {"id": r["id"], "en": r["en"], "en_tts": r["en"], "ru": r["ru"], "ru_tts": r["ru"]})
    print(f"{len(rows)} lines ({len(todo)} new) -> {out}")


if __name__ == "__main__":
    main()

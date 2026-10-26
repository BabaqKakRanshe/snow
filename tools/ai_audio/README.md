# AI audio: takes to choose from

Local generation of SFX, music, songs and speech. Several takes per request, you listen and pick.

## What is installed (26.10.2026)

| Where | What |
|---|---|
| `F:\ClaudeGames\ai_audio\soundbox` | Soundbox 0.4.0 (MIT), built from source. UI + API at http://127.0.0.1:17494 |
| `F:\ClaudeGames\ai_audio\sbdata` | Soundbox data: audio.cpp runtime (CUDA), model weights (26 GB), library |
| `F:\ClaudeGames\ai_audio\fish-speech` | Fish Speech (Python, torch 2.8 cu129) + S2-Pro weights (11 GB). Does not run on the RTX 3080, see below |
| `F:\ClaudeGames\snow_audio_gen` | Generated takes, one folder per request |

Start: `F:\ClaudeGames\ai_audio\start_soundbox.cmd` (keep the window open).

## Models

| Short name | Model | For | Licence for a sold game |
|---|---|---|---|
| `sfx --model sfx` | Stable Audio 3.0 Small SFX | sound effects, ambience | Stability Community: free under $1M revenue/year |
| `sfx --model medium`, `music --model medium` | Stable Audio 3.0 Medium | better SFX and music, slower | same |
| `music --model small` | Stable Audio 3.0 Small Music | instrumental, loops | same |
| `music/song --model ace` | ACE-Step 1.5 Turbo | songs with lyrics, instrumental | MIT, free |
| `music/song --model minimax` | MiniMax Music 3 | songs; wants ~10 GB VRAM | conditional: revenue cap + credit in the game |
| `voice --model design` | Qwen3-TTS 1.7B VoiceDesign | a new voice from a text description | Apache 2.0, free |
| `voice` | Fish Audio S2 Pro (q8 GGUF, audio.cpp) | speech in ~100 languages, voice cloning, `[whisper]` style tags | **research / non-commercial only**: a sold game needs a paid licence from Fish Audio |

## Commands

```
python tools/ai_audio/snowgen.py sfx   "wooden door slam, small hut" -n 6 -d 3
python tools/ai_audio/snowgen.py music "dark drone, frozen forest" -n 3 -d 60 --model medium
python tools/ai_audio/snowgen.py song  "bard song, acoustic guitar" --lyrics lyrics.txt -n 2 -d 90
python tools/ai_audio/snowgen.py voice "Приём. Говорит пост." -n 4 --model design --instruct "old hoarse man, slow"
python tools/ai_audio/snowgen.py voice "Приём. Говорит пост." -n 4 --voice F:/ClaudeGames/ai_audio/voices/radio_man.wav
```

Voices: Fish only clones, it has no voice of its own. Make a character voice with `--model design --instruct "..."` (Qwen3-TTS VoiceDesign, Apache 2.0), pick the best take, copy it to `F:\ClaudeGames\ai_audio\voices\<name>.wav` with its words in `<name>.txt`, then speak every line of that character with `--voice`. Never clone a real person without their consent. `[whisper]`, `[angry]`, `[laughing]` inside the text steer Fish's delivery. Voice runs audio.cpp directly (Soundbox drops the reference transcript), so Soundbox need not be running for it.

## Game sound effects (all cues of tools/audio/audio_manifest.json)

```
python tools/ai_audio/gen_sfx.py                       # missing takes for every cue (Soundbox running)
uv run --python 3.12 --with librosa --with soundfile python tools/ai_audio/gen_sfx.py --slice
python tools/ai_audio/gen_sfx.py --pick os.branch=2,4,5 forest.knock=3
python tools/ai_audio/gen_sfx.py --switch              # once: manifest src -> gen:<cue>/pick/*.wav
python tools/audio/build_audio.py --install            # editor not in Play; then Snow/Audio/Rebuild Sound Library
```

- Prompts per cue in `sfx_prompts.json` (English, with the game's setting in them). Takes: `F:\ClaudeGames\snow_audio_gen\sfx_lib\<cue>\vNN.wav`, page `sfx_lib\index.html` (green = in the game).
- Footsteps and typewriter keys are sliced: a take asked for "a single footstep" usually holds 2–10. `--slice` cuts takes at onsets (heel+toe within 0.22 s = one step) and puts 8 single hits from different takes into `pick/`; landings get the strongest hit of each take.
- `--install` now also deletes clips in Assets that no cue lists, so replaced sounds really leave the game.

## Game lines (Snow_VoiceLines.csv)

```
uv run --python 3.12 --with ruaccent --with "transformers<4.46" python tools/ai_audio/ru_stress.py kessler
python tools/ai_audio/voice_batch.py kessler --lang en --voice F:/ClaudeGames/ai_audio/voices/kessler_alive_a.wav -n 3
python tools/ai_audio/voice_batch.py kessler --lang ru --voice F:/ClaudeGames/ai_audio/voices/kessler_alive_a.wav -n 3
python tools/ai_audio/voice_batch.py kessler --lang en --promote all=1,kes_hello_02=3
```

- `tts_<speaker>.csv` holds what is spoken: `en_tts` / `ru_tts` with direction tags (`[tired, low voice]`, `[pause]`, `[sigh]`, `[exhale]`) and Russian stress as a capital vowel (`прОсекой`). Only capitals work: `´` breaks the word, `+` is read as «плюс». `ru_stress.py` makes the first pass with RUAccent; read it through by hand (it gets `пОтом`, `пОчту`, `двЕри` wrong).
- Splice pieces (digits, call signs, code words) stay without tags: an even tone hides the seams.
- The batch refuses to run when the spoken words differ from the subtitle (tags are ignored in that check).
- Takes: `F:\ClaudeGames\snow_audio_gen\vo_<speaker>_<lang>\index.html`, clean and radio copy per take. A rerun fills only what is missing.
- `--promote` writes 48 kHz mono 24-bit into `Runtime/Voice/Resources/Voice/` (`_ru` twin for Russian) and marks the row `tts`.
- Voices: Kessler = `kessler_alive_a.wav` (picked by intonation spread 5.1 st), radio voice = `german_grim.wav` (user approved 26.10). Check every new reference: median pitch (VoiceDesign sometimes returns a female voice for a male description) and intonation spread (≥ 3 semitones; below that sounds flat).

Each run writes `v01.wav, v02.wav ...`, `takes.json` (prompt, model, seed) and `index.html` to listen. A second run with the same `--name` adds takes to the same folder. Seed of a liked take + `--seed` repeats it.

## Native Fish Speech

Installed and weights downloaded, but S2-Pro needs ~24 GB VRAM in the original form (bf16 weights 9 GB + codec + cache) and first builds the model in fp32 in RAM (~18 GB): on this PC it crashes while loading. Drama 3 (the newest Fish model, commercial use allowed under $500K) needs 48 GB VRAM and was not downloaded. Speech goes through Soundbox's quantised S2 Pro instead.

## Known Soundbox quirk

A model download can hang at "Downloading — Not started" after the file is complete (audio.cpp's helper restarts). If the Models page shows the model as installed, cancel the job.

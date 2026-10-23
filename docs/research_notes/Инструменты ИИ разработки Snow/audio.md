# Audio tools for an AI-driven Unity game (Snow)

Scope: SFX, ambience, voice, music, audio QA for Snow (Unity 6, URP, NGO 2.x, Windows 11, Claude Code + Unity/Blender MCP). Policy: $0, SFX from free packs, AI only for voice and music, voice chat via UniVoice, existing `tools/audio/build_audio.py` + `audio_manifest.json`.
Research date: 2026-10-23. About 16 web calls. Many hits were reseller/aggregator pages (tts.ai, glama.ai, mcpservers.org); these are flagged. Items marked "[prior knowledge]" were not re-verified this session and belong to the report writer's "verify" bucket.

## 1. Free/local AI generation of SFX, ambience, music and voice (quality, license, hardware, 2026 state)

### Takeaway
The 2026 change is that Stable Audio 3.0 (May 2026) shipped open-weight Small SFX, Small and Medium models under the Stability Community License, which allows commercial use below $1M annual revenue. ACE-Step 1.5 (MIT code, under 4 GB VRAM, REST API + CLI) is the strongest local music option. For voice, the cleanly commercial local options are Chatterbox (MIT, cloning), Kokoro (Apache 2.0, no cloning) and CosyVoice (Apache 2.0, reseller claim). F5-TTS weights, XTTS-v2 and MMAudio are non-commercial.

### Cited Findings
**SFX / ambience / music: Stable Audio**
- Stable Audio 3.0 family announced May 20 (2026). Open weights: **3.0 Small SFX** (sound effects, runs "on-device, such as mobile phones and consumer-grade laptops"), **3.0 Small** (music up to 2 min), **3.0 Medium** (music up to 6:20). **3.0 Large** (>6 min) is API/enterprise only, not on HF. Weights are in the HF collection `stabilityai/stable-audio-3`. LoRA training docs exist for Small and Medium. — [Stability AI news](https://stability.ai/news-updates/meet-stable-audio-3-the-model-family-built-for-artistic-experimentation-with-open-weight-models); [The Decoder](https://the-decoder.com/stability-ai-launches-stable-audio-3-0-with-up-to-six-minute-tracks-and-open-weights/); [Digital Music News](https://www.digitalmusicnews.com/2026/05/21/stability-ai-3-0-release/)
- License: the Stability AI Community License allows research, non-commercial and commercial use for individuals/orgs with under $1M annual revenue. Above that you need an Enterprise license. You own your outputs. — [Stability membership/licensing](https://stability.ai/membership); [Stability explainer](https://stability.ai/explainers/stable-audio-vs-competitors-licensing-export-rights-and-self-hosting-compared)
- The older **Stable Audio Open 1.0** (2024) was released under a "non-commercial research community agreement" per VentureBeat. Later directory listings say commercial use is allowed under the revenue threshold. **Conflict, unresolved.** It was trained on about 486k CC recordings from Freesound and the Free Music Archive. — [VentureBeat 2024](https://venturebeat.com/ai/stability-ai-debuts-new-stable-audio-open-for-sound-design); [theresanaiforthat](https://theresanaiforthat.com/model/stable-audio-open/)
- VRAM and parameter counts for Stable Audio 3.0 are not stated on the announcement page. — [Stability AI news](https://stability.ai/news-updates/meet-stable-audio-3-the-model-family-built-for-artistic-experimentation-with-open-weight-models)

**Music: ACE-Step 1.5**
- Code is MIT. The README states no separate weights license. It "runs locally with less than 4GB of VRAM": the 2B turbo model fits ≤6 GB, XL models need ≥12 GB with offload/quantization or 20 GB without. It also runs on CPU/ROCm/XPU/MPS. — [GitHub ace-step/ACE-Step-1.5](https://github.com/ace-step/ACE-Step-1.5)
- Windows launch scripts and a portable Windows package are provided. Output length is 10 s to 10 min. Lyrics in "50+ languages" (Russian not explicitly listed). Interfaces: `uv run acestep-api` REST server (port 8001), CLI wizard, Gradio UI, Python API. XL model released 2026-04-02. — [GitHub ace-step/ACE-Step-1.5](https://github.com/ace-step/ACE-Step-1.5)
- Aggregators list ACE-Step 1.5 as Apache-2.0. One lists its 5Hz LM component as MIT. **Conflict with the README (MIT).** — [ComfyUI Wiki](https://comfyui-wiki.com/en/models/ace-step/ace-step-v1-5); [dev.co](https://dev.co/ai/llms/acestep-5hz-lm-4b)

**Video-to-audio / Foley models (relevant if SFX are ever generated from gameplay capture)**
- MMAudio weights are reported as CC-BY-NC 4.0 (non-commercial). HunyuanVideo-Foley uses the Tencent Hunyuan Community License: commercial use allowed with conditions, reportedly **not licensed in EU, UK, South Korea**. ThinkSound weights are tagged apache-2.0 on HF. All from secondary sources. — [creativeainews](https://www.creativeainews.com/articles/how-to-add-sound-effects-to-ai-video-2026/); [aifilms HunyuanVideo-Foley](https://studio.aifilms.ai/blog/hunyuanvideo-foley); [HF ThinkSound card commit](https://huggingface.co/backups/ThinkSound/commit/97c736d7ae83714dc2e2ccf0307f65e7f85d7f0d)

**Voice / TTS**
- **Chatterbox** (Resemble AI): MIT on the official HF card. Emotion exaggeration control. Multilingual variant covers about 23 languages. Zero-shot cloning from one sample (third-party description). Resemble claims it is preferred over ElevenLabs in side-by-side tests (vendor claim). — [HF ResembleAI/chatterbox README](https://huggingface.co/ResembleAI/chatterbox/blob/refs%2Fpr%2F48/README.md); [tts.ai Chatterbox](https://tts.ai/voices/chatterbox/)
- **Kokoro**: 82M params, Apache 2.0, about 100x real time on GPU, no voice cloning. Language list includes Russian (vendor blog). — [tts.ai 2026 guide](https://tts.ai/blog/open-source-text-to-speech-guide-2026/?lang=ru)
- **Dia**: Apache 2.0, high quality but slow, no cloning (per same vendor table). **Orpheus**: built on Llama 3.2, so it needs "Built with Llama" attribution / the Llama license. Higgs Audio V2 and Dia2 are also listed among the leading 2026 open models. Fish Audio S2 Pro requires a paid commercial license. — [tts.ai 2026 guide](https://tts.ai/blog/open-source-text-to-speech-guide-2026/?lang=am); [dograh](https://www.dograh.com/feeds/blog/open-source-ai-voice-generator)
- **F5-TTS**: code MIT, pretrained weights **CC-BY-NC-4.0** (because of the Emilia dataset). The maintainer reportedly said fine-tunes of the Emilia base also cannot be used commercially (GitHub discussion #997, second-hand). The community **OpenF5-TTS-Base** is Apache 2.0 but weaker. — [vantaige F5-TTS](https://vantaige.io/ai-tool/f5-tts); [sourcepulse](https://www.sourcepulse.org/projects/1877241); [HF model tree OpenF5](https://huggingface.co/datasets/midah/model_trees/commit/1b86f56c560bb68d4879ece099836417ae2aec9e)
- **XTTS-v2**: Coqui Public Model License, non-commercial. Coqui shut down in Jan 2024, so there is nobody left to sell a commercial license. Treat it as non-commercial only. — [promptquorum](https://www.promptquorum.com/power-local-llm/local-tts-voice-cloning-piper-coqui-xtts)
- **CosyVoice 2 / CosyVoice 3** (Alibaba FunAudioLLM): Apache 2.0 per a reseller page; not confirmed from the repo. — [tts.ai CosyVoice3](https://tts.ai/voices/cosyvoice3-english-male/?lang=en)
- **ElevenLabs**: free tier of 10k credits/month (per MCP listing). It covers TTS, voice cloning, sound effects and soundscapes from text, and audio isolation. — [glama ElevenLabs MCP](https://glama.ai/mcp/servers/elevenlabs/elevenlabs-mcp); [truto blog](https://truto.one/blog/connect-elevenlabs-to-chatgpt-create-ai-speech-sound-fx-voices/)

### Inferences
- For Snow's $0 + commercial-safe policy, the realistic local stack is:
  - Music: ACE-Step 1.5 via its REST API (agent-drivable from Python, like `build_audio.py`), or Stable Audio 3.0 Small/Medium for instrumental/ambient beds.
  - Voice: Chatterbox (cloning, MIT), or Kokoro for fast generic voices and placeholders. Avoid F5-TTS, XTTS-v2 and MMAudio for shipped assets.
- Stable Audio 3.0 Small SFX could cover SFX gaps if the user ever relaxes "SFX only from packs". It is commercially fine while revenue stays under $1M. Note that SFX generation sits outside the current policy, so it is a user decision.
- Russian voice is the biggest unknown. Kokoro lists Russian. Chatterbox multilingual (23 langs) probably includes it but this was not confirmed. A test run per model is needed before committing.
- [prior knowledge] Meta AudioCraft (AudioGen/MusicGen) weights are CC-BY-NC 4.0, so they are non-commercial and also superseded in quality by the 2025–26 models. YuE (lyrics-to-song) is reported Apache 2.0, but it is heavy (high VRAM) and slow. Silero TTS (good Russian) has historically used non-commercial (CC BY-NC-SA) terms for its models. Verify all of these before use.

### Gaps
- No VRAM numbers for Stable Audio 3.0 and no independent quality comparison of 3.0 Small SFX against pack SFX.
- Official LICENSE files for CosyVoice 3, Chatterbox multilingual, IndexTTS2 and VibeVoice were not opened. IndexTTS2 and VibeVoice licenses were not found at all; VibeVoice was reportedly pulled/restricted by Microsoft in 2025 [prior knowledge, unverified].
- Whether Stable Audio Open 1.0 outputs are commercially usable today (sources conflict).
- Russian quality per TTS model: no sourced benchmarks found.

## 2. Free SFX libraries and how an agent can search/download/tag them

### Takeaway
The Sonniss GDC 2026 bundle (about 7.5 GB, 17 vendors, royalty-free, no attribution) is the best bulk $0 source, but it bans AI/ML training use. Freesound is the searchable source for agents: a community MCP server (`freesound-mcp`) wraps API search and download with license filtering.

### Cited Findings
- Sonniss GDC 2026 Game Audio Bundle: free, about 7.47 GB from 17 vendors. No attribution required, unlimited projects, lifetime use. — [Rekkerd](https://rekkerd.org/sonniss-releases-gdc-2026-game-audio-bundle/)
- The Sonniss archive license **prohibits AI/ML training use**. — [Zeli](https://zeli.app/en/story/49262841)
- The cumulative multi-year GDC archive is said to be over 200 GB. Sonniss has distributed via BitTorrent because of hosting costs (date unclear). — [Cinevva guide](https://app.cinevva.com/guides/free-sound-effects-music); [TouchArcade](https://toucharcade.com/?p=166172)
- `freesound-mcp` (mushan-bit, Node.js, stdio) has two tools:
  - `freesound_search(query, maxDuration?, license?)` returns id, name, duration, license and preview URL.
  - `freesound_download(id, quality, dir)`.
  - It needs the `FREESOUND_API_KEY` env var. This is community-made, not official, and its maintenance status is unknown. — [mcpservers.org](https://mcpservers.org/en/servers/mushan-bit/freesound-mcp); [glama](https://glama.ai/mcp/servers/cr8tbh7alc)

### Inferences
- An agent can tag a local pack (Sonniss and similar) with no API: run CLAP embeddings or the Qwen3-Omni Captioner (section 3) over the files, write tags and descriptions into a JSON index next to `audio_manifest.json`, then search by text. This is inference/retrieval, not training, so it likely stays inside the Sonniss "no AI/ML training" clause. That reading is an interpretation; the clause text was not read.
- [prior knowledge] Freesound API v2 offers text search, filters (license, duration, tags), previews (MP3/OGG) with a token, and original-file downloads, which require OAuth2. Each sound has its own CC license (CC0 / CC-BY / CC-BY-NC). An agent must filter out NC and record CC-BY attributions in a credits file. Docs: https://freesound.org/docs/api/
- [prior knowledge] Other $0 sources: Kenney audio packs (CC0), OpenGameArt (mixed licenses), BBC Sound Effects (RemArc license, non-commercial, so unsuitable), Sonniss archive years 2015–2026.

### Gaps
- Exact Sonniss license wording on the AI/ML clause (does it cover analysis/embedding, or only training?).
- Freesound OAuth2 requirements for original-quality downloads were not re-verified this session.

## 3. How an AI agent can "hear" or check audio

### Takeaway
Use two layers. (a) Objective metrics from cheap local tools: ffmpeg `loudnorm`/`ebur128`, `astats`, `silencedetect`, pyloudnorm, spectrogram PNGs the agent can view. (b) Semantic "listening" with an audio captioner: Qwen3-Omni-30B-A3B-Captioner (Apache 2.0, local, heavy) or the Gemini API (cloud, understands non-speech sounds, 32 tokens/sec of audio). Whisper covers speech and voice-line verification.

### Cited Findings
- Qwen3-Omni-30B-A3B-Captioner is open source under Apache 2.0, alongside the Instruct and Thinking variants. It is a "general-purpose, highly detailed, low-hallucination audio captioning model". It describes speech, music, ambient sounds and effects without a prompt. — [HF Qwen3-Omni Instruct card](https://huggingface.co/Qwen/Qwen3-Omni-30B-A3B-Instruct); [arXiv 2509.17765](https://arxiv.org/abs/2509.17765); [AIML API doc](https://docs.aimlapi.com/api-references/text-models-llm/alibaba-cloud/qwen3-omni-30b-a3b-captioner)
- A community AWQ 4-bit quant of Qwen3-Omni exists, which lowers VRAM needs. — [HF cpatonn AWQ](https://huggingface.co/cpatonn/Qwen3-Omni-30B-A3B-Instruct-AWQ-4bit)
- Gemini API audio understanding:
  - It understands non-speech sounds such as birdsong and sirens.
  - Audio costs 32 tokens per second (older docs said 25).
  - Max 9.5 h of audio per prompt.
  - Audio is downsampled to 16 Kbps mono, so it cannot judge stereo or fine fidelity.
  - Official price per audio token was not found. — [Google AI docs](https://ai.google.dev/gemini-api/docs/audio?lang=node)

### Inferences
- [prior knowledge] Deterministic QA toolkit, all free and CLI/Python friendly (an agent can drive every one of them):
  - **Loudness**: `ffmpeg -af ebur128` / `loudnorm=print_format=json` for integrated LUFS, true peak and LRA ([ffmpeg filters](https://ffmpeg.org/ffmpeg-filters.html#loudnorm)); `pyloudnorm` (ITU-R BS.1770) in Python.
  - **Clipping/DC/RMS**: `ffmpeg -af astats`; `sox stat`.
  - **Silence and trimming**: `silencedetect`; `silenceremove`.
  - **Spectrogram PNG for the agent to look at**: `ffmpeg -lavfi showspectrumpic`; `sox spectrogram`; librosa + matplotlib.
  - **Speech checks**: Whisper / faster-whisper turns a TTS line back into text, then compare it with the script (WER) to catch garbled AI voice lines.
  - **Text-audio similarity**: LAION-CLAP / MS-CLAP give a score for "does this file match the label 'wooden door creak'". This is useful both for search and for rejecting bad generations.
- Practical pattern for `build_audio.py`: add a `qa` step that writes per-file JSON (LUFS, true peak, duration, leading/trailing silence, clip count, CLAP score against the manifest description, Whisper transcript for voice) plus one spectrogram PNG. The agent reads JSON and PNG and needs no human ear for first-pass checks.
- Gemini's 16 Kbps mono downsampling means it suits "what is this sound" checks, not mix-quality judgments.

### Gaps
- No sourced VRAM figure for running the Captioner locally on Windows. 30B-A3B MoE in 4-bit is likely about 20 GB or more; not verified.
- Qwen2-Audio and other audio LLMs (e.g., Kimi-Audio, Audio Flamingo) were not researched this session.
- No benchmark comparing CLAP vs captioner accuracy on game SFX.

## 4. Batch processing pipelines and Unity import automation

### Takeaway
ffmpeg, sox, pedalboard and librosa cover all batch DSP headlessly. Audacity and REAPER can be scripted (and now have MCP servers, see section 6) when a human-style edit is needed. Unity import settings are best enforced by an `AssetPostprocessor` that reads the same manifest.

### Cited Findings
- Audacity can be driven externally via mod-script-pipe, which must be enabled manually in Preferences. MCP servers build on it. — [webboxesdeveloper/mcp-audacity](https://github.com/webboxesdeveloper/mcp-audacity); [AudacityMCP](https://mcpservers.org/servers/xdarkzx/audacity-mcp)
- The Unity MCP available in this project exposes `Unity_AudioClip_Edit` and `Unity_ManageAsset` tools. This was seen in the session's own tool list, not a web source.

### Inferences
- [prior knowledge] Recommended headless chain (all free, Windows OK):
  - `ffmpeg` for convert, resample, `loudnorm` two-pass and trim.
  - `sox` for effects and quick stats.
  - Spotify `pedalboard` (GPL-3.0, Python, VST3 host plus built-in EQ/reverb/compressor/distortion) for "radio", "muffled through wall" and similar variants.
  - `librosa` for analysis.
  - `pyloudnorm` for normalization targets.
- [prior knowledge] Unity side:
  - An editor `AssetPostprocessor.OnPreprocessAudio` sets `AudioImporterSampleSettings` (loadType: DecompressOnLoad for short SFX, CompressedInMemory for medium, Streaming for music/ambience; compressionFormat Vorbis/ADPCM; quality; forceToMono for 3D SFX; preloadAudioData) by folder or manifest tag.
  - This makes import deterministic and agent-proof.
  - Addressables groups for music/ambience streaming are optional at Snow's scale.
- Keep the manifest as the single source of truth: Python writes processed files plus `import_rules.json`, and the C# postprocessor reads it. The agent then edits data, not import settings by hand. This matches the "edit data, not scene" lesson in memory.

### Gaps
- No current (2026) source on Unity 6 AudioImporter API changes was fetched.
- Licensing of pedalboard (GPL) for build-time tooling only: it is fine because it is not shipped, but this was not verified.

## 5. Unity audio middleware/runtime options and AI-friendliness

### Takeaway
FMOD and Wwise both have community MCP servers that drive their authoring tools (FMOD via its Studio scripting terminal, Wwise via WAAPI). Unity's built-in audio has no extra dependency and is already reachable through the Unity MCP. For a $0 co-op horror game of Snow's size, built-in audio plus Steam Audio (spatial/occlusion) is the lowest-risk path. FMOD is the upgrade path if adaptive music or complex events are needed.

### Cited Findings
- **FMOD MCP servers (community):**
  - `fmod-mcp` (jmperez127): drives the running FMOD Studio through its scripting terminal over TCP, 35 tools (events, audio import, bus routing, effects). Needs Studio 2.02+ and Python 3.11+.
  - `fmod-studio-mcp` (EYamanS): one tool per Scripting API member, no eval, alpha.
  - FmodStudioMCP (Daniel Hodgetts): events, import, params, buses, banks, builds.
  - Listings advise saving and version-controlling the project because they edit live. — [glama fmod-studio-mcp](https://glama.ai/mcp/servers/EYamanS/fmod-studio-mcp); [mcpmarket FMOD](https://mcpmarket.com/server/fmod-studio-1)
- **Wwise MCP servers (community):**
  - SK Wwise MCP: 12 servers of ≤15 tools each over WAAPI, stdio, Apache 2.0.
  - Another Wwise MCP has 45 tools including SoundBank generation and profiler capture. — [glama sk-wwise-mcp](https://glama.ai/mcp/servers/silver-rain-dev/sk-wwise-mcp); [mcpmarket Wwise](https://mcpmarket.com/server/wwise-2)

### Inferences
- [prior knowledge, verify]
  - **FMOD** has a free Indie license for small developers (historically under $200k revenue and a small dev budget, one game, with a credit/logo requirement). The licensing page could not be fetched this session (empty response): https://www.fmod.com/licensing
  - **Wwise** has a free tier for small budgets (historically a capped number of sound assets).
  - **Steam Audio** (Valve) has been open source under Apache 2.0 since 2024, with a Unity plugin for HRTF, occlusion and reflections: https://github.com/ValveSoftware/steam-audio
  - **Resonance Audio** (Google) is effectively unmaintained (archived), so treat it as outdated.
- Middleware means a second authoring tool, a separate bank build step and a different runtime API. Every extra layer is more surface for the agent to break. The MCP servers lower that cost, but all are community or alpha.
- Snow's existing `Runtime/Audio` code is built on Unity built-in audio, per the memory note "package 1 compiled". Switching to middleware is a large architectural change and should be a user decision.

### Gaps
- Current 2026 FMOD and Wwise free-tier thresholds (page fetch failed).
- Whether Steam Audio's Unity plugin is verified on Unity 6000.0 + URP in 2026.
- Maintenance and star counts for each FMOD/Wwise MCP were not verified.

## 6. MCP servers for audio

### Takeaway
Agent-drivable audio MCPs now exist for ElevenLabs (official), Freesound, Audacity, REAPER, FMOD and Wwise. Only ElevenLabs' is official; the rest are community projects of unknown maturity.

### Cited Findings
- **ElevenLabs MCP (official, `elevenlabs/elevenlabs-mcp`)**: TTS with voice/stability/speed/style settings, voice cloning, sound-effect and soundscape generation from text, audio isolation, playback. Needs an API key; free tier about 10k credits/month. — [glama official listing](https://glama.ai/mcp/servers/elevenlabs/elevenlabs-mcp); [Algolia docsearch](https://docsearch.algolia.com/mcp/docs/repo/elevenlabs/elevenlabs-mcp)
- **Audacity**:
  - `webboxesdeveloper/mcp-audacity` uses mod-script-pipe and is launched with uv.
  - `xdarkzx/Audacity-MCP` (PyPI `audacity-mcp`) has 131 tools and supports **Audacity 3.x only; 4.x not supported yet**. — [GitHub mcp-audacity](https://github.com/webboxesdeveloper/mcp-audacity); [mcpservers AudacityMCP](https://mcpservers.org/servers/xdarkzx/audacity-mcp)
- **REAPER**:
  - `itsuzef/reaper-mcp` works in OSC and ReaScript modes.
  - `bonfire-audio/reaper-mcp` (pip `reaper-mcp-server`, python-reapy) has 58 tools incl. mixing, mastering, rendering and audio analysis.
  - `TwelveTake-Studios/reaper-mcp` has 129 tools (unverified).
  - `shiehn/total-reaper-mcp` also exists. — [glama itsuzef](https://glama.ai/mcp/servers/itsuzef/reaper-mcp); [playbooks](https://playbooks.com/mcp/itsuzef/reaper-mcp); [enterprisedna TwelveTake](https://enterprisedna.co/directories/mcp/twelvetake-studios-reaper-mcp); [glama total-reaper dev guide](https://glama.ai/mcp/servers/@shiehn/total-reaper-mcp/blob/71b82bfc6e960d2433c536050de0291394415800/reaper_mcp_development_guide.md)
- **Multi-DAW**: `vecnode/dawmcp` is a Rust server covering both REAPER and Audacity. — [glama dawmcp](https://glama.ai/mcp/servers/vecnode/dawmcp)
- **Freesound**: `mushan-bit/freesound-mcp` (see section 2). — [mcpservers.org](https://mcpservers.org/en/servers/mushan-bit/freesound-mcp)
- **FMOD / Wwise**: see section 5.

### Inferences
- The ElevenLabs free tier conflicts with "$0 local" only on scale, not cost. 10k credits/month is a few minutes of TTS. It is useful for reference or temp voices, but ElevenLabs free-tier output historically requires attribution and bars commercial use (that needs a paid plan) [prior knowledge, verify]. So it is not a source of shipped assets for Snow.
- REAPER needs a paid license after its evaluation period (US$60 discounted) [prior knowledge], so it does not fit $0. Audacity (free) + AudacityMCP is the $0 DAW route, but pin Audacity to 3.x.
- A self-built thin MCP (or simply CLI scripts called by Claude Code) around ffmpeg, pyloudnorm, CLAP, faster-whisper and the ACE-Step/Chatterbox REST servers is likely more reliable than community DAW MCPs. Snow already uses a Python `build_audio.py`, which matches the "scripts over tool calls" rule.

### Gaps
- No official MCP from Unity for audio specifically, beyond the general Unity MCP tool `Unity_AudioClip_Edit`.
- Licenses and maintenance of community MCPs were not checked repo by repo.

## 7. Voice chat / radio effect tooling for co-op horror

### Takeaway
UniVoice (adrenak) is active (last push March 2026). It is networking-agnostic with built-in NGO support, Opus, RNNoise and VAD, so it fits Snow's choice. Proximity and radio effects are built on top: position the peer's AudioSource and add a DSP chain (band-pass + distortion + noise + squelch).

### Cited Findings
- UniVoice is networking-agnostic and has built-in support for Mirror, FishNet and Netcode for GameObjects. Features: Opus encode/decode, RNNoise noise removal, energy-based VAD, group chat with per-peer muting. — [UniVoice README (unpkg 4.12.0)](https://unpkg.com/com.adrenak.univoice@4.12.0/README.md); [GitHub adrenak/univoice](https://github.com/adrenak/univoice)
- Activity: last push 2026-03-10, about 448 stars, 45 forks (aggregator figure). — [gittrend](https://gittrend.io/repo/adrenak/univoice)
- Dissonance was in 2022 the only known option working with NGO. One developer reported about 450 ms latency with Dissonance on LAN (anecdotal, possibly outdated). — [Unity forum 2022](https://forum.unity.com/threads/voice-chat-with-netcode.1362802/); [csub blog](https://solaire.cs.csub.edu/aestus/voice-chat-in-unity-and-the-problems-therein/)
- There is a paid Asset Store package "NetVoice — Voice Chat for Netcode for GameObjects". — [Asset Store](https://assetstore-fallback.unity.com/packages/tools/audio/netvoice-voice-chat-for-netcode-for-gameobjects-309337)

### Inferences
- [prior knowledge, verify]
  - Vivox is Unity's hosted voice service (UGS). It has positional channels and a free usage tier, but it is cloud-dependent and needs UGS project linking.
  - Dissonance is a paid Asset Store package with built-in proximity rooms and NGO integration.
  - Given $0 and the existing choice, UniVoice stays the right pick.
- Radio effect: implement as Unity `OnAudioFilterRead` on the received-voice AudioSource, or with built-in AudioHighPassFilter + AudioLowPassFilter (about 300–3400 Hz band) + AudioDistortionFilter, plus key-up/key-down squelch clips from the SFX packs. Pre-render radio variants of AI voice lines offline with pedalboard (section 4) to keep runtime cheap. This fits the existing ForestRadio / T-reply design in memory.
- Proximity: attach the UniVoice peer output to the remote player's head transform with spatialBlend=1 and a custom rolloff. For walls and occlusion, Steam Audio, or a raycast-driven low-pass, gives the horror "muffled behind door" effect.

### Gaps
- No 2026 primary sources for Vivox pricing or Dissonance status/pricing.
- No source on UniVoice latency measurements with NGO 2.x.

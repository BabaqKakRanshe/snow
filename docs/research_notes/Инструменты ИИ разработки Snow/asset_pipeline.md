# AI-assisted asset pipeline for Snow (Unity 6 URP, PSX look, Windows 11, ~$0) — state as of Oct 2026

Scope note: ~27 searches/fetches. Primary sources (GitHub, HF model cards, vendor docs, Unity manual) are marked as such. Many 2026 "best of" roundups are vendor-written (Cinevva, Sorceress, Tripo blog), so treat their claims with caution. Anything I could not source this session sits under "Gaps".

## 1. Image/text-to-3D: local vs cloud, licenses, topology/UV, retopology, LOD

### Takeaway
The best model you can run locally with a clean commercial license is **Microsoft TRELLIS.2 (MIT)**. But it officially needs Linux and a 24 GB NVIDIA GPU, so on a Windows box it means WSL2 or a community port. **Hunyuan3D 2.1** is the strongest Windows-friendly local option (shape ~10 GB, texture ~21 GB VRAM, ComfyUI wrapper). Its Tencent community license **excludes the EU, UK and South Korea**, so check where the developer and players are. Hunyuan 3.0 is hosted/API-only. **Stable Fast 3D / SPAR3D** are fast and low-VRAM, but they come under the Stability Community License, which is free only **under $1M annual revenue**. Meshy and Tripo free tiers give **CC BY 4.0** output (commercial use only with attribution). Raw AI meshes are dense and triangulated, so plan on a decimate/retopo and LOD step: Blender QuadriFlow, Instant Meshes, or Unity 6.2+ built-in Mesh LOD.

### Cited Findings
**TRELLIS.2 (Microsoft)**
- 4B-parameter image-to-3D model, **MIT license** for code and model. Dependencies nvdiffrast/nvdiffrec have their own licenses — [GitHub microsoft/TRELLIS.2](https://github.com/microsoft/TRELLIS.2); [HF model card](https://huggingface.co/microsoft/TRELLIS.2-4B/blob/main/README.md)
- Hardware: "An NVIDIA GPU with at least 24GB of memory is necessary". Verified on A100/H100, CUDA 12.4, **"tested only on Linux"**, no Windows mention — [GitHub](https://github.com/microsoft/TRELLIS.2)
- Output: GLB (WebP textures) + MP4 preview. PBR maps: Base Color, Roughness, Metallic, Opacity. Alpha is preserved but has to be wired manually (export is OPAQUE by default) — [GitHub](https://github.com/microsoft/TRELLIS.2)
- O-Voxel representation handles open surfaces and non-manifold geometry. The CuMesh package does UV unwrapping. Example export uses `decimation_target=1000000`, `texture_size=4096` and optional remeshing, so the default output is far above a PSX budget — [GitHub](https://github.com/microsoft/TRELLIS.2)
- Has a **texturing-only mode** (`example_texturing.py`, `app_texturing.py`) that makes PBR textures for an existing mesh. This matters for "keep my Blender mesh, AI-texture it" — [GitHub](https://github.com/microsoft/TRELLIS.2)
- Timing on H100: ~3 s at 512³, ~17 s at 1024³, ~60 s at 1536³ — [GitHub](https://github.com/microsoft/TRELLIS.2)
- Released Dec 2025. The released model has no preference-alignment stage, so meshes may have small holes — [ComfyUI-wiki news](https://comfyui-wiki.com/en/news/2025-12-18-microsoft-trellis2-3d-generation)
- A Sept 2026 roundup still calls TRELLIS.2 "the best model you can download and run" (secondary, vendor blog) — [Cinevva timeline 2026](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)

**Hunyuan3D (Tencent)**
- 2.1 (released Jun 13, 2025): fully open weights + training code, **PBR texture pipeline**, supports **macOS, Windows, Linux**. VRAM: shape ~10 GB, texture ~21 GB, both ~29 GB. Repo ships `api_server.py` + `API_DOCUMENTATION.md`, so an agent can call it over local HTTP. Community integrations listed for ComfyUI and Unity — [GitHub Tencent-Hunyuan/Hunyuan3D-2.1](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1)
- License: Tencent Hunyuan 3D 2.0 Community License. Territory = "worldwide **excluding the European Union, United Kingdom and South Korea**". 2.1 uses the same carve-out — [HF Hunyuan3D-2 LICENSE](https://huggingface.co/tencent/Hunyuan3D-2/blob/main/LICENSE); [ScanCode license DB](https://scancode-licensedb.aboutcode.org/tencent-hunyuan-3d-2.0-cla.html); [2.1 LICENSE mirror](https://gitserver.onethingai.com/ai-models/Hunyuan3D-2.1/raw/branch/main/LICENSE)
- Hunyuan3D-Omni carries the same three-region exclusion — [DeepWiki](https://deepwiki.com/Tencent-Hunyuan/Hunyuan3D-Omni/6.1-licensing-and-legal)
- **3.0 is hosted/API**: Tencent launched the Hunyuan 3D engine and an English API on Tencent Cloud on 25 Nov 2025. The last open weights are 2.1 (secondary sources, not confirmed on Tencent's GitHub) — [aiwiki](https://aiwiki.ai/wiki/hunyuan_3d); [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026); [ToolNavs (KO)](https://toolnavs.com/ko/article/275-hunyuan3d-30-is-coming-ai-triple-accuracy-and-15363-resolution-3d-dit-reconstruc)
- Do not confuse this with the Hunyuan **LLM** "Hy3", which moved to Apache 2.0 in Jul 2026 and dropped the region limits. That change does not apply to Hunyuan3D — [search summary of aicoin report](https://www.aicoin.com/en/news-flash/2969704)
- ComfyUI route: kijai **ComfyUI-Hunyuan3DWrapper** is "workable, especially under Windows". Recommends ≥8 GB VRAM + 16 GB RAM. The texture stage needs a compiled `custom_rasterizer`, and Windows wheels are shipped. It installs via ComfyUI Manager. Last update listed 2026-07-29 — [Habr](https://habr.com/en/articles/878770); [ComfyUI-wiki guide](https://comfyui-wiki.com/en/tutorial/advanced/3d/huanyuan3d-2); [RunComfy listing](https://www.runcomfy.com/comfyui-nodes/ComfyUI-Hunyuan3DWrapper)

**Stability: Stable Fast 3D / SPAR3D**
- Community License covers individuals and organisations with **< $1M annual revenue** and lists Stable Fast 3D. Above $1M you need an Enterprise license — [stability.ai/license](https://stability.ai/license); [HF stable-fast-3d (gated)](https://huggingface.co/stabilityai/stable-fast-3d)
- SPAR3D is under the same Community License (secondary source only) — [mind-verse.de](https://mind-verse.de/news/neueste-entwicklungen-3d-modellierung-spar3d-stability-ai)

**Cloud services**
- **Meshy** Free plan: 100 credits/month, output under **CC BY 4.0** (commercial OK with attribution), user owns output. Paid plans are private. One generation costs 20 credits (Meshy 6/7) — [Meshy pricing docs](https://docs.meshy.ai/en/webapp/pricing). A vendor blog claims a "commercial use disabled" overlay on Free, which contradicts the official docs — [Sorceress](https://sorceress.games/blog/best-free-ai-3d-model-generator-honest-2026-picks)
- **Tripo**: free output is reportedly CC BY 4.0. Private commercial rights come with Pro (~$14/mo annual, 3,000 credits). Free-tier credits are reported as 200 vs 300/month (conflict) — [Tripo blog](https://www.tripo3d.ai/blog/cheaper-meshy-alternative-private-commercial-use-3d-models); [Costbench](https://www.costbench.com/compare/meshy-vs-tripo-ai/)
- Tripo's newest hosted engines are H3.1 and P1.0 (announced 9 Mar 2026) — [Cinevva timeline](https://app.cinevva.com/guides/ai-3d-model-generation-timeline-2026)

**Retopology / decimation / LOD**
- Blender's built-in Remesh → QuadriFlow is based on Instant Field-Aligned Meshes. Instant Meshes is a free standalone tool (JRemesh add-on wraps it). Its remeshes can leave holes — [JRemesh (Superhive)](https://superhivemarket.com/products/jremesh-tools); [80.lv](https://80.lv/articles/a-new-open-source-auto-retopology-tool)
- **AutoRemesher** (Jeremy Hu) is an open-source auto-retopology tool, and a Blender add-on exists — [80.lv](https://80.lv/articles/a-new-open-source-auto-retopology-tool)
- **Unity 6.2+ built-in Mesh LOD**: in the model importer, Mesh → LODs → "Generate mesh LODs". It runs at import time (slower import) and has settings for Discard Odd Levels, Limit LODs / Maximum Levels and Keep Quads. It can silently produce no LODs if the mesh doesn't meet the conditions — [Unity 6.2 manual: generate LODs](https://docs.unity3d.com/6000.2/Documentation/Manual/lod/mesh-lod-generate-lods.html); [FBX importer Model tab](https://docs.unity3d.com/6000.2/Documentation/Manual/FBXImporter-Model.html)
- **Simplygon** Unity plugin is "available for all license types, including Free": reduction, aggregation, remeshing, impostors/billboard clouds (vegetation), occlusion meshes. It depends on the Unity USD package — [Simplygon Unity docs 10.1](https://documentation.simplygon.com/SimplygonSDK_10.1.400.0/unity/). The current free-tier terms are unclear: older indie terms had a 2% royalty above $25k/quarter, and another older source says "100% free" — [gamefromscratch](https://gamefromscratch.com/?p=8669); [cgpress](https://cgpress.org/archives/simplygon-launches-open-access-business-model.html)

### Inferences
- For Snow's PSX look, a high-poly 1M-tri / 4K-texture output is wasted. The useful flow is generate → decimate to a few hundred to low thousands of tris (Blender Decimate/QuadriFlow via MCP `execute_blender_code`) → bake to 128–512 px → `psx_texture_tool.py`. Low budgets also hide AI topology flaws.
- Licensing ranking for a commercial Steam release with near-$0 budget: TRELLIS.2 (MIT) > Stable Fast 3D/SPAR3D (fine under $1M) > Hunyuan3D 2.1 (fine only if neither the developer nor distribution falls in the EU/UK/KR, and the license text should be read for those exact terms) > Meshy/Tripo free (CC BY attribution in credits).
- TRELLIS.2 on Windows: run it inside WSL2 Ubuntu with CUDA, or rent a cloud GPU. A 24 GB card is required either way. Hunyuan3D 2.1 shape-only (~10 GB) plus texturing in Blender/StableGen fits consumer GPUs better.
- The Blender MCP `generate_3d` tool already wraps a hosted generator (it "may cost the user a credit"). For many props, the cheapest path is the existing `search_assets` → Poly Haven (CC0) / Poly Pizza before generating anything.

### Gaps
- No Windows-native TRELLIS.2 port or ComfyUI-Trellis2 node was confirmed this session.
- Rodin (Hyper3D) pricing/license and TripoSR (believed MIT) / Tripo official ToS were not verified.
- The user's GPU VRAM is unknown, and it decides which local models are viable.
- No independent 2026 benchmark of topology/UV quality across generators was found (only vendor roundups).

## 2. Textures / materials: AI PBR, tiling, upscaling, PSX downscale

### Takeaway
The practical free stack is: **Material Maker** (MIT, procedural PBR, has Unity URP export) for tileable ground/snow/bark/rock; **StableGen** (GPL-3 Blender add-on → ComfyUI, SDXL/FLUX projection texturing) or **TRELLIS.2 texturing mode** to paint existing meshes; Codex image gen for albedo concepts; then the project's own `psx_texture_tool.py` for downscale/dither. **Unity's Material and Texture2D Generators** exist in-editor but cost Unity points/credits.

### Cited Findings
- **Material Maker**: MIT, ~250 nodes, PBR. Has export templates for Unity/Unreal/Godot. 1.4 added a **Unity URP lit** export target. Dynamic materials are translated GLSL→HLSL. Latest reported version is **1.7 (Jul 2026, Godot 4.7)**. Release date of 1.4 is reported inconsistently (2023 vs 2025) — [CG Channel](https://www.cgchannel.com/2025/10/material-maker-1-4/); [Digital Production 1.7](https://digitalproduction.com/2026/07/23/material-maker-1-7-improves-graph-workflow/); [itch.io](https://rodzilla.itch.io/material-maker)
- **StableGen**: Blender add-on that drives a ComfyUI backend with SDXL / FLUX.1-dev. Textures all visible meshes from defined camera viewpoints, with a grid preview mode. Needs Blender 4.2+, Windows 10/11 or Linux, NVIDIA GPU recommended. Licensed **GPL v3** (the add-on code; generated textures aren't the add-on) — [GitHub](https://github.com/Zaws77/StableGen); [SourcePulse](https://www.sourcepulse.org/projects/17250649)
- Texturaizer is a separate paid ComfyUI-Blender add-on (commercial/non-commercial tiers) — [Superhive](https://superhivemarket.com/products/texturaizer)
- TRELLIS.2 can texture an existing mesh with PBR (texturing-only scripts) — [GitHub](https://github.com/microsoft/TRELLIS.2)
- Hunyuan3D 2.1 Paint makes PBR textures for a given mesh (~21 GB VRAM) — [GitHub](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1)
- **Unity Generators** include a **Texture2D Generator**, a **Material Generator (PBR)**, a **Terrain Layer Generator** and a Cubemap Generator (package `com.unity.ai.generators`; newer versions need Unity 6.3) — [Generators manual 1.0](https://docs.unity3d.com/Packages/com.unity.ai.generators@1.0/manual/index.html); [1.6](https://docs.unity3d.com/Packages/com.unity.ai.generators@1.6/manual/index.html)
- Mixamo, Poly Haven and others are reachable via Blender MCP `search_assets`/`import_asset` (tool description in this session)

### Inferences
- For a PSX look, AI texture detail mostly gets thrown away at 64–256 px. Tileable procedural sources (Material Maker graphs saved in repo) are more reproducible and style-controllable than diffusion outputs, and the agent can re-export them headless.
- A reusable "palette + dither" step (`psx_texture_tool.py`) is where style is enforced, so every AI texture source should go through it. That keeps the human in control of style.
- Material Maker graph files (.ptex) are text/JSON. An agent can edit parameters and re-export, but CLI export support was not verified.

### Gaps
- Real-ESRGAN license (believed BSD-3) and Windows ncnn-vulkan binary were not verified this session. For PSX, upscaling is rarely needed anyway.
- Dream Textures (Blender SD add-on) maintenance status in 2026 was not checked.
- No sourced ComfyUI "seamless tiling" node info was gathered (circular-padding/tiling nodes exist in community packs, unverified).
- Whether Material Maker has a headless/CLI export usable by an agent was not verified.

## 3. Animation: video mocap, text-to-motion, auto-rig, retarget into Unity Humanoid

### Takeaway
For commercial use at $0 the safe pieces are: **Mixamo** (free, still up, maintenance mode, raw files can't be redistributed), **AccuRIG 2** (free auto-rig + ActorCore motion retarget, FBX with Unity presets), **FreeMoCap** (AGPL, multi-webcam), **UniRig** (code reportedly MIT; check checkpoint/data terms), and **HY-Motion 1.0** (Tencent text-to-motion, commercial use allowed per secondary reports except >100M MAU). Academic video-to-motion (GVHMR, WHAM) depends on **SMPL, which is non-commercial**, so it's unusable for shipping. Rokoko Vision was folded into Rokoko Create in Jul 2026, and its free tier is unclear.

### Cited Findings
- **Mixamo**: still running in mid-2026, free with an Adobe account, maintenance mode, no major update since 2015 acquisition. Several-day outage Jun 2025. Official Blender add-on retired. Terms allow commercial use, but raw files can't be redistributed (vendor-written guides) — [Cinevva guide](https://app.cinevva.com/guides/free-character-animations-rigging); [Cinevva Mixamo→Blender 2026](https://app.cinevva.com/guides/mixamo-to-blender-2026)
- **AccuRIG 2.0** (Reallusion/ActorCore, Jul 2025): free, needs a free ActorCore account to export. FBX/USD export with Unity presets. Can browse ActorCore motions, retarget them, and export with the character. EULA text not found — [CG Channel](https://www.cgchannel.com/?p=168596); [Digital Production](https://digitalproduction.com/2025/07/21/animation-for-all-reallusion-accurig-2-0-and-friends-keep-things-moving/); [ActorCore export page](https://actorcore.reallusion.com/static-page/auto-rig/pre_page/upload-export/upload-export.html)
- **UniRig** (VAST, SIGGRAPH 2025): transformer predicts a skeleton, then a cross-attention model predicts skinning weights. Repo reportedly MIT, but checkpoints and the Rig-XL/VRoid/Objaverse data have their own terms. Some checkpoints were listed as "coming soon". Skinning degrades with a bad skeleton — [HF VAST-AI/UniRig](https://huggingface.co/VAST-AI/UniRig/tree/main); [SourcePulse](https://www.sourcepulse.org/projects/2180219); [ComfyUI-wiki](https://comfyui-wiki.com/fr/news/2025-04-12-unirig-one-model-to-rig-them-all)
- **HY-Motion 1.0** (Tencent, Dec 2025/Jan 2026): ~1B-param DiT flow-matching text-to-motion model, trained on >3,000 h of motion. Weights at huggingface.co/tencent/HY-Motion-1.0 — [arXiv 2512.23464](https://arxiv.org/abs/2512.23464v1). License: "Tencent HY-Motion 1.0 Community License", free incl. commercial except platforms >100M MAU (cgworld). Another source just says "Hunyuan Community License". **Not verified on the primary LICENSE**, and whether the EU/UK/KR carve-out applies is unknown — [cgworld](https://cgworld.jp/flashnews/01-202601-HY-Motion.html); [weel.co.jp](https://weel.co.jp/media/tech/hy-motion-1-0/)
- **GVHMR** weights: "non-commercial research". SMPL/SMPL-X are MPI registration-gated and can't be redistributed. The ComfyUI-MotionCapture wrapper warns against commercial use. WHAM also needs SMPL — [HF gvhmr](https://huggingface.co/ryanrudes/gvhmr); [ComfyUI-MotionCapture](https://github.com/PozzettiAndrea/ComfyUI-MotionCapture); [WHAM GitHub](https://github.com/yohanshin/WHAM)
- **FreeMoCap**: open source **AGPL**, maintainers open to other terms for commercial use. Markerless, 2+ USB webcams + printed Charuco board, Blender integration. GUI via `python -m freemocap` (v1.1.0 on PyPI) — [PyPI](https://pypi.org/project/freemocap/1.1.0/); [CG Channel 2021](https://www.cgchannel.com/2021/07/the-freemocap-project-markerless-mocap-for-under-100)
- **Rokoko Vision** became part of Rokoko Create on 16 Jul 2026. Dual-cam and online webcam capture were retired on 16 Aug 2026 — [Rokoko support](https://support.rokoko.com/hc/en-us/articles/48823033216017-Rokoko-Vision-and-Rokoko-Create-What-you-need-to-know). Studio Basic ~$10/mo annual incl. 600 s/month Vision AI (reseller listing). The free tier lacks BVH and advanced export — [Rokoko plans](https://support.rokoko.com/hc/en-us/articles/15712580191761-Rokoko-Studio-Subscriptions-plans-explained)
- **Unity Animation Generator**: makes animation clips from text or video input for a character, inside the Editor (costs Unity points) — [Generators manual](https://docs.unity3d.com/Packages/com.unity.ai.generators@1.6/manual/index.html)

### Inferences
- Snow's needs (walk/crouch/climb/carry, monster idles, ragdoll) fit this chain: humanoid mesh → AccuRIG or Mixamo rig (both give Unity-Humanoid-compatible skeletons) → Mixamo/ActorCore clips + HY-Motion for odd motions (SMPL-free BVH would still need retargeting) → Unity Humanoid Avatar retargeting. Non-humanoid monsters still need manual or UniRig rigs and Generic animation.
- An agent can drive HY-Motion and UniRig through Python CLIs. Mixamo/AccuRIG/Rokoko are GUI or web-only, so they are human steps (or Chrome automation, which is brittle).
- Text-to-motion models trained on HumanML3D/AMASS may inherit SMPL licensing questions. Check HY-Motion's license and output format before shipping.

### Gaps
- DeepMotion and Move AI free tiers/pricing for 2026 were not checked.
- MDM / MotionGPT licenses were not checked (code is typically MIT, but they depend on SMPL/HumanML3D, both non-commercial).
- AccuRIG/ActorCore EULA commercial terms were not found in primary text.
- HY-Motion's primary LICENSE file and output skeleton format (SMPL vs BVH) were not read.

## 4. Unity-side automation: import validation, Addressables, prefab-from-data, placement, Unity AI

### Takeaway
Unity 6 gives the agent strong editor-side hooks: `AssetPostprocessor` validators, import-time Mesh LOD (6.2+), and the Unity MCP already connected in this session, which exposes `Unity_ImportExternalModel`, `Unity_AssetGeneration_*`, multi-angle scene capture and the profiler. **Unity AI Generators** (Sprite, Texture2D, Material, Terrain Layer, Cubemap, Animation, Sound) are integrated but **paid by Unity points/credits**. Pricing is not clearly documented publicly. The project already follows a "data → scene" pattern (ForestYardDress.json), which is the right shape for agent-driven placement.

### Cited Findings
- Unity AI = Assistant + Generators. Both are enabled by default per org in the Unity Dashboard. "Improve Unity AI models" (training opt-in) is **off by default** — [Unity AI overview](https://docs.unity.com/en-us/ai/ai-overview)
- Generators list (package `com.unity.ai.generators`): Sprite, Texture2D, Sound, Animation (from text/video), Material (PBR), Terrain Layer, Cubemap. Newer packages need Unity 6.3. Accessed via the AI menu → "Generate New" — [Generators 1.0](https://docs.unity3d.com/Packages/com.unity.ai.generators@1.0/manual/index.html); [Generators 1.6](https://docs.unity3d.com/Packages/com.unity.ai.generators@1.6/manual/index.html); [AI menu 6000.3](https://docs.unity.com/en-us/engine/6000.3/manual/unity-ai/ai-menu-access.md)
- Cost: the 2025 roadmap said Unity Points are spent per Assistant/Generator action, with the cost shown before spending, and Inference Engine is free — [Unity pricing roadmap](https://activation.unity3d.com/roadmap/official-pricing). The Dashboard now talks of "Unity Credits" with usage/balance pages — [Monitor AI usage](https://docs.unity.com/en-us/ai/credits). Secondary 2026 claims (unverified, conflicting): Personal gets a 14-day 1,000-credit trial then $10/mo per 1,000 credits, and external models through the AI Gateway don't use credits — [issoh.co.jp](https://www.issoh.co.jp/tech/details/8536/). Unity AI open beta from 4 May 2026 — [buildfastwithai](https://www.buildfastwithai.com/blogs/unity-ai-open-beta-guide-2026)
- Unity 6.2 beta handed out free points that expired before paid points began — [Unity discussions](https://discussions.unity.com/t/unity-6-2-beta-is-now-available/1639999)
- Mesh LOD at import (see §1) — [Unity manual](https://docs.unity3d.com/6000.2/Documentation/Manual/lod/mesh-lod-generate-lods.html)
- No third-party 3D-mesh partner in Unity Generators was found in docs (roundups only) — [Krea roundup](https://www.krea.ai/blog/best-ai-3d-model-generators-2026)

### Inferences
- Agent-friendly pattern: an `AssetPostprocessor` (OnPreprocessModel/OnPostprocessModel/OnPreprocessTexture) under `Assets/Snow/Editor/Import/` that enforces PSX budgets. It would set max texture size, point filtering, no mips or chosen mips, scale factor, and Humanoid rig for `*_char` files, and log over-budget meshes. Rules live in a JSON/ScriptableObject so the human sets style limits and the agent only reads them.
- Placement: keep extending the data-driven approach (ForestYardDress.json, ForestPlacedHeights) instead of buying Gaia/MicroVerse. Those are paid Asset Store tools, and pricing was not checked.
- Addressables adds value only if build size or streaming becomes a problem. For a co-op horror with 2 levels, plain scenes + Resources/direct references are simpler.
- Unity Generators are a fine "point-of-need" texture/material/sound tool for the human, but at near-$0 the credit spend competes with free Codex image gen.

### Gaps
- Exact 2026 Unity credit prices and the free allowance per plan were not found on an official page (the credits-about page was not fetched).
- Gaia/MicroVerse current prices and Unity 6 URP compatibility were not checked.
- Commercial-use terms for Unity Generator output were not read (Unity AI Guiding Principles page exists: [unity.com/legal/unityai-models-partners](https://unity.com/legal/unityai-models-partners)).

## 5. Asset QA for agents: turntables, budget reports, VLM style checks

### Takeaway
Everything needed is already in reach without new tools. Blender MCP `look` / headless `blender -b -P turntable.py` renders turntables, and the Unity MCP has `Unity_SceneView_CaptureMultiAngleSceneView`, `Unity_Camera_Capture` and profiler tools. Budget reports can come from an editor script that walks prefabs. Claude itself can be the VLM judge if it compares the renders against a fixed style reference sheet. No off-the-shelf open-source "AI style QA" product for games was found.

### Cited Findings
- Unity MCP in this session exposes `Unity_SceneView_CaptureMultiAngleSceneView`, `Unity_Camera_Capture`, `Unity_Profiler_*`, `Unity_ImportExternalModel` and `Unity_AssetGeneration_*` (tool list of this environment, not a web source)
- Blender MCP `look` renders the current Blender view for the agent. `generate_3d` produces "one new textured model … 1–3 minutes, may cost the user a credit" (MCP server instructions in this session)
- TRELLIS.2 exports an MP4 preview alongside each GLB, a ready-made turntable for review — [GitHub](https://github.com/microsoft/TRELLIS.2)

### Inferences
- Minimal QA loop per asset: (1) Blender: decimate → report tris/materials/texture px; (2) render 4-angle turntable at in-game resolution with PSX post (low res, dither); (3) agent compares against `docs/art/style_ref.png` and a written checklist (palette, silhouette readability at 20 m, texel density); (4) Unity import → postprocessor logs budget → multi-angle capture in the Forest lighting (the dark-night look matters most); (5) human approves/rejects from a contact sheet. This keeps the human as the style gate.
- VLM style checks are unreliable on subtle style drift. Use them as a pre-filter (obvious mismatches such as photoreal textures or wrong scale), not as the final judge.

### Gaps
- No sourced research on VLM-based style-consistency scoring for game assets was gathered this session.
- No existing open-source Unity "asset budget report" package was verified (custom script assumed).

## 6. ComfyUI as a hub on Windows (local, API, MCP)

### Takeaway
ComfyUI is practical for a solo Windows dev as a **local HTTP-API backend**. It already powers StableGen and the Hunyuan3D wrapper, and several MCP servers (including an early first-party one) let Claude Code run saved workflows. The costs are dependency upkeep (compiled rasterizers, CUDA/PyTorch pins) and VRAM. It's worth adopting only if the user's GPU has ≥12–16 GB and there's a recurring need (texturing or Hunyuan shape gen). Otherwise Codex images + Blender MCP cover most needs.

### Cited Findings
- **Comfy first-party `comfy-local-mcp`**: an early proof-of-concept whose core loop is `server_info`, `run_workflow`, `fetch_outputs` — [docs.comfy.org agent tools](https://docs.comfy.org/agent-tools/local.md)
- **artokun/comfyui-mcp** (npm, Node 22+): macOS/Linux/Windows, auto-detects the install/port. Can search/install/repair node packs and find missing models — [README](https://cdn.jsdelivr.net/npm/comfyui-mcp@0.9.4/README.md); [install guide for agents](https://cdn.jsdelivr.net/npm/comfyui-mcp@0.9.4/llms-install.md)
- **zturner1/comfyui-mcp** (Python 3.10+): lists node types including custom ones, gets schemas, validates workflow JSON. Windows setup via `COMFYUI_ROOT`. Registers with `claude mcp add`. Execution is off unless `COMFYUI_ENABLE_EXECUTE=1` — [Glama README](https://glama.ai/mcp/servers/@zturner1/comfyui-mcp/blob/a7ca2334f6e6ddaea2357d959032a5b2694e1ee6/README.md)
- **Nikolaibibo/claude-comfyui-mcp** targets Windows 11 + Claude Desktop, image-gen focused, 15 tools — [Glama](https://glama.ai/mcp/servers/@Nikolaibibo/claude-comfyui-mcp/blob/a9753e3b6d51bb274ba21354174c251d19edf5db/ComfyUI-MCP-Specification.md)
- Hunyuan3D wrapper works on Windows with Windows wheels for the rasterizer — [ComfyUI-wiki](https://comfyui-wiki.com/en/tutorial/advanced/3d/huanyuan3d-2)
- StableGen uses ComfyUI as its backend and supports Windows — [SourcePulse](https://www.sourcepulse.org/projects/17250649)

### Inferences
- Recommended minimal setup if adopted: ComfyUI portable (Windows) + ComfyUI Manager + kijai Hunyuan3DWrapper + an SDXL checkpoint + StableGen. Expose it to Claude Code via `zturner1/comfyui-mcp` (validation + execute gate) or call ComfyUI's HTTP `/prompt` endpoint from a small Python script in `tools/`. Saved workflow JSONs in the repo are the reproducible "style recipe" the human signs off on.
- Model licenses still apply per node. ComfyUI being GPL doesn't affect output, but each checkpoint (SDXL OpenRAIL++, FLUX.1-dev **non-commercial**, Hunyuan region-locked) carries its own terms. For StableGen texturing intended for sale, use SDXL-family or FLUX.1-schnell (Apache), not FLUX.1-dev (inference; license terms not re-verified this session).

### Gaps
- FLUX.1-dev / SDXL license specifics were not re-verified this session.
- No sourced r/StableDiffusion / r/gamedev 2025–26 threads on Windows ComfyUI maintenance pain were collected.

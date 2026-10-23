# Observability, logging, telemetry, profiling and Unity MCP servers for an AI agent (Snow, Unity 6, Oct 2026)

Scope note: about 20 search/fetch calls. Primary sources (GitHub READMEs, Unity/Sentry docs) are used where they could be fetched. Aggregator and vendor-blog figures are labelled. Items marked "(doc URL not fetched this session)" are standard Unity APIs cited from known documentation pages that were not re-read here; the report writer should treat them as lower-confidence citations.

## 1. Unity MCP servers in 2026: which to pick or combine

### Takeaway
Keep the official Unity MCP that is already installed (editor bridge, profiler/console/capture tools, needs Unity 6). Add IvanMurzak/Unity-MCP only if the agent must reach into a running **player build**, because it is the only server documented to run inside compiled games. CoplayDev/unity-mcp is the most popular editor-only server, but its features largely overlap the official one. Running two editor bridges at once adds port and tool-name clutter for little gain.

### Cited Findings
**Official Unity MCP (com.unity.ai.assistant)**
- Architecture: the AI client starts a relay binary (`~/.unity/relay/`, flag `--mcp`) over stdio. The relay talks to the Editor's MCP Bridge over local IPC (named pipes on Windows). The bridge starts with the Editor and exposes tools from `McpToolRegistry`. — [Unity docs: Unity MCP overview 2.0](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.0/manual/unity-mcp-overview.html)
- Custom tools can be registered "using attributes, interfaces, or runtime APIs" and are auto-discovered at editor startup. Settings live under Edit > Project Settings > AI > Unity MCP (bridge status, client approvals, per-tool enable toggles). Several clients can connect to one Editor. — [same page](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.0/manual/unity-mcp-overview.html)
- Direct external clients (such as Claude Code) need one-time approval in Project Settings. AI Gateway connections are auto-approved. — [same page](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.0/manual/unity-mcp-overview.html)
- Requires Unity 6 (6000.0)+. — [Unity docs: get started 2.0](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.0/manual/unity-mcp-get-started.html)
- Separate feature: the Assistant can itself act as an MCP *client* to outside servers over stdio or Streamable HTTP. — [Unity docs 2.11: MCP overview](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.11/manual/integration/mcp-overview.html)
- Licensing: Unity says the MCP Server and third-party agents via the AI Gateway do **not consume Unity AI credits**. Pro/Enterprise/Industry include MCP access (Pro: 3 concurrent MCP connections). For **Personal**, Unity's pages conflict: one lists 1 concurrent connection as included, another marks it "Monthly subscription required" ($10/mo for 1,000 credits). A forum user reads it as gated. Still in beta. — [Unity AI page](https://unity.com/ai); [Unity Discussions: MCP versus built-in Assistant](https://discussions.unity.com/t/mcp-versus-built-in-assistant/1719106)
- One aggregator (Aug 2026) called the official package "pre-release (v2.5.0-pre.2)" and said community servers remain the production choice. Unity docs now go up to version 2.11, so that version number is stale. — [ChatForest review](https://chatforest.com/reviews/game-engine-3d-development-mcp-servers/); [Unity docs 2.11](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.11/manual/integration/mcp-configure.html)
- Tool set exposed in this project's own Claude Code session (local tool manifest, not a web source): `Unity_RunCommand`, `Unity_ReadConsole`/`GetConsoleLogs`, `Camera_Capture`, `SceneView_Capture2DScene`/`CaptureMultiAngleSceneView`, about 15 `Unity_Profiler_*` tools (frame top-time samples, bottom-up sample trees, GC allocations per frame/range/sample, counter summaries/tables), `ManageScene`/`ManageGameObject`/`ManageAsset`/`ManageEditor`/`ManageMenuItem`, `PackageManager_*`, script edit/validate, and asset generation.

**IvanMurzak/Unity-MCP ("AI Game Developer")**
- Apache-2.0, 4.5k stars, 402 forks, 3,203 commits. Transports: stdio or `streamableHttp` (remote/Docker; Docker image `ivanmurzakdev/unity-mcp-server`). 70+ built-in tools: assets, prefabs, materials, shaders, packages, GameObjects/components/scenes, screenshots, console logs, play-mode control, C# reflection method find/call, Roslyn script execution, tests, profiler control, lighting/baking. Optional extension packs: Animation, Cinemachine, InputSystem, Navigation, ParticleSystem, ProBuilder, Splines, Terrain, Tilemap, Timeline. — [GitHub README](https://github.com/IvanMurzak/Unity-MCP)
- **Runtime:** the plugin "works inside your compiled game", but **no tools are registered by default in runtime**, so you must write and register your own. — [GitHub README](https://github.com/IvanMurzak/Unity-MCP)
- Quirks: project path cannot contain spaces. Default plugin/server port is 8080 and the two must match. Tool calls time out after 300 s and are never re-sent. The server shuts down after 600 s idle. — [GitHub README](https://github.com/IvanMurzak/Unity-MCP)
- An aggregator calls it the "deepest integration" and the only server with runtime agents embedded in builds. — [ChatForest](https://chatforest.com/reviews/game-engine-3d-development-mcp-servers/)

**CoplayDev/unity-mcp ("MCP for Unity")**
- MIT, 15.3k stars, 1.6k forks. Latest **v10.3.0 dated 2026-10-04**. Supports Unity 2021.3 LTS to 6.x. Needs Python 3.10+ via `uv`. About 50 MCP tool entrypoints, organized into tool groups (vfx, animation, ui, testing...). Also offers multi-instance routing, Roslyn script validation, test running, profiling, building, and a remote-hosted server option with auth. Editor bridge, with no runtime/player mode documented. — [GitHub README](https://github.com/CoplayDev/unity-mcp)
- Stability signal: default branch is `beta`. The README suggests pinning a tag (e.g. `#v10.0.0`). "Not affiliated with Unity Technologies"; sponsored by Aura, which also sells a premium product. — [GitHub README](https://github.com/CoplayDev/unity-mcp)
- Directory listings for justinpbarnett/unity-mcp show the same description (MIT, 2021.3 to 6.x, Python/uv) and point to CoplayDev/unity-mcp, which suggests the project moved there. An archive banner was not confirmed. — [mcp.so listing](https://mcp.so/zh/servers/unity-mcp)
- Coplay itself frames the open-source server as "lightweight... basic Unity tools", separate from its paid Coplay assistant and Coplay MCP. — [Coplay blog](https://coplay.dev/blog/coplay-vs-coplay-mcp-vs-unity-mcp)

**Others (Aug 2026 snapshot, aggregator data)**
- CoderGamester/mcp-unity: MIT, about 1.9k stars, 30+ tools, runs a WebSocket server inside the Editor, aimed at IDEs. — [ChatForest](https://chatforest.com/reviews/game-engine-3d-development-mcp-servers/); [typingmind listing](https://www.typingmind.com/mcp/codergamester-mcp-unity)
- AnkleBreaker-Studio/unity-mcp-server (268 tools, includes **MPPM** and Shader Graph, new Feb 2026). Glade-tool/glade-mcp-unity (222+ tools, GDD context). youichi-uda/unity-mcp-pro-plugin (147 tools). Codeturion/unity-api-mcp (API lookup to reduce hallucinations). nurture-tech/unity-mcp-server (vision: scene/camera/thumbnails). All are young and small (under 400 stars), so treat them as **experimental**. — [ChatForest](https://chatforest.com/reviews/game-engine-3d-development-mcp-servers/)

### Inferences
- For Snow, the gap in the official MCP is **play-mode and build visibility**. It sees the Editor, including play mode in the Editor, but not a standalone player or MPPM clone processes. To cover that, either use IvanMurzak's runtime mode with custom tools, or (simpler and more robust) have the game write files and serve a small local HTTP endpoint that the agent reads with curl or a thin custom MCP (see section 4).
- Codeturion/unity-api-mcp may help with API hallucinations, but this is low priority; context7 is already available.
- AnkleBreaker's MPPM support is interesting for 4-player Netcode tests but unproven. Mark it experimental.

### Gaps
- No primary-source list of the official MCP's built-in tools or the exact custom-tool attribute name (the docs page names only categories).
- No head-to-head stability benchmark exists. Star counts differ between sources and dates (IvanMurzak 2.3k to 4.5k, CoplayDev 8.9k to 15.3k).
- Whether the official MCP works in a development Player build: not documented. It appears to be editor-only.

## 2. Structured logging and log files an agent can tail or grep

### Takeaway
Simplest high-value move: keep `Debug.Log`, but hook `Application.logMessageReceivedThreaded` and write a per-session **JSON Lines** file (timestamp, frame, level, system tag, clientId, message, stack). ZLogger gives zero-allocation structured logs on top of Microsoft.Extensions.Logging if volume grows. com.unity.logging is a niche, server-oriented option.

### Cited Findings
- ZLogger (Cysharp, MIT): zero-allocation logger built on Microsoft.Extensions.Logging. Providers: Console, File, RollingFile, InMemory, Stream, and custom LogProcessor. Plain-text or JSON output via `Utf8JsonWriter`. Unity integration needs Unity 2022.2+ (2022.3.12f1+ recommended for source generators) and csc.rsp settings for C# 10/11. — [ZLogger author's article](https://neuecc.medium.com/zlogger-zero-allocation-logger-for-net-core-and-unity-d51e675fca76); [sourcepulse summary](https://www.sourcepulse.org/projects/1983611)
- Unity's com.unity.logging package can write text or **JSON** files, with rolling files by default. Unity positions it for "high-performance servers and other niche applications" and still recommends `Debug.Log` for ordinary projects. — [Unity docs com.unity.logging 1.3](https://docs.unity3d.com/Packages/com.unity.logging@1.3/manual/index.html); [1.0 docs](https://docs.unity.cn/Packages/com.unity.logging@1.0/manual/index.html)
- Editor.log on Windows is at `%LOCALAPPDATA%\Unity\Editor\Editor.log`. Player.log is at `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\Player.log`. The `-logFile <path>` command-line argument redirects it, which is useful to give each MPPM/standalone client its own file. — [Unity Manual: Log files](https://docs.unity3d.com/6000.0/Documentation/Manual/log-files.html) (doc URL not fetched this session)
- `Application.logMessageReceivedThreaded` receives every log message (any thread) with condition, stackTrace, and LogType, so a custom JSONL writer can be added without touching call sites. — [Unity ScriptReference](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-logMessageReceivedThreaded.html) (doc URL not fetched this session)

### Inferences
- Agent consumption: JSONL files in a known folder (e.g. `Logs/session-<date>-<role>.jsonl`) can be read with grep, jq, or a Python summarizer added to tools/snow_qa. No MCP is needed. The same lines can be shipped to Loki (section 5).
- A per-system tag (`[Net]`, `[Voice]`, `[Forest]`...) plus a level filter set from the ImGui pult gives the human control and gives the agent a filter.
- Serilog works in Unity in principle, but no 2025–26 Unity-specific source was found. ZLogger is the better-documented choice for Unity.

### Gaps
- No Unity 6-specific ZLogger setup recipe (JSON file sink) was verified. Check the README before adopting.
- Serilog-on-Unity status was not researched.

## 3. Crash and exception reporting

### Takeaway
Sentry is the best fit: an official Unity SDK with native crash capture on Windows, IL2CPP line numbers, a free single-developer plan, and an official **remote MCP server** (`mcp.sentry.dev`) that Claude Code can query directly. Unity Cloud Diagnostics *Advanced* is dead (moved to Backtrace). BugSplat and Backtrace are alternatives without a known MCP server.

### Cited Findings
- Sentry Unity SDK captures C# exceptions and **native crashes** on Windows/macOS/Linux/Android/iOS, enabled by default. Console support is a work in progress. No native crash detection on WebGL. — [Sentry docs: Native support](https://docs.sentry.io/platforms/unity/native-support/); [Sentry known limitations](https://docs.sentry.io/platforms/unity/troubleshooting/known-limitations/)
- IL2CPP line numbers are supported on Windows (Unity 2021.3+). The C#-to-C++ mapping is uploaded at build time, not shipped with the game. — [Sentry docs: IL2CPP](https://docs.sentry.io/platforms/unity/configuration/il2cpp/)
- Native SDKs can be initialized at build time, before the engine starts. Options are then baked in and cannot change at runtime. — [Sentry docs: Native support](https://docs.sentry.io/platforms/unity/native-support/)
- **Sentry MCP**: hosted at `https://mcp.sentry.dev/mcp`, OAuth login, optional org/project scoping. It lets assistants query errors, investigate performance, triage issues, and search docs. Claude Code setup: `claude mcp add --transport http sentry https://mcp.sentry.dev/mcp/{org}/{project}`. Append `?experimental=1` for experimental features. — [Sentry MCP](https://mcp.sentry.dev/)
- Sentry free tier: most third-party sources say the "Developer" plan is $0 for 1 user and 5k errors/month, and Team is $26/mo. One source claims 50k errors after an Aug 2025 change. **Conflicting, unverified against sentry.io.** — [last9 Sentry pricing](https://last9.io/blog/sentry-pricing/); [agentdeals](https://agentdeals.dev/vendor/sentry)
- Unity **Cloud Diagnostics Advanced** is deprecated; users were directed to Backtrace ("core technology is identical"), with sunset dates given as Oct 31 2024 / Q3 2025. Basic Cloud Diagnostics is reported (by a competing vendor's blog) to be replaced by a built-in Diagnostics service in Unity 6.2+. — [Unity Discussions: CDA deprecation](https://discussions.unity.com/t/deprecation-announcement-cloud-diagnostics-advanced-cda/1488742/1); [Bugnet blog (vendor)](https://bugnet.io/blog/unity-cloud-diagnostics-alternative)
- Backtrace: a free tier of 1 developer and 25k errors/mo is reported by a competitor blog only. BugSplat: "start for free", with a page on free crash reporting for indie games. Paid plans reportedly from $37/mo. — [Bugnet comparison (vendor)](https://bugnet.io/blog/best-crash-reporting-tools-for-unity); [BugSplat for Unity](https://bugsplat.com/for/unity); [BugSplat billing docs](https://docs.bugsplat.com/administration/billing)
- In this environment, a Backstage MCP exposes `get-sentry-issues`/`list-sentry-projects` tools (local tool manifest, a different project's setup). This shows Sentry data can already flow into agent tools here.

### Inferences
- For a pre-release co-op game with a few testers, the Sentry Developer plan plus the Sentry MCP is enough: the agent can list new issues after each playtest with no file plumbing. Self-hosted Sentry is possible but heavy. Whether the hosted MCP supports self-hosted Sentry is not documented.
- Even without Sentry, local JSONL plus Player.log covers most dev needs. Sentry mainly adds value once outside testers run builds.

### Gaps
- Official Sentry pricing numbers not verified.
- Whether `mcp.sentry.dev` works against self-hosted Sentry, and its exact tool list, are not documented on the fetched page.
- No MCP server found for Backtrace or BugSplat.

## 4. In-game consoles, remote inspection, remote control of a running build

### Takeaway
Snow already has a Dear ImGui pult, which covers the human side. Extra value comes from (a) a **command registry** shared by the pult and a remote endpoint, and (b) a small dev-only HTTP/WebSocket server in the player that the agent can call (`/state`, `/cmd`, `/screenshot`, `/log?since=`). That is self-built. Off-the-shelf consoles are mostly human-facing.

### Cited Findings
- yasirkula **IngameDebugConsole** (free, open source, Asset Store and OpenUPM): uGUI console that shows logs and runs commands in builds. Can be dragged, resized, and hidden. Mirrors Android logcat. The most actively distributed free option. — [GitHub mirror/README](https://github.com/HiWill/UnityIngameDebugConsole); [builder.io summary](https://best-of-web.builder.io/library/yasirkula/UnityIngameDebugConsole)
- **Quantum Console** (paid asset): `[Command]` attribute registration, autocompletion, and an `exec` command that compiles and runs C# at runtime. The only source found is the 2018 launch thread, so Unity 6 status is unverified. — [Unity Discussions thread](https://discussions.unity.com/t/quantum-console-the-ultimate-in-game-development-console/716150)
- **SRDebugger** (paid): on-device console, options panel, profiler, and built-in **bug reporter**. A 2025 forum user says it has not been updated in 2+ years, worries about compatibility, and suggests **NOA Debugger** as an alternative. — [Unity Discussions: alternatives to SRDebugger](https://discussions.unity.com/t/any-alternatives-to-srdebugger/1643212); [SRDebugger thread](https://discussions.unity.com/t/srdebugger-on-device-console-options-panel-and-bug-reporter/566514)
- IvanMurzak Unity-MCP can live inside a compiled game, but runtime tools must be custom-registered. — [GitHub README](https://github.com/IvanMurzak/Unity-MCP)

### Inferences
- Recommended self-build: `DevCommands.Register("name", args => ...)`, used by both the ImGui pult and an `HttpListener` on `127.0.0.1:<port>` (dev builds only, behind `DEVELOPMENT_BUILD`). Each MPPM/standalone client gets a port offset. The agent calls it with curl, or through a 50-line Python MCP wrapper. This mirrors the existing "bench port 7797" pattern in the project.
- Dear ImGui stays the human UI. Adding IngameDebugConsole or Quantum Console would duplicate it.

### Gaps
- No source found for RuntimeInspector (yasirkula) Unity 6 status, Unity Remote Config current state, or NOA Debugger details.

## 5. Telemetry and metrics: OpenTelemetry, Grafana/Loki/Prometheus, analytics, heatmaps

### Takeaway
The user already has a Grafana MCP with Loki, Prometheus, Tempo, and Pyroscope query tools. A local dev stack (Grafana + Loki + Prometheus via docker-compose, or a Grafana Alloy/OTel Collector) would let the agent run LogQL/PromQL over playtest sessions. For a small team this is optional: JSONL files plus a Python summarizer give 80% of the value at no infrastructure cost.

### Cited Findings
- Loki natively ingests **OTLP**. The recommended path is app to OpenTelemetry Collector to `otlphttp` exporter to `http://loki:3100/otlp`, with a batch processor. Traces and metrics need their own backends (Tempo, Prometheus). — [Grafana Loki docs: OTel Collector getting started](https://grafana.com/docs/loki/latest/send-data/otel/otel-collector-getting-started/)
- The Grafana MCP available in this environment exposes `query_loki_logs`, `query_loki_patterns`, `query_prometheus`, `query_prometheus_histogram`, `search_tempo_traces`, `query_pyroscope`, `create_annotation`, and `get_panel_image` (local tool manifest). So once data lands in Loki/Prometheus, the agent can query it with no new tooling.

### Inferences
- Cheapest path to Loki: write JSONL (section 2) and have Grafana Alloy or Promtail tail the log folder. No SDK goes into the game, which avoids OpenTelemetry .NET-in-IL2CPP risk.
- Metrics: a dev-only `/metrics` endpoint in Prometheus text format (fps, frame ms, GC alloc/frame, net RTT, entity counts) from `ProfilerRecorder` values. Prometheus scrapes it.
- Heatmaps: log player position, death, and event points as JSONL, then render a top-down PNG with Python or matplotlib over the Forest terrain map. Self-built. No current off-the-shelf Unity heatmap tool was researched.

### Gaps
- Not verified: whether the OpenTelemetry .NET SDK works under Unity IL2CPP, and any Unity-specific OTel package. Searches returned only Loki docs.
- Unity Analytics (UGS) current state and pricing were not researched.

## 6. Profiling automation: what an agent can capture and summarize

### Takeaway
The official MCP already gives the agent profiler sample/GC queries in the Editor. For players and repeatable checks, add: `ProfilerRecorder` counters logged to JSONL each second, `Profiler.logFile` binary captures for deep dives, scripted `MemoryProfiler.TakeSnapshot`, and Project Auditor static scans (built into Unity 6.1+ as a package).

### Cited Findings
- `MemoryProfiler.TakeSnapshot` works at runtime in players. Snapshots are saved to device storage or streamed to the attached Editor. `.snap` files open in the Memory Profiler. Calls must be sequential, waiting for finishCallback. `TakeTempSnapshot` writes to `Application.temporaryCachePath`. Analyze memory in development players, not only the Editor. — [Unity 6.0 ScriptReference: MemoryProfiler.TakeSnapshot](https://docs.unity3d.com/ja/6000.0/ScriptReference/Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot.html); [docs.unity.com mirror](https://docs.unity.com/en-us/engine/6000.0/script-reference/unity/profiling/memory/memoryprofiler/takesnapshot)
- **Project Auditor**: static analysis of scripts, assets, and settings, plus the last build report. Its Code view flags managed allocations on hot paths. Full release with Unity 6.1 as package `com.unity.project-auditor` 1.0.1. From Unity 6.4 it is built in under Window > Analysis. The old GitHub repo is retired. — [Unity 6.1 manual](https://docs.unity.cn/Manual/com.unity.project-auditor.html); [Unity blog: 6.1 performance tools](https://unity.com/blog/mobile-recap-new-tools-for-performance-in-unity-6-1); [Unity 6.6 manual](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/project-auditor/introduction.md); [retired repo](https://github.com/Unity-Technologies/ProjectAuditor)
- `ProfilerRecorder` reads built-in counters (e.g. "Main Thread", "GC Allocated In Frame", "Draw Calls Count", "System Used Memory") in development and release players. `Profiler.logFile` plus `enableBinaryLog` write `.raw` captures that load in the Profiler window. — [Unity ScriptReference: ProfilerRecorder](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html); [Profiler.logFile](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.Profiler-logFile.html) (doc URLs not fetched this session)

### Inferences
- Agent loop: run a scripted scenario (fly over Forest, 4 MPPM clients), log ProfilerRecorder counters to JSONL, have a Python script output p50/p95/p99 frame ms, GC/frame, and spikes with timestamps, then load the matching `.raw` in the Editor and query it via the official MCP `Unity_Profiler_*` tools.
- Project Auditor can run in batch mode as a "lint" step in snow_qa. Its programmatic API was not verified here.
- RenderDoc, PIX, and Superluminal are human GUI tools. RenderDoc has a Python API (`renderdoccmd`, `qrenderdoc --python`), but no MCP was found. They are low priority for the agent.

### Gaps
- No sources fetched for RenderDoc/PIX/Superluminal automation, Frame Debugger scripting, or a Project Auditor CLI/batch API.

## 7. Video and screenshot capture, "bug report button" bundles

### Takeaway
For the human: OBS with its built-in WebSocket v5 and a **replay buffer**, so an "F8 = save last 60 s" key from the game or a script saves the clip. For the agent: an in-game bug-report action that bundles screenshot, last N log lines, game state JSON, and an optional replay clip into one folder the agent can read.

### Cited Findings
- OBS Studio 28+ bundles obs-websocket v5 (default port 4455). The protocol includes `StartReplayBuffer`/`StopReplayBuffer`/`SaveReplayBuffer`/`GetReplayBufferStatus`. Python SDK: `obsws-python` (does not implement every endpoint). CLI wrapper `obs_cli` exposes `save-replay-buffer` and similar. — [obs-websocket 5.0 release notes](https://newreleases.io/project/github/obsproject/obs-websocket/release/5.0.0-beta1); [obsws-python on PyPI](https://pypi.org/project/obsws-python); [obs_cli](https://pub.dev/packages/obs_cli/example)
- An OBS MCP server exists (royshil/obs-mcp). — [glama listing](https://glama.ai/mcp/servers/@royshil/obs-mcp/blob/314e7c065ef8c5e2656757b35b24ea7fa81489b0/py_src/streaming.py)
- SRDebugger includes a built-in bug reporter, but it may be unmaintained (see section 4). — [Unity Discussions](https://discussions.unity.com/t/any-alternatives-to-srdebugger/1643212)
- The official Unity MCP already captures Game/Scene view images (`Unity_Camera_Capture`, `SceneView_Capture*`), from the local tool manifest. This is Editor-only.

### Inferences
- Self-built bug report (about 100 lines): a hotkey or pult button runs `ScreenCapture.CaptureScreenshot`, copies the session JSONL tail, dumps game state (scene, player positions, NetworkManager status, pockets, quest step), and optionally calls OBS `SaveReplayBuffer` via a localhost WebSocket. Output goes to `BugReports/<timestamp>/`. The agent lists and reads that folder.
- ffmpeg can turn replay clips into contact sheets (one frame per second) that the agent can view as images. That is cheaper than video.

### Gaps
- Unity Recorder package Unity 6 status and its scripting API were not researched in this session.
- The obs-mcp maturity and tool list were not verified.

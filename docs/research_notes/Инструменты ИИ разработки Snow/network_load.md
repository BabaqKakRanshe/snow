# Multiplayer load and network testing tools for Snow (Unity 6, NGO 2.13, UTP, MPPM)

Scope: tools and techniques to check whether a 2–8 player listen-server co-op game holds up with many players, bad connections and long sessions, and how an AI agent (Claude Code + Unity MCP + Python) can run them headless and read the results. Researched 23.10.2026. About 20 web searches/fetches; Unity package pages often returned only overview text, so some API details are marked as gaps.

## 1. Running many clients / bots (headless builds, MPPM, ParrelSync, UGS)

### Takeaway
On one Windows PC the practical path is: MPPM for up to 4 interactive players in the editor (plus up to 4 local built instances in MPPM 2.0, which needs Unity 6000.3+), and for more than that, a development build launched N times with `-batchmode -nographics` from a Python script, with a small "bot" component that joins and plays scripted input. Unity's hosted load path (Multiplay) is gone as of 31.03.2026, so there is no first-party "virtual players in the cloud" option to rely on.

### Cited Findings
- MPPM supports up to four players total: main editor player plus three virtual players, on the same machine using the same assets on disk — [Unity MPPM 1.6 docs](https://docs.unity3d.com/Packages/com.unity.multiplayer.playmode@1.6/manual/index.html); [Unity networking utilities overview](https://docs.unity.com/en-us/multiplayer/netcode/networking-utilities)
- MPPM 1.6 targets Unity 6000.0.50f1 or later — [MPPM 1.6 docs](https://docs.unity3d.com/Packages/com.unity.multiplayer.playmode@1.6/manual/index.html)
- MPPM 2.0 requires Unity 6000.3.0b4 or later; keeps the editor cap (editor + 3 more editor instances) and adds "up to four locally built instances"; Windows and macOS only — [MPPM 2.0 docs](https://docs.unity3d.com/Packages/com.unity.multiplayer.playmode@2.0/manual/index.html)
- I found no documented command-line or batch API for MPPM in the docs pages fetched — [MPPM 2.0 docs](https://docs.unity3d.com/Packages/com.unity.multiplayer.playmode@2.0/manual/index.html) (absence noted, see Gaps)
- Desktop headless mode = run a Player with `-batchmode -nographics`, graphics device not initialized; Unity notes it "doesn't perform any optimizations for running a build as a dedicated server" — [Unity 6.3 Manual: Desktop headless mode](https://docs.unity3d.com/Manual/desktop-headless-mode.html)
- Unity's Dedicated Server package lets one project switch between server and client roles for the Dedicated Server build target — [Unity networking utilities overview](https://docs.unity.com/en-us/multiplayer/netcode/networking-utilities)
- Pattern of launching several headless instances from a script with `-batchmode -nographics -logFile` plus custom args (map/room) is documented by Photon Bolt (other netcode, same Unity mechanism) — [Photon Bolt: Headless Server](https://doc.photonengine.com/en-US/bolt/current/in-depth/headless-server)
- An MCP project drives Unity purely in `-batchmode -nographics` and reads verdicts from NUnit results XML instead of the exit code (macOS-only, not load testing, but the "launch, wait, parse files on disk" pattern applies) — [glama.ai MCP listing](https://glama.ai/mcp/servers/lvkp1fh0m2)
- ParrelSync = separate full editor instances of the same project via symbolic links; drawback is multiple editors open — [Fish-Networking docs: Testing with Multiple Editors](https://fish-networking.gitbook.io/docs/tutorials/simple/testing-with-multiple-editors)
- Unity's own Bitesize samples replaced ParrelSync with MPPM when moving to Unity 6 (PR, Sept 2024) — [Unity bitesize samples PR #218](https://github.com/Unity-Technologies/com.unity.multiplayer.samples.bitesize/pull/218)
- NGO ships an integration-test base class `NetcodeIntegrationTest` (namespace `Unity.Netcode.TestHelpers.Runtime`) that spins up a server and N client NetworkManagers in one process; exposes `m_ServerNetworkManager`, `m_ClientNetworkManagers`, `OnServerAndClientsCreated`, a log-assert helper and an 8 s default global timeout (seen in a v1.2.0 source mirror; may differ in 2.13) — [NetcodeIntegrationTest.cs mirror (NGO 1.2.0)](https://plastichub.unity.cn/unity-tech-cn/MetacityTemplate/src/commit/720f3ddc-bab8-4e90-92bf-5cfac1c70369...Packages/com.unity.netcode.gameobjects@1.2.0/TestHelpers/Runtime/NetcodeIntegrationTest.cs)
- Multiplay Game Server Hosting: Unity ended direct support 31.03.2026; status page says deprecated from 01.04.2026, no new servers or allocations; software licensed to Rocket Science Group ("Multiplay by Rocket Science") — [Unity status notice: Multiplay Deprecation](https://status.unity.com/info_notices/362941); [Unity Multiplay Hosting docs](https://docs.unity.com/en-us/multiplay-hosting)
- Third party says Unity Relay, Lobby, Matchmaker and Distributed Authority are not affected by the Multiplay shutdown; lists Edgegap and Gameye as alternatives (vendor blog, biased) — [Gameye: game server shake-up 2026](https://gameye.com/blog/game-server-shake-up-2026)

### Inferences
- For Snow (listen server, 2–8 players), the "server" is the host player's game. The real load test is: one host (ideally a normal windowed dev build or the editor, so rendering cost is included) + 7 headless bot clients. Headless clients are cheap (no GPU), so 7–15 of them on one PC is likely feasible, but this was not measured in any source — test it.
- Bot client design (self-build, ~1 C# file): read command-line args (`-snowBot`, `-connect 127.0.0.1:7777`, `-botSeed N`, `-runMinutes 60`, `-metricsOut path.jsonl`), call `StartClient()`, then feed the same input path the player uses (move, look, pick up, use E, radio T) from a seeded random walk or a recorded input script. Write one JSON line per second to the metrics file. Exit with a code on disconnect/exception.
- AI-run loop: Python `subprocess.Popen` starts host + N bots, waits for `-runMinutes`, kills, then parses all `*.jsonl` + `Player.log` files (`-logFile path`) and writes a summary. This needs no MPPM and no editor, so it fits the "agent runs it itself" requirement.
- `NetcodeIntegrationTest` is the right tool for fast deterministic checks (spawn, ownership, RPC correctness with 4–8 in-process clients) run via Unity Test Framework in batchmode; it is not a performance test because all clients share one process and frame loop.
- Do not plan around Unity Multiplay / UGS "virtual players" for load testing; Multiplay is deprecated. Relay is still available if Snow later goes relay.
- MPPM 2.0 local built instances would let the editor orchestrate builds, but needs an editor upgrade to 6000.3+; Snow is on 6000.0.x, so stay on MPPM 1.x + scripted builds.

### Gaps
- No official Unity doc found on a "bot client" or headless load-test harness for NGO; no source gives numbers for how many headless NGO clients one PC can run.
- MPPM command-line/scripting API (e.g. starting virtual players from a script) not found in the fetched pages.
- ParrelSync's current maintenance status for Unity 6 not confirmed (no release/commit data found).
- Whether `NetcodeIntegrationTest` still has the same API in NGO 2.13 not verified.

## 2. Network condition simulation (bad connections)

### Takeaway
Two in-engine options (Multiplayer Tools Network Simulator with presets and scripted scenarios; Unity Transport's debug simulator via `SetDebugSimulatorParameters`) plus one free OS-level option (clumsy, MIT, works on localhost). In-engine is easiest for an AI to drive from code and command-line args; clumsy tests the real stack including Steam/other transports.

### Cited Findings
- Multiplayer Tools Network Simulator: add a `NetworkSimulator` component on a persistent GameObject; controls for Connection (set "Disconnected" to cut connection), lag spike duration + Trigger, Connection Preset; `NetworkSimulator.TriggerLagSpike` named in docs — [Multiplayer Tools 2.2: Network Simulator](https://docs.unity3d.com/Packages/com.unity.multiplayer.tools@2.2/manual/network-simulator.html)
- Built-in presets (delay ms / jitter ms / loss %): Home Fiber 10/1/0, Home Cable 25/5/0, Home DSL 30/10/0, Home Broadband 32/12/2, Home Satellite 100/10/0, Home Broadband with Congested Network 50/50/1, Mobile 2G 520/50/7, 2.5G 480/40/7, 2.75G 440/40/7, 3G 360/30/7, 3.5G 160/30/6, 3.75G 130/30/6, 4G 100/20/4, 4.5G 80/20/4, 5G 30/20/4 — [Network Simulator docs](https://docs.unity3d.com/Packages/com.unity.multiplayer.tools@2.2/manual/network-simulator.html)
- Custom presets via Create > Multiplayer > NetworkSimulatorPresetAsset (delay, jitter, loss % and interval); edits apply live — [Network Simulator docs](https://docs.unity3d.com/Packages/com.unity.multiplayer.tools@2.2/manual/network-simulator.html)
- Scenarios: built-in "Connections Cycle" and "Random Connections Swap" (change preset every Change Interval); "Auto Run Scenario" can be set in code; samples `LagSpikeScenarioWithAsyncTask.cs`, `DisconnectScenarioWithMonoBehaviour.cs`, `UnstableConnection.cs`, `ConnectionParametersWithCurves.cs` — [Network Simulator docs](https://docs.unity3d.com/Packages/com.unity.multiplayer.tools@2.2/manual/network-simulator.html); [Network Simulator 2.1.0 docs](https://mp-docs.dl.it.unity3d.com/tools/2.1.0/tools-network-simulator/)
- Requirements: Unity 2022.2+, Unity Transport 2.0.0+, NGO 1.1.0+, Multiplayer Tools 1.1.0+; works through the UTP simulator pipeline stage — [Network Simulator docs](https://docs.unity3d.com/Packages/com.unity.multiplayer.tools@2.2/manual/network-simulator.html)
- Multiplayer Tools 2.x requires Unity 6000.0+; package versions reported inconsistently: registry/6.6 alpha docs list 2.2.8, Unity 6.5 manual lists 2.2.9, the 2.2 profiler page header says 2.2.12 — [packages.unity.com](https://packages.unity.com/com.unity.multiplayer.tools); [Unity 6.5 Manual](https://docs.unity3d.com/Manual/com.unity.multiplayer.tools.html); [Multiplayer Tools 2.2 Network Profiler](https://docs.unity3d.com/Packages/com.unity.multiplayer.tools@2.2/manual/profiler.html)
- Unity Transport simulator parameters: `PacketDelayMs`, `PacketJitterMs`, `PacketDropInterval` (drop every Nth, for predictable tests), `PacketDropPercentage` (random), `MaxPacketCount`, `MaxPacketSize`, fuzz (bit flips), `RandomSeed` (from a source mirror, may be older) — [SimulatorUtility.cs mirror](https://plastichub.unity.cn/unity-tech-cn/com-unity-services-samples-game-lobby/raw/commit/f2cbe7d0-312d-4c83-b532-55623d0bd941...Packages/com.unity.transport/Runtime/Pipelines/SimulatorUtility.cs)
- In NGO's UnityTransport the parameters are under "Debug Simulator" on the Unity Transport component or set with `SetDebugSimulatorParameters` — [NGO UTP adapter changelog](https://docs.unity3d.com/Packages/com.unity.netcode.adapter.utp@1.0/changelog/CHANGELOG.html)
- clumsy: Windows tool on WinDivert that lags, drops and tampers live packets; no install, no proxy, no code changes; MIT; "works even if you're offline (localhost to localhost)" — [clumsy README](https://cdn.jsdelivr.net/gh/jagt/clumsy@master/README.md); [clumsy GitHub](https://www.github.com/jagt/clumsy)
- Forks/alternatives: clumsy2 adds a bandwidth limit; fumble (Rust) does drop/delay/throttle/reorder/tamper; Bean Network Tester (GPL-3.0, PyDivert) adds jitter, bandwidth caps, TCP resets and has a CLI mode for CI — [clumsy2](https://github.com/mimicode/clumsy2); [fumble on docs.rs](https://docs.rs/crate/fumble/0.6.1); [Bean Network Tester on AlternativeTo](https://alternativeto.net/software/bean-network-tester/about/)
- NetLimiter 5: per-application/per-connection bandwidth limits and stats; subscription (~30 USD / 2 years Pro per vendor announcement), 1 machine per standard license; no latency simulation mentioned — [NetLimiter v5 licensing news](https://netlimiter.com/news/version-5-subscription-license-upgrades-from-ver4-to-ver5); [NetLimiter purchase docs](https://netlimiter.com/docs/installation/purchase-and-registration)

### Inferences
- Simplest AI-drivable route: a `-netSim <preset>` or `-netSim delay,jitter,loss` command-line arg read at startup by Snow's network bootstrap, which calls `UnityTransport.SetDebugSimulatorParameters(...)` (or sets the `NetworkSimulator` preset). Each bot can then have its own bad connection, e.g. bot 1 = Mobile 4G, bot 2 = Home Satellite, bot 3 = 7% loss. No GUI, no admin rights.
- Use `PacketDropInterval` / `RandomSeed` when a test must be repeatable (regression), and percentage loss for soak runs.
- clumsy is the second layer: it hits all traffic (including Steam/Facepunch or Relay later) and needs no game code, but it is a GUI app needing admin rights (WinDivert driver) — scripting it is not documented in the README. For CLI use, Bean Network Tester or fumble are better candidates.
- NetLimiter is only worth paying for to test bandwidth caps per process; clumsy2/fumble cover throttling for free.
- tc/netem in WSL2: no sources gathered. Note that Windows-side loopback traffic does not pass through the WSL2 VM, so netem would only shape traffic of game processes running inside WSL/Linux — treat as low-value for Snow (see Gaps).
- Multiplayer Tools Network Simulator only affects UTP; if Snow switches to a Steam transport, only clumsy-type tools remain.

### Gaps
- Exact `SetDebugSimulatorParameters` signature in NGO 2.13 and whether it is compiled only in Editor/Development builds was not confirmed (adapter changelog says editor + development builds for the old adapter).
- Whether `NetworkSimulator` works in non-editor development builds is not stated in docs.
- clumsy's CLI arguments, admin rights, and last release date not confirmed from the README fetched.
- No source found for tc/netem usage with Unity on WSL2.

## 3. Network profiling and metrics, export for an AI

### Takeaway
Multiplayer Tools adds "NGO Messages" and "NGO Objects" modules to the Unity Profiler (bytes per frame per message type, RPCs, NetworkVariable updates, spawn/destroy, ownership, per NetworkObject), and the Runtime Network Stats Monitor (RNSM) shows stats on screen. Neither has a documented "export to JSON". For AI-readable output, Snow needs its own small metrics writer.

### Cited Findings
- Network Profiler adds two Profiler modules: NGO Messages (bytes sent/received per frame: total, named, unnamed, scene events) and NGO Objects (bytes per frame for RPCs, NetworkVariable updates, spawn/destroy, ownership changes), grouped per NetworkObject; filters `t:`, `dir:in/out`, `b>=N` — [Multiplayer Tools 2.2: Network Profiler](https://docs.unity3d.com/Packages/com.unity.multiplayer.tools@2.2/manual/profiler.html)
- Messages a host sends to itself appear grayed out; Unity recommends profiling a development build on target hardware — [Network Profiler docs](https://docs.unity3d.com/Packages/com.unity.multiplayer.tools@2.2/manual/profiler.html)
- Network profiler data is sampled per frame, so graphs look spiky; NGO does not update the legacy "Network Messages"/"Network Operations" modules — [Network Profiler 1.1.0 docs](https://mp-docs.dl.it.unity3d.com/tools/1.1.0/profiler)
- RNSM: configurable on-screen component showing NGO stats and custom user-defined stats; graphs and moving-average counters can sample per second (since 1.1.0) — [RNSM 2.1.0 docs](https://mp-docs.dl.it.unity3d.com/tools/2.1.0/RNSM/); [Multiplayer Tools 1.1 changelog](https://docs.unity3d.com/Packages/com.unity.multiplayer.tools@1.1/changelog/CHANGELOG.html)
- Multiplayer Tools 2.2.0 fixed RNSM showing no metrics in builds with code stripping, and RNSM not working with NGO 2.0.0+ — [Multiplayer Tools 2.2 changelog](https://docs.unity3d.com/Packages/com.unity.multiplayer.tools@2.2/changelog/CHANGELOG.html)
- On standalone players, profiling can be written to a file by setting `Profiler.logFile`, `Profiler.enabled = true`, `Profiler.enableBinaryLog = true`; most Profiler API needs a Development Build — [Unity ScriptReference: Profiler](https://docs.unity3d.com/kr/2023.1/ScriptReference/Profiling.Profiler.html)
- `ProfilerRecorder` reads built-in profiler counters (memory, timing) at runtime — [Unity ScriptReference: ProfilerRecorder](https://docs.unity3d.com/kr/6000.0/ScriptReference/Unity.Profiling.ProfilerRecorder.html)

### Inferences
- The `.raw` binary profiler logs are readable only by the Unity Profiler window; an AI can open them through Unity MCP (this session has `Unity_Profiler_*` tools for frame time, GC alloc, sample summaries), but they are large. Better: per-second JSONL written by game code.
- Self-built `NetMetricsLogger` (one C# file, dev builds only) writing one JSON line per second: time, players connected, RTT per client (`UnityTransport.GetCurrentRtt(clientId)`), bytes in/out (count in a custom `NetworkTransport` wrapper or sample UTP driver stats), RPC counts per type (counter incremented in a helper), NetworkVariable dirty count per object, spawned NetworkObject count, frame time, GC alloc, managed/total memory via `ProfilerRecorder`. Python then makes a summary (p50/p95/max, trend over time, top-10 chattiest objects).
- Use the Network Profiler by hand (or via MCP) only to find *which* object is chatty after the JSONL shows a problem.
- Whether NGO network counters are exposed as `ProfilerRecorder` counters is unconfirmed; do not depend on it without checking package source.

### Gaps
- No documented export (CSV/JSON) for Network Profiler or RNSM data.
- No confirmed public API for reading NGO's per-message byte counters at runtime.
- `-profiler-log-file` / `-profiler-enable` command-line flag syntax for Unity 6 players not confirmed in this research.

## 4. Soak / long-session testing (memory leaks, GC, frame time over hours)

### Takeaway
Use `Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot` from code at fixed times (start, +30 min, +2 h) and compare snapshots in the Memory Profiler package (GUI compare only); plus cheap per-second counters (managed heap, total memory, GC alloc, frame time) to JSONL for the AI to plot trends.

### Cited Findings
- `MemoryProfiler.TakeSnapshot(path, finishCallback, captureFlags)` in `Unity.Profiling.Memory`; callback reports success + path; defaults to managed + native objects; overload with screenshot callback; editor outside Play Mode makes no screenshot — [Unity 6000.0 Script Reference: MemoryProfiler.TakeSnapshot](https://docs.unity.com/en-us/engine/6000.0/script-reference/unity/profiling/memory/memoryprofiler/takesnapshot)
- Snapshots default to a `MemoryCaptures` folder in the project; path set in Preferences > Analysis > Profiling > MemoryProfiler — [Memory Profiler 1.0: snapshot capture](https://docs.unity3d.com/Packages/com.unity.memoryprofiler@1.0/manual/snapshot-capture.html)
- Comparison is done in the Memory Profiler window ("Compare Snapshots" mode, select two); snapshots from different sessions may not compare accurately — [Memory Profiler 1.1: snapshots comparison](https://docs.unity3d.com/Packages/com.unity.memoryprofiler@1.1/manual/snapshots-comparison.html)
- `ProfilerRecorder` can read memory and timing counters at runtime — [Unity ScriptReference: ProfilerRecorder](https://docs.unity3d.com/kr/6000.0/ScriptReference/Unity.Profiling.ProfilerRecorder.html)

### Inferences
- Soak recipe for the AI: host + 3–7 bots, `-runMinutes 120`, bots loop through a route (walk, pick up/drop items, open doors, use radio, die/respawn, scene reload if any), metrics JSONL every second, `TakeSnapshot` at 5 min and 115 min **in the same session** (so the diff is valid). Pass: managed heap and NetworkObject count flat after warm-up; no growing count of spawned objects/RPC subscribers; p99 frame time stable; zero exceptions in `Player.log`.
- Since snapshot diff is GUI-only, the AI reads the trend from JSONL and only asks the user (or uses MCP to open the window) when the trend shows growth.
- Also count NGO-specific leaks: `NetworkManager.SpawnManager.SpawnedObjects.Count`, connected clients, pooled objects — these are cheap and catch "despawn forgotten" bugs.

### Gaps
- No command-line or scripted snapshot diff found in Memory Profiler docs.
- No source on typical leak patterns specific to NGO 2.x long sessions.

## 5. Determinism, desync detection, replay recording

### Takeaway
No ready tool was found for NGO desync detection or netcode replay. NGO is server-authoritative state replication (not deterministic lockstep), so "desync" means a client's view diverges from the host's NetworkVariables/transforms; detection has to be self-built by hashing state on host and clients and comparing.

### Cited Findings
- Players of Lethal Company (Unity) report entities (e.g. Nutcracker, Coilhead) desyncing from the host "way too often", also in vanilla; mods cause weather/flood desyncs — anecdotal player reports — [Steam discussion (wishlist post)](https://steamcommunity.com/app/1966720/discussions/0/4034726898983980740); [Steam discussion (LateCompany mod desync)](https://steamcommunity.com/app/1966720/discussions/0/4032473196476445758)
- NGO integration tests run server + clients in one process, letting a test assert on both sides' state — [NetcodeIntegrationTest.cs mirror](https://plastichub.unity.cn/unity-tech-cn/MetacityTemplate/src/commit/720f3ddc-bab8-4e90-92bf-5cfac1c70369...Packages/com.unity.netcode.gameobjects@1.2.0/TestHelpers/Runtime/NetcodeIntegrationTest.cs)

### Inferences
- Self-built desync check: every N seconds each peer writes a line `{t, objectId, ownerId, pos (rounded), key NetworkVariables}` for all spawned NetworkObjects to its JSONL; Python aligns by time and flags objects whose state differs beyond a tolerance for > X seconds, or that exist on one side only (spawn/despawn desync). This catches the Lethal-Company-style "enemy desync" class of bugs.
- Replay for netcode bugs: record the bot input script + seed + net-sim settings per run; rerun the same files to reproduce. Full packet replay is not needed at Snow's scale.

### Gaps
- No NGO-specific desync detector, state-hash tool or replay recorder found.

## 6. Server-side budgets and how small Unity co-op games test multiplayer

### Takeaway
No public developer write-ups were found on how Lethal Company, R.E.P.O. or Content Warning test their multiplayer. Only player reports and modding tools exist, so budgets must come from Snow's own measurements.

### Cited Findings
- Lethal Company: Unity game by Zeekerss, early access on Steam 23.10.2023 — [Wikipedia: Lethal Company](https://en.wikipedia.org/wiki/Lethal_Company)
- Modding tooling (NetcodePatcher) exists for "assemblies that depend upon Unity Netcode for GameObjects", in the Lethal Company modding ecosystem; this suggests but does not confirm the game uses NGO — [NuGet profile evaisa](https://feed.nuget.org/profiles/evaisa)
- Player reports of rubber-banding regardless of host, sometimes fixed by changing VPN — anecdotal — [Steam discussion](https://steamcommunity.com/app/1966720/discussions/0/591761374510645905)
- A Lethal-Company-inspired indie devlog covers enemy spawning/syncing and lobby/join codes (third-party project, not the original devs) — [Candy Corporate devlog #7](https://jurgenwithwings.itch.io/candycorporate/devlog/847837/devlog-7-lobby-join-codes)

### Inferences
- Suggested Snow budgets to verify by own tests (not sourced): host frame time stays within target with 8 players; host upload is the real limit on a home connection, so measure bytes/s out at 4 and 8 players; target RTT tolerance — game should stay playable at "Home Satellite" (100 ms) and "Mobile 4G" (100 ms, 4% loss) presets.
- The listen-server host pays both render and server cost; test host performance on the weakest target PC, not the dev PC.

### Gaps
- No primary source on multiplayer QA practices of Lethal Company (Zeekerss), R.E.P.O. (semiwork) or Content Warning (Landfall); no confirmed netcode stack for these games from developer sources.
- No published bandwidth/CPU budgets for small NGO co-op games found.

## 7. Generic load tools (k6, Locust, Artillery) — when they apply

### Takeaway
Not researched with sources in this pass. They test HTTP/WebSocket backends only; they cannot speak NGO/UTP over UDP, so they do not apply to Snow's in-game netcode.

### Cited Findings
- Unity Relay, Lobby and Matchmaker remain available after the Multiplay shutdown (third-party claim) — [Gameye: game server shake-up 2026](https://gameye.com/blog/game-server-shake-up-2026)

### Inferences
- k6/Locust/Artillery only make sense if Snow gets its own HTTP backend (stats, save cloud, custom lobby). Unity Lobby/Relay and Steam are third-party services; load testing them is the provider's job and may break their terms of use.
- For Snow now (2–8 players, listen server, no own backend), these tools are not needed.

### Gaps
- No sources fetched on k6/Locust/Artillery licensing or game-backend usage in this pass.

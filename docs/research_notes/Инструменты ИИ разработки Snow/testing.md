# Automated testing and AI playtesting tools for Snow (Unity 6, URP, NGO 2.x, MPPM, Windows 11)

Research date: 2026-10-23. About 22 search/fetch calls. Items marked "(prior knowledge, unverified)" come from the researcher's background knowledge and were not confirmed by a source in this session.

## 1. Unity Test Framework, code coverage, Automated QA, Performance Testing: status and headless runs

### Takeaway
Unity Test Framework (UTF) is now part of the engine (embedded package since 1.5.1, Feb 2025). It runs EditMode and PlayMode tests headless from the command line and writes NUnit XML, which an agent can parse directly. Code coverage uses the `-enableCodeCoverage` flag. The Performance Testing package is maintained (3.x line). Unity Automated QA (`com.unity.automated-testing`) has been on hold and unsupported since Dec 2021. Do not use it.

### Cited Findings
- Command-line form: `Unity.exe -runTests -batchmode -projectPath <path> -testResults C:\temp\results.xml -testPlatform <EditMode|PlayMode|target>`. Results are NUnit-format XML and go to the project root by default. Without `-testPlatform`, tests run in EditMode — [Unity 6.2 manual: command line](https://docs.unity3d.com/Manual/test-framework/reference-command-line.html)
- Other flags: `-assemblyNames` (list separated by `;`), `-testSettingsFile` (TestSettings.json), `-orderedTestListFile` (txt file of full test names to run in order), `-repeat N`, `-retry N`. The regular `-quit` flag is not supported while tests run, because the runner exits by itself — [Unity 6.2 command line ref](https://docs.unity3d.com/6000.2/Documentation/Manual/test-framework/reference-command-line.html); [6000.3 docs](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/test-framework-introduction/reference-command-line)
- UTF 1.5.1 (2025-02-26) moved into Unity's source code as an embedded package. It also fixed retry/repeat counts resetting on domain reload. Test-level retry/repeat has existed since 1.3.5 — [UTF 1.5 changelog](https://docs.unity3d.com/Packages/com.unity.test-framework@1.5/changelog/CHANGELOG.html)
- Code coverage: set `Coverage.enabled` from the API, or pass `-enableCodeCoverage` in batchmode. The Code Coverage package collects coverage from automated tests and also offers "Coverage Recording" for manual play sessions — [Unity 6.0 ScriptReference: TestTools.Coverage](https://docs.unity3d.com/kr/6000.0/ScriptReference/TestTools.Coverage.html)
- Performance Testing (`com.unity.test-framework.performance`): its API includes `Measure.Method`, `Measure.Frames`, `Measure.Scope`, `Measure.ProfilerMarkers` and `Measure.Custom` — [Perf Testing 2.0 manual](https://docs.unity3d.com/Packages/com.unity.test-framework.performance@2.0/manual/index.html). The 3.4 changelog says the data format was refactored with less nesting, and the package switched to the Newtonsoft JSON package — [Perf Testing 3.4 changelog](https://docs.unity3d.com/Packages/com.unity.test-framework.performance@3.4/changelog/CHANGELOG.html). Unity 6.3 docs show `Measure.Frames().Scope()` in a sample PlayMode perf test — [Unity 6.3 manual: Performance tests (Lost Crypt)](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/test-framework-introduction/overview/lost-crypt-introduction/performance-tests.md)
- Automated QA (`com.unity.automated-testing`): development paused Dec 2021. It is unsupported, won't be updated for newer Unity versions, and its forum and email are unmonitored. Cloud device testing was shut down. Local recorded playback was said to keep working — [Unity blog (RU)](https://blog.unity.com/ru/games/on-demand-qa-testing-with-unity-automated-qa); [package docs](https://docs.unity3d.com/Packages/com.unity.automated-testing@latest)

### Inferences
- For an agent, the simplest loop is a Python wrapper in tools/snow_qa. It launches `Unity.exe -batchmode -runTests ... -testResults X.xml -logFile Y.log`, parses the NUnit XML (`test-case` nodes with `result`, `message`, `stack-trace`), and greps the log for `Exception`/`Error`. Note: batchmode needs the project to be closed in the interactive Editor, because Unity locks a project to one instance. So with the Editor open, run tests through MCP or the in-Editor TestRunnerApi instead.
- In-Editor alternative (prior knowledge, unverified): `UnityEditor.TestTools.TestRunner.Api.TestRunnerApi.Execute(new ExecutionSettings(filter))` plus an `ICallbacks` that writes results to a file. An agent can trigger it via Unity MCP `RunCommand` without closing the Editor.
- Automated QA's recorder concept (record input, replay as test) has no supported Unity successor. Build your own on Input System events (see section 2).

### Gaps
- Exact 3.x Performance Testing JSON schema not confirmed. Check a real TestResults.xml or the package source.
- Whether UTF batchmode PlayMode runs need `-nographics` off for URP rendering/screenshots was not confirmed (rendering tests need a GPU, so don't use `-nographics` for them).

## 2. UI/game automation drivers: AltTester, GameDriver, Airtest/Poco, Input System test fixtures, record/replay

### Takeaway
For a solo dev plus an AI agent, the most practical free options are Input System's `InputTestFixture` inside UTF PlayMode tests, plus your own in-game command channel (MCP or a socket). AltTester still has a free Community tier, but since late 2024 it needs an account and a license key, and its newest features ship non-GPL first. GameDriver is commercial (free for solo devs per a 2025 press release). Airtest/Poco is mobile-oriented and its Unity 6 status is unconfirmed.

### Cited Findings
- `InputTestFixture`: derive the test class, add devices with `InputSystem.AddDevice<T>()`, then feed input with `Press`, `Release`, `PressAndRelease`, `Set(control, value)` and `Trigger(action)`. The fixture restores Input System state after each test — [Input System 1.6 manual: Testing](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.6/manual/Testing.html). Unity's own tutorial drives a third-person controller this way in PlayMode tests — [Unity how-to: automated tests with UTF](https://unity.com/how-to/automated-tests-unity-test-framework)
- AltTester licensing (blog post of 27 Nov 2024): a non-GPL Unity SDK is released, with UI Toolkit support. New features land there first. All plans, including the free Community plan, need an AltTester account and license key. Community stays free for small studios, with a revenue threshold that is not stated. Source access costs extra. A GPL version remains — [AltTester blog](https://alttester.com/the-next-chapter-for-alttester-expanding-with-unreal-sdk-and-licensing-updates/). The SDK 2.1.x license page says GPLv3 — [AltTester 2.1.1 license](https://alttester.com/docs/sdk/2.1.1/pages/license.html). The 2.2.x pages point to a LICENSE file instead — [2.2.5 license](https://alttester.com/docs/sdk/2.2.5/pages/license.html)
- GameDriver: the March 2025 press release says it is "free for solo developers and educators", with starter plans from $150/month — [BusinessWire 2025-03-17](https://www.businesswire.com/news/home/20250317108053/en). The pricing page lists $200–$400/node/month tiers — [GameDriver pricing](https://www2.gamedriver.io/en-us/pricing). Unity 6 (preview) support was added in 2024.07 — [GameDriver 2024.07](https://gamedriver.io/blog/2024/july/gamedriver-202407-for-unreal-engine-and-unity). The free-solo terms and the per-node prices conflict, so verify before relying on either.
- Airtest + Poco (NetEase): Airtest is image-based and Poco is object-hierarchy based. Both are scripted in Python and best suited to mobile games — [QAwerk comparison](https://qawerk.com/blog/game-testing-automation-tools/). The Unity3D Poco driver docs still point to the AirtestProject/Poco-SDK repo — [Poco Unity3D driver](https://poco.readthedocs.io/en/dev/source/doc/drivers/unity3d.html)

### Inferences
- Snow already has an agent-to-Editor bridge (Unity MCP `RunCommand`) and snow_qa. Adding AltTester or GameDriver would duplicate that bridge and add licensing friction. A thin in-game "test hooks" layer gives the same power: teleport, give item, press action, read state as JSON. Expose it through a dev command or a local socket and drive it from snow_qa.
- Record/replay (self-build): Input System supports `InputEventTrace` for capturing and replaying device events (prior knowledge, unverified). A replay of a recorded "walk to van, open door, pick up item" session, run as a PlayMode test, is a cheap regression test.
- Snow uses OnGUI and Dear ImGui for UI, not uGUI or UI Toolkit. Object-tree drivers like AltTester and Poco mostly target uGUI and UI Toolkit, so they would add little for Snow's UI anyway.

### Gaps
- No source confirmed that AltTester SDK 2.2.x is officially certified on Unity 6000.0, or what the Community revenue threshold is.
- Airtest/Poco: no source on Unity 6 support or whether the repo is archived.

## 3. AI playtesting agents: ML-Agents, LLM/VLM agents, commercial services

### Takeaway
Research and industry (NetEase TITAN, WeChat AstraGame) show LLM/VLM agents finding real bugs. But a 2026 study finds that VLMs give many false positives, so they work best as high-recall filters followed by verification. For a small team, the practical setup is a self-built loop: Claude Code drives the game via MCP or test hooks, takes screenshots, reads state and logs, and judges. Commercial options (Razer AI QA Copilot, modl.ai) are cloud and enterprise products with unclear solo pricing. ML-Agents is still alive (4.0.x) but is RL training, not QA.

### Cited Findings
- TITAN (arXiv 2509.22170, Sep 2025, NetEase Fuxi and others): an LLM-driven agent for MMORPG testing. It found four bugs that earlier testing approaches missed, and it is deployed in eight game QA pipelines — [arXiv 2509.22170](https://arxiv.org/pdf/2509.22170v1)
- VLM geometry-clipping study (arXiv 2607.25921, Jul 2026): pairs autonomous exploration with VLM anomaly detection. It finds many false positives on ambiguous frames and concludes VLMs work best as high-recall candidate filters in multi-stage pipelines, not as standalone detectors — [alphaxiv 2607.25921](https://www.alphaxiv.org/abs/2607.25921.md)
- AstraGame (FSE 2026 industry track): VLM testing deployed on WeChat mini-games. Small specialized models handle perception and caching, and large VLMs handle reasoning, to cut cost and latency — [FSE 2026](https://conf.researchr.org/details/fse-2026/fse-2026-industry-papers/50/AstraGame-Effective-and-Efficient-VLM-Agent-Serving-for-Large-Scale-Game-Testing-in-)
- BALROG benchmark (ICLR 2025): several models performed worse when given visual input instead of text — [arXiv 2411.13543](https://arxiv.org/abs/2411.13543v2). GamingAgent (lmgame-org) is an open-source harness where LLM/VLM agents play games — [GitHub](https://github.com/lmgame-org/GamingAgent)
- Razer AI QA Copilot (Wyvrn): a cloud plugin for Unreal, Unity and C++ engines. It monitors gameplay, detects errors and performance issues, and writes QA reports. Razer claims 20–25% more defects found and up to 50% less QA time; these are vendor figures — [KitGuru](https://www.kitguru.net/gaming/joao-silva/razer-gets-into-ai-with-new-wyvrn-ecosystem/); [CO/AI](https://getcoai.com/?p=58197)
- modl.ai: bots that model player behavior to find issues across hardware. Little detail was found — [AI Game Lab newsletter](https://ai-game-lab.beehiiv.com/p/ai-powered-qa-automated-bug-hunting-and-performance-tuning)
- ML-Agents: 4.0.0 commit dated 2025-09-02, 4.0.2 dated 2026-02-16. A forum report shows 4.0.2 in use on Unity 6.3 LTS — [mirror commit log](https://code.morphllm.com/Unity-Technologies/ml-agents/commits/branch/develop); [Unity discussions ml-agents tag](https://discussions.unity.com/tag/ml-agents/159289)
- Community Unity MCP servers: IvanMurzak/Unity-MCP (Apache-2.0, about 3k stars) also runs inside a compiled game build for runtime AI interaction — [listing](https://mcpservers.org/fr/servers/github-com-ivanmurzak-unity-mcp). "Unity Code" MCP exposes EditMode/PlayMode test runs and needs Unity 6+ — [mcp.so](https://mcp.so/servers/unity_code_mcp)

### Inferences
- Recommended agent loop for Snow (self-build, $0 beyond the Claude subscription):
  1. Give the agent structured state (player position, held item, quest step, NetworkObject counts) as JSON. Don't rely only on pixels; BALROG and the clipping study both suggest text state beats vision alone.
  2. Script the goal ("walk from yard to van, start engine") as scripted waypoints and actions, not free play.
  3. Take screenshots at checkpoints. A VLM judges them only to flag suspicious frames, and a human or second check confirms (the pattern from the clipping paper).
  4. Write an HTML or Markdown report with pass/fail, screenshots and log excerpts.
- ML-Agents only pays off for learned bots (for example a monster AI or an explorer that learns to reach places). Training cost and Python setup are high for a solo dev. A NavMesh random-walk bot gives most of the coverage value for less.
- A second Unity MCP server (IvanMurzak) that runs in player builds could let the agent drive standalone builds, not only the Editor. The official com.unity.ai.assistant MCP is Editor-only (inference, not verified).

### Gaps
- No pricing or solo-dev availability found for Razer AI QA Copilot or modl.ai in 2026.
- No GDC 2023–2026 talk was verified this session on LLM playtesters at specific studios (Riot, King, Ubisoft).

## 4. Visual regression testing: screenshot diffs, perceptual metrics, VLM-as-judge

### Takeaway
Unity's Graphics Test Framework (`com.unity.testframework.graphics`, used by Unity's own SRP tests) gives `ImageAssert.AreEqual` with per-pixel and whole-image thresholds plus reference image workflows. Outside Unity, a Python SSIM or pixelmatch diff in snow_qa over fixed-camera screenshots is simpler and enough for a solo project. Use a VLM only as a coarse judge ("is the scene black / missing terrain / UI overlapping?").

### Cited Findings
- Graphics Test Framework: render an image, compare it to a reference with `ImageAssert.AreEqual()`. A camera overload captures automatically. Thresholds can be set per pixel and per whole image to tolerate driver differences. Reference images are produced by running tests and then using "Extract images from TestResults.xml". A shared `ReferenceImagesBase` folder is used across APIs and platforms. The Engine/Editor `TestTools.Graphics` asmdefs must be referenced — [Graphics Test Framework 8.9 manual](https://docs.unity3d.com/Packages/com.unity.testframework.graphics@8.9/manual/index.html)
- The VLM false-positive finding in section 3 applies to screenshot judging too — [alphaxiv 2607.25921](https://www.alphaxiv.org/abs/2607.25921.md)

### Inferences
- The package is published as preview/internal (versions 7.x and 8.x). Its docs don't mention Unity 6 or URP, so expect some friction installing it via git URL. A self-built compare in Python is lower risk (prior knowledge, unverified): scikit-image `structural_similarity`, or `pixelmatch` (Python port), with a mask for HUD and random elements such as snow particles and wind.
- Visual tests need determinism: fixed camera bookmarks, fixed time of day and weather, a fixed random seed, VFX and particles off or paused. Snow's weather, fog and flashlight flicker would otherwise break every diff. Add a "test capture mode" flag.
- Snow already has screenshot capture (snow_qa and MCP `Camera_Capture`). The missing pieces are a reference set and a diff and report step.
- The "dark night was a black void" incident (memory: forest-night) is exactly the regression type a mean-luminance or histogram check plus a VLM "is anything visible?" prompt would catch cheaply.

### Gaps
- No verified source this session for LPIPS or pixelmatch versions or licenses, or for a Unity 6 URP-specific graphics test setup.

## 5. Smoke and soak tests: walk bots, stuck/fall detection, exception sweeps, validators, static analysis

### Takeaway
Almost all of this is self-built and cheap. The proven references: Rare's Sea of Thieves (gameplay tests from day one, with a custom framework and several test types) and Unity's own UTF features (`LogAssert`, retry/repeat). For static analysis, Microsoft.Unity.Analyzers (free, MIT, NuGet 1.25.0 as of Aug 2025) is the standard Unity-aware Roslyn set. Unity 6 loads analyzer DLLs labeled `RoslynAnalyzer` from Assets.

### Cited Findings
- Microsoft.Unity.Analyzers: Roslyn analyzers for Unity, latest NuGet 1.25.0 (2025-08-22), targets .NET Standard 2.0. They ship in Visual Studio's Unity workload, and VSTU adds them to generated projects. Installing them also as a VSIX can duplicate diagnostics — [NuGet profile](https://www.nuget.org/profiles/VisualStudioToolsForUnity); [GitHub](https://github.com/microsoft/Microsoft.Unity.Analyzers)
- Unity 6: to make analyzers apply to Unity's own compile, put the DLL in Assets and label it `RoslynAnalyzer` — [Unity 6 manual: install existing analyzer](https://docs-alpha.unity3d.com/Manual/install-existing-analyzer.html); [create Roslyn analyzer 6.5](https://docs.unity3d.com/Manual/create-roslyn-analyzer.html)
- Sea of Thieves: built with automated gameplay testing from the start. Rare built its own framework for quick test authoring, used several test types, and chose a matching level of coverage — [GDC 2019 talk](https://gdcvault.com/play/1026366/Automated-Testing-of-Gameplay-Features); [GDC news](https://gdconf.com/news/sea-thieves-devs-share-automated-testing-tips-gdc-2019). Microsoft says the project has "hundreds of thousands" of automated tests — [MS Learn](https://learn.microsoft.com/en-us/shows/pure-virtual-cpp-2024/automated-testing-of-shader-code). Unreal Fest talk: [Automated Testing at Scale in Sea of Thieves](https://www.unrealengine.com/events/unreal-fest-europe-2019/automated-testing-at-scale-in-sea-of-thieves)
- UTF `-repeat`/`-retry` flags allow soak-style repeats of a test — [command line ref](https://docs.unity3d.com/6000.2/Documentation/Manual/test-framework/reference-command-line.html)

### Inferences (self-build recipes; API names are prior knowledge, unverified)
- **Exception sweep**: by default UTF fails a test on any unhandled `LogType.Error` or `Exception` (`LogAssert`). A PlayMode test that loads each scene (Forest, SkyIsland, BaseV2), runs N seconds as host, and fails on any error log gives a zero-effort null-ref sweep.
- **Walk bot / NavMesh explorer**: pick random points with `NavMesh.SamplePosition` and walk the real player controller there via InputTestFixture or a direct move API. Log each point reached. Flags:
  - stuck: no progress > X m in T s
  - fall-through-floor: player y below terrain height minus margin, or below a kill-plane
  - NaN positions
  - FPS dips: record with `Measure.Frames`
  Write a heatmap PNG of visited and failed cells for the human report.
- **Scene/asset validators**: Editor scripts that iterate prefabs and scenes to check:
  - missing scripts and missing references
  - NetworkObjects without a NetworkManager prefab-list entry
  - colliders on player-walkable meshes
  - materials using non-URP shaders (pink)
  - LOD and lightmap sanity
  Run them in batchmode with `-executeMethod`. Odin Validator is paid (Asset Store); Unity's free Project Auditor package does static checks for code and settings. Not verified this session.
- Snow should keep the analyzer set to Microsoft.Unity.Analyzers plus maybe a few project rules (for example "no `FindObjectOfType` in Update"). Labeling the DLL `RoslynAnalyzer` makes warnings appear in the Unity console, which the agent already reads via MCP.

### Gaps
- No verified source this session for Odin Validator pricing or Unity 6 support, or for Unity Project Auditor's package status in Unity 6.

## 6. Crash and exception capture in tests and CI on Windows (GitHub Actions, GameCI, self-hosted, licensing)

### Takeaway
GameCI is the standard free path (unity-test-runner, unity-builder). Its documented Personal-license flow uses a `.ulf` from Unity Hub stored as a GitHub secret, but forum reports say manual Personal activation broke at some point, so verify. For a solo Windows dev with the machine already licensed, a self-hosted GitHub Actions runner (or just a local scheduled script) running `Unity.exe -batchmode -runTests` avoids license activation in CI entirely. GameCI's Docker images are mainly Linux-focused.

### Cited Findings
- GameCI Personal license: in Unity Hub, Preferences → Licenses → Add → free personal license (make sure the `.ulf` is actually created). Copy `C:\ProgramData\Unity\Unity_lic.ulf` into the `UNITY_LICENSE` secret, plus `UNITY_EMAIL` and `UNITY_PASSWORD`. Pro/Plus licenses use `UNITY_SERIAL`. A floating license server is supported via `unityLicensingServer`. "Licenses are not tied to a specific Unity version or platform" — [GameCI activation docs](https://game.ci/docs/github/activation)
- Conflict: a Unity forum thread titled "Unity no longer supports manual activation of Personal licenses" says the .alf→.ulf web flow broke, affecting GameCI (unity-test-runner issue #235) — [Unity Discussions](https://discussions.unity.com/t/unity-no-longer-supports-manual-activation-of-personal-licenses/926760). The current GameCI docs use the Hub-generated .ulf instead, which seems to be the workaround.
- A GameCI Windows activation script (unity-builder `activate.ps1`) uses UNITY_EMAIL, UNITY_PASSWORD and UNITY_SERIAL or UNITY_LICENSE — [Gitea mirror](https://gitea.com/dncry/unity-builder/src/branch/main/dist/platforms/windows/activate.ps1) (third-party mirror)

### Inferences
- For Snow:
  - The repos are private, so GitHub-hosted Windows minutes cost money.
  - URP PlayMode and visual tests need a GPU.
  - The project is large (LFS assets, 157 MB scene history).
  So a self-hosted runner on the dev PC, or simply snow_qa run by the agent, is the realistic CI.
- Crash capture (prior knowledge, unverified):
  - Always pass `-logFile <path>`.
  - Treat a non-zero exit code or a missing results XML as a crash.
  - Unity Editor crashes write to `%LOCALAPPDATA%\Temp\Unity\Editor\Crashes`.
  - Player builds write to `%LOCALAPPDATA%Low\<Company>\<Product>\Crashes` (Player.log, crash.dmp).
  - Add `Application.logMessageReceivedThreaded` in builds to dump exceptions to a JSON file the agent can read.
- Exit codes for `-runTests` (prior knowledge, unverified): 0 = all passed, 2 = some tests failed, 3 = run error. snow_qa should check these.

### Gaps
- No confirmed 2026 statement from Unity on Personal-license activation for headless CI on Unity 6 (Unity Hub CLI or licensing client). GameCI Windows self-hosted support for Unity 6 was not verified.

## 7. Netcode-specific testing: NGO integration test helpers, multiple clients

### Takeaway
NGO ships its own `NetcodeIntegrationTest` base class (in the package's TestHelpers). It spins up a host plus N clients in a single process inside a UTF PlayMode test. That is the right tool for automated co-op tests. MPPM (max 4 players) is for manual or agent-observed sessions and has no built-in automation API.

### Cited Findings
- `NetcodeIntegrationTest`: override `NumberOfClients`. Clients can be added mid-test with `CreateAndStartNewClient()`. Client managers are in `m_ClientNetworkManagers`. The instantiation mode (per test or per fixture) can be overridden. This is from the NGO 1.2.0 source mirror, so 2.x may differ — [NetcodeIntegrationTest.cs (1.2.0 mirror)](https://plastichub.unity.cn/unity-tech-cn/MetacityTemplate/raw/commit/720f3ddc-bab8-4e90-92bf-5cfac1c70369...Packages/com.unity.netcode.gameobjects@1.2.0/TestHelpers/Runtime/NetcodeIntegrationTest.cs); example usage in [OwnerModifiedTests.cs](https://plastichub.unity.cn/unity-tech-cn/MetacityTemplate/src/branch/main...Packages/com.unity.netcode.gameobjects@1.2.0/Tests/Runtime/OwnerModifiedTests.cs)
- MPPM: a Unity staff answer says there is no built-in way to automate virtual players. Connect them yourself (pipes, sockets, Unity Transport). MPPM supports up to 4 editor players (main plus 3 virtual). Virtual players cannot create or change GameObjects in the editor. Play Mode Scenarios arrived as experimental in MPPM 1.3 — [Unity Discussions: automated gameplay testing with MPPM](https://discussions.unity.com/t/automated-gameplay-testing-with-multiplayer-play-mode/1626883); [MPPM 2.0 docs](https://docs.unity3d.com/Packages/com.unity.multiplayer.playmode@2.0/manual/index.html); [MPPM scenarios announcement](https://discussions.unity.com/t/multiplayer-play-mode-now-extends-play-mode-with-custom-scenarios/952004)

### Inferences
- `NetcodeIntegrationTest` lives in the NGO package's TestHelpers assembly. Using it from game tests may require adding `com.unity.netcode.gameobjects` to `testables` in manifest.json, and referencing `Unity.Netcode.TestHelpers.Runtime` in the test asmdef. Check the installed 2.x package source in `Library/PackageCache` before writing tests.
- Good first NGO tests for Snow:
  - item pickup authority: pocket slot sync to all clients
  - van door and engine state sync
  - late-join client sees placed objects
  - 4 clients spawn without duplicate NetworkObjectIds
  - disconnect and rejoin
- For standalone multi-process soak tests: build a player and launch 1 host plus 3 clients via command-line args. Each logs to its own file, and snow_qa merges the logs. This is how you test real transport latency, which in-process tests skip.
- MPPM auto-join (the user wants the agent to join clients itself) works best via a small IPC hook. The forum thread confirms there is no official one.

### Gaps
- No official NGO 2.x documentation page on `NetcodeIntegrationTest` was found this session. Exact 2.x API names and whether the helpers are still public should be checked in the installed package.

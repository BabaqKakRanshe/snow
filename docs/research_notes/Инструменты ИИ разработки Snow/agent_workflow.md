# Agent workflow tooling for Snow (Claude Code, solo dev, Unity 6), as of Oct 2026

Scope: tools and practices that let a solo developer either (a) keep tight control and visibility over AI work, or (b) hand whole tasks to AI safely. Research budget: ~18 tool calls. Official Anthropic docs (code.claude.com) were fetched directly. Most third-party claims come from search snippets and are marked as such.

## 1. Claude Code built-in features useful for control vs delegation

### Takeaway
By Oct 2026 Claude Code has a deep set of built-in control points. There are 30+ hook events that can block, rewrite or verify actions, including LLM-judged `prompt` hooks and `agent` hooks. There is OpenTelemetry export, background agents, cloud scheduled tasks, local `/code-review` and GitHub Actions. For a solo developer, the strongest built-in combination is hooks for hard rules, subagents for isolated side work, skills for repeatable procedures and Stop/TaskCompleted hooks as verification gates. Most "control" tooling can be built from these without third-party orchestrators.

### Cited Findings
**Hooks (official reference)**
- Hook events include SessionStart, UserPromptSubmit, PreToolUse, PermissionRequest, PostToolUse, PostToolUseFailure, PostToolBatch, SubagentStart/Stop, TaskCreated, TaskCompleted, Stop, StopFailure, TeammateIdle, InstructionsLoaded, ConfigChange, FileChanged, WorktreeCreate/Remove, PreCompact/PostCompact, Pre/PostModelSwitch, Elicitation, SessionEnd and others. — [Claude Code hooks reference](https://code.claude.com/docs/en/hooks)
- Blocking events: PreToolUse, UserPromptSubmit, PostToolBatch, SubagentStop, TaskCreated, TaskCompleted, Stop ("continues the conversation"), PreCompact, ConfigChange, WorktreeCreate/Remove. PostToolUse cannot block, but its stderr is shown to Claude. — [hooks reference](https://code.claude.com/docs/en/hooks)
- Hook types: `command` (shell), `http` (POST JSON to a URL), `mcp_tool` (calls a tool on an MCP server), `prompt` (single-turn Claude evaluation returning a JSON decision) and `agent` (spawns a subagent with Read/Grep/Glob to verify conditions; marked experimental). — [hooks reference](https://code.claude.com/docs/en/hooks)
- PreToolUse can return `allow`/`deny`/`ask`/`defer` and rewrite tool input via `updatedInput`. PostToolUse can replace tool output via `updatedToolOutput`. — [hooks reference](https://code.claude.com/docs/en/hooks)
- Notable options: `async: true` and `asyncRewake` (background hook that wakes Claude on exit 2), the `if` filter in permission-rule syntax (e.g. `Bash(git *)`, `Edit(*.ts)`), `onFailure: "block"` (v2.1.295+), `additionalContext` injection, `terminalSequence` for desktop notifications, and a read-only `/hooks` menu. Hooks also run inside subagents, and their input includes `agent_id`/`agent_type`. Hooks can live in settings, plugin `hooks/hooks.json`, or skill/subagent frontmatter. — [hooks reference](https://code.claude.com/docs/en/hooks)
- Practitioner consensus: hooks are the enforcement layer because they are executed by software, while CLAUDE.md is "context, not enforced configuration". — [spotdev.co.uk](https://www.spotdev.co.uk/blog/rules-skills-subagents-hooks-ai-coding) (cites Anthropic docs)
- Common split: subagents keep exploration and review out of the main context, hooks run deterministic checks, skills hold domain knowledge loaded only when relevant, and CLAUDE.md/settings hold configuration. — [smartscope.blog](https://smartscope.blog/en/generative-ai/claude/claude-code-best-practices-advanced-2026/)
- Keep CLAUDE.md short because it loads every session and consumes context. — [levelup.gitconnected.com](https://levelup.gitconnected.com/a-mental-model-for-claude-code-skills-subagents-and-plugins-3dea9924bf05)
- Skills can run in forked-subagent mode via frontmatter. Plugins bundle skills, subagents, hooks and MCP servers, with user/project/local scopes and an official Anthropic marketplace. — [developerintheloop.substack.com](https://developerintheloop.substack.com/p/the-ultimate-guide-to-configuring)

**Surfaces and automation (official overview)**
- Background agents: "run several full sessions in parallel and watch them from one screen" (agent view). — [Claude Code overview](https://code.claude.com/docs/en/overview)
- Subagents / agent teams: "a lead agent coordinates the work, assigns subtasks, and merges results". "Dynamic workflows" orchestrate many subagents at once. — [overview](https://code.claude.com/docs/en/overview), [A harness for every task (Anthropic blog)](https://claude.com/blog/a-harness-for-every-task-dynamic-workflows-in-claude-code)
- Headless `claude -p` for piping, CI and bulk operations (e.g. `git diff main --name-only | claude -p "review these changed files…"`). — [overview](https://code.claude.com/docs/en/overview)
- Scheduling comes in three forms: cloud scheduled tasks (formerly "routines") run in the cloud even when the PC is off and can trigger on API calls or GitHub events, created via web, Desktop or `/schedule`. Desktop scheduled tasks run locally with access to local files and tools. `/loop` repeats a prompt inside a CLI session. — [overview](https://code.claude.com/docs/en/overview)
- Routines launched as a research preview on 14 Apr 2026: a saved bundle of prompt, repos and connectors triggered by schedule, API or GitHub event, running on Anthropic infrastructure. — [makerkit.dev guide](https://makerkit.dev/blog/tutorials/claude-code-routines-guide), [Anthropic: Introducing routines](https://claude.com/resources/articles/introducing-routines-in-claude-code), [DataCamp](https://datacamp.com/tutorial/claude-code-routines)
- Remote Control (continue a local session from a phone or browser), Dispatch, `claude --teleport` (pull a web session into the terminal), `/desktop` handoff for visual diff review (Windows x64 supported), and Channels (push Telegram/Discord/webhook events into a session). — [overview](https://code.claude.com/docs/en/overview)
- The Desktop app (Windows x64/ARM64) reviews diffs visually, runs several sessions side by side, schedules tasks and starts cloud sessions. — [overview](https://code.claude.com/docs/en/overview)
- Auto memory: "Claude also builds auto memory as it works, saving learnings across sessions". AGENTS.md is read in place of CLAUDE.md if present. — [overview](https://code.claude.com/docs/en/overview)

**Code review options (official)**
- Managed GitHub Code Review (Team/Enterprise only): several agents find issues, a verification step filters false positives, then findings are ranked by severity and posted as inline comments plus a "Claude Code Review" check run. It never approves or blocks a PR. Levels are Quick, Standard and Deep. It is tuned with `REVIEW.md`, read from the base branch, never from the PR itself. It does not build or run code. — [Code Review docs](https://code.claude.com/docs/en/code-review)
- Available on any plan: `/code-review` locally, Ultrareview (cloud sandbox, needs a claude.ai login), Claude in your own CI via GitHub Actions/GitLab CI (needs an API key), or your own reviewer subagent or skill. — [Code Review docs](https://code.claude.com/docs/en/code-review)

### Inferences
- For Snow (solo, Unity, Windows), the most valuable cheap additions are:
  1. A **Stop hook** that runs `tools/snow_qa` or a compile check and returns exit 2 with errors, so Claude cannot finish a turn while the Unity console shows compile errors.
  2. A **PostToolUse hook on Edit|Write of `*.cs`** that triggers a Unity refresh/compile and feeds errors back.
  3. A **`prompt` or `agent` hook on TaskCompleted/Stop** that checks "did the agent actually verify, e.g. take a screenshot or run the check, or only claim it?". This directly targets the pain point that verification is manual.
  4. A **SessionEnd/PreCompact hook** that appends a session summary (git diff stat, touched files, open questions) to a dated log in docs/. This targets lost context between sessions.
- Delegation path: background agents or cloud scheduled tasks are suited to text/doc work (GDD question lists, research, devlog drafts). They are poorly suited to Unity Editor work, because a cloud sandbox has no Unity Editor, licence or MCP bridge to the local editor. Desktop scheduled tasks are the local alternative.
- `/desktop` handoff and Desktop visual diffs are a low-effort "visibility" upgrade over terminal scrollback.

### Gaps
- Checkpoints/rewind (`/rewind`, Esc-Esc), worktree support (`--worktree`, EnterWorktree), output styles and status line config were not fetched from the official docs this session. They exist (the local environment exposes EnterWorktree/ExitWorktree tools and a statusline-setup agent), but exact behaviour and limits were not verified.
- No experienced-user survey with numbers on which features matter most. The guides found are opinion pieces.
- Exact pricing of cloud scheduled tasks and Ultrareview on Max/Pro plans was not checked.

## 2. Dashboards and observability for agent sessions; task boards linked to agents

### Takeaway
There are two tiers. Local log readers (ccusage-style tools and statuslines) work with zero setup, while official OpenTelemetry export (metrics and events GA, traces beta) can feed a Grafana stack, which the developer already has via Grafana MCP. Markdown-in-git task boards (Backlog.md, Spec Kit) fit a "docs in git" workflow better than SaaS boards. ClickUp MCP is already connected.

### Cited Findings
- OTel: enable with `CLAUDE_CODE_ENABLE_TELEMETRY=1` plus standard `OTEL_*` vars. Metrics and events are generally available, traces are beta (`CLAUDE_CODE_ENHANCED_TELEMETRY_BETA=1`, `OTEL_TRACES_EXPORTER=otlp`). Prometheus scrape is possible at `localhost:9464/metrics`. — [Claude Code monitoring docs](https://code.claude.com/docs/en/monitoring-usage)
- Metrics: `claude_code.session.count`, `lines_of_code.count`, `pull_request.count`, `commit.count`, `cost.usage`, `token.usage`, `code_edit_tool.decision`, `active_time.total`. Events: `user_prompt`, `assistant_response`, `tool_result`, `tool_decision`, `api_request`, `api_error`, `api_refusal`, `safety_event`, and others. Traces have a `claude_code.interaction` root span with `llm_request`, `tool` and `hook` child spans. Trace context propagates to Bash subprocesses via `TRACEPARENT`. — [monitoring docs](https://code.claude.com/docs/en/monitoring-usage)
- Privacy defaults: prompts, tool parameters, tool content and responses are redacted unless `OTEL_LOG_USER_PROMPTS`, `OTEL_LOG_TOOL_DETAILS`, `OTEL_LOG_TOOL_CONTENT` or `OTEL_LOG_ASSISTANT_RESPONSES` is set. Repository `.claude/settings.json` cannot set the OTel exporter variables. — [monitoring docs](https://code.claude.com/docs/en/monitoring-usage)
- ccusage reads Claude Code's local `.jsonl` logs for fast reports. Claude-Code-Usage-Monitor gives a real-time terminal dashboard. — [eesel.ai guide](https://www.eesel.ai/blog/monitoring-claude-code)
- ccmon is a terminal dashboard inspired by ccusage that receives OTLP data instead of reading logs. — [ccmon GitHub](https://github.com/Yukaii/ccmon)
- claude-code-otel provides preconfigured Grafana dashboards (cost, user activity, tool performance) via Docker. Other write-ups cover SigNoz and OpenObserve backends. — [eesel.ai](https://www.eesel.ai/blog/monitoring-claude-code), [SigNoz](https://signoz.io/blog/claude-code-monitoring-with-opentelemetry), [OpenObserve](https://openobserve.ai/blog/monitor-claude-code-usage-openobserve-mcp/)
- Tools listed in awesome-claude-code (partial read):
  - Usage: claude-statusbar (5-hour/7-day limits, context, cost), Sidekick for Max (VS Code and terminal quota heatmap).
  - Session viewers: Agent Workbench (local web UI for past sessions, context meter), Vibeyard (desktop IDE with a real-time session inspector and kanban), vir (transcripts to typed markdown notes).
  - Statuslines: ccstatusline, CCometixLine (Rust), Claude HUD (context, tools, agents, todos), claude-powerline.
  - Notification hooks: anotifier (desktop and phone push when a task finishes or needs input), Claudio (sounds).
  - Doc review: MDXG Redline (inline browser comments fed back to AI-written docs).
  - Sharing: Claude Threads (stream a session to Slack/Mattermost for watching and approving).
  - [awesome-claude-code README](https://github.com/hesreallyhim/awesome-claude-code)
- Backlog.md: a "Markdown-native Task Manager & Kanban visualizer for any Git repository". Claude Task Master is listed as a task and workflow manager for Claude Code. — [nikiforovall Claude Code rules blog](https://nikiforovall.blog/claude-code-rules/category/extras)
- GitHub Spec Kit: an open-source CLI that turns feature descriptions into specs, implementation plans and task breakdowns, with "constitutional principles" (project governance) and templates. It works with Claude Code. — [GitHub blog](https://github.blog/2025-09-02-spec-driven-development-with-ai-get-started-with-a-new-open-source-toolkit/), [context7 spec-kit](https://context7.com/github/spec-kit)
- Plain Claude Code "does not provide native drift detection, robust multi-agent coordination, or guaranteed spec compliance" for spec-driven work. — [augmentcode guide](https://www.augmentcode.com/guides/claude-code-spec-driven-development) (vendor source, competing product)

### Inferences
- Snow already has Grafana MCP. Pointing Claude Code's OTel at a local Grafana/Prometheus/Loki stack (claude-code-otel or similar) would give a per-session timeline of tools, edits and cost at $0. Turn on `OTEL_LOG_TOOL_DETAILS=1` locally to see which files and commands each session touched.
- A statusline (ccstatusline or Claude HUD) is the cheapest visibility win: it shows context use and running agents at a glance.
- Backlog.md fits the existing pattern of `docs/gdd/QUESTIONS.md` and QUESTS.md: tasks as markdown in git, readable by both human and agent. ClickUp MCP duplicates that in SaaS, so pick one source of truth.
- Langfuse and Helicone are generic LLM-tracing tools for API apps. For Claude Code itself, native OTel is the supported path.

### Gaps
- Exact current versions, maintenance status and Windows support of ccusage, Claude-Code-Usage-Monitor, claude-code-log and Vibeyard were not verified.
- Langfuse/Helicone integration with Claude Code (via OTel) was not researched. No source found.
- Claude Task Master details (pricing, API-key needs, PRD parsing) were not fetched.

## 3. Multi-agent orchestration tools — when they help a solo dev vs add chaos

### Takeaway
The category is churning: Vibe Kanban's company shut down in April 2026, and the project is now community-maintained. Most tools wrap git worktrees plus a diff-review UI, and Claude Code now has native background agents, agent teams and worktrees. For a solo Unity developer, parallel agents conflict over one Unity Editor instance, one Library folder and binary scenes, so third-party orchestrators are mostly useful for text and code-only tasks.

### Cited Findings
- Vibe Kanban is an open-source board that orchestrates Claude Code, Codex, Gemini CLI and OpenCode in isolated git worktrees, with diff review, inline feedback, preview and PR creation. "The original company shut down in April 2026, while the repository continues as a community-maintained project." — [aiidelist Vibe Kanban](https://aiidelist.com/ide/vibe-kanban)
- Conductor is a Mac desktop app with a clean diff-review view that uses worktrees. Claude Squad is terminal-only. Crystal was named but not described. — [agentsroom.dev comparison](https://agentsroom.dev/zh/blog/best-multi-agent-coding-tools) (vendor of a competing product, so possibly biased)
- Other orchestrators listed in awesome-claude-code: Nimbalyst (visual workspace with session and task kanban, worktrees and visual diffs), Fusion Harness (2–5 models as architect and builders with "gate-first validation"), Repowire (tmux mesh between agents), and Agents Can Communicate (cross-session question and review exchange). — [awesome-claude-code](https://github.com/hesreallyhim/awesome-claude-code)
- Native options: background agents (agent view), subagents and agent teams with a lead agent, dynamic workflows, and the Agent SDK for fully custom orchestration with control over tools and permissions. — [Claude Code overview](https://code.claude.com/docs/en/overview)
- "Claude Code Game Studios" is a framework with 49 agents and 72 slash commands, claimed to cut indie game design time. The claim is unverified. — [besthub.dev](https://www.besthub.dev/tags/engine-agnostic)

### Inferences
- Solo dev on Windows: Conductor is Mac-only per the source, so it is out. Claude Squad needs tmux, which means WSL on Windows. Native background agents and worktrees avoid both dependencies.
- Parallelism helps when tasks are independent and text-based: research, docs, GDD question lists, C# runtime systems with no scene edits. It adds chaos for Unity scene and prefab work, because one editor and MCP bridge is shared, worktrees need separate Unity Library imports (slow, large), and YAML scene merges are painful. The memory note "ask before taking shared editor" already reflects this.
- A 49-agent framework is likely to amplify the "hard to follow" pain. A small number of named subagents (reviewer, QA runner, doc writer) is easier to audit.

### Gaps
- Crystal's current status (possibly renamed or merged into Nimbalyst?) was not confirmed. No source found this session.
- claude-flow details (maturity, risks) were not fetched.
- No first-hand Windows reports were found for Claude Squad, Crystal or Vibe Kanban.

## 4. Game-dev MCP servers and agent tools beyond Unity

### Takeaway
Blender MCP (ahujasid) is the de-facto standard and now supports AI 3D generation backends (Hunyuan3D, Hyper3D Rodin), and the developer already runs a Blender MCP with `generate_3d`. Tripo has an MCP server with text/image-to-3D, rigging and animation. Godot and Unreal have community MCPs, with one vendor claiming 523 Unreal tools. All of these are community projects of varying maturity, and MCP security incidents in 2025–26 make vetting mandatory.

### Cited Findings
- Blender MCP (ahujasid): connects Blender to Claude for scene inspection, object creation and modification, materials, Python execution inside Blender, and Poly Haven assets. It has added Hunyuan3D and Hyper3D Rodin generation. Users report the first connection sometimes fails. — [blender-mcp GitHub](https://github.com/ahujasid/blender-mcp), [glama listing](https://glama.ai/mcp/servers/b94nj6bzgy)
- Unity MCP (CoplayDev "MCP for Unity"): community-maintained, "not affiliated with Unity Technologies". It manages assets, scenes and scripts, installs via Git URL or OpenUPM (`com.coplaydev.unity-mcp`) plus a Python server via `uvx`, and runs an HTTP server on localhost:8080. A Docker image exists under a personal account, so verify the source before using it. — [glama CoplayDev/unity-mcp](https://glama.ai/mcp/servers/@CoplayDev/unity-mcp)
- Tripo AI MCP server: text-to-3D, image-to-3D, rigging, animation, stylization and format conversion, with direct .glb and .tscn import into Godot. — [glama Tripo MCP](https://glama.ai/mcp/servers/dnvrfqhryr)
- Godot-MCP (Dokujaa): Claude controls the Godot editor, with optional Meshy API integration for generated meshes. — [mcpservers.org Godot-MCP](https://mcpservers.org/servers/Dokujaa/Godot-MCP)
- StraySpark advertises an Unreal MCP server with "523 built-in editor tools (v5.1 beta, UE 5.8, Win64)" and a Blender→Unreal pipeline using Meshy and Tripo. This is a vendor claim. — [strayspark.studio](https://www.strayspark.studio/blog/tag/blender?page=10)
- Asset gap: "none of the usual Claude setups alone produce sprite sheets, music, 3D meshes, or a packaged playable game". The source sells an asset tool, so the claim may be biased. — [sorceress.games](https://sorceress.games/blog/claude-vibe-coding-for-games-you-need-more-than-code)
- MCP security:
  - Tool poisoning, where hidden instructions in tool descriptions or outputs steer the model (example: an "add" tool told to read SSH keys).
  - The GitHub MCP prompt-injection leak in May 2025.
  - MCP Inspector RCE (CVE-2025-49596).
  - Cursor MCPoison (CVE-2025-54136), where an approved config was altered without re-approval.
  - One blog reports 30+ MCP CVEs in early 2026, which is unverified.
  - Defences: mcp-scan (Invariant Labs) and the OWASP MCP Top 10.
  - Sources: [practical-devsecops](https://www.practical-devsecops.com/mcp-security-vulnerabilities/), [heyuan110 (unverified CVE count)](https://heyuan110.com/posts/ai/2026-03-10-mcp-security-2026), [mcpplaygroundonline](https://mcpplaygroundonline.com/blog/mcp-security-tool-poisoning-owasp-top-10-mcp-scan.md), [workos](https://workos.com/blog/mcp-security-risks-best-practices)

### Inferences
- Snow already covers Unity (official Unity AI MCP tools are visible in the environment: `Unity_*` incl. Camera_Capture, Profiler, RunCommand), Blender (with generate_3d), Chrome, Grafana, ClickUp and context7. The marginal gains are probably:
  - a **Git/GitHub MCP or `gh` CLI** (gh is already the Claude Code default);
  - a **Sentry MCP** for crash and exception reports from playtest builds, once builds go out;
  - **Tripo MCP** if Blender's built-in generators are insufficient (it adds rigging and animation).
- Every MCP server is code running with the user's privileges, and its tool descriptions go straight into the model context. Run `mcp-scan` on the configured servers, pin versions, and prefer servers with an official vendor or a high-activity repo. The Unity-related Docker image under a personal account is an example of what to avoid.
- Figma MCP is low value for a horror game without a UI designer.

### Gaps
- Meshy official MCP, Hunyuan3D standalone MCP, TRELLIS MCP and the Figma Dev Mode MCP were not found or verified this session.
- The official Unity MCP / Unity AI Assistant (vs CoplayDev) status and pricing in Unity 6 were not researched here.
- Sentry MCP capabilities and pricing were not fetched.

## 5. Practices from developers shipping games with AI agents; tools for understanding changes

### Takeaway
No credible write-up was found of a shipped commercial game built with a Claude Code agent workflow. Available material is tutorials and vendor content. The transferable practices are generic but consistent:
- a plan, run, verify loop;
- spec or markdown-first tasks;
- hooks as hard gates;
- independent review: a local `/code-review` or reviewer subagent, or CodeRabbit;
- symbol-level code navigation with Serena to save tokens on large repos.

### Cited Findings
- Solo-dev game loop: brief Claude Code, accept diffs, run the build. The loop "stops at art and audio". — [sorceress.games](https://sorceress.games/blog/use-claude-code-for-vibe-coding-game-dev-test) (vendor)
- Godot agentic workflows: GDScript generation, physics debugging, and automated playtests from the terminal (tutorial). — [aiforanything.io](https://aiforanything.io/blog/claude-code-for-godot-game-development-tutorial-2026)
- General plan, run, verify loop write-up (not games). — [okhlopkov.com](https://okhlopkov.com/claude-code-workflow-2026/)
- A claimed "62% of studios use AI agents" figure from a 2026 Unity report could not be verified. — [kevurugames](https://kevurugames.com/blog/using-claude-ai-in-game-development/)
- Serena: a free, open-source MCP toolkit using language servers for symbol-level retrieval and editing, added with one `claude mcp add`/`uvx` command. Token savings are smaller on small projects. — [archestra Serena](https://archestra.ai/mcp-catalog/oraios__serena), [zenn.dev](https://zenn.dev/sc30gsw/articles/ff81891959aaef)
- CodeRabbit: AI PR review on GitHub. A community MCP lets Claude Code fetch CodeRabbit comments and trigger reviews, and needs the CodeRabbit GitHub App plus a token. — [coderabbit-mcp-integration](https://github.com/hongkongkiwi/coderabbit-mcp-integration), [DEV/CodeRabbit](https://dev.to/coderabbitai/how-to-deploy-and-integrate-mcp-servers-with-coderabbit-1lkb)
- Claude's own review options for a non-Team plan: `/code-review`, Ultrareview, GitHub Actions with an API key, or a custom reviewer subagent. — [Code Review docs](https://code.claude.com/docs/en/code-review)
- Spec-driven development: write specs, plans and tasks before code. Spec Kit adds templates and a project "constitution". — [GitHub blog](https://github.blog/2025-09-02-spec-driven-development-with-ai-get-started-with-a-new-open-source-toolkit/)
- Tool to turn transcripts into markdown notes (vir) and an inline doc-review tool (MDXG Redline). — [awesome-claude-code](https://github.com/hesreallyhim/awesome-claude-code)

### Inferences
- **"Agent verifies with screenshots"** is directly supported by the tools Snow already has: Unity `Camera_Capture`, `SceneView_Capture*`, Blender `look` and Chrome screenshots. A Stop or TaskCompleted `agent` hook can require a capture file in the session folder before "done". This makes verification evidence-based rather than claimed.
- **Devlog auto-generation from git**: a Desktop scheduled task or SessionEnd hook can run `git log --since` plus `claude -p` to draft a devlog entry or changelog into docs/ (cost is subscription usage only). The humanizer skill already exists for the polish pass.
- **Review gate for a solo dev with no Team plan**: run `/code-review` (local) or a reviewer subagent on each feature branch before merge, or GitHub Actions with an API key on PRs (that costs API tokens). CodeRabbit is free for public repos per general knowledge, not verified here. Snow's repos are private.
- **Codebase map**: Serena is the main free option for C# symbol navigation. Its benefit grows with repo size, and Snow's Runtime has many systems, so it is likely worth a trial. Repomix (pack a repo into one file) is better for handing context to external chats like the claude.ai Blender agent than for in-session use.
- Context loss between sessions is already partially solved by memory files and the "обнови доки" protocol. Hooks (SessionEnd/PreCompact writing a summary) would make it automatic rather than prompt-driven.

### Gaps
- No verified case study of a shipped game built mainly by Claude Code agents was found. This is a real evidence gap, not just a search miss.
- Repomix, Greptile and architecture-diagram generators (e.g. Mermaid from code) were not researched for current status or pricing.
- CodeRabbit pricing for private repos in 2026 was not verified.
- Hacker News and Reddit threads were not reached within the tool budget, so "experienced user" opinions here come from blogs only.

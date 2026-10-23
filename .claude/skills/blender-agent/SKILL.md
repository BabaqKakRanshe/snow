---
name: blender-agent
description: Order 3D models (props, buildings, vehicles, figures, kits) for Snow from the user's dedicated claude.ai chat "3D модель Blender для Unity", which owns the game's visual style and Blender pipeline. Use when the game needs a new or reworked model, a style-consistent asset, or questions about how an existing model was built - "нужна модель", "закажи модель", "спроси у агента Blender".
---

# Blender agent (claude.ai chat)

The user's claude.ai chat **"3D модель Blender для Unity"** builds all Snow models in Blender
(via Blender MCP on this PC) and knows the visual style. It is NOT a Claude Code session:
`ListAgents` / `SendMessage` cannot reach it. Talk to it through Chrome (Claude in Chrome).

- Chat URL: https://claude.ai/chat/2342600e-e89a-4bf6-a001-1065a63a8e88
- Org (for API reads): `7fb5a493-43e8-48d2-87dc-c027284c500b`
- It delivers packages to `source/assets/<pack>/`: `<pack>.blend`, `tex/`, `*_CONTRACT.md` + `*.json` contract, sheets `*_A.jpg ...`, `src/`, zip next to the folder. Read existing contracts (e.g. `source/assets/forest_items/ITEMS_CONTRACT.md`) to match the format you ask for.

## House style (for briefs and for Codex images that must match)
"Стиль генераторной": pixel-art texture atlases, up to 51.3 px/m texel density (items 64 px/m),
64 colors per atlas (items 128), AO baked into color, 4x4 dithering, wear painted by rule
(dust and pine needles on top, dirt in corners, splashes and moss near ground, streaks, rust at fasteners).
Signs: German first, English below; crate stencils English. FBX Y-up, Unity prefabs, `IX_` prefix for
movable parts with pivot/axis/travel, `COL_` for colliders. Ask the agent itself for anything finer.

## Sending a request

1. **Ask the user first** in this chat: show the exact message text. Send only after a yes (it is posted under the user's account).
2. Check the chat is idle: don't post while it is mid-task (a long run shows "Выполнение команды ..." with a spinner). If busy, wait or tell the user.
3. Chrome: `tabs_context_mcp {createIfEmpty:true}` -> navigate to the chat URL -> wait for load -> `find "reply composer"` (placeholder "Ответить") -> click -> `type` the message -> `key Enter`. Screenshot to confirm it was sent. The page is heavy; if JS calls time out, use clicks/typing, not `javascript_tool`.
4. Message template (Russian, self-contained, it doesn't know our session):

```
[Запрос от Claude Code (проект Snow, F:\ClaudeGames\snow) по поручению пользователя]
Нужно: <что за модель, для какой сцены/механики, сколько вариантов>
Контекст: <где стоит на карте / с чем стыкуется / ссылки на файлы проекта>
Требования: <габариты, подвижные части IX_ (оси, ход), точки привязки, лимит треугольников, материалы/атлас>
Стиль: как остальные пакеты (стиль генераторной).
Сдача: пакет в F:\ClaudeGames\snow\source\assets\<pack>\ с <PACK>_CONTRACT.md и json-контрактом, как в forest_items.
Когда всё готово — последним шагом создай файл F:\ClaudeGames\snow\source\assets\<pack>\DONE.txt со списком файлов.
```

## Context of a place (rework models where they stand)

When the request is about models on the map (rework, restyle, fit to neighbours), first export the place with the
Blender Context tool (`tools/blender_context/README.md`): `ue.run_long('return Snow.EditorTools.BlenderContextExport.Export("<area>", true);')`
for a preset in `tools/blender_context/areas.json` (add one if missing), or `ExportAroundSelection(size, true)`.
It writes `source/context/<area>/context.blend` (every object where Unity has it, Empty per object with
`unity_path`/`prefab`/`fbx`/`sources` properties, terrain with paint, trees, lamps), `README.md` for the agent and
`shots/`. In the message add: `Контекст места: F:\ClaudeGames\snow\source\context\<area>\ (открой context.blend, README.md там же)`.
Export again before each request: the folder is regenerated, never edited by hand.

## Waiting for the result

Don't poll the chat page. Wait on the filesystem with Monitor (until-loop on `source/assets/<pack>/DONE.txt`, check every ~60 s), or check when the user says it's done. Then read the contract, view sheets with Read, and integrate per the contract.

If you need its text answer (a question, not a package), read the last messages through the API from a light claude.ai page (e.g. navigate to `https://claude.ai/api/organizations`), kicking the fetch off in the background because the response is ~1.5 MB:

```js
window.__st='start'; (async()=>{ const r=await fetch('/api/organizations/7fb5a493-43e8-48d2-87dc-c027284c500b/chat_conversations/2342600e-e89a-4bf6-a001-1065a63a8e88?tree=False&rendering_mode=raw'); const j=await r.json(); window.__last=j.chat_messages.slice(-2).map(m=>m.sender+': '+(Array.isArray(m.content)?m.content.filter(c=>c.type==='text').map(c=>c.text).join('\n'):m.text)); window.__st='done'; })(); 'kicked'
```

Then poll `window.__st` and read `window.__last` (tool output truncates around ~1.3k chars - slice it).

Close the Chrome tabs you opened when done.

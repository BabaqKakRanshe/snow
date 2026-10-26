---
name: snow-pipeline
description: The user's standard 6-step pipeline for new Snow content — in-game frames → references → GPT Image (Codex) → Blender models → Terrain and the player's path → build in Unity. Use when the user asks for a new or reworked location / place / yard / building on the map, a new monster, or a new event or scene — "нужна новая локация", "переделать место", "новый монстр", "новое событие", "как обычно", "по пайплайну".
---

# Snow pipeline: frames → refs → image → models → terrain → build

One pipeline, three tracks (location, monster, event). Same 6 steps, different output at each step.
Diagram: `pipeline.html` next to this file. Write to the user short and plain (see memory "Plain text").

## Topic folder (one per job)

`docs/gdd/<kind>/<id>/` — kind = `locations`, `monsters` (already exists), `events`. Inside:
`frames/` (step 1), `refs/` + `RESEARCH.md` + `QUESTIONS.md` (step 2), `concept_vN/` + `ingame/` (step 3),
`model/` (step 4 sheets), `README.md` — what it is, the 6 steps as a checklist with links, user decisions.
Prompts for every image lie next to the image as `*_prompt_*.txt`. After-shots go to `docs/dev/Audit/<id>_<date>/` as usual.

## Steps

**1. Frames of the game.** Shots of the real place now, at the times it will be seen (day / night / storm).
`tools/snow_qa/shots.py` with points in `configs/*_points.json` (add a group for the topic), or `game.py screenshot`.
Take the approach too: from the road, from the trail, from where the player first sees it. 5–10 frames, one sheet (`sheet.py`).
Monster: the night places where it shows up. Event: the spot and the view of the player who sees it.

**2. References.** Web research: games that did it well, real places (Bavaria / Austria for Forest), practices.
Write `RESEARCH.md` short: what to take, what not. Then `QUESTIONS.md` — numbered, simple → hard, decided items as
"(сейчас: …)", my ideas as [предложение] (memory "Design by questions"). User refs go to `refs/`.
**Stop: the user answers the questions.**

**3. GPT Image.** Skill `codex-imagegen`. Edit the step-1 frames (`--edit -r frames/<x>.png`), not blank pictures:
the target is "this same frame, but with the new thing", same PSX look, same light. 2–4 variants per frame.
Location: target picture from the approach + 1–2 close views. Monster: concept series → model sheets
(turnaround, head, poses) → `ingame/` mockups in real frames. Event: storyboard of 3–4 frames, what each player sees.
Look at every image with Read; note Codex mistakes (broken text, wrong proportions) in README.
**Stop: the user picks a picture (or "смесь 2+4").**

**4. Models in Blender.** List what is new vs what existing kits already cover — reuse first.
Small props / variants → skill `snow-model` (local). Big or hero models (buildings, vehicles, creatures) → skill
`blender-agent` (claude.ai chat; show the message to the user before sending). Reworks of models on the map:
export the place first (Blender Context tool, `tools/blender_context/`). Give the chosen step-3 picture as the target.
Delivery is a contract package in `source/assets/<pack>/` + `docs/gdd/<PACK>_CONTRACT.md`.

**5. Terrain and the player's path.** Before placing anything, write in README a short path, one line each:
- **how they get there** — road, trail, ice; by van or on foot; what leads them (sign, light, sound, tracks);
- **first sight** — from where it is first seen and what is in that frame (it must match the step-3 target);
- **arrival** — where the van stops, where they walk in, where they wait;
- **what they meet** — items, notes, tasks, events, monster spots, danger at night;
- **way out** — back or onward, and how it looks at night.
Then change the ground: height, paint, trees removed / added, trail. Do it as DATA + an idempotent step
(pattern: `Snow/Levels/Forest/Forest*Dress.json` + `Snow/Editor/Forest*DressStep.cs`, ground originals saved to
`*_ground.bytes` / `*_paint.bytes`). Only this part of the map; nothing else may change. Never hand-edit the scene.
Monster: routes, hiding spots, where it may not go (rules in `MONSTERS.md` + its README). Event: the trigger zone and
condition — a row in `EVENTS.md` (условие → эффект → кто видит → повтор → фаза).

**6. Build the location.** Install the package (`tools/<pack>/export_fbx.py`, `make_unity_data.py`, Build Props menu),
place props through the Dress JSON, lamps through their data. Checks: lean list from `tools/snow_qa/README.md`
(memory "Lean checks"), after-shots from the SAME step-1 points, one sheet: before / target picture / after.
Say what is still off vs the target. Update `docs/dev/ImplementationStatus.md` and the topic README checklist.

## Rules
- Do the steps in order; skip a step only if the user says so, and say which one you skipped.
- Two stops are fixed: after step 2 (answers) and after step 3 (picture). Other questions go in one batch.
- Do only the asked topic; don't redo neighbours (memory "No scope creep").
- Images only through Codex; models only through `snow-model` / `blender-agent`.

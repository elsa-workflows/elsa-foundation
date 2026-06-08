# Program Goals

Program goals are mid-term buckets of related short-term objectives. They are coordination surfaces for architects, engineers, and agents; they are not quality gates, glossary definitions, generated facts, or task-runner instructions.

Use this directory to see which larger efforts are active, who is stewarding them, what work is currently in scope, and where the supporting reports/specs/branches live. Keep one file per goal bucket to reduce merge conflicts.

## Program Goal State

Before substantial planning, "what next" ranking, roadmap/drift review, or multi-session handoff, identify the current program goal state when it is unclear.

Valid states:

- A named program-goal bucket from this directory.
- `none/free-flow`: no active program goal; the user is exploring, developing, researching, or planning without a mid-term coordination bucket.
- `unknown/not-assessed`: temporary state before the agent has enough context to decide whether a named bucket or `none/free-flow` applies.

Do not invent a named program-goal bucket just because one is missing. Propose creating or selecting a bucket only when the work is forming a mid-term coordination surface that would help future agents, architects, or engineers.

## Registry

| Goal | Status | Area | Steward(s) | Current focus |
|---|---|---|---|---|
| [Elsa Brain Operating Model](elsa-brain-operating-model.md) | Active | Repository operating model / AI workspace | Joey plus active architects/agents | Rebalance the broader Elsa-brain surfaces before returning to CShells generator-specific work |

## Goal File Rules

- Keep program-goal files concise and amendable.
- Record active objectives and roadmap notes here instead of in `AGENTS.md`.
- Link to reports, specs, maps, branches, and PRs instead of copying their content.
- If a goal becomes a ratified gate, move the gate to the constitution and leave a link here.
- If a goal becomes a repeatable workflow, move the workflow to `docs/skills/catalog.md` and leave a link here.
- If a goal is completed, paused, or superseded, update its status rather than deleting history immediately.

## Suggested Goal File Shape

- Status
- Area
- Steward(s)
- Purpose
- In scope
- Out of scope
- Active objectives
- Linked surfaces
- Current roadmap notes
- Drift / review notes
- Removal or completion conditions

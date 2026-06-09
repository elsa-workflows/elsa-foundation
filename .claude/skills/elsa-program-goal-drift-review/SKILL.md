---
name: "elsa-program-goal-drift-review"
description: "Review whether Elsa-brain work is drifting from the selected work tracking model or current program goal state. Use when a user asks whether work is drifting, a thread reaches a third consecutive related work unit, recent work keeps deepening one local area, or a proposed task would change or split a program-goal bucket."
argument-hint: "Proposed objective or recent work context"
compatibility: "Requires elsa-foundation docs and AGENTS.md"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#program-goal-drift-review"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `AGENTS.md`, especially source-of-truth layers and the work tracking / drift guard.
2. Read `docs/skills/catalog.md#program-goal-drift-review`.
3. Read `.agent-prefs/work-tracking-model.md` when it exists; use `docs/reference/agent-preferences.md#work-tracking-models` when durable planning needs a missing model choice.
4. Read `.agent-prefs/program-goal-selection.md` when the current program goal state is unclear; use `docs/reference/agent-preferences.md#program-goal-selection-models` when that model is missing and the review needs an explicit state.
5. Inspect `docs/program-goals/` only when the selected tracking model or user request involves program-goal buckets.
6. Identify the current program goal state, recent short-term objectives, and the next proposed objective.
7. Return a concise alignment note: continue in the current bucket, continue as `none/free-flow`, redirect to a more relevant bucket, or update/split/create a program goal.

Do not perform this review as a ritual at every fresh session start. If a report finding becomes planned work, route it through the selected work tracking model before implementation.

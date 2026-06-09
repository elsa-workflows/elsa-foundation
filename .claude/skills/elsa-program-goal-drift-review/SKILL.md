---
name: "elsa-program-goal-drift-review"
description: "Review whether Elsa-brain work is drifting from the shared program-goal planner or current program-goal state. Use when a user asks whether work is drifting, a thread reaches a third consecutive related work unit, recent work keeps deepening one local area, or a proposed task would change or split a program-goal bucket."
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

1. Read `AGENTS.md`, especially source-of-truth layers and the program goals / drift guard.
2. Read `docs/skills/catalog.md#program-goal-drift-review`.
3. Inspect `docs/program-goals/` and identify the current program-goal state: a named bucket, `none/free-flow`, or temporarily `unknown/not-assessed`.
4. Identify recent short-term objectives and the next proposed objective.
5. Return a concise alignment note: continue in the current bucket, continue as `none/free-flow`, redirect to a more relevant bucket, or update/split/create a program goal.

Do not perform this review as a ritual at every fresh session start. If a report finding becomes planned durable work, add or move it to the relevant program-goal bucket before implementation, or explicitly keep it `none/free-flow`.

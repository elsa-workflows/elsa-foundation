---
name: "elsa-whats-next"
description: "Find and rank the next Elsa-brain work unit from unfinished work, reports, specs, and source markers. Use when a user asks what is next, what is unfinished, what is unratified, what is weakly implemented, or which unit should be planned next in elsa-foundation."
argument-hint: "Optional focus area"
compatibility: "Requires elsa-foundation docs and AGENTS.md"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#whats-next--unfinished-work"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `AGENTS.md`, especially source-of-truth layers and the program goals / drift guard.
2. Read `docs/skills/catalog.md#whats-next--unfinished-work`.
3. Inspect `docs/program-goals/` to identify the current program-goal state when it affects ranking or is unclear: a named bucket, `none/free-flow`, or temporarily `unknown/not-assessed`.
4. Read `.agent-prefs/session-execution-model.md` if it exists.
5. Read `docs/reports/unfinished-work.md` as inventory/evidence, not an active queue.
6. Refresh context with the marker search from `unfinished-work.md` when the user asks for current state or when freshness is uncertain.
7. Rank candidates by current program-goal state when applicable, user intent, severity, and unblock value.
8. Return a concise drift check, top candidates, recommendation, source-of-truth layer, program-goal route, program-goal state when used, and execution route. If the recommended unit requires substantial planning or implementation and the local session preference favors fresh workers or ask-each-time, ask whether to plan/execute here or prepare a reviewed handoff prompt for a fresh agent/thread.

Do not implement from this skill. If the user approves a unit, hand off to `elsa-work-unit-planner` or the relevant implementation skill.

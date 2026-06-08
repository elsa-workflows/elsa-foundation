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

1. Read `AGENTS.md`, especially the program goal, source-of-truth layers, and zoom-out rule.
2. Read `docs/skills/catalog.md#whats-next--unfinished-work`.
3. Read `.agent-prefs/session-execution-model.md` if it exists.
4. Read `docs/reports/unfinished-work.md`.
5. Refresh context with the marker search from `unfinished-work.md` when the user asks for current state or when freshness is uncertain.
6. Rank candidates by Elsa-brain milestone advanced, not by nearby local cleanup.
7. Return a concise zoom-out check, top candidates, recommendation, source-of-truth layer, and execution route. If the recommended unit requires substantial planning or implementation and the local session preference favors fresh workers or ask-each-time, ask whether to plan/execute here or prepare a reviewed handoff prompt for a fresh agent/thread.

Do not implement from this skill. If the user approves a unit, hand off to `elsa-work-unit-planner` or the relevant implementation skill.

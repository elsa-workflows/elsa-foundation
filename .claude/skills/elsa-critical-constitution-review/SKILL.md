---
name: "elsa-critical-constitution-review"
description: "Critically review Elsa or framework constitution sections for enforceable gates, ambiguity, contradictions, missing exceptions, ratification readiness, and source-of-truth drift. Use when a user asks to question, revise, finalize, ratify, or challenge constitution/architecture rules."
argument-hint: "Constitution section or topic"
compatibility: "Requires .specify/memory constitutions and docs"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#critical-constitution-review"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#critical-constitution-review`.
2. Read the target constitution section in `.specify/memory/constitution-framework.md` or `.specify/memory/constitution.md`.
3. Read linked glossary/reference/report material only when needed to separate gate meaning from rationale/history.
4. Compare the target rule against current code reality and maps when the review asks whether it is enforceable.
5. Present findings first: ambiguity, contradiction, missing exception, non-gate material, stale draft history, or ratification blocker.
6. Propose a revision path or work unit. Do not silently rewrite constitutional meaning.

If the review produces deferred findings, update or propose an update to `docs/reports/unfinished-work.md`. If a finding becomes planned work, route it through the selected work tracking model before implementation.

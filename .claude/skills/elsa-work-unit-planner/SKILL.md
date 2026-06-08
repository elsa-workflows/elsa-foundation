---
name: "elsa-work-unit-planner"
description: "Turn Elsa-brain findings into an architecture-first or Speckit-ready work-unit plan. Use when a report finding, deferred decision, weak implementation, docs/maps/skills gap, or user-selected next unit needs scope, success criteria, source-of-truth layer, and exact files before implementation."
argument-hint: "Finding or unit to plan"
compatibility: "Requires elsa-foundation docs and AGENTS.md"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#work-unit-planner"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `AGENTS.md` for the zoom-out rule and source-of-truth layers.
2. Read `docs/skills/catalog.md#work-unit-planner` and `docs/skills/catalog.md#promote-finding-to-work-unit`.
3. Read the source report/spec/constitution section behind the finding.
4. Classify the unit as architecture development, feature development, codebase verification, docs/maps/skills work, or code.
5. Define goal, success criteria, in scope, out of scope, source-of-truth layer, affected gates/maps/docs, tests, and review points.
6. End with a `<proposed_plan>` block listing exact files to create or update, including likely follow-through obligations such as tests, extension-point catalogs, generated-map refreshes, or docs updates.

Do not run Speckit or edit code unless the user explicitly approves implementation. After a unit is approved, required follow-through obligations are part of completing that unit unless they introduce a new architecture decision.

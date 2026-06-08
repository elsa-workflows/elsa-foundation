---
name: "elsa-add-bridge-adapter"
description: "Plan or implement an Elsa bridge or adapter between seams, phase-owned contracts, or external/heavy dependencies. Use when code must connect two domains without making either own the other, isolate an external dependency, or translate design/runtime contracts."
argument-hint: "Bridge or adapter description"
compatibility: "Requires elsa-foundation seam/bridge docs and constitution gates"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#add-bridge-or-adapter"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#add-bridge-or-adapter`.
2. Read `docs/seams.md` and glossary entries for the relevant terms.
3. Identify both sides' `.Core` contracts, ownership boundaries, dependency direction, and allowed crossing point.
4. Keep the bridge/adapter outside the domains it connects unless an existing local pattern says otherwise.
5. Plan failure semantics, tests, docs, and extension-point catalog updates.
6. If this touches Workflows.Design/Runtime execution, read the runtime pre-spec handoff and avoid turning open architecture questions into code.

If the boundary is ambiguous, use `elsa-critical-constitution-review` or `elsa-work-unit-planner` before implementation. After the user approves the bridge/adapter plan, complete required tests, extension-point catalog updates, generated-map refreshes, and small docs follow-through as part of the approved work.

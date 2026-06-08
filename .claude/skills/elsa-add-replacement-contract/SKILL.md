---
name: "elsa-add-replacement-contract"
description: "Plan an Elsa replacement-contract implementation before coding. Use when exactly one implementation should be active per app/runtime context, when adding or replacing a single-implementation service, or when checking conflict detection for replacement contracts."
argument-hint: "Replacement contract scenario"
compatibility: "Requires elsa-foundation constitution, docs, and extension-point catalogs"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#add-replacement-contract-implementation"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#add-replacement-contract-implementation`.
2. Read the framework replacement-contract gate.
3. Identify the owning domain, contract, current implementations, and expected selection/conflict behavior.
4. Confirm the workflow is not a contribution flow and must not use contribution-style `IEnumerable<T>` semantics.
5. Plan registration, conflict detection or prevention, extension-point catalog updates, and tests.
6. Return a plan with exact files and open questions before implementation.

Do not implement until the user approves the plan. After approval, required tests, extension-point catalog updates, generated-map refreshes, and small docs follow-through are part of completing the approved work.

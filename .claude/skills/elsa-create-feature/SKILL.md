---
name: "elsa-create-feature"
description: "Plan or implement a new Elsa feature/module shape using the modular framework rules. Use when adding, porting, splitting, or scaffolding a feature/module, including package placement, dependency envelope, feature registration, tests, docs, and extension-point catalog updates."
argument-hint: "Feature/module description"
compatibility: "Requires elsa-foundation constitution, docs, and source layout"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#create-feature-or-module"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#create-feature-or-module`.
2. Read the relevant framework gates for three-layer separation, naming, feature identity, provider decomposition, and unit tests.
3. Identify owning domain, dependency envelope, `.Core`/helper/implementation shape, and whether Speckit planning is required first.
4. Plan feature registration tests, implementation tests, docs, and `EXTENSION_POINTS.md` updates before coding.
5. Preserve official Speckit flow for feature/work-unit development unless the user explicitly chooses a different path.

If the user has not explicitly approved implementation, stop with a plan and exact file list. After the user approves the feature/module plan, complete required tests, extension-point catalog updates, generated-map refreshes, and small docs follow-through as part of the approved work.

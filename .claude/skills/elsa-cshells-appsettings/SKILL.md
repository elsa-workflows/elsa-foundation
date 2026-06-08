---
name: "elsa-cshells-appsettings"
description: "Plan or generate CShells/Nuplane appsettings JSON from selected Elsa features. Use after feature composition inputs are known, or when evaluating blockers around feature identifiers, appsettings schema, dependencies, and optional features."
argument-hint: "Selected features or appsettings request"
compatibility: "Requires discoverable feature identifiers and appsettings conventions"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#cshells-appsettings-generator"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#cshells-appsettings-generator`.
2. Confirm selected features, required dependencies, optional features, and target shell context.
3. Verify that feature identifiers and appsettings schema conventions are discoverable.
4. If identifiers or schema are not stable, stop with blockers and propose the needed work unit.
5. If inputs are stable, generate JSON and explain how it was derived.

Do not guess feature IDs, appsettings keys, or configuration semantics.

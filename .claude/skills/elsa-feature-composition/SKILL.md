---
name: "elsa-feature-composition"
description: "Explore Elsa feature composition for an API, shell, or runnable feature set. Use when selecting features, dependencies, provider modules, or compatibility inputs before generating CShells/Nuplane appsettings."
argument-hint: "Desired API/shell capabilities"
compatibility: "Requires elsa-foundation maps and feature catalogs"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#feature-composition-explorer"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#feature-composition-explorer`.
2. Identify required capabilities and map them to projects/features using maps and extension-point catalogs.
3. Include required dependencies and flag optional provider choices separately.
4. Check external package/version compatibility signals using existing maps.
5. Produce a minimal proposed feature set and list unresolved identifiers or configuration questions.
6. Hand off to `elsa-cshells-appsettings` only after feature identifiers and appsettings inputs are clear.

Do not guess feature IDs.

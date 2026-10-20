---
name: "elsa-refresh-generated-maps"
description: "Get current generated Elsa maps by generating them locally or reading the generated-maps branch, and regenerate the committed dependency map when the project graph changed. Use when another skill needs current navigation facts, map inputs changed, or the dependency-map check is red."
argument-hint: "Map layer or freshness concern"
compatibility: "Requires the elsa-foundation map generator or access to the generated-maps branch"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#refresh-generated-maps"
user-invocable: true
disable-model-invocation: true
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#refresh-generated-maps` and `docs/contributing/generated-maps.md`.
2. The documentation maps are not committed to `main`. For the latest merge, read the `generated-maps` branch: browse, raw-URL and `git fetch origin generated-maps` routes are in `docs/maps/README.md`.
3. For the current working tree, generate locally with `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, or the narrowest layer that fits. The outputs are git-ignored; there is nothing to stage.
4. `-- check` guards only the committed `docs/maps/dependency-map.json`. When it is red or the project graph changed, run `-- dependency-map` and commit that file.
5. Review the generated findings reports before relying on a map; stop and tell the user if they show drift that makes the work unsafe.
6. Record findings in reports; do not hand-edit generated map facts.

---
name: "elsa-refresh-generated-maps"
description: "Get current generated Elsa maps and the dependency-map dataset by generating them locally or reading the generated-maps branch. Use when another skill needs current navigation facts, map inputs changed, or the generated-maps check is red."
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
4. The dependency-map dataset is not committed either; the versioning calculator derives it per commit. `-- check` guards the committed solution filters: when it is red, run `-- solution-filters` and commit them.
5. Review the generated findings reports before relying on a map; stop and tell the user if they show drift that makes the work unsafe.
6. Record findings in reports; do not hand-edit generated map facts.

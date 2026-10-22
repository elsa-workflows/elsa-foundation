---
name: "elsa-feature-dependency-map"
description: "Build or analyze Elsa feature, project, package, external NuGet, or dependency maps. Use when a user asks about feature dependencies, project references, external package versions, compatibility signals, dependency envelopes, or generated map/report updates."
argument-hint: "Map or dependency question"
compatibility: "Requires elsa-foundation source projects; generated maps are generated locally or read on the generated-maps branch"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#featuredependency-map-builder"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#featuredependency-map-builder`.
2. Inspect the generated maps before parsing source files directly: generate locally (`dotnet run --project tools/maps/Elsa.Maps.Generator -- all`) or read the `generated-maps` branch (see `docs/maps/README.md`).
3. For the current working tree rather than the latest merge, generate locally; see `elsa-refresh-generated-maps`.
4. Parse `.csproj` files for `ProjectReference` and `PackageReference` when needed.
5. Group findings by domain/feature and flag external package version clusters or dependency-envelope concerns.
6. Output point-in-time report findings; the dependency map and the documentation maps are generated or read from the `generated-maps` branch, never committed.

Do not make compatibility claims beyond the evidence available in maps/source.

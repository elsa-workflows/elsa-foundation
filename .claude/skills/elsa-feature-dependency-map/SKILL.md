---
name: "elsa-feature-dependency-map"
description: "Build or analyze Elsa feature, project, package, external NuGet, or dependency maps. Use when a user asks about feature dependencies, project references, external package versions, compatibility signals, dependency envelopes, or generated map/report updates."
argument-hint: "Map or dependency question"
compatibility: "Requires elsa-foundation source projects and docs/maps"
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
2. Inspect existing maps before parsing source files directly.
3. If freshness is uncertain, use `elsa-refresh-generated-maps` or propose a refresh.
4. Parse `.csproj` files for `ProjectReference` and `PackageReference` when needed.
5. Group findings by domain/feature and flag external package version clusters or dependency-envelope concerns.
6. Output either stable map updates or point-in-time report findings, depending on the task.

Do not make compatibility claims beyond the evidence available in maps/source.

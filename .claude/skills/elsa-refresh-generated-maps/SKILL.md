---
name: "elsa-refresh-generated-maps"
description: "Refresh or plan refreshes for generated Elsa maps. Use when map inputs changed, map freshness is uncertain, extension-point catalogs changed, project/package references changed, specs changed, or another skill needs current navigation facts."
argument-hint: "Map layer or freshness concern"
compatibility: "Requires elsa-foundation map scripts"
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

1. Read `docs/skills/catalog.md#refresh-generated-maps` and `AGENTS.md#refresh-generated-maps`.
2. Establish freshness with `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` when the user explicitly requests a refresh. That is the authoritative signal; `docs/maps/manifest.json` summarizes coverage but does not report staleness.
3. Choose the narrowest map generator that matches the changed inputs.
4. Prefer PowerShell scripts on Windows and Bash scripts where appropriate.
5. Run map generators only when the user explicitly requests a refresh (or explicitly authorizes it as part of an approved task).
6. Record findings in reports; do not hand-edit generated map facts.

If a map refresh is follow-through for an approved change, run it. Otherwise return the proposed refresh command(s) first.

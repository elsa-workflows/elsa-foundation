---
name: "elsa-extension-point-catalog"
description: "Plan and apply Elsa extension-point catalog updates. Use when a project exposes, implements, or changes events, contributor interfaces, replacement contracts, feature inheritance points, bridges, handlers, or other extension surfaces; also use implicitly after code changes that affect EXTENSION_POINTS.md or generated extension-point maps."
argument-hint: "Extension point change"
compatibility: "Requires elsa-foundation extension-point catalogs and docs/maps"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#add-extension-point-catalog-entry"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#add-extension-point-catalog-entry`.
2. Inspect the owning project's `EXTENSION_POINTS.md` and related entries in `docs/maps/extension-point-map.md`.
3. Classify each changed surface: event, contributor interface, replacement contract, feature inheritance point, bridge/adapter, handler, or other extension point.
4. Plan the catalog update, including intra-domain default vs cross-domain contribution labels where relevant.
5. Include generated map refresh in the plan when catalog inputs changed.
6. For tiny catalog-only follow-through after an approved code change, apply the catalog edit; otherwise return a plan first.

If a user explicitly asks to only plan, do not edit.

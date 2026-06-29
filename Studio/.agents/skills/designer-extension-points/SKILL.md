---
name: "designer-extension-points"
description: "Locate the right extension point in elsa-foundation-designer: where to plug in nav, routes, the module gate, API/data hooks, i18n, or shared UI. Use when a user asks 'where do I add/change X?' in the studio."
argument-hint: "What you want to plug in or change"
compatibility: "Requires elsa-foundation-designer docs"
metadata:
  author: "elsa-foundation-designer"
  source: "docs/skills/catalog.md#extension-point-catalog"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#extension-point-catalog`.
2. Read `docs/extension-points.md`.
3. Return the owning file plus the how-to for the asked-about extension point.
4. If nothing fits, recommend adding a new extension point and updating the
   catalog rather than a one-off hack.

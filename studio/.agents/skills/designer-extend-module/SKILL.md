---
name: "designer-extend-module"
description: "Extend an existing Elsa Studio (designer) module: add a sub-page, relabel, change icon/colour, add an owned route, wire new data, or change required/default. Use when modifying a module already registered in elsa-foundation-designer."
argument-hint: "What to change, and on which module"
compatibility: "Requires elsa-foundation-designer docs and module system"
metadata:
  author: "elsa-foundation-designer"
  source: "docs/skills/catalog.md#extend-module"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#extend-module`.
2. Read `docs/modules.md#extend-an-existing-module` and map the request to the
   smallest touchpoint in that table.
3. If the change involves ids / owned paths / required / default, edit **both**
   `features/<id>/module.ts` and `lib/modules/route-manifest.ts`. Visual-only
   changes (icon, tint, label, description) touch only `module.ts` (+ i18n).
4. For new data, add a hook in `lib/api/<domain>.ts` following the template in
   `docs/extension-points.md#resource-hook-shape-the-template`.
5. Run the `designer-verify` routine.

Do not refactor unrelated module wiring. Keep registry ↔ route-manifest aligned.

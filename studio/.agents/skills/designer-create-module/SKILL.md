---
name: "designer-create-module"
description: "Plan or scaffold a new Elsa Studio (designer) module: manifest, registry + route-manifest registration, routes, i18n, and the Settings toggle. Use when adding, porting, or scaffolding a studio module/feature in elsa-foundation-designer."
argument-hint: "Module description (name + what it does)"
compatibility: "Requires elsa-foundation-designer docs and module system"
metadata:
  author: "elsa-foundation-designer"
  source: "docs/skills/catalog.md#create-module"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#create-module`.
2. Read `docs/modules.md` — the `StudioModule` contract and the 5 touchpoints.
3. Pin down the module shape: `id` (kebab-case), `navGroup`, leaf-vs-parent nav,
   `ownedPaths`, `required`, `defaultEnabled`, and whether the first page is a
   real list page or a `ComingSoon` placeholder.
4. Produce the exact file list before editing:
   `features/<id>/module.ts`, `lib/modules/registry.ts`,
   `lib/modules/route-manifest.ts`, `app/(app)/<owned-path>/page.tsx`,
   `messages/en.json` + `messages/nl.json`.

If the user has not explicitly approved implementation, stop with the plan and
file list. After approval, implement all five touchpoints, keeping
`registry.ts` and `route-manifest.ts` in lock-step (the dev-only drift check
throws otherwise) and `route-manifest.ts` icon-free/React-free. Then run the
`designer-verify` routine and confirm the new nav entry and Settings toggle
behave.

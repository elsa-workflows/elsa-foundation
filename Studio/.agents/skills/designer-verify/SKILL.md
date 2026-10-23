---
name: "designer-verify"
description: "Verify a change to elsa-foundation-designer is sound: typecheck, lint, build (which catches module registry/route-manifest drift), and a runtime sanity check for module work. Use before handing off or pushing studio changes."
argument-hint: "Optional: what changed"
compatibility: "Requires pnpm and the elsa-foundation-designer toolchain"
metadata:
  author: "elsa-foundation-designer"
  source: "docs/skills/catalog.md#verify-codebase"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#verify-codebase`.
2. Run in order, reporting each result honestly:
   - `pnpm typecheck`
   - `pnpm lint`
   - `pnpm build` (catches the module drift check and route/SSR errors)
3. For module work, do a runtime sanity check with `pnpm dev`: the new sidebar
   entry appears and the Settings → Modules toggle hides/redirects the route.
4. Report a short status line per step; paste the failing output if any step
   fails. Never claim green when a step failed.

---
name: "designer-migrate-feature"
description: "Port a feature from the original Blazor Elsa Studio (elsa-workflows/elsa-studio) into a module in elsa-foundation-designer. Use when migrating, porting, or rebuilding an existing Elsa Studio feature here — behaviour parity, not MudBlazor markup."
argument-hint: "Which feature to port (and source path if known)"
compatibility: "Requires elsa-foundation-designer docs; source repo elsa-workflows/elsa-studio"
metadata:
  author: "elsa-foundation-designer"
  source: "docs/skills/catalog.md#migrate-feature"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/skills/catalog.md#migrate-feature`.
2. Read `docs/migrating.md` (the Blazor → module playbook) and
   `docs/modules.md` (the destination module shape).
3. Locate the source feature in `elsa-workflows/elsa-studio` and **read the
   actual source** before porting — do not assume API names. Inventory its nav,
   pages/routes, `Elsa.Api.Client` calls, dialogs, localized strings, and
   authorization.
4. Map each piece onto this repo using the table in `docs/migrating.md`, then
   build it as a module via the 5 touchpoints in `docs/modules.md`.
5. Reuse generated DTOs (`lib/api/generated/elsa.d.ts`; `pnpm gen:api` if an
   endpoint is missing) and the `elsa` client; rebuild UI with shadcn/ui, not
   MudBlazor markup.

Produce the port plan + file list first. If the user hasn't approved
implementation, stop there. After approval, implement, verify behaviour parity
against the source, run the `designer-verify` routine, and flag anything
deferred (e.g. permission checks).

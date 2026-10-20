# Implementation plan: HTTP progressive disclosure

**Branch**: `agent/codex/3064-http-configuration` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

Input: #3064, epic #3057, program #2559. Source base: `825aa98dd624295655576f7ed299f6146b991bc8`.

## Summary

Present ordinary HTTP request choices directly and move unset optional controls into the existing shared disclosure. Use the catalog's existing `tier` and `isRequired` metadata; promote only HTTP Connection's tier. Preserve authored inputs, method/body behavior, categories and unknown-metadata fallback. Configured values and current errors remain discoverable. No new endpoint, execution semantics, persisted authoring member or optional module is needed.

## Technical context

- Language/version: TypeScript 5.6, React 19; existing C#/.NET 10 projects (SDK 10.0.300).
- Dependencies: current Workflows module, Studio SDK, public `@elsa-workflows/studio-ui` disclosure kit, existing catalog input tier model. No new package.
- Storage: existing authored ActivityNode/ArgumentState only; disclosure state stays UI-local.
- Testing: current Vitest panel harness and SDK compatibility tests, Playwright/axe, xUnit scanner/catalog suites, rebuilt-server PowerShell HTTP E2E.
- Target: current Workbench browser composition; desktop/laptop/narrow inspector, Light/Dark, keyboard.
- Performance: retain current module bundle budgets and memoized row behavior; no timing benchmark or performance campaign.
- Constraints: one source writer, no dependency on unmerged #3062, no field-name heuristics, no method-driven content clearing or expression-engine redesign.
- Scope: generic descriptor presentation with HTTP as reference activity, one bounded PR.

## Constitution check

Both constitution Gate indexes and `docs/contributing/review-checklist.md` reviewed before research and rechecked after design. Pass: no new .NET project, DI/event mechanism, heavy/Core dependency, internals grant, runtime-to-design reference, persistence shape, feature identity or package-version override. The catalog remains the design source; its tier is already presentation metadata. Existing input state is preserved. Risky behavior has regression tests and baseline failure proof under the repository merge gate; no new test asserts Markdown.

The existing tier-only reconciliation exception in Elsa §E2.8 is provisional; this work consumes it without changing or ratifying that policy. Elsa §E2.9 remains provisional and gains no member. Framework §2.24 is draft and is not relied on as a ratified gate. No constitution exception is requested. No new .NET public API is planned; SDK fields are optional, additive declarations of JSON already emitted by the catalog.

## Project structure

Feature artifacts: `spec.md`, this plan, `research.md`, `data-model.md`, `contracts/inspector-disclosure.md`, `quickstart.md`, and subsequently `tasks.md`.

Source and test seams:

- `Studio/src/apps/Elsa.Studio.Web/Client/src/sdk/index.ts`: optional input metadata; existing `src/__tests__/sdk-typing-copies.test.ts` and Secrets/JavaScript/Liquid/Connections declaration copies.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx`: grouping, shared disclosure and row visibility feedback; small input-state helpers may live in `activityProperties.ts` if reused/testable.
- Existing Workflows `src/__tests__/activityPropertyGroups.test.tsx`, `activityProperties.test.ts`, `activityInputWire.test.ts`; reuse fixtures/setup.
- `Essentials/src/Activities/Http/Activities/SendHttpRequest.cs` and `Essentials/tests/Activities/Design/Tests/Unit/ClrAssemblyScannerTests.cs`: Connection tier and scanner proof.
- `Studio/tests/browser/`: sibling real-panel HTTP disclosure fixture/spec if the existing inspector fixture cannot express the full matrix without duplication.
- `Studio/docs/ux/design-spec.md`: module-author guidance. Shared disclosure source changes only if a concrete missing contract is demonstrated; no copy of #3062's unmerged change.

## Design and sequencing

1. Add optional `tier?: string | null` and `isRequired?: boolean` to the SDK and all current local declaration copies. Known tier values are lowercase essential/optional/setting; unknown strings remain representable and visible. Add Connections to existing copy parity coverage.
2. Preserve `groupInputs` category labels and explicit/delivery ordering. Direct inputs are required inputs and every tier except exact optional. Render direct groups, then one “More options” AdvancedSection containing optional groups. Preserve category labels/order inside each partition and avoid empty/duplicate headings. HTTP has one category and one opening action for four secondary controls.
3. Determine configured state from stored values, not `readWrappedInputValue`'s synthesized default. Honor opaque reference keys, expanded top-level inputs and any seeded wire entries. Explicitly stored defaults, bindings, conversions and unknown ArgumentState fields remain discoverable. Presence inspection must not mutate/canonicalize state. Conservative visibility is preferable to hiding ambiguous authored state.
4. Keep PropertyRows mounted in the shared hidden disclosure body, retaining hooks, editor state and retry controls. Existing setCount keeps configured sections open. Do not duplicate the unmerged allowCollapseWhenSet feature from #3062.
5. Bridge current host-owned row signals through a stable internal callback keyed by activity/input identity. Aggregate current severity-error diagnostics into hasError. A failed dynamic-options source forces open to expose its existing alert/Retry; keep it open through retry until settled. Initial loading/empty results, warning/info and unavailable/unauthorized tooling are not validation errors. Deduplicate parent updates, scope/filter to current rows, clean up removed rows and include callback in custom memo equality.
6. Contributed editors have no host-visible private-error reporting contract. Do not inspect their DOM or invent validation. Preserve existing custom-editor access; if a concrete existing contributed editor can hide a private error without stored state, keep that unsupported case directly visible as a conservative fallback rather than silently claiming coverage. Any necessary wider SDK reporting contract needs an explicit plan refinement before implementation.
7. Promote only SendHttpRequest.Connection to Essential; retain not-required, Content Essential and ContentType Optional. Runtime content remains method-independent and untouched.
8. Document implemented metadata/fallback behavior in existing UX guidance. Root validates actual inspector hierarchy and adjusts spacing/heading composition within this scope based on screenshots.

US1 establishes hierarchy and catalog/SDK metadata; US2 adds safe configured/error state and lossless journeys; US3 proves fallback/categories/requiredness. One worker owns overlapping panel files sequentially. Root owns final integration, source-composed browser proof and review.

## Verification and delivery

See [quickstart.md](quickstart.md) for executable commands and [interaction contract](contracts/inspector-disclosure.md) for the matrix. Root runs whole affected test projects, typecheck/build/budgets, lint/CSS/layout, red→green browser/panel proof, rebuilt-server HTTP E2E, architecture guard with discovery count reconciliation, maps check and diff review. Heavy local .NET checks stay serial under the build-slot wrapper; whole-solution validation is supplied by CI.

Before commit, recheck claims/open PRs. Open one App-authored PR fixing #3064, post evidence and request review. Independent non-author correctness/standards/acceptance reviews run without sharing their output. Merge under the current org gate only on the exact clean reviewed head, then observe resulting-main checks. Keep epic/project issue transitions synchronized. Final epic qualification #3075 is still separate.

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

The existing tier-only reconciliation exception in Elsa §E2.8 is provisional; this work consumes it without changing or ratifying that policy. Elsa §E2.9 remains provisional and gains no member. Framework §2.24 is draft and is not relied on as a ratified gate. No constitution exception is requested. No new .NET public API is planned. Catalog SDK fields optionally declare JSON already emitted; the refined property-editor reporting members are a separate additive client-only extension contract with conservative legacy fallback and no persistence/wire change.

## Project structure

Feature artifacts: `spec.md`, this plan, `research.md`, `data-model.md`, `contracts/inspector-disclosure.md`, `quickstart.md`, and subsequently `tasks.md`.

Source and test seams:

- `Studio/src/apps/Elsa.Studio.Web/Client/src/sdk/index.ts`: optional input metadata; existing `src/__tests__/sdk-typing-copies.test.ts` and Secrets/JavaScript/Liquid/Connections declaration copies.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx`: grouping, shared disclosure and row visibility feedback; small input-state helpers may live in `activityProperties.ts` if reused/testable.
- Existing Workflows `src/__tests__/activityPropertyGroups.test.tsx`, `activityProperties.test.ts`, `activityInputWire.test.ts`; reuse fixtures/setup.
- Refined local-validation seams: Web `src/app/propertyEditors.tsx` plus Workflows scalar/collection/dictionary editor rendering. Reuse existing collection/dictionary identities and lifecycle setup rather than adding an unrelated validation engine.
- `Essentials/src/Activities/Http/Activities/SendHttpRequest.cs` and `Essentials/tests/Activities/Design/Tests/Unit/ClrAssemblyScannerTests.cs`: Connection tier and scanner proof.
- `Studio/tests/browser/`: sibling real-panel HTTP disclosure fixture/spec if the existing inspector fixture cannot express the full matrix without duplication.
- `Studio/docs/ux/design-spec.md`: module-author guidance. Round 1 demonstrated the configured-collapse gap; reuse the identical opt-in shared contract already reviewed in #3062, without importing its Runs changes or depending on that PR merging.

## Design and sequencing

1. Add optional `tier?: string | null` and `isRequired?: boolean | null` to the SDK and all current local declaration copies. Known tier values are lowercase essential/optional/setting; unknown strings remain representable and visible. Add Connections to copy parity coverage. Following round-1 standards review, use isolated TypeScript consumer compilations for each declaration rather than asserting source formatting. Check exact nullable types and optionality, including legacy sensitivity/context and reporting members.
2. Preserve `groupInputs` category labels and explicit/delivery ordering. Direct inputs are required inputs and every tier except exact optional. Render direct groups, then one “More options” AdvancedSection containing optional groups. Preserve category labels/order inside each partition and avoid empty/duplicate headings. HTTP has one category and one opening action for four secondary controls.
3. Determine configured state from stored values, not `readWrappedInputValue`'s synthesized default. Honor opaque reference keys, expanded top-level inputs and any seeded wire entries. Explicitly stored defaults, bindings, conversions and unknown ArgumentState fields remain discoverable. Presence inspection must not mutate/canonicalize state. Conservative visibility is preferable to hiding ambiguous authored state.
4. Keep PropertyRows mounted in the shared hidden disclosure body, retaining hooks, editor state and retry controls. **Round-1 root refinement:** use the same additive `allowCollapseWhenSet` contract present in #3062's shared AdvancedSection, retaining the default forced-open behavior for other callers. The HTTP panel opts in and starts an already-configured section open. Authors may then collapse it with the configured badge remaining visible. Errors, unknown editor completeness and options failures still force visibility. This closes the reproduced acceptance gap without a second disclosure primitive or dependency on the held Runs PR.
5. Bridge current host-owned row signals through a stable internal callback keyed by activity/input identity. Aggregate current severity-error diagnostics into hasError. A failed dynamic-options source forces open to expose its existing alert/Retry; keep it open through retry until settled. Initial loading/empty results, warning/info and unavailable/unauthorized tooling are not validation errors. Deduplicate parent updates, scope/filter to current rows, clean up removed rows and include callback in custom memo equality.
6. **Root-approved refinement, 2026-10-20:** the built-in TimeSpan and Numeric editors reject invalid local text without changing the authored value. A stored-state count cannot expose those errors. Add optional `onValidationChange(hasError: boolean): void` to property-editor props and optional `supportsValidationReporting?: true` to the contribution. The capability promises that all local blocking errors are reported; editors without private validation may declare it without emitting errors. Omitted capability keeps the selected editor's optional row directly visible. Existing contributions remain source-compatible; no DOM inspection or input-name/ID allowlist is used. Copy the additive declarations to all current extension SDK copies.
   - Built-in duration/number editors report current invalid/recovered state; all other built-ins declare support only after their error surfaces are checked. Callback payloads contain no values or diagnostic text and never change authored state.
   - Aggregate reports per mounted editor instance, including collection/dictionary elements; one valid sibling must not clear another's error. Release only the removed/replaced instance. Scope callbacks to draft/activity-version/node/input identity and ignore stale callbacks after removal, editor replacement or scope changes. Include callback/capability semantics in memo behavior.
   - Keep optional rows using a selected inline expression editor directly visible. The inline editor and diagnostics provider are resolved independently, and the SDK makes no completeness promise for private component errors. Even a present provider or empty diagnostics result cannot authorize hiding that editor. Existing current-revision/severity error diagnostics remain positive error signals; do not add an expression SDK capability in this slice.
   - Keep the metadata partition and mounted row subtree stable. Conservative fallback may force the existing secondary section open without an Error pill; do not relocate an already-mounted editor between group parents and lose its private invalid text when capability/options state changes.
   - Prove local error/recovery, unknown contribution fallback, sibling aggregation/removal and activity/draft/editor switches. This is a bounded additive client extension contract required by FR-006, not a new validation engine or backend wire contract.
7. Promote only SendHttpRequest.Connection to Essential; retain not-required, Content Essential and ContentType Optional. Runtime content remains method-independent and untouched.
8. Document implemented metadata/fallback behavior in existing UX guidance. Root validates actual inspector hierarchy and adjusts spacing/heading composition within this scope based on screenshots.

US1 establishes hierarchy and catalog/SDK metadata; US2 adds safe configured/error state and lossless journeys; US3 proves fallback/categories/requiredness. One worker owns overlapping panel files sequentially. Root owns final integration, source-composed browser proof and review.

## Verification and delivery

See [quickstart.md](quickstart.md) for executable commands and [interaction contract](contracts/inspector-disclosure.md) for the matrix. Root runs whole affected test projects, typecheck/build/budgets, lint/CSS/layout, red→green browser/panel proof, rebuilt-server HTTP E2E, architecture guard with discovery count reconciliation, maps check and diff review. Heavy local .NET checks stay serial under the build-slot wrapper; whole-solution validation is supplied by CI.

Before commit, recheck claims/open PRs. Open one App-authored PR fixing #3064, post evidence and request review. Independent non-author correctness/standards/acceptance reviews run without sharing their output. Merge under the current org gate only on the exact clean reviewed head, then observe resulting-main checks. Keep epic/project issue transitions synchronized. Final epic qualification #3075 is still separate.

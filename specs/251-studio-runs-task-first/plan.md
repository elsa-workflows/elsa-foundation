# Implementation Plan: Task-first Runs triage in Studio

**Branch**: `agent/codex/3062-runs-golden-path` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: #3062 “Studio UX: Make Runs a task-first triage list with progressive filters”; see [spec.md](spec.md).

**Issue**: [#3062](https://github.com/elsa-workflows/elsa-foundation/issues/3062), sole active leaf of #3057; base `42226851b85385ea764658db8d816fcad8a3bea7`.

## Summary

Reshape the existing Runs list into a task-first triage surface while keeping its current server-side filter and URL-backed pagination model. Add independent Needs attention and Running controls, a Runs-local workflow-definition chooser, explicit advanced-filter Apply and truthful removable chips, human-name-first rows with copyable exact IDs, selection-only bulk actions, and safe list-context return from run details. Add one optional, default-off collapse behavior to the existing public Studio `AdvancedSection` so applied counts remain truthful while More filters can stay closed. No runtime/backend contract or dependency change is expected.

## Technical Context

**Language/Version**: TypeScript and React in the Studio pnpm workspace.

**Primary Dependencies**: Existing `@elsa-workflows/studio-workflows`, `@elsa-workflows/studio-ui`, and `@elsa-workflows/studio-sdk` packages; existing Vitest/Testing Library and Playwright. No new dependency.

**Storage**: No new storage. Applied list context remains URL-backed; draft advanced fields remain local UI state.

**Testing**: Focused Vitest coverage for disclosure, filter state, picker, row identity and return validation; Playwright browser journey against a deterministic mocked endpoint; Workflows/UI package typechecks and builds.

**Target Platform**: Elsa Studio browser client.

**Project Type**: Existing multi-package web workspace.

**Performance Goals**: N/A. Performance measurement is outside this program slice.

**Constraints**: Preserve incident-health meaning, lifecycle separation, server-side filtering before paging, health capability behavior, date semantics, existing permission/capability gates, keyboard inspection and deletion semantics. Use only supported public module boundaries. Do not add runtime name filtering, backend contracts, recovery actions, or a generalized list framework.

**Scale/Scope**: One Runs surface, one local definition-search component, one additive opt-in on the shared disclosure, focused tests/fixture, and the required Studio list-design convention update.

## Constitution Check

*Checked against framework constitution v6.1.0, Elsa constitution v6.0.2, their Gate indexes, and `docs/contributing/review-checklist.md`. Rechecked after the design below; no exception required.*

- **§2.6 / §2.23.4 — module boundaries and hidden coupling**: Keep the chooser in the Workflows client. Consume the shared disclosure through its supported public Studio UI entry; do not deep-import app-private UI files or reproduce their coupling.
- **§2.23.1 / §2.23.2 — meaningful tests**: Add behavior tests for filter composition, draft/apply state, identity selection, return validation, keyboard behavior, and disclosure forcing. Assert observable state and request effects, not prose alone. Preserve existing valid-behavior assertions.
- **§2.23.3 — public surface**: The only intended public-surface addition is an optional TypeScript prop on the existing shared `AdvancedSection`; its default preserves current behavior for all other consumers. Keep set-count, explicit force-open, and error behavior truthful. No C# public API, new NuGet package, or C# API-tracking file is anticipated.
- **§4.2 / Elsa §E5**: No C# package version line changes are expected. Verify Studio workspace package build/typecheck and consumer compilation for the shared UI prop.
- **Elsa §E2.2.3 / §E2.6**: No Design/Runtime deployment shape, runtime artifact, or runtime contract changes are in scope.
- **Automated architecture gates**: No new C# feature, project reference, persistence contract, or runtime API is planned, so no architecture-guard change is expected. The generated feature map catalogs C# shell features and does not describe Studio client packages; this slice does not add a shell feature.

**Result**: No constitution exception is proposed. Review the above at design and implementation review; do not treat draft §2.24 as an extra approval gate.

## Implementation Outline

1. **Shared disclosure behavior** — Add `allowCollapseWhenSet?: boolean` to the public `AdvancedSection` surface, defaulting to false. When opted in by Runs, a nonzero applied count does not force the section open; explicit force-open and error conditions still do. Continue showing the real applied count. Keep children mounted across collapse so draft fields persist. Do not export the lower-level disclosure primitive or use a false count.
2. **Runs filters and definition choice** — Keep primary controls independent and immediate. Add a small Workflows-local searchable chooser that uses the existing definition list/search and exact-identity lookup, then applies the selected definition identity. Typing alone does not alter the run query. Keep technical/date fields in More filters with explicit Apply; merge those drafts with the latest primary filters. Render chips only for applied criteria, remove one predicate at a time, clear cursor/selection on query changes, and preserve page size and unrelated drafts.
3. **Rows and bulk actions** — Make the human workflow name the leading identity. Keep execution, artifact, definition and version IDs visible and provide explicit copy actions. Preserve Enter/Space inspection, checkbox selection, accessible per-run actions, deletion semantics and existing capability/permission checks. Hide the bulk toolbar at zero selection.
4. **Detail round-trip** — Carry the complete current local Runs URL into detail navigation, validate it as a same-origin Runs-list destination, and return to it from the in-app Back action. A direct detail link without return context uses the default Runs list. Keep browser Back normal. Preserve the existing successful-empty-cursor recovery; a cursor request error remains an error.
5. **Shared convention** — Update `Studio/docs/ux/design-spec.md` with the convention established by this slice: primary task controls, collapsible advanced criteria, applied chips, selection actions, and list-context return.

## Project Structure

### Documentation (this feature)

```text
specs/251-studio-runs-task-first/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md
```

### Source Code (repository root)

```text
Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/disclosure/
├── AdvancedSection.tsx
└── __tests__/disclosure.test.tsx

Studio/src/essentials/Elsa.Studio.Workflows/Client/src/
├── api/workflowDesign.ts                         # existing search/identity reads
├── workflow-editor/WorkflowInstances.tsx          # list, filters, rows, selection, URL
├── workflow-editor/pages.tsx                      # run-detail route context
├── workflow-editor/definitionDisplayNames.ts      # existing identity helpers
└── __tests__/workflowRunHistory.test.tsx

Studio/tests/browser/
├── golden-runs.html
├── goldenRunsFixture.tsx
└── golden-runs.spec.ts

Studio/docs/ux/design-spec.md
```

**Structure Decision**: Keep all run-specific search/filter presentation in the existing Workflows client. Change only the existing shared disclosure component for the optional collapse behavior. Add no backend, persistence, feature-composition, or shared list framework project.

## Validation Plan

- In the Studio Web Client, run the focused disclosure test and the full Studio Web Client suite on the final candidate. The disclosure tests live in that app's Client; the `studio-ui` package's `test` script allows no tests and is not evidence for this behavior. Extend disclosure tests so the existing default force-open behavior remains, opt-in permits collapse with a truthful count, error and explicit force-open still win, and drafts survive collapse/reopen.
- Extend Runs tests for independent primary predicates, composition, advanced Apply/Reset/one-chip removal, preserving unrelated drafts, page-size retention and cursor/selection reset, identity resolution and missing-name fallback, safe return-path validation, unsupported health, and the existing successful-empty-page recovery.
- Add picker tests for duplicate names, exact selection, keyboard navigation, stale search responses, and loading/empty/error results.
- Add one focused browser journey with the prepared 14-run server-filter-before-page fixture: active blocking/nonblocking and healthy/resolved/suppressed controls, independent Running intersection, duplicate-name selection, advanced chip removal, explicit ID copying, selection-only bulk actions, and real detail-to-return restoration from page two. Observe loading, successful empty, unsupported, and request-error states without conflating them.
- Verify keyboard operation and long-name/ID presentation at 1280px and 1440px in light and dark themes. Capture focused screenshots for visual review.
- Run the focused disclosure test and full Studio Web Client suite, the affected Workflows Vitest suite, package typechecks/builds, relevant CSS/layout lint, and the focused Playwright spec.
- Run the repository-required Architecture guard suite and generated-map freshness check as delivery gates, even though this is a Studio-client change. Adding the numbered spec/status changes the generated spec-status map; refresh the affected generated map when stale and stage only changed generated paths (including the manifest if the generator changes it). Do not waive shared gates because no C# feature was added.

**Command references** (run each group from the indicated directory):
```sh
# From Studio/src/apps/Elsa.Studio.Web/Client
pnpm test -- src/app/ui/disclosure/__tests__/disclosure.test.tsx
pnpm test

# From Studio/
pnpm --filter @elsa-workflows/studio-ui build
pnpm --filter @elsa-workflows/studio-workflows test
pnpm --filter @elsa-workflows/studio-workflows typecheck
pnpm --filter @elsa-workflows/studio-workflows build
pnpm test:browser -- tests/browser/golden-runs.spec.ts
pnpm lint:css
pnpm lint:layout

# From repository root
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
# Run the required Architecture guard test suite per the repository gate.
```

## Complexity Tracking

No constitution violations or architecture exceptions are proposed.


## Cadence and ownership

This modifies the existing Runs surface. Use risk-based behavioral tests and the org-required revert/mutation proof for changed behavior; no application-wide TDD mandate is inferred. The derived constitution does not declare a greenfield cadence; this unit records that gap without inventing an exemption from tests. One serialized implementation worker owns the source patch; root reviews/integrates and owns final QA, GitHub state and gates. No parallel writers share the checkout.

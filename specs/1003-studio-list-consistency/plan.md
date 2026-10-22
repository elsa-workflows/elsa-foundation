# Implementation Plan: Studio Definition List Consistency

**Branch**: `agent/codex/3070-list-consistency` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)

**Input**: [Issue #3070](https://github.com/elsa-workflows/elsa-foundation/issues/3070), its accepted claim/scope checkpoint, and the current issue comments.

**Planning boundary**: Root approved this package and authorized source implementation in [issue comment 6343367959](https://github.com/elsa-workflows/elsa-foundation/issues/3070#issuecomment-6343367959). The assigned checkout starts at pinned frozen Home head `58a36c93f1395a0479868af19754aac2043be818`; preserve those changes. Root's fresh ownership/prerequisite/open-PR/base checks found no failed required base gate or conflicting ownership. Home Core and Chromium checks are still running; their pending state is not a pass or waiver. Root owns the separate serial integration lane and integrates qualified resulting main before this leaf's final PR qualification or merge. This plan authorizes the scoped source work, not runtime acceptance or final delivery gates.

## Summary

Align Workflow Definitions and Activity Definitions with the proven Runs presentation for page hierarchy, actions, compact identity, and truthful result states. Make the narrowly scoped Runs row-action and technical-identity refinement part of the same composition. Preserve each list's search, filter, lifecycle, authorization, selection, and paging contracts. Restore Activity Definition collection context through both editor Back and browser Back, with one context per history entry and the existing recovery for a stale continuation. Extract only presentation structure that the target consumers actually share; add no generic list framework, replacement grid, backend behavior, or public UI API.

## Planning Evidence and Technical Context

The GitHub issue was reread with all four pre-claim refinements, the winning claim, and the active scope note. #3062 and #3068 are accepted prerequisites. No new source work is claimed by those reads. The repository code graph was ready for the canonical indexed project but returned no Studio TSX symbols; source discovery then used the exact known files in this checkout. No code, tests, browser, host, or build were run for this planning slice.

Source findings at the planning head:

- `WorkflowDefinitions.tsx` already uses `WorkflowsPageFrame`, a URL/history-backed browse session, lifecycle tabs, folder/unfiled scope, live search, selection, a capability-gated Move action, and per-row Delete/Restore/Delete permanently menus. Keep these #3068 and lifecycle semantics.
- `ActivityDefinitionsPage.tsx` keeps search, provider, authority, page size, cursor stack, and cursor position inside the collection component. The component unmounts when the workbench opens. Its current row opens on click and Enter/Space. The key handler does not exclude nested controls, so new identity disclosures must be isolated from row activation and validated with a guard-removal mutation; absence of those new controls on the old base is not an existing interaction defect. Its 250 ms live search, Designed/Built-in/All authority, 10/25/50 page size, authorization-scoped opaque continuation, denied/unavailable cache redaction, confirmed-page refresh behavior, and expired-snapshot restart are existing contracts to preserve.
- `workflowDefinitionBrowseSession.ts` already responds to auth-session start/end and `context.baseUrl` changes by invalidating its cursor, request generation, and authority revision. This is a local pattern to study for new Activity history scopes; do not change the accepted workflow session. Activity Definitions currently keys definition queries as `['activity-design', 'definitions', request]` without a host/auth key. Per-entry history state alone is not an authorization proof, so restoration must use the existing local redaction seam and must not recommit previously displayed rows after an authority change. This slice does not redesign global query caching or authentication.
- The Activity Definitions collection currently has a custom page header and an ARIA table. Workflow Definitions also uses table semantics. Keep their meaningful row/column semantics and the Activity-specific draft/published summaries.
- Public `studio-ui` already provides `CopyableIdentifier`, `StudioButton`, `OverflowMenu`, `AdvancedSection`, and `TechnicalDetails`. Use those existing surfaces before proposing controls. `CopyableIdentifier` exposes the full value to assistive technology and copies the exact value even when its visual text is truncated.
- Runs already leads with workflow name and displays four copyable identifiers, but its dense identifier block increases row height. Its row Delete action also needs the themed presentation from the issue refinement. Keep Runs filtering, selection, details, permission checks, and query return behavior outside this change.

The behavior tests below are planned evidence, not reproductions. They have not been run on the selected base. Implement the existing Activity return-context fix first and record the actual base result for its focused test. The nested identity disclosures are new behavior: their initial absence may produce a feature RED, not proof of an existing row-event bug. After adding the disclosure and guard, prove the nested-action test's sensitivity by temporarily removing the guard, then restore it and rerun green.

Phase 0 research decisions are recorded in [research.md](research.md). The conceptual, non-persistent UI state and identity model is in [data-model.md](data-model.md), and the behavioral UI contract is in [contracts/ui-context-and-identity.md](contracts/ui-context-and-identity.md). [quickstart.md](quickstart.md) gives the future validation sequence. These documents make the plan's research, design, contract, and validation outputs explicit; they do not approve implementation or claim any gate has run.

**Language/Version**: Existing TypeScript / React packages in Studio; no language or runtime change.

**Primary Dependencies**: Existing `@elsa-workflows/studio-ui` public exports and existing Workflows client route/query helpers. No package or SDK dependency is added.

**Storage**: Existing route and browser-history state only. No persistence or backend schema change.

**Testing**: Existing Workflows client Vitest suites and Studio browser/Playwright coverage, supplemented by real Workbench/Studio acceptance. Targeted test files include `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitions.test.tsx`, `workflowDefinitionRoute.test.tsx`, `workflowDefinitionBrowseLocation.test.ts`, `workflowDefinitionSearch.test.tsx`, `workflowInstances.test.tsx`, and `workflowRunHistory.test.tsx`. The existing route-mocked browser fixtures are supplemental and do not substitute for normal-composition acceptance.

**Target Platform**: The real source-built `Elsa.Workbench` host with the normal `Studio.Web` shell, authorization, design APIs, and backing store, exercised at laptop and narrow browser widths.

**Project Type**: Existing Studio Workflows client and Studio Web shared UI package.

**Performance Goals**: Make no new performance claim. Preserve existing server-side live search timing, list page sizes, and cursor paging.

**Constraints**: No backend, global authentication, persisted projection, public SDK, dependency, optional-module composition, or unrelated-page change. No new button variant or generalized grid. Preserve the frozen Home source already present at the starting head.

**Scale/Scope**: Workflow Definitions, Activity Definitions, and the row-action/compact-identity portions of Runs only.

## Constitution Check

### Pre-design

- **Framework Gate index**: This work adds no domain, `.Core`, feature, contract, package, reference, persistence provider, or module registration. The relevant automated architecture gates for project references, provider dependencies, and feature composition have no proposed changes to inspect. The review gates that apply are framework §2.17 (avoid needless coupling), §2.21.1 (preserve valid existing behavior), §2.23.2 (test changed logic through behavior), and the accessibility, authorization, API-surface, and scope checks in `docs/contributing/review-checklist.md`.
- **Test cadence**: The derived constitution does not declare a general TDD cadence for this UI work; framework §2.21.2 leaves greenfield cadence to the application. This plan does not infer an application-wide TDD rule. Run the existing Activity history-return test against the approved base and report its actual result. Treat identity disclosures as new feature behavior; verify the row-event guard with a deliberate removal mutation rather than claiming a pre-existing defect.
- **Framework §2.22**: Update the existing Studio design specification with the shipped examples. No event, extension-point, task, or feature catalog changes are implied.
- **Framework §2.24**: This section is marked Draft pending ratification and is not a gate for this plan. The work adopts existing public UI primitives and introduces no new architectural pattern.
- **Elsa constitution**: The change stays in Studio presentation and browser history. It does not modify design/runtime package boundaries, persisted domain state, APIs, authorization, or deployment composition. Elsa §E2.9 is provisional and does not govern this work because no `WorkflowDefinitionState`, persisted collection projection, or workflow-definition contract is changed.
- **Review checklist**: Side-by-side screenshots, keyboard operation, semantic tables, long-name layout, denied/unavailable outcomes, and truthful loading/empty/error behavior are explicit acceptance evidence. No new public UI API or dependency is planned.

**Pre-design result**: No gate violation or constitution exception is proposed. The draft/provisional sections above are excluded because the planned changes do not touch their subject matter.

### Post-design

The design below keeps separate list contracts and reuses only public UI primitives or an existing internal frame. It adds no general abstraction, persistence/API surface, backend contract, or module. Behavior tests cover only the risks introduced by changing row contents and return context; existing behavior remains protected by its current suites. The design-spec update satisfies the documentation obligation without testing Markdown content.

**Post-design result**: All applicable gates pass by scope and design. Final source review and repository gates remain pending root-owned implementation QA; no tests/builds have been run or passed in this planning task.

## Implementation Decisions

1. **Align presentation without merging list contracts.** Keep each search input live and keep each list's current filter placement semantics, lifecycle choices, authority choices, and page contract. Reuse `WorkflowsPageFrame` only for the compositions it already owns. Align Activity Definitions where its custom header/actions can use the same visual hierarchy without forcing workflow-only controls or semantics into that page.
2. **Keep the default row compact.** Lead with the human name and primary identity/status/timing. Move secondary technical IDs into an accessible progressive disclosure. Use `CopyableIdentifier` for exact keyboard-copyable values and the existing public disclosure controls; do not rely on hover titles or add another clipboard implementation.
3. **Protect interactive rows.** Nested disclosure and copy actions must stop or be excluded from row click and Enter/Space handling. Verify that this holds for both pointer and keyboard activation, including after the row identity layout changes.
4. **Store Activity collection context per history entry.** Preserve raw and settled/debounced search and provider values, authority, page size, cursor stack, and current cursor with the originating browser history entry. Use the existing local workflow-session invalidation pattern as a reference for the new Activity restoration scope, without altering that session. Back from the workbench/editor and browser Back must return to the matching entry. Never use a single mutable “last collection” snapshot for multiple origins. Keep the cursor opaque and out of newly invented URL fields. Because Activity's existing query key has no host/auth key, per-entry state is not authorization proof: on auth/base-url changes, route restoration through the existing local redaction seam and test that it cannot recommit previously displayed rows. Preserve valid filters/page size and use the existing restart/recovery behavior for invalid cursors; do not redesign global query caching or authentication.
5. **Keep actions domain-owned.** Use existing themed button and overflow-menu controls to align visible actions. Preserve Workflow Definition selection, Move, Delete, Restore, permanent Delete, and confirmations. Preserve Activity Definition Create/Refresh/Tools and workbench operations; do not add collection selection or lifecycle actions. Preserve the Runs delete permission and confirmation behavior.
6. **Document examples, not a new framework.** Update `Studio/docs/ux/design-spec.md` with the three consumers, compact/expanded identity behavior, action hierarchy, return-context rule, and named domain exceptions. Do not replace the existing tables with `StudioDataGrid` or introduce a common list abstraction without two or more consumers proving the same structure.

## Project Structure

### Documentation (this feature)

```text
specs/1003-studio-list-consistency/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ui-context-and-identity.md
├── tasks.md
└── checklists/requirements.md
```

The data model captures only ephemeral browser-entry context and row-identity presentation; no persisted entity is added. The UI contract records existing and required browser-visible behavior, with no backend or public SDK contract. The quickstart describes validation steps but records no results. Source evidence and validation boundaries are linked above; implementation remains outside this planning phase.

### Source Code (repository root)

```text
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/
├── ActivityDefinitionsPage.tsx
├── activityDefinitionCollection.css
├── workflow-editor/
│   ├── pages.tsx                         # existing WorkflowsPageFrame
│   ├── WorkflowDefinitions.tsx
│   └── WorkflowInstances.tsx             # Runs row-only refinement
└── __tests__/
    ├── activityDefinitions.test.tsx
    ├── workflowDefinitionRoute.test.tsx
    ├── workflowDefinitionBrowseLocation.test.ts
    └── workflowDefinitionSearch.test.tsx

Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/
├── identity/CopyableIdentifier.tsx       # existing public control; consume first
├── disclosure/                           # existing public controls; consume first
└── forms/Button.tsx                      # existing public button; consume first

Studio/tests/browser/
├── activity-definitions.spec.ts           # supplemental focused fixture journey
├── workflow-definition-browse-context.spec.ts
└── new or existing list-consistency journey for the real host

Studio/docs/ux/design-spec.md
```

**Structure Decision**: Keep behavior in the existing Workflows client. Reuse the existing `WorkflowsPageFrame` within its established scope and keep Activity Definitions' collection-specific controls and table semantics. An internal presentation extraction is allowed only if inspection of the three consumers proves the same structure; do not add a reusable library or generic data-grid layer.

## Validation Plan

The following are future gates, not current results:

1. **Activity return context first**: Add the focused test for editor Back, browser Back, multiple origin entries, direct-entry fallback, and reload. Run it against the approved source base and record its actual result. Implement this existing functional defect before identity or visual changes. Cover changed authorization, invalid/expired cursor, and current recovery behavior; never put a synthetic cursor in the URL.
2. **Exact identities second**: Add focused coverage for compact/default versus expanded identity, exact keyboard copy, and nested pointer/Enter/Space actions. A missing control on the old base is a new-feature gap, not a pre-existing defect. After implementing the guard, temporarily remove it to demonstrate the test failing, restore it, and rerun green.
3. **Visual alignment third**: Run the existing browse/search/location/status tests for `WorkflowDefinitions.tsx` to show #3068 URL, lifecycle, folder, selection, and action behavior remains intact. Do not reimplement or broaden the workflow browse-session slice.
4. **Normal-host visual acceptance**: Start the source-built `Essentials/apps/Elsa.Workbench` with the ordinary `Studio.Web` shell and real authorized Activities Design and Workflows Design APIs. Use persisted rows across multiple pages, different activity authorities, draft-only/published summaries, folders/unfiled workflow rows, duplicate and long names, and exact IDs. Use a bounded representative matrix: inspect the three lists at 1280px in light theme and 1440px in dark theme with representative populated, empty, error, denied/unavailable, and expired-snapshot states; at 390px verify long identities, keyboard operation, row actions, and selected return/recovery states. Capture side-by-side views and keep route-mocked browser fixtures supplemental; they are not normal-composition proof.
5. **Provenance and cleanup**: Record the exact source SHA, host and Studio build command/results, package/assets served by the browser, authorization mode, and fixture/data source. After acceptance, remove seeded rows and temporary data with a recorded cleanup check. Do not use stale bundles or fixture-mode routes as source-built host evidence.
6. **Repository delivery gates**: Root owns the integrated diff review, affected complete Studio/Workflows client suites, relevant package builds and layout/CSS checks, any applicable backend e2e suite, architecture guard, generated-map check/refresh decision, exact-head CI, PR evidence, and resulting-main CI/Maps. Run only checks applicable to the final changed files and report each gate separately. No result from this planning branch substitutes for those gates.

## Complexity Tracking

No constitution violations, new public API, backend contract, package, dependency, or architecture exception is proposed.

## Cadence and Ownership

This is one implementation leaf under one serial integration model. A single source writer owns the entire shared-list change; the shared row components and Activity history state do not support independent source writers. Root approved this package before source work and owns integration and final review. Root decides final repository gates. The spec is `Approved`; the eventual implementation PR sets `Implemented` before merge per [spec lifecycle](../../docs/reference/spec-lifecycle.md).

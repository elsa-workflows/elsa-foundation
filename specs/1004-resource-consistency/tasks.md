# Tasks: Studio resource-list consistency and Home access

**Input**: Design documents from `specs/1004-resource-consistency/`

**Prerequisites**: `plan.md` and `spec.md`; `research.md`, `data-model.md`, `contracts/ui-contract.md`, and `quickstart.md` are available.

**Tests and cadence**: This repairs existing Studio UI logic. The framework's greenfield test-first cadence is not inferred. Focused behavioral regressions and source-revert causal controls are required by the approved program for these changes; place and run them as described below. Do not report a test as passing until the exact candidate head was run and its result retained.

**Task format**: Every task below follows `- [ ] Tnnn [P?] [USn?] Description with exact file path(s)`. This work needs no project initialization or shared infrastructure task: all target modules, tests, endpoints, and shared controls already exist.

## Phase 1: User Story 1 - Manage Secrets from a clear, bounded inventory (Priority: P1)

**Goal**: Align Secrets' hierarchy and actions while preserving the 100-record inventory, query states, scope wording, and metadata-only behavior. Make browser fixture write attempts observable even when they fail.

**Independent Test**: Run `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretsPage.test.tsx` and `Studio/tests/browser/secret-scopes.spec.ts` against the candidate head. Verify the 100/101 boundary, server search, loaded-scope qualification, action permissions/confirmations/focus, query-state behavior, and POST/PUT/DELETE count including failures, with no request payload inspection.

- [X] T001 [P] [US1] Add focused Secrets behavior cases and causal negative controls in `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretsPage.test.tsx` for action priority, 100/101 coverage, server search, loaded-scope wording, and accepted/draft/failed query behavior.
- [X] T002 [US1] Align the page hierarchy and Secrets row actions in `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretsPage.tsx` and `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/styles.css`; bind the request limit and coverage wording to one internal value and preserve existing permissions, confirmations, busy/error states, focus return, and technical-name-only copy.
- [X] T003 [US1] Make every attempted fixture POST, PUT, and DELETE visibly increment, including thrown/rejected handlers, and add safe counter assertions in `Studio/tests/browser/secretScopesFixture.tsx` and `Studio/tests/browser/secret-scopes.spec.ts` without inspecting bodies, headers, or secret values; update only the affected header-opener locator in `Studio/tests/browser/creation-dialog-tokens.spec.ts` for the new Create secret label.

**Checkpoint**: Secrets remains bounded and truthful, its primary/secondary actions retain existing semantics, and the browser fixture exposes attempted-write counts without sensitive data.

## Phase 2: User Story 2 - Keep Connections actions and creation in context (Priority: P2)

**Goal**: Match the approved high-level list hierarchy while retaining Details and its actions; preserve the accepted search across create and later actions.

**Independent Test**: Run `Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/__tests__/connectionsPage.test.tsx` and `Studio/tests/browser/connections-management.spec.ts`. Create under a non-empty accepted query and verify the exact notice, same-query reload, returned-item-only selection, Details actions, and state preservation.

- [X] T004 [P] [US2] Add a regression and causal negative control for create-under-search in `Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/__tests__/connectionsPage.test.tsx`: preserve the accepted query, exact success notice, reload that query, and do not select a created name absent from returned `items`.
- [X] T005 [US2] Repair Connection create query handling and align the page hierarchy in `Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionsPage.tsx` and `Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/styles.css`; retain `ConnectionList.tsx`, `ConnectionDetailDialog.tsx`, Details focus restoration, and existing action semantics.
- [X] T006 [US2] Extend `Studio/tests/browser/connections-management.spec.ts` to verify accepted-query retention, exact notice, returned-item-only selection, existing details operations, and keyboard/focus behavior with loopback-only fixtures and no external calls; adapt the existing header-opener locator in `Studio/tests/browser/connection-secrets.spec.ts` to Create connection.

**Checkpoint**: Connection creation leaves the operator in the accepted list context, and Details remains the domain-specific row entry with its existing actions.

## Phase 3: User Story 3 - Open Home's active-incident destination only with access (Priority: P3)

**Goal**: Gate only Needs attention for authenticated runtime-read access and preserve authentication-disabled and Continue behavior.

**Independent Test**: Run `Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/__tests__/homeTaskGroup.test.tsx` and `Studio/tests/browser/home.spec.ts` for authenticated allowed/denied and authentication-disabled sessions; verify the exact active-incident URL and unchanged Continue semantics.

- [X] T007 [P] [US3] Add allowed, denied, auth-disabled, matcher-grammar, exact-destination, and unchanged-Continue cases in `Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/__tests__/homeTaskGroup.test.tsx` and `Studio/tests/browser/home.spec.ts`, using bounded authenticated scenarios in `Studio/tests/browser/homeFixture.tsx` while preserving its default anonymous session.
- [X] T008 [US3] Gate only the Needs attention destination in `Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/HomeTaskGroup.tsx` using the existing effective-permission matcher; retain the active-incident URL, authentication-disabled behavior, and Continue catalog/count behavior without changing `Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/styles.css` unless visual review finds a concrete required correction.

**Checkpoint**: Authenticated users without effective runtime-read cannot open Needs attention; authorized and authentication-disabled sessions keep the intended destination, and Continue remains unchanged.

## Phase 4: Integration, visual review, and exact-head qualification

**Purpose**: Complete the approved product scope, retain sensitive-data controls, and provide root with reviewable candidate-head evidence. No check in this phase has run yet.

- [ ] T009 Review combined loading, empty, failed, action-error, keyboard, accessible-name, confirmation, focus-return, masking, and responsive states; save before/after screenshots at 1600×1000 @2x in Classic Light and dark, and inspect Dim, High Contrast, 1280 px light, 1440 px dark, and 390 px narrow layouts under `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/`.
- [ ] T010 Run the exact-head focused Vitest and browser suites from the story checkpoints, causal negative controls, full Workflows shuffle seeds 1/11/29, full Studio `pnpm typecheck` including `tests/live`, affected lint/style/layout checks, affected Secrets and Connections client builds, five existing build groups, applicable architecture/maps checks, and full Chromium; preserve actual failures, skips, and unexecuted counts.
- [ ] T011 Complete the fresh disposable normal-host proof described in `specs/1004-resource-consistency/quickstart.md` with 101 synthetic Secrets, returned registered loopback-only Connections, strict-write and masking checks, authenticated allow/deny including 403, and cleanup; keep secret values, request bodies, headers, and credentials out of all evidence and do not claim remote HTTP test or revocation behavior.
- [ ] T012 Refresh the exact gate matrix, then hand the complete candidate and evidence to the root for three independent review axes, actual Full-tier review after readiness, exact-head app approval, and resulting-main CI/Maps/Studio verification after any authorized merge; run `build:lib` only if its published source surface was changed.

## Dependencies and Execution Order

### Phase dependencies

- User Stories 1, 2, and 3 can be developed as independent slices after the root approves this specification and authorizes product writes.
- T001, T004, and T007 touch separate focused test files and can be prepared in parallel. Each implementation task follows its story's regression work so the approved negative control can demonstrate the defect on the selected baseline.
- T003 is owned with the Secrets slice and can proceed after fixture assertions are specified; it changes a distinct fixture/test pair.
- T009–T012 depend on all three stories being integrated and reviewed. Host, full browser, review, and resulting-main gates are root-owned.

### User story dependencies

- **US1 (P1)**: Independent. Includes Secrets page behavior and the browser fixture.
- **US2 (P2)**: Independent. It preserves the existing Connections API/Details behavior.
- **US3 (P3)**: Independent. It reuses existing Home permission matching and does not depend on either resource page.

### Parallel opportunities

- The Secrets+fixture slice (T001–T003), Connections slice (T004–T006), and Home slice (T007–T008) are separate bounded ownership areas.
- Root owns Home implementation, cross-slice integration, visual/host qualification, and QA. Separate writers must not edit the same files concurrently.

## Implementation Strategy

1. Root reviews and approves this draft and authorizes product writes before any source or test edits.
2. Keep implementation to the existing Secrets, Connections, Home, and browser-fixture paths above. Add behavioral regressions and source-revert controls for the approved changes; the plan does not claim constitutional test-first cadence.
3. Integrate the three independent stories, then review the complete diff for scope, access behavior, query state, secret masking, and design boundaries.
4. Run the exact-head checks in T009–T012; preserve each result and do not carry forward #3069/#3310 proof as #3071 acceptance.
5. Mark the spec `Implemented` only in an authorized implementation PR when the lifecycle transition is ready. During implementation, this specification remains **In progress** and actual checks remain **Not run**.

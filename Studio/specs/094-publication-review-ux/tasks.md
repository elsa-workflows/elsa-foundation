# Tasks: Workflow Publication Review UX

**Input**: Design documents from `specs/094-publication-review-ux/`

**Tests**: Required by the feature specification and Studio constitution. Write focused tests before implementation and prove the real modal in a browser.

## Phase 1: Setup and shared contracts

- [x] T001 Confirm the paired Foundation capability relation and preflight/promotion request shapes in `specs/094-publication-review-ux/contracts/workflow-publication-review.md`
- [x] T002 [P] Add capability helpers and promotion-preflight client types in `src/Elsa.Studio.Workflows/Client/src/api/capabilities.ts` and `src/Elsa.Studio.Workflows/Client/src/api/workflowDesign.ts`
- [x] T003 [P] Add failing review-model tests for channel selection, effect, baseline, automatic policy labels, and exact-version capability gating in `src/Elsa.Studio.Workflows/Client/src/__tests__/publicationReview.test.ts`

## Phase 2: Foundational review orchestration

- [x] T004 Extend the publication review state/intent projection in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/publicationReview.ts` without changing wire-level publication authority
- [x] T005 Add failing operation tests for automatic latest-only preflight, stale-result suppression, capability absence, exact-version preflight, and retained-promotion retry in `src/Elsa.Studio.Workflows/Client/src/__tests__/workflowPublicationOperations.test.tsx`
- [x] T006 Split read-only review refresh from mutation and integrate Foundation promotion preflight in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/useWorkflowOperations.ts`

## Phase 3: User Story 1 — Review a routine publication at a glance (P1)

**Goal**: Present destination, effect, policy-assigned version, readiness, baseline, and compact changes first.

**Independent Test**: Open the dialog for a ready default-channel publication and understand the outcome without opening disclosures.

- [x] T007 [US1] Add failing component assertions for the default hierarchy, captured-editor-state copy, and progressive disclosures in `src/Elsa.Studio.Workflows/Client/src/__tests__/publicationSlots.test.tsx`
- [x] T008 [US1] Recompose `PublicationReviewDialog` into persistent header, scroll body, and footer in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx`
- [x] T009 [US1] Render compact changes plus Changes details and Advanced details disclosures in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx`
- [x] T010 [US1] Add token-based fixed-shell, summary, disclosure, and responsive styles in `src/Elsa.Studio.Workflows/Client/src/styles.css`

## Phase 4: User Story 2 — Choose a publication channel with clear consequences (P1)

**Goal**: Select an existing Publication channel or create a named one, with the resolved create/replace effect made explicit.

**Independent Test**: Keyboard-select `default`, an occupied named channel, and Create new channel; verify current authoritative evidence refreshes and stale responses cannot enable Publish.

- [x] T011 [US2] Add failing keyboard/component tests for the existing-channel combobox and create-new path in `src/Elsa.Studio.Workflows/Client/src/__tests__/publicationSlots.test.tsx`
- [x] T012 [US2] Implement the accessible existing-channel selector and distinct create-new-channel path in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx`
- [x] T013 [US2] Remove routine action radios, derive effect from current authoritative target evidence, and expose checking/blocked reasons in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx`

## Phase 5: User Story 3 — Complete or recover without losing controls (P1)

**Goal**: Keep actions visible in every state and provide truthful compact success and partial-failure recovery.

**Independent Test**: Publish successfully and simulate activation failure after promotion at constrained height; Close, Open published executable, and Retry publication remain visible and correct.

- [x] T014 [US3] Add failing success/recovery/fixed-footer component tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/publicationSlots.test.tsx`
- [x] T015 [US3] Replace appended alerts with dedicated success, saved-failure, and retained-promotion recovery bodies in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx`
- [x] T016 [US3] Wire Close, Open published executable, and activation-only Retry publication actions in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx` and `src/Elsa.Studio.Workflows/Client/src/workflow-editor/useWorkflowOperations.ts`

## Phase 6: User Story 4 — Override an automatic version when supported (P2)

**Goal**: Offer progressive exact SemVer editing only when Foundation advertises end-to-end support.

**Independent Test**: With capability absent, no edit control appears and automatic promotion is unchanged; with capability present, valid forward/prerelease versions become ready and invalid/duplicate/non-forward values stay blocked before promotion.

- [x] T017 [US4] Add failing capability-present/absent and exact-version validation tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/publicationSlots.test.tsx` and `src/Elsa.Studio.Workflows/Client/src/__tests__/workflowPublicationOperations.test.tsx`
- [x] T018 [US4] Add the progressive Edit version control, checking state, and inline authoritative diagnostics in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx`
- [x] T019 [US4] Pass exact version only through advertised preflight/promotion relations in `src/Elsa.Studio.Workflows/Client/src/api/workflowDesign.ts` and `src/Elsa.Studio.Workflows/Client/src/workflow-editor/useWorkflowOperations.ts`

## Phase 7: Verification and polish

- [x] T020 Add/extend the real Workflows browser fixture for normal, occupied named, new named, unsupported/supported exact version, success, recovery, keyboard, and constrained-height states in `tests/browser/workflow-publication-review.spec.ts`
- [x] T021 Run focused Vitest suites, Workflows typecheck/build, `pnpm lint:css`, and repository lint
- [x] T022 Run Playwright at normal and constrained heights, inspect themed screenshots, and record evidence against `specs/094-publication-review-ux/quickstart.md`
- [x] T023 Review focus restoration, live-region/error announcements, latest-only preflight, capability absence, and no-regression publication authority

## Dependencies and execution order

- T001–T006 establish the compatible client/state/orchestration boundary and block the UI stories.
- US1 and US2 share the dialog and should land sequentially; US3 builds on the fixed shell; US4 additionally depends on the paired Foundation capability.
- Tests in each story are written first and observed failing before implementation.
- T020–T023 run only after all desired stories integrate.

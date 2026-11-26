# Tasks: Activity Instance Metadata

**Input**: Design documents from `/specs/094-activity-instance-metadata/`

## Phase 1: Foundation contract and persistence

- [x] T001 [US1] Add normalized `ActivityPresentationRecord` and expose it from draft/version
  design metadata contracts.
- [x] T002 [US1] Extend create, clone, update, promote, read, and Groundwork persistence paths with
  the full-state presentation collection.
- [x] T003 [US1] Extend design API commands/views and validation for the 200/2,000 character limits,
  blank normalization, duplicate IDs, and empty-record omission.
- [x] T004 [US4] Add the frozen executable-node presentation collection to Source References and
  inspection views.
- [x] T005 [US4] Snapshot and remap presentation through publish, reusable-activity publication,
  template placement, and Test Run paths.
- [x] T006 [US4] Advance Source Reference persistence compatibility fixtures while retaining
  backward reads.
- [x] T007 [US4] Prove presentation is excluded from Execution Material and `ArtifactHash`.

## Phase 2: Studio draft model and shared UI

- [x] T008 [P] [US2] Implement and export the shared accessible `CopyableIdentifier` primitive.
- [x] T009 [US1] Add activity-presentation metadata to workflow draft JSON, autosave signature,
  undo/redo snapshots, duplication, version replacement, and deletion pruning.
- [x] T010 [US1] Add the shared resolved-label helper and use it across editable and read-only
  activity projections.
- [x] T011 [US1] Add editable Display Name and Description fields to Inspector Details with live
  updates and read-only historical rendering.
- [x] T012 [US2] Move Node ID below the Inspector title and migrate Activity Type and Activity
  Version ID to `CopyableIdentifier`.

## Phase 3: Declutter and historical projections

- [x] T013 [P] [US3] Remove version badges from every editable, Test Run, ghost, and executable
  canvas node.
- [x] T014 [P] [US3] Hide palette versions unless multiple selectable versions of the same activity
  are ambiguous; preserve tooltip/accessibility text.
- [x] T015 [US4] Join frozen Source Reference presentation into executable/Test Run/Run graphs and
  include authored metadata in Weaver context.

## Phase 4: Tests and verification

- [x] T016 [P] Add Foundation unit/contract tests for normalization, lifecycle copying, Groundwork
  compatibility, source freezing/remapping, and hash invariance.
- [x] T017 [P] Add Studio unit/component tests for label resolution, draft signatures/history,
  Inspector editing, exact copy behavior, local announcements, and version visibility.
- [x] T018 Add Playwright coverage for editable metadata and component coverage for frozen historical
  rendering.
- [x] T019 Run Foundation targeted test projects and Studio typecheck, lint, Vitest, and Playwright
  checks.
- [x] T020 Review both repository diffs for contract compatibility, accessibility, design tokens,
  repetition, and unrelated changes; update quickstart/results.

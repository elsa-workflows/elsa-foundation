# Tasks: Run Detail Input Evidence Workbench

**Input**: [spec.md](./spec.md), [plan.md](./plan.md), [data-model.md](./data-model.md), and [contracts](./contracts/)
**Tests**: Required. Add failing tests before implementation in each slice.

## Phase 1: Setup and guardrails

- [X] T001 Confirm both worktrees are on `codex/run-detail-input-evidence` and preserve unrelated changes documented in `specs/006-run-detail-input-evidence/tracking.md`
- [X] T002 [P] Add focused Foundation test fixtures for Source Reference sidecars, compiled bindings, evidence provenance, permission denial, and legacy records
- [X] T003 [P] Add focused Studio test builders for declared/source/evidence unions

## Phase 2: Foundation contracts and persistence

- [X] T004 [P] Add failing Source Reference sidecar serialization/store/hash-invariance tests in `/Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Workflows/Runtime/Tests/` and publishing tests
- [X] T005 [P] Add failing executable-inspector tests for ReferenceKey, structured bindings, sensitivity, permission-hidden fields, and legacy records in `/Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Workflows/Runtime/Api/Tests/WorkflowExecutableInspectorTests.cs`
- [X] T006 [P] Add failing activity-catalog API tests for `referenceKey` in the existing Activities Design API test project
- [X] T007 Add a verbatim-`JsonElement` Authored Inputs Sidecar keyed by executable node ID and input ReferenceKey to `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutableSourceReference.cs`
- [X] T008 Copy authored `ArgumentState` plus sensitivity into the Source Reference sidecar in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Publishing/Api/Handlers/PublishWorkflowRequestHandler.cs` and `StartWorkflowTestRunRequestHandler.cs`
- [X] T009 Expose `referenceKey` in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Activities/Design/Api/Models/ActivityAuthoringCatalogView.cs` and `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Activities/Design/Api/Handlers/ListActivityAuthoringCatalogRequestHandler.cs`
- [X] T010 Extend executable input-binding inspection models with ReferenceKey, structured supported binding variants, sensitivity, and safe legacy fields in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Api/Models/WorkflowExecutableInspectionViews.cs`
- [X] T011 Preserve sensitivity/reference metadata through `ExecutableNodeCompiler.cs`, `RuntimeInputBindingCompiler.cs`, supported runtime binding serialization, and hashing without adding authored provenance to execution material under `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Publishing/Api/Services/`
- [X] T012 Pass Source Reference sidecar round-trip, executable hash-invariance, catalog, inspector, and legacy tests

## Phase 3: Foundation runtime provenance, failure isolation, and security

- [X] T013 [P] Add failing evidence-model/API tests that preserve existing subject/mode/state/type/snapshot wire semantics while adding inputKey, evaluation ID/phase/sequence, and legacy defaults in `/Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Workflows/Runtime/Tests/RuntimeActivityExecutionInspectionTests.cs`
- [X] T014 [P] Add failing invoke, bookmark-resume, and parent-completion tests for replay deduplication, atomic sequence, sensitivity, and per-input failure isolation in the relevant Foundation runtime/resumption test projects
- [X] T015 [P] Add failing API permission-matrix tests proving `workflow-publishing.read` source access and `workflow-runtime.read` evidence access independently omit every protected field
- [X] T016 Extend `ActivityExecutionInspectionValueSnapshot` and API view additively with inputKey, evaluation ID/phase/sequence, access state, and sanitized failure without changing existing enums/unions in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Core/Models/ActivityExecutionInspectionValueSnapshot.cs` and `Runtime/Api/Models/WorkflowExecutionViews.cs`
- [X] T017 Redesign runtime input materialization to return per-input success/failure results and carry ReferenceKey/sensitivity under `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/`
- [X] T018 Propagate evidence provenance/sensitivity and persist successful plus failed input evidence in initial invoke paths including `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Activities/Runtime/Services/ActivityOutputPublisher.cs`
- [X] T019 Propagate the same contract through `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Activities/Runtime/Services/WorkflowResumeBookmarkSchedulerWorkHandler.cs`
- [X] T020 Propagate the same contract through `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Activities/Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs`
- [X] T021 Deduplicate replay by evaluation ID and allocate sequence atomically with checkpoint/state persistence in the activity-execution inspection persistence path
- [X] T022 Add a source-sidecar inspection endpoint/projection protected by `workflow-publishing.read`; keep evidence projection under `workflow-runtime.read`; remove denied source/evidence fields before serialization
- [X] T023 Pass Foundation runtime, resumption, API, permission, sensitivity-boundary, and legacy compatibility tests

## Phase 4: User Story 2 — Paired input source and evidence (P1)

**Independent test**: Every supported compiled binding plus arbitrary authored expression types and an unknown Studio API discriminator pair by ReferenceKey without parsing `summary`.

- [X] T024 Extend Studio SDK/activity catalog/runtime types for `referenceKey`, source sidecar, compiled binding, and additive evidence fields in `src/Elsa.Studio.Web/Client/src/sdk/index.ts` and `src/Elsa.Studio.Workflows/Client/src/workflowTypes.ts`
- [X] T025 [P] [US2] Add failing pure union-row tests covering canonical, one-sided, compiled-only/source-unavailable, duplicate, and legacy records in `src/Elsa.Studio.Workflows/Client/src/__tests__/inputInspectionRows.test.ts`
- [X] T026 [US2] Implement pure ReferenceKey-scoped input union derivation in `src/Elsa.Studio.Workflows/Client/src/inputInspectionRows.ts`
- [X] T027 [P] [US2] Add failing SDK admission/order/Host Policy and generic fallback tests for optional read-only expression source rendering
- [X] T028 [US2] Add optional `sourceRenderer` to the existing public `StudioExpressionEditorContribution`, host registry declarations/tests, and JavaScript/Liquid module contributions in their owning modules; keep generic fallback in Workflows
- [X] T029 [P] [US2] Add compact/expanded paired-input component tests in `src/Elsa.Studio.Workflows/Client/src/__tests__/workflowActivityExecutionDetails.test.tsx`
- [X] T030 [US2] Retain canonical compiled bindings and pinned Source Reference authored sidecar without summary parsing in `src/Elsa.Studio.Workflows/Client/src/executableGraph.ts` and the Run detail data loader
- [X] T031 [US2] Implement compact/expanded input rows and preserve runtime-only Outputs in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`

## Phase 5: User Story 3 — Provenance and security boundaries (P2)

**Independent test**: All phases, four source/evidence permission combinations, sensitive inputs, compatibility states, and per-input failures render safely.

- [X] T032 [P] [US3] Add Studio tests for replay-deduplicated ordered history, protected-state matrix, metadata-only/unavailable/redacted evidence, failures, issue correlation, and no protected DOM content
- [X] T033 [US3] Render “Evaluated at runtime”, latest sequence, phase-labelled history, safe failures, compiled-only/source-unavailable, and explicit anomaly/protected states in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`
- [X] T034 [US3] Reorder activity content to summary, Inputs, Outputs, collapsed Execution details in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`

## Phase 6: User Story 1 — Full-height Run workbench (P1)

**Independent test**: Real shell geometry has no empty band, overlap, or route scrollbar across console and host-responsive states.

- [X] T035 [P] [US1] Add Vitest assertions for Run-specific route classes/layout state and regressions protecting list/designer/executable-inspector layouts
- [X] T036 [P] [US1] Add Playwright geometry proof in `tests/browser/workflow-run-detail-layout.spec.ts` for workbench/console bounding rectangles and desktop/medium/narrow transitions
- [X] T037 [US1] Add a route-specific `wf-page--run-workbench` variant and carry `min-height: 0` through `src/Elsa.Studio.Workflows/Client/src/workflow-editor/pages.tsx` and a narrowly targeted host rule in `src/Elsa.Studio.Web/Client/src/app/styles.css`
- [X] T038 [US1] Replace viewport-derived sizing with intrinsic header/toolbar plus `minmax(0, 1fr)` workbench allocation and internal scroll ownership in `src/Elsa.Studio.Workflows/Client/src/styles.css`
- [X] T039 [US1] Create `src/Elsa.Studio.Workflows/Client/src/workflow-editor/useRunDetailLayout.ts` with Run-specific storage keys, 400px default, 340px min, dynamic `min(640px, 50%, width - 10px - 480px)` max, and 42px rail; do not change shared designer defaults

## Phase 7: User Story 4 — Container-responsive inspector (P2)

**Independent test**: Container-only resize crosses desktop ≥830, medium 480–829, and narrow <480 with correct focus and no implicit columns.

- [X] T040 [P] [US4] Add mode-transition tests for exact boundaries, default clamping, selection/removal, persistence, modal drawer dismissal/focus trap/restore, and narrow Back behavior
- [X] T041 [US4] Implement container observation and exact responsive state transitions in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/useRunDetailLayout.ts`
- [X] T042 [US4] Implement medium labelled modal drawer (close/Escape/backdrop/inert/focus restore) and narrow non-modal canvas/inspector focus views in `src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`
- [X] T043 [US4] Reset explicit grid placement/resize controls per mode and add overlay/focus styles using Studio tokens in `src/Elsa.Studio.Workflows/Client/src/styles.css`

## Phase 8: Integration and proof

- [X] T044 [P] Run focused Foundation tests/builds and record commands/results in `specs/006-run-detail-input-evidence/tracking.md`
- [X] T045 [P] Run Studio workflow/SDK/expression-module tests, typecheck, changed-file lint/CSS lint, and build; record results in tracking
- [X] T046 Run `tests/browser/workflow-run-detail-layout.spec.ts` and capture desktop/medium/narrow browser proof in tracking
- [X] T047 Perform up to five root-led self-review/fix iterations across correctness, security, artifact identity, replay safety, compatibility, accessibility, layout, and maintainability
- [X] T048 Update parent/slice issues and PR tracking links/status in `specs/006-run-detail-input-evidence/tracking.md`

## Dependencies and parallel opportunities

- T004–T012 establish persistence/source/catalog contracts and block production Studio source wiring.
- T013–T023 establish evidence provenance/security and block final US3 integration.
- Studio fixtures and failing tests T025/T027/T029 can run while Foundation contracts settle.
- Layout T035–T043 is independent of Foundation and may run in parallel with backend work.
- T044 and T045 may run in parallel; T046–T048 require integrated code.

## Implementation strategy

Foundation artifact/source-reference correctness and permission boundaries are release gates. The minimum demonstrable increment is Foundation contracts plus Studio paired rows and full-height layout, but no release is complete without sensitivity propagation, replay safety, per-input failure evidence, responsive modes, and browser geometry proof.

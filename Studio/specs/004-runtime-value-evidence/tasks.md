# Tasks: Runtime Value Evidence

**Input**: Design documents from `specs/004-runtime-value-evidence/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required by the feature specification for policy resolution, snapshot bounds, redaction, settings API, runtime capture, and Studio rendering.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm repository state and active contracts before implementation.

- [x] T001 Verify current Studio worktree status and active feature metadata in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/.specify/feature.json`
- [x] T002 [P] Verify Elsa foundation backend worktree status in `/Users/sipke/Projects/Elsa/elsa-foundation`
- [x] T003 [P] Review current activity inspection rendering code in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`
- [x] T004 [P] Review current runtime payload capture policy code in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Core/Models/RuntimePayloadCapturePolicy.cs` and `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Services/DefaultRuntimePayloadCapturePolicy.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Define shared backend/client vocabulary needed by every user story.

- [x] T005 Add `DiagnosticSnapshot` to `RuntimePayloadCaptureMode` and preserve level ordering in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Core/Models/RuntimePayloadCapturePolicy.cs`
- [x] T006 [P] Add Diagnostic Snapshot node models and snapshot limit options in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Core/Models/DiagnosticSnapshot.cs`
- [x] T007 [P] Add Studio Diagnostic Snapshot TypeScript union types in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/workflowTypes.ts`
- [x] T008 Add runtime diagnostics settings DTOs and evidence-level types in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/workflowTypes.ts`
- [x] T009 [P] Add backend tests for capture mode ordering and Diagnostic Snapshot model serialization in `/Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Workflows/Runtime/Tests/RuntimeDiagnosticSnapshotTests.cs`

**Checkpoint**: Shared vocabulary exists for backend capture, API projection, and Studio rendering.

---

## Phase 3: User Story 1 - Troubleshoot Activity Values Safely (Priority: P1) MVP

**Goal**: Activity inspection shows bounded Diagnostic Snapshots for activity inputs and outputs by default when Host Policy allows it.

**Independent Test**: Run a workflow with activity inputs/outputs, open the Activity inspector, and verify input/output Diagnostic Snapshots render without raw full payloads.

### Tests for User Story 1

- [x] T010 [P] [US1] Add backend snapshot bounds/redaction tests in `/Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Workflows/Runtime/Tests/RuntimeDiagnosticSnapshotTests.cs`
- [x] T011 [P] [US1] Update backend default policy tests in `/Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Workflows/Runtime/Tests/RuntimeDiagnosticsHistoryIncidentContractTests.cs`
- [x] T012 [P] [US1] Update backend activity execution capture tests in `/Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Activities/Runtime/Tests/WorkflowInvokeActivitySchedulerWorkHandlerTests.cs`
- [x] T013 [P] [US1] Add Studio rendering tests for input/output Diagnostic Snapshot evidence in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/workflowActivityExecutionDetails.test.tsx`

### Implementation for User Story 1

- [x] T014 [US1] Implement bounded Diagnostic Snapshot generation service in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Services/DefaultDiagnosticSnapshotFactory.cs`
- [x] T015 [US1] Inject and use Diagnostic Snapshot generation for activity input/output capture in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Activities/Runtime/Services/ActivityOutputPublisher.cs`
- [x] T016 [US1] Apply Diagnostic Snapshot generation in resume/parent activity capture paths in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Activities/Runtime/Services/WorkflowResumeBookmarkSchedulerWorkHandler.cs` and `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Activities/Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs`
- [x] T017 [US1] Project Diagnostic Snapshot payloads through the activity execution inspection API view in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Api/Models/WorkflowExecutionViews.cs`
- [x] T018 [US1] Update Studio activity inspection types for `snapshot`, `state`, input subjects, and output subjects in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/workflowTypes.ts`
- [x] T019 [US1] Replace input-only payload rendering with input/output evidence sections and Diagnostic Snapshot tree rendering in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`
- [x] T020 [US1] Add token-governed styles for snapshot tree, markers, and compact evidence states in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/styles.css`

**Checkpoint**: User Story 1 is functional and independently testable.

---

## Phase 4: User Story 2 - Control Runtime Diagnostics In Studio (Priority: P2)

**Goal**: Studio administrators can request runtime diagnostics levels and see effective levels after Host Policy caps.

**Independent Test**: Change Workflows runtime diagnostics settings in Studio, observe requested/effective state, run a new workflow, and verify only future evidence changes.

### Tests for User Story 2

- [x] T021 [P] [US2] Add backend settings API tests in `/Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Workflows/Runtime/Tests/RuntimeDiagnosticsSettingsApiTests.cs`
- [x] T022 [P] [US2] Add Studio settings API hook tests in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/runtimeDiagnosticsSettings.test.tsx`
- [x] T023 [P] [US2] Add Studio route/module registration tests for runtime diagnostics settings in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/module.test.tsx`

### Implementation for User Story 2

- [x] T024 [US2] Add backend runtime diagnostics settings models and effective policy resolver in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Core/Models/RuntimeDiagnosticsSettings.cs`
- [x] T025 [US2] Add backend runtime diagnostics settings endpoint handlers in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Api/Handlers`
- [x] T026 [US2] Add Studio runtime diagnostics settings query/mutation helpers in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/api/workflows.ts`
- [x] T027 [US2] Add Workflows runtime diagnostics settings page in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/RuntimeDiagnosticsSettingsPage.tsx`
- [x] T028 [US2] Register the Workflows runtime diagnostics route and nav item in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/module.tsx`
- [x] T029 [US2] Add token-governed settings page styles in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/styles.css`

**Checkpoint**: User Story 2 is functional and independently testable.

---

## Phase 5: User Story 3 - Govern Unsafe Payloads And Sensitive Values (Priority: P3)

**Goal**: Unsafe full payload capture is gated, sensitive values are redacted, and permission-hidden evidence does not leak values.

**Independent Test**: Cap Host Policy below Full Payload, mark values sensitive, and verify API/UI redacts or hides evidence while still showing safe diagnostic shape.

### Tests for User Story 3

- [x] T030 [P] [US3] Add backend Host Policy cap and Full Payload gating tests in `/Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Workflows/Runtime/Tests/RuntimeDiagnosticsSettingsApiTests.cs`
- [x] T031 [P] [US3] Add backend sensitive-name redaction tests in `/Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Workflows/Runtime/Tests/RuntimeDiagnosticSnapshotTests.cs`
- [x] T032 [P] [US3] Add Studio redaction, truncation, and permission-hidden rendering tests in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/workflowActivityExecutionDetails.test.tsx`

### Implementation for User Story 3

- [x] T033 [US3] Enforce Host Policy caps and Full Payload permission state in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Core/Models/RuntimeDiagnosticsSettings.cs`
- [x] T034 [US3] Enforce sensitive-source, sensitive-type, and sensitive-name redaction markers in `/Users/sipke/Projects/Elsa/elsa-foundation/src/Elsa/Workflows/Runtime/Services/DefaultDiagnosticSnapshotFactory.cs`
- [x] T035 [US3] Render redaction, truncation, unsupported, error, malformed, and permission-hidden nodes in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`
- [x] T036 [US3] Disable or cap Full Payload controls and show limitation reasons in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/RuntimeDiagnosticsSettingsPage.tsx`

**Checkpoint**: User Story 3 is functional and independently testable.

---

## Phase 6: User Story 4 - Prepare Reference-Based Evidence (Priority: P4)

**Goal**: Runtime Payload Reference leaves are documented and safely renderable, while production capture does not emit them until a provider/resolver slice exists.

**Independent Test**: Render fixture evidence containing a Runtime Payload Reference leaf and verify it displays safe metadata with unavailable resolution.

### Tests for User Story 4

- [x] T037 [P] [US4] Add Studio Runtime Payload Reference fixture rendering tests in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/workflowActivityExecutionDetails.test.tsx`
- [x] T038 [P] [US4] Add backend test proving generic snapshot capture does not emit payload reference leaves in `/Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Workflows/Runtime/Tests/RuntimeDiagnosticSnapshotTests.cs`

### Implementation for User Story 4

- [x] T039 [US4] Add Runtime Payload Reference Diagnostic Snapshot leaf type to `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/workflowTypes.ts`
- [x] T040 [US4] Render Runtime Payload Reference leaves as safe unavailable links/actions in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`
- [x] T041 [US4] Document deferred reference provider/resolver issue seeds in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/specs/004-runtime-value-evidence/spec.md`

**Checkpoint**: User Story 4 is functional and independently testable with fixtures.

---

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Validation, documentation, and release readiness.

- [x] T042 [P] Run backend tests from `/Users/sipke/Projects/Elsa/elsa-foundation` with `dotnet test`
- [x] T043 [P] Run Studio Workflows tests from `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio` with `pnpm --filter @elsa-workflows/studio-workflows test`
- [x] T044 Run Studio lint from `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio` with `pnpm lint`
- [x] T045 Validate quickstart scenarios in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/specs/004-runtime-value-evidence/quickstart.md`
- [x] T046 Run self review and address actionable issues in changed files across both repositories
- [x] T047 Update checked task status in `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/specs/004-runtime-value-evidence/tasks.md`
- [ ] T048 Open pull request for `/Users/sipke/.codex/worktrees/3255/elsa-foundation-studio` changes and merge it after checks pass

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational; MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational and can be implemented independently, but full validation benefits from US1 capture evidence.
- **User Story 3 (Phase 5)**: Depends on US1 and US2 policy/settings surfaces.
- **User Story 4 (Phase 6)**: Depends on US1 snapshot renderer and can use fixtures.
- **Polish**: Depends on implemented stories.

### Parallel Opportunities

- T002, T003, and T004 can run in parallel.
- T006, T007, T008, and T009 can run in parallel after T005.
- US1 tests T010-T013 can be written in parallel.
- US2 tests T021-T023 can be written in parallel.
- US3 tests T030-T032 can be written in parallel.
- US4 tests T037-T038 can be written in parallel.
- Backend validation T042 and Studio validation T043 can run in parallel after implementation.

## Parallel Example: User Story 1

```bash
Task: "Add backend snapshot bounds/redaction tests in /Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Workflows/Runtime/Tests/RuntimeDiagnosticSnapshotTests.cs"
Task: "Update backend default policy tests in /Users/sipke/Projects/Elsa/elsa-foundation/tests/Elsa/Workflows/Runtime/Tests/RuntimeDiagnosticsHistoryIncidentContractTests.cs"
Task: "Add Studio rendering tests for input/output Diagnostic Snapshot evidence in /Users/sipke/.codex/worktrees/3255/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/workflowActivityExecutionDetails.test.tsx"
```

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete User Story 1.
3. Validate activity input/output Diagnostic Snapshot evidence independently.
4. Continue with settings and governance stories.

### Incremental Delivery

1. Build shared backend/client vocabulary.
2. Deliver default Diagnostic Snapshot capture and rendering.
3. Add Studio-managed settings and effective Host Policy display.
4. Add unsafe payload governance and richer marker handling.
5. Add reserved reference leaf rendering for future provider-backed work.

## Notes

- `[P]` tasks touch different files or can be completed without waiting for another incomplete task in the same phase.
- Cross-repository backend tasks are necessary because Studio cannot render runtime values that the backend does not capture or expose.
- Runtime Payload Reference provider creation, resolution endpoints, reveal/download permissions, audit records, storage integrations, retention policy, and per-workflow overrides remain deferred issue seeds.

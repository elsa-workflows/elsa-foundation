# Tasks: Variable lifecycle inspection

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/inspection-api.md](contracts/inspection-api.md), [quickstart.md](quickstart.md)

**Prerequisites**: the spec is approved; spec 1221's write history (its US1, US3, US6 and the write-record data) is
merged into the initiative branch. Tests use xUnit only; risk-based (ADR 0080).

**Format**: `[ID] [P?] [Story] Description` — `[P]` can run in parallel (different files, no dependency).

## Phase 1: Setup

- [ ] T001 Recheck Q1 and Q9 against spec 1221's merged implementation: does `VariableWriteRecord` carry an instance
  commit sequence, and does `ModifyVariable` leave a write record? Drop T004/T008 parts that already exist; record the
  result in the PR description.
- [ ] T002 Verify that every commit path (inline drain, coalesced segment, background result, alteration, recovery)
  goes through `RuntimeCheckpointCommitter` under the instance fence (research R-1). List any path that does not.

## Phase 2: Foundation (blocks all stories)

- [ ] T003 Add `LastCommitSequence` to the workflow execution state and advance it under the fence in
  `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Checkpoints/RuntimeCheckpointCommitter.cs`; in the
  coalescing drain number each logical checkpoint before folding (`Services/Coalescing/`).
- [ ] T004 Add optional `CommitSequence` to `VariableWriteRecord` and stamp it at every write site
  (`Essentials/src/Workflows/Runtime/Core/Models/VariableLifecycleModels.cs`); keep positional signatures.
- [ ] T005 Add optional lifecycle stamps (`ActivityLifecycleStamp`) to `ActivityExecutionState` for started, suspended,
  resumed, completed, faulted and superseded, with commit sequence, branch/path and evaluation id.
- [ ] T006 Tests: commit sequence is total and strictly increasing per instance across inline, coalesced, background and alteration
  commits; `0 → 1 → 0` in one buffered segment keeps two sequences; older records read back with members absent.
  In `…/Tests/VariableLifecycleInspection/CommitSequenceTests.cs`.

## Phase 3: User Story 1 — who wrote a variable, which write won (P1) 🎯 MVP

- [ ] T007 [US1] Read model in `Essentials/src/Workflows/Runtime/Api/Services/VariableLifecycleInspection.cs`: page the
  instance's activity states, flatten write records, merge alteration records, order by commit sequence (ties: frame
  revision, record order), mark the current value, map value states (research R-4), mark legacy order approximate.
- [ ] T008 [US1] `ModifyVariableAlterationHandler`: write a `VariableWriteRecord` (source `Alteration`) on the
  alteration job outcome with its commit sequence (Q9).
- [ ] T009 [P] [US1] Views in `Essentials/src/Workflows/Runtime/Api/Models/VariableLifecycleViews.cs`; register in
  `WorkflowsRuntimeJsonContext`.
- [ ] T010 [US1] Endpoints `…/variables` and `…/variable-writes` with filters and paging
  (`Essentials/src/Workflows/Runtime/Api/Endpoints/Instances/{Variables,VariableWrites}/Endpoint.cs`), existing
  inspection permission.
- [ ] T011 [US1] Tests: order and current value for three writes; unchanged value not listed; `0 → 1 → 0`; `omitted`
  vs captured `null`; `redacted` without sensitive permission; `ModifyVariable` in its position; legacy instance shows
  "no history recorded".

## Phase 4: User Story 2 — lost updates and concurrent overwrites (P1)

- [ ] T012 [US2] Record the arrivals that satisfied a join on the scheduled invocation's provenance
  (`ActivitySchedulingProvenance`, join-completion scheduling site); optional member.
- [ ] T013 [US2] Happens-before walk and concurrent-overwrite marking in the read model (research R-3); bounded by the
  overwritten write's commit sequence.
- [ ] T014 [US2] Instance details: `LostUpdateWarningCount`, `ConcurrentOverwriteCount`
  (`Api/Handlers/WorkflowInstanceDetailsService.cs`, `WorkflowInstanceDetailsView`).
- [ ] T015 [US2] Tests: two branches `count + 1` → one lost update naming both writers and branches and the overwritten
  value's state; blind `Set` on a third branch → concurrent overwrite, not lost update; sequential and joined-then-write
  → none; self-overwrite across suspension → none; retry attempts of one logical invocation → none; re-execution can
  overwrite its predecessor; `Fault` → refused write listed, current value unchanged.

## Phase 5: User Story 3 — commit-ordered journal (P2)

- [ ] T016 [US3] Journal in the read model: group transitions and writes by commit sequence; page by sequence.
- [ ] T017 [US3] Endpoint `…/journal`.
- [ ] T018 [US3] Tests: every commit once and in order; coalesced segment keeps logical order; paging continues
  exactly; refreshed background evaluation linked to the superseded one (once spec 1221's US5 lands; otherwise skipped
  with a reason).

## Phase 6: User Story 4 — compare two instances (P2)

- [ ] T019 [US4] Comparison: same executable identity only (else `409 differentExecutable`); match by node id and
  invocation origin path; first diverging commit; per variable final digest and write sequence; no values; `Partial`
  when one side runs.
- [ ] T020 [US4] Endpoint `…/compare/{otherWorkflowExecutionId}`.
- [ ] T021 [US4] Tests: identical runs → no divergence; reordered branches → first divergence and differing variable;
  different versions refused; no value in the response for any caller.

## Phase 7: User Story 5 — current variable state (P3)

- [ ] T022 [US5] Variable summary from current frames plus last history entry; closed-scope (graph-row) variables shown
  as unversioned.
- [ ] T023 [US5] Tests: root and container variables; legacy instance at version 0 with "no history recorded".

## Phase 8: Polish

- [ ] T024 [P] Inspection section in `docs/runtime-variable-lifecycle.md`: the views, the concurrent-overwrite rule, value
  states, and what Studio can build on.
- [ ] T025 Measurement SC-004 (10,000 activity executions, first page) and SC-006 (stamp overhead against spec 1221's
  benchmark); if SC-004 fails, record it and propose the projection fallback (research R-2) on the PR.
- [ ] T026 Run [quickstart.md](quickstart.md) on the Workbench; access tests for SC-005.
- [ ] T027 Hand the API contract to elsa-studio for the presentation (instance diagnostics badge, history, journal,
  comparison views).

## Dependencies

- Phase 2 blocks everything. US1 before US2 (US2 extends the read model). US3 and US4 need Phase 2 and US1's read model;
  US4 uses US3's journal. US5 needs only Phase 2.
- T012 is the only change outside the inspection path besides Phase 2 and T008.

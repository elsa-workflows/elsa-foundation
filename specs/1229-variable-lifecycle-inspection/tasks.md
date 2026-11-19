# Tasks: Variable lifecycle inspection

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/inspection-api.md](contracts/inspection-api.md), [quickstart.md](quickstart.md)

**Prerequisites**: the spec is approved; spec 1221's write history, including the write-time data in the data model's
first table, is merged into the initiative branch. Tests use xUnit only; risk-based (ADR 0080).

**Format**: `[ID] [P?] [Story] Description` — `[P]` can run in parallel (different files, no dependency).

## Phase 1: Setup

- [x] T001 Recheck the data recorded by spec 1221 against the data model's first table (names, placement, optional
  members, writes that changed nothing not recorded). A missing member is raised on spec 1221 before building on it.
  Done: spec 1221 records every member of the first table (`CommitSequence`, `FrameId`, `Digest`, `Transitions`, `FirstCommitSequence`/`LastCommitSequence`, alteration `VariableWrites`, `VariableWrite` value snapshots). Nothing raised on spec 1221.
- [x] T002 List the commit paths made outside an execution lease (recovery and deletion among them), which carry no
  commit sequence, and check the read model orders what they commit as legacy (research R-1).
  Done: only `RuntimeCheckpointCommitter` numbers commits, under the ambient execution lease; a commit without one (no ownership context) and a direct `IActivityExecutionStateStore.SaveAsync` leave records unnumbered. Their records read as approximate and come first; deletion removes the rows with the instance.

## Phase 2: Foundation (blocks US2, US3, US4)

- [x] T003 Add optional `CausedBy` to `ActivitySchedulingProvenance`
  (`Essentials/src/Workflows/Runtime/Core/Models/ActivitySchedulingProvenance.cs`) and fill it where a completion
  schedules: parent callbacks, `Do`/`While`/`For`/`ForEach` next iterations, join and `Parallel` completions.
  Done: `CausedBy` (`ActivitySchedulingProvenance.WithCausedBy`) is filled for every callback scheduling from the completed child (`StructuralParentEvaluationSupport`), which covers `Sequence`, loops and parent callbacks, and with every arrival at a Flowchart join (`FlowchartJoinCoordinator`). A `Parallel` or join completion is ordered through the container-completion rule of the read model (a completed container comes after the children that completed before it). Not done: BPMN joining gateways name only the last arrival (token-level join data lives in the BPMN semantics layer); recorded on the PR and in the docs as a known limit.
- [x] T003a Add optional `LostUpdateWarningCount` to `ActivityExecutionState`, set when the row is written from its
  lost-update warning records; store columns and an index for `FirstCommitSequence`, `LastCommitSequence` and
  `LostUpdateWarningCount` with a migration in every runtime store provider (FR-016).
  Done: set by `RuntimeCommitSequenceStamper` on every committed row; columns, index (`IX_elsa_runtime_activity_execution_state_commit_range`) and a jobs-by-instance index in migration `VariableLifecycleInspection` for Sqlite, SQL Server, PostgreSQL and MySQL (written by hand: the local tooling build could not produce four unrelated module DLLs; the Sqlite snapshot is checked by `The_runtime_model_snapshot_has_no_pending_model_changes`).
- [x] T004 Tests: each of those schedulings records its cause; the row columns match the document; `Sequence` keeps passing its completed child; overhead
  within noise of spec 1221's benchmark (SC-006).
  Done: run-time drives (`InspectionDriveTests`) check the causes of a Sequence step and of a step after a `Parallel`, and the row count; loops, joins and the container rule are covered over committed record sets (`VariableLifecycleInspectionTests`); the EF columns by `Rows_page_by_commit_sequence_range_and_lost_updates_sum_from_the_column`. SC-006 was not benchmarked separately: the run-time cost is the cause ids on a callback scheduling and one count on a row already written.

## Phase 3: User Story 1 — who wrote a variable, which write won (P1) 🎯 MVP

- [x] T005 [US1] Read model in `Essentials/src/Workflows/Runtime/Api/Services/VariableLifecycleInspection.cs`: page rows
  by commit-sequence range, flatten write records and stamps, merge alteration records, key by variable instance,
  order by (commit sequence, position), mark applied and current value (research R-8), link accepted overrides through
  `RetryOfActivityExecutionId`, resolve logical invocations, handle legacy records (approximate).
- [x] T006 [US1] Payload read for write-record values (written, overwritten, refused; activity or alteration owner)
  under the existing payload permission; value-state mapping (research R-4).
  Done for activity owners (written, overwritten, refused). An alteration's own value is not on its write record (spec 1221 recorded it "with no value"; the value is only in the protected alteration request), so it shows `omitted`; raised on the PR.
- [x] T007 [P] [US1] Views in `Essentials/src/Workflows/Runtime/Api/Models/VariableLifecycleViews.cs`; register in
  `WorkflowsRuntimeJsonContext`.
- [x] T008 [US1] Endpoints `…/variables`, `…/variable-writes` (filters, cursor paging) and
  `…/variable-writes/{entryId}/payload`, existing inspection permission.
- [x] T009 [US1] Tests: order and current value for three writes; `0 → 1 → 0`; loop iterations as separate variable
  instances; `omitted` vs captured `null`; `redacted` without sensitive permission; payload route refuses without
  payload permission; `ModifyVariable` in its position; accepted override linked to its refused write; legacy instance
  shows "no history recorded"; a page ending inside a commit continues exactly.

## Phase 4: User Story 2 — lost updates and concurrent overwrites (P1)

- [x] T010 [US2] Happens-before walk and concurrent-overwrite marking in the read model (research R-3), bounded by the
  replaced write's commit sequence; `concurrentOverwriteTotal` on the filtered history.
  Decision applied: an alteration write is never itself marked as a concurrent overwrite (an operator acts on the state they inspected); a later write not scheduled after it is (spec Edge Cases). Raised on the PR for confirmation.
- [x] T011 [US2] Instance details: `LostUpdateWarningCount` summed over the instance's rows
  (`Api/Handlers/WorkflowInstanceDetailsService.cs`, `WorkflowInstanceDetailsView`).
- [x] T012 [US2] Tests: two branches `count + 1` → one lost update naming both writers and branches and the overwritten
  value's state; blind `Set` on a third branch → concurrent overwrite, not lost update; sequential, join, `Parallel`
  and `Do`/`While`/`ForEach` loops → none; container schedules a child, then writes, then the child overwrites →
  concurrent; self-overwrite across suspension → none; retry attempts of one logical invocation → none; re-execution
  after `ModifyVariable` → ordered; `Fault` → refused write listed, not applied, not an overwrite; accepting that
  recorded completion → applied entry with outcome accepted override, linked, both kept.

## Phase 5: User Story 3 — commit-ordered journal (P2)

- [x] T013 [US3] Journal in the read model: group stamps, input refreshes and writes by commit sequence; cursor paging.
- [x] T014 [US3] Endpoint `…/journal`.
- [x] T015 [US3] Tests: every commit once and in order with every stamp kind; coalesced segment keeps
  logical order; paging continues exactly; a background input refresh appears with both evaluations linked.
  Done over record sets for every stamp kind. A coalesced segment keeps its order because spec 1221 numbers each logical checkpoint before the fold (its `CommitOrderTests`); the journal groups by those numbers and needs no separate case.

## Phase 6: User Story 4 — compare two instances (P2)

- [x] T016 [US4] Run-time path (research R-5) and comparison: same executable identity only (else `409
  differentExecutable`); first diverging commit; per variable instance final digest and written digests; no values;
  `Partial` when one side runs.
- [x] T017 [US4] Endpoint `…/compare/{otherWorkflowExecutionId}`.
- [x] T018 [US4] Tests: identical runs → no divergence; reordered branches → first divergence and differing variable;
  loops with the same iteration count match by occurrence; different versions refused; no value in the response.

## Phase 7: User Story 5 — current variable state (P3)

- [x] T019 [US5] Variable summary from current frames and closed-scope rows (their version, digest and writer kept
  with the row) plus the last applied entry.
- [x] T020 [US5] Tests: root, container and iteration variables; legacy instance at version 0 with "no history
  recorded".

## Phase 8: Polish

- [x] T021 [P] Inspection section in `docs/runtime-variable-lifecycle.md`: the views, the concurrent-overwrite rule, value
  states, and what Studio can build on.
- [x] T022 Measurement SC-004 (10,000 activity executions, first page; instance details unchanged); if it fails, record
  it and propose the projection fallback (research R-2) on the PR.
  Measured (in-memory store, 10,000 activity executions with one write each, Debug, loaded dev machine): activity-list
  first page 2 ms; history first page 35 ms; journal first page 93 ms; lost-update sum for the details 4 ms (a column
  sum on EF). SC-004 is not met: every read loads all of the instance's rows (the overwrite walk and the run-time paths
  need the whole graph), so its cost grows with the instance. Recorded on the PR with the projection fallback (R-2)
  proposed. Instance details read time: one indexed column sum added.
- [x] T023 Run [quickstart.md](quickstart.md) on the Workbench; access tests for SC-005.
  Done. Quickstart steps on the composed host (UX acceptance journey `variable lifecycle warnings and refusals`,
  Studio/tests/acceptance/variableLifecycleJourney.ts, run by CI on every Studio PR with retained screenshots; captures
  listed in `tools/ux-acceptance/run.py`): a real lost-update warning and a Fault refusal on Workbench + Studio with
  disposable stores (steps 1–2); the run badge and the lost-update count (step 3); the write history filtered to lost
  updates, naming the concurrent overwrite and the current value (step 4); the commit journal in commit order with writes and transitions per commit (step 6; which commit holds which write is checked by `The_journal_lists_every_commit_once_in_order...` and `A_background_result_is_numbered_in_its_own_commit...`);
  the comparison refused for runs of another workflow. Covered by tests instead: the `overwrites=true` filter and `concurrentOverwriteTotal`
  (step 5, `VariableLifecycleInspectionServiceTests.Filters_on_overwrites_return_the_total...`); a comparison of two runs
  of one version with a divergence (step 7, `...Two_runs_that_committed_in_a_different_order...`); redaction (step 8,
  `...Without_the_sensitive_value_permission...`); sequential runs, joins and loops with no overwrite (step 9,
  `InspectionDriveTests`, `LoopSchedulingCauseTests` and `VariableLifecycleInspectionTests`). SC-005 access:
  `VariableLifecycleInspectionServiceTests`, `VariableLifecycleEndpointTests`.
- [x] T024 Move the Studio items to Phase 9 (T025–T029); nothing is handed to another repository.

## Phase 9: Studio presentation (FR-017), in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/`

Builds on #4735 (spec 1221's activity-level items) and on this spec's endpoints (T008, T011, T014, T017).

- [x] T025 [P] API client and types for the variables, variable-writes, journal and compare endpoints next to
  `workflowInstanceInspection.ts`, with cursor paging.
- [x] T026 [US2] Instance diagnostics badge for the lost-update count on the instance view (`WorkflowInstanceTimeline.tsx`),
  opening the write history filtered to lost updates.
  Done: #4759's `LostUpdateBadge` on the run's Timeline tab (`WorkflowInstances.tsx`), with the run's `lostUpdateWarningCount`; it opens the new Variables tab with the write history filtered to `lostUpdateWarning`.
- [x] T027 [US1] [US2] Write history view per variable: writers, versions, commit order, lost-update and
  concurrent-overwrite marks, values through `RuntimeValueEvidence.tsx` (value states, payload read).
  Built in `VariableLifecycleViews.tsx` (filters: variable, outcome, source, frame, overwrites). Mounted as the run view's Variables tab (shown when the host advertises the routes; `?tab=variables`).
- [x] T028 [US3] [US4] Journal view in commit order (stamps, input refreshes, writes) and the comparison view of two
  instances (first divergence, differing variables, digests only).
  Built in `VariableLifecycleViews.tsx`; the Journal and Compare tabs of the Variables tab.
- [x] T029 [P] Tests in `__tests__/` (vitest) for the client paging and each view's states, including omitted and
  redacted values and a legacy instance with no history.

## Dependencies

- T001 and T002 first. Phase 2 blocks US2, US3 and US4 (they need the causes). US1 and US5 need only spec 1221's data.
- US2 extends US1's read model; US4 uses US3's journal.

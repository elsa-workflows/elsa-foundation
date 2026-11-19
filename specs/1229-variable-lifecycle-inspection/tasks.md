# Tasks: Variable lifecycle inspection

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/inspection-api.md](contracts/inspection-api.md), [quickstart.md](quickstart.md)

**Prerequisites**: the spec is approved; spec 1221's write history, including the write-time data in the data model's
first table, is merged into the initiative branch. Tests use xUnit only; risk-based (ADR 0080).

**Format**: `[ID] [P?] [Story] Description` — `[P]` can run in parallel (different files, no dependency).

## Phase 1: Setup

- [ ] T001 Recheck the data recorded by spec 1221 against the data model's first table (names, placement, optional
  members, writes that changed nothing not recorded). A missing member is raised on spec 1221 before building on it.
- [ ] T002 List the commit paths made outside an execution lease (recovery and deletion among them), which carry no
  commit sequence, and check the read model orders what they commit as legacy (research R-1).

## Phase 2: Foundation (blocks US2, US3, US4)

- [ ] T003 Add optional `CausedBy` to `ActivitySchedulingProvenance`
  (`Essentials/src/Workflows/Runtime/Core/Models/ActivitySchedulingProvenance.cs`) and fill it where a completion
  schedules: parent callbacks, `Do`/`While`/`For`/`ForEach` next iterations, join and `Parallel` completions.
- [ ] T003a Add optional `LostUpdateWarningCount` to `ActivityExecutionState`, set when the row is written from its
  lost-update warning records; store columns and an index for `FirstCommitSequence`, `LastCommitSequence` and
  `LostUpdateWarningCount` with a migration in every runtime store provider (FR-016).
- [ ] T004 Tests: each of those schedulings records its cause; the row columns match the document; `Sequence` keeps passing its completed child; overhead
  within noise of spec 1221's benchmark (SC-006).

## Phase 3: User Story 1 — who wrote a variable, which write won (P1) 🎯 MVP

- [ ] T005 [US1] Read model in `Essentials/src/Workflows/Runtime/Api/Services/VariableLifecycleInspection.cs`: page rows
  by commit-sequence range, flatten write records and stamps, merge alteration records, key by variable instance,
  order by (commit sequence, position), mark applied and current value (research R-8), link accepted overrides through
  `RetryOfActivityExecutionId`, resolve logical invocations, handle legacy records (approximate).
- [ ] T006 [US1] Payload read for write-record values (written, overwritten, refused; activity or alteration owner)
  under the existing payload permission; value-state mapping (research R-4).
- [ ] T007 [P] [US1] Views in `Essentials/src/Workflows/Runtime/Api/Models/VariableLifecycleViews.cs`; register in
  `WorkflowsRuntimeJsonContext`.
- [ ] T008 [US1] Endpoints `…/variables`, `…/variable-writes` (filters, cursor paging) and
  `…/variable-writes/{entryId}/payload`, existing inspection permission.
- [ ] T009 [US1] Tests: order and current value for three writes; `0 → 1 → 0`; loop iterations as separate variable
  instances; `omitted` vs captured `null`; `redacted` without sensitive permission; payload route refuses without
  payload permission; `ModifyVariable` in its position; accepted override linked to its refused write; legacy instance
  shows "no history recorded"; a page ending inside a commit continues exactly.

## Phase 4: User Story 2 — lost updates and concurrent overwrites (P1)

- [ ] T010 [US2] Happens-before walk and concurrent-overwrite marking in the read model (research R-3), bounded by the
  replaced write's commit sequence; `concurrentOverwriteTotal` on the filtered history.
- [ ] T011 [US2] Instance details: `LostUpdateWarningCount` summed over the instance's rows
  (`Api/Handlers/WorkflowInstanceDetailsService.cs`, `WorkflowInstanceDetailsView`).
- [ ] T012 [US2] Tests: two branches `count + 1` → one lost update naming both writers and branches and the overwritten
  value's state; blind `Set` on a third branch → concurrent overwrite, not lost update; sequential, join, `Parallel`
  and `Do`/`While`/`ForEach` loops → none; container schedules a child, then writes, then the child overwrites →
  concurrent; self-overwrite across suspension → none; retry attempts of one logical invocation → none; re-execution
  after `ModifyVariable` → ordered; `Fault` → refused write listed, not applied, not an overwrite; accepting that
  recorded completion → applied entry with outcome accepted override, linked, both kept.

## Phase 5: User Story 3 — commit-ordered journal (P2)

- [ ] T013 [US3] Journal in the read model: group stamps, input refreshes and writes by commit sequence; cursor paging.
- [ ] T014 [US3] Endpoint `…/journal`.
- [ ] T015 [US3] Tests: every commit once and in order with every stamp kind; coalesced segment keeps
  logical order; paging continues exactly; a background input refresh appears with both evaluations linked.

## Phase 6: User Story 4 — compare two instances (P2)

- [ ] T016 [US4] Run-time path (research R-5) and comparison: same executable identity only (else `409
  differentExecutable`); first diverging commit; per variable instance final digest and written digests; no values;
  `Partial` when one side runs.
- [ ] T017 [US4] Endpoint `…/compare/{otherWorkflowExecutionId}`.
- [ ] T018 [US4] Tests: identical runs → no divergence; reordered branches → first divergence and differing variable;
  loops with the same iteration count match by occurrence; different versions refused; no value in the response.

## Phase 7: User Story 5 — current variable state (P3)

- [ ] T019 [US5] Variable summary from current frames and closed-scope rows (their version, digest and writer kept
  with the row) plus the last applied entry.
- [ ] T020 [US5] Tests: root, container and iteration variables; legacy instance at version 0 with "no history
  recorded".

## Phase 8: Polish

- [ ] T021 [P] Inspection section in `docs/runtime-variable-lifecycle.md`: the views, the concurrent-overwrite rule, value
  states, and what Studio can build on.
- [ ] T022 Measurement SC-004 (10,000 activity executions, first page; instance details unchanged); if it fails, record
  it and propose the projection fallback (research R-2) on the PR.
- [ ] T023 Run [quickstart.md](quickstart.md) on the Workbench; access tests for SC-005.
- [ ] T024 Move the Studio items to Phase 9 (T025–T029); nothing is handed to another repository.

## Phase 9: Studio presentation (FR-017), in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/`

Builds on #4735 (spec 1221's activity-level items) and on this spec's endpoints (T008, T011, T014, T017).

- [ ] T025 [P] API client and types for the variables, variable-writes, journal and compare endpoints next to
  `workflowInstanceInspection.ts`, with cursor paging.
- [ ] T026 [US2] Instance diagnostics badge for the lost-update count on the instance view (`WorkflowInstanceTimeline.tsx`),
  opening the write history filtered to lost updates.
- [ ] T027 [US1] [US2] Write history view per variable: writers, versions, commit order, lost-update and
  concurrent-overwrite marks, values through `RuntimeValueEvidence.tsx` (value states, payload read).
- [ ] T028 [US3] [US4] Journal view in commit order (stamps, input refreshes, writes) and the comparison view of two
  instances (first divergence, differing variables, digests only).
- [ ] T029 [P] Tests in `__tests__/` (vitest) for the client paging and each view's states, including omitted and
  redacted values and a legacy instance with no history.

## Dependencies

- T001 and T002 first. Phase 2 blocks US2, US3 and US4 (they need the causes). US1 and US5 need only spec 1221's data.
- US2 extends US1's read model; US4 uses US3's journal.

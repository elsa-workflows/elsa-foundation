# Tasks: Variable lifecycle model

**Input**: Design documents from `specs/1221-variable-lifecycle-model/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Status**: provisional. These tasks are the implementation plan for when ADR 0092 is agreed and the initiative branch
lands; nothing is implemented before then. They also serve the provisional spec's purpose — the reproduction drives and
the no-gap PoC test whether ADR 0092 works in code, and findings flow back to #3411.

**Tests / cadence**: Elsa uses xUnit, no FluentAssertions (constitution). The two reproduction drives (US1, US2) and the
no-gap proof (US4) are **test-first** — the ADR's *Required evidence* needs them failing before and passing after. Other
tests follow §2.23 (behaviour tests for logic-bearing implementations; feature-registration tests only where wiring
carries logic) at the application cadence (§2.21.2). No new CI tier (existing Full tier runs them).

**Implementation status (first implementation PR, #4316):** the ticked tasks are done there. Open, with the reason:

- **T015, T016 (session reads, self-write on suspend):** no variable session API exists on the initiative branch (ADR
  0090's session is not implemented), so there is no session read or session write to instrument. The read-set model
  keeps one entry per variable (oldest read wins, the spec's oldest-in-use rule); session reads and own-write base
  advancement land with the session API.
- **T017–T021 (US2 operator actions):** a second implementation PR. Finding for an amendment: in this code
  `InvocationId` is the activity execution id, so a retry that "keeps the `InvocationId`" (ADR 0092 decision 8) must
  re-run the faulted row in place instead of superseding it, which changes the `RetryFaultedActivity/1` supersession
  lineage of ADR 0045/0049. A re-execution with fresh inputs must also re-derive the iteration frames of a loop body.
- **T010a, T029–T032 (background branches, admission refresh):** with US5 in a later implementation PR. The stale-base
  drives of T010/T011 cover the commit rule a background result meets.
- **T024a:** spec 266's unordered-writer warning is not implemented on the branch yet; the pointer to the `Fault`
  opt-in goes in with S-5's implementation.
- **T028, T042:** coordinator steps on #3411; the PoC result is in #4316's description.
- **T039:** partly done (capture, alteration and buffered-segment records are covered); intrinsic and background-result
  records, and restoring an overwritten value, are covered with US2/US5.
- **T041:** after the remaining stories.

## Phase 1: Setup

- [x] T001 Confirm test projects reference the runtime projects under change (`Essentials/tests/Workflows/Runtime/Tests`, `Essentials/tests/Activities/Runtime/Tests`) and add a `VariableLifecycle` test folder in each.
- [x] T002 [P] Add a large-payload / hot-loop benchmark harness stub under `Essentials/tests/Workflows/Runtime/Tests/VariableLifecycle/Benchmarks` for SC-005 / Open question B (no thresholds yet).

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: the data model and the commit rule every story depends on.

- [x] T003 Add per-variable `Version` and `Digest` to `VariableFrameState` in `Essentials/src/Workflows/Runtime/Core/Models/VariableFrameState.cs`, beside the frame revision; mirror in the row store.
- [x] T004 [P] Add `ReadSetEntry`, `InputReadSet` and `SessionReads` types in `Essentials/src/Workflows/Runtime/Core/Models/` (per data-model.md).
- [x] T005 [P] Add the `WriteHistoryRecord` type (mandatory metadata; optional value; conditional operational data) in `Essentials/src/Workflows/Runtime/Core/Models/`.
- [x] T006 Add the digest computation over ADR 0091 canonical form (one per applied write) in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/VariableFrameFactory.cs`.
- [x] T007 Implement `VariableCommitRule` (blind / current / returned-value / lost-update) in `Essentials/src/Workflows/Runtime/Core/` per contracts/commit-rule.md.
- [x] T008 [P] Behaviour tests for `VariableCommitRule`: blind applies; base==current applies; `0→1→0` applies + records mismatch; stale+changed is a lost update; unchanged value writes nothing but two 0→1 writers do not hide the second. In `.../Tests/VariableLifecycle/CommitRuleTests.cs`.
- [x] T009 Document/schema version increment + upcast for `VariableFrameState` and `WriteHistoryRecord` (missing version → 0 / no base); test old-record read-back.

**Checkpoint**: data model and commit rule exist and are unit-tested; no write site uses them yet.

---

## Phase 3: User Story 1 — Parallel read-modify-write is never silent (Priority: P1) 🎯 MVP

**Goal**: a parallel input+capture `count++` either totals correctly (inline: `2`) or applies with a visible,
repairable lost-update warning (background, suspension), never silently loses an update.
**Independent test**: the reproduction drive below fails before and passes after. Run Phase 6's PoC (T025) first: the
default of FR-011 depends on it (FR-013a).

- [x] T010 [US1] **(test-first)** Reproduction drive: `Parallel` of two inline `External` leaves taking `count` and capturing `count + 1`; assert silent `1` today, `2` after (with US4). In `.../Tests/VariableLifecycle/ParallelLostUpdateTests.cs`.
- [ ] T010a [P] [US1] **(test-first)** Drive: the same with two background branches → `1` with a lost-update warning on the second commit (instance diagnostic + write-history entry naming both writers, overwritten value kept).
- [x] T011 [P] [US1] **(test-first)** Drive: two branches read 0 and write 1 → second is a lost update (warning), not a no-op; `0 → 1 → 0` in default mode warns.
- [x] T012 [US1] Capture the input read set during materialization in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/WorkflowStartActivitySchedulerWorkHandler.cs`; commit it with the input snapshot.
- [x] T013 [US1] Apply the commit rule and base at the capture site in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/RuntimeOutputCaptureProjection.cs`.
- [x] T014 [P] [US1] Apply the commit rule at the intrinsic site (Set/Merge/Reduce) in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/WorkflowIntrinsicExecutor.cs`.
- [ ] T015 [US1] Record session reads per attempt in the variable session read path; implement base selection (immutable input bases; oldest-in-use tie-break) per data-model.md and R-4.
- [ ] T016 [US1] Self-write handling: an invocation's applied write advances its own base for X (session change on suspend + capture on complete, ADR 0090 §6 order); test no self-conflict.

**Checkpoint**: US1 reproduction passes; lost updates are never silent across `External`; blind writes unaffected.

---

## Phase 4: User Story 2 — Operator correction reaches the step (Priority: P1)

**Goal**: `ModifyVariable` corrections reach a re-executed step; retry keeps the snapshot; accept applies without the body.

- [ ] T017 [US2] **(test-first)** Drive: `ModifyVariable` then re-execute uses the corrected value; retry keeps `InvocationId` + snapshot. In `.../Tests/VariableLifecycle/OperatorActionsTests.cs`.
- [ ] T018 [US2] Split the three actions in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/ActivitySuccessorStaging.cs`: retry (same id + snapshot), re-execute/reschedule (new id + fresh inputs + new input read set), accept recorded completion.
- [ ] T019 [US2] Fix the current bug where an operator retry gets a new id with a copied snapshot; amend ADR 0045 §3 behavior (re-execute materializes fresh inputs).
- [ ] T020 [P] [US2] Add the owned-frame re-activation guard on the successor `Start` re-entry path in `WorkflowStartActivitySchedulerWorkHandler.cs`.
- [ ] T021 [US2] Implement accept-recorded-completion: revalidate state/scope/contracts/policies, apply the recorded transition atomically + duplicate-safely, refuse a faulted body, keep the conflict in history, no silent input refresh; test repeated acceptance does not double-schedule downstream.

**Checkpoint**: the three operator actions are distinct and tested; corrections reach re-executed steps.

---

## Phase 5: User Story 3 — Opt-in strict read-modify-write (Priority: P2)

- [x] T021a [US1] Default lost-update warning: on a stale base the write applies and an instance diagnostic (Studio) plus a write-history entry is raised, naming the variable, base and current versions and both writers, keeping the overwritten value; versions only, no digest in the read set.
- [x] T022 [US3] Add the `Fault` opt-in: `LostUpdateMode` per activity type (attribute) and per node (setting) in the runtime policy resolution, pinned in the executable; `Fault` nodes record stored digests in their read sets and use the exact check.
- [x] T023 [US3] Route a `Fault` lost update through the existing incident path with the diagnostic (variable, versions, writers); record the rejected completion. Reference `docs/runtime-fault-behavior.md`.
- [x] T024 [P] [US3] Behaviour tests: default warns and applies (incl. `0 → 1 → 0`); `Fault` (type and node) applies `0 → 1 → 0` with a recorded mismatch and faults a real lost update through the incident strategy. In `.../Tests/VariableLifecycle/LostUpdatePolicyTests.cs`.
- [ ] T024a [P] [US3] Point spec 266's unordered-writer publish warning to the `Fault` opt-in (FR-012a); coordinate with S-5 (#3286).

---

## Phase 6: User Story 4 — No inline gap for `External` (Priority: P1) — Open question A (run T025 first)

- [x] T025 [US4] **(test-first / PoC)** Prove no other branch's work item runs between an `External` inline leaf's input materialization, body and transition commit, with claim/fence/durability intact and no DB transaction around user code. In `.../Tests/VariableLifecycle/InlineNoGapTests.cs`.
- [x] T026 [US4] Extend the single-work-item dispatch to `External` leaves in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/ReplaySafeFusionDriver.cs` (and the start path), keeping `ActivityAttemptClaimed` claim/flush as intermediate commits.
- [x] T027 [P] [US4] Parallel test for `ReplaySafe` leaves (already fused) confirming the same no-gap guarantee.
- [ ] T028 [US4] **Flow finding back to #3411**: post the PoC result (feasible / not) as a comment on ADR 0092; if infeasible without weakening ADR 0032 R2, it reopens decision 10 and the default of decision 6 (FR-013a), before this spec is approved.

---

## Phase 7: User Story 5 — Background runs on fresh inputs (D-9) (Priority: P2)

- [ ] T029 [US5] Implement the admission refresh: compare input read set to current versions; on change, a new evaluation (same `InvocationId`, own sequence + idempotency keys) on the background admission path.
- [ ] T030 [P] [US5] Per-node refresh opt-out; a stale base then handled at commit.
- [ ] T031 [US5] Retain both snapshots linked with the reason; the worker's snapshot immutable thereafter; cover only Start→admission.
- [ ] T032 [P] [US5] Tests: refresh on changed input; opt-out meets the commit rule (warning by default, fault under `Fault`); superseded snapshot stays in history. In `.../Tests/VariableLifecycle/BackgroundRefreshTests.cs`.

---

## Phase 8: User Story 6 — Suspension keeps base (D-11) (Priority: P3)

- [x] T033 [US6] On resume: keep committed snapshot + input read set (own-write bases advanced), fresh session, no auto-refresh; an explicit stale-base write meets the commit rule and the node's mode.
- [x] T034 [P] [US6] `ModifyVariableAlterationHandler`: record the variable's version and apply only if unchanged (replace the root-frame revision check); a later stale-base write keeps the operator's value in the warning; a read alone never produces a lost update. In `.../Alterations/Handlers/ModifyVariableAlterationHandler.cs`.
- [x] T035 [P] [US6] Tests: resume keeps base; read-only change produces no lost update; a stale-base write warns by default and faults under `Fault`. In `.../Tests/VariableLifecycle/SuspensionTests.cs`.

---

## Phase 9: User Story 7 — Write history data (D-13) (Priority: P3) — Open question B

- [x] T036 [US7] Record a `WriteHistoryRecord` at every applied write and lost update (metadata always, with the outcome and both writers; value opt-in + retention; operational data — rejected completion, the value a warning overwrote — retained for recovery and repair).
- [x] T037 [US7] Preserve every intermediate write incl. `0→1→0` within one buffered segment; distinguish omitted/redacted/expired from `null`.
- [x] T038 [US7] **Measurement (Open question B)**: benchmark rows vs. derived-from-checkpoint under large payloads / hot loops; choose the physical storage; set the SC-005 overhead threshold. Post the result on #3411 (reviewer Q12).
- [ ] T039 [P] [US7] Tests: required metadata present for each write source; intermediate writes survive folding; operational data (rejected completion, overwritten value) available with inspection values off/expired; an overwritten value can be restored. In `.../Tests/VariableLifecycle/WriteHistoryTests.cs`.

---

## Phase 10: Polish & cross-cutting

- [x] T040 [P] Update `docs/runtime-fault-behavior.md` and the variable/lifecycle docs for the commit rule, the lost-update warning and the `Fault` opt-in, and the operator actions; document how to sequence branches or aggregate results, and when to opt in to `Fault` (§2.22).
- [ ] T041 Confirm the full reproduction + evidence suite from the ADR's *Required evidence* list passes; cross-check against SC-001…SC-005.
- [ ] T042 Coordinator check: on #3411, confirm the agreed ADR 0092 text still says option C (decision 6) and the suspended-keeps-base contract (decision 11) as this spec applies them; recheck the spec if it changed.

## Dependencies & order

- Phase 2 (foundational) blocks all user stories.
- US1 and US2 are both P1 and largely independent (different sites: US1 at capture/intrinsic/input; US2 at successor staging). US1 is the MVP.
- T025 (US4 PoC) runs first: the default of FR-011 depends on it (FR-013a). US3 depends on US1's commit machinery. US4 is otherwise independent (scheduling). US5 depends on US1's read sets. US6 depends on US1 + US2. US7 depends on US1 (writes to record).
- T028 (US4) and T038 (US7) are the provisional-spec feedback tasks to #3411.

## Parallel opportunities

- Within Phase 2: T004, T005 parallel; T008 parallel after T007.
- US1: T011 parallel with T010; T014 parallel with T013.
- Cross-story after foundational: US4 (scheduling) can proceed alongside US1/US2 since it touches the fusion/start path, not the write sites — coordinate on `WorkflowStartActivitySchedulerWorkHandler.cs` (US1 T012, US2 T020, US4 T026 all touch it: sequence those).

## MVP

User Story 1 with the US4 PoC (Phases 1–3 plus T025) delivers the core: no silent lost updates, with versions, read
sets, the commit rule and the default lost-update warning in place. It is independently testable via the parallel reproduction drive.

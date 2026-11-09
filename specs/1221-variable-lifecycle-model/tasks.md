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

## Phase 1: Setup

- [ ] T001 Confirm test projects reference the runtime projects under change (`Essentials/tests/Workflows/Runtime/Tests`, `Essentials/tests/Activities/Runtime/Tests`) and add a `VariableLifecycle` test folder in each.
- [ ] T002 [P] Add a large-payload / hot-loop benchmark harness stub under `Essentials/tests/Workflows/Runtime/Tests/VariableLifecycle/Benchmarks` for SC-005 / Open question B (no thresholds yet).

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: the data model and the commit rule every story depends on.

- [ ] T003 Add per-variable `Version` and `Digest` to `VariableFrameState` in `Essentials/src/Workflows/Runtime/Core/Models/VariableFrameState.cs`, beside the frame revision; mirror in the row store.
- [ ] T004 [P] Add `ReadSetEntry`, `InputReadSet` and `SessionReads` types in `Essentials/src/Workflows/Runtime/Core/Models/` (per data-model.md).
- [ ] T005 [P] Add the `WriteHistoryRecord` type (mandatory metadata; optional value; conditional operational data) in `Essentials/src/Workflows/Runtime/Core/Models/`.
- [ ] T006 Add the digest computation over ADR 0091 canonical form (one per applied write) in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/VariableFrameFactory.cs`.
- [ ] T007 Implement `VariableCommitRule` (blind / current / returned-value / lost-update) in `Essentials/src/Workflows/Runtime/Core/` per contracts/commit-rule.md.
- [ ] T008 [P] Behaviour tests for `VariableCommitRule`: blind applies; base==current applies; `0→1→0` applies + records mismatch; stale+changed is a lost update; unchanged value writes nothing but two 0→1 writers do not hide the second. In `.../Tests/VariableLifecycle/CommitRuleTests.cs`.
- [ ] T009 Document/schema version increment + upcast for `VariableFrameState` and `WriteHistoryRecord` (missing version → 0 / no base); test old-record read-back.

**Checkpoint**: data model and commit rule exist and are unit-tested; no write site uses them yet.

---

## Phase 3: User Story 1 — Parallel read-modify-write is caught (Priority: P1) 🎯 MVP

**Goal**: a parallel input+capture `count++` either totals correctly or faults visibly, never silently loses an update.
**Independent test**: the reproduction drive below fails before and faults after.

- [ ] T010 [US1] **(test-first)** Reproduction drive: `Parallel` of two `External` leaves taking `count` and capturing `count + 1`; assert silent `1` today, lost-update fault after. In `.../Tests/VariableLifecycle/ParallelLostUpdateTests.cs`.
- [ ] T011 [P] [US1] **(test-first)** Drive: two branches read 0 and write 1 → second is a lost update, not a no-op.
- [ ] T012 [US1] Capture the input read set during materialization in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/WorkflowStartActivitySchedulerWorkHandler.cs`; commit it with the input snapshot.
- [ ] T013 [US1] Apply the commit rule and base at the capture site in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/RuntimeOutputCaptureProjection.cs`.
- [ ] T014 [P] [US1] Apply the commit rule at the intrinsic site (Set/Merge/Reduce) in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/WorkflowIntrinsicExecutor.cs`.
- [ ] T015 [US1] Record session reads per attempt in the variable session read path; implement base selection (immutable input bases; oldest-in-use tie-break) per data-model.md and R-4.
- [ ] T016 [US1] Self-write handling: an invocation's applied write advances its own base for X (session change on suspend + capture on complete, ADR 0090 §6 order); test no self-conflict.

**Checkpoint**: US1 reproduction passes; lost updates detected across `External`; blind writes unaffected.

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

## Phase 5: User Story 3 — Explicit last-wins (Priority: P2)

- [ ] T022 [US3] Add the `LastWins` policy: per activity type (attribute) and per node (setting) in the runtime policy resolution.
- [ ] T023 [US3] Route the default `Fault` through the existing incident path with the diagnostic (variable, versions, writers); record the rejected completion. Reference `docs/runtime-fault-behavior.md`.
- [ ] T024 [P] [US3] Behaviour tests: `LastWins` (type and node) applies a stale-changed write and records it; default faults through the incident strategy. In `.../Tests/VariableLifecycle/LostUpdatePolicyTests.cs`.

---

## Phase 6: User Story 4 — No inline gap for `External` (Priority: P2) — Open question A

- [ ] T025 [US4] **(test-first / PoC)** Prove no other branch's work item runs between an `External` inline leaf's input materialization, body and transition commit, with claim/fence/durability intact and no DB transaction around user code. In `.../Tests/VariableLifecycle/InlineNoGapTests.cs`.
- [ ] T026 [US4] Extend the single-work-item dispatch to `External` leaves in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/ReplaySafeFusionDriver.cs` (and the start path), keeping `ActivityAttemptClaimed` claim/flush as intermediate commits.
- [ ] T027 [P] [US4] Parallel test for `ReplaySafe` leaves (already fused) confirming the same no-gap guarantee.
- [ ] T028 [US4] **Flow finding back to #3411**: post the PoC result (feasible / not) as a comment on ADR 0092; if infeasible without weakening ADR 0032 R2, it reopens decision 10.

---

## Phase 7: User Story 5 — Background runs on fresh inputs (D-9) (Priority: P2)

- [ ] T029 [US5] Implement the admission refresh: compare input read set to current versions; on change, a new evaluation (same `InvocationId`, own sequence + idempotency keys) on the background admission path.
- [ ] T030 [P] [US5] Per-node refresh opt-out; a stale base then handled at commit.
- [ ] T031 [US5] Retain both snapshots linked with the reason; the worker's snapshot immutable thereafter; cover only Start→admission.
- [ ] T032 [P] [US5] Tests: refresh on changed input; opt-out faults/LastWins at commit; superseded snapshot stays in history. In `.../Tests/VariableLifecycle/BackgroundRefreshTests.cs`.

---

## Phase 8: User Story 6 — Suspension keeps base (D-11) (Priority: P3)

- [ ] T033 [US6] On resume: keep committed snapshot + input read set (own-write bases advanced), fresh session, no auto-refresh; fault only on an explicit stale-changed write.
- [ ] T034 [P] [US6] `ModifyVariableAlterationHandler`: record the variable's version and apply only if unchanged (replace the root-frame revision check); a read alone never faults. In `.../Alterations/Handlers/ModifyVariableAlterationHandler.cs`.
- [ ] T035 [P] [US6] Tests: resume keeps base; read-only change does not fault; stale-changed write faults. In `.../Tests/VariableLifecycle/SuspensionTests.cs`.

---

## Phase 9: User Story 7 — Write history data (D-13) (Priority: P3) — Open question B

- [ ] T036 [US7] Record a `WriteHistoryRecord` at every applied write and conflict (metadata always; value opt-in + retention; operational data retained for enabled merge/recovery).
- [ ] T037 [US7] Preserve every intermediate write incl. `0→1→0` within one buffered segment; distinguish omitted/redacted/expired from `null`.
- [ ] T038 [US7] **Measurement (Open question B)**: benchmark rows vs. derived-from-checkpoint under large payloads / hot loops; choose the physical storage; set the SC-005 overhead threshold. Post the result on #3411 (reviewer Q12).
- [ ] T039 [P] [US7] Tests: required metadata present for each write source; intermediate writes survive folding; operational data available with inspection values off/expired. In `.../Tests/VariableLifecycle/WriteHistoryTests.cs`.

---

## Phase 10: Polish & cross-cutting

- [ ] T040 [P] Update `docs/runtime-fault-behavior.md` and the variable/lifecycle docs for the commit rule, policies and operator actions (§2.22).
- [ ] T041 Confirm the full reproduction + evidence suite from the ADR's *Required evidence* list passes; cross-check against SC-001…SC-005.
- [ ] T042 Coordinator closes: on #3411, apply the ADR text-closes for reviewer Q3 (strike strict-only framing in decision 6) and Q8 (strike the open framing in decision 11), per *Approval* in spec.md.

## Dependencies & order

- Phase 2 (foundational) blocks all user stories.
- US1 and US2 are both P1 and largely independent (different sites: US1 at capture/intrinsic/input; US2 at successor staging). US1 is the MVP.
- US3 depends on US1's commit machinery. US4 is independent (scheduling). US5 depends on US1's read sets. US6 depends on US1 + US2. US7 depends on US1 (writes to record).
- T028 (US4) and T038 (US7) are the provisional-spec feedback tasks to #3411.

## Parallel opportunities

- Within Phase 2: T004, T005 parallel; T008 parallel after T007.
- US1: T011 parallel with T010; T014 parallel with T013.
- Cross-story after foundational: US4 (scheduling) can proceed alongside US1/US2 since it touches the fusion/start path, not the write sites — coordinate on `WorkflowStartActivitySchedulerWorkHandler.cs` (US1 T012, US2 T020, US4 T026 all touch it: sequence those).

## MVP

User Story 1 alone (Phases 1–3) delivers the core: lost updates are detected and faulted, with versions, read sets and
the commit rule in place. It is independently testable via the parallel reproduction drive.

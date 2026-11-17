# Tasks: Variable lifecycle model

**Input**: Design documents from `specs/1221-variable-lifecycle-model/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Status**: implemented in #4316 (2026-11-17). Every task is done there, moved (T015, T016 to S-7; T024a to S-5), or
posted (T028).

**Tests / cadence**: Elsa uses xUnit, no FluentAssertions (constitution). The two reproduction drives (US1, US2) and the
no-gap proof (US4) are **test-first** — the ADR's *Required evidence* needs them failing before and passing after. Other
tests follow §2.23 (behaviour tests for logic-bearing implementations; feature-registration tests only where wiring
carries logic) at the application cadence (§2.21.2). No new CI tier (existing Full tier runs them).

**Implementation status (#4316):** the ticked tasks are done there. Notes per task:

- **T015, T016 (session reads, own-write bases): moved to S-7** (coordinator, 2026-11-17; see the spec's *Scope
  moved*). No variable session exists before S-7 (ADR 0090) builds it. The read-set model keeps one entry per variable
  (oldest read wins, the spec's oldest-in-use rule), which S-7's session reads join.
- **US2 operator actions (T017–T020 in #4316):** following the ADR 0092 decision 8 amendment of 2026-11-16,
  `ActivityExecutionState.LogicalInvocationId` is the logical invocation identity (the first attempt's row id through
  the attempt lineage; `OperationKey` already falls back to it). A retry successor joins the source's lineage and keeps
  its snapshot and read set; `RescheduleActivity/1` and the new `ReExecuteFaultedActivity/1` create a re-execution
  successor that re-enters through `ScheduleActivity` with no snapshot and no activated frames, so its Start activates
  them once and materializes fresh inputs. Activity code reads the logical identity as `context.LogicalInvocationId`
  (also on the start and resume contexts), or the operation key.
- **T020:** the re-execution successor carries no activated frames and re-requests its loop iteration; a drive
  re-executes a `ForEach` body and shows one iteration frame, activated by the successor for the same item, with the
  source's frame untouched (red when the successor keeps the source's frame).
  A second drive runs a `ForEach` over three items whose body faults on the second: under an ancestor that waits for
  intervention, retrying or re-executing iteration 2 runs only that iteration again (on its own item) and then
  iteration 3, which never ran; under the `Fault` strategy the run ends, iteration 3 never runs and both actions are
  refused (`WorkflowTerminal`).
- **T021 (accept recorded completion):** `AcceptRecordedCompletion/1`. A `Fault` refusal on an invoke keeps the
  whole refused completion on the faulted row (`RecordedCompletion`: result, outcome selection, attempt and every
  variable write resolved to its value). Accepting it revalidates the pinned contract and each target variable's declaration (type and protection), then
  supersedes the row with a completed successor of the same logical invocation; each recorded write applies as an
  override (`AcceptedOverride`, writer `AcceptedCompletion`) that keeps the value it overwrote, and the parent hears of
  the completion once. A repeated request finds the source superseded and is refused. Not recorded, so refused
  (`NoRecordedCompletion`): a faulted body, a completion that also produced durable outputs, a workflow dispatch, a
  published stimulus or a parent or ancestor notification (an alteration cannot replay those), and a refusal on resume or on a composite's completion.
- **T010a, T029–T032 (background branches, admission refresh):** `BackgroundActivityCoordinator.RefreshInputsAsync`
  compares the pinned snapshot's read set with the current versions of the visible frames at admission; on a change it
  materializes the inputs again as evaluation n+1 of the same invocation (the work id names the evaluation), keeps the
  superseded snapshot in `InputRefreshes` with the changed variables, and pins the fresh one for the worker. The node
  opt-out is `ActivityNode.DisableBackgroundInputRefresh`, pinned as `runtime.backgroundInputRefresh=off`. Drives in
  `BackgroundRefreshTests`: refresh on change, no refresh when unchanged, opt-out warns by default and faults under
  `Fault`, and two background branches (T010a) total `1` with a warning on the second commit.
- **T024a: delivered by S-5** (coordinator, 2026-11-17): spec 266's unordered-writer warning is S-5's (#4669), so
  the pointer to the `Fault` opt-in goes into that warning there; S-10 does not wait for it.
- **T028:** the PoC result (feasible: inline `External` and `ReplaySafe` leaves fuse) is posted on #3411. **T042:**
  checked against the merged ADR text.
- **T003:** versions and digests are on frames and on closed-scope variable rows (row metadata
  `runtime.variable.version/digest/writer`). A body node reads rows through a frame that carries those versions, so its
  read set records them, and a capture into a row is judged by the commit rule (`RuntimeOutputCaptureProjector`,
  `ClosedScopeVariableRows`); a refused row write refuses the completion. Intrinsics cannot write a closed scope's
  variables yet, so captures are the only row writers.
- **T021a:** the warning is its write-history entry, with both writers, both versions and the overwritten value; the
  API that exposes it is S-6 and Studio presentation is its own elsa-foundation-studio work (spec *Scope moved*). A
  warning incident was not used: an incident in a commit ends the coalescing session and would re-open the inline gap
  for later leaves.
- **T033:** resume keeps the snapshot and read set and starts with no refresh. Advancing own-write bases on resume
  moved to S-7 with T016.
- **T036, T037:** every applied write and lost update leaves a record on the writer's activity state; a
  `ModifyVariable` leaves one on its job outcome (versions and writer, no value). FR-024's instance commit order:
  `RuntimeCommitSequenceStamper` numbers each logical checkpoint under the execution lease (fencing token, then the
  ordinal within the lease) before any coalescing fold, and stamps new records and activity lifecycle transitions
  (`ActivityExecutionState.Transitions`); no read or write per commit. FR-025: each value a leaf capture or an intrinsic wrote is an inspection
  value of the writer (subject `VariableWrite`, setting `variableWrites`, off unless that setting is given a level), so capture,
  sensitivity and retention follow the existing rules and a value not captured never reads as `null`. FR-026: the
  overwritten value, the refused write and the refused completion stay on the state whatever that setting says.
  Intermediate writes inside one folded segment keep their own records and numbers (FR-027).
- **T038:** measured; storage chosen (records ride the writer's checkpoint, no extra rows); the SC-005 budget is
  proposed in #4316's description and posted on #3411 with T028.
- **T009:** no document version is bumped, by design: every new member is additive and omitted while empty, so an older
  frame, row, snapshot or record reads back at version 0, with no base, no commit sequence and no transitions, which
  tests cover. A reader treats a missing commit sequence as order unknown (S-6 marks it approximate).
- **T039:** `WriteHistoryTests` (values captured and not captured, required metadata, an intrinsic write), operational
  data and restoring a value a warning overwrote (`OperatorActionsTests.A_value_a_lost_update_overwrote_can_be_restored`),
  background-result records (`BackgroundRefreshTests`), buffered-segment records and commit order (`CommitOrderTests`,
  `CommitSequenceStampingTests`), closed-scope rows (`GraphBodyScopeTests`).
- **T041:** the evidence suite is in #4316's description, checked against SC-001 to SC-005.
- **For the next implementation PR:** keep the positional signatures of `VariableFrameState` and
  `WorkflowAlterationCapturedConcurrency` as they are now (Code Review, #4316); add members as optional ones.

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
- [x] T010a [P] [US1] **(test-first)** Drive: the same with two background branches → `1` with a lost-update warning on the second commit (instance diagnostic + write-history entry naming both writers, overwritten value kept).
- [x] T011 [P] [US1] **(test-first)** Drive: two branches read 0 and write 1 → second is a lost update (warning), not a no-op; `0 → 1 → 0` in default mode warns.
- [x] T012 [US1] Capture the input read set during materialization in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/WorkflowStartActivitySchedulerWorkHandler.cs`; commit it with the input snapshot.
- [x] T013 [US1] Apply the commit rule and base at the capture site in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/RuntimeOutputCaptureProjection.cs`.
- [x] T014 [P] [US1] Apply the commit rule at the intrinsic site (Set/Merge/Reduce) in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/WorkflowIntrinsicExecutor.cs`.
- [x] T015 [US1] **Moved to S-7, not built here.** Record session reads per attempt in the variable session read path; implement base selection (immutable input bases; oldest-in-use tie-break) per data-model.md and R-4.
- [x] T016 [US1] **Moved to S-7, not built here.** Self-write handling: an invocation's applied write advances its own base for X (session change on suspend + capture on complete, ADR 0090 §6 order); test no self-conflict.

**Checkpoint**: US1 reproduction passes; lost updates are never silent across `External`; blind writes unaffected.

---

## Phase 4: User Story 2 — Operator correction reaches the step (Priority: P1)

**Goal**: `ModifyVariable` corrections reach a re-executed step; retry keeps the snapshot; accept applies without the body.

- [x] T017 [US2] **(test-first)** Drive: `ModifyVariable` then re-execute uses the corrected value; retry keeps `InvocationId` + snapshot. In `.../Tests/VariableLifecycle/OperatorActionsTests.cs`.
- [x] T018 [US2] Split the three actions in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/ActivitySuccessorStaging.cs`: retry (same id + snapshot), re-execute/reschedule (new id + fresh inputs + new input read set), accept recorded completion.
- [x] T019 [US2] Fix the current bug where an operator retry gets a new id with a copied snapshot; amend ADR 0045 §3 behavior (re-execute materializes fresh inputs).
- [x] T020 [P] [US2] Add the owned-frame re-activation guard on the successor `Start` re-entry path in `WorkflowStartActivitySchedulerWorkHandler.cs`.
- [x] T021 [US2] Implement accept-recorded-completion: revalidate state/scope/contracts/policies, apply the recorded transition atomically + duplicate-safely, refuse a faulted body, keep the conflict in history, no silent input refresh; test repeated acceptance does not double-schedule downstream.

**Checkpoint**: the three operator actions are distinct and tested; corrections reach re-executed steps.

---

## Phase 5: User Story 3 — Opt-in strict read-modify-write (Priority: P2)

- [x] T021a [US1] Default lost-update warning: on a stale base the write applies and an instance diagnostic (Studio) plus a write-history entry is raised, naming the variable, base and current versions and both writers, keeping the overwritten value; versions only, no digest in the read set.
- [x] T022 [US3] Add the `Fault` opt-in: `LostUpdateMode` per activity type (attribute) and per node (setting) in the runtime policy resolution, pinned in the executable; `Fault` nodes record stored digests in their read sets and use the exact check.
- [x] T023 [US3] Route a `Fault` lost update through the existing incident path with the diagnostic (variable, versions, writers); record the rejected completion. Reference `docs/runtime-fault-behavior.md`.
- [x] T024 [P] [US3] Behaviour tests: default warns and applies (incl. `0 → 1 → 0`); `Fault` (type and node) applies `0 → 1 → 0` with a recorded mismatch and faults a real lost update through the incident strategy. In `.../Tests/VariableLifecycle/LostUpdatePolicyTests.cs`.
- [x] T024a [P] [US3] **Delivered by S-5 (#4669).** Point spec 266's unordered-writer publish warning to the `Fault` opt-in (FR-012a); coordinate with S-5 (#3286).

---

## Phase 6: User Story 4 — No inline gap for `External` (Priority: P1) — Open question A (run T025 first)

- [x] T025 [US4] **(test-first / PoC)** Prove no other branch's work item runs between an `External` inline leaf's input materialization, body and transition commit, with claim/fence/durability intact and no DB transaction around user code. In `.../Tests/VariableLifecycle/InlineNoGapTests.cs`.
- [x] T026 [US4] Extend the single-work-item dispatch to `External` leaves in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/ReplaySafeFusionDriver.cs` (and the start path), keeping `ActivityAttemptClaimed` claim/flush as intermediate commits.
- [x] T027 [P] [US4] Parallel test for `ReplaySafe` leaves (already fused) confirming the same no-gap guarantee.
- [x] T028 [US4] **Posted 2026-11-17: [#3411 comment](https://github.com/elsa-workflows/elsa-foundation/pull/3411#issuecomment-6945308445), feasible.** **Flow finding back to #3411**: post the PoC result (feasible / not) as a comment on ADR 0092; if infeasible without weakening ADR 0032 R2, it reopens decision 10 and the default of decision 6 (FR-013a), before this spec is approved.

---

## Phase 7: User Story 5 — Background runs on fresh inputs (D-9) (Priority: P2)

- [x] T029 [US5] Implement the admission refresh: compare input read set to current versions; on change, a new evaluation (same `InvocationId`, own sequence + idempotency keys) on the background admission path.
- [x] T030 [P] [US5] Per-node refresh opt-out; a stale base then handled at commit.
- [x] T031 [US5] Retain both snapshots linked with the reason; the worker's snapshot immutable thereafter; cover only Start→admission.
- [x] T032 [P] [US5] Tests: refresh on changed input; opt-out meets the commit rule (warning by default, fault under `Fault`); superseded snapshot stays in history. In `.../Tests/VariableLifecycle/BackgroundRefreshTests.cs`.

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
- [x] T039 [P] [US7] Tests: required metadata present for each write source; intermediate writes survive folding; operational data (rejected completion, overwritten value) available with inspection values off/expired; an overwritten value can be restored. In `.../Tests/VariableLifecycle/WriteHistoryTests.cs`.

---

## Phase 10: Polish & cross-cutting

- [x] T040 [P] Update `docs/runtime-fault-behavior.md` and the variable/lifecycle docs for the commit rule, the lost-update warning and the `Fault` opt-in, and the operator actions; document how to sequence branches or aggregate results, and when to opt in to `Fault` (§2.22).
- [x] T041 Confirm the full reproduction + evidence suite from the ADR's *Required evidence* list passes; cross-check against SC-001…SC-005.
- [x] T042 **Checked 2026-11-17 against the merged ADR text on the branch: decision 6 is option C, decision 11 the suspended-keeps-base contract, decision 8 as amended 2026-11-16.** Coordinator check: on #3411, confirm the agreed ADR 0092 text still says option C (decision 6) and the suspended-keeps-base contract (decision 11) as this spec applies them; recheck the spec if it changed.

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

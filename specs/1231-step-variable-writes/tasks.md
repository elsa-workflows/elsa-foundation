# Tasks: Activities change variables, committed with the step (spec 1231, S-7)

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/step-variables.md](contracts/step-variables.md)

**Gates**: the spec is approved (2026-11-17). S-10 (#4316) must be on the branch
before Phase 2; S-11 (#4725) before Phase 6. If either has not merged, the implementing PR depends on it. Phase 4
(the reference types and the marker) and Phase 6 (C9) land in **the same PR** (ADR 0093 *Consequences*).

Tests use xunit only. `[P]` = can run in parallel with other `[P]` tasks of the same phase (different files).

## Phase 1: Setup

- [ ] T001 Merge the initiative branch into the implementing branch and confirm S-10's types (`VariableReadSetEntry`, `VariableCommitRule`, `LostUpdateMode`, `VariableWriteSource.SessionChange`) and S-11's `BackgroundEligibility` are present; record the heads in the PR body.
- [ ] T002 [P] Add the public API entries to `PublicAPI.Unshipped.txt` of the touched packages as types are added (tracked per phase).

## Phase 2: Foundational — the session and the commit path (blocks every story)

- [ ] T003 Add `IStepVariables` and `IActivityExecutionContext.Variables` in `Essentials/src/Activities/Runtime/Core/Contracts/` (FR-002, FR-023).
- [ ] T004 Implement `StepVariableSession` and `WriteIntent` (data-model.md) in `Essentials/src/Workflows/Runtime/Core/Models/`: committed read at the moment of the read over the visible frame chain shared with `RuntimeContainerScopeService`, read-your-writes view, loop items read-only, `VF-ACT-010` for withheld values (FR-003, FR-004, FR-006); the read by scope and key that S-4's T050 calls (FR-006a); transient and withheld matches fault on the host side, uncatchable by the script (FR-006b).
- [ ] T005 Record session reads per attempt as `VariableReadSetEntry` with the protection policy (digest only under `Fault`); never carried to a new attempt or across suspension (FR-007; **spec 1221 T015**).
- [ ] T006 Base selection per research R-3: session read → input read set → blind; oldest in-use on ambiguity (FR-008, FR-009; **spec 1221 T015**, FR-006 session half).
- [ ] T007 Create the session per step in `WorkflowInvokeActivitySchedulerWorkHandler` and `WorkflowResumeBookmarkSchedulerWorkHandler`; a fresh session on resume and on every new attempt.
- [ ] T008 Apply intents in the step's checkpoint commit before captures, through S-10's `VariableCommitRule` with the node's lost-update mode and then the C4 no-op check; write records with `SessionChange` (FR-011–FR-013, FR-016); integrate with `RuntimeOutputCaptureProjection` so the capture wins.
- [ ] T009 Own-write base advance: an applied own write moves the invocation's base for that variable; persist it on the activity execution state; restore on resume (FR-010; **spec 1221 T016** and the resume part of T033).
- [ ] T010 Fault and cancel discard pending intents; type-check and serialization errors fault before the commit; suspension commits changes so far and drops pending intents (FR-014, FR-015).

**Checkpoint**: a test-only activity can read and write through the session and the commit rule.

## Phase 3: User Story 3 — lost updates on step writes (P1)

- [ ] T011 [P] [US3] **(test-first)** Two parallel inline branches each increment `count` through the session → `2` (no inline gap).
- [ ] T012 [P] [US3] **(test-first)** Read `count`, suspend, another branch writes, resume and write → lost update: default warning with both writers and the overwritten value; `[ActivityLostUpdateMode(Fault)]` and the per-node setting → fault, nothing saved.
- [ ] T013 [P] [US3] **(test-first)** Blind session write is never a lost update; follows D-8.
- [ ] T014 [P] [US3] **(test-first)** Input read `count = 0`, later session read `count = 10`, capture computed from the input → the capture's base is the input version (older dependency kept).
- [ ] T015 [P] [US3] **(test-first)** Session write on suspend + capture on completion → no self-conflict; session write + capture in one commit → capture wins (spec 1221 T016 evidence).
- [ ] T016 [P] [US3] **(test-first)** A retry, a re-execution and a crash redelivery each start a new session and read committed values again; a re-driven burst never applies an intent twice.

## Phase 4: User Story 2 + 4 — C# references and free names (P1/P2)

- [ ] T017 [US2] Add `VariableReference<T>` and `ReadOnlyVariableReference<T>` (`Essentials/src/Activities/Runtime/Core/Models/VariableReference.cs`) and the contract input kind + direction in `ActivityContract`; binding like a capture target (FR-022).
- [ ] T018 [US2] `Get`/`Set` on references: copy on read, intent on `Set`, `Set` on read-only throws; `Get<T>` on `Any` defaults to `JsonNode`; dropped-member warning on write-back from a narrower type (FR-017, FR-023).
- [ ] T019 [US2] Publish validation: unbound, undeclared, writable bound to a read-only target → refused; may-write set = writable bound references (FR-024), in `ExecutableNodeCompiler` / `ValueFlowValidator`.
- [ ] T020 [US4] Add `AccessesUndeclaredVariablesAttribute`, pin it into the contract and descriptor; free-name `Get`/`Set` only on marked types, else throw; undeclared name faults before commit (FR-025, FR-026).
- [ ] T021 [US2] Transient references: live instance on `Get`, in-memory `Set`, nothing committed; publish refuses a transient reference on a background-placed or suspending activity; a free-name transient access in such a step faults (FR-027).
- [ ] T022 [P] [US2] **(test-first)** Acceptance 1–6 of User Story 2.
- [ ] T023 [P] [US4] **(test-first)** Acceptance 1–5 of User Story 4.

## Phase 5: User Story 1 — script writes (P1)

- [ ] T024 [US1] Extend S-4's body analyzer with write forms (contract table): pin targets, compute may-write set and body touch set; refuse undeclared, transient and read-only literal writes with diagnostics naming position and target (FR-018–FR-020); mark a node with a computed or opaque write as possibly writing every visible variable (FR-020a).
- [ ] T025 [US1] Jint write surface in the body: `variables` assignable, `setVariable('x', v)`, no `set<Name>()`; whole assignments go to the session (P-8) (FR-005, FR-021).
- [ ] T026 [US1] Hand S-8's reported member changes to the session as `MemberChange` intents (C3), once S-8's bridge (spec 1228 T026) is on the branch; until then member changes stay unsupported and the test is skipped with a reason.
- [ ] T027 [P] [US1] **(test-first)** Acceptance 1–5 of User Story 1 (whole assignments, fault, read-your-writes, read-only, no-op).
- [ ] T028 [P] [US1] **(test-first)** Acceptance 6 (member change, C2 warning) — lands with S-8 US3's end-to-end test (spec 1228 T028), whichever of the two arrives second.
- [ ] T029 [P] [US1] **(test-first)** A script mutating a loop item throws; publish refuses assigning a loop item or index.
- [ ] T029a [P] [US1] **(test-first)** Computed and opaque writes (FR-020a): `variables[k] = v`, `setVariable(k, v)` and `const v = variables; v.X = 1` publish with the every-visible-variable mark, commit with the step, and fault on an undeclared or transient name; a plain write in an aliasing script works; the concurrent-write rule counts the node against every visible variable.

## Phase 6: User Story 5 — background eligibility C9 (P2, same PR as Phase 4)

- [ ] T030 [US5] Extend `BackgroundEligibility.AccessesVariablesByName(Type)` in `Essentials/src/Workflows/Runtime/Core/Background/BackgroundEligibility.cs`: `[AccessesUndeclaredVariables]`, any `VariableReference<T>` or `ReadOnlyVariableReference<T>` input (Q2); keep `IRuntimeScopedVariableReader` (FR-030 a–c).
- [ ] T031 [US5] Add a subject fact `TouchesVariables` built from the pinned contract (marker, reference inputs), checked by C9 even when `TypeCheckedAtRunTime` (FR-031); update `ForPinnedNode` and the template placer's subject.
- [ ] T032 [P] [US5] **(test-first)** For each signal — (a) marker, (b) writable reference, (c) read-only reference — publication is refused with exactly `VF-BG-001 (C9)` and the run-time re-check (admission and worker) refuses with the same text.
- [ ] T033 [P] [US5] **(test-first)** Template placement in a design-only shell refuses the contract-visible signals at placement and the type-only ones at admission.
- [ ] T034 [US5] Update ADR 0093's C9 *Today* note only through an amendment PR if the coordinator chooses Q2's alternative; otherwise nothing (`RunJavaScript` is already refused in the background by S-11).

## Phase 7: Publish-time write analysis (all stories)

- [ ] T035 Feed may-write sets into `ValueFlow/ConcurrentWrite` (spec 266's warning, PR #4669); a marked free-name activity touches every visible variable (FR-028).
- [ ] T036 Warn when a node's may-write set and one of its own captures name the same variable (FR-029).
- [ ] T037 [P] **(test-first)** Concurrent-write and overlap diagnostics, including the marked-activity case.

## Phase 8: User Story 6 — Elsa 3 conversion (P2)

- [ ] T038 [US6] In `Elsa3ExpressionRewriter` script mode: rewrite `setVariable('<lit>', v)` and `set<Name>(v)` to `variables.<Name> = v` (bracket form for non-identifiers), and `getVariable('<lit>').m = v` to `variables.<Name>.m = v` (FR-032, Q6).
- [ ] T039 [US6] New stable refusals with guidance: computed setter name, setter of an undeclared variable, setter of a variable that converts to a transient one; expression mode keeps today's refusal with guidance naming a Run JavaScript activity; `setOutput`, `setInput`, `setResult`, `setCorrelationId`, `setWorkflowInstanceName` stay refused (FR-033, FR-034).
- [ ] T040 [P] [US6] **(test-first)** Converter tests for acceptance 1–6 of User Story 6, plus a publish-and-run test of the converted script.
- [ ] T041 [US6] Re-measure the 36 JTest definitions (conversion, publication, run) and post the counts on #3052 (SC-006).

## Phase 9: Polish and evidence

- [ ] T042 Measure write cost for a 1 MiB value and a hot loop; record in research.md R-9 (FR-035, SC-007).
- [ ] T043 [P] Docs: activity authoring (step writes, references, the marker, `[ActivityLostUpdateMode]`), script docs (write forms; copy explicitly with `{...variables.o}`), `EXTENSION_POINTS.md`, Elsa 3 conversion guide, release notes (§E2.6.1 note).
- [ ] T044 Map every ADR 0090 *Required evidence* item in scope to its test in the PR body (SC-001); list the host-interop items as out of scope (#4103).
- [ ] T045 Set the spec's `Status: Implemented` in the finishing PR; update spec 1221's tasks.md notes for T015/T016 to point at this PR, and spec 1222 T047/T050 and spec 1228 T026/T028 if they can now proceed.

## Dependencies

- Phase 2 → all stories. Phase 3 needs Phase 2 only (test activities). Phase 4 → Phase 6 (same PR). Phase 5 needs
  S-4's body read surface (spec 1222 T045–T050) and, for member changes, S-8. Phase 8 needs Phase 5.
- Spec 1222 T047/T050 (body read path) and spec 1228 US3 wait on Phase 2 (and T025 for writes).

# Tasks: Compatible BPMN library adoption

**Input**: Reviewed [spec](spec.md), [plan](plan.md), [research](research.md), [data model](data-model.md), [compatibility contract](contracts/compatibility.md) and [validation guide](quickstart.md).

Spec review: three independent lenses clean. Plan review: corrected verdict-aware batch reconciliation, exact-handle signal retirement and same-node re-arm capture, then three lenses clean. Existing #2735 claim and one ordered switch PR; no new leaf. This task list must be reviewed before source implementation.

## Phase 1: Setup and durable record

- [x] T001 Refresh existing #2735 claim/PR/main and deliver parity PR #3335 with exact-head review/check evidence.
- [x] T002 Create/review `specs/1006-bpmn-library-adoption/spec.md` and requirements checklist before planning.
- [x] T003 Create/review `specs/1006-bpmn-library-adoption/plan.md` and its research/data-model/contract/quickstart outputs before tasks.
- [x] T004 Independently review `specs/1006-bpmn-library-adoption/tasks.md` for complete dependencies and proof gates; record outcome before implementing.
- [x] T005 Open the second ordered draft PR for #2735 from `agent/codex/2735-bpmn-switch`, with reviewed artifacts and a checklist of still-unproved acceptance. Keep #2735/#2892 checkpoints current; preserve all frozen files.

## Phase 2: Actual legacy capture — blocks pin changes and writer deletion

Goal: actual coherent execution checkpoints and a faithful capture control. This is a prerequisite for US1, not completed upgraded-host recovery.

- [x] T006 Add a test-only committed-checkpoint capture/gate in `Essentials/tests/Activities/Bpmn/Tests/` that records actual state/queue/outbox/bookmark/value/incident documents before the next scheduler action. Capture source/pins/executable and assert boundary coherence. Normal tests must not regenerate goldens.
- [x] T007 Capture a real resolved event race with canceled loser and trailing live wait under `specs/1006-bpmn-library-adoption/evidence/legacy/`; assert canceled token, exact terminal child/provenance and cleaned bookmark.
- [x] T008 Capture and prove the separate loser-completion-already-queued-before-winner-cancellation order in the same evidence directory; a failed precondition is a failed capture, not a pass.
- [x] T009 Capture parallel MI outstanding work and a non-interrupting listener's terminal previous invocation plus live same-node/null-iteration re-arm; preserve distinct tokens, child handles and iteration/value state.
- [x] T010 Capture an actual committed legacy PendingFault after an evaluation already staged work; retain original code/message, child/incident state and owed work. A synthetic typed state cannot satisfy this task.
- [x] T011 Restore/reopen the captured checkpoint through an old-host control in the BPMN test/integration projects, drive its remaining work, and record fixture digests/provenance in `specs/1006-bpmn-library-adoption/evidence/legacy/README.md`.
- [x] T012 Review capture preconditions, portable fixture completeness and old-host controls; commit the verified fixtures while the legacy writer and package pins still exist. Correct the reader design if evidence contradicts it. Gate A remains closed until T006–T012 pass.

## Phase 3: US1 — compatible execution-state continuation (P1)

Independent test: old fixtures enter a fresh upgraded host/provider and finish with exact work/incident/parent identity; ambiguous/missing identity refuses dispatch safely.

- [x] T013 [US1] Adopt published Model/Interchange/Semantics 0.5.0 in `Directory.Packages.props` and the BPMN project; regenerate affected lockfiles using the normal locked-restore workflow. This follows Gate A; do not use source project references as release proof.
- [x] T014 [US1] Add state-compatibility tests in `Essentials/tests/Activities/Bpmn/Tests/` and the runtime EF project using the actual captured fixtures; prove direct model substitution fails before implementing the versioned boundary.
- [x] T015 [US1] Implement explicit legacy/current readers and writers in `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/`; keep existing v1 storage representation and preserve all pending-fault/race/loop/compensation/diagnostic fields. Add alias/version/corruption and old-reader controls.
- [x] T016 [US1] Resolve exact active/stopped handles from complete durable direct-child history, exact callback IDs and queued provenance in the host adapter; validate parent/node/element/token/iteration uniqueness before released pruning. Distinguish never-issued work from missing identity.
- [ ] T017 [US1] Prove completion, fault, replay and pruning for canceled work, queued-loser order, MI and same-node re-arm through the upgraded host. Mutate lookup to first node/iteration match and prove the re-arm witness fails; restore and rerun green.
- [x] T018 [US1] Prove fresh-provider recovery of original deferred fault and all fixture outcomes in `Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/`; retain capable readers/replay IDs and document actual rollback limits.

## Phase 4: US2 — released semantics through Foundation (P1)

Independent test: existing host scenarios, combined-outcome recovery and native parents run through the released interpreter, with no second semantic implementation.

- [x] T019 [US2] Implement the authored/executable-to-library graph and nested-work translation in `Elsa.Activities.Bpmn/Internal/`; reuse it in execution, start-trigger and recurring-schedule providers. Preserve authored DTOs and variable declarations.
- [x] T020 [US2] Implement explicit capabilities, host values/refusals, scope invocation correlation and exact WorkTokenId live snapshots in that package; preserve produced-by provenance and typed iteration scopes.
- [ ] T021 [US2] Implement ordered command application and library-verdict/state staging through existing Foundation runtime operations; wrap boundary failures and map coded BPMN errors separately from incident/fault classification.
- [ ] T022 [US2] Add real-host combined-outcome tests: non-interrupting handler start then uncaught fault, first-of-two notifications interrupting, concurrent same-node invocation untouched, semantic terminal precedence, outward signal preservation and exact original incident absorption. Establish failing controls.
- [x] T023 [US2] Implement `BpmnProcess` combined outcome handling and verdict-aware local command reconciliation: retire exact handles between signals, consume later notifications for retired handles, retain terminal fault evaluation and suppress all unissued starts/inadmissible cancellations on final Fault. Do not prune between batch items.
- [ ] T024 [US2] Prove combined outcome durability with fresh EF host, split/concurrent outbox claims, dispatch failure and replay after parent commit before acknowledgement in the existing runtime persistence test project.
- [ ] T025 [US2] Re-run and extend native Sequence and outer Flowchart witnesses in the runtime EF project for both orders, cancellation, typed fault and replay, preserving parent-specific fault status and exactly-once semantic advancement.

## Phase 5: US3 — binding and Studio compatibility (P1)

Independent test: normal registered APIs and real Workbench/Studio import/edit/publish/execute/export retain the frozen contracts and supported behavior.

- [x] T026 [US3] Extract/register the work binder in `Essentials/src/Activities/Bpmn/Interchange/`; preserve Delay, Event, PublishEvent, DispatchWorkflow and nested-process policy. Add registration tests that fail when importer/exporter/binder interfaces are missing.
- [x] T027 [US3] Preserve representable release descriptors/data and report exact unsupported/degraded capabilities; Unknown never qualifies readiness. Add affected interchange tests for date/cycle/signal/typed-field limits without silently erasing work.
- [x] T028 [US3] Fix default-flow translation from source `DefaultFlowId` and prove real registered import/export plus Studio edge preservation in the BPMN interchange and Studio Workflows tests.
- [ ] T029 [US3] Prove ordinary bound-task compensation and interrupting/non-interrupting Call Activity escalation through the actual Foundation host; preserve dispatcher result/error identity.
- [x] T030 [US3] Compare real published BpmnProcess/BpmnDecision descriptors against the frozen fingerprint; keep `docs/reports/evidence/bpmn-extraction-baseline/` artifacts unchanged and reconcile actual authoring payloads with Studio's mirror.
- [x] T031 [US3] Run the real source-built Workbench/Studio import → edit → publish → execute → export journeys, capture source-bound screenshots/receipts in `specs/1006-bpmn-library-adoption/evidence/`, and retain browser proof limits.

## Phase 5 refinement: called-workflow control delivery (FR-014)

The spec refinement (`0cf0a03f7`) and [control transport plan](contracts/called-workflow-control.md) were reviewed in order before these tasks. Existing IDs remain stable; T039–T045 are dependencies of T029 and the deletion/final gates, not work to do after merging. Independent correctness, standards and acceptance reviews completed; the story-label format finding was corrected before transport source implementation.

- [x] T039 [US3] Add failing contract/legacy-wire controls, then the bounded versioned generic control batch, exact source transport envelope and additive activity-state/continuation/waited-result projections in `Essentials/src/Workflows/Runtime/Core/` and `Essentials/src/Activities/DispatchWorkflow/Runtime/`. Preserve old constructors and absent-field defaults; validate bounded identifiers/count/payload, unsupported versions and malformed source identities. Track intentional public API deltas separately from the frozen authoring oracle.
- [x] T040 [US3] Implement compiled generic consumer capability and wait-mode admission from exact child scheduling provenance in the executable compiler/DispatchWorkflow package; add the scoped boundary/authorizer and child-start lookup hint. Validate committed admitted dispatch/caller/direct parent/root/executable/tenant/partition/test-scope identity; refuse Pending and cancellation before admission. Controls must reject arbitrary roots, parent-ID-only claims, mismatched callers, foreign source and fire-and-forget while preserving legacy/no-capability behavior.
- [ ] T041 [US3] Implement producer root-local committed ordinals and control projection through the existing structural checkpoint path. Suppress separate nonterminal emission at a terminal root checkpoint. Implement retained-root lookup with current-change overlay and mandatory fresh terminal barriers, including empty success, ordinary exception/fault and cancellation with no authored classification. Test root-fault checkpoint followed by later workflow-terminal checkpoint with no root in its changed rows.
- [ ] T042 [US3] Implement deterministic nonterminal outbox intent and terminal single parent-resume/result enrichment in `Essentials/src/Activities/DispatchWorkflow/Runtime/`; include exact source commit/intent/outbox item IDs and validate immutable persisted payload/ordinal/target/result. Preserve existing equivalent committed resume authority and fixed diagnostics. Resume completes the ordinary leaf with its existing outcome; never fabricate a local fault or foreign incident absorption. Test forged/missing authority and legacy result deserialization.
- [ ] T043 [US3] Implement generic exact-call ordinal inbox and child-control callback delivery in `Essentials/src/Activities/Runtime/` using existing StructuralCallbackEvaluation/parent-completion machinery. Atomically commit cursor/pending/processed-child contributions with parent state and owed work on Defer, Complete, intentional Fault and Cancel; include callback-free buffered acceptance. Test future ordinal buffering, contiguous release, duplicates, conflict refusal and cleanup/retention constraints without assuming outbox claim order.
- [ ] T044 [US3] Wire BpmnProcessRuntime to the declared generic consumer and capability service. Fold contiguous control batches with exact call token/handle, ordered signals and terminal classification in one local evaluation before pruning/command application. Add failing controls for split terminal batches, sibling identity, missing token, premature starts and wrong authored error code; preserve current same-workflow fault/incident behavior and ordinary DispatchWorkflow Faulted outcomes.
- [ ] T045 [US3] Prove the complete bridge through actual Foundation host plus fresh EF providers: coded error match/mismatch; both escalation kinds during execution and terminal delivery; signal-plus-failure precedence; concurrent same-binding calls; exact child-workflow cancellation; duplicate delivery, parent commit before acknowledgement and split/concurrent claims. Deliver empty terminal-success and ordinary-fault barriers before an earlier escalation, restart with the gap buffered and prove no early advancement plus ordered eventual delivery. Confirm actual child status/incident fidelity, safe diagnostics, no-capability legacy recovery and byte-identical authoring fingerprint. Review this increment independently; it does not replace T024's local combined-outcome or T029's ordinary-compensation proof.

T040/T041 follow T039; T042 follows both; T043 follows T042; T044 follows T043; T045 follows T044. Source implementation remains sequential. T029 cannot close before T039–T045 and its ordinary-task compensation witness pass. T032 and T036–T038 depend on them. No production acceptance issue or ADR is closed by this refinement alone.

## Phase 6: Delete duplication and qualify the ordered switch

- [ ] T032 After actual captured-fixture/upgraded-adapter proof, delete the duplicated interpreter/graph/coordinator/mutator/behaviors/semantic state and `PruneForPersistence` from `Elsa.Activities.Bpmn`; retain only authored compatibility DTOs and host adapter responsibilities.
- [ ] T033 Add/mutation-test public Semantics simple-name collision and explicit deleted-semantics guards in the BPMN test project. Do not ban retained authored DTOs simply because their names also occur in Model.
- [ ] T034 Reconcile every moved/changed scenario against the permanent 242-method audit in `specs/1006-bpmn-library-adoption/evidence/test-reconciliation.md`; preserve host assertions and name released replacements/reasons for intentionally changed expectations.
- [ ] T035 Update affected BPMN/interchange READMEs, API surface/version evidence and relevant extension-point references with delivered behavior and exact limits. Complete spec status/checklist cleanup only when implementation is actually ready.
- [ ] T036 Run affected whole projects with discovery/execution counts, relevant Studio suites/browser proof, architecture guards, generated maps and locked solution restore when references change; record red/green controls and retained failures/skips on the PR.
- [ ] T037 Review the complete switch PR to convergence with independent correctness/standards/acceptance lenses, record each round and gate evidence, mark ready and retain all required exact-head checks/protection/human holds.
- [ ] T038 Merge only under the existing review/standing-authorization rules, verify resulting main and checkpoint #2735/#2892. Keep ADR0063 proposed until the third ordered PR; keep #2898/#2907 and unproved production criteria open.

## Dependencies and execution

T001–T005 precede source implementation. Gate A T006–T012 precedes T013 or any writer deletion. US1 compatibility tests follow actual capture; US2's graph/runtime boundary enables their upgraded-host completion. T014–T025 may therefore iterate together after capture, but acceptance requires all US1 and US2 evidence. US3 uses the same released boundary and is independently validated before deletion/merge. T032 requires captured fixtures and upgraded-adapter proof; T036–T038 require every story's evidence.

Independent read-only reviews may run concurrently. Source implementation is sequential in this session unless separately authorized. Parallel test opportunities, when machine build slots allow, include runtime vs interchange whole projects, Studio unit checks vs backend builds, and independent fixture review; dependent edits/pin changes/capture waits are serial.

The first small deliverable is reviewed planning plus verified legacy captures in the single switch draft. It does not establish adoption acceptance. Continue through the complete switch; do not substitute a planning-only merge for the second ordered PR.

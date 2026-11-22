# Tasks: Step bodies change workflow state

**Input**: [spec](spec.md), [plan](plan.md), [research](research.md), [data model](data-model.md), [contract](contracts/workflow-state.md), [validation guide](quickstart.md).

All 40 tasks below have been executed. Local validation and independent source/visual review converged on the existing isolated branch and [PR #5415](https://github.com/elsa-workflows/elsa-foundation/pull/5415), which retains the raw receipts and exact reviewed/tested heads. Formal initiative integration, review and coordinator merge remain separate; CI is deferred. No ADR decisions or broader graph support are authorized by this task list. Tests followed the plan's risk-based cadence.

## Phase 1 — Setup

- [X] T001 Re-read the approved `specs/1234-step-workflow-state/spec.md` and exact merged initiative head; record approved scope/source on the existing PR and take exclusive ownership of its isolated checkout/branch without editing the shared root.
- [X] T002 Inventory current commit/replay/protection call sites against `specs/1234-step-workflow-state/research.md`, checking incoming initiative changes before implementing the named paths.
- [X] T003 Define exact affected suites and owned composed-host journeys from `specs/1234-step-workflow-state/quickstart.md`; record baseline failures/not-run states on the implementation PR.

## Phase 2 — Shared capability and placement foundation

- [X] T004 Add additive `IStepWorkflowState`, native capability declaration and `ActivityExecutionContext.WorkflowState` in `Essentials/src/Activities/Runtime/Core/Contracts/IStepWorkflowState.cs`, `Attributes/AccessesWorkflowStateAttribute.cs` and `Models/ActivityExecutionContext.cs`; update that project's `PublicAPI.Unshipped.txt` without adding a Workflows.Runtime dependency.
- [X] T005 Carry the independent workflow-state capability signal through native contract/descriptor discovery in `Essentials/src/Activities/Design/Reconciliation/Clr` and the existing authoring models in `Essentials/src/Activities/Design/Core`; preserve existing public overloads and API baselines.
- [X] T006 Analyze literal known/computed/opaque script effects in `Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/Services/ScriptBodyWriteAnalyzer.cs` and pin them through `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/StepVariableWriteMetadata.cs` into executable metadata; do not blanket-refuse known read-only graph scripts.
- [X] T007 Extend `Essentials/src/Workflows/Runtime/Core/Background/BackgroundEligibility.cs` and authoring placement validation for independent workflow-state use, including identity-only writers; add publish/runtime bypass controls in `Essentials/tests/Workflows/Design/Tests/StepWorkflowStateValidationTests.cs` and existing background eligibility tests.

## Phase 3 — US1: one atomic body capability

**Goal**: native and script bodies read/write all three state kinds within the established evaluation and reject failures atomically.

**Independent criterion**: mixed variable/output/identity/result workflow passes; fault/cancel/caught refusal/invalid completion and capture-conflict controls leave no successful effects.

- [X] T008 [US1] Implement transient copies, explicit presence, last-assignment intents, sticky refusal and protection state in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/StepState/StepWorkflowState.cs`, using existing intrinsic normalization/slot semantics.
- [X] T009 [US1] Add policy-aware immutable preparation in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/StepState/StepStateCommit.cs`; reuse or extract the runtime-owned intrinsic preparation seam in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/WorkflowIntrinsicExecutor.cs` through contracts, preserving runtime domain boundaries; create Any/InstanceInline new outputs and Any replacements retaining destination policy/storage minima through ValuePolicyCombiner and existing encoders, refusing unavailable/incompatible policies.
- [X] T010 [US1] Wire fresh invoke sessions and combine state/header/frame/durable changes in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`; captures follow step intents, duplicate physical targets fold explicitly, and all failure/disposal paths discard successful effects.
- [X] T011 [US1] Mirror the session/commit lifecycle for completion and successful suspension in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowResumeBookmarkSchedulerWorkHandler.cs`; earlier evaluations survive later faults.
- [X] T012 [US1] Add the narrow optional script adapter request in `Essentials/src/Expressions/JavaScript/Core/Contracts/IJavaScriptScriptEvaluator.cs` and `Essentials/src/Activities/Scripting/Services/StepScriptWorkflowState.cs`; pass it from `Activities/RunJavaScript.cs` with conservative protection and sticky refusal checked after evaluation/serialization.
- [X] T013 [US1] Install bounded `workflowState` methods in `Essentials/src/Expressions/JavaScript/Jint/Services/JintJavaScriptScriptEvaluator.cs` using the existing value bridge/limits; preserve undefined returns, null/missing distinctions, sticky requireExactIdentity guards with runtime-owned VF-STATE-005 and exactly-once JSON encoding; ordinary input evaluation never receives the capability.
- [X] T014 [US1] Add native session/atomicity behavior tests in `Essentials/tests/Activities/Runtime/Tests/VariableLifecycle/StepWorkflowStateTests.cs` for repeated writes/copies/null, mixed frame/header updates, suspension, rollback, namespace distinctions and captured host refusal.
- [X] T015 [US1] Add script bridge tests in `Essentials/tests/Expressions/JavaScript/Jint/Tests/ScriptWorkflowStateTests.cs` and `Essentials/tests/Activities/Scripting/Tests/RunJavaScriptContractTests.cs` for calls/returns, missing/null, invalid payload/name/arity, caught refusals, tainted public identity, Any typed/external replacement with destination policy/storage retained, constant replacement of protected destinations, combined source protection/refused policy and serialization hooks.
- [X] T016 [US1] Add input-expression/unmarked/structural/background/closed-graph bypass and protected-output tests in `Essentials/tests/Activities/Runtime/Tests/StepWorkflowStateBoundaryTests.cs`; retain S-7 graph-safe read-only behavior and existing precision/withholding domain codes.

## Phase 4 — US2: correlation never rebinds waits

**Goal**: change current workflow identity and its projection without changing any existing wait key.

**Independent criterion**: old and new explicitly bound waits resume only under their captured keys after restart; failed identity change leaves all keys unchanged.

- [X] T017 [US2] Verify state preparation generates no bookmark/registration/stimulus changes in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/StepState/StepStateCommit.cs` and reuse identity projection normalization from `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimeWorkflowStateSeed.cs`.
- [X] T018 [US2] Add old-wait/change/new-wait and failed/cancelled change controls to `Essentials/tests/Activities/Runtime/Tests/EventCorrelationRoutingTests.cs`, including later explicit independent correlation and same-body pending reads.
- [X] T019 [US2] Add retained-key/restart assertions using existing provider fixtures in `Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/StepCorrelationRecoveryTests.cs`; inspect bookmark and workflow identity projection rows, not just final headers.

## Phase 5 — US3: faithful Elsa 3 conversion

**Goal**: supported body setters/getters lower to the native capability, with unchanged pure-expression and graph refusals.

**Independent criterion**: real import→publish→run matches native output/identity/result; source semantic/unsupported-form controls prove faithful lowering or stable refusal.

- [X] T020 [US3] Extend `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs` with direct body setter lowering and same-body correlation/name getter lowering; emit requireExactIdentity guards and reject normalization-changing literal identities; preserve source ranges/scopes, once-only argument ordering, short-circuit behavior and undefined setter values.
- [X] T021 [US3] Preserve/refine stable refusal/guidance for input expressions, alias/opaque helpers, root collisions, spread/wrong arity, setInput/setResult and graph bodies in `Extensions/Elsa3/src/Mapping/Internal/Elsa3ImportDiagnosticCodes.cs`, `Services/Elsa3ExpressionRewriter.cs` and existing reusable analysis/guidance.
- [X] T022 [US3] Integrate converted effect/value-kind metadata and explicit JSON output policy with the existing script body mapping under `Extensions/Elsa3/src/Mapping`; do not infer reusable-output mapping or broaden activity-result readers.
- [X] T023 [US3] Add conversion tests in `Extensions/Elsa3/tests/Mapping/Tests/Elsa3WorkflowStateConversionTests.cs` for all setters/getters, computed names, value positions, local shadowing, nested/short-circuit arguments and every retained refusal; include literal/computed blank identities, unchanged nonblank/null values and caught exact-identity guard refusal.
- [X] T024 [US3] Add native-equivalence import→publish→run plus throwing/protected/bypass cases in `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/RunJavaScriptSetsWorkflowStateRunTests.cs` and retain the existing graph/reusable caller-output limitations as explicit controls.
- [X] T025 [US3] Update `Extensions/Elsa3/src/Mapping/README.md`, `docs/reference/elsa3-migration-readiness.md` and `Essentials/src/Activities/Scripting/Activities/RunJavaScript.cs` author documentation for supported body helpers and surviving limitations; link existing graph follow-up from the PR without closing its broader scope.

## Phase 6 — US4: persistence and recovery

**Goal**: existing checkpoints durably accept all effects or none; redelivery and recorded-completion handling do not omit state-only writes.

**Independent criterion**: injected precommit failure and postcommit redelivery prove consistent state and one accepted continuation, with fresh resumed sessions.

- [X] T026 [US4] Audit/extend recorded-completion eligibility and acceptance in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`, `WorkflowResumeBookmarkSchedulerWorkHandler.cs` and existing alteration/recovery consumers so output/identity-only effects cannot be replayed as captures alone.
- [X] T027 [US4] Add state-only capture conflict, stale variable Fault rollback, retry/resume and duplicate delivery controls in `Essentials/tests/Activities/Runtime/Tests/VariableLifecycle/StepWorkflowStateRecoveryTests.cs`, reusing existing S-7 redelivery/lost-update harnesses.
- [X] T028 [US4] Add EF participant failure/transaction rollback and post-marker redelivery tests in `Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/StepStateCheckpointTests.cs`; prove header, identity/output rows, frame changes and queued successors share the existing atomic checkpoint.
- [X] T029 [US4] Verify Durable/Balanced/Ephemeral working-state and flush behavior, including Ephemeral RecordCompletion on/off and previously persisted runs through existing runtime persistence tests in `Essentials/tests/Workflows/Runtime/Tests`; demonstrate no per-setter persistence and retain failed/not-run provider states in the PR receipt.

## Phase 7 — US5: Studio and inspection

**Goal**: show possible node effects and committed/failed evaluation state through existing authoring/instance views without a new payload-history store.

**Independent criterion**: composed Studio author/import/run/inspect journeys pass and an independent reviewer inspects current-head downloadable screenshots/traces with no verified findings.

- [X] T030 [US5] Add value-free accepted state-kind attribution to existing activity inspection models/projection under `Essentials/src/Workflows/Runtime/Core/Models` and `Essentials/src/Workflows/Runtime/Api/Models/WorkflowExecutionViews.cs`; reuse existing instance output/identity read models and capture-policy value endpoints.
- [X] T031 [US5] Test header/output/inspection consistency, failure versus committed attribution and protected payload permissions in `Essentials/tests/Workflows/Runtime/Api/Tests/StepWorkflowStateInspectionTests.cs`.
- [X] T032 [US5] Show known/conservative node effects in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx` using existing effect UI primitives and pinned descriptor facts; include native and imported bodies.
- [X] T033 [US5] Present committed outputs/current identity and value-free effect attribution in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflowInstanceInspection.ts`, `WorkflowInstanceTimeline.tsx` and `workflow-editor/WorkflowInstanceInspectionFacts.tsx`, preserving failure/cancel/protected/empty states.
- [X] T034 [US5] Add interaction/regression cases in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowInstanceInspection.test.tsx` and affected Activity Properties tests; run affected whole frontend suites.
- [X] T035 [US5] Extend owned composed-host journeys in `Studio/tests/acceptance` and the journey/artifact inventory in `tools/ux-acceptance/run.py` for native/imported authoring and success/fault/cancel/protected inspection, narrow/themes/keyboard; retain exact-head artifacts and an independent inspected UX verdict on the implementation PR.

## Phase 8 — Cross-cutting delivery

- [X] T036 Run the affected project builds/tests and verified negative controls in `specs/1234-step-workflow-state/quickstart.md`, recording exact source SHA, shared-machine load, commands/counts and all platform/provider failures or not-run states on the implementation PR.
- [X] T037 Update touched public surfaces and domain extension catalogs (`Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/EXTENSION_POINTS.md`, relevant Expressions/Runtime catalogs and root `EXTENSION_POINTS.md` only if its links change); document registrations and avoid unnecessary new public implementations.
- [X] T038 Review the full implementation diff, issue/PR discussions and composed UX independently for correctness, standards and acceptance; verify every finding against source/reproduction, fix surviving findings in new commits, and repeat until a fresh independent round returns no verified findings. Record exact heads and false-positive/out-of-scope evidence on the implementation PR.
- [X] T039 Set `specs/1234-step-workflow-state/spec.md` to Implemented in the completing implementation PR only after the approved scope/local/UX acceptance is actually delivered; remove terminal checklist residue under the lifecycle rule and retain design/validation artifacts.
- [X] T040 Post final scope/validation/review receipts on the implementation PR and a factual pointer on the initiative, explicitly linking the remaining graph work and stating CI deferred plus outstanding formal approval/merge gates; do not claim the initiative pre-merge gate or main landing.

## Dependencies and execution strategy

Phase 1 → capability/placement foundation → US1. US2 and US3 follow US1; US4 follows the runtime commit integration, and US5 follows the pinned metadata/inspection shape. Cross-cutting completion waits for all five stories. US1 is the smallest useful increment, but it is not the full delivery acceptance.

Independent test/doc work can run in parallel when it touches different files: US1 native tests (T014) versus script adapter tests (T015); US2 routing tests (T018) versus converter semantic tests (T023) after the runtime contract settles; US4 provider tests (T028) versus Studio UI tests (T034) after inspection shape settles. Shared invoke/resume/compiler/rewriter edits stay sequential. Heavy builds/tests never run concurrently in this session.

## Requirements traceability

| Requirements | Tasks |
|---|---|
| FR-001–FR-004, FR-007–FR-008 | T004, T008–T016 |
| FR-005–FR-006 | T009–T011, T014, T026–T028 |
| FR-009 | T017–T019 |
| FR-010–FR-012 | T005–T007, T012–T016, T032 |
| FR-013–FR-016 | T020–T025 |
| FR-017–FR-018 | T010–T011, T019, T026–T029 |
| FR-019 | T008–T009, T012–T016, T024, T031, T035 |
| FR-020–FR-021 | T030–T035 |
| FR-022; SC-001–SC-005 | T014–T016, T018–T019, T023–T024, T027–T029, T031, T034–T040 |

**Task count**: 40 (setup 3; foundation 4; US1 9; US2 3; US3 6; US4 4; US5 6; completion 5). All story-phase tasks have story labels; all tasks have sequential IDs and concrete paths. The parallel examples describe dependency-safe opportunities without authorizing simultaneous shared-machine builds.

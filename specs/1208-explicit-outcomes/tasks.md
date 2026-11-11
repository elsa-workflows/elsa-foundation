# Tasks: Explicit outcomes

**Input**: Design documents from `specs/1208-explicit-outcomes/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/outcome-selection.md, quickstart.md

**Provisional**: do not start any task before this spec is approved and #3052's specs are defined. That means #3389 is
agreed and row 14 is decided (see the spec's *Approval* and Q1). T001 is the gate.

**Tests**: framework §2.23 requires them for every logic-bearing change, and SC-001 to SC-006 require them too. The
application declares no TDD cadence, so within each story tests follow the implementation. One exception is written
first: T013, which reproduces #3295 by converting, publishing and running. The PR's bite-proof shows each new test
failing without the change.

**Build rule**: build and test only the projects a task touches (shared machine); CI runs the rest.

**Paths**: under `Essentials/src` unless another root is given.

**Row 14**: tasks that depend on the script outcome API's form are marked *(API)*. They name the setters of the #3389
draft. If the call adds an explicitly recognized returned completion value, only those tasks change.

## Phase 1: Setup

- [ ] T001 Confirm that #3389 is agreed and row 14 is decided. Diff the agreed ADR 0045 §6 and spec 095 text against
  spec.md, and amend spec, plan, contract and these tasks for any change (including the answers to Q1–Q11) before
  starting. Also read Elsa 3's SendHttpRequest (elsa-core) and record whether `Done` fires on "Failed to
  connect" and "Timeout" (research R-7, FR-029a). Confirm that #3195 is fixed, and #3287 for the fixtures that read
  variables: without them no converted RunJavaScript runs, so T013, T015 and T030 cannot pass (research R-7)
- [ ] T002 Check whether the general port defect issue (spec *Out of scope*, research R-8) is still open. If it is
  fixed, drop the port check in T031 and build US6 on the existing one

## Phase 2: Foundational (blocks every story)

- [ ] T003 Add `OutcomeSelection` (`Unspecified`, `Named`, `Empty`; distinct ordered non-blank names; `ResolveNames()`)
  in `Activities/Runtime/Core/Models/OutcomeSelection.cs`, with unit tests for each state and each guard in
  `Essentials/tests/Activities/Runtime/Tests/OutcomeSelectionTests.cs`
- [ ] T004 Change `ActivityTransition.Complete` (both overloads) and `IActivityCompletionTransition` to carry an
  `OutcomeSelection`, with one-name and several-name convenience overloads, in
  `Activities/Runtime/Core/Models/ActivityTransition.cs`; update the "Exactly one" doc comment (L6) and every caller
- [ ] T005 Replace `ActivityCompletion.OutcomeKey` with the selection, bump its document version in
  `ActivityExecutionValueFlowDocumentVersions`, and upcast a stored `OutcomeKey` to `Named([key])`, in
  `Workflows/Runtime/Core/Models/ActivityExecutionValueFlowRecords.cs`; add an upcast test with a stored record of the
  current version
- [ ] T006 Carry the selection in `RuntimeStructuralContinuation` (L30, L41–47), `BackgroundActivityWork` (L149, L177)
  and `BackgroundActivityCompletionCodec` (L54–67), with round-trip tests for all three states
- [ ] T007 Replace every one-name list in the runtime handlers with `selection.ResolveNames()`:
  `WorkflowInvokeActivitySchedulerWorkHandler.cs` L442–447 and L462–466, `WorkflowResumeBookmarkSchedulerWorkHandler.cs`
  L553–555, `WorkflowParentActivityCompletionSchedulerWorkHandler.cs` L747, `StructuralCallbackEvaluation.cs` L434,
  `ChildControlDelivery.cs` L74, `WorkflowIntrinsicExecutor.cs` L217 and L349, `UserTaskRuntime.cs` L103, and BPMN
  `BpmnNativeWorkBoundary.cs` L304/L319 and `BpmnDirectExecutionDriver.cs` L315/L337
- [ ] T008 Keep an empty selection empty: remove the empty → `Done` default from `SchedulerWorkHandlerHelpers.NormalizeOutcomeNames`
  and `ReadCompletionOutcomeNames` for completions that carry a selection, and from `StructuralParentEvaluationSupport.cs`
  L421; keep the old default only for records without a selection (T005 upcast)

## Phase 3: User Story 1 - A script chooses its outcomes explicitly (P1) 🎯 MVP

- [ ] T009 [US1] *(API)* Add an outcome sink to `JavaScriptScriptEvaluationRequest` and return
  `(Value, Selection)` from `IJavaScriptScriptEvaluator` (`Expressions/JavaScript/Core/Contracts/`); register
  `setOutcome(name)` and `setOutcomes(names)` in `Expressions/JavaScript/Jint/Services/JintJavaScriptScriptEvaluator.cs`,
  each replacing the pending selection; `setOutcomes([])` is `Empty`. Keep `JavaScriptExecutionApi.cs` L77–80
  returning the value only
- [ ] T010 [US1] Remove `SelectOutcome`, `UnmatchedOutcome` and routing by return value from
  `Activities/Scripting/Activities/RunJavaScript.cs`; complete with the evaluator's selection; rewrite the doc comment
  (L22–31)
- [ ] T011 [US1] Make `ActivityValueOutcomesAttribute.UnmatchedOutcome` optional (`Activities/Runtime/Core/Attributes/ActivityValueOutcomesAttribute.cs`)
  and declare none on RunJavaScript; update `ClrAssemblyScanner.cs` L392–406 and the activity contract surface
  baseline (`Essentials/tests/Activities/Design/Tests/Contracts/activity-contract-surface.baseline.json` L1670–1677)
- [ ] T012 [US1] Rewrite `Essentials/tests/Activities/Scripting/Tests/RunJavaScriptExecutionTests.cs` L86–141 for US1
  scenarios 1–6 (one, several, none → `Done`, empty, returned string ignored, fault discards the selection) and
  `Essentials/tests/Activities/Behavioral/Tests/Drives/ScriptingDrives.cs`

## Phase 4: User Story 2 - Converted RunJavaScript keeps its routing (P1)

- [ ] T013 [US2] Write first: an Elsa 3 convert-publish-run test for RunJavaScript → `Done` → WriteLine, without and
  with `PossibleOutcomes`, using `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Fixtures/reusable-reads-result.json`
  plus a new fixture with possible outcomes; it fails today (#3295)
- [ ] T014 [US2] Remove `[Outcome("Done", "Unmatched")]` from `Extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs`
  L295–303 and `Unmatched` from `EmittedOutcomes` in `Elsa3FlowchartGraphBuilder.cs` L278–295; update
  `Elsa3ActivityResolverTests.cs` L148/L157
- [ ] T015 [US2] Map Elsa 3 SendHttpRequest `Done` onto every response outcome when expected codes are set
  (`Extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs` L216–224, per T001), narrow
  `ELS3-BEHAVIOUR-HTTP-OUTCOME-SINGLE` to the `Failed` difference (`Elsa3ActivityNormalizer.cs` L1041), and add a
  convert-publish-run test for US2 scenario 4
- [ ] T016 [US2] Add a corpus assertion that no converted RunJavaScript connection is on `Unmatched` (US2 scenario 3),
  next to the spec 236 member-map counts

## Phase 5: User Story 3 - An unknown outcome name is caught (P1)

- [ ] T017 [US3] In `Activities/Runtime/Elsa.Activities.Runtime/Services/ActivityCompletionProjector.cs` L55 (on `main` after #3801): check
  every selected name against the node's pinned set and the multi-outcome flag (no node-level fallback, Q6), and
  fault with the `VF-ACT-006` code on the fault record instead of a message prefix; same in
  `BackgroundActivityCompletionCodec.ValidateOutcome`. Update `ActivityCompletionContractTests.cs` L46 and
  `WorkflowParentActivityCompletionSchedulerWorkHandlerTests.cs` L30 to assert the code
- [ ] T018 [US3] *(API)* Add `ScriptOutcomeLiteralValidator` in `Workflows/Publishing/Elsa.Workflows.Publishing/Services/`:
  parse RunJavaScript scripts with Acornima, find calls of the script outcome API with literal arguments, and report
  `VF-AUTH-010` for names outside the node's set; tests for literal, array literal, computed (no diagnostic) and
  template-literal arguments
- [ ] T019 [US3] Tests for US3 scenarios 2–4: computed unknown name, an activity-declared catch-all, several names on a
  single-outcome node
- [ ] T019a [US3] Publication checks: `VF-AUTH-013` for a node without `Done` whose activity cannot guarantee a
  selection (FR-002b), with conformance tests that If, Switch and SendHttpRequest with expected codes select on every
  completion; `VF-AUTH-012` warning for a RunJavaScript that returns a string literal equal to a connected outcome
  name (FR-008a); tests for US1 scenario 7 and US3 scenario 5

## Phase 6: User Story 4 - Several outcomes run as an inclusive fork (P2)

- [ ] T020 [US4] Return the outcome set with `AllowsMultiple` from `ExecutableNodeCompiler.ResolveOutcomes`
  (`Workflows/Publishing/Elsa.Workflows.Publishing/Services/ExecutableNodeCompiler.cs` L623–688), pin it in the
  executable, and add `Done` to the set of every activity that can complete unspecified (Q2); add a static declaration
  (`AllowsMultiple`) on the activity outcome attributes and a provider hook for configuration-dependent flags (Q10)
- [ ] T021 [US4] Apply the `Done` rule (FR-002, FR-012a): drop `Done` from SendHttpRequest's pinned set when expected
  codes are set (`ExecutableNodeCompiler.cs`, `SendHttpRequest.cs` L56), fault an unspecified selection on a node
  without `Done` with a message that names the activity's code (FR-002a, `ActivityCompletionProjector.cs`), and add
  an architecture test that no built-in activity declares `Done` next to an always-selected branch set
- [ ] T022 [US4] In `Activities/Flowchart/Internal/FlowchartGraph.cs` L457–464 and
  `Internal/Policies/FlowchartPolicyConnectionSelector.cs` L31–32, stop mapping an empty set to `Done`; remove the
  duplicate-target fault in `FlowchartExecutionEngine.cs` L144–145 and `FlowchartGraph.cs` L151–152 so each taken
  connection is an arrival at the target's implicit join (Q4)
- [ ] T023 [US4] Flowchart engine tests: two of three outcomes taken and joined (ADR 0064 dead path on the third); two
  outcomes to one target run it once; empty selection ends the path and leaves a parallel branch running
- [ ] T024 [US4] Expose `AllowsMultiple` to the unordered-write analysis, inspection and Studio
  (`ExecutableNodeCompiler`, `WorkflowExecutableInspection.cs`); restart-recovery test for all three states (US4
  scenario 4)

## Phase 7: User Story 5 - Elsa 3 multi-outcome constructs convert (P2)

- [ ] T025 [US5] Add Switch `Mode` (`MatchFirst` default, `MatchAny`) in `Activities/ControlFlow/Switch/Activities/Switch.cs`
  and `Internal/SwitchNavigator.cs` L80–91, with the flag provider for `MatchAny`, and tests
- [ ] T026 [US5] Let Graph `Complete` (`Activities/Graph/Runtime/Activities/Complete.cs` L35–56), the Finish intrinsic
  (`WorkflowIntrinsicExecutor.cs` L325–349) and `GraphActivity.cs` L124–131 carry several outcomes when the graph
  declares the flag; tests
- [ ] T027 [US5] *(API)* Convert `setOutcome` / `setOutcomes` instead of refusing them: remove them from
  `RefusedHostFunctions` in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs` L510–522, rewrite to the
  script outcome API, and preserve routing per Q7: the known set from `PossibleOutcomes`, literals and connected
  ports, plus a converter-declared catch-all with no connection that the script selects for any other name, with
  Elsa 3 `Done` mapped onto it; refuse with guidance when no faithful route exists or `PossibleOutcomes` is empty and
  the name is computed; no warning-only conversion; update
  `Elsa3JavaScriptHostFunctionTests.cs` L183–184 and `Elsa3JavaScriptConversionTests.cs` L118
- [ ] T028 [US5] Convert Switch `MatchAny` in `Elsa3SwitchValueSynthesisRewrite.cs` L26–38 and Complete with several
  literal outcomes in `Elsa3TerminalPlacementRewrite.cs` L101–107 (an expression outcome stays refused)
- [ ] T029 [US5] Remove `ELS3-BEHAVIOUR-SCRIPT-OUTCOME-ROUTING` (`Elsa3ActivityDiagnosticCodes.cs` L128–129,
  `Elsa3ActivityNormalizer.cs` L1049, `Elsa3ConversionDiagnosticsTests.cs` L250–251)
- [ ] T030 [US5] Convert-publish-run fixtures for each construct (US5 scenarios 1–5, FR-030), including a computed name
  outside the known set that ends the path without a fault

## Phase 8: User Story 6 - Ports are known at design time (P3)

- [ ] T031 [US6] Check every connection's source port against the source node's pinned set in
  `Activities/Flowchart/Internal/FlowchartStructureHandler.cs` L71–85 and report `VF-AUTH-011`; pin value-derived names
  as written and compare connection ports exactly (research R-8); tests
- [ ] T032 [P] [US6] Studio: in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflowAdapter.ts`
  L1189–1217, show `Done` instead of the unmatched port for RunJavaScript, show Switch `MatchAny` ports, and flag
  connections on ports that are gone; update `__tests__/dynamicOutcomePorts.test.ts`
- [ ] T033 [P] [US6] Bring `Essentials/tests/Activities/Testing/WorkflowExecutionHarness.cs` L770–790 in line with
  `ResolveOutcomes`, including value-derived outcomes and the flag

## Phase 9: Polish

- [ ] T034 [P] Update the documents in research R-10, and replace spec 247 FR-019 with a pointer to FR-027 (FR-033)
- [ ] T035 [P] Update `docs/reference/elsa3-migration-readiness.md` L24, L80 and `Extensions/Elsa3/src/Mapping/README.md`
  L97, L213 for the newly convertible constructs
- [ ] T036 Run quickstart.md by hand in the Workbench and Studio
- [ ] T037 Set the spec's status per the spec lifecycle in the finishing PR

## Dependencies

- **T001 gates everything.** T003 to T008 block every story.
- **Stories**:
  - US1 needs T003–T008.
  - US2 needs T020 (`Done` in RunJavaScript's set), T021 (the `Done` rule, for scenario 4) and T014; T013 comes first.
  - US3 needs T020.
  - US4 needs T003–T008 and T020.
  - US5 needs US1 (T009) and US4 (T020).
  - US6 needs T020.
- **Parallel**: T032 and T033 with T031; T034 and T035 together.

## Implementation strategy

1. **MVP**: US1 with T020, so a script selects explicitly and RunJavaScript's set has `Done`.
2. Then US2 (#3295) and US3 (validation), which close the routing gap.
3. Then US4 (inclusive fork), US5 (Elsa 3 constructs) and US6 (ports).

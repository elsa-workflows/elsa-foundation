# Tasks: Warn about writes to one variable whose order is not defined

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/unordered-write-warning.md](contracts/unordered-write-warning.md)

**Tests**: xunit only. Risk-based behaviour tests per framework §2.23 for the new analysis and every built-in
declaration, and feature-registration tests for the analyzer's composition (review round 2). No TDD cadence is declared; tests follow
implementation within each phase.

## Phase 1: Setup

- [X] T001 Merge `origin/initiative/3052-elsa3-to-elsa4-conversion` into the branch and confirm spec 266 still fits
  the agreed ADRs (research R-2)

## Phase 2: Foundational (blocks every story)

- [X] T002 Add `ActivityChildTimeRelation` in `Essentials/src/Workflows/Design/Core/Models/ActivityChildTimeRelation.cs`
- [X] T003 Add `RelateChildren` and `CompletesAfterItsChildren` with defaults to
  `Essentials/src/Workflows/Design/Core/Contracts/IActivityStructureHandler.cs` and forwarding members (with
  defaults) to `Essentials/src/Workflows/Design/Core/Contracts/IActivityStructureService.cs`
- [X] T004 Forward both members in `Essentials/src/Workflows/Design/Core/Services/DefaultActivityStructureService.cs`
- [X] T005 Implement the analysis (writes, rules of data-model.md, findings, message): contract
  `IUnorderedVariableWriteAnalyzer` and findings in `Essentials/src/Workflows/Design/Validations/Core/`, internal
  `UnorderedVariableWriteAnalyzer` in `Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/Services/`,
  registered by `WorkflowDesignValidationsFeature` (moved out of Design.Core in review round 1)
- [X] T006 [P] Analysis tests over a test-only structure service (ordered, unordered, exclusive, undeclared,
  self-overlap, early release, three writers, duplicate capture target) in
  `Essentials/tests/Workflows/Design/Tests/UnorderedVariableWriteAnalysisTests.cs`

## Phase 3: User Story 1 - Unordered writes in a workflow are reported (P1)

**Independent test**: a Parallel with two capturing branches warns, names both, and promotes.

- [X] T007 [US1] Declare Parallel (unordered; completes after children unless threshold < branches) in
  `Essentials/src/Activities/ControlFlow/Parallel/Internal/ParallelStructureHandler.cs`
- [X] T008 [US1] Declare Sequence (index order, completes after children) in
  `Essentials/src/Activities/Sequence/Internal/SequenceStructureHandler.cs`
- [X] T009 [US1] Declare Flowchart (reachability with waiting joins, `merge` rule, trigger-entry exclusivity,
  forks whatever the ports (D-2a as amended), merge self-overlap; completes after children unless a `firstWins` node exists) in
  `Essentials/src/Activities/Flowchart/Internal/FlowchartChildResultAnalysis.cs` and `FlowchartStructureHandler.cs`
- [X] T010 [US1] Replace `ValueFlowValidator`'s concurrency code with the analysis (captures + Set/Merge/Reduce,
  warning severity, duplicate capture warning) in
  `Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/Validators/ValueFlowValidator.cs`
- [X] T011 [US1] Update the existing intrinsic tests to expect warning severity, and add Story 1 scenarios 1, 3, 4,
  5, 6 (promotion), 7 and 8 (threshold Parallel and Flowchart `merge` join) in
  `Essentials/tests/Workflows/Design/Tests/ValueFlowValidatorTests.cs`

## Phase 4: User Story 2 - Exclusive and sequential paths stay quiet (P1)

**Independent test**: If arms, Switch cases, a loop body, a Flowchart path and a waiting join report nothing.

- [X] T012 [P] [US2] Declare If and Switch (mutually exclusive, completes after children) in
  `Essentials/src/Activities/ControlFlow/If/Internal/IfStructureHandler.cs` and
  `Essentials/src/Activities/ControlFlow/Switch/Internal/SwitchStructureHandler.cs`
- [X] T013 [P] [US2] Declare the loops (one child, never overlapping, completes after children) in
  `Essentials/src/Activities/ControlFlow/Loops/LoopStructureHandler.cs`
- [X] T014 [US2] Story 2 scenarios 1–4 and 6 (waiting join orders branch before continuation) in
  `Essentials/tests/Workflows/Design/Tests/ValueFlowValidatorTests.cs`; scenario 5 is covered as the D-2a
  several-outcomes case (research R-2, finding F-1)

## Phase 5: User Story 3 - Unordered writes in a graph activity body are reported (P2)

**Independent test**: a graph body Parallel warns in draft validation and compilation; compilation succeeds.

- [X] T015 [US3] Run the analysis in `GraphActivityProvider.ValidateAsync` and `CompileAsync` and add the warnings in
  `Essentials/src/Activities/Graph/Design/Services/GraphActivityProvider.cs`
- [X] T016 [US3] Story 3 scenarios 1–3, keeping spec 265's two tests unchanged, in
  `Essentials/tests/Activities/Graph/Tests/GraphUnorderedWriteTests.cs`

## Phase 6: User Story 4 - Every structure declares how its children relate, BPMN included (P2)

**Independent test**: a BPMN parallel gateway warns, an exclusive gateway does not, a test-only structure works.

- [X] T017 [US4] Declare BPMN (element-graph analysis of research R-6, multi-instance self-overlap, completes after
  children) in `Essentials/src/Activities/Bpmn/Design/Internal/BpmnTimeRelationAnalysis.cs` and
  `BpmnStructureHandler.cs`
- [X] T018 [P] [US4] Declare CompensationScope (forward steps in order, then handlers in reverse; completes after
  children) in `Essentials/src/Activities/Compensation/Internal/CompensationScopeStructureHandler.cs`
- [X] T019 [US4] Declaration tests for every built-in structure in
  `Essentials/tests/Activities/ControlFlow/Tests/ChildTimeRelationTests.cs` and the BPMN/Flowchart test projects;
  Story 4 scenarios 1–3 (BPMN parallel and exclusive gateway, undeclared structure) in
  `Essentials/tests/Workflows/Design/Tests/ValueFlowValidatorTests.cs`

## Phase 7: Polish

- [X] T020 [P] Record the declaration in `EXTENSION_POINTS.md` of `Elsa.Workflows.Design.Core` and every structure
  module (FR-012)
- [X] T021 [P] Write `docs/reference/variable-write-order.md` (FR-004, FR-014) and link it from
  `docs/reference/activity-definition-authoring-composition.md`
- [X] T022 SC-005: confirm the shared-ancestor Parallel capture test fails with the rule removed
- [X] T024 Review rounds 1-2: the composition check for exactly one singleton analyzer
  (`DataSelectionAnalyzerCompositionCheck`), `GraphActivitiesDesignFeature` requiring it, kept constructor overloads,
  and registration tests in `Essentials/tests/Workflows/Design/Tests/Unit/ValidationsFeatureRegistrationTests.cs`
- [X] T025 F-2 for `Break` (coordinator, 2026-11-17): `IRuntimeActivityExecutionContext.RequestLiveChildrenSubtreeCancellation`
  planned in `StructuralParentEvaluationSupport`, staged by `FlowchartExecutionEngine` on `Break`; tests in
  `Essentials/tests/Activities/Runtime/Tests/BreakPropagationExecutionTests.cs` and `ChildSubtreeCancellationExecutionTests.cs`.
  First-wins race and threshold Parallel handed to #4193
- [X] T023 Build the touched projects and run their tests; set the spec's Status to Implemented

## Dependencies

Phase 2 blocks all stories. US1 and US2 share `ValueFlowValidatorTests.cs` (run in sequence). US3 needs only
Phase 2 and the Parallel/Sequence declarations (T007, T008). US4 is independent after Phase 2.

## Parallel examples

- T012, T013 and T018 touch different handlers.
- T020 and T021 are documentation only.

## Implementation strategy

MVP is Phase 2 plus US1. US2 removes false positives, US3 extends to graph bodies, US4 completes the declarations.

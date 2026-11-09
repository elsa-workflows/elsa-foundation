# Tasks: Elsa 3 converter faithful fixes from the JTest measurement

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [quickstart.md](quickstart.md)

**Tests**: required. Each row changes logic-bearing conversion behaviour (framework §2.23), so each story has a
converting test and a test for the refusal next to it. The tests were written first and run against `main` to confirm
the baseline failures (research.md); this is not a TDD cadence declaration.

## Phase 1: Setup

- [x] T001 Create the worktree `../elsa-foundation-3051` on `agent/claude/3051-elsa3-faithful-fixes` from `origin/main` and claim #3051
- [x] T002 Add `Extensions/Elsa3/tests/Mapping/Tests/Elsa3FaithfulFixesTests.cs` with one region per row; run it on unchanged code and record the failures in research.md

## Phase 2: Foundational

None: the four rows are independent.

## Phase 3: User Story 1 - A WriteLine without text converts (P1)

**Independent test**: `Elsa3FaithfulFixesTests` row 1 tests pass; `PublishEvent` without an event name is still refused.

- [x] T003 [US1] Change the WriteLine rule to `DefaultFlip("Text", "text", "")` in `Extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs`
- [x] T004 [US1] In `CheckRequiredInputs`, accept a required input whose value is the literal its entry's R2 rule emitted, in `Extensions/Elsa3/src/Mapping/Conversion/Elsa3ActivityNormalizer.cs`
- [x] T005 [US1] Change the `required-input-unbound` case to `Elsa.PublishEvent` without an event name in `Extensions/Elsa3/tests/Mapping/Tests/Elsa3ConversionDiagnosticsTests.cs`

## Phase 4: User Story 2 - canStartWorkflow on a non-trigger only picks the flowchart start (P1)

**Independent test**: row 2 tests pass; a Timer used mid-flow is still refused.

- [x] T006 [US2] In `ApplyCustomProperties`, drop the flag (no diagnostic) when the entry declares no `CanStartWorkflow` rule, in `Extensions/Elsa3/src/Mapping/Conversion/Elsa3ActivityNormalizer.cs`
- [x] T007 [US2] In `CheckStartTriggers`, skip a child whose map entry declares no `CanStartWorkflow` rule, in `Extensions/Elsa3/src/Mapping/Conversion/Elsa3FlowchartGraphBuilder.cs`
- [x] T008 [US2] Add a coverage fact: the Elsa 3 triggers (Cron, Event, HttpEndpoint, StartAt, Timer) with a counterpart declare a `CanStartWorkflow` rule, and no other entry does, in `Extensions/Elsa3/tests/Mapping/Tests/Elsa3ActivityMapCoverageTests.cs`

## Phase 5: User Story 4 - VF-IMP-008 judges the normalized contract (P1)

**Independent test**: row 4 tests pass; `Boolean` bound to `SupportedMethods` still raises `VF-IMP-008`.

- [x] T009 [US4] Map the declared Elsa 3 alias to its Elsa 4 shape in `ReadType` (`HttpStatusCode`, `HttpHeaders`, `ObjectDictionary`, generic list syntax), and let an untyped element match when collection kinds agree in `TypesMatch`, in `Extensions/Elsa3/src/Mapping/Services/Elsa3ValueFlowLowerer.cs`

## Phase 6: User Story 3 - ObjectDictionary input converts (P2)

**Independent test**: row 3 tests pass with a JavaScript expression declared `ObjectDictionary`; an `Int32` expression is still refused. Needs T009 for the lowering step.

- [x] T010 [US3] Add `ObjectDictionary` to the JSON-only alias list in `CheckContract`, in `Extensions/Elsa3/src/Mapping/Conversion/Elsa3ActivityNormalizer.cs`
- [x] T011 [US3] Bind the row 3 test's `input` to a JavaScript expression declared `ObjectDictionary` (the JTest shape) in `Extensions/Elsa3/tests/Mapping/Tests/Elsa3FaithfulFixesTests.cs`

## Phase 7: Polish & cross-cutting

- [x] T012 [P] Amend `specs/236-elsa3-activity-map/member-map.md`: the `customProperties.canStartWorkflow` row, the `WriteLine.Text` row and the "Elsa 4 required members with no Elsa 3 source" list
- [x] T013 [P] Update "Behaviour worth knowing" in `Extensions/Elsa3/src/Mapping/README.md` (WriteLine default, non-trigger flag, declared types)
- [x] T014 Run `Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj` in full
- [x] T015 Throwaway JTest check per quickstart.md; delete the probe; record the result on the PR
- [ ] T016 Set the spec status, open the PR linked to #3051, post gate evidence, request review

## Dependencies

- US1, US2 and US4 are independent. US3's lowering step depends on US4 (T009).
- Polish depends on all stories.

## Parallel opportunities

- T003–T005, T006–T008 and T009 touch different methods; T004, T006 and T010 share `Elsa3ActivityNormalizer.cs`, so they run in sequence.
- T012 and T013 are documentation in different files.

## Implementation strategy

One small unit: implement the stories in the order above, run the touched test project, then the JTest check.

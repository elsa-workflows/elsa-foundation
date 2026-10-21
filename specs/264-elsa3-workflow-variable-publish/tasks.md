# Tasks: Elsa 3 converter: workflow variables that can be published

**Input**: Design documents from `specs/264-elsa3-workflow-variable-publish/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/variable-starting-values.md](contracts/variable-starting-values.md), [quickstart.md](quickstart.md)

**Tests**: Required (FR-006, FR-007; framework §2.23 risk-based behaviour tests). The application's derived constitution
declares no TDD, so tests sit beside the implementation they prove.

**Organization**: by user story. US1 (no starting value) is the MVP and fixes #3122 as reported; US2 adds faithful
starting values and the refusal.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Record the JTest baseline (D-7, R-8): a throwaway test in `Extensions/Elsa3/tests/Mapping/Tests` reads every definition under `../JTEST-nexxbiz`, counts workflow and container variables, those with a value, and the definitions that convert end to end today; write the numbers into a "Measured baseline" section of `specs/264-elsa3-workflow-variable-publish/spec.md`, then delete the test

## Phase 2: Foundational

- [X] T002 Add `VariableValueUnconvertible = "ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE"` with an XML summary to `Extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs`
- [X] T003 Register the object converter in the Mapping tests' container (`services.AddSingleton(SerializationServices.ObjectConverter())`) in `Extensions/Elsa3/tests/Mapping/Tests/Support/ConversionServices.cs`

## Phase 3: User Story 1 - An imported workflow with a variable publishes and runs (P1) MVP

**Goal**: a variable with no Elsa 3 value has no starting value, so publication accepts it.

**Independent Test**: import the Event waiter with a workflow variable capturing the Event result, publish, run,
publish the event, read the variable from the root variable frame.

- [X] T004 [US1] In `Extensions/Elsa3/src/Mapping/Conversion/Elsa3VariableMapper.cs`, return `Default: null` when `source.Value` is `null` (D-1)
- [X] T005 [P] [US1] Add fixture `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Fixtures/workflow-variable-captures-event.json`: the `publish-event-waiter` shape with definition id `workflow-variable-captures-event`, a workflow variable `approval` (`Object`, no value), and the Event's `result` captured into it (`"result": { "typeName": "Object", "memoryReference": { "id": "approval" } }`)
- [X] T006 [US1] Add `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/WorkflowVariablePublishTests.cs`: import `workflow-variable-captures-event` with `publish-event-publisher-payload`, publish both, execute the waiter, wait for the Event to suspend, run the publisher, wait until settled; assert Completed, no incidents, and that `run.Workflow.RootVariableFrame.Values["approval"]` holds `PublishEventResumeTests.Elsa3ObjectLiteralPayload`. A second case publishes the waiter with an unused `String` variable and no value (US1 scenario 3)
- [X] T007 [P] [US1] Mapping test in `Extensions/Elsa3/tests/Mapping/Tests/Elsa3VariableStartingValueTests.cs`: a workflow variable and a Sequence variable with no value both convert with `Default == null`

**Checkpoint**: #3122's reproduction publishes and runs.

## Phase 4: User Story 2 - A starting value is the Elsa 3 value (P1)

**Goal**: values follow Elsa 3's rule (R-2) as `Literal` JSON (R-3); unconvertible values are refused (contract).

**Independent Test**: one variable per kind publishes and starts with the data model's value; `Int32` with `"abc"` is
refused at `$.variables[i]`.

- [X] T008 [US2] In `Elsa3VariableMapper.cs`, add `TryMap(IWellKnownTypeRegistry, IObjectConverter, Elsa3Variable, out VariableDefinition?, out string? failure)`: resolve the CLR type (array when `IsArray`), `TryConvertTo(source.Value, clrType)`, serialise the converted value with `JsonSerializer.SerializeToElement`, and build `new ArgumentValue(json, "Literal")`; a conversion or serialisation failure returns `false`. Keep `Map` for validated input, throwing `InvalidOperationException` on failure (D-2, D-3)
- [X] T009 [US2] In `Extensions/Elsa3/src/Mapping/Conversion/Elsa3ActivityNormalizer.cs`, inject `IObjectConverter`; in `NormalizeAsync` check `clone.Variables` before visiting the tree and add a contract refusal per failing variable through `Elsa3ConversionDiagnostics.ForDefinition` at `$.variables[{i}]` with `VariableName` metadata; in `ReadContainerVariables` use `TryMap` and report a failure through `context.Error(VariableValueUnconvertible, …)` (D-4)
- [X] T010 [US2] In `Extensions/Elsa3/src/Mapping/Mappings/Elsa3WorkflowDefinitionToState.cs`, inject `IObjectConverter` and pass it to `Elsa3VariableMapper.Map`
- [X] T011 [US2] Extend `Elsa3VariableStartingValueTests.cs`: one case per data-model row (`String`, `Int32` `"5"`, `Boolean` `"true"`, an enum `HttpStatusCode` `"OK"`, `Object` with JSON text stays text, `List<String>` `["a","b"]`, `ObjectDictionary` `{"a":1}`, `isArray` `String` `["x"]`), each asserting `ExpressionType == "Literal"` and the JSON value; refusals for `Int32` `"abc"` (workflow level, path `$.variables[0]`) and a Sequence variable (container level), each asserting the code, message, guidance and that conversion fails
- [X] T012 [P] [US2] Add fixture `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Fixtures/workflow-variable-starting-values.json`: a regular workflow with one WriteLine and one variable per T011 kind (except `HttpStatusCode`), each with a value
- [X] T013 [US2] Add a case to `WorkflowVariablePublishTests.cs`: import, publish and run `workflow-variable-starting-values`, and assert each variable's value in the root variable frame against the data model's table

**Checkpoint**: every kind starts as in Elsa 3; nothing that converts fails at publish.

**As implemented**: conversion is strict (second clarification), and a starting value on a type Elsa 4's registry does
not know is refused (research R-2). So T011 registers dotted test aliases for the enum and the two collections, and
refuses `List<String>`, `ObjectDictionary`, `HttpHeaders` and an unregistered enum. T012's fixture has no enum variable,
because the import host registers none.

## Phase 5: Polish

- [X] T014 [P] Update `Extensions/Elsa3/src/Mapping/README.md`: how variable starting values convert (data-model table) and the new refusal row with its guidance
- [X] T015 Run the full suites of `Extensions/Elsa3/tests/Mapping/Tests` and `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests`; fix any expectation that relied on the old untyped default (for example reusable manifests whose `initialValue` was a `Literal` `null`)
- [X] T016 Re-measure the JTest corpus with T001's method, record the "Measured after" numbers in `spec.md`, delete the throwaway test, and set `**Status**: Implemented` in `spec.md` when the PR is ready to merge

## Dependencies

- T001 first (baseline before code changes).
- T002, T003 before US2's tests; T003 before T007.
- US1 (T004–T007) before US2 (T008–T013): T008 replaces T004's branch with the full rule.
- T014 any time after T008; T015 after all code; T016 last.

## Parallel examples

- US1: T005 (fixture) and T007 (mapping test) beside T004.
- US2: T012 (fixture) beside T008–T011.

## Implementation strategy

MVP is US1: it alone makes #3122's reproduction publish and run. US2 closes the faithfulness gap for variables with
values and adds the refusal. Both land in one PR.

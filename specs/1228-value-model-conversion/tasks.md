# Tasks: Value model and conversion

**Input**: design documents in `specs/1228-value-model-conversion/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: required by framework §2.23 for every logic-bearing change below, plus one test per acceptance scenario.
This work changes existing code, not greenfield: tests go with each change; no test-first cadence is declared.

**Gates**:
- User Story 4 waits on Q1 (#3422 Q5, `Object` → `Any`).
- User Story 3 waits on the S-10 session write path (#4316) on the initiative branch.
- User Story 6 waits on #3167 (S-0).

Paths: `Jint/` = `Essentials/src/Expressions/JavaScript/Jint/`, `JsCore/` =
`Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/`, `Prim/` = `Essentials/src/Primitives/Primitives/`,
`Pub/` = `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/`, `E3/` = `Extensions/Elsa3/src/Mapping/`.

## Phase 1: Setup

- [ ] T001 Create the precision test suite data (numbers beyond ±2^53, a 29-digit decimal, `10.50`, `2.0`, `1e21`, negatives) as shared test fixtures in `Essentials/tests/Expressions/JavaScript/Jint/Tests/Fixtures/PrecisionCases.cs`
- [ ] T002 [P] Record the baseline JTest corpus measurement (convert and publish counts) before any change, using the spec 267 method (throwaway tests against a local JTest checkout, deleted before commit); note the counts in the PR description

## Phase 2: Foundational

- [ ] T003 Implement the lossy-number check of research R-2 (decimal value of the JSON text vs the double's round-trip value) in `Jint/Services/JavaScriptNumberPrecision.cs`, with unit tests over T001's cases in `Essentials/tests/Expressions/JavaScript/Jint/Tests/JavaScriptNumberPrecisionTests.cs`
- [ ] T004 Add the entry record (entered `JsonElement` per value key, lossy paths) of data-model.md in `Jint/Models/ScriptEntryRecord.cs`, and fill it in `Jint/Services/IsolatedJintEngine.cs` (`SetReadOnlyArgs`, `SetReadOnlyVariables`, `CreateValue`)
- [ ] T005 Add the JavaScript adapter version constant and pin it in the executable at publish in `Pub/ExecutableNodeCompiler.cs` (FR-027)

## Phase 3: User Story 1 — A script reads data without changing it (P1)

**Goal**: plain values in, a read writes nothing.
**Independent Test**: spec User Story 1.

- [ ] T006 [US1] Remove non-writable descriptors and `PreventExtensions` from `CreateObject` and `CreateArray` in `Jint/Services/IsolatedJintEngine.cs`; keep the `args` and `variables` globals non-replaceable
- [ ] T007 [US1] Implement copy, compare, patch (research R-1) in `Jint/Services/ScriptChangeDetector.cs`: unchanged → nothing; member → path patch copied from the entry for untouched members; whole value → replace; array → whole; equal JavaScript value → unchanged (FR-003–FR-006)
- [ ] T008 [P] [US1] Unit tests for `ScriptChangeDetector` (each row of the "Out of JavaScript" table in `contracts/javascript-adapter.md`, key order and casing kept, member deletion) in `Essentials/tests/Expressions/JavaScript/Jint/Tests/ScriptChangeDetectorTests.cs`
- [ ] T009 [US1] Update `ScriptValueIsolationTests` for the new contract (plain changeable values, still no CLR surface) in `Essentials/tests/Expressions/JavaScript/Jint/Tests/ScriptValueIsolationTests.cs`
- [ ] T010 [US1] End-to-end test: a read-only script over an order with a large id leaves the stored text and version unchanged; a variable never read is untouched (acceptance 1–3) in `Essentials/tests/Workflows/Runtime/Integration/ScriptReadsKeepValuesTests.cs`
- [ ] T011 [P] [US1] Test that no CLR object, type or member is reachable from scripts and JavaScript input-binding expressions (FR-008) in `Essentials/tests/Expressions/JavaScript/Jint/Tests/NoClrSurfaceTests.cs`

## Phase 4: User Story 2 — A lost digit never reaches storage silently (P1)

**Goal**: one replaceable policy refuses lossy writes by default.
**Independent Test**: spec User Story 2.

- [ ] T012 [US2] Add the replacement contract `IJavaScriptPrecisionPolicy` with its context and decision types (contracts/precision-policy.md) in `JsCore/Services/IJavaScriptPrecisionPolicy.cs`
- [ ] T013 [US2] Implement `RefusingPrecisionPolicy` (no options; refuse unless the node accepts; diagnostics per contract, whole-array form, the `9007199254740991` reason and the remedy) in `JsCore/Services/RefusingPrecisionPolicy.cs`
- [ ] T014 [US2] Register the default policy as the single replaceable implementation in the JavaScript feature in `JsCore/JavaScriptFeature.cs` (or `Jint/JintFeature.cs`, whichever owns the engine registration); add the composition/registration test only if the wiring carries logic
- [ ] T015 [US2] Call the policy from result materialization and from detected changes, before anything of the step is written, in `Jint/Services/JintResultMaterializer.cs` and `Jint/Services/ScriptChangeDetector.cs`; raise refusals as instance diagnostics visible in Studio
- [ ] T016 [US2] Warn, without refusing, when a lossy value is only computed with, and log a warning when one is passed to a host function (FR-013) in `Jint/Services/IsolatedJintEngine.cs`
- [ ] T017 [US2] Add the script node setting `AcceptPrecisionLoss` (default off), compile it into the executable and pass it to the policy (FR-014, research R-3) in the RunJavaScript activity and `Pub/ExecutableNodeCompiler.cs`
- [ ] T018 [P] [US2] Unit tests for `RefusingPrecisionPolicy` (acceptance 1–3, 7) in `Essentials/tests/Expressions/JavaScript/Tests/RefusingPrecisionPolicyTests.cs`
- [ ] T019 [US2] End-to-end tests: refused lossy return, string id passes, whole-array diagnostic lists elements, `10.50` no loss, host replacement policy decides (acceptance 1–7) in `Essentials/tests/Workflows/Runtime/Integration/ScriptPrecisionPolicyTests.cs`
- [ ] T020 [US2] Add the policy to `EXTENSION_POINTS.md` and regenerate the extension-point map

## Phase 5: User Story 5 — The dynamic kind binds across its .NET names (P1)

**Goal**: `Any` binds to `JsonElement`/`JsonNode`; nested collection types.
**Independent Test**: spec User Story 5.

- [ ] T021 [US5] Add `IsDynamicKind(alias)` covering `Any`, `Elsa.Any`, `Object`, `JsonElement`, `JsonNode`, their full and nullable names, in `Prim/Models/TypeAliasConvention.cs`, with tests in `Essentials/tests/Primitives/TypeAliasConventionTests.cs`
- [ ] T022 [US5] Use it in the declared-type check of `CompilePortableExpressionInput` in `Pub/RuntimeInputBindingCompiler.cs`; keep the `JsonObject` run-time shape check (FR-020)
- [ ] T023 [US5] Add an optional `Element` type reference and make `Decompose` and `Close` recurse in `Prim/Models/TypeReference.cs` and `Prim/Models/TypeReferenceFactory.cs`; print the full shape in diagnostics; bump the document version (FR-021)
- [ ] T024 [P] [US5] Tests for nested types (`IDictionary<string, string[]>` round trip, diagnostics text) in `Essentials/tests/Primitives/TypeReferenceFactoryTests.cs`
- [ ] T025 [US5] End-to-end tests: an `Any` expression on `JsonElement`, `JsonElement?`, `JsonNode` and `JsonObject` inputs; `WriteHttpResponse` with `{"X-Test": ["1"]}` sends the header (acceptance 1–4) in `Essentials/tests/Workflows/Publishing/Integration/DynamicKindBindingTests.cs`

## Phase 6: User Story 3 — A script changes one member and keeps the rest (P2)

**Goal**: script variable writes through the step. **Waits on** the S-10 session write path.
**Independent Test**: spec User Story 3.

- [ ] T026 [US3] Make `variables` writable for script activities only; keep it read-only for input-binding expressions (research R-4) in `Jint/Services/IsolatedJintEngine.cs` and `Jint/Services/JintJavaScriptScriptEvaluator.cs`
- [ ] T027 [US3] Hand detected variable changes to the step's variable session as writes with their ADR 0092 base (the read-set entry of the value read) in `Jint/Services/JintJavaScriptScriptEvaluator.cs`
- [ ] T028 [US3] End-to-end tests: member change keeps other members' text; whole assignment replaces; array written whole; equal-value write keeps stored value; string forces the write; fault discards all writes; read-modify-write base recorded (acceptance 1–6) in `Essentials/tests/Workflows/Runtime/Integration/ScriptVariableWritesTests.cs`

## Phase 7: User Story 4 — One name for dynamic data (P2)

**Goal**: `Any` shown as JSON; `Object` retired. **Waits on** Q1 (#3422 Q5).
**Independent Test**: spec User Story 4.

- [ ] T029 [US4] Register `Any` and resolve `Object`, `System.Object` and `Elsa.Any` to it; register `DateOnly` and `TimeOnly` as bare aliases in `Prim/Models/TypeAliasConvention.cs`
- [ ] T030 [US4] Replace `Object` with `Any` labelled "JSON" in `Essentials/src/Expressions/Elsa.Expressions/Services/DefaultVariableTypeDescriptorProvider.cs`; add `DateOnly` and `TimeOnly` descriptors
- [ ] T031 [US4] Replace the `Elsa.Any` constants and the `Object`/`Elsa.Any` comparisons with `IsDynamicKind` at the sites listed in research R-0 (`ExecutableNodeCompiler`, `ValueRepresentation`, `ValueConversionCompatibility`, `IntrinsicAuthoringDescriptorProvider`, `ModifyVariableAlterationHandler`, `ForEach`, `RuntimeMemberRead`, `RuntimeInputBindingStateProjection`, `RuntimeActivityInputMaterializer`, `WorkflowIntrinsicExecutor`, `GraphActivity`, `PortableLiquidExpressionHandler`, the Agent schema compiler)
- [ ] T032 [US4] Make the Elsa 3 converter emit `Any` where it emits `Object` (`Elsa3ValueFlowLowerer.ReadType` and `Elsa3TypeShapes`, `Elsa3ActivityNormalizer`, `Elsa3VariableMapper`, `Elsa3DefinitionConverter`, `Elsa3ActivityResolver`, the rewrites listed in research R-0) in `E3/`; text starting values become JSON strings (FR-018)
- [ ] T033 [P] [US4] Tests: definitions naming `Object`/`Elsa.Any` load as `Any`; Studio type list has JSON and no Object; Elsa 3 `ObjectDictionary` and unknown types import as `Any` and publish; `DateOnly`/`TimeOnly` stored as ISO text (acceptance 1–4) in `Essentials/tests/Primitives/DynamicKindTests.cs` and `Extensions/Elsa3/tests/`
- [ ] T034 [US4] Update converter golden fixtures and mapping tests that pin `Object` in `Extensions/Elsa3/tests/`

## Phase 8: User Story 6 — Elsa 3 script result into a typed input (P2)

**Goal**: faithful or refused at import. **Waits on** #3167.
**Independent Test**: spec User Story 6.

- [ ] T035 [US6] Re-measure #3196 after #3167 lands; record which bindings now convert in the PR description
- [ ] T036 [US6] Record Elsa 3.6.3's coercion per JSON kind (boolean, string, number, null, object, array) into each scalar target, from the Elsa 3 source (research R-5), as a table in `specs/1228-value-model-conversion/research.md` (R-5)
- [ ] T037 [US6] Make the `elsa.json@1` route convert a JSON value of the matching kind into a scalar target, never parsing text (FR-024), in `Pub/ValueConversionPlanResolver.cs`, with tests in `Essentials/tests/Workflows/Publishing/ValueConversionPlanResolverTests.cs`
- [ ] T038 [US6] Stop `TypesMatch` treating an untyped producer as compatible with a typed input; convert per T036's table or refuse with a new stable `ELS3-` code naming activity, input and guidance, in `E3/Services/Elsa3ValueFlowLowerer.cs`
- [ ] T039 [US6] End-to-end test: fixture `reusable-reads-result` imports, publishes and runs both branches; each JSON kind converts or is refused at import; no `VF-COER-001` (acceptance 1–4) in `Extensions/Elsa3/tests/Integration/ScriptResultIntoTypedInputTests.cs`

## Phase 9: User Story 7 — A script result is as protected as what it read (P3)

**Independent Test**: spec User Story 7.

- [ ] T040 [US7] At result materialization, take the strongest protection policy among the read set's values (ADR 0092) and attach it to the result, in `Jint/Services/JintResultMaterializer.cs` or the script activity's result capture
- [ ] T041 [US7] End-to-end tests: result of a sensitive read is sensitive and redacted; result with no protected read gets none (acceptance 1–2) in `Essentials/tests/Workflows/Runtime/Integration/ScriptResultProtectionTests.cs`

## Phase 10: Polish

- [ ] T042 [P] Write the adapter contracts for JavaScript, activity inputs and outputs, and Liquid (FR-026) in `docs/reference/value-adapters.md`, from `contracts/javascript-adapter.md` and the ADR 0046 adapter tables
- [ ] T043 [P] Test that Liquid reads large numbers and long decimals without narrowing in `Essentials/tests/Expressions/Liquid/Tests/LiquidNumberTests.cs`
- [ ] T044 Run the change-detection benchmark on a 1 MB input (SC-007) and record the result in the PR description
- [ ] T045 Re-run the JTest corpus measurement (SC-008) and compare with T002
- [ ] T046 Regenerate the dependency map and solution filters if project references changed; run the quickstart by hand
- [ ] T047 Set the spec status to `Implemented` in the finishing PR

## Dependencies

- Phase 2 before every story.
- US1 before US2 and US3 (both use the change detector).
- US5 T021 before US4 T031.
- US3 after the S-10 session write path. US4 after Q1. US6 after #3167.
- US7 after US1.

## Parallel examples

- After Phase 2: US1, US5 and US6's T036 can run in parallel.
- Within US2: T018 alongside T015–T017.
- Polish: T042 and T043 in parallel.

## Implementation strategy

- **MVP**: Phases 1–4 (US1 + US2). This closes the JavaScript part of #3421 and delivers D-17.
- **Next**: US5 (closes the type parts of #3287 and #3202), then US3, US4 and US6 as their gates open, then US7.

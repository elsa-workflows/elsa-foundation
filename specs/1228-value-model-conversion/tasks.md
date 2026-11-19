# Tasks: Value model and conversion

**Input**: design documents in `specs/1228-value-model-conversion/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: required by framework §2.23 for every logic-bearing change below, plus one test per acceptance scenario.
This work changes existing code, not greenfield: tests go with each change; no test-first cadence is declared.

**Gates**:
- User Story 3's end-to-end test (T028) waits on S-7's step write path (ADR 0090).
- User Story 6 waits on #3167 (S-0).
- User Story 3, User Story 7 and T010's variable cases need S-4's script body read surface (spec 1222).

Paths: `Jint/` = `Essentials/src/Expressions/JavaScript/Jint/`, `JsCore/` =
`Essentials/src/Expressions/JavaScript/Core/` (the Jint project already references it), `Prim/` = `Essentials/src/Primitives/Primitives/`,
`Pub/` = `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/`, `E3/` = `Extensions/Elsa3/src/Mapping/`.

## Phase 1: Setup

- [x] T001 Create the precision test suite data (numbers beyond ±2^53, a 29-digit decimal, `0.10000000000000000000000000001`, `10.50`, `2.0`, `1e21`, `1e30`, `1e400`, negatives) as shared test fixtures in `Essentials/tests/Expressions/JavaScript/Jint/Tests/Fixtures/PrecisionCases.cs`
- [x] T002 [P] Record the baseline JTest corpus measurement (convert and publish counts) before any change, using the spec 267 method (throwaway tests against a local JTest checkout, deleted before commit); note the counts in the PR description — 8 of 36 convert and 6 of 36 publish at `b6a67a880a` (2 run to completion); in the PR description

## Phase 2: Foundational

- [x] T003 Implement the exact lossy-number check of research R-2 (big-integer mantissa and exponent of the JSON text vs the exact value of the double; `Infinity` is a loss) in `Jint/Services/JavaScriptNumberPrecision.cs`, with unit tests over T001's cases in `Essentials/tests/Expressions/JavaScript/Jint/Tests/JavaScriptNumberPrecisionTests.cs` — V9 as written; research R-2 amended (the draft compared binary values, which would refuse every `0.1`)
- [x] T004 Add the replacement contract `IScriptValueBridge` (enter values → entry record; collect changes → changes) and the entry record and change types of data-model.md in `JsCore/Services/IScriptValueBridge.cs` and `JsCore/Models/`; make `Jint/Services/IsolatedJintEngine.cs` (`SetReadOnlyArgs`, `SetReadOnlyVariables`) enter values only through the contract (FR-009) — the engine-neutral models (`ScriptValueChange`, `JavaScriptLossyNumber`, `JavaScriptPrecisionLoss`, `ScriptValueWrite`) are in `JsCore/Models/`; `IScriptValueBridge` itself is in `Jint/Contracts/`, because entering a value and finding a change work on the engine's values and JsCore does not reference Jint
- [x] T005 Add the JavaScript adapter version constant, pin it in the executable at publish in `Pub/ExecutableNodeCompiler.cs`, and refuse at run time an executable without it, with a republish diagnostic and a test (FR-028) — the version is `JavaScriptValueAdapter.Version` (`json-1`). On Joey's call, one pin per executable: a language registers an `ExpressionValueAdapter` and a script activity a `ScriptActivityLanguage`; publication puts `javascript.valueAdapter` into the compatibility metadata of an executable that uses JavaScript, the behavioral hash covers it (an executable without JavaScript keeps its hash), and the runtime refuses a missing or different pin once per loaded executable (`SchedulerWorkHandlerHelpers.EnsureValueAdaptersPinned`) with `VF-ADPT-001`; activity draft Test Run wrappers carry it too

## Phase 3: User Story 1 — A script reads data without changing it (P1)

**Goal**: plain values in, a read writes nothing.
**Independent Test**: spec User Story 1.

- [x] T006 [US1] Remove non-writable descriptors and `PreventExtensions` from `CreateObject` and `CreateArray` in `Jint/Services/IsolatedJintEngine.cs`; keep the `args` and `variables` globals non-replaceable
- [x] T007 [US1] Implement the default bridge (one JavaScript value per variable per evaluation, shared by `variables.x`, `variables['x']` and `getVariable`, replacing the frozen values S-4 (spec 1222 FR-037) leaves on the `variables` root and `getVariable` in input expressions and the script body; an assigned but unread variable is a whole assignment; `args` changes are local), copy, compare, patch (research R-1), in `Jint/Services/CopyComparePatchValueBridge.cs`: keep the entered JSON and lossy marks; after the run compare JavaScript with JavaScript (ADR 0090 C2): unchanged; member change with untouched members taken from the entry; whole assignment; whole array; equal JavaScript value on a member → unchanged (FR-003–FR-005); register it as the default in the JavaScript feature
- [x] T008 [P] [US1] Unit tests for the bridge (each row of the "Out of JavaScript" table in `contracts/javascript-adapter.md`, key order and casing kept, member deletion, local copy `{...x}` is no change) in `Essentials/tests/Expressions/JavaScript/Jint/Tests/CopyComparePatchValueBridgeTests.cs`
- [x] T008a [US1] Record enter and collect durations and bytes converted per evaluation as a histogram on a new `Elsa.Expressions.JavaScript` meter (FR-010) in `Jint/Services/CopyComparePatchValueBridge.cs`, with a test that the measurement is emitted
- [x] T009 [US1] Update `ScriptValueIsolationTests` for the new contract (plain changeable values, still no CLR surface) in `Essentials/tests/Expressions/JavaScript/Jint/Tests/ScriptValueIsolationTests.cs`
- [x] T010 [US1] End-to-end test: a read-only script over an order with a large id leaves the stored text and version unchanged; a variable never read is untouched (acceptance 1–3) in `Essentials/tests/Workflows/Runtime/Tests/ScriptReadsKeepValuesTests.cs` — in `Essentials/tests/Activities/Runtime/Tests/ScriptValueModelRunTests.cs`, through the real engine and Run JavaScript; the script body still reads through `args` until S-4 T047
- [x] T010a [US1] Refuse at publish a JavaScript input expression that assigns (`=`, compound assignment, `++`, `--`, `delete`) a target rooted at `variables.<name>`, `variables['<literal>']` or `getVariable('<literal>')`, with a new stable `VF-` code and the FR-006a diagnostic, in the JavaScript binding compiler under `Pub/` (ADR 0083 grammar check) — the JavaScript analyzer reports the writes (`ScriptVariableReadAnalysis.Writes`), and `ExecutableNodeCompiler` refuses them with `VF-BIND-001`
- [x] T010b [US1] Fail the evaluation of a JavaScript input expression when the bridge reports any change, with the same code and the path, in `Jint/Services/JintPortableJavaScriptEvaluator.cs`
- [x] T010c [US1] Tests: direct assignment refused at publish; aliased member change fails at run time and writes nothing; a changed local copy succeeds; change-and-revert succeeds (acceptance 4–6) in `Essentials/tests/Workflows/Publishing/Tests/InputExpressionWriteRefusalTests.cs` — publish: `WorkflowExecutableCompilerTests.ScriptReads.cs` and `ScriptVariableWriteAnalyzerTests.cs`; run time: `ScriptValueAdapterTests.cs` and `ScriptValueModelRunTests.cs`
- [x] T011 [P] [US1] Test that no CLR object, type or member is reachable from scripts and JavaScript input-binding expressions (FR-008) in `Essentials/tests/Expressions/JavaScript/Jint/Tests/NoClrSurfaceTests.cs` — in `ScriptValueAdapterTests.No_clr_object_type_or_member_is_reachable` and `ScriptValueIsolationTests`

## Phase 4: User Story 2 — A lost digit never reaches storage silently (P1)

**Goal**: one replaceable policy refuses lossy writes by default.
**Independent Test**: spec User Story 2.

- [x] T012 [US2] Add the replacement contract `IJavaScriptPrecisionPolicy` with its context and decision types (contracts/precision-policy.md) in `JsCore/Services/IJavaScriptPrecisionPolicy.cs`
- [x] T013 [US2] Implement `RefusingPrecisionPolicy` (no options, always refuses; diagnostics per contract, whole-array form, the `9007199254740991` reason and the remedy) in `JsCore/Services/RefusingPrecisionPolicy.cs`
- [x] T014 [US2] Register the default policy as the single replaceable implementation in the JavaScript feature in `JsCore/JavaScriptFeature.cs` (or `Jint/JintFeature.cs`, whichever owns the engine registration); add the composition/registration test only if the wiring carries logic — registered with `TryAddSingleton` in `Jint/JintFeature.cs`, which owns the engine registration
- [x] T014a [US2] Apply the FR-011a matching rule: write an unchanged entered value from its original JSON; match other written numbers by double value against lossy entered numbers, in `Jint/Services/CopyComparePatchValueBridge.cs`, with unit tests (pass-through of a large id is no loss; a copied large id is)
- [x] T015 [US2] Call the policy from result materialization (scripts and input expressions) and from reported changes, before anything of the step is written, in `Jint/Services/JintResultMaterializer.cs` and `Jint/Services/CopyComparePatchValueBridge.cs`; raise refusals as instance diagnostics visible in Studio — a refusal faults Run JavaScript with `VF-PREC-001`; an input expression's refusal blocks the node with an incident that carries the diagnostic
- [x] T016 [US2] Raise one warning per evaluation, as an instance diagnostic, for lossy numbers read but not written (FR-015) in `Jint/Services/CopyComparePatchValueBridge.cs`; expose the loss information to S-1's host function argument conversion and log its warning there (coordinate with spec 1223 FR-011) — on Joey's call the warning goes through a new shared run-time warning channel built in this PR: `ExecutionWarning` reported to an `IExecutionWarningSink` (`ExpressionEvaluationRequest.Warnings`, `JavaScriptScriptEvaluationRequest.Warnings`, `ActivityExecutionContext.Warnings`), kept with the input snapshot or the activity state, listed by the inspection view (`Warnings`) and shown in Studio's run view; it names paths, never values. `IScriptValueEvaluation.TryFindLoss` serves S-1's host function argument check; the isolated engine has no host functions yet
- [x] T016a [P] [US2] Tests for US2 scenarios 4–5 (read-only warning; host-function argument warning, call not refused) and FR-016 (no number ever turned into a string) in `Essentials/tests/Expressions/JavaScript/Jint/Tests/PrecisionWarningTests.cs` — in `ScriptValueAdapterTests` (read-only warning, FR-016) and `CopyComparePatchValueBridgeTests` (loss lookup for a host function argument)
- [x] T018 [P] [US2] Unit tests for `RefusingPrecisionPolicy` (acceptance 1–3) in `Essentials/tests/Expressions/JavaScript/Jint/Tests/RefusingPrecisionPolicyTests.cs`
- [x] T019 [US2] End-to-end tests: refused lossy return, string id passes, whole-array diagnostic lists elements, `10.50` no loss, a lossy input-expression result into an input is refused, host replacement policy decides (acceptance 1–6) in `Essentials/tests/Workflows/Runtime/Tests/ScriptPrecisionPolicyTests.cs` — in `ScriptValueModelRunTests.cs` (end to end) and `ScriptValueAdapterTests.cs` (whole-array diagnostic, `10.50`, host policy)
- [x] T020 [US2] Add the policy and the bridge to `EXTENSION_POINTS.md` and regenerate the extension-point map — in `Elsa.Expressions.JavaScript/EXTENSION_POINTS.md`; the extension-point map is generated and not committed

## Phase 5: User Story 5 — The dynamic kind binds across its .NET names (P1)

**Goal**: `Any` binds to `JsonElement`/`JsonNode`; nested collection types.
**Independent Test**: spec User Story 5.

- [x] T021 [US5] Add `IsDynamicKind(alias)` covering `Any`, `Elsa.Any`, `Object`, `JsonElement`, `JsonNode`, their full and nullable names (not `JsonObject` or `JsonArray`, which are shaped JSON), in `Prim/Models/TypeAliasConvention.cs`, with tests in `Essentials/tests/Primitives/Tests/TypeAliasConventionTests.cs` — also `IsShapedJson` (JsonObject, JsonArray), `ReadAlias` and `IsAny`
- [x] T022 [US5] Use it in the declared-type check of `CompilePortableExpressionInput` in `Pub/RuntimeInputBindingCompiler.cs`; accept `Any` on `JsonObject` and `JsonArray` inputs with a run-time shape check (FR-021) — `DeclaredTypeFits`; the shape check runs in `RuntimePortableExpressionEvaluator`
- [x] T023 [US5] Add an optional `Element` type reference and make `Decompose` and `Close` recurse in `Prim/Models/TypeReference.cs` and `Prim/Models/TypeReferenceFactory.cs`; print the full shape in diagnostics; bump the document version (FR-022) — `TypeReference.Element` and `ToDisplayString()`; `ValueTypeDescriptor` carries the element, in the hash and the conversion-plan fingerprint only when present; the CLR provider schema version goes from 1 to 2
- [x] T024 [P] [US5] Tests for nested types (`IDictionary<string, string[]>` round trip, diagnostics text) in `Essentials/tests/Primitives/Tests/TypeReferenceFactoryTests.cs`
- [x] T025 [US5] End-to-end tests: an `Any` expression on `JsonElement`, `JsonElement?`, `JsonNode` and `JsonObject` inputs; `WriteHttpResponse` with `{"X-Test": ["1"]}` sends the header (acceptance 1–4) in `Essentials/tests/Workflows/Publishing/Tests/DynamicKindBindingTests.cs` — in `Essentials/tests/Activities/Behavioral/Tests/DynamicKindBindingTests.cs`, which has the publish-and-run harness; the header map is checked on the committed response instruction

## Phase 6: User Story 3 — A script changes one member and keeps the rest (P2)

**Goal**: a script's changes reach S-7's step write path. The end-to-end test waits on S-7.
**Independent Test**: spec User Story 3.

- [ ] T026 [US3] On S-4's script body read surface, make `variables` assignable in script activities; in `Jint/Services/JintJavaScriptScriptEvaluator.cs` pass a script activity's changes to S-7's step write path with their ADR 0092 base — **Waits on** S-4's T047 (the `variables` root in the Run JavaScript body, S-4 Phase 8).
- [ ] T027 [US3] Raise the ADR 0090 C2 visibility warning for a member change with no whole assignment in `Jint/Services/CopyComparePatchValueBridge.cs`, with a unit test — **Waits on** S-4's T047: the warning is raised when a script body's member change is committed, which needs the body's `variables` root.
- [ ] T028 [US3] End-to-end tests, once S-7's write path is on the branch: member change keeps other members' text; whole assignment replaces; array written whole; equal-value member change keeps stored value; equal-value whole assignment is a write; string forces the write; visibility warning (acceptance 1–6) in `Essentials/tests/Workflows/Runtime/Tests/ScriptVariableWritesTests.cs` — **Waits on** T026, so S-4's T047 (the body's `variables` root); S-7's write path (#4761) is merged. US3 change detection is tested at the bridge level in `CopyComparePatchValueBridgeTests`.

## Phase 7: User Story 4 — One name for dynamic data (P2)

**Goal**: `Any` shown as JSON; `Object` retired.
**Independent Test**: spec User Story 4.

- [x] T029 [US4] Register `Any` and resolve `Object`, `System.Object` and `Elsa.Any` to it; register `DateOnly` and `TimeOnly` as bare aliases in `Prim/Models/TypeAliasConvention.cs`
- [x] T030 [US4] Replace `Object` with `Any` labelled "JSON" in `Essentials/src/Expressions/Elsa.Expressions/Services/DefaultVariableTypeDescriptorProvider.cs`; add `DateOnly` and `TimeOnly` descriptors — one `Any` entry labelled "JSON" replaces Object and the separate JsonElement entry; "Date" and "Time of day" added
- [x] T031 [US4] Replace only the dynamic-name comparisons (`Elsa.Any` constants, `Object`/`Elsa.Any` checks) with `IsDynamicKind`, keeping `JsonObject` in the JSON (structured) representation groupings of `ValueRepresentation` and `ValueConversionCompatibility`, at the sites listed in research R-0 (`ExecutableNodeCompiler`, `ValueRepresentation`, `ValueConversionCompatibility`, `IntrinsicAuthoringDescriptorProvider`, `ModifyVariableAlterationHandler`, `ForEach`, `RuntimeMemberRead`, `RuntimeInputBindingStateProjection`, `RuntimeActivityInputMaterializer`, `WorkflowIntrinsicExecutor`, `GraphActivity`, `PortableLiquidExpressionHandler`, the Agent schema compiler) — `ValueRepresentationDefaults.Infer` is left to #3167; aliases now normalize to `Any`, which its existing branch covers
- [x] T032 [US4] Make the Elsa 3 converter emit `Any` where it emits `Object` (`Elsa3ValueFlowLowerer.ReadType` and its `Elsa3TypeShapes` table, `Elsa3ActivityNormalizer`, `Elsa3VariableMapper`, `Elsa3DefinitionConverter`, `Elsa3ActivityResolver`, the rewrites listed in research R-0) in `E3/`; text starting values become JSON strings (FR-019) — the Elsa 3 script rewriter is untouched (S-4); HttpHeaders is the nested `Dictionary<String, String[]>`
- [x] T033 [P] [US4] Tests: definitions naming `Object`/`Elsa.Any` load as `Any`; Studio type list has JSON and no Object; Elsa 3 `ObjectDictionary` and unknown types import as `Any` and publish; `DateOnly`/`TimeOnly` stored as ISO text (acceptance 1–4) in `Essentials/tests/Primitives/Tests/DynamicKindTests.cs` and `Extensions/Elsa3/tests/` — in `DynamicKindTests.cs`, `WellKnownTypeRegistryTests`, `VariableTypeDescriptorCatalogTests`, `DynamicKindBindingTests` and `Elsa3VariableStartingValueTests`
- [x] T034 [US4] Update converter golden fixtures and mapping tests that pin `Object` in `Extensions/Elsa3/tests/`

## Phase 8: User Story 6 — Elsa 3 script result into a typed input (P2)

**Goal**: faithful or refused at import. **Waits on** #3167.
**Independent Test**: spec User Story 6.

- [ ] T035 [US6] Re-measure #3196 after #3167 lands; record which bindings now convert in the PR description — **Waits on** #3167 (S-0), being fixed in a parallel session.
- [ ] T036 [US6] Record Elsa 3.6.3's coercion per JSON kind (boolean, string, number, null, object, array) into each scalar target, from the Elsa 3 source (research R-5), as a table in `specs/1228-value-model-conversion/research.md` (R-5) — **Waits on** #3167 (S-0), being fixed in a parallel session.
- [ ] T037 [US6] Make the `elsa.json@1` route convert a JSON value of the matching kind into a scalar target, never parsing text (FR-025), in `Pub/ValueConversionPlanResolver.cs`, with tests in `Essentials/tests/Workflows/Publishing/Api/Tests/ValueConversionPlanResolverTests.cs` — **Waits on** #3167 (S-0), being fixed in a parallel session.
- [ ] T038 [US6] Stop `TypesMatch` treating an untyped producer as compatible with a typed input; convert per T036's table or refuse with a new stable `ELS3-` code naming activity, input and guidance, in `E3/Services/Elsa3ValueFlowLowerer.cs` — **Waits on** #3167 (S-0), being fixed in a parallel session.
- [ ] T039 [US6] End-to-end test: fixture `reusable-reads-result` imports, publishes and runs both branches; each JSON kind converts or is refused at import; no `VF-COER-001` (acceptance 1–4) in `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/ScriptResultIntoTypedInputTests.cs` — **Waits on** #3167 (S-0), being fixed in a parallel session.

## Phase 9: User Story 7 — A script result is as protected as what it read (P3)

**Independent Test**: spec User Story 7.

- [ ] T040 [US7] At result materialization of a script body, take the strongest protection policy among its session reads in the read set (ADR 0092; input expressions keep the existing fold in `RuntimePortableExpressionEvaluator`) and attach it to the result, in `Jint/Services/JintResultMaterializer.cs` or the script activity's result capture — **Waits on** S-4's T047 (the script body's session reads).
- [ ] T041 [US7] End-to-end tests: result of a sensitive read is sensitive and redacted; result with no protected read gets none (acceptance 1–2) in `Essentials/tests/Workflows/Runtime/Tests/ScriptResultProtectionTests.cs` — **Waits on** S-4's T047 (the script body's session reads).

## Phase 10: Polish

- [x] T042 [P] Write the adapter contracts for JavaScript, activity inputs and outputs, and Liquid (FR-027) in `docs/reference/value-adapters.md`, from `contracts/javascript-adapter.md` and the ADR 0046 adapter tables
- [x] T043 [P] Test that Liquid reads large numbers and long decimals without narrowing in `Essentials/tests/Expressions/Tests/LiquidNumberTests.cs`
- [x] T044 Run the bridge benchmark on 1 KB, 100 KB and 1 MB inputs against today's engine (SC-007), keep it in the benchmark project so a later bridge can be compared, and record the result in the PR description — opt-in xunit benchmark `ScriptValueBridgeBenchmark` (no benchmark project exists and the plan adds no external dependency); the baseline is the pre-spec frozen conversion, kept in the benchmark; results in the PR
- [x] T045 Re-run the JTest corpus measurement (SC-008) and compare with T002 — unchanged at the PR head: 8 of 36 convert and 6 of 36 publish (2 run to completion); one definition moved between refusal codes; SC-008 holds
- [x] T046 Regenerate the dependency map and solution filters if project references changed; run the quickstart by hand — no project reference changed (only a package reference in the Jint project, whose lock files are updated), so the solution filters are unchanged and the dependency map is generated, not committed; quickstart steps 5 and 6 and the input-expression parts were run in the real app (PR description); steps 1 to 4 need the Run JavaScript body's `variables` root (S-4 T047)
- [ ] T047 Set the spec status to `Implemented` in the finishing PR — **Waits on** US3, US6 and US7; the spec stays Approved, not Implemented, in this PR.

## Dependencies

- Phase 2 before every story.
- US1 before US2 and US3 (both use the change detector).
- US5 T021 before US4 T031.
- T028 after S-7's step write path. US6 after #3167.
- US7 after US1.

## Parallel examples

- After Phase 2: US1, US5 and US6's T036 can run in parallel.
- Within US2: T018 alongside T015–T016.
- Polish: T042 and T043 in parallel.

## Implementation strategy

- **MVP**: Phases 1–4 (US1 + US2). This closes the JavaScript part of #3421 and delivers D-17.
- **Next**: US5 (closes the type parts of #3287 and #3202) and US4, then US3 and US6 as their gates open, then US7.

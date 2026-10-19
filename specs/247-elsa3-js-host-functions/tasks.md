# Tasks: Elsa 3 JavaScript host functions (spec 247)

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/), [quickstart.md](quickstart.md)

**Worktree**: `C:\Users\JoeyBarten\source\repos\w2717`, branch `2717-elsa3-js-host-functions`. All paths below are
relative to the repository root.

**Test cadence**: The Elsa constitution declares no TDD cadence (framework §2.21.2), and this is a change to existing
code, not greenfield. Each story's tests are written in the same phase as its implementation and must pass before the
phase is checked off. Tests use xunit only (no FluentAssertions). Converter tests use `RealActivityCatalog`.

**Builds**: Build and test only the touched projects:
- `Extensions/Elsa3/tests/Mapping/Tests`
- `Essentials/tests/Expressions/Tests`
- `Essentials/tests/Workflows/Runtime/Tests`
- `Essentials/tests/Activities/Runtime/Tests` (US3 only)

**Never commit**:
- `Extensions/Elsa3/tests/Mapping/Tests/ZzCorpusProbe.cs`
- `Extensions/Elsa3/tests/Mapping/Tests/ZzJsProbe.cs`
- anything from the JTest corpus

## Format: `- [ ] [ID] [P?] [Story?] Description`

- **[P]**: can run in parallel (different files, no unfinished dependency).
- **[USn]**: user story from spec.md.

---

## Phase 1: Setup

- [X] T001 Add a test-only `ProjectReference` to `Essentials/src/Expressions/JavaScript/Jint/Elsa.Expressions.JavaScript.Jint.csproj` in `Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj`, so equivalence tests (SC-003) can evaluate a converted `ExpressionDefinition` through the real portable JavaScript evaluator. Refresh `packages.lock.json` with `dotnet restore --force-evaluate` for that project only
- [X] T002 Create the test helper `Extensions/Elsa3/tests/Mapping/Tests/Support/PortableJavaScript.cs` (internal static). `EvaluateAsync(ExpressionDefinition definition, IReadOnlyDictionary<string, JsonElement> args)` evaluates through the Jint portable evaluator, with the binding-pure-v1 profile and explicit parameter values (no runtime), and returns the `JsonElement` result

---

## Phase 2: Foundational

- [X] T003 Create `Extensions/Elsa3/src/Mapping/Internal/Elsa3JavaScriptNames.cs` (internal static) with:
  - `Pascalize(string)`: upper-case the first character with `CultureInfo.InvariantCulture`. Callers pass only accessor-valid names (research R-3)
  - `IsAccessorName(string)`: non-empty, and every character satisfies `char.IsLetterOrDigit`
  - `BuiltIns`: a `FrozenSet<string>` of every Elsa 3 built-in function name from research R-6/R-7, plus `getVariable`, `getInput`, `getOutputFrom`, `setVariable` and `getLastResult`
- [X] T004 [P] Unit tests for T003 in `Extensions/Elsa3/tests/Mapping/Tests/Elsa3JavaScriptNamesTests.cs`:
  - `httpRequestBody` → `HttpRequestBody`, `RequestBody` unchanged, `x1` → `X1`
  - `IsAccessorName` rejects `""`, `a_b` and `a$`

**Checkpoint**: the name helpers exist; stories can start.

---

## Phase 3: User Story 1 — top-level `return` (P1) 🎯 MVP

**Goal**: Elsa 3 scripts with a top-level `return` convert and return the same value (FR-001 to FR-003, research R-1).

**Independent test**: `return false`, `return { a: 1 }` and `if (x) { return 1 } return 2` convert, and evaluate to
Elsa 3's values.

- [X] T005 [US1] In `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs` `RewriteJavaScript`:
  - Parse with `new Parser(new ParserOptions { AllowReturnOutsideFunction = true })`
  - Add a private static `HasTopLevelReturn(Script)` that walks statements, skipping nested `IFunction` nodes
- [X] T006 [US1] In the same method, when `HasTopLevelReturn`:
  - **Expression mode**: if the last top-level statement is an `ExpressionStatement`, insert `return `; if it is a `ReturnStatement`, keep it; otherwise fail with `DynamicIssue` (VF-IMP-006, message "ends with a statement that is neither a return nor an expression"). Wrap the result in `(function () {\n…\n})()`, the same wrapper the existing statements branch uses
  - **Script mode**: apply the same last-statement rule. Set `completionValueLost: false` when the body ends in a `ReturnStatement`
  - Bodies without a top-level return keep today's path unchanged
- [X] T007 [P] [US1] Tests in a new `Extensions/Elsa3/tests/Mapping/Tests/Elsa3TopLevelReturnTests.cs`, through `ConversionServices.ConvertAsync` and `RealActivityCatalog`:
  - US1 scenarios 1–5: a FlowDecision condition `return false`, an object return, branch returns, an earlier return plus a trailing expression, a trailing `if` refused with VF-IMP-006, and a RunJavaScript body with `return`
  - `return` inside a nested function stays legal and unwrapped (edge case)
  - Evaluate the converted sources with T002 and assert Elsa 3's values (SC-003)
- [X] T008 [US1] Update `Extensions/Elsa3/tests/Mapping/Tests/Elsa3JavaScriptConversionTests.cs` and `Elsa3ExpressionRewriterTests.cs` where they assert that a top-level `return` is refused. Keep every other spec 236 assertion. (No such assertion existed; nothing changed.)

**Checkpoint**: run `dotnet test Extensions/Elsa3/tests/Mapping/Tests`; it is green.

---

## Phase 4: User Story 2 — generated accessors (P1)

**Goal**: `get{Name}()` converts as Elsa 3 generated it (FR-004 to FR-009, research R-2 and R-3).

**Independent test**: a variable `HttpRequestBody` and an input `OrderId` give the same bindings as the explicit
`getVariable`/`getInput` calls.

- [X] T009 [US2] Add `GeneratedAccessor` to the public enum `Elsa3AmbientReferenceKind` in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs`, with an XML doc: "a call `get{Name}()`; `Name` is the callee without `get`, as written"
- [X] T010 [US2] In `Elsa3ExpressionRewriter.JavaScriptAnalyzer.VisitNode`, for an undeclared callee `get{N}` with zero arguments that is not a built-in, call `AddResolvedEdit(call, GeneratedAccessor, N)`. Rules:
  - The parameter is named after the accessor (`getHttpRequestBody()` → `args.HttpRequestBody`), so no `ParameterName` was added to the resolution (deviation from the first draft of this task)
  - With arguments, or a callee used without a call (`var f = getX`) where `getX` resolves → `DynamicIssue` (VF-IMP-006)
  - Treat `set{N}(…)` for a resolvable `N` as a mutation (VF-IMP-005, guidance "Use a Set Variable activity")
  - Extend `ContainsAmbientRoot` so mutating `get{N}()` results is refused, like `getVariable`
- [X] T011 [US2] In `Extensions/Elsa3/src/Mapping/Services/Elsa3ValueFlowLowerer.cs` `ResolveAmbient`, add the `GeneratedAccessor` case, in order (data-model.md, research R-2):
  1. An output-accessor match → `Failure(VF-IMP-005, "reads activity output accessor …", "Capture the output into a variable and read it with get{Variable}()")`. A match is a named activity in the definition with an output whose `get{Pascalize(output)}From{Pascalize(activityName)}` equals the callee
  2. Standalone only: an input with ordinal-equal name → `WorkflowRequestExpressionParameterBinding`
  3. Variables with `IsAccessorName(name)` and `Pascalize(name) == N`, through the existing variable path (loop variables included). In reusable mode, inputs with `Pascalize(input) == N` are also candidates; an input match resolves as `getInput` does today
  4. No match → `Failure(VF-IMP-005, "calls unknown or unapproved host function 'get{N}'", existing guidance)`

  Two candidates at one step give VF-IMP-002. Thread `IsReusable` from `Elsa3ConversionRequest` into the lowerer if it is not already available there
- [X] T012 [P] [US2] Tests in a new `Extensions/Elsa3/tests/Mapping/Tests/Elsa3GeneratedAccessorTests.cs`:
  - US2 scenarios 1–6
  - `getHttpRequestBody().text` and `getVariable('HttpRequestBody').text` give equal parameter bindings
  - `getorderId()` with a variable `orderId` → VF-IMP-005
  - An input and a variable both named `Foo` → the input binding
  - An output accessor name → refused
  - A reusable conversion with input `CustomInput` → the input binding through `getCustomInput()`
  - A variable `a_b` has no accessor → refused
  - `getOrderId(1)` → refused
  - Run the converted expressions with T002 to check the value (SC-003)
- [X] T013 [US2] In `Extensions/Elsa3/tests/Mapping/Tests/Elsa3JavaScriptConversionTests.cs`, move the `getOrderId()` row of `Constructs_elsa4_cannot_run_are_refused` to a case with no matching declaration, so it still refuses for the right reason. (Already true: that fixture declares no matching variable; nothing changed.)

**Checkpoint**: Mapping tests are green.

---

## Phase 5: User Story 3 — correlation ID and instance ID (P2)

**Goal**: an Elsa 4 binding reads instance metadata, and the converter lowers `getCorrelationId()` and
`getWorkflowInstanceId()` to it (FR-010 to FR-014, research R-4 and R-5,
[contracts/instance-metadata-binding.md](contracts/instance-metadata-binding.md)).

**Independent test**: an Elsa 4 workflow started with correlation ID `c-1` evaluates a declared read to `c-1`;
converted Elsa 3 calls lower to it.

### Core

- [ ] T014 [US3] Add `WorkflowInstanceExpressionParameterBinding` to `Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs`:
  - A sealed record with `[JsonPropertyName("member")] string Member`
  - Nested static class `Members` with `CorrelationId = "correlationId"` and `InstanceId = "instanceId"`
  - The `[JsonConstructor]` constructor throws `ArgumentException` for any other value
  - Add `[JsonDerivedType(typeof(WorkflowInstanceExpressionParameterBinding), "WorkflowInstance")]`
  - XML docs as in the contract
- [ ] T015 [US3] Add the new public API lines to `Essentials/src/Expressions/Core/PublicAPI.Unshipped.txt`
- [ ] T016 [P] [US3] In `Essentials/tests/Expressions/Tests/ExpressionParameterBindingWireTests.cs`:
  - Add the kind to the wire-shape theory and the round-trip tests
  - Add a test that an unknown `member` is rejected by both the constructor and deserialization
  - Add a separate pinned-bytes fixture for a definition containing the new kind
  - Leave the existing four-kind fingerprint fixture unchanged

### Runtime

- [ ] T017 [US3] Add `public string? CorrelationId { get; init; }` (XML doc: committed identity value, null when the instance has none) to `Essentials/src/Workflows/Runtime/Core/Models/RuntimeInputBindingResolutionContext.cs`
- [ ] T018 [US3] In `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimeWorkflowStateSeed.cs` `BuildSeedChanges`, write the `identity:correlationId` durable value (tagged `RuntimeMetadataKeys.IdentityName`, the same shape as `BuildIdentityChanges`) when the start carries a correlation ID
- [ ] T019 [US3] Fill `CorrelationId` from `RuntimeIdentityStateProjection` over the committed durable values the context already loads, in both places that build the context:
  - `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimeActivityInputSnapshotMaterializer.cs`
  - `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/WorkflowIntrinsicExecutor.cs`
- [ ] T020 [US3] In `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimePortableExpressionEvaluator.cs`:
  - `MaterializeParametersAsync`: `WorkflowInstanceExpressionParameterBinding` → `CorrelationId` (JSON null when absent) or `WorkflowExecutionId`
  - `DetermineDependencyPolicy`: an explicit arm returning `null` (no policy)
- [ ] T021 [P] [US3] Runtime tests:
  - `Essentials/tests/Workflows/Runtime/Tests/RuntimeWorkflowStateSeedTests.cs`: the start seed writes `identity:correlationId`, and writes nothing without one
  - A new `Essentials/tests/Workflows/Runtime/Tests/WorkflowInstanceParameterBindingTests.cs`: both members resolve; an absent correlation ID binds JSON null; the value comes from committed state, so re-materializing a snapshot gives the same value
- [ ] T022 [P] [US3] End-to-end test in `Essentials/tests/Activities/Runtime/Tests/WorkflowInstanceParameterRunTests.cs`, modelled on `SetVariableDurabilityExecutionTests.cs`:
  - A WriteLine whose text reads the binding writes `c-1`
  - After a `SetCorrelationId` intrinsic to `c-2`, a later WriteLine writes `c-2`
  - With no correlation ID, it writes null (US3 scenarios 1–3)

### Converter

- [ ] T023 [US3] In `Elsa3ExpressionRewriter.JavaScriptAnalyzer.VisitNode`, an undeclared zero-argument `getCorrelationId()` or `getWorkflowInstanceId()`:
  - First checks for a generated accessor with the same name, by resolving `GeneratedAccessor` (`CorrelationId` / `WorkflowInstanceId`). If it resolves → VF-IMP-002 ("variable or input shadows built-in"; guidance: rename)
  - Otherwise replaces the call with `args.elsa3CorrelationId` / `args.elsa3WorkflowInstanceId`, bound to the new binding
  - Refuses with VF-IMP-002 if the script declares those names
- [ ] T024 [P] [US3] Tests in `Extensions/Elsa3/tests/Mapping/Tests/Elsa3JavaScriptHostFunctionTests.cs` (new):
  - Both calls lower to the right binding
  - A variable `CorrelationId` → VF-IMP-002
  - Evaluate with T002, passing a parameter value `c-1`
  - Remove the `getCorrelationId()` row from `Constructs_elsa4_cannot_run_are_refused` in `Elsa3JavaScriptConversionTests.cs`

### Decision record and docs

- [ ] T025 [US3] Add "**Amendment (issue #2717): workflow-instance metadata parameter binding.**" to `docs/adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md`, after the #1004 amendment. It covers: the new kind; the declared, pinned, committed-state rules; the start seed; and the §E2.6.1 compatibility note
- [ ] T026 [P] [US3] Amend `specs/095-value-flow-redesign/contracts/expression-and-import-contract.md`: change "exactly one of" the four kinds to five, and link the ADR amendment
- [ ] T027 [P] [US3] Docs:
  - `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md`: add "workflow-instance metadata" to the `IRuntimeInputBindingResolver` sources
  - `Essentials/src/Expressions/Elsa.Expressions/EXTENSION_POINTS.md`: note that instance metadata reaches a handler only as a declared parameter
  - The "Activity input binding" value sources in `docs/glossary/elsa.md`

**Checkpoint**: Expressions, Workflows Runtime, Activities Runtime and Mapping tests are green.

---

## Phase 6: User Story 5 — refusals with specific guidance (P2)

**Goal**: every refused construct names itself and the Elsa 3 rewrite (FR-016 to FR-021, research R-7,
[contracts/javascript-conversion.md](contracts/javascript-conversion.md) "Refused").

**Independent test**: one definition per refused construct gives its code and guidance.

- [ ] T028 [US5] In `Elsa3ExpressionRewriter.JavaScriptAnalyzer`, add a private static `FrozenDictionary<string, (string Code, string Message, string Guidance)> RefusedHostFunctions` with every "Refused" row of the contract that is keyed by a function name. Look it up before the generic "unknown or unapproved host function" fallback
- [ ] T029 [US5] Make `UnavailableGlobal` and `IsNondeterministicCall` use specific guidance:
  - `Date` (including `Date.now`): Delay activity and inputs
  - `Math.random` / `crypto.randomUUID`: inputs
  - Keep the codes
- [ ] T030 [P] [US5] Tests in `Elsa3JavaScriptHostFunctionTests.cs`:
  - A theory over every refusal-table row asserting code, message and guidance substrings (FR-017 to FR-019, SC-004)
  - `setOutcome('x')` inside RunJavaScript refuses the whole definition (`result.Succeeded == false`, no `Value`)
  - Every spec 236 refusal row in `Elsa3JavaScriptConversionTests.cs` keeps its code (FR-020)
- [ ] T031 [US5] Rewrite the JavaScript section of `Extensions/Elsa3/src/Mapping/README.md` as the migration story (FR-021, SC-005):
  - What converts
  - The refusal table with each Elsa 3 rewrite
  - The statement that workflows which need the current time or a fresh unique value cannot migrate until Elsa 4 has an explicit capability, linking the follow-up issue (T040) once it is filed

**Checkpoint**: Mapping tests are green.

---

## Phase 7: User Story 4 — pure helpers (P3)

**Goal**: `isNullOrEmpty` and `isNullOrWhiteSpace` convert exactly; `toJson`, `parseGuid` and the byte helpers stay
refused (FR-015, FR-016, research R-6).

**Independent test**: the converted helpers return Elsa 3's results on `null`, `""`, `"  "`, `"a"`, `"\u0085"` and
`"\uFEFF"`.

- [ ] T032 [US4] In `Elsa3ExpressionRewriter`, replace undeclared `isNullOrEmpty(x)` / `isNullOrWhiteSpace(x)` (exactly one argument) with `__elsa3IsNullOrEmpty(x)` / `__elsa3IsNullOrWhiteSpace(x)`. Then:
  - Prepend the prelude functions from the contract once per expression. This forces the wrapped form `(function () {\n<prelude>\n…\n})()` in Expression mode, and a leading statement in Script mode
  - Refuse with VF-IMP-002 if the script declares either name
  - Any other argument count stays refused
- [ ] T033 [P] [US4] Tests in `Elsa3JavaScriptHostFunctionTests.cs`:
  - The truth tables above, evaluated with T002, against the values .NET's `string.IsNullOrEmpty` / `IsNullOrWhiteSpace` give for the same strings (computed in the test)
  - The argument is evaluated once (an argument with a side effect on a local array)
  - `toJson`, `parseGuid` and `bytesToString` give their refusal rows (covered by T030; here, assert only that they are not lowered)

**Checkpoint**: Mapping tests are green.

---

## Phase 8: User Story 6 — retire the pending-graph diagnostic (P3)

**Goal**: FR-024 and FR-025; D-6 is approved.

**Independent test**: the catalog and XML doc say "retired"; the removed members no longer exist; the full Mapping
suite is green.

- [ ] T034 [P] [US6] In `Extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs`, rewrite the XML doc of `GraphConversionPending`: "Retired 2026-10-19 (spec 247): no longer raised since spec 239 converts Flowchart, FlowFork, FlowJoin and Start; kept because a code is never removed or reused." In `specs/236-elsa3-activity-map/contracts/diagnostic-codes.md`, mark the row **Retired** with the same reason
- [ ] T035 [US6] Remove the pending-graph path (D-6):
  - `Elsa3ActivityTarget.GraphLevel` in `Extensions/Elsa3/src/Mapping/ActivityMap/Elsa3ActivityTarget.cs`
  - its case in `Extensions/Elsa3/src/Mapping/Resolution/Elsa3ActivityResolver.cs`
  - `IsDeferred` and its three uses in `Extensions/Elsa3/src/Mapping/Conversion/Elsa3DefinitionConverter.cs`, so `PrepareAsync` and `LowerAsync` fail on any error
  - `Deferred` and its protected constructor parameter in `Extensions/Elsa3/src/Mapping/Conversion/IElsa3NodeConverter.cs`
  - the constructor call in `Extensions/Elsa3/src/Mapping/Conversion/Elsa3PreparedNodeConversionContext.cs`
  - the deferred remarks in `IElsa3NodeConverter.cs`
  - the "deferred" sentence and the constructor signature in `Extensions/Elsa3/src/Mapping/EXTENSION_POINTS.md`
- [ ] T036 [US6] Update the tests:
  - `Elsa3ActivityResolverTests.cs`: remove the `GraphLevel` test map and the `Elsa.FlowFork` → `GraphConversionPending` row
  - `Elsa3NodeConverterTests.cs`: remove the `Deferred` assertion and the filter on line 59
  - `Elsa3MappingFeatureRegistrationTests.cs`: update the test context constructor
  - Keep `Elsa3FlowchartConversionTests` asserting the code never appears

**Checkpoint**: Mapping tests are green; `grep -rn "GraphLevel(" Extensions/Elsa3/src` matches only `Elsa3MemberRuleKind.GraphLevel`.

---

## Phase 9: Polish and cross-cutting

- [ ] T037 Run the touched suites:
  - `dotnet test Extensions/Elsa3/tests/Mapping/Tests`
  - `dotnet test Essentials/tests/Expressions/Tests`
  - `dotnet test Essentials/tests/Workflows/Runtime/Tests`
  - `dotnet test Essentials/tests/Activities/Runtime/Tests`
  - the architecture guard tests (`Essentials/tests/Architecture/Tests`, filter `ArchitectureGuardTests|Elsa3MigrationBoundaryTests`) for §E2.2 and §E2.7
- [ ] T038 Refresh the generated maps: run `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, then `git diff --stat docs/` (the local `-- check` is unreliable on Windows). Stage only the maps that actually changed, by explicit path, including `docs/maps/manifest.json`
- [ ] T039 Measure (SC-001, SC-002, SC-006):
  - Rerun the throwaway probes `ZzJsProbe` (with the Literal-script extension) and `ZzCorpusProbe` with `JTEST_CORPUS` set
  - Record: parse refusals (target 0), definitions with fully converting JavaScript (target ≥ 7 of 33), and the remaining refusal counts per construct
  - Convert one corpus definition twice and compare bytes
  - Keep the numbers for the PR evidence. Do not commit the probes
- [ ] T040 Draft the follow-up issue for an explicit Elsa 4 time and unique-value capability (FR-022). Use the corpus numbers (`newGuid` in 17 definitions, `Date` in 9) and the migration story. Show the text to the maintainer and file it as `needs-triage` only after approval; then link it from the README (T031)
- [ ] T041 Update `specs/247-elsa3-js-host-functions/spec.md` status to `Implemented — <PR link>` per `docs/reference/spec-lifecycle.md` when the PR opens. Remove the probe files from the worktree before the final `git status` check

---

## Dependencies and execution order

```text
Phase 1 (T001-T002) → Phase 2 (T003-T004) → US1 (T005-T008)
US1 → US2 (T009-T013)           # accessors build on the rewriter changes of US1
US2 → US3 converter (T023-T024)  # the built-in collision check uses GeneratedAccessor resolution
US3 Core/Runtime (T014-T022) can start after Phase 2, in parallel with US1/US2 (different projects)
US3 docs (T025-T027) after T014
US5 (T028-T031) after US3 converter (the refusal table sits beside the lowered built-ins)
US4 (T032-T033) after US1 (prelude needs the wrapped form)
US6 (T034-T036) independent of US1-US5; any time after Phase 2
Phase 9 last
```

Story completion order: US1 → US2 → US3 → US5 → US4 → US6. US6 and the Core/Runtime half of US3 may run in parallel
with the rest.

## Parallel examples

- After Phase 2: T014–T022 (Essentials) alongside T005–T008 (Extensions/Elsa3), because the files are disjoint.
- In US3: T016, T021 and T022 (different test projects) once T014–T020 are in.
- In US6: T034 alongside T035.

## Implementation strategy

1. **MVP = US1.** It alone removes all 200 parse refusals (SC-001) and is reviewable on its own.
2. Add US2. The largest category (201 accessor uses) converts.
3. Add US3. This is the only Essentials change; review the ADR amendment with the maintainer before moving on.
4. Add US5 and US4. The migration story is complete.
5. US6 cleanup.
6. Measure and draft the follow-up issue; then ask the maintainer before pushing and opening a draft PR against `main`.

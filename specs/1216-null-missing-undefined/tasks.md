# Tasks: Null, missing and undefined

**Input**: Design documents from `specs/1216-null-missing-undefined/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/null-policies.md, quickstart.md

**Gate**: start once the coordinator has approved and merged this spec (D-18). S-8's value bridge, which Phase 7
builds on, is merged.

**Tests**: required by framework §2.23 for every logic-bearing change, and by SC-004 to SC-006. The application
declares no TDD cadence, so tests follow implementation within each story; the PR's bite-proof shows each new test
red without the change. xUnit only.

**Build rule**: build and test only the projects a task touches (shared machine); CI runs the rest.

## Phase 1: Setup

- [x] T001 Confirm the ADRs this spec applies are merged and diff their text against spec.md. Done in the readiness
  recheck: ADR 0045 §2, ADR 0083, ADR 0091 decisions 3 and 8, ADR 0046 V8 and ADR 0089 R3 are on the initiative branch
  and the spec applies them as merged
- [x] T002 Align the proposed diagnostic codes with the existing catalogs (`VF-ACT-*`, `VF-READ-*`, S-8's
  `VF-PREC-*`, `runtime.input.*`). Done: final names in contracts/null-policies.md
- [ ] T003 Run the #3052 JTest measurement on the implementation branch before any change and record the baseline in
  the PR

## Phase 2: Foundational (blocks every story)

- [ ] T004 Implement FR-001a from the type registry's existing `supportsNull` (`DefaultVariableTypeDescriptorProvider`)
  for variables, workflow inputs and outputs and authored activity definitions; .NET activities keep their C#
  nullability. Nothing reads a stored `isNullable` flag for authored members any more
- [ ] T004a FR-001c: an unassigned variable reads its type default (`false`, `0`, an enum's zero value) where the
  read materializes a variable with no value (S-4's read path, the variable store's initial value, and
  `GraphActivity`'s ambient variable read), `null` for a
  type that accepts it; publication refuses a date, time, `TimeSpan` or `Guid` variable without a declared default
  (`VF-ACT-003`). Update S-4's test for spec 1222 US1 scenario 6 for a value-type variable
- [ ] T005 [P] Add `NullPolicyDefaults` in `Essentials/src/Workflows/Runtime/Core/Services/NullPolicyDefaults.cs`
  (FR-001b) with two lookups: `TryGetTypeDefault(alias)` for authored members (registry aliases) and
  `TryGetTypeDefault(Type)` for .NET activity inputs (`bool`, every numeric primitive including `float`, `short`,
  `byte` and `uint`, and any enum). A default for `Boolean`, the number types and enums only; unit tests including `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`,
  `Guid`, `String`, `Any` and a collection of `Int32` having none, a collection accepting `null` (FR-001a), and a
  .NET enum and `float` having one
- [ ] T006 Add `NullHandling` (`DefaultOnNull`, `UndefinedAsNull`) to
  `Essentials/src/Workflows/Design/Core/Models/WorkflowStrategyOptions.cs`, `ValuePolicies` to
  `Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutable.cs`, and compile one into the other in
  `WorkflowExecutableCompiler.cs` (data-model.md)
- [ ] T007 Add `IsNullable` to `WorkflowDeclaredInput` in `WorkflowExecutableInputContract.cs` and fill it in
  `WorkflowExecutableCompiler.BuildInputContract` from the input's type (T004, FR-001a; R-4). Publication refuses an
  optional, non-nullable workflow input whose type has no non-null default and that declares no default
  (`VF-ACT-003`, FR-006). The message for a workflow input says to turn *Required* on or declare a default (through
  the API or JSON), because Studio's inputs table has no default column
- [ ] T007a Activity inputs (FR-006, FR-001b): in `RuntimeInputBindingCompiler.CompileOmitted`, replace
  `TryCreateClrDefault` (any value type's CLR zero value) with `NullPolicyDefaults.TryGetTypeDefault(Type)`, so an
  omitted optional enum or numeric input keeps its zero value, an omitted nullable input stays `Absent` (the activity
  receives `null`), and an omitted
  optional date, time, `TimeSpan` or `Guid` input without a declared default fails `VF-ACT-003`. Audit built-in
  activities for such inputs (`Delay.Duration` declares a default and is unaffected), declare a default where one is
  meant, and test
- [ ] T008 Hand the executable's pinned `UndefinedAsNull` to every JavaScript evaluation; no per-binding field (R-2).
  Bindings: the input materializer holds the executable. `Run JavaScript`: the runtime puts the executable's value
  policies on `IActivityExecutionContext` (both `ActivityExecutionContext` and `SimpleActivityExecutionContext`, which
  `GraphActivity` runs on), and the activity passes them with its evaluation request,
  which also covers FR-030 writes made in that script session. `Set` / `SetOutput` expressions
  (`WorkflowIntrinsicExecutor`) get it from the executable the executor holds. Expressions inside an authored
  activity definition's graph (`GraphActivity`) use the host workflow's policies, taken from the host's
  `ActivityExecutionContext`, because the graph runs as part of the host's step
- [ ] T008a Expose `strategyOptions.nullHandling` through the workflow definition API (read and write) and the
  exported JSON, with an API round-trip test (FR-024)
- [ ] T009 Write `valuePolicies` in `WorkflowExecutableHasher.WriteBehavioralPayload` only when a policy is on, and an
  input's nullability only when it is non-nullable (most workflow inputs are nullable). Tests: turning a policy on
  changes the hash (ADR 0038, US5 scenario 3); a non-nullable input writes its pinned nullability, and changing an
  input's type between a nullable and a non-nullable one (`String` to `Int32`) moves the hash. Regenerate in place any stored fixture
  whose hash moves

## Phase 3: User Story 1 - Set a variable or output to null (P1) 🎯 MVP

**Independent test**: a `Set` writing literal `null` into a nullable variable publishes and runs.

- [ ] T010 [US1] Build the intrinsic `value` definition with the target's nullability in
  `ExecutableNodeCompiler.CompileIntrinsicNode` (R-5, FR-001a: by the target's type); keep `name` and `outcome`
  non-nullable
- [ ] T010a [US1] Run time: in `WorkflowIntrinsicExecutor`, check a `null` from every `Set` / `SetOutput` source
  (literal, variable, request member, expression) against the target's nullability before it is written:
  `VF-ACT-004` for a non-nullable target, the type default under `DefaultOnNull` (R-6), recording `VF-NULL-001` on the
  intrinsic's execution like an activity input. Today `ValidateAssignmentPayload` skips anything not `Present`. Tests:
  a literal `null`, a variable holding `null` and the expression `null` into an `Int32` variable, with and without the
  policy
- [ ] T010b [US1] Step writes: in `StepVariableSession.Write` (used by `Run JavaScript` script writes through
  `StepScriptVariables` and by .NET activities through `IStepVariables`), check a `null` against the variable's
  nullability before it becomes an intent: `VF-ACT-004` for a non-nullable variable, the type default under
  `DefaultOnNull` (from the executable the session holds), recording `VF-NULL-001` on the step's invocation. Today
  `Validate` skips anything not `Present`, so `variables.count = null` on an `Int32` variable is written. Tests: a
  script write and an `IStepVariables` write of `null` to an `Int32` variable, with and without the policy
- [ ] T010c [US1] Output captures: in `RuntimeOutputCaptureProjector` (frame path and graph-row path; today an
  `ExplicitNull` projection is written and `RuntimeValueConversionExecutor` passes any non-`Present` source through),
  check a `null` captured into a variable or workflow output against the target's nullability: `VF-ACT-004` for a
  non-nullable target, the type default under `DefaultOnNull` with `VF-NULL-001` on the invocation. Tests: a
  `Run JavaScript` result of `null` bound to an `Int32` variable, with and without the policy
- [ ] T010d [US1] Variable declared defaults: `ExecutableNodeCompiler.CompileVariableDeclaration` compiles a
  variable's default against the variable's type-derived nullability (T004) instead of `IsNullable: true`, so a
  declared default of `null` on an `Int32` variable fails publication with `VF-ACT-004`. Default-on-null does not
  apply to an authored default: the author fixes it. Test
- [ ] T010e [US1] Operator alteration: `ModifyVariableAlterationHandler.IsCompatible` refuses JSON `null` for a
  non-nullable variable (`VariableValueTypeMismatch`). Default-on-null does not apply to operator writes. Test
- [ ] T011 [US1] Tests in `Essentials/tests/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs`: null
  literal into a nullable variable and a nullable output publishes; into an `Int32` variable fails with `VF-ACT-004`
- [ ] T012 [US1] Run test: the variable holds `ExplicitNull` and a workflow output persists an explicit JSON `null`
  for every output storage kind (`WorkflowIntrinsicExecutor.NewWorkflowOutputChange` writes it only for Inline and
  Custom today; fix the others)

## Phase 4: User Story 2 - Start a workflow without an optional input (P1)

**Independent test**: `composition-bulk-dispatch-sub-workflow` starts with no input and completes.

- [ ] T013 [US2] Extend #3731's admission (R-3). In `WorkflowExecutableInputValidator`'s complete-bag normalization:
  complete an omitted optional input without a declared default with JSON `null` when nullable, otherwise its type
  default (`NullPolicyDefaults`); refuse an explicit `null` for a non-nullable input with
  `runtime.input.null-not-allowed`; accept an explicit `null` for a nullable input without running the type
  conversion, and leave that conversion as it is (S-9 owns it). In `WorkflowStartDispatcher.ApplyInputContract`, and
  in the early checks of `StartWorkflowTestRunRequestHandler` and `DispatchWorkflow` and in Design's
  `DispatchPinSource` (which validates a child workflow's pinned inputs at publication), substitute the type default
  for such a `null` before validating when the pinned `DefaultOnNull` is on (no separate record, FR-013). One shared
  helper does the substitution, so all four give the same answer; the early checks exist because they refuse before
  their own side effects (#3731). Known-inputs mode (`ValidateKnownInputs`) applies the same `null` refusal and
  substitution to the members it is given; it fills declared defaults as today but adds no `null` or type-default
  completion
- [ ] T013a [US2] Q9 (FR-034): publication refuses a member declared both required and with a default with
  `VF-ACT-014` (activity inputs, authored activity definition inputs, workflow inputs); remove the validator's
  `!DefaultValue.HasValue` exemption, which can then never apply. Audit built-in activity descriptors for inputs
  declared that way, declare them optional, and test
- [ ] T014 [US2] Check that `RuntimeWorkflowStateSeed.BuildSeedChanges` stores a completed JSON `null` as an explicit
  null envelope, not a present JSON `null`, and fix it if not
- [ ] T015 [US2] Tests for S-2's additions (US2 scenarios 2, 3 and 5, and the `null` refusal) on the API, trigger,
  test-run and child-workflow paths, beside #3731's `WorkflowStartInputContractTests`; scenarios 1 and 4 are covered
  there already. Include a refused start that schedules nothing, and one test per validator mode proving known-inputs
  mode fills declared defaults but adds no `null` or type-default completion
- [ ] T016 [US2] Behavioural test: a JavaScript condition reads an omitted optional input as `null`
  (`Essentials/tests/Activities/Behavioral/Tests`)

## Phase 5: User Story 7 - Null and absent survive storage (P1)

**Independent test**: suspend and resume with one activity input bound to `null` and one omitted nullable activity
input, and one workflow input supplied as `null`.

- [ ] T017 [P] [US7] Round-trip tests for `ExplicitNull` and `Absent` envelopes in every runtime store provider
  (in-memory and each EF provider), covering invocation records, workflow request rows, variables and outputs
- [ ] T018 [US7] Fix any store that loses the difference; confirm `RuntimeInputBinding.LiteralValue` keeps `Absent`
  and `ExplicitNull` apart wherever it is read back. A fix that needs a serializer option change goes to S-9 (message
  its session first; S-2 does not edit the baseline)

## Phase 6: User Story 3 - Null into a non-nullable input, by policy (P2)

**Independent test**: `return null;` into an `Int32` input gives `0` under the policy, `VF-ACT-004` without.

- [ ] T019 [US3] Publication: in `RuntimeInputBindingCompiler.ValidateNull` (activity inputs and the intrinsic `value`
  input alike), keep a literal `null` for a non-nullable input when the workflow's `DefaultOnNull` is on and the type has a type default, so T020 substitutes it at run time
  and records `VF-NULL-001` like any other `null` (R-6)
- [ ] T020 [US3] Run time: in `RuntimeActivityInputMaterializer.ValidatePresence`, substitute the type default for
  `ExplicitNull` under the policy and record `VF-NULL-001` on the invocation
- [ ] T020a [US3] Graphs: in `GraphActivity.CoerceExpressionResult` (graph inner nodes skip the input materializer),
  apply the host workflow's `DefaultOnNull` from `IActivityExecutionContext` (T008): substitute the type default and record
  `VF-NULL-001`, otherwise refuse with `VF-ACT-004` instead of today's uncoded `InvalidOperationException`. Test
- [ ] T021 [US3] Tests for each US3 acceptance scenario, including a non-nullable `String` that still fails, and
  update `ValueDurabilityPolicyTests.Snapshot_materialization_rejects_nullish_value_for_non_nullable_contract` to
  cover the policy off and on

## Phase 7: User Story 4 - `undefined` at the JavaScript boundary (P2)

**Independent test**: `({ a: undefined, b: NaN, c: [1, , undefined] })` with and without the policy.

- [ ] T022 [US4] In S-8's bridge writer (`CopyComparePatchValueBridge`), run the replacer on every written value
  (today `Mark` skips it for a non-object value and for an object without an entered object, so a top-level `0/0` or
  `1/0` is written as `null` silently) and add `undefined` and `NaN` markers that `WriteMarked` resolves
  with their paths (R-7), following S-8's value-free rule for paths (FR-028): a dropped
  `undefined` property records `VF-UNDEF-002`; an `undefined` array element or hole becomes a `null` placeholder with
  `VF-UNDEF-003` (holes checked by index, FR-016a); a computed `NaN` or `±Infinity` becomes `null` with `VF-NUM-001`, after
  S-8's precision check; under `UndefinedAsNull` an `undefined` property is kept as `null`. Add the codes to
  `JavaScriptValueDiagnostics`; warnings go through `IExecutionWarningSink`. Check that the byte, depth and node
  limits still count the output
- [ ] T023 [US4] Under `UndefinedAsNull`, an `undefined` binding result materializes as `null` in
  `JintPortableJavaScriptEvaluator` for both binding forms; without it the ADR 0083 error stays, now carrying
  `VF-UNDEF-001` (today an uncoded `InvalidOperationException`) with the ADR 0083 guidance
- [ ] T024 [US4] `Run JavaScript` with a bound result (R-8): refuse a body with no `return` statement at publication
  (`VF-UNDEF-001`, from the publish-time JavaScript parse, where the output binding is known); never refuse a binding
  for lacking one. At run time the activity's result carries "no value" (a top-level `undefined`) distinct from JSON
  `null`; the runtime step that writes the result to its bound target faults on "no value" with `VF-UNDEF-001`, or
  writes `ExplicitNull` under the policy; an unbound result writes nothing
- [ ] T025 [US4] `undefined` assigned to a whole variable (FR-030, Q7): carry "no value" distinct from JSON `null`
  from the bridge (today a top-level `undefined` becomes `ScriptValueWrite(null)`) through to
  `StepVariableSession.Write` (today it maps `Undefined` to `ValueEnvelope.Null`); there the step faults with
  `VF-UNDEF-001` naming the variable, or writes `ExplicitNull` under the policy. A member set to `undefined` follows
  T022. End-to-end tests through `Run JavaScript`, including `variables.discount = order.discount` with no
  `order.discount`
- [ ] T026 [US4] Tests for each US4 acceptance scenario, including a `JSON.stringify` string that is not rewritten,
  `undefined` at depth three, a hole, a top-level `0/0` and `1/0` binding result, a `Run JavaScript` body that returns on some paths only, and a path warning
  that names only the root when the evaluation read a protected value (FR-028)

## Phase 8: User Story 5 - Converted workflows carry their policies (P2)

**Independent test**: convert, export, import on a host without the converter, run: same outputs.

- [ ] T027 [US5] Add `Elsa3MappingOptions.ApplyElsa3NullPolicies` (default `true`) in
  `Extensions/Elsa3/src/Mapping/Elsa3MappingOptions.cs`, bind it in `Elsa3MappingFeature`, with a feature
  registration test (options binding carries logic)
- [ ] T028 [US5] Set `StrategyOptions.NullHandling` in `Elsa3WorkflowDefinitionToState.Map` when the option is on, and
  list the policies in the conversion report; say the stricter Elsa 4 rules apply when it is off. Test both converter
  entry points: an Elsa 3 import and Elsa 3 JSON reconciled at engine start (FR-022)
- [ ] T029 [US5] Remove `AddNullCoalescing` from `Elsa3ExpressionRewriter` (FR-023) and update the spec 262 tests
- [ ] T030 [US5] Narrow spec 267's `NullRefused` for SetVariable `Value` and SetOutput `OutputValue` in
  `Elsa3BuiltInActivityMap.Entries.cs`: emit a typed `Literal` null for a nullable target, or when `DefaultOnNull` is
  recorded and the target type has a type default (`NullPolicyDefaults`); refuse otherwise, including a non-nullable
  `String` target (FR-029); update `Elsa3NullMemberTests`
- [ ] T031 [US5] Verify Elsa 3 3.6.3 input requiredness (R-11) and map converted inputs' requiredness accordingly;
  nullability follows the converted type (Q8) and is not mapped
- [ ] T031a [US5] Converter (FR-001b, FR-001c, FR-006): an Elsa 3 variable, or an optional workflow input, of a
  date, time, `TimeSpan` or `Guid` type with no starting value is refused (a required input converts as is) at conversion with an `ELS3-*` diagnostic that says to
  declare a default (or type the member `String` / `Any`), instead of converting and then failing publication. List
  these refusals in the conversion report and the Elsa 3 migration readiness guide; T003 and T039 count them
- [ ] T032 [US5] Export/import round-trip test: `nullHandling` survives. Parity test (SC-004): run every US3 and US4
  acceptance scenario of T021 and T026 once as a native workflow and once as a converted one with the same policies,
  and compare outputs and diagnostics

## Phase 9: Studio (Q6: no policy controls; Q8, Q9)

- [ ] T033 [P] Retire *Allow null* (Q8): drop `isNullable` from `WorkflowInput` / `WorkflowOutput`
  (`workflowTypes.ts`) and from `createInput` / `createOutput` (`workflowProperties.ts`); remove the *Allow null*
  checkbox and its rules from `ActivityDefinitionContractEditor.tsx`, `activityContractAuthoring.ts` and
  `activityContractComparison.ts` (a nullability change becomes a type change), the "Allows null" fact in
  `activityContractProposalModel.ts`, and the flag from `activityDefinitionTypes.ts` and `activityInputWire.ts`; the
  activity definition test run (`ActivityDefinitionTestRunDialog.tsx`, `activityDefinitionTestRuns.ts`), the
  "Allows null" / "Non-null" labels in `ActivityDefinitionWorkbench.tsx`, the read in
  `ActivityGraphImplementationEditor.tsx` and the test-drive inputs in `workflow-editor/WorkflowTestDrivePanel.tsx`
  take nullability from the type; the test-drive panel also drops its "required unless a default exists" rule (Q9). On the backend, authored members stop carrying the flag; for .NET
  activities the descriptor's nullability is derived from the C# type, not authored. Regenerate any stored fixture
  that carried it
- [ ] T034 [P] Activity definition contract editor (`ActivityDefinitionContractEditor.tsx`): setting a default turns
  *Required* off, and *Required* is disabled while a default is set, with the hint "A member with a default is
  optional"; replace today's hint "A value must be available after the default is applied" (Q9). Client test

- [ ] T035 Check that the `VF-UNDEF-*`, `VF-NUM-001` and `VF-NULL-001` warnings show in Studio's existing activity
  execution warnings with no new surface (FR-033); a Studio client test if a mapping is missing

## Phase 10: User Story 6 - A computed variable name that matches nothing (P3)

Built by S-4 (spec 1222 FR-011, FR-012; `VF-READ-001`).

- [ ] T036 [US6] Confirm S-4's tests (`ScriptVariableReadMaterializationTests`, `VariablesRootEvaluationTests`) still
  cover US6 scenarios 1 to 3 after this spec's changes; no new code

## Phase 11: Polish

- [ ] T037 [P] Docs (FR-031): the Elsa 3 converter feature's documentation and the workflow definition reference
  state, for the option and each policy, what it changes at run time with a before/after example, that converted
  workflows record it and native workflows keep it off; glossary rows for *default-on-null* and
  *undefined-as-null*, Elsa 3 Mapping README and migration readiness guide (spec 262 and 267 sections), and the
  `IWorkflowExecutableInputValidator` and `IWorkflowStartDispatcher` *Usage* lines in the Runtime `EXTENSION_POINTS.md`
  (completion, `null` refusal, default-on-null at admission)
- [ ] T038 [P] Q5 audit (R-12): list built-in activity inputs that cannot hold `null` and whose Elsa 3 counterparts
  accepted it; declare each nullable where the Elsa 4 activity handles `null`, with a test, and record the rest in the
  Elsa 3 migration readiness guide
- [ ] T039 Rerun the #3052 JTest measurement and post it on #3052 (SC-002, SC-003), listing T031a's refusals
- [ ] T040 Close #3280, #3288, #3172 and #3170 after the merge (initiative branch: they do not auto-close); set this
  spec's **Status** to `Implemented` in the delivery PR

## Dependencies

- Phase 2 blocks every story. T013 builds on #3731. T010a's and T010b's "with the policy" tests need T019 (a literal
  `null` must pass publication) and T006.
- US1, US2 and US7 (P1) can run in parallel after Phase 2. US3 needs T006. US4 needs T008.
  US5 needs US3 and US4 (the policies it records must work). Phase 9 needs Phase 7.
- MVP: Phases 1–5.

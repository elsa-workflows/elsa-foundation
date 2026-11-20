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
- [ ] T005 [P] Add `NullPolicyDefaults.TryGetTypeDefault(alias)` in
  `Essentials/src/Workflows/Runtime/Core/Services/NullPolicyDefaults.cs` (FR-001b): a default for `Boolean`, the
  number types and enums only; unit tests including `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`,
  `Guid`, `String` and `Any` having none
- [ ] T006 Add `NullHandling` (`DefaultOnNull`, `UndefinedAsNull`) to
  `Essentials/src/Workflows/Design/Core/Models/WorkflowStrategyOptions.cs`, `ValuePolicies` to
  `Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutable.cs`, and compile one into the other in
  `WorkflowExecutableCompiler.cs` (data-model.md)
- [ ] T007 Add `IsNullable` to `WorkflowDeclaredInput` in `WorkflowExecutableInputContract.cs` and fill it in
  `WorkflowExecutableCompiler.BuildInputContract` from the input's type (T004, FR-001a; R-4). Publication refuses an
  optional, non-nullable workflow input whose type has no non-null default and that declares no default
  (`VF-ACT-003`, FR-006), as it already does for activity inputs
- [ ] T008 Hand the executable's pinned `UndefinedAsNull` to every JavaScript evaluation (binding and `Run
  JavaScript`) from the callers that already hold the executable; no per-binding field (R-2)
- [ ] T008a Expose `strategyOptions.nullHandling` through the workflow definition API (read and write) and the
  exported JSON, with an API round-trip test (FR-024)
- [ ] T009 Write `valuePolicies` and input `isNullable` in `WorkflowExecutableHasher.WriteBehavioralPayload` only when
  set; test that each one changes the hash when set (ADR 0038, US5 scenario 3). Regenerate in place any stored fixture
  whose hash moves

## Phase 3: User Story 1 - Set a variable or output to null (P1) 🎯 MVP

**Independent test**: a `Set` writing literal `null` into a nullable variable publishes and runs.

- [ ] T010 [US1] Build the intrinsic `value` definition with the target's nullability in
  `ExecutableNodeCompiler.CompileIntrinsicNode` (R-5, FR-001a: by the target's type); keep `name` and `outcome`
  non-nullable
- [ ] T010a [US1] Run time: in `WorkflowIntrinsicExecutor`, check a `null` from a `Set` / `SetOutput` expression
  against the target's nullability: `VF-ACT-004` for a non-nullable target, the type default under `DefaultOnNull`
  (R-6). Test: the expression `null` into an `Int32` variable faults today's silent write
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
  in the early checks of `StartWorkflowTestRunRequestHandler` and `DispatchWorkflow`, substitute the type default for
  such a `null` before validating when the pinned `DefaultOnNull` is on. The start command carries the
  substitutions, and the seed records them as `VF-NULL-001` through S-8's warning channel when the instance starts. One shared helper does the substitution; the early checks call it only because they refuse
  before their own side effects (#3731), so all three give the same answer
- [ ] T013a [US2] Q9 (FR-034): publication refuses a member declared both required and with a default with
  `VF-ACT-013` (activity inputs, authored activity definition inputs, workflow inputs); remove the validator's
  `!DefaultValue.HasValue` exemption, which can then never apply. Audit built-in activity descriptors for inputs
  declared that way, declare them optional, and test
- [ ] T014 [US2] Check that `RuntimeWorkflowStateSeed.BuildSeedChanges` stores a completed JSON `null` as an explicit
  null envelope, not a present JSON `null`, and fix it if not
- [ ] T015 [US2] Tests for S-2's additions (US2 scenarios 2, 3 and 5, and the `null` refusal) on the API, trigger,
  test-run and child-workflow paths, beside #3731's `WorkflowStartInputContractTests`; scenarios 1 and 4 are covered
  there already. Include a refused start that schedules nothing, and one test per validator mode proving known-inputs
  mode does not complete members
- [ ] T016 [US2] Behavioural test: a JavaScript condition reads an omitted optional input as `null`
  (`Essentials/tests/Activities/Behavioral/Tests`)

## Phase 5: User Story 7 - Null and absent survive storage (P1)

**Independent test**: suspend and resume with one explicit-null and one omitted input.

- [ ] T017 [P] [US7] Round-trip tests for `ExplicitNull` and `Absent` envelopes in every runtime store provider
  (in-memory and each EF provider), covering invocation records, workflow request rows, variables and outputs
- [ ] T018 [US7] Fix any store that loses the difference; confirm `RuntimeInputBinding.LiteralValue` keeps `Absent`
  and `ExplicitNull` apart wherever it is read back. A fix that needs a serializer option change goes to S-9 (message
  its session first; S-2 does not edit the baseline)

## Phase 6: User Story 3 - Null into a non-nullable input, by policy (P2)

**Independent test**: `return null;` into an `Int32` input gives `0` under the policy, `VF-ACT-004` without.

- [ ] T019 [US3] Publication: in `RuntimeInputBindingCompiler.ValidateNull`, pin the type default when the workflow's
  `DefaultOnNull` is on and the type has one (R-6)
- [ ] T020 [US3] Run time: in `RuntimeActivityInputMaterializer.ValidatePresence`, substitute the type default for
  `ExplicitNull` under the policy and record `VF-NULL-001` on the invocation
- [ ] T021 [US3] Tests for each US3 acceptance scenario, including a non-nullable `String` that still fails, and
  update `ValueDurabilityPolicyTests.Snapshot_materialization_rejects_nullish_value_for_non_nullable_contract` to
  cover the policy off and on

## Phase 7: User Story 4 - `undefined` at the JavaScript boundary (P2)

**Independent test**: `({ a: undefined, b: NaN, c: [1, , undefined] })` with and without the policy.

- [ ] T022 [US4] In S-8's bridge writer (`CopyComparePatchValueBridge`), run the replacer on every written object
  (today `Mark` skips it without an entered object) and add `undefined` and `NaN` markers that `WriteMarked` resolves
  with their paths (R-7): a dropped
  `undefined` property records `VF-UNDEF-002`; an `undefined` array element or hole becomes a `null` placeholder with
  `VF-UNDEF-003` (holes checked by index, FR-016a); a computed `NaN` or `±Infinity` becomes `null` with `VF-NUM-001`, after
  S-8's precision check; under `UndefinedAsNull` an `undefined` property is kept as `null`. Add the codes to
  `JavaScriptValueDiagnostics`; warnings go through `IExecutionWarningSink`. Check that the byte, depth and node
  limits still count the output
- [ ] T023 [US4] Under `UndefinedAsNull`, an `undefined` binding result materializes as `null` in
  `JintPortableJavaScriptEvaluator` for both binding forms; without it the ADR 0083 error stays
- [ ] T024 [US4] `Run JavaScript` with a bound result (R-8): refuse a body with no `return` statement at publication
  (`VF-UNDEF-001`, from the publish-time JavaScript parse); never refuse a binding for lacking one; fault the step when
  a bound result is `undefined` at run time, `ExplicitNull` under the policy; an unbound result writes nothing
- [ ] T025 [US4] `undefined` assigned to a whole variable (FR-030, Q7): the bridge's change report marks it; the step
  faults with `VF-UNDEF-001` naming the variable, or writes `ExplicitNull` under the policy. A member set to
  `undefined` follows T022. Test `variables.discount = order.discount` with no `order.discount` as well. If S-7
  part 2 (script writes) is not merged yet, test at the bridge and leave the end-to-end test to whichever lands
  second
- [ ] T026 [US4] Tests for each US4 acceptance scenario, including a `JSON.stringify` string that is not rewritten,
  `undefined` at depth three, a hole, and a `Run JavaScript` body that returns on some paths only

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
- [ ] T031 [US5] Verify Elsa 3 3.6.3 input requiredness (R-11) and map converted inputs' requiredness and nullability
  accordingly
- [ ] T032 [US5] Export/import round-trip test: `nullHandling` survives. Parity test (SC-004): run every US3 and US4
  acceptance scenario of T021 and T026 once as a native workflow and once as a converted one with the same policies,
  and compare outputs and diagnostics

## Phase 9: Studio (Q6: no policy controls; Q8, Q9)

- [ ] T033 [P] Retire *Allow null* (Q8): drop `isNullable` from `WorkflowInput` / `WorkflowOutput`
  (`workflowTypes.ts`, `createInput` / `createOutput`) and the *Allow null* checkbox and its rules from
  `ActivityDefinitionContractEditor.tsx`, `activityContractAuthoring.ts` and `activityContractComparison.ts` (a
  nullability change becomes a type change). On the backend, authored members stop carrying the flag; for .NET
  activities the descriptor's nullability is derived from the C# type, not authored. Regenerate any stored fixture
  that carried it
- [ ] T034 [P] Activity definition contract editor (`ActivityDefinitionContractEditor.tsx`): setting a default turns
  *Required* off, and *Required* is disabled while a default is set, with the hint "A member with a default is
  optional"; replace today's hint "A value must be available after the default is applied" (Q9). Client test

- [ ] T035 Check that the `VF-UNDEF-*`, `VF-NUM-001` and `VF-NULL-001` warnings show in Studio's existing instance
  diagnostics with no new surface (FR-033); a Studio client test if a mapping is missing

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
- [ ] T039 Rerun the #3052 JTest measurement and post it on #3052 (SC-002, SC-003)
- [ ] T040 Close #3280, #3288, #3172 and #3170 after the merge (initiative branch: they do not auto-close); set this
  spec's **Status** to `Implemented` in the delivery PR

## Dependencies

- Phase 2 blocks every story. T013 builds on #3731.
- US1, US2 and US7 (P1) can run in parallel after Phase 2. US3 needs T006. US4 needs T008.
  US5 needs US3 and US4 (the policies it records must work). Phase 9 needs Phase 7.
- MVP: Phases 1–5.

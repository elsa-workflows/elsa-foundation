# Tasks: Null, missing and undefined

**Input**: Design documents from `specs/1201-null-missing-undefined/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/null-policies.md, quickstart.md

**Provisional**: do not start any task before this spec is approved (its ADR PRs agreed, see spec *Approval*) and
#3052's specs are defined. T001 is the gate.

**Tests**: required by framework §2.23 for every logic-bearing change, and by SC-004 to SC-006. The application
declares no TDD cadence, so tests follow implementation within each story; the PR's bite-proof shows each new test
red without the change.

**Build rule**: build and test only the projects a task touches (shared machine); CI runs the rest.

## Phase 1: Setup

- [ ] T001 Confirm #3388, #3390, #3408 and #3420 are agreed, diff their agreed text against spec.md, and amend spec,
  plan and these tasks for any change before starting
- [ ] T002 Align the proposed diagnostic codes in contracts/null-policies.md with the existing code catalogs
  (`VF-ACT-*` in `RuntimeInputBindingCompiler.cs`, `Elsa3ActivityDiagnosticCodes.cs`, Jint diagnostics) and fix the
  final names in the contract
- [ ] T003 Run the #3052 JTest measurement on this branch before any change and record the baseline in the PR

## Phase 2: Foundational (blocks every story)

- [ ] T004 Verify D-2 (variable nullability from the type alias) against spec 264's aliases in
  `Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutable.cs` (`WorkflowVariables`) and record the table in
  research.md R-5
- [ ] T005 [P] Add `NullPolicyDefaults.TryGetTypeDefault(alias)` in
  `Essentials/src/Workflows/Runtime/Core/Services/NullPolicyDefaults.cs`, with unit tests for value types, enums and
  types without a type default
- [ ] T006 Add `NullHandling` (`DefaultOnNull`, `UndefinedAsNull`) to
  `Essentials/src/Workflows/Design/Core/Models/WorkflowStrategyOptions.cs`, `ValuePolicies` to
  `Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutable.cs`, and compile one into the other in
  `WorkflowExecutableCompiler.cs` (data-model.md)
- [ ] T007 Add `IsNullable` to `WorkflowDeclaredInput` in `WorkflowExecutableInputContract.cs` and fill it in
  `WorkflowExecutableCompiler.BuildInputContract` from `InputDefinition.IsNullable` (R-4). Publication refuses an
  optional, non-nullable workflow input whose type has no non-null default and that declares no default
  (`VF-ACT-003`, FR-006), as it already does for activity inputs
- [ ] T008 Add `UndefinedPolicy` to `RuntimeExpressionBinding` in `RuntimeInputBinding.cs` and pin it on JavaScript
  bindings at publication from `ValuePolicies` (R-2)
- [ ] T009 Write `valuePolicies`, input `isNullable` and binding `undefinedPolicy` in
  `WorkflowExecutableHasher.WriteBehavioralPayload` only when set; add hash-stability tests proving unchanged hashes
  for workflows without them, and changed hashes when each is set (ADR 0038, US5 scenario 3)

## Phase 3: User Story 1 - Set a variable or output to null (P1) 🎯 MVP

**Independent test**: a `Set` writing literal `null` into a nullable variable publishes and runs.

- [ ] T010 [US1] Build the intrinsic `value` definition with the target's nullability in
  `ExecutableNodeCompiler.CompileIntrinsicNode` (R-5); keep `name` and `outcome` non-nullable
- [ ] T011 [US1] Tests in `Essentials/tests/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs`: null
  literal into a nullable variable and a nullable output publishes; into an `Int32` variable fails with `VF-ACT-004`
- [ ] T012 [US1] Run test: the variable holds `ExplicitNull` and a workflow output persists an explicit JSON `null`
  for every output storage kind (`WorkflowIntrinsicExecutor.NewWorkflowOutputChange` writes it only for Inline and
  Custom today; fix the others)

## Phase 4: User Story 2 - Start a workflow without an optional input (P1)

**Independent test**: `composition-bulk-dispatch-sub-workflow` starts with no input and completes.

- [ ] T013 [US2] Extend #3731's admission (R-3); the contract check on every start, declared defaults and the
  missing-required refusal are on `main` already. In `WorkflowExecutableInputValidator`'s complete-bag normalization:
  complete an omitted optional input without a declared default with JSON `null` when nullable, otherwise its type
  default (`NullPolicyDefaults`); refuse an explicit `null` for a non-nullable input with `VF-WF-REQ-002`; accept an
  explicit `null` for a nullable input without running the L119 conversion, and leave that conversion as it is (S-9
  owns it). In `WorkflowStartDispatcher.ApplyInputContract`, and in the early checks of
  `StartWorkflowTestRunRequestHandler` and `DispatchWorkflow`, substitute the type default for such a `null` before
  validating when the pinned `DefaultOnNull` is on
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
- [ ] T018 [US7] Fix any store that loses the difference; confirm `RuntimeInputBinding.LiteralValue` keeps
  `Absent` and `ExplicitNull` apart wherever it is read back

## Phase 6: User Story 3 - Null into a non-nullable input, by policy (P2)

**Independent test**: `return null;` into an `Int32` input gives `0` under the policy, `VF-ACT-004` without.

- [ ] T019 [US3] Publication: in `RuntimeInputBindingCompiler.ValidateNull`, pin the type default when the workflow's
  `DefaultOnNull` is on and the type has one (R-6)
- [ ] T020 [US3] Run time: in `RuntimeActivityInputMaterializer.ValidatePresence`, substitute the type default for
  `ExplicitNull` under the policy and record the substitution diagnostic on the invocation
- [ ] T021 [US3] Tests for each US3 acceptance scenario, including a non-nullable `String` that still fails, and
  update `ValueDurabilityPolicyTests.Snapshot_materialization_rejects_nullish_value_for_non_nullable_contract` to
  cover the policy off and on

## Phase 7: User Story 4 - `undefined` at the JavaScript boundary (P2)

**Independent test**: `({ a: undefined, b: NaN })` with and without the policy.

- [ ] T022 [US4] Install a path-tracking replacer in `JintResultMaterializer` beside `__elsaCanonicalJsonStringify`:
  dropped `undefined` and non-finite numbers record warnings; under the pinned `AsNull` policy `undefined` becomes
  `null` (R-7). Check that the byte, depth and node limits still count the output
- [ ] T023 [US4] Under `AsNull`, an `undefined` binding result materializes as `null` in
  `JintPortableJavaScriptEvaluator` for both binding forms; without it the ADR 0083 error stays
- [ ] T024 [US4] Script activity with a bound result (R-8): refuse a script without a `return` statement at publication,
  and fault the step in `JintJavaScriptScriptEvaluator` when the run produces `undefined`, both with a fix instruction;
  `ExplicitNull` under the policy; an unbound result writes nothing
- [ ] T025 [US4] Tests for each US4 acceptance scenario, including a `JSON.stringify` string that is not rewritten,
  `undefined` at depth three, and a script that returns on some paths only

## Phase 8: User Story 5 - Converted workflows carry their policies (P2)

**Independent test**: convert, export, import on a host without the converter, run: same outputs.

- [ ] T026 [US5] Add `Elsa3MappingOptions.ApplyElsa3NullPolicies` (default `true`) in
  `Extensions/Elsa3/src/Mapping/Elsa3MappingOptions.cs`, bind it in `Elsa3MappingFeature`, with a feature
  registration test (options binding carries logic)
- [ ] T027 [US5] Set `StrategyOptions.NullHandling` in `Elsa3WorkflowDefinitionToState.Map` when the option is on, and
  list the policies in the conversion report; say the stricter Elsa 4 rules apply when it is off
- [ ] T028 [US5] Remove `AddNullCoalescing` from `Elsa3ExpressionRewriter` (FR-023) and update the spec 262 tests
- [ ] T029 [US5] Narrow spec 267's `NullRefused` for SetVariable `Value` and SetOutput `OutputValue` in
  `Elsa3BuiltInActivityMap.Entries.cs`: emit a typed `Literal` null for a nullable target, or when `DefaultOnNull` is
  recorded; refuse otherwise; update `Elsa3NullMemberTests`
- [ ] T030 [US5] Verify Elsa 3 3.6.3 input requiredness (R-11) and map converted inputs' requiredness and nullability
  accordingly
- [ ] T031 [US5] Export/import round-trip test: `nullHandling` survives, and a native workflow with the same policies
  gives the same outputs (SC-004)

## Phase 9: User Story 6 - A computed variable name that matches nothing (P3)

**Independent test**: `getVariable('no' + 'pe') === undefined` and a warning is recorded.

- [ ] T032 [US6] In `IsolatedJintEngine`, always install `getVariable`; on a miss record the not-found warning and
  mark the read absent in the read set; JavaScript still sees `undefined` (R-10)
- [ ] T033 [US6] Record the same for a computed `variables[name]` miss through a proxy over the frozen snapshot
- [ ] T034 [US6] Tests for US6 scenarios 1 and 2; add the shared diagnostic code for Liquid without behaviour (R-10)

## Phase 10: Polish

- [ ] T035 [P] Docs: workflow settings authoring reference (`nullHandling`), glossary rows for *default-on-null* and
  *undefined-as-null*, Elsa 3 Mapping README and migration readiness guide (spec 262 and 267 sections), and the
  `IWorkflowExecutableInputValidator` and `IWorkflowStartDispatcher` *Usage* lines in the Runtime `EXTENSION_POINTS.md`
  (completion, `null` refusal, default-on-null at admission)
- [ ] T036 Rerun the #3052 JTest measurement and post it on #3052 (SC-002, SC-003)
- [ ] T038 [P] Q5 audit (R-12): list built-in activity inputs that cannot hold `null` and whose Elsa 3 counterparts
  accepted it; declare each nullable where the Elsa 4 activity handles `null`, with a test, and record the rest in the
  Elsa 3 migration readiness guide
- [ ] T037 Close #3280, #3288, #3172 and #3170 from the delivery PR; set this spec's **Status** to `Implemented` in
  that PR

## Dependencies

- T001 gates everything. Phase 2 blocks every story. T013 builds on #3731 (merged 2026-11-04, `4c002dadb`).
- US1, US2 and US7 (P1) can run in parallel after Phase 2. US3 needs T019/T020's policy from T006. US4 needs T008.
  US5 needs US3 and US4 (the policies it records must work). US6 is independent after Phase 2.
- MVP: Phases 1–5.

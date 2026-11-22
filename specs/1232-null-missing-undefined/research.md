# Research: Null, missing and undefined

**Spec**: [spec.md](spec.md) · **Date**: 2026-11-03, rechecked against #3731 and, on 2026-11-20, against the
initiative branch at `771ba6679f` (S-4, S-6, S-7 part 1, S-10, S-11 merged) and S-8's implementation (#4838, since
merged as `8bf168f3e2` with the same bridge code).
Line numbers are from that initiative head.

## R-1 What the code does today

| Area | Where | Today |
|---|---|---|
| Value states | `Essentials/src/Workflows/Runtime/Core/Models/ValueEnvelope.cs` (`ValuePresence`, L128–139) | `Absent`, `ExplicitNull`, `Present`, `Withheld`. `Presence` is hashed (runtime `WorkflowExecutableHasher.FormatEnvelope`, L175–190). |
| Intrinsic inputs | `ExecutableNodeCompiler.CompileIntrinsicNode` (L460; synthetic definition L515–528) | Every intrinsic input compiles against `InputDefinition(IsNullable: false, IsRequired: true)`, so a null literal on `Set` / `SetOutput` fails with `VF-ACT-004` (#3280). |
| Literal null check | `RuntimeInputBindingCompiler.ValidateNull` (L1186–1192) | `VF-ACT-004` when the input is not nullable. |
| Omitted activity inputs | `RuntimeInputBindingCompiler.CompileOmitted` (L344–380) | Optional non-nullable value types pin their CLR default; a non-nullable reference type throws `VF-ACT-003`; optional nullable pins an `Absent` literal. |
| Run-time presence | `RuntimeActivityInputMaterializer.ValidatePresence` (L676–682); `ActivityInputHydrator.Materialize` (L83–100) | `Absent` or `ExplicitNull` for a non-nullable contract fails with `VF-ACT-004` (#3172). The hydrator repeats the guard. |
| Intrinsic execution | `WorkflowIntrinsicExecutor` (L663–665, L679–680) | An expression null or `undefined` becomes `ExplicitNull`; an `Absent` source fails. A null literal never reaches it. S-7 did not change it. |
| Workflow inputs | `WorkflowDeclaredInput` (`WorkflowExecutableInputContract.cs` L51–57); `WorkflowExecutableCompiler.BuildInputContract` (L598–612) | `Name`, `Type`, `IsRequired`, `DefaultValue`. **No nullability**: the compiler drops `InputDefinition.IsNullable`. Studio already authors `isNullable` on workflow inputs but shows no column for it. |
| Admission | `WorkflowStartDispatcher.ApplyInputContract` (L465–481, called at L212); `WorkflowExecutableInputValidator` (L80–89 required, L95–99 defaults, L119 type check); `RuntimeWorkflowStateSeed.BuildSeedChanges` (L63–77) | Since #3731 every new start runs the validator in complete-bag mode before enqueue. An optional input with no declared default is still left out of the bag, so it gets no envelope and the first read fails (#3288). An explicit `null` passes for a `String` input and fails as `incompatible-value` for an `Int32` one: the outcome comes from the L119 conversion, not from a declared nullability. Reason codes: `WorkflowExecutableInputValidation.cs` L67–75 (`runtime.input.*`). |
| JavaScript results (initiative head) | `JintResultMaterializer.Materialize` (L32–73) | The intrinsic `JSON.stringify`: `undefined` properties dropped, `undefined` array elements and holes become `null`, `NaN` / `Infinity` become `null`, all without a diagnostic (#3170). |
| JavaScript results (S-8, #4838, merged) | `CopyComparePatchValueBridge.cs`; `JintResultMaterializer` split into `Stringify(engine, value, replacer?)` and `ParseResult` | Every value leaving a script (binding result, script result, variable change) goes through the bridge's writer with a marker replacer. A top-level `undefined` becomes `ScriptValueWrite(null)`. Only an `Infinity` that came from an entered too-large number is tracked, as a precision loss (`VF-PREC-001`); computed `NaN` / `Infinity`, `undefined` properties and holes stay silent. Warnings go through `IExecutionWarningSink` (`ExecutionWarning`). |
| `undefined` result | `JintPortableJavaScriptEvaluator` L71–72 (S-8: after the write check); `JintJavaScriptScriptEvaluator` L79–80 (S-8: `Write` L123–124) | Bindings throw (ADR 0083). `Run JavaScript` returns no value (C# `null` / `ScriptValueWrite(null)`). |
| Script forms | `PreparedJavaScript.cs` L21–29 (bindings), L46 (`Run JavaScript`) | Bindings are programs: an expression, or a statement body whose value is its completion value or a top-level `return`. `Run JavaScript` is a function body (`"use strict"; (() => { source })()`): its result is only what `return` gives. It is the only built-in script activity. ADR 0083 keeps this "function-body contract". |
| Spec 262 lowering | `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs` (`AddNullCoalescing`, L481–484; called at L142, 153, 214, 221) | The converter wraps each top-level expression result as `(...) ?? null`. |
| Capability profile | `RuntimeExpressionBinding.CapabilityProfile` (`RuntimeInputBinding.cs` L336–337), hashed in `FormatExpression` (L202–221) | Pinned per binding. S-1 has since delivered `binding-readonly-v1`; S-2 reuses it. |
| Workflow settings | `WorkflowStrategyOptions` (`ActivationStrategyType`, `IncidentStrategy`, `PersistenceMode`) → `WorkflowExecutableCompiler` L301–310 → hashed in `WriteBehavioralPayload` (L279–368) | The place for workflow-level, hashed settings. No null-handling setting exists. Studio shows the strategy settings in `WorkflowPropertiesView.tsx` (`IncidentStrategySection`, `PersistenceModeSection`). |
| Converter | `Elsa3MappingFeature` (L25); `Elsa3WorkflowDefinitionToState.Map` (L30) | No options class. `StrategyOptions: null`, so nothing workflow-level is recorded. |
| Spec 267 refusals | `Elsa3BuiltInActivityMap.Entries.cs` L104, L113 (`NullRefused`) | SetVariable `Value` and SetOutput `OutputValue` refuse null with `ELS3-ACTIVITY-NULL-UNCONVERTIBLE`. |
| Computed reads | S-4: `IsolatedJintEngine` L94–190, `RuntimeVariableReadFallback` L77–83, `RuntimeVariableReadRecorder` L45, L119 (`VF-READ-001`); Liquid `LiquidVariablesValue` L42–59 | Delivered: a missing computed name reads `undefined` (JavaScript) or nil (Liquid), records `VF-READ-001` and marks the read absent. |
| Variable nullability | `VariableDefinition` (`Expressions/Core/Models/VariableDefinition.cs` L5–10) | Type only, no `IsNullable`. A variable default compiles with a synthetic `IsNullable: true` (`ExecutableNodeCompiler.cs` L1103–1110). |
| Diagnostic codes | `VF-ACT-003/004` inline in the compiler and materializer; `VF-READ-*` constants; S-8's `JavaScriptValueDiagnostics` (`VF-PREC-001/002`, `VF-BIND-001`, `VF-WRITE-001`) | Pattern `VF-<AREA>-NNN`. No code exists for `undefined` or non-finite values. |

## R-2 Where the two policies live

- **Decision**: a new `NullHandling` block on `WorkflowStrategyOptions` (`DefaultOnNull`, `UndefinedAsNull`, both
  `false` by default), compiled to a `WorkflowValuePolicies` record on `WorkflowExecutable` and written in
  `WriteBehavioralPayload` only when a policy is on. The evaluator gets the undefined policy from the executable with
  each evaluation; no per-binding copy is pinned.
- **Rationale**: it follows how `IncidentStrategy` and `PersistenceMode` are authored, pinned and hashed. Writing the
  block only when set leaves the hashes of workflows without a policy as they are, so no stored fixture needs
  regenerating for them; compatibility is not owed before the first stable release, so this is a convenience, not a
  constraint. The input materializer holds the executable and `Run JavaScript` gets the policies on its execution
  context (T008), so a per-binding copy would only
  have to agree with the workflow-level value.
- **Alternatives**: workflow metadata (not hashed, rejected by FR-021); per-binding only (no workflow-level record,
  rejected by D-11); a per-binding copy beside the capability profile (rejected: a second hashed copy of one setting);
  a new capability profile name (rejected: the policy acts after the source runs, not on what the source may do,
  ADR 0083 amendment).

## R-3 Admission that always completes the request

- **What #3731 already does**: `WorkflowStartDispatcher.ApplyInputContract` is the one admission step. Every start
  reaches it: the runtime API, the stimulus router (triggers, live and saved), BPMN, test runs
  (`TestRunTargetDispatcher`) and DispatchWorkflow children (retained dispatch). It runs after the keyed and retained
  duplicate answers and before the start policy and enqueue, so before the start checkpoint. It fills declared
  defaults, refuses a missing required input, and normalizes the bag the start command carries. The early checks in
  `StartWorkflowTestRunRequestHandler` and `DispatchWorkflow` stay, because they refuse before their own side effects.
  BPMN declares no workflow inputs, so nothing changes for it.
- **Decision**: S-2 extends that step and adds no new one (no `WorkflowRequestAdmission`, no change to
  `WorkflowStartSchedulerWorkHandler`):
  1. check requiredness first in complete-bag mode, then fill declared defaults, preserving the distinction between
     no default and an explicit-null default. The validator's complete-bag normalization also completes an omitted optional input without a declared default:
     JSON `null` when the input is nullable (R-4), otherwise the type default from `NullPolicyDefaults` (R-6). A member
     with none of these was refused at publication (FR-006, T007). Known-inputs mode (Design-time `DispatchWorkflow`
     and `DispatchPinSource`) fills declared defaults as today but adds no `null` or type-default completion, and
     applies steps 2 and 3 to supplied values and the declared defaults it materializes;
  2. the validator refuses an explicit `null` for a non-nullable input with a new reason code,
     `runtime.input.null-not-allowed` (R-4). An explicit
     `null` for a nullable input is accepted without the L119 conversion, which a `null` for a nullable value type
     would otherwise fail. The conversion itself is unchanged (S-9 moves it);
  3. after default completion and before the null/type checks in step 2, the validator replaces null for a
     non-nullable input that has a supported type default when pinned `DefaultOnNull` is on (R-6). This includes null
     introduced by a declared workflow-input default. `ApplyInputContract`, the early checks in the test-run handler
     and `DispatchWorkflow`, and Design's `DispatchPinSource` pass the executable's `WorkflowValuePolicies` to new
     policy-aware validator overloads, so both modes use one normalization. The old two-argument overloads remain
     policies-off; the new required interface overloads are tracked as a Major API shape change, not an untracked
     signature replacement. A variable's authored-null-default refusal remains distinct (T010d).
  The seed already writes every member of the normalized bag; T014 checks that a completed `null` is stored as an
  explicit null.
- **Rationale**: #3731 put the contract on every start path. Completion and the null refusal belong in the same
  normalization that already fills declared defaults, so one place decides what a declared workflow input holds.
- **Alternatives**: a separate admission step in the start work handler (rejected: a second place deciding inputs,
  and it would run after enqueue); materialize on first read (rejected by ADR 0045 §2: admission, not a later step).

## R-4 Workflow input nullability

- **Decision**: add `IsNullable` to `WorkflowDeclaredInput`, fill it in `BuildInputContract` from the input's type
  (R-5, Q8; the stored `InputDefinition.IsNullable` flag is not read for workflow inputs), and hash it in
  `inputContract` only when the input is non-nullable. The validator refuses an explicit JSON `null` for a non-nullable input unless default-on-null
  applies (R-3 step 2 and 3).
- **Rationale**: FR-005, FR-001a. Studio stores `isNullable: false` on every new workflow input and output, so
  trusting the flag would refuse `null` for a `String` input; the type gives the same answer .NET would.
- **Alternatives**: trust the stored `isNullable` flag (rejected: Studio stores `false` for every new member).

## R-5 Explicit null on intrinsics

- **Decision**: `CompileIntrinsicNode` builds the synthetic `value` definition with `IsNullable` from the target
  (variable type, or the workflow output's declared type), and keeps `IsRequired: true`. Literal keys (`name`,
  `outcome`) stay non-nullable. `WorkflowIntrinsicExecutor` already stores `ExplicitNull`.
- **Rationale**: FR-001, FR-002. Required stays: the input must be bound, and may be bound to `null`.
- **Decided (Q8)**: variables, workflow inputs and workflow outputs take their nullability from their type, as in
  .NET, by the type registry's `supportsNull`: a value type (`Int32`, `Boolean`, `DateTime`, `TimeSpan`, `Guid`, …)
  is not nullable, every other type is nullable. Only `Boolean`, the number types and enums have a type default
  (Q10, FR-001b). This matches how `Any` / `String` variables already hold
  `null` from expressions. For the JSON kinds the rule goes by kind, not CLR type: `Any` and
  every JSON alias (`JsonElement`, `JsonNode` and their full names, which S-8 FR-021 and #4832 treat as one
  structured kind) are nullable, although `JsonElement` is a CLR struct; `Object` reads as `Any` after S-8. Checked in
  T004 against the alias registry.

## R-6 Default-on-null in the runtime

- **Decision**: one public shared domain helper, `NullPolicyDefaults`, in `Elsa.Workflows.Runtime.Core`, tracked in
  `PublicAPI.Unshipped.txt` because Runtime, Publishing and Elsa3 consume the same rule, looked up by alias for authored
  members and by CLR type for .NET activity inputs (the registry has no enums and only four number types), returning
  the type default for `Boolean` (`false`), the number types (`0`) and enums (zero value) and nothing for other types
  (dates, times, `TimeSpan`, `Guid`, `String`, `Any` and the JSON aliases, Q10). It is applied in the places listed under *Every write path*, each only
  when the workflow's pinned `DefaultOnNull` is on: publication (`ValidateNull`: keep the literal `null` instead of
  failing, so the run-time step substitutes and records it), run-time materialization (`ValidatePresence`: replace `ExplicitNull` with the default and record a
  `VF-NULL-001` diagnostic on the invocation), the intrinsic executor (`WorkflowIntrinsicExecutor`: a `Set` /
  `SetOutput` expression that evaluates to `null` becomes `ExplicitNull` without passing the materializer, and
  `ValidateAssignmentPayload` skips anything not `Present`, so today `null` into an `Int32` variable is written; S-2
  refuses it with `VF-ACT-004` or substitutes under the policy), and admission
  (the input validator normalization called by `WorkflowStartDispatcher.ApplyInputContract`, R-3). The hydrator's CLR guard stays as a
  backstop; it never sees a null for such an input once the materializer substituted.
- **Every write path.** The same check (refuse with `VF-ACT-004`, or substitute and record `VF-NULL-001` under
  `DefaultOnNull`) runs wherever a value is written to a declared member: admission (T013), activity inputs (T019,
  T020), graph inner nodes (T020a), `Set` / `SetOutput` (T010a), step writes from scripts and .NET activities
  (T010b), and activity output captures into variables and workflow outputs (T010c). Two more refuse `null` for a
  non-nullable variable without substituting, because an author or operator chose the value directly: a variable's
  declared default (T010d) and the `ModifyVariable` alteration (T010e).
- **Rationale**: one rule at every entry point; the hydrator stays a pure contract check.
- **Alternatives**: substitute in the hydrator (rejected: the invocation record would not show the substitution).

## R-7 Boundary diagnostics and undefined-as-null in S-8's value bridge

- **Decision**: build in the writer of S-8's script value bridge (`CopyComparePatchValueBridge`), the one place every
  value leaves a script: a binding result, a `Run JavaScript` result and each variable change. On #4838 the replacer
  only marks an `Infinity` from an entered number and unchanged entered objects; paths are built in C# (`WriteMarked`)
  after stringify, when `undefined` members are already gone; and a value with no entered object skips the replacer
  (`Mark`), so a freshly computed `({ discount: undefined })` never reaches it. S-2 therefore makes the replacer run on
  every written object and adds `undefined` and `NaN` markers, which `WriteMarked` turns into the diagnostics below
  with their paths. The extra cost shows in S-8's per-evaluation bridge metric (S-8 FR-010):
  - an `undefined` object property: dropped with `VF-UNDEF-002` (default), or kept as `null` (policy);
  - an `undefined` array element or sparse hole: a `null` placeholder with `VF-UNDEF-003`, under either policy;
  - a computed `NaN` or `±Infinity`: `null` with `VF-NUM-001`. An `Infinity` that came from an entered number stays
    S-8's precision loss (`VF-PREC-001`), checked first;
  - a top-level `undefined`: the caller's rule (R-8, FR-015, FR-030).
  Warnings go through S-8's `IExecutionWarningSink`, so Studio shows them with the instance's diagnostics.
- **Rationale**: one pass per written value, no change to JavaScript semantics; a string the script itself produced with
  `JSON.stringify` is a string value and is never re-parsed (FR-018). Building in the bridge, not in
  `JintResultMaterializer`, follows S-8: the materializer is only the bridge's stringify step after #4838.
- **Note**: a JavaScript `Array` hole is not an own property, so the
  writer checks holes by index, not through the replacer's key list (check in T022).
- **Check in T022**: the replacer's cost on the byte, depth and node limits; the limits must count the output, not the
  replacer's calls.

## R-8 A consumed result without a value

- **Decision** (Q3): the forms follow ADR 0083.
  - Input bindings are programs: an expression, or a statement body whose value is its completion value or a
    top-level `return`. They need no `return` and are never refused for lacking one. An `undefined` value follows
    FR-015 and FR-018 (an error by default, `null` under the policy).
  - `Run JavaScript` is a function body (`PreparedJavaScript` L46). It is the only built-in script activity. When its
    result is bound and the body has no `return` statement anywhere, publication refuses it with `VF-UNDEF-001`, using
    the JavaScript parser the publish analysis already runs. A body that returns on some paths only passes.
  - At run time, a bound `Run JavaScript` result that is `undefined` faults the step with `VF-UNDEF-001`, or is
    `ExplicitNull` under the policy. An unbound result writes nothing.
  - A script that assigns `undefined` to a whole variable (FR-030, Q7) faults the step with `VF-UNDEF-001` naming the
    variable, or writes `ExplicitNull` under the policy. Script writes go through S-7's `StepVariableSession.Write`;
    the bridge and the session both carry "no value" distinct from JSON `null`, and the session decides. A `null`
    written there is checked against the variable's nullability like a `Set` (T010b).
- **Rationale**: a bound result that is never produced is a bug in the script; this matches ADR 0083's rule for
  bindings. Elsa 3's `Result = null` stays available through the policy.
- **Alternatives**: an absent result that the binding skips (rejected: it silently keeps the old value); always
  `null` (rejected: hides the missing `return`).

## R-9 Converter option

- **Decision**: add `Elsa3MappingOptions` with one property, `ApplyElsa3NullPolicies` (default `true`, Q4), bound
  through the `Elsa3Mapping` shell feature's configuration section. When on, `Elsa3WorkflowDefinitionToState.Map`
  sets `StrategyOptions.NullHandling` to both policies, and the conversion report says so. Remove
  `AddNullCoalescing` from `Elsa3ExpressionRewriter`. Change spec 267's `NullRefused` for SetVariable / SetOutput to
  emit a typed `Literal` null when the target is nullable, or when `DefaultOnNull` is recorded and the target type
  has a type default, and keep the refusal otherwise (T030).
- **Rationale**: FR-022, FR-023, FR-004; ADR 0045 §2 ("one option").
- **Check**: the shell feature options pattern (framework §2.24.2 sanctioned patterns): an options class bound by the
  feature is in the catalog.

## R-10 Computed reads that match nothing

- **Delivered by S-4** (spec 1222 FR-011, FR-012; #4762): `getVariable(expr)` and `variables[expr]` read `undefined`
  on a miss, Liquid reads nil, `VF-READ-001` is recorded and the read is marked absent. S-2 adds no work; its FR-025 to
  FR-027 state the rule.

## R-12 Inputs that cannot hold null (Q5)

- **Decision**: `null` for a type without a non-null default stays `VF-ACT-004` under any policy. T038 audits the
  built-in activities whose Elsa 3 counterparts accepted `null` for such inputs, and declares an input nullable where
  the Elsa 4 activity handles `null`.
- **Rationale**: faithful or refuse; the activity contract is fixed at its source instead of being worked around.

## R-11 Elsa 3 input requiredness

- **Decision**: to verify in T031 against Elsa 3 3.6.3 `InputDefinition`. If Elsa 3 has no enforced required flag, the
  converter maps every input as optional. Nullability is not mapped: it follows the converted type (Q8), so an
  omitted `Any` input reads `null` and an omitted `Boolean` input reads `false`.

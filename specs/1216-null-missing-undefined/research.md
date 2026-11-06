# Research: Null, missing and undefined

**Spec**: [spec.md](spec.md) · **Date**: 2026-11-03, rechecked 2026-11-04 against #3731 · Line numbers are from
`origin/main` at `4c002dadb`.

## R-1 What the code does today

| Area | Where | Today |
|---|---|---|
| Value states | `Essentials/src/Workflows/Runtime/Core/Models/ValueEnvelope.cs` (`ValuePresence`, L128–139) | `Absent`, `ExplicitNull`, `Present`, `Withheld`. `Presence` is serialized and hashed (`WorkflowExecutableHasher.FormatEnvelope`, L162). |
| Intrinsic inputs | `ExecutableNodeCompiler.CompileIntrinsicNode` (L321–435) | Every intrinsic input compiles against a synthetic `InputDefinition(IsNullable: false, IsRequired: true)` (L375–389), so a null literal on `Set` / `SetOutput` fails with `VF-ACT-004` (#3280). |
| Literal null check | `RuntimeInputBindingCompiler.ValidateNull` (L998–1005) | `VF-ACT-004` when the input is not nullable. |
| Omitted activity inputs | `RuntimeInputBindingCompiler.CompileOmitted` (L319–355) | Optional non-nullable value types pin their CLR default; a non-nullable reference type throws `VF-ACT-003`; optional nullable pins an `Absent` literal. |
| Run-time presence | `RuntimeActivityInputMaterializer.ValidatePresence` (L605–612); `ActivityInputHydrator.Materialize` (L83–115) | `Absent` or `ExplicitNull` for a non-nullable contract fails with `VF-ACT-004` (#3172). The hydrator repeats the guard from CLR nullability. |
| Intrinsic execution | `WorkflowIntrinsicExecutor` (L640–642, L656–657) | An expression null or `undefined` becomes `ExplicitNull`; an `Absent` source fails. A null literal never reaches it. |
| Workflow inputs | `WorkflowDeclaredInput` (`WorkflowExecutableInputContract.cs` L51–84); `WorkflowExecutableCompiler.BuildInputContract` (L511–528) | `Name`, `Type`, `IsRequired`, `DefaultValue`. **No nullability**: the compiler drops `InputDefinition.IsNullable`. |
| Admission | `WorkflowStartDispatcher.ApplyInputContract` (L465–481, called at L212 before the start policy at L213); `WorkflowExecutableInputValidator` (L80–99 required and defaults, L119 type check); `RuntimeWorkflowStateSeed.BuildSeedChanges` (L63–77) | Since #3731 every new start runs the validator in complete-bag mode before enqueue: declared defaults are appended, and a blank, unknown, duplicate or mistyped input or a missing required one refuses the start (`WorkflowExecutableStartRejectedException`, category `Input`, `400`; the stimulus router reports `Refused`). A legacy artifact without a contract keeps its supplied inputs. An optional input with no declared default is still left out of the bag, so it gets no envelope and the first read fails (`RuntimePortableExpressionEvaluator` L388–389, `RuntimeInputBindingResolver` L57–63) (#3288). An explicit `null` passes for a `String` input and fails as `incompatible-value` for an `Int32` one: the outcome comes from the L119 conversion, not from a declared nullability. |
| JavaScript results | `JintResultMaterializer.Materialize` (L32–73) | The intrinsic `JSON.stringify`: `undefined` properties dropped silently, `NaN` / `Infinity` → `null` silently (#3170). |
| `undefined` result | `JintPortableJavaScriptEvaluator` L71–72; `JintJavaScriptScriptEvaluator` L21–22 | Bindings throw (ADR 0083). Script activities return C# `null`. |
| Spec 262 lowering | `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs` (`AddNullCoalescing`, L333–336) | The converter wraps each top-level expression result as `(...) ?? null`. |
| Capability profile | `RuntimeExpressionBinding.CapabilityProfile` (`RuntimeInputBinding.cs` L254–284), hashed in `FormatExpression` (L188) | Pinned per binding, not per workflow. |
| Workflow settings | `WorkflowStrategyOptions` (`Essentials/src/Workflows/Design/Core/Models/WorkflowStrategyOptions.cs`) → `WorkflowExecutable` (`IncidentStrategy`, `PersistenceMode`), hashed in `WriteBehavioralPayload` (L246–335) | The place for workflow-level, hashed settings. |
| Converter | `Elsa3MappingFeature` (`Extensions/Elsa3/src/Mapping/Elsa3MappingFeature.cs`); `Elsa3WorkflowDefinitionToState.Map` (L25–31) | No options class. `StrategyOptions: null`, so nothing workflow-level is recorded. |
| Spec 267 refusals | `Elsa3BuiltInActivityMap.Entries.cs` L101–116 (`NullRefused`) | SetVariable `Value` and SetOutput `OutputValue` refuse null with `ELS3-ACTIVITY-NULL-UNCONVERTIBLE`. |
| Computed reads | `IsolatedJintEngine` L98–138 | `getVariable(name)` returns `undefined` for an unknown name, silently. With an empty snapshot `getVariable` is not installed at all (ReferenceError). |
| Liquid | `PortableLiquidExpressionHandler` L42–43 | An undeclared root parameter throws; a JSON null renders as nil. |

## R-2 Where the two policies live

- **Decision**: a new `NullHandling` block on `WorkflowStrategyOptions` (`DefaultOnNull`, `UndefinedAsNull`, both
  `false` by default), compiled to a `WorkflowValuePolicies` record on `WorkflowExecutable` and written in
  `WriteBehavioralPayload` only when a policy is on. Publication also pins the effective undefined policy on each
  JavaScript `RuntimeExpressionBinding`, next to the capability profile, so the evaluator never needs the workflow.
- **Rationale**: it follows how `IncidentStrategy` and `PersistenceMode` are authored, pinned and hashed. Writing the
  block only when set keeps every existing artifact hash unchanged (ADR 0038). Per-binding pinning follows the
  capability profile.
- **Alternatives**: workflow metadata (not hashed, rejected by FR-021); per-binding only (no workflow-level record,
  rejected by D-11); a new capability profile name (rejected: the policy acts after the source runs, not on what the
  source may do, ADR 0083 amendment).

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
  1. the validator's complete-bag normalization also completes an omitted optional input without a declared default:
     JSON `null` when the input is nullable (R-4), otherwise the type default from `NullPolicyDefaults` (R-6). A member
     with none of these was refused at publication (FR-006, T007). Known-inputs mode (Design-time DispatchWorkflow)
     stays as it is;
  2. the validator refuses an explicit `null` for a non-nullable input with a new reason code (R-4). An explicit
     `null` for a nullable input is accepted without the L119 conversion, which a `null` for a nullable value type
     would otherwise fail. The conversion itself is unchanged (S-9 moves it);
  3. under default-on-null, `ApplyInputContract` replaces an explicit `null` for a non-nullable input that has a type
     default before it calls the validator, from the executable's pinned `ValuePolicies` (R-6). The early checks in
     the test-run handler and `DispatchWorkflow` apply the same `NullPolicyDefaults` substitution before validating,
     so they give the same answer.
  The seed already writes every member of the normalized bag; T014 checks that a completed `null` is stored as an
  explicit null.
- **Rationale**: #3731 put the contract on every start path. Completion and the null refusal belong in the same
  normalization that already fills declared defaults, so one place decides what a declared workflow input holds.
- **Alternatives**: a separate admission step in the start work handler (rejected: a second place deciding inputs,
  and it would run after enqueue); materialize on first read (rejected by ADR 0045 §2: admission, not a later step).

## R-4 Workflow input nullability

- **Decision**: add `IsNullable` to `WorkflowDeclaredInput`, fill it from `InputDefinition.IsNullable` in
  `BuildInputContract`, and hash it in `inputContract` only when `true`, so existing hashes are unchanged for inputs
  that stay non-nullable. The validator refuses an explicit JSON `null` for a non-nullable input unless default-on-null
  applies (R-3 step 2 and 3).
- **Rationale**: FR-005; the authored model already has the flag.
- **Alternatives**: infer nullability from the type alias (rejected: it would change meaning silently).

## R-5 Explicit null on intrinsics

- **Decision**: `CompileIntrinsicNode` builds the synthetic `value` definition with `IsNullable` from the target
  (variable type, or the workflow output's declared type), and keeps `IsRequired: true`. Literal keys (`name`,
  `outcome`) stay non-nullable. `WorkflowIntrinsicExecutor` already stores `ExplicitNull`.
- **Rationale**: FR-001, FR-002. Required stays: the input must be bound, and may be bound to `null`.
- **Open**: how a variable's nullability is declared. Variables carry a type alias and no nullability flag today. The
  proposal: a variable is nullable unless its type is a non-nullable value type (`Int32`, `Boolean`, …), which matches
  how `Object` / `Any` / `String` variables already hold `null` from expressions. Checked in T004 against spec 264's
  type aliases.

## R-6 Default-on-null in the runtime

- **Decision**: one helper, `NullPolicyDefaults.TryGetTypeDefault(alias)`, in `Elsa.Workflows.Runtime.Core`, returning
  the type default for value types and enums and nothing for other types. It is applied in three places, each only
  when the workflow's pinned `DefaultOnNull` is on: publication (`ValidateNull`: pin the default literal instead of
  failing), run-time materialization (`ValidatePresence`: replace `ExplicitNull` with the default and record a
  `substituted-default` diagnostic on the invocation), and admission (`WorkflowStartDispatcher.ApplyInputContract`,
  R-3). The hydrator's CLR guard stays as a
  backstop; it never sees a null for such an input once the materializer substituted.
- **Rationale**: one rule, three entry points; the hydrator stays a pure contract check.
- **Alternatives**: substitute in the hydrator (rejected: the invocation record would not show the substitution).

## R-7 Boundary diagnostics and undefined-as-null in Jint

- **Decision**: `JintResultMaterializer` passes a replacer to the captured intrinsic `JSON.stringify`. The replacer
  tracks the property path, and for each `undefined` property either records a `js-undefined-dropped` warning and
  returns `undefined` (default), or returns `null` (policy). For `NaN` and `±Infinity` it records `js-nonfinite-null`
  and returns `null`. A top-level `undefined` under the policy materializes as `null` before stringify. The replacer is
  installed the same non-writable way as `__elsaCanonicalJsonStringify`.
- **Rationale**: one pass, no change to JavaScript semantics; a string the script itself produced with
  `JSON.stringify` is a string value and is never re-parsed (FR-018).
- **Check in T022**: the replacer's cost on the byte, depth and node limits; the limits must count the output, not the
  replacer's calls.

## R-8 Script activity without a return value

- **Decision** (Q3): when the script activity's result is bound, publication refuses a script with no `return`
  statement (the JavaScript analyzer already parses the body), and `JintJavaScriptScriptEvaluator` faults the step when
  the run produces `undefined`. Both carry a fix instruction. Under undefined-as-null the result is `ExplicitNull`.
  When the result is not bound, `undefined` produces no value and nothing is written.
- **Rationale**: a bound result that is never produced is a bug in the script; this matches ADR 0083's rule for
  bindings. Elsa 3's `Result = null` stays available through the policy.
- **Alternatives**: an absent result that the binding skips (rejected in review: it silently keeps the old value);
  always `null` (rejected: hides the missing `return`).

## R-9 Converter option

- **Decision**: add `Elsa3MappingOptions` with one property, `ApplyElsa3NullPolicies` (default `true`, Q4), bound
  through the `Elsa3Mapping` shell feature's configuration section. When on, `Elsa3WorkflowDefinitionToState.Map`
  sets `StrategyOptions.NullHandling` to both policies, and the conversion report says so. Remove
  `AddNullCoalescing` from `Elsa3ExpressionRewriter`. Change spec 267's `NullRefused` for SetVariable / SetOutput to
  emit a typed `Literal` null when the target is nullable, and keep the refusal otherwise.
- **Rationale**: FR-022, FR-023, FR-004; ADR 0045 §2 ("one option").
- **Check**: the shell feature options pattern (framework §2.24.2 sanctioned patterns): an options class bound by the
  feature is in the catalog.

## R-10 Computed reads that match nothing

- **Decision**: `IsolatedJintEngine` always installs `getVariable`, and on a miss records an `variable-not-found`
  warning on the evaluation and adds the name to the read set as absent; JavaScript still sees `undefined`. A computed
  `variables[name]` miss on the frozen object is recorded the same way by wrapping it in a proxy over the snapshot.
  Liquid: research found no computed variable access in the portable Liquid handler (an undeclared root throws); FR-026
  applies once S-4 adds `variables` to Liquid, and S-2 adds only the shared diagnostic code.
- **Rationale**: row 10 (#3420 question 3). S-4 replaces the accessor and keeps the outcome.

## R-12 Inputs that cannot hold null (Q5)

- **Decision**: `null` for a type without a non-null default stays `VF-ACT-004` under any policy. T038 audits the
  built-in activities whose Elsa 3 counterparts accepted `null` for such inputs, and declares an input nullable where
  the Elsa 4 activity handles `null`.
- **Rationale**: faithful or refuse; the activity contract is fixed at its source instead of being worked around.

## R-11 Elsa 3 input requiredness

- **Decision**: to verify in T030 against Elsa 3 3.6.3 `InputDefinition`. If Elsa 3 has no enforced required flag, the
  converter maps every input as optional and nullable, which makes #3288 read `null`, as Elsa 3 did.

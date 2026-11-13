# Implementation Plan: Null, missing and undefined

**Branch**: `agent/claude/3052-s2-null-missing-undefined` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

**Status**: provisional, like the spec. The ADR PRs it waits on are listed in the spec's *Approval* section. Nothing
here is implemented until #3052's specs are defined and this spec is approved.

## Summary

Make explicit null a value, omission a defined outcome and `undefined` a visible policy, as ADR 0045 §2 (D-10, D-11)
says. The technical approach (research R-2 to R-10):
- intrinsics take their target's nullability;
- #3731's admission step (`WorkflowStartDispatcher.ApplyInputContract`, on every start) also completes optional
  inputs without a declared default and refuses `null` for non-nullable inputs;
- workflow inputs carry nullability in the pinned input contract;
- two workflow-level policies, `DefaultOnNull` and `UndefinedAsNull`, are authored on `WorkflowStrategyOptions`,
  pinned and hashed on `WorkflowExecutable`, and the undefined policy is also pinned per JavaScript binding;
- the Jint materializer reports or converts `undefined`, `NaN` and `Infinity` through one replacer;
- the Elsa 3 converter gets one option that records both policies, and loses spec 262's `?? null` lowering and
  spec 267's null refusals for nullable targets;
- a computed variable read that matches nothing stays `undefined` and records a warning.

## Technical Context

**Language/Version**: C# / .NET (repository `global.json`).

**Primary Dependencies**: Jint (JavaScript), Fluid (Liquid), System.Text.Json, CShells features.

**Storage**: `ValueEnvelope` in every runtime store (EF Core providers, in-memory). No schema change: `Presence` is
already stored.

**Testing**: xUnit (no FluentAssertions), behavioural drives in `Essentials/tests/Activities/Behavioral`, the JTest
measurement of #3052.

**Target Platform**: Elsa 4 server hosts.

**Project Type**: libraries (workflow publishing, runtime, expressions) and the Elsa 3 extension.

**Performance Goals**: the Jint replacer adds no more than one pass over the result; no extra store round trip at
admission.

**Constraints**: artifact hashes of workflows that select no policy and declare no nullable input stay unchanged
(ADR 0038). Elsa 3 knowledge stays in `Extensions/Elsa3` (§E2.7).

**Scale/Scope**: about 15 source files in Essentials, 5 in `Extensions/Elsa3`, and their tests.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.2 Runtime takes no Design reference | PASS | Policies are compiled from `WorkflowStrategyOptions` in Publishing, which already references both sides. Runtime reads only `WorkflowExecutable`. |
| §E2.1 domain boundaries | PASS | Value rules stay in `Workflows.Runtime.Core` (`NullPolicyDefaults`); Jint changes stay in `Expressions/JavaScript/Jint`. |
| §E2.7 Elsa 3 stays behind the import boundary | PASS | Elsa 4 gains general policies, usable by any workflow. The converter only selects them (D-11: no "converted from Elsa 3" marker). |
| §E5 versioning / §2.23.3 visibility | PASS | Additive public surface: `WorkflowStrategyOptions.NullHandling`, `WorkflowExecutable.ValuePolicies`, `WorkflowDeclaredInput.IsNullable`, `Elsa3MappingOptions`. Removal: `Elsa3ExpressionRewriter.AddNullCoalescing` (internal behaviour, pre-GA, ADR 0080). New services internal. |
| §2.6.2 replacement contracts | PASS | None added. `IWorkflowExecutableInputValidator`'s signature is unchanged; its complete-bag normalization also completes omitted optional inputs and refuses `null` for non-nullable ones (R-3). The catalog entry's *Usage* line says so (T035). |
| §2.17 duplication beats dependency | PASS | The type-default table is one helper in `Runtime.Core`, which every caller already references. |
| §2.24.2 sanctioned patterns | PASS | Feature options bound from configuration; hashed strategy options. No new extension point. |
| §2.22 / §2.22.1 docs and extension points | ACTION | Spec 095 contracts (already amended by #3388), the authoring reference for workflow settings, the Elsa 3 Mapping README and migration readiness guide, glossary rows for *default-on-null* and *undefined-as-null*, and the *Usage* line of `IWorkflowExecutableInputValidator` and `IWorkflowStartDispatcher` in the Runtime extension-point catalog. No contributor or replacement contract is added. |
| §2.23.1–§2.23.2 tests | PASS | A test per acceptance scenario; a registration test for `Elsa3MappingOptions`; hash-stability tests. |
| §2.23.5 exception boundaries | PASS | Publication and admission refusals keep their diagnostic exceptions; boundary warnings are diagnostics, not exceptions. |
| ADR 0038 content-addressed executables | PASS | Policies and input nullability are hashed, written only when set (R-2, R-4). |
| ADR 0045 §2 (#3388), ADR 0083 (#3390), ADR 0091 d. 8 (#3408), ADR 0089 q. 3 (#3420) | WAIT | Applied as drafted; this plan follows any change before agreement. |

## Decisions

| # | Decision | Detail |
|---|---|---|
| D-1 | Intrinsic `value` takes the target's nullability | R-5. `name` / `outcome` stay non-nullable strings. |
| D-2 | Variable nullability from the type | R-5: nullable unless a non-nullable value type. Verified in T004. |
| D-3 | Workflow input nullability pinned | R-4. Hashed only when `true`. |
| D-4 | Extend #3731's admission | R-3. #3731 already checks the input contract once in `WorkflowStartDispatcher` for every start, before the start policy and enqueue, and fills declared defaults. S-2 adds D-10's completion (`null` or type default) and the `null` refusal for non-nullable inputs to the validator's normalization, and default-on-null substitution before it. No new admission step. |
| D-5 | Policies on `WorkflowStrategyOptions.NullHandling` | R-2. Compiled to `WorkflowExecutable.ValuePolicies`; undefined policy also pinned on each JavaScript binding. |
| D-6 | Default-on-null = type default | R-6, Q2. Value types and enums only; other types keep `VF-ACT-004`. |
| D-7 | Jint replacer | R-7. Warnings `js-undefined-dropped`, `js-nonfinite-null`; an `undefined` array element or sparse hole becomes a `null` placeholder (a path diagnostic, not dropped) so positions hold under either policy; policy turns `undefined` into `null`. |
| D-8 | Script without a value | R-8, Q3. Expression scripts and completion-value Script activities need no `return`; only a function-body script with a consumed result and no `return` is refused at publication. A consumed `undefined` faults the step at run time; `null` under the policy. Unconsumed: nothing written. |
| D-9 | Converter option | R-9, Q4. `Elsa3MappingOptions.ApplyElsa3NullPolicies`, default `true`. |
| D-10 | Computed read miss | R-10. `undefined` plus `variable-not-found` warning; read set marks the name absent. |
| D-12 | Inputs that cannot hold null | R-12, Q5. `VF-ACT-004` under any policy; audit built-in activities. |
| D-11 | Diagnostic codes | [contracts/null-policies.md](contracts/null-policies.md). |

## Project Structure

### Documentation (this feature)

```text
specs/1201-null-missing-undefined/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/null-policies.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
Essentials/src/Workflows/Design/Core/Models/WorkflowStrategyOptions.cs           # NullHandling
Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutable.cs               # ValuePolicies
Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutableInputContract.cs  # IsNullable
Essentials/src/Workflows/Runtime/Core/Models/RuntimeInputBinding.cs              # pinned undefined policy
Essentials/src/Workflows/Runtime/Core/Services/NullPolicyDefaults.cs             # new
Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/
  ├── ExecutableNodeCompiler.cs        # intrinsic nullability
  ├── RuntimeInputBindingCompiler.cs   # ValidateNull + policy
  └── WorkflowExecutableCompiler.cs    # input contract, policies
Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/
  ├── Executables/WorkflowExecutableHasher.cs
  ├── Executables/WorkflowExecutableInputValidator.cs     # completion, null refusal (not the L119 conversion)
  ├── Executions/WorkflowStartDispatcher.cs               # default-on-null before validation
  └── Values/RuntimeActivityInputMaterializer.cs
Essentials/src/Workflows/Publishing/Api/Handlers/StartWorkflowTestRunRequestHandler.cs  # same substitution
Essentials/src/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs       # same substitution
Essentials/src/Expressions/JavaScript/Jint/Services/
  ├── JintResultMaterializer.cs
  ├── JintJavaScriptScriptEvaluator.cs
  └── IsolatedJintEngine.cs
Extensions/Elsa3/src/Mapping/
  ├── Elsa3MappingFeature.cs, Elsa3MappingOptions.cs (new)
  ├── Mappings/Elsa3WorkflowDefinitionToState.cs
  ├── Services/Elsa3ExpressionRewriter.cs
  └── ActivityMap/Elsa3BuiltInActivityMap.Entries.cs
```

**Structure Decision**: existing projects only; one new file in Runtime.Core (`NullPolicyDefaults`), one in the
Elsa 3 extension.

## Complexity Tracking

None.

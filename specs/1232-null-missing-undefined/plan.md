# Implementation Plan: Null, missing and undefined

**Branch**: `agent/claude/3052-s2-null-missing-undefined` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

**Status**: draft with the spec. The ADR baseline, including ADR 0089's decision-8 amendment, and S-8's value bridge
(#4838) are merged. Implementation starts once the spec is agreed and merged (D-18).

## Summary

Make explicit null a value, omission a defined outcome and `undefined` a visible policy, as ADR 0045 §2 (D-10, D-11)
says. The technical approach (research R-2 to R-10):
- intrinsics take their target's nullability;
- #3731's admission step (`WorkflowStartDispatcher.ApplyInputContract`, on every start) also completes optional
  inputs without a declared default and refuses `null` for non-nullable inputs;
- workflow inputs carry nullability in the pinned input contract;
- two workflow-level policies, `DefaultOnNull` and `UndefinedAsNull`, are authored on `WorkflowStrategyOptions`,
  pinned and hashed on `WorkflowExecutable`, and handed to each JavaScript evaluation from the executable;
- S-8's script value bridge writer reports or converts `undefined`, `NaN` and `Infinity` (one pass, its existing
  replacer), for binding results, `Run JavaScript` results and variable changes alike;
- nullability of variables, workflow inputs and outputs comes from the type, as in .NET; no Studio controls, the
  converter feature's documentation explains the policies;
- the Elsa 3 converter gets one option that records both policies, and loses spec 262's `?? null` lowering and
  spec 267's null refusals for nullable targets;
- a computed variable read that matches nothing stays `undefined` and records a warning: already built by S-4.

## Technical Context

**Language/Version**: C# / .NET (repository `global.json`).

**Primary Dependencies**: Jint (JavaScript), Fluid (Liquid), System.Text.Json, CShells features; Studio (React,
TypeScript) under `Studio/`. Builds on S-8's `CopyComparePatchValueBridge` and `IExecutionWarningSink` (#4838).

**Storage**: `ValueEnvelope` in every runtime store (EF Core providers, in-memory). No schema change: `Presence` is
already stored.

**Testing**: xUnit (no FluentAssertions), behavioural drives in `essentials/tests/Activities/Behavioral`, Studio's
client tests, the JTest measurement of #3052.

**Target Platform**: Elsa 4 server hosts.

**Project Type**: libraries (workflow publishing, runtime, expressions) and the Elsa 3 extension.

**Performance Goals**: the Jint replacer adds no more than one pass over the result; no extra store round trip at
admission.

**Constraints**: Elsa 3 knowledge stays in `extensions/Elsa3` (§E2.7). No compatibility path for executables or
fixtures from before this change: compatibility is owed from the first stable release, and fixtures whose hashes move
are regenerated in place. The policies are hashed only when on, and an input's nullability only when it is
non-nullable, so most fixtures do not move.

**Scale/Scope**: existing workflow, activity, expression, converter and Studio projects, including every write path
in research R-6 and their tests. Reconcile source paths with the initiative's required main sync before coding.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.2 Runtime takes no Design reference | PASS | Policies are compiled from `WorkflowStrategyOptions` in Publishing, which already references both sides. Runtime reads only `WorkflowExecutable`. |
| §E2.1 domain boundaries | PASS | Value rules stay in `Workflows.Runtime.Core` (`NullPolicyDefaults`); JavaScript boundary changes stay in S-8's bridge in `Expressions/JavaScript/Jint`. |
| §E2.7 Elsa 3 stays behind the import boundary | PASS | Elsa 4 gains general policies, usable by any workflow. The converter only selects them (D-11: no "converted from Elsa 3" marker). |
| §E5 versioning / §2.23.3 visibility | PASS | Public surface: `WorkflowStrategyOptions.NullHandling`, `WorkflowExecutable.ValuePolicies`, `WorkflowDeclaredInput.IsNullable`, `Elsa3MappingOptions`, and `NullPolicyDefaults` (shared domain API for runtime, Publishing and converter consumers). Policy-aware validator and trigger-mapper overloads are required interface members: Major API shape under §4.2, tracked with the target-policy projection in PublicAPI.Unshipped; existing overloads retain native policies-off behavior. Removal: internal `Elsa3ExpressionRewriter.AddNullCoalescing` and Studio's authored `isNullable` flags (pre-GA, ADR 0080). All other new services are internal. |
| §2.6.2 replacement contracts | PASS | No new replacement contract. The existing validator and trigger mapper gain policy-aware overloads; both validator modes apply policy after declared-default completion and before null/type validation (R-3). Catalog signatures and *Usage* lines change together (T037). |
| §2.17 duplication beats dependency | PASS | The type-default table is one helper in `Runtime.Core`, which every caller already references. |
| §2.24.2 sanctioned patterns (draft; advisory) | PASS | Feature options bound from configuration; hashed strategy options. No new extension point; this provisional catalog is not a ratified gate. |
| §2.22 / §2.22.1 docs and extension points | ACTION | Spec 095 contracts (already amended by #3388), the authoring reference for workflow settings, the Elsa 3 Mapping README and migration readiness guide, glossary rows for *default-on-null* and *undefined-as-null*, and the signatures/usage of `IWorkflowExecutableInputValidator`, `ITriggerInputMapper` and `IWorkflowStartDispatcher` in the Runtime extension-point catalog. No contributor or replacement contract is added. |
| §2.23.1–§2.23.2 tests | PASS | A test per acceptance scenario; a registration test for `Elsa3MappingOptions`; hash tests for each new field; Studio client tests for T033 to T035 and composed-host acceptance in T035a. |
| §2.23.5 exception boundaries | PASS | Publication and admission refusals keep their diagnostic exceptions; boundary warnings are diagnostics, not exceptions. |
| ADR 0038 content-addressed executables | PASS | Policies and input nullability are hashed, written only when set (R-2, R-4). |
| ADR 0045 §2, ADR 0083, ADR 0091 d. 3 and 8, ADR 0046 V8, ADR 0089 R3 and decision 8 | PASS | All are agreed and merged; ADR 0089's initial-value rule matches FR-001c. |
| Studio UX acceptance / initiatives guide, *Studio work stays in the initiative* | ACTION | Policy controls remain absent (Q6); changed nullability/default controls and diagnostics follow [ux-acceptance.md](ux-acceptance.md), T033 to T035a, on the composed host with independent inspection. |

## Decisions

Plan decisions are numbered P-n, apart from the board's D-n.

| # | Decision | Detail |
|---|---|---|
| P-1 | Intrinsic `value` takes the target's nullability | R-5, Q8. From the target's type (P-2); `name` / `outcome` stay non-nullable strings. The intrinsic executor checks a run-time `null` too (R-6). |
| P-2 | Nullability from the type | R-5, Q8, Q10: variables, workflow inputs and outputs and authored activity definitions, by the type registry's `supportsNull`; a type default only for `Boolean`, the number types and enums (FR-001b); an unassigned variable reads that default (Q11). T004, T004a. |
| P-3 | Workflow input nullability pinned | R-4. From the type (P-2); hashed only when non-nullable. |
| P-4 | Extend #3731's admission | R-3. Requiredness first, declared-default completion, complete-bag optional completion, policy substitution, then null/type validation in the same validator normalization. All admission and early-validation callers, including current trigger mapping/discovery and physical saved-start validation, pass the selected or retained executable's policies through the new overloads. No new admission step. |
| P-5 | Policies on `WorkflowStrategyOptions.NullHandling` | R-2. Compiled to `WorkflowExecutable.ValuePolicies`; each JavaScript evaluation gets the undefined policy from the executable. |
| P-6 | Default-on-null = type default | R-6, Q2, Q10. `Boolean`, the number types and enums only (FR-001b); every other type keeps `VF-ACT-004`. |
| P-7 | In S-8's bridge writer | R-7. `VF-UNDEF-002` (property dropped), `VF-UNDEF-003` (array element or hole as a `null` placeholder, either policy), `VF-NUM-001` (computed `NaN`/`Infinity`); the policy keeps `undefined` properties as `null`. |
| P-8 | Script without a value | R-8, Q3. Bindings (expression or statement body) need no `return`; only `Run JavaScript` (a function body) with a bound result and no `return` is refused at publication (`VF-UNDEF-001`). A consumed `undefined` faults the step at run time; `null` under the policy. Unbound: nothing written. |
| P-13 | `undefined` assigned to a variable | R-8, Q7. `VF-UNDEF-001` fault naming the variable; `null` under the policy. |
| P-14 | Studio | Q6. No controls: engineers set the policies through the converter option or the definition API, and the documentation explains them. Warnings show through the existing activity execution warnings. |
| P-9 | Converter option | R-9, Q4. `Elsa3MappingOptions.ApplyElsa3NullPolicies`, default `true`. |
| P-10 | Computed read miss | R-10. Built by S-4 (`VF-READ-001`); no work here. |
| P-15 | Required with a default | Q9, FR-034. Publication refuses a member declared both required and with a default (`VF-ACT-014`); Studio's contract editor turns *Required* off when a default is set; built-in inputs declared that way become optional. |
| P-12 | Inputs that cannot hold null | R-12, Q5. `VF-ACT-004` under any policy; audit built-in activities. |
| P-11 | Diagnostic codes | [contracts/null-policies.md](contracts/null-policies.md). |

## Project Structure

### Documentation (this feature)

```text
specs/1232-null-missing-undefined/
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
essentials/src/Workflows/Design/Core/Models/WorkflowStrategyOptions.cs           # NullHandling
essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutable.cs               # ValuePolicies
essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutableInputContract.cs  # IsNullable
essentials/src/Workflows/Runtime/Core/Services/NullPolicyDefaults.cs             # new
essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/
  ├── ExecutableNodeCompiler.cs        # intrinsic nullability
  ├── RuntimeInputBindingCompiler.cs   # ValidateNull + policy
  └── WorkflowExecutableCompiler.cs    # input contract, policies
essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/
  ├── Executables/WorkflowExecutableHasher.cs
  ├── Executables/WorkflowExecutableInputValidator.cs     # completion, null refusal (not the L119 conversion)
  ├── Executions/WorkflowStartDispatcher.cs               # pass policies to validator normalization
  └── Values/RuntimeActivityInputMaterializer.cs
essentials/src/Workflows/Publishing/Api/Handlers/StartWorkflowTestRunRequestHandler.cs  # same validator policies
essentials/src/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs       # same validator policies
essentials/src/Expressions/JavaScript/Jint/Services/          # S-8's bridge (#4838, merged)
  ├── CopyComparePatchValueBridge.cs       # undefined, holes, NaN/Infinity, the policy
  ├── JintPortableJavaScriptEvaluator.cs   # undefined binding result under the policy
  └── JintJavaScriptScriptEvaluator.cs     # Run JavaScript result
essentials/src/Expressions/JavaScript/Core/Exceptions/JavaScriptValueExceptions.cs  # VF-UNDEF-*, VF-NUM-001
docs: Elsa 3 converter feature README, workflow definition reference
extensions/Elsa3/src/Mapping/
  ├── Elsa3MappingFeature.cs, Elsa3MappingOptions.cs (new)
  ├── Mappings/Elsa3WorkflowDefinitionToState.cs
  ├── Services/Elsa3ExpressionRewriter.cs
  └── ActivityMap/Elsa3BuiltInActivityMap.Entries.cs
```

**Structure Decision**: existing projects only. New policy/default helpers stay in the owning projects shown above.

**Main-sync reconciliation**: the source inventory above uses the reconciled repository paths. The main-sync tree
already contains `WorkflowDeclaredInput.IsNullable` and `HasExplicitNullDefault`. Reuse them: derive authored
nullability from the type, preserve the explicit-null-default marker, and reconcile the hash encoding with P-3 and
T009. The reconciled validator accepts supplied null for nullable inputs independently of requiredness; retain that
behavior and its no-default regression while extending normalization in T013. Requiredness concerns presence.
Recheck S-7's script-write integration and every R-6 write path on the reconciled tree before implementation.

## Complexity Tracking

None.

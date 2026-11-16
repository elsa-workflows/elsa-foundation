# Implementation Plan: Value model and conversion

**Branch**: `claude/3052-s8-define-cc74d4` | **Date**: 2026-11-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1228-value-model-conversion/spec.md`

## Summary

Build the defaults of the ADR 0046 amendment. The Jint integration stops converting every value to frozen doubles up
front: a replaceable script value bridge puts plain JavaScript values into scripts and JavaScript input expressions
alike, records what entered, and after the run reports only what the script changed (copy, compare, patch). Script
body changes go to S-7's step write path (ADR 0090); an input expression that writes is refused at publish where
visible and fails at run time otherwise. The bridge's cost is
measured. One replaceable precision policy, installed by a feature, refuses a lossy
number at a write. The dynamic kind gets one registered alias, `Any`; `Object` and `Elsa.Any` read as `Any`. Binding
checks treat `Any`, `JsonElement` and `JsonNode` as one kind. A type reference can nest, so a dictionary of string
arrays has a full type. The Elsa 3 converter converts an untyped result into a typed input per JSON kind, or refuses at
import. A script result inherits the protection of what it read.

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json` SDK).

**Primary Dependencies**: Jint (already used), System.Text.Json; no new external dependency.

**Storage**: none new. The executable records the pinned JavaScript adapter version (FR-028). Elsa 4 has no released definitions, so the `Object` → `Any` reading needs no data migration.

**Testing**: xUnit (no FluentAssertions). A precision suite (numbers beyond 2^53, long decimals, `10.50`, `2.0`,
`1e21`, negatives), change-detection unit tests, end-to-end publish-and-run tests, the Elsa 3 fixture
`reusable-reads-result`, a JTest corpus measurement, and a change-detection benchmark (SC-007).

**Target Platform**: Elsa 4 server hosts (Foundation host, Workbench) and Studio's variable type list.

**Project Type**: libraries.

**Performance Goals**: change detection linear in the data a script reads; at most twice today's run time on a 1 MB
input (SC-007). The bridge time is a metric per evaluation (FR-010), so a later bridge can be compared.

**Constraints**:
- No CLR surface in scripts (D-16); the S-1 detector stays the refusal for Elsa 3 scripts that need one.
- Runtime takes no Design reference.
- An adapter rule never changes in place: the JavaScript adapter's new behaviour is a new pinned version.
- Date interpretation, unmatched keys and the serializer baseline are S-9's; nothing here adds a second rule.

**Scale/Scope**: the Jint engine and result materializer; one new policy seam; the alias registry and Studio's
variable type list; about ten `Elsa.Any`/`Object` comparison sites; `TypeReferenceFactory`; publication's binding
type check; the Elsa 3 lowerer and its type shapes.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §2.24.2 / §2.24.3 sanctioned patterns | PASS | The script value bridge and the precision policy are replacement contracts (one active implementation per host), a sanctioned pattern. No new pattern. |
| §E2.2 Runtime takes no Design reference | PASS | Engine, policy and change detection are runtime-side. Studio's type list and the converter are design-side. |
| §2.17 duplication beats dependency | PASS | The dynamic-kind check lives once, in the alias convention, replacing about ten string comparisons. |
| §E2.6.1 executable-always-runs | PASS | Before 4.0, reset and republish (ADR 0046 V3). The new adapter version is pinned in new executables. |
| §E5 / §4.2 versioning | ACTION | `TypeReference` gains a nested element; the executable gains the adapter version. Document and schema versions increase. |
| Extension-point catalog | ACTION | The bridge and the precision policy are new replacement contracts: `EXTENSION_POINTS.md` and the generated map change. |
| §2.22 docs | ACTION | The adapter contracts (FR-027) and the JavaScript value docs. |
| §2.23 tests | PASS | One test per acceptance scenario, the precision suite, the registration test for the policy feature. |
| ADR 0046 amendment (#3422) | WAIT | Applied at `ba99ca02d`; agreed, not yet merged into the branch. Implementation starts once it is merged (D-18). |
| ADR 0090 (accepted) | PASS | C1–C4 applied; the step write path is S-7's. |

## Project Structure

### Documentation (this feature)

```text
specs/1228-value-model-conversion/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── javascript-adapter.md
│   └── precision-policy.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
Essentials/src/Expressions/JavaScript/Jint/Services/
├── IsolatedJintEngine.cs              # values in through the bridge; no freezing
├── JintResultMaterializer.cs          # result out through the policy
└── (new) CopyComparePatchValueBridge.cs  # default bridge: entry record, lossy marks, copy, compare, patch, metric

Essentials/src/Expressions/JavaScript/Core/
└── (new) IScriptValueBridge, IJavaScriptPrecisionPolicy, RefusingPrecisionPolicy; registered by the feature

Essentials/src/Primitives/Primitives/Models/
├── TypeAliasConvention.cs             # Any registered; Object/Elsa.Any read as Any; DateOnly, TimeOnly
├── TypeReferenceFactory.cs            # nested element type
└── ValueRepresentation.cs             # dynamic-kind check through the convention

Essentials/src/Expressions/Elsa.Expressions/Services/DefaultVariableTypeDescriptorProvider.cs   # JSON, no Object
Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/
├── RuntimeInputBindingCompiler.cs     # one dynamic kind in the declared-type check
└── ValueConversionPlanResolver.cs     # JSON value of matching kind → scalar (D-12)

Extensions/Elsa3/src/Mapping/          # Object → Any; TypesMatch; per-kind conversion or refusal
Essentials/tests/...                   # precision suite, change detection, end-to-end, Elsa 3 fixture
docs/reference/value-adapters.md       # adapter contracts (FR-027)
```

**Structure Decision**: libraries only. New contracts go in `Essentials/src/Expressions/JavaScript/Core`, which the
Jint project already references. The script body read surface is S-4's (spec 1222); S-8 builds on it. The JavaScript adapter and the policy live in the JavaScript expression
modules. The kinds live in Primitives. Publication and the Elsa 3 converter change at the sites listed.

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Plain values in | FR-001, FR-002. `CreateObject` and `CreateArray` stop setting non-writable descriptors and `PreventExtensions`. The `args` and `variables` containers stay non-replaceable globals. `ScriptValueIsolationTests` changes with it. Script activities and input expressions use the same bridge (D-15). |
| P-2 | Bridge contract | FR-009. `IScriptValueBridge` (replacement contract): enter values → entry record; collect changes from the engine → changes. Default `CopyComparePatchValueBridge`. The Jint engine calls only the contract. |
| P-3 | Entry record | FR-003, FR-011. For each value read, keep the `JsonElement` that entered and mark each lossy number (ADR 0046 V9) with its path. Per evaluation, discarded after. |
| P-4 | Change detection | FR-003–FR-005. After the run, convert each original JSON afresh and compare it with the script's value, JavaScript with JavaScript (ADR 0090 C2): equal → unchanged; a changed member → member change; a reassigned variable → whole assignment; a changed array → whole. Unchanged members come from the original JSON. |
| P-5 | Cost metric | FR-010. The bridge records enter and collect durations and the bytes converted per evaluation as a histogram on the JavaScript expression meter; the benchmark of SC-007 uses the same numbers. |
| P-6 | Precision policy | FR-012–FR-015. `IJavaScriptPrecisionPolicy` is a replacement contract with one built-in `RefusingPrecisionPolicy`, installed by the JavaScript feature, with no options and no node or workflow override. Called at a write with the lossy paths it carries. Computing and host-function arguments only warn. |
| P-6a | Old executables | FR-028. An executable without a JavaScript adapter version is refused at run time with a republish diagnostic. |
| P-7 | Hand-off to S-7 | FR-006, FR-007, Q1. A script activity passes its changes to the step write path (S-7) with the read-set base (ADR 0092); the bridge raises the C2 visibility warning. Until S-7's path is on the branch, User Story 3 is tested at the bridge level. |
| P-7a | Input expressions never write | FR-006a, Q6. Publish: the JavaScript binding compiler (ADR 0083 grammar) refuses an assignment whose target is `variables.<name>` or a member path under it, with a new stable `VF-` code. Run time: `JintPortableJavaScriptEvaluator` fails the evaluation when the bridge reports any change, with the same code and the path. |
| P-8 | One dynamic kind | FR-017–FR-020. `TypeAliasConvention` registers `Any` and resolves `Object`, `System.Object` and `Elsa.Any` to it; `object` is no longer a variable type. The `Elsa.Any` constants and the `Alias is "Object" or "Elsa.Any"` checks go through one `IsDynamicKind`. `DateOnly` and `TimeOnly` register as bare aliases. |
| P-9 | Binding check | FR-021. The exact alias match in `RuntimeInputBindingCompiler` treats the dynamic kind as one. `JsonObject` keeps a run-time shape check. |
| P-10 | Nested types | FR-022. `TypeReference` gets an optional `Element` type reference. `Decompose` recurses. Diagnostics print the full shape. |
| P-11 | JSON to scalar | FR-025. With #3167 fixed (S-0), the `elsa.json@1` route converts a JSON boolean, number or string to a matching scalar. Text is never parsed. |
| P-12 | Elsa 3 conversion | FR-019, FR-023, FR-024. The converter emits `Any` for its `Object` cases. `TypesMatch` stops treating an untyped producer as compatible with a typed input; a per-kind table from Elsa 3.6.3 (research R-5) decides convert or refuse with a new `ELS3-` code. |
| P-13 | Protection | FR-026. At result materialization, take the strongest protection policy among the read set's values (ADR 0092) and attach it to the result. |
| P-14 | Adapter contracts | FR-027, FR-028. `docs/reference/value-adapters.md`; the JavaScript adapter gets a version that new executables pin. |

## Complexity Tracking

> No constitution violations. The ACTION gates are covered by tasks. The WAIT gate is the merge of the agreed ADR 0046
> amendment into the initiative branch.

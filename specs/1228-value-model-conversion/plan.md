# Implementation Plan: Value model and conversion

**Branch**: `claude/3052-s8-define-cc74d4` | **Date**: 2026-11-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1228-value-model-conversion/spec.md`

## Summary

Build the defaults of the ADR 0046 amendment. The Jint integration stops converting every value to frozen doubles up
front: values enter as plain JavaScript values, the engine records what entered, and after the run it writes only what
the script changed (copy, compare, patch). One replaceable precision policy, installed by a feature, refuses a lossy
number at a write. The dynamic kind gets one registered alias, `Any`; `Object` and `Elsa.Any` read as `Any`. Binding
checks treat `Any`, `JsonElement` and `JsonNode` as one kind. A type reference can nest, so a dictionary of string
arrays has a full type. The Elsa 3 converter converts an untyped result into a typed input per JSON kind, or refuses at
import. A script result inherits the protection of what it read.

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json` SDK).

**Primary Dependencies**: Jint (already used), System.Text.Json; no new external dependency.

**Storage**: none new. The executable records the node's precision acceptance (FR-014) and the pinned JavaScript
adapter version (FR-027). Elsa 4 has no released definitions, so the `Object` → `Any` reading needs no data migration.

**Testing**: xUnit (no FluentAssertions). A precision suite (numbers beyond 2^53, long decimals, `10.50`, `2.0`,
`1e21`, negatives), change-detection unit tests, end-to-end publish-and-run tests, the Elsa 3 fixture
`reusable-reads-result`, a JTest corpus measurement, and a change-detection benchmark (SC-007).

**Target Platform**: Elsa 4 server hosts (Foundation host, Workbench) and Studio's variable type list.

**Project Type**: libraries.

**Performance Goals**: change detection linear in the data a script reads; at most twice today's run time on a 1 MB
input (SC-007).

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
| §2.24.2 / §2.24.3 sanctioned patterns | PASS | The precision policy is a replacement contract (one active implementation per host), a sanctioned pattern. No new pattern. |
| §E2.2 Runtime takes no Design reference | PASS | Engine, policy and change detection are runtime-side. Studio's type list and the converter are design-side. |
| §2.17 duplication beats dependency | PASS | The dynamic-kind check lives once, in the alias convention, replacing about ten string comparisons. |
| §E2.6.1 executable-always-runs | PASS | Before 4.0, reset and republish (ADR 0046 V3). The new adapter version is pinned in new executables. |
| §E5 / §4.2 versioning | ACTION | `TypeReference` gains a nested element; the executable gains the adapter version and the precision acceptance. Document and schema versions increase. |
| Extension-point catalog | ACTION | The precision policy is a new replacement contract: `EXTENSION_POINTS.md` and the generated map change. |
| §2.22 docs | ACTION | The adapter contracts (FR-026) and the JavaScript value docs. |
| §2.23 tests | PASS | One test per acceptance scenario, the precision suite, the registration test for the policy feature. |
| ADR 0046 amendment (#3422) | WAIT | Applied at `ba99ca02d`. Q5 (`Object` → `Any`) is open: User Story 4's tasks wait on it. |

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
├── IsolatedJintEngine.cs              # plain values in; entry record; no freezing; lossy-number marks
├── JintResultMaterializer.cs          # result out through the change detector and the policy
└── (new) ScriptChangeDetector.cs      # copy, compare, patch

Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/
└── (new) IJavaScriptPrecisionPolicy + RefusingPrecisionPolicy + feature

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
docs/reference/value-adapters.md       # adapter contracts (FR-026)
```

**Structure Decision**: libraries only. The JavaScript adapter and the policy live in the JavaScript expression
modules. The kinds live in Primitives. Publication and the Elsa 3 converter change at the sites listed.

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Plain values in | FR-001, FR-002. `CreateObject` and `CreateArray` stop setting non-writable descriptors and `PreventExtensions`. The `args` and `variables` containers stay non-replaceable globals. `ScriptValueIsolationTests` changes with it. |
| P-2 | Entry record | FR-003, FR-009. For each value read, keep the `JsonElement` that entered, and mark each number that is lossy (Q5) with its path. Kept per run, discarded after. |
| P-3 | Change detection | FR-003–FR-006. After the run, walk each read value against its entry: equal by JavaScript value → unchanged; a changed member → patch that path; a replaced value → replace; a changed array → whole array. Unchanged members are copied from the entry, not from JavaScript, so their text is kept. |
| P-4 | Precision policy | FR-010–FR-013. `IJavaScriptPrecisionPolicy` is a replacement contract with one built-in `RefusingPrecisionPolicy`, installed by the JavaScript feature. Called at a write with the list of lossy paths it carries; returns refuse or allow with diagnostics. Computing and host-function arguments only warn and never reach the policy as a write. |
| P-5 | Acceptance | FR-014. A script activity setting `AcceptPrecisionLoss` (default off), compiled into the executable; the policy gets it in its context. |
| P-6 | Script variable writes | FR-007, Q3. `variables.x` becomes writable for script activities only. Detected changes go to the step's variable session (ADR 0090 amendment; ADR 0092 read set as base). Input-binding expressions keep a read-only `variables`. Built on the S-10 session write path. |
| P-7 | One dynamic kind | FR-016–FR-019. `TypeAliasConvention` registers `Any` and resolves `Object`, `System.Object` and `Elsa.Any` to it; `object` is no longer a variable type. The `Elsa.Any` constants and the `Alias is "Object" or "Elsa.Any"` checks go through one `IsDynamicKind`. `DateOnly` and `TimeOnly` register as bare aliases. |
| P-8 | Binding check | FR-020. The exact alias match in `RuntimeInputBindingCompiler` treats the dynamic kind as one. `JsonObject` keeps a run-time shape check. |
| P-9 | Nested types | FR-021. `TypeReference` gets an optional `Element` type reference. `Decompose` recurses. Diagnostics print the full shape. |
| P-10 | JSON to scalar | FR-024. With #3167 fixed (S-0), the `elsa.json@1` route converts a JSON boolean, number or string to a matching scalar. Text is never parsed. |
| P-11 | Elsa 3 conversion | FR-018, FR-022, FR-023. The converter emits `Any` for its `Object` cases. `TypesMatch` stops treating an untyped producer as compatible with a typed input; a per-kind table from Elsa 3.6.3 (research R-5) decides convert or refuse with a new `ELS3-` code. |
| P-12 | Protection | FR-025. At result materialization, take the strongest protection policy among the read set's values (ADR 0092) and attach it to the result. |
| P-13 | Adapter contracts | FR-026, FR-027. `docs/reference/value-adapters.md`; the JavaScript adapter gets a version that new executables pin. |

## Complexity Tracking

> No constitution violations. The ACTION gates are covered by tasks. The WAIT gate is Q1 (#3422 Q5): User Story 4's
> tasks wait on it.

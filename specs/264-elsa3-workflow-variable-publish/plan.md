# Implementation Plan: Elsa 3 converter: workflow variables that can be published

**Branch**: `agent/claude/3122-elsa3-workflow-variable-publish` | **Date**: 2026-10-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/264-elsa3-workflow-variable-publish/spec.md`

## Summary

The change is in the converter's variable mapping, `Elsa3VariableMapper`:
- A variable with no Elsa 3 value gets no starting value (`Default: null`).
- A variable with a value is converted with Elsa 3's own rule, which Elsa 4's `IObjectConverter` keeps line for line
  (R-2), and carried as a `Literal` starting value holding the converted value as JSON (R-3).
- A value that Elsa 3's rule cannot convert is refused with a new code, `ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE`
  (R-5).

Workflow variables are checked in the normaliser, so a refusal stops the conversion before the state is mapped (R-4).
Container variables go through the same mapper and get the same rule. Publication, the runtime and the Elsa 4 variable
model do not change. The Mapping README describes how starting values convert.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: `IObjectConverter` and `IWellKnownTypeRegistry` from `Elsa.Serialization.Core`, already
referenced by `Elsa3.Mapping`; both are registered by the same `Serialization` feature (R-6). None added.

**Storage**: N/A

**Testing**: xunit only (no FluentAssertions).
- Mapping tests in `Extensions/Elsa3/tests/Mapping/Tests`, through `ConversionServices` (which gains
  `SerializationServices.ObjectConverter()`).
- One integration test class in `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests`, through
  `Elsa3ImportHost`, reading the run's root variable frame (`WorkflowExecutionState.RootVariableFrame`).

**Target Platform**: library (`Elsa3.Mapping`)

**Project Type**: single library and its two test projects

**Performance Goals**: N/A. One object conversion per declared variable with a value.

**Constraints**:
- Faithful or refuse.
- Converter only.
- Build and test only the touched projects (shared machine).

**Scale/Scope**: about 60 production lines in three files, one new diagnostic code, one new mapping test class, one
new integration test class and two fixtures.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 stays behind the import boundary | PASS | Every production change is in `Extensions/Elsa3/src/Mapping` and `Extensions/Elsa3/src/Models` |
| §E2.2 Runtime takes no Design reference | PASS | No project reference changes |
| §E5 versioning | PASS | `Elsa3VariableMapper` is internal. One public constant is added to `Elsa3ActivityDiagnosticCodes`; nothing is removed or renamed |
| §2.6.2 replacement contracts | PASS | None added or changed |
| §2.17 duplication beats dependency | PASS | No new dependency: `IObjectConverter` comes from a project and a feature the mapping already depends on (R-6). Re-implementing Elsa 3's conversion rule would be far more than 5–10 lines |
| §2.24.2 sanctioned patterns | PASS | Existing patterns only: a normaliser pass that adds diagnostics to the plan, and constructor injection into the existing mapping services |
| §2.22 / §2.22.1 docs and extension points | ACTION | Mapping README: starting values and the new code. No extension point changes, so `EXTENSION_POINTS.md` and the generated maps are unchanged |
| §2.23.1–§2.23.3 tests and visibility | PASS | A test per starting-value kind and per refusal (FR-007), and the issue's end-to-end test (FR-006). No new types |
| §2.23.5 exception boundaries | PASS | The converter's `TryConvertTo` returns a result; a failure becomes the refusal. Serialising the converted value is inside the same check (R-5). Nothing new throws on input |

## Decisions

| # | Decision | Detail |
|---|---|---|
| D-1 | No value, no starting value | `source.Value is null` → `Default: null`. Elsa 3 started such a variable as `null`; Elsa 4 does the same when no initial binding is declared (R-1) |
| D-2 | Elsa 3's rule through `IObjectConverter` | `TryConvertTo(source.Value, clrType, StrictMode: true)`, where `clrType` is the type the mapper already resolves, closed to an array when `isArray` is set (R-2; strict per the second clarification). A value on a type the registry does not know is refused before conversion |
| D-3 | A `Literal` holding JSON | The converted value serialised to a `JsonElement`, as `new ArgumentValue(json, "Literal")`, matching `WorkflowBuilder.Variable` (R-3) |
| D-4 | Check workflow variables in the normaliser | A normaliser pass over the definition's variables reports a refusal at `$.variables[i]`. The state mapper maps them afterwards with the same mapper. Container variables are checked where they are read today (`ReadContainerVariables`), through `context.Error` (R-4) |
| D-5 | One new code | `ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE`, an error. No existing code means "this value cannot become its own declared type" (R-5) |
| D-6 | Reusable activities unchanged in shape | The graph manifest already writes `initialValue` from `Default` (`Elsa3ReusableActivityImportMaterializer.BuildGraphManifest`); with D-1 and D-3 it gets `null` or a `Literal` (R-7) |
| D-7 | JTest measurement | A throwaway mapping test, deleted afterwards, runs every workflow and container variable in the corpus through the mapper and records how many have a value, how many convert and how many are refused (SC-004, R-8) |

## Project Structure

### Documentation (this feature)

```text
specs/264-elsa3-workflow-variable-publish/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── variable-starting-values.md
├── checklists/
│   └── requirements.md
└── tasks.md            (speckit-tasks)
```

### Source Code (repository root)

```text
Extensions/Elsa3/src/Models/
└── Elsa3ActivityDiagnosticCodes.cs          + VariableValueUnconvertible

Extensions/Elsa3/src/Mapping/
├── Conversion/Elsa3VariableMapper.cs        D-1..D-3: TryMap with IObjectConverter; Map for validated input
├── Conversion/Elsa3ActivityNormalizer.cs    D-4: workflow-variable pass; container variables through TryMap
├── Mappings/Elsa3WorkflowDefinitionToState.cs   inject IObjectConverter, map with the same rule
└── README.md                                starting values, new code

Extensions/Elsa3/tests/Mapping/Tests/
├── Support/ConversionServices.cs            + IObjectConverter
└── Elsa3VariableStartingValueTests.cs       new: every kind of FR-003, every refusal of FR-004

Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/
├── WorkflowVariablePublishTests.cs          new: FR-006 and US2 end to end
└── Fixtures/
    ├── workflow-variable-captures-event.json    new: the waiter with a workflow variable capturing the Event result
    └── workflow-variable-starting-values.json   new: one variable per kind, with values
```

**Structure Decision**: library change inside the existing `Elsa3.Mapping` project; tests in the existing Mapping and
Import integration test projects.

## Complexity Tracking

None.

# Implementation Plan: Elsa 3 converter: a null activity member converts to what Elsa 3 ran with

**Branch**: `agent/claude/3200-elsa3-null-input` | **Date**: 2026-10-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/267-elsa3-null-members/spec.md`

## Summary

The activity map records, per member, what a `null` converts to (`OnNull`: no binding, typed null, Elsa 3's value, or
refuse). The normaliser's member pass applies it before the member rule, so no bare JSON `null` reaches the state
mapper. A refusal uses one new code, `ELS3-ACTIVITY-NULL-UNCONVERTIBLE`. The state mapper reads the typed null as a
`Literal` with a null value, which publication accepts for a nullable input. Outputs and members resolved by name get
no binding. A coverage test checks every mapped input against the real Elsa 4 catalog. Publication, the runtime and
Elsa 4 defaults do not change.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: none added.

**Storage**: N/A

**Testing**: xunit only (no FluentAssertions).
- Mapping tests in `Extensions/Elsa3/tests/Mapping/Tests`, through `ConversionServices` and `RealActivityCatalog`.
- One integration test class in `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests`, through
  `Elsa3ImportHost`: import, publish, run.

**Target Platform**: library (`Elsa3.Mapping`)

**Project Type**: single library and its two test projects

**Performance Goals**: N/A

**Constraints**:
- Faithful or refuse.
- Converter only.
- Build and test only the touched projects.

**Scale/Scope**: about 120 production lines in five files (the rule type, the table, the normaliser, the state mapper,
the codes), one new code, one mapping test class, two coverage checks, one integration test class and one fixture.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 stays behind the import boundary | PASS | Every production change is in `Extensions/Elsa3/src/Mapping` and `Extensions/Elsa3/src/Models` |
| §E2.2 Runtime takes no Design reference | PASS | No project reference changes |
| §E5 versioning | PASS | `Elsa3MemberRule` gains an optional trailing parameter; one public constant is added to `Elsa3ActivityDiagnosticCodes`; nothing is removed or renamed |
| §2.6.2 replacement contracts | PASS | None added or changed |
| §2.17 duplication beats dependency | PASS | No new dependency |
| §2.24.2 sanctioned patterns | PASS | Existing patterns only: a member-rule field read by the normaliser's member pass, diagnostics added to the plan |
| §2.22 / §2.22.1 docs and extension points | ACTION | Mapping README (null members, the new code) and the spec 236 member map (FR-009). No extension point changes |
| §2.23.1–§2.23.3 tests and visibility | PASS | A test per table row (SC-004), the coverage check (FR-008) and the end-to-end test (SC-003). The new record is public like `Elsa3MemberRule` |
| §2.23.5 exception boundaries | PASS | Refusals are diagnostics; nothing new throws on input |

## Decisions

| # | Decision | Detail |
|---|---|---|
| D-1 | Per-member `OnNull` | `Elsa3MemberRule.OnNull` (`Elsa3NullConversion`), set in `Elsa3BuiltInActivityMap.Entries.cs` for every Carry and Narrowed-contract input, from the spec's table (R-6) |
| D-2 | Applied in the member pass | `ApplyRule` handles a null (JSON `null`, or a literal holding null, R-5) through `OnNull` before the rule; an R2/R5 default without `OnNull` keeps its existing path through `ApplyDefaults` (R-2) |
| D-3 | Typed null | `Elsa3TypedValues.CreateNull(alias)`, the Elsa 4 input's alias; `Elsa3ActivityToState` maps it to `ArgumentValue(null, "Literal")`. `ApplyDefaults` leaves it alone (R-3, R-8) |
| D-4 | Never keep a bare null | `Elsa3ActivityToState.Map` drops a value it cannot read, null included; the "a JSON null is kept" branch goes (R-2) |
| D-5 | One new code | `ELS3-ACTIVITY-NULL-UNCONVERTIBLE`; the node fails, so `CheckRequiredInputs` does not repeat it (R-4) |
| D-6 | Emitted values count as bound | `IsEmittedDefault` also accepts an `OnNull` emitted value (Correlate / SetName `""`) (R-9) |
| D-7 | Outputs and by-name members | A null output and a null member in `BindByName` get no binding (R-10) |
| D-8 | Coverage against the real catalog | Two checks in `Elsa3ActivityMapCoverageTests` (R-7) |
| D-9 | Deliberate null | Read as a JSON `null` (unchanged behaviour); the declared-default gap goes to #3281 (R-5). Spec FR-005 corrected |
| D-10 | Measurement | Throwaway import-host test, before and after (R-11) |

## Project Structure

### Documentation (this feature)

```text
specs/267-elsa3-null-members/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── null-member-diagnostic.md
├── checklists/
│   └── requirements.md
└── tasks.md            (speckit-tasks)
```

### Source Code (repository root)

```text
Extensions/Elsa3/src/Models/
└── Elsa3ActivityDiagnosticCodes.cs              + NullUnconvertible

Extensions/Elsa3/src/Mapping/
├── ActivityMap/Elsa3MemberRule.cs               + OnNull, Elsa3NullConversion, Elsa3NullRule
├── ActivityMap/Elsa3BuiltInActivityMap.Rules.cs + null helpers
├── ActivityMap/Elsa3BuiltInActivityMap.Entries.cs   OnNull per member (the spec's table)
├── Conversion/Elsa3TypedValues.cs               + CreateNull, IsNullLiteral
├── Conversion/Elsa3ActivityNormalizer.cs        D-2, D-3, D-5, D-6, D-7
├── Mappings/Elsa3ActivityToState.cs             D-3, D-4
└── README.md                                    null members, the new code

specs/236-elsa3-activity-map/member-map.md       FR-009: null conversions

Extensions/Elsa3/tests/Mapping/Tests/
├── Elsa3NullMemberTests.cs                      new: one case per table row, deliberate null, outputs, by-name
└── Elsa3ActivityMapCoverageTests.cs             + D-8

Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/
├── NullMemberPublishTests.cs                    new: SC-003
└── Fixtures/null-members.json                   new
```

**Structure Decision**: library change inside the existing `Elsa3.Mapping` project; tests in the existing Mapping and
Import integration test projects.

## Complexity Tracking

None.

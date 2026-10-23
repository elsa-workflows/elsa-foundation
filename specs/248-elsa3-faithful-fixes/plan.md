# Implementation Plan: Elsa 3 converter faithful fixes from the JTest measurement

**Branch**: `agent/claude/3051-elsa3-faithful-fixes` | **Date**: 2026-10-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/248-elsa3-faithful-fixes/spec.md`

## Summary

Four converter refusals in `Extensions/Elsa3/src/Mapping` reject Elsa 3 input that Elsa 4 runs the same. Each fix is
local to the stage that refuses: the activity map and normalizer for rows 1–3, the flowchart graph builder for the
non-trigger part of row 2, and value-flow lowering's type check for row 4. No diagnostic code changes. The spec 236
member map, the Mapping README and the activity-map coverage test are amended in the same unit (owner approval,
2026-10-19).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: none added

**Storage**: N/A

**Testing**: xunit only (no FluentAssertions), `Extensions/Elsa3/tests/Mapping/Tests` with `RealActivityCatalog` and `ConversionServices`

**Target Platform**: library (`Elsa3.Mapping`)

**Project Type**: single library + its test project

**Performance Goals**: N/A (a small table lookup per typed value)

**Constraints**: faithful or refuse; build and test only the touched projects (shared machine)

**Scale/Scope**: four rows; about 40 production lines, one new test class, one coverage fact

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 stays behind the import boundary | PASS | Every change is in `Extensions/Elsa3/src/Mapping`. The Elsa 3 type aliases (`HttpHeaders`, `ObjectDictionary`, `HttpStatusCode`, `List<T>`) are read there only |
| §E2.2 Runtime takes no Design reference | PASS | No project reference changes |
| §E5 versioning | PASS | No public API change. `Elsa3ValueFlowLowerer` is public, but only private members change |
| §2.6.2 replacement contracts | PASS | None added or changed |
| §2.17 duplication beats dependency | PASS | The Elsa 3 alias table is a few lines in the lowerer, not a shared dependency |
| §2.24.2 sanctioned patterns | PASS | Existing patterns only: a member rule (R2 `DefaultFlip`), a map-entry lookup, a static alias table |
| §2.22 / §2.22.1 docs and extension points | ACTION | Mapping README ("Behaviour worth knowing") and the spec 236 member map. No extension point changes, so `EXTENSION_POINTS.md` and the generated maps are unchanged |
| §2.23.1–§2.23.3 tests and visibility | PASS | One test per row plus a test per kept refusal next to it; a coverage fact pins which entries are triggers. No new types |
| §2.23.5 exception boundaries | PASS | Nothing new throws on input |
| Decision record | ACTION | The spec 236 member map is the decision record for members; amended for rows 1 and 2 with the owner's 2026-10-19 approval |

## Decisions

| # | Decision | Detail |
|---|---|---|
| D-1 | WriteLine.text is an R2 default flip to `""` | The entry rule becomes `DefaultFlip("Text", "text", "")`, so `ApplyDefaults` emits `text = ""` when the node leaves it unset or blank. `CheckRequiredInputs` accepts a required input whose value is the literal its entry's R2 rule emitted, because `IsBound` treats a blank literal as unbound (R-1) |
| D-2 | A non-trigger drops `canStartWorkflow` | In `ApplyCustomProperties`, an entry with no `CanStartWorkflow` rule no longer raises `ELS3-ACTIVITY-REMOVED-INPUT-BOUND`; the flag is left on the Elsa 3 node for start resolution and emitted nowhere. The graph builder's start-trigger check (spec 238) skips such nodes, because a non-trigger never fires (R-2). Trigger entries are unchanged |
| D-3 | The coverage test pins the triggers | The Elsa 3 triggers of the surface are Cron, Event, HttpEndpoint, StartAt and Timer. Every one with a counterpart declares a `CanStartWorkflow` rule, and no other entry does, so D-2 can never drop a trigger's flag (R-2) |
| D-4 | `ObjectDictionary` is JSON | Added to the JSON-only alias list in `CheckContract` (R-3) |
| D-5 | Value-flow lowering normalizes the declared Elsa 3 type first | `ReadType` maps the Elsa 3 declared alias to the Elsa 4 type shape normalization has already put the value in: `HttpStatusCode` → `Int32` (R5 narrowed the name to an integer), `HttpHeaders` → dictionary of `String[]`, `ObjectDictionary` → dictionary of `Object`, and Elsa 3's generic list syntax (`List<T>`, `IList<T>`, `ICollection<T>`, `IEnumerable<T>`) → list of `T`. `TypesMatch` lets an untyped element match when the collection kinds agree (R-4) |

## Project Structure

### Documentation (this feature)

```text
specs/248-elsa3-faithful-fixes/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/requirements.md
└── tasks.md
```

No `contracts/`: no interface the project exposes changes.

### Source Code (repository root)

```text
Extensions/Elsa3/src/Mapping/
├── ActivityMap/Elsa3BuiltInActivityMap.Entries.cs   # D-1 WriteLine rule
├── Conversion/Elsa3ActivityNormalizer.cs            # D-1 required check, D-2 flag, D-4 alias list
├── Conversion/Elsa3FlowchartGraphBuilder.cs         # D-2 start-trigger check
├── Services/Elsa3ValueFlowLowerer.cs                # D-5
└── README.md
Extensions/Elsa3/tests/Mapping/Tests/
├── Elsa3FaithfulFixesTests.cs                       # one test per row, plus the kept refusals
├── Elsa3ActivityMapCoverageTests.cs                 # D-3
└── Elsa3ConversionDiagnosticsTests.cs               # the required-input case no longer uses WriteLine
specs/236-elsa3-activity-map/member-map.md           # rows 1 and 2
```

**Structure Decision**: existing projects only.

## Complexity Tracking

None.

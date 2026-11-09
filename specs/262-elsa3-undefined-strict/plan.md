# Implementation Plan: Elsa 3 converter: undefined results and strict mode

**Branch**: `agent/claude/3012-elsa3-undefined-strict` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/262-elsa3-undefined-strict/spec.md`

## Summary

All changes are in the converter's JavaScript path in `Elsa3ExpressionRewriter`:
- In Expression mode, every place a value leaves the converted expression becomes `(<expr>) ?? null` (R-1, R-2).
- The analyzer refuses writes to undeclared names, top-level `this`, top-level `arguments`, and writes that rely on
  `arguments` aliasing (R-4, R-6).
- The rewritten source is parsed in strict mode, exactly as Elsa 4 will see it, and refused if that parse fails (R-3).
- A converted RunJavaScript body whose last line is a comment ends with a newline (R-5).

Every new refusal uses the existing code `VF-IMP-007`. Elsa 4 is not touched. The Mapping README's migration story is
updated, and two Elsa 4 issues are drafted for the maintainer.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: Acornima 1.6.1 (already referenced by `Elsa3.Mapping`); none added

**Storage**: N/A

**Testing**: xunit only (no FluentAssertions). Tests live in `Extensions/Elsa3/tests/Mapping/Tests`, use
`ConversionServices` with `RealActivityCatalog`, and use `PortableJavaScript` to evaluate converted expressions in the
real Elsa 4 evaluator.

**Target Platform**: library (`Elsa3.Mapping`)

**Project Type**: single library and its test project

**Performance Goals**: N/A. One extra parse of each converted source at conversion time.

**Constraints**:
- Faithful or refuse.
- Converter only (ADR 0083 unchanged).
- Build and test only the touched projects (shared machine).

**Scale/Scope**: about 120 production lines in one file, two new test classes, and expected-source updates in the
existing JavaScript tests.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 stays behind the import boundary | PASS | Every production change is in `Extensions/Elsa3/src/Mapping` |
| §E2.2 Runtime takes no Design reference | PASS | No project reference changes |
| §E5 versioning | PASS | No public API change. `Elsa3ExpressionRewriter` keeps its signatures; only private members and the private analyzer change |
| §2.6.2 replacement contracts | PASS | None added or changed |
| §2.17 duplication beats dependency | PASS | Elsa 4's grammar rule (`JavaScriptBindingSyntax`, internal) is mirrored in two lines instead of taking a reference (R-3) |
| §2.24.2 sanctioned patterns | PASS | Existing patterns only: syntax-tree analysis with parser-range source edits, as in specs 236 and 247 |
| §2.22 / §2.22.1 docs and extension points | ACTION | Mapping README, "Migrating Elsa 3 JavaScript". No extension point changes, so `EXTENSION_POINTS.md` and the generated maps are unchanged |
| §2.23.1–§2.23.3 tests and visibility | PASS | Equivalence tests in the real Elsa 4 evaluator for every `?? null` case, a refusal test per refused construct, and a conversion test per counterpart that still converts. No new types |
| §2.23.5 exception boundaries | PASS | Acornima's `ParseErrorException` from the strict parse is caught and turned into a refusal, like today's sloppy parse. Nothing new throws on input |
| ADR 0083 (Proposed) | PASS | Not changed: an `undefined` binding result stays an error in Elsa 4; the converter avoids producing one |

## Decisions

| # | Decision | Detail |
|---|---|---|
| D-1 | `(<expr>) ?? null` where the result leaves | Expression mode only. Applied to the single expression, the trailing completion expression and every top-level `return` argument. A bare `return;` stays `return null;`, and `return` inside a declared function is unchanged (R-1) |
| D-2 | Edit order | The wrap's insertions are added after the analyzer's edits, so with the stable descending sort, a replacement at the same offset applies first (R-2) |
| D-3 | Strict parse of the output | Expression mode: `ParseExpression(source, strict: true)`. Script mode: `ParseScript("(() => { " + source + " })()", strict: true)` with top-level `return` allowed. A failure gives the `VF-IMP-007` strict-mode refusal with the parser's description (R-3) |
| D-4 | Undeclared writes | Assignment (any operator, including destructuring), update and `for…in`/`for…of` heads whose target name `IsDeclared` does not find are refused. This runs after the ambient-mutation check (R-4) |
| D-5 | `this` / `arguments` | The analyzer tracks the enclosing non-arrow function (class bodies count). It refuses `this` and undeclared `arguments` outside one. Inside one, it refuses writes to `arguments[…]` or to the function's parameters when the function, including its arrow functions, reads `arguments` (R-6) |
| D-6 | Script trailing comment | In Script mode, append `\n` to the output when its last line contains `//` (R-5) |
| D-7 | One code | Every new refusal is `VF-IMP-007`, with the contract's guidance text (R-7) |
| D-8 | Spec widening | FR-013 also covers undeclared `arguments` outside every function. This is a provable case under clarification Q2; the spec is amended in this unit (R-6) |
| D-9 | Elsa 4 issues | Draft three issues and show them to the maintainer before filing them as `needs-triage`: the nested-`undefined` key loss in the JSON result, the one-line script wrapper, and `null` into a non-nullable input (R-8). The README links the first and third once they are filed (FR-014) |
| D-10 | Typed `null` | Research only (R-8), no tests. Elsa 3 used `default(T)` for value types; Elsa 4 refuses with `VF-ACT-004` at run time. This applies to every `null` binding and depends on the data, so it goes in the README and the D-9 issue, not in a converter rule |

## Project Structure

### Documentation (this feature)

```text
specs/262-elsa3-undefined-strict/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/javascript-strict-and-undefined.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
Extensions/Elsa3/src/Mapping/
├── Services/Elsa3ExpressionRewriter.cs     # D-1 to D-7
└── README.md                               # migration story (FR-010)
Extensions/Elsa3/tests/Mapping/Tests/
├── Elsa3UndefinedResultTests.cs            # new: US1 equivalence in the Elsa 4 evaluator
├── Elsa3StrictModeTests.cs                 # new: US2–US4 refusals, the counterparts that convert, and the trailing comment
├── Elsa3TopLevelReturnTests.cs             # expected sources gain ?? null
├── Elsa3JavaScriptConversionTests.cs       # expected sources gain ?? null
├── Elsa3GeneratedAccessorTests.cs          # expected sources gain ?? null
├── Elsa3JavaScriptHostFunctionTests.cs     # expected sources gain ?? null
└── Elsa3MemoryReferenceImportTests.cs      # expected sources gain ?? null where they are JavaScript
```

**Structure Decision**: existing projects only.

## Complexity Tracking

None.

# Implementation Plan: Scripts Read Variables By Name

**Branch**: `claude/3052-s4-scripts-read-by-name-60c01a` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1222-scripts-read-by-name/spec.md`

**Status**: **Provisional** (initiative #3052). This plan tests ADR 0089, with the minimal D-7 amendment, against the
code. No production code lands until the initiative branch lands; the gates below are enforced in the implementing PRs
on `main`.

## Summary

Scripts read workflow variables by name in JavaScript and Liquid. A **literal** name (`variables.X`,
`variables['lit']`, `getVariable('lit')`) is resolved at publish to its nearest declaring scope and pinned as a spec 095
`Variable` declared parameter, exposed at run time under a `variables` root distinct from `args`. A **computed** name
(`variables[expr]`, `getVariable(expr)`) or an opaque host-function read falls back to a **run-time lookup**: publish
reports which variables it could read, and each actual read is recorded, protection-checked and added to the step's
input snapshot; a name that matches nothing reads `undefined` (nil in Liquid) with a warning and the read marked absent
(spec 1216 FR-025–FR-027). The fallback reuses today's ambient visible-variable projection, confined to expressions that
need it. The Elsa 3 converter emits `variables.X` for literal reads, keeps computed `getVariable(expr)` (served by the
fallback) instead of refusing it, drops the `args` rewrite for variable reads (fixing #3296), gives the `RunJavaScript`
`arguments` expression its real type (fixing #3287), and emits unwrapped statement bodies (#3010). This is decision D-7
(minimal amendment, Sipke 6 Nov on #3420); it supersedes findings F-1–F-3.

## Technical Context

**Language/Version**: C# (.NET, per repo), JavaScript (Jint engine), Liquid (Fluid).

**Primary Dependencies**: Jint (isolated JS engine); the spec 095 value-flow model (declared parameters, `Variable`
binding); ADR 0092 variable session (PR #3411); Studio `ScopedVariableResolver`; the Elsa 3 converter (spec 247).

**Storage**: N/A for reads (values come from the pinned input snapshot / external payload store as today).

**Testing**: xUnit only (no FluentAssertions — constitutionally pinned). Architecture guard tests in
`Essentials/tests/Architecture/Tests/`. Converter tests in `Extensions/Elsa3/tests/Mapping/Tests/`.

**Target Platform**: Elsa 4 workflow engine (unreleased; no production script data → no backward-compatibility cost).

**Project Type**: Modular workflow framework (library + modules + converter extension).

**Performance Goals**: Literal-only expressions materialize only the variables they name (no "load all visible"); no
regression vs today's declared-parameter path. Expressions with computed or opaque reads pay the fallback's cost (the
conservative candidate set made readable before evaluation) and no more than today's ambient path.

**Constraints**: Jint reads are synchronous; variable values may be stored externally and loaded async, so read values
— pinned values and the fallback's candidate set — must be materialized before evaluation. Runtime must not take a Design reference (§E2.2); Elsa 3 stays behind the
import boundary (§E2.7).

**Scale/Scope**: The four tracked issues (#3204, #3296, #3287 arguments part) plus the read model; converter + runtime
+ publish validation + spec 095 contract edit.

## Constitution Check

*GATE: applied in the implementing PRs on `main` (provisional spec: no code lands until the branch lands).*

Relevant gates (from `.specify/memory/constitution.md`, `constitution-framework.md`, and `review-checklist.md`):

- **§E2.2 Runtime ⊄ Design** — the publish-time read-set resolver reuses the runtime scoped reader
  (`TryReadScopedVariableValue`) and Studio's `ScopedVariableResolver` on the Design side; the runtime path must not
  add a Design reference (`ArchitectureGuardTests.Runtime_projects_do_not_add_design_references`). Plan keeps
  resolution split: Studio/publish (Design) resolves literal names and computes the conservative report; runtime reads
  pinned values and runs the fallback lookup over its own visible-variable frames (no Design types).
- **§E2.7 Elsa 3 import-only** — the converter changes stay in `Extensions/Elsa3/*`, behind the import boundary
  (`Elsa3MigrationBoundaryTests`). No runtime project references the Elsa 3 compatibility projects.
- **§2.5 / §2.23.1 Feature composition** — any new/changed service registers and resolves
  (`FeatureCompositionTests`). Unit tests required for logic-bearing services and registration
  (`elsa-add-unit-tests`).
- **§2.24.2 Sanctioned patterns** — reuse existing patterns (declared-parameter materialization, scoped resolver,
  the existing ambient visible-variable projection as the fallback, converter rewriter visitor); no new pattern
  introduced.
- **No FluentAssertions** — xUnit only in all new/changed tests.
- **§E7 Test tiers** — new tests placed in the correct CI tier.

No violations anticipated; **Complexity Tracking** below is empty.

## Project Structure

### Documentation (this feature)

```text
specs/1222-scripts-read-by-name/
├── plan.md              # This file
├── spec.md              # Feature spec (provisional)
├── research.md          # Phase 0 — decisions (OQ-1, OQ-2, OQ-3, selective load, run-time fallback)
├── data-model.md        # Phase 1 — entities
├── quickstart.md        # Phase 1 — how to validate
├── contracts/
│   ├── variables-read-surface.md   # The `variables` root + publish read-set contract
│   └── converter-reads.md          # Converter emission + refusal contract
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root)

```text
Essentials/src/Expressions/JavaScript/Jint/Services/
├── IsolatedJintEngine.cs              # Build `variables` from pinned params, backed by the fallback lookup for unpinned names;
│                                      # keep `getVariable` as the fallback accessor; remove generated get<Name>() (decision 6);
│                                      # record each fallback read; missing name → undefined + warning
└── JintPortableJavaScriptEvaluator.cs # Pass pinned values + the fallback candidate set; return the recorded reads

Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/
├── RuntimePortableExpressionEvaluator.cs  # Remove regex read-detection; keep the visible-variable projection only for
│                                          # expressions marked as needing the fallback; per-read protection check
└── RuntimeActivityInputMaterializer.cs    # Selective load of named variables into pinned params; add recorded fallback
                                           # reads to the step's input snapshot

Essentials/src/Expressions/Liquid/...      # `variables` root over pinned params + fallback; missing computed name → nil + warning

Essentials/src/Workflows/Design/...        # Publish-time read-set resolver: parse source, resolve literal names nearest scope
│                                           # (reuse ScopedVariableResolver + iteration scope), mark computed/opaque expressions
│                                           # for the fallback with a conservative read report; refuse unresolvable literal /
│                                           # transient literal reads; warn on shadow change

Extensions/Elsa3/src/Mapping/
├── Services/Elsa3ExpressionRewriter.cs    # Emit variables.X for literal reads; keep getVariable(expr)/variables[expr] for
│                                          # computed reads; refuse on author `args` collision (#3296)
├── Services/Elsa3ValueFlowLowerer.cs      # arguments type Object→real (#3287); no IIFE for body (#3010); faithfulness check
└── Internal/Elsa3JavaScriptNames.cs       # Naming rule for converted get<Name>() (decision 6)

specs/095-value-flow-redesign/contracts/expression-and-import-contract.md
                                           # §1 `variables` root over pinned params + fallback marker and recorded reads;
                                           # §6 importer keeps variables.X and computed getVariable(expr)

Essentials/tests/Architecture/Tests/, Essentials/tests/..., Extensions/Elsa3/tests/Mapping/Tests/
                                           # Unit + architecture + converter tests
```

**Structure Decision**: Single modular framework; changes split across Expressions/JavaScript and Liquid (runtime
surface and fallback),
Workflows.Runtime (materialization), Workflows.Design (publish-time resolution), and the Elsa3 converter extension,
keeping the Runtime⊄Design and Elsa-3-import-only seams intact.

## Complexity Tracking

*No constitution violations — none to justify.*

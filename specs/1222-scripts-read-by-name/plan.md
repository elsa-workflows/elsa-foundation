# Implementation Plan: Scripts Read Variables By Name

**Branch**: `claude/3052-s4-scripts-read-by-name-60c01a` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1222-scripts-read-by-name/spec.md`

**Status**: **Provisional** (initiative #3052). This plan tests ADR 0089's read model against the code. No production
code lands until the initiative branch lands; the gates below are enforced in the implementing PRs on `main`.

## Summary

Scripts read workflow variables by **direct name** (`variables.X`, `variables['lit']`, `getVariable('lit')`) in
JavaScript and Liquid. Publish resolves each name to its nearest declaring scope and pins it as a spec 095 `Variable`
declared parameter, exposed at run time under a `variables` root distinct from `args`; the script sees only pinned
values (no run-time name lookup). **Computed** names are refused at publish and deferred. The Elsa 3 converter emits
`variables.X`, drops the `args` rewrite for variable reads (fixing #3296), gives the `RunJavaScript` `arguments`
expression its real type (fixing #3287), and emits unwrapped statement bodies (#3010). This implements the original
ADR 0089; the #3420 amendment is reduced accordingly (finding F-1).

## Technical Context

**Language/Version**: C# (.NET, per repo), JavaScript (Jint engine), Liquid (Fluid).

**Primary Dependencies**: Jint (isolated JS engine); the spec 095 value-flow model (declared parameters, `Variable`
binding); ADR 0092 variable session (PR #3411); Studio `ScopedVariableResolver`; the Elsa 3 converter (spec 247).

**Storage**: N/A for reads (values come from the pinned input snapshot / external payload store as today).

**Testing**: xUnit only (no FluentAssertions — constitutionally pinned). Architecture guard tests in
`Essentials/tests/Architecture/Tests/`. Converter tests in `Extensions/Elsa3/tests/Mapping/Tests/`.

**Target Platform**: Elsa 4 workflow engine (unreleased; no production script data → no backward-compatibility cost).

**Project Type**: Modular workflow framework (library + modules + converter extension).

**Performance Goals**: Reads materialize only the variables a script names (no "load all visible"); no regression vs
today's declared-parameter path.

**Constraints**: Jint reads are synchronous; variable values may be stored externally and loaded async, so read values
must be materialized before evaluation. Runtime must not take a Design reference (§E2.2); Elsa 3 stays behind the
import boundary (§E2.7).

**Scale/Scope**: The four tracked issues (#3204, #3296, #3287 arguments part) plus the read model; converter + runtime
+ publish validation + spec 095 contract edit.

## Constitution Check

*GATE: applied in the implementing PRs on `main` (provisional spec: no code lands until the branch lands).*

Relevant gates (from `.specify/memory/constitution.md`, `constitution-framework.md`, and `review-checklist.md`):

- **§E2.2 Runtime ⊄ Design** — the publish-time read-set resolver reuses the runtime scoped reader
  (`TryReadScopedVariableValue`) and Studio's `ScopedVariableResolver` on the Design side; the runtime path must not
  add a Design reference (`ArchitectureGuardTests.Runtime_projects_do_not_add_design_references`). Plan keeps
  resolution split: Studio/publish (Design) resolves names; runtime only reads pinned values.
- **§E2.7 Elsa 3 import-only** — the converter changes stay in `Extensions/Elsa3/*`, behind the import boundary
  (`Elsa3MigrationBoundaryTests`). No runtime project references the Elsa 3 compatibility projects.
- **§2.5 / §2.23.1 Feature composition** — any new/changed service registers and resolves
  (`FeatureCompositionTests`). Unit tests required for logic-bearing services and registration
  (`elsa-add-unit-tests`).
- **§2.24.2 Sanctioned patterns** — reuse existing patterns (declared-parameter materialization, scoped resolver,
  converter rewriter visitor); no new pattern introduced.
- **No FluentAssertions** — xUnit only in all new/changed tests.
- **§E7 Test tiers** — new tests placed in the correct CI tier.

No violations anticipated; **Complexity Tracking** below is empty.

## Project Structure

### Documentation (this feature)

```text
specs/1222-scripts-read-by-name/
├── plan.md              # This file
├── spec.md              # Feature spec (provisional)
├── research.md          # Phase 0 — decisions (OQ-1, OQ-2, selective load, computed refusal)
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
├── IsolatedJintEngine.cs              # Build `variables` from pinned params; remove ambient getVariable/get<Name> runtime surface (decision 6)
└── JintPortableJavaScriptEvaluator.cs # Pass the `variables` root from pinned values

Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/
├── RuntimePortableExpressionEvaluator.cs  # Remove regex read-detection + ambient projection; feed `variables` from pinned params
└── RuntimeActivityInputMaterializer.cs    # Selective load of named variables into pinned params

Essentials/src/Workflows/Design/...        # Publish-time read-set resolver: parse source, resolve nearest scope
│                                           # (reuse ScopedVariableResolver + iteration scope), refuse computed/unresolvable/transient, warn on shadow change

Extensions/Elsa3/src/Mapping/
├── Services/Elsa3ExpressionRewriter.cs    # Emit variables.X; refuse on author `args` collision (#3296)
├── Services/Elsa3ValueFlowLowerer.cs      # arguments type Object→real (#3287); no IIFE for body (#3010); faithfulness check
└── Internal/Elsa3JavaScriptNames.cs       # Naming rule for converted get<Name>() (decision 6)

specs/095-value-flow-redesign/contracts/expression-and-import-contract.md
                                           # §1 add `variables` root surface over pinned params; §6 importer keeps variables.X

Essentials/tests/Architecture/Tests/, Essentials/tests/..., Extensions/Elsa3/tests/Mapping/Tests/
                                           # Unit + architecture + converter tests
```

**Structure Decision**: Single modular framework; changes split across Expressions/JavaScript (runtime surface),
Workflows.Runtime (materialization), Workflows.Design (publish-time resolution), and the Elsa3 converter extension,
keeping the Runtime⊄Design and Elsa-3-import-only seams intact.

## Complexity Tracking

*No constitution violations — none to justify.*

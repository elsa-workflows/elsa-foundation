# Implementation Plan: Scripts Read Variables By Name

**Branch**: `claude/3052-s4-scripts-read-by-name-60c01a` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/1222-scripts-read-by-name/spec.md`

**Status**: follows the spec, approved 2026-11-17 (initiative #3052). This plan applies ADR 0089 as
accepted and merged (`0a381a812a`), with ADR 0090 and ADR 0092 (both accepted). It is
implemented on the initiative branch (D-18); the gates below are enforced in those implementation PRs.

## Summary

Scripts read workflow variables by name in JavaScript and Liquid. A **literal** name (`variables.X`,
`variables['lit']`, `getVariable('lit')`) is resolved at publish to its nearest declaring scope and pinned as a spec 095
`Variable` declared parameter, exposed at run time under a `variables` root distinct from `args`. A **computed** name
(`variables[expr]`, `getVariable(expr)`) or an opaque host-function read falls back to a **run-time lookup**: publish
reports which variables it could read, and each actual read is recorded, protection-checked and added to the step's
input snapshot; a name that matches nothing reads `undefined` (nil in Liquid) with a warning and the read marked absent
(spec 1216 FR-025–FR-027). The fallback extends today's ambient visible-variable projection (adding each candidate's
declaring scope and a transient marker), confined to expressions that
need it. The Elsa 3 converter emits `variables.X` for literal reads, keeps computed `getVariable(expr)` (served by the
fallback) instead of refusing it, drops the `args` rewrite for variable reads (fixing #3296), and emits unwrapped
statement bodies (#3010). The `arguments` type fix (#3287) already landed with #4315. A `RunJavaScript` body gets the
`variables` root and `getVariable`, read through its variable session (ADR 0090 decision 2, ADR 0092) with every read
recorded in the read set; spec 1228 (S-8) builds on that surface. Loop items and indexes resolve as variables declared
by the loop node (spec Q1). This is decision D-7, as ADR 0089 records it.

## Technical Context

**Language/Version**: C# (.NET, per repo), JavaScript (Jint engine), Liquid (Fluid).

**Primary Dependencies**: Jint (isolated JS engine); the spec 095 value-flow model (declared parameters, `Variable`
binding); ADR 0090 / ADR 0092 variable session; Studio `ScopedVariableResolver`; the Elsa 3 converter (spec 247).

**Dependencies on other specs**: S-10 (spec 1221, PR #4316) provides the read-set types (`InputReadSet`,
`SessionReads`); S-7 (ADR 0090) builds the variable session, its read path, its session reads and its writes, which
FR-033–FR-035 need (spec 1221 T015 and T016 moved to S-7 on 2026-11-17); spec 1216 (S-2) FR-025–FR-027 is not yet on the initiative branch.

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

**Scale/Scope**: The tracked issues (#3204, #3296) plus the read model; converter + runtime + publish validation +
spec 095 contract edit + the `RunJavaScript` body read surface that spec 1228 builds on.

## Constitution Check

*GATE: applied in the implementation PRs on the initiative branch (D-18).*

Relevant gates (from `.specify/memory/constitution.md`, `constitution-framework.md`, and `review-checklist.md`):

- **§E2.2 Runtime ⊄ Design** — the publish-time read-set resolver reuses Studio's `ScopedVariableResolver` on the
  Design side, with loop iteration declarations from a new default-empty structure-handler member implemented in the
  loop module (`Elsa.Activities.ControlFlow`, which already references both Design and Runtime core packages); the runtime path must not
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
├── spec.md              # Feature spec
├── research.md          # Phase 0 — decisions (surface flag, loop values, transient fault, selective load, run-time fallback)
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

Essentials/src/Activities/Scripting/Activities/RunJavaScript.cs
                                       # Today the body gets only frozen `args`; give it `variables` and `getVariable`,
                                       # read through the step's variable session (ADR 0090 decision 2), each read
                                       # recorded in the read set with its protection policy (FR-033–FR-035, for S-8)

Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/
├── RuntimePortableExpressionEvaluator.cs  # Remove regex read-detection; keep the visible-variable projection only for
│                                          # expressions marked as needing the fallback; per-read protection check
├── RuntimeContainerScopeService.cs        # Ambient projection gains declaring scope + transient marker per candidate
├── RuntimeActivityInputSnapshotMaterializer.cs, WorkflowIntrinsicExecutor.cs
│                                          # Where the projection is wired today; selective load of named variables;
│                                          # add recorded fallback reads to the step's input snapshot
└── RuntimeLoopIterationFrameFactory.cs    # Pinned parameters whose declaring scope is a loop node read the current pass

Essentials/src/Expressions/Liquid/...      # `variables` root over pinned params + fallback; missing computed name → nil + warning

Essentials/src/Workflows/Design/...        # Publish-time read-set resolver: parse source, resolve literal names nearest scope
│                                           # (reuse ScopedVariableResolver + add the loop's iteration declarations as the
│                                           # innermost scope, Q1), mark computed/opaque expressions
│                                           # for the fallback with a conservative read report; refuse unresolvable literal /
│                                           # transient literal reads; warn on shadow change

Extensions/Elsa3/src/Mapping/
├── Services/Elsa3ExpressionRewriter.cs    # Emit variables.X for literal reads; keep getVariable(expr)/variables[expr] for
│                                          # computed reads; refuse on author `args` collision (#3296)
├── Services/Elsa3ValueFlowLowerer.cs      # faithfulness check (the #3010 IIFE is emitted in Elsa3ExpressionRewriter)
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

# Implementation Plan: Elsa 3 activity map

**Branch**: `236-elsa3-activity-map` | **Date**: 2026-10-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/236-elsa3-activity-map/spec.md` and the decision record
[member-map.md](member-map.md)

> **Unblocked (2026-10-15).** The engine gap from research R-1 ([#2750](https://github.com/elsa-workflows/elsa-foundation/issues/2750))
> is resolved by spec 237's `Complete` activity (PR #2791, merged as `4764f246a`). The branch is merged with that
> main. A converted reusable workflow emits `Complete` for Elsa 3 `Complete`/`End`; the decisions are D-1 to D-10
> below.
>
> Imported reusable activities are still never published, so they do not run as graphs yet (research R-10). That is
> split out to [#2796](https://github.com/elsa-workflows/elsa-foundation/issues/2796) and does not block this spec.

## Summary

**What.**
- The Elsa 3 converter gains a complete Elsa 3 → Elsa 4 activity map: types, versions, members, ports, outcomes.
- Every member is classified by the rules in member-map.md, with stable `ELS3-ACTIVITY-*` errors and
  `ELS3-BEHAVIOUR-*` warnings in place of exceptions and silent drops.
- One public conversion operation is used by the single-definition import, the collection materializer, #2705 and
  spec 235.

**How.**
- The map is C# data in `Elsa3.Mapping`, plus named node-rewrite strategies.
- One scoped resolver serves both catalog lookup sites. It resolves in this order: plan replacement → built-in map →
  `ReplacesActivityType` index → verbatim key, and takes the highest valid-SemVer catalog version.
- Conversion runs clone → normalize → collect + lower → map, on both paths.

**Two findings made during planning that widen the work.**
- Containers must get their target's **native** structure, because the current opaque Elsa 3 structure never runs
  (research R-6).
- Reusable workflows need `outputMappings` and output variables, plus wrapper output propagation (FR-004e).

**Two more, found when resuming on 2026-10-15.**
- A reusable workflow's `Complete`/`End` become spec 237 `Complete` nodes in a schema-2 graph whose contract
  declares the outcomes (D-1 to D-7).
- The collection import never publishes, so imported reusables do not run (R-10, split out to #2796). This spec proves
  its reusable output by provider validation and compilation in tests.

**One small Essentials change.** A generic `[ReplacesActivityType]` attribute, recorded by the CLR scanner as a design
facet (research R-2).

## Technical Context

**Language/Version**: C# / .NET (repo SDK, `global.json`), nullable enabled

**Primary Dependencies**:
- `System.Text.Json`
- Acornima (JavaScript AST, already used by `Elsa3ExpressionRewriter`)
- `Elsa.Activities.Design.Persistence.Core` (lookup, version store)
- `Elsa.Workflows.Design.Core`
- `Elsa.Activities.Runtime.Core` (attribute)
- `Elsa.Activities.Design.Reconciliation.Clr` (scanner)
- No new external package.

**Storage**: none new. Catalog facets use the existing `DesignFacetsSource` JSON column. No schema or EF migration.

**Testing**:
- xunit only (constitutional; no FluentAssertions).
- `Extensions/Elsa3/tests/Mapping/Tests` gets the coverage fixture, a real-catalog fixture built with the CLR scanner
  over the Elsa 4 activity assemblies, and theory tests per diagnostic code.
- The real catalog includes the `Elsa.Activities.Graph.Runtime` assembly, so `Complete` resolves like any CLR activity.
- Converted reusable output goes through `GraphActivityProvider.ValidateAsync` and `CompileAsync` with its derived
  contract (R-10). This needs a test-only reference from `Elsa3.Mapping.Tests` to `Elsa.Activities.Graph.Design`.
  The production Mapping project gains no Graph reference: it emits the type key and resolves the version from the
  catalog.
- `Essentials/tests/Activities/Design/Tests` covers the scanner facet.
- The EF import tests cover the collection path.

**Target Platform**: server library (Elsa 4 Design side). No Runtime project changes.

**Project Type**: libraries inside the existing modular solution.

**Performance Goals**: one catalog version read (`ListAsync`) per conversion scope for the alias index, and one
version pick per distinct Elsa 3 type (cached). Conversion stays linear in node count.

**Constraints**:
- Deterministic output (FR-020).
- No exception from input (FR-012).
- The analyzer and the materializer helpers are not restructured (FR-019).
- §E2.7: no Elsa 3 knowledge in Essentials.
- `MaxNestingDepth` 14 is unchanged.

**Scale/Scope**:
- 67 Elsa 3 activity classes in five modules, plus SetOutput.
- Roughly 130 member rules.
- 8 node-rewrite strategies.
- 14 error codes and 12 warning codes.
- JTest corpus: 36 definitions.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md`,
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.7 Elsa 3 behind the import boundary (`Elsa3MigrationBoundaryTests`, runtime guard) | PASS | Essentials gains only a generic attribute and facet with no Elsa 3 name or reference. All Elsa 3 knowledge stays in `Elsa3.*` (R-2) |
| §E2.2 Runtime takes no Design reference | PASS | No Runtime project changes. `Elsa.Activities.Runtime.Core` gains an attribute only |
| §E2.6 published artifact always runs | PASS, improves | Native structures replace the opaque kind, so converted containers execute (R-6). Imported reusables are not published artifacts at all today; #2796 owns that (R-10) |
| §E2.8 catalog as source of truth; Model X | PASS | The alias is read from the catalog, never from assemblies. Hash discipline is respected: alias changes need a version bump, as documented. No operational sibling |
| §E2.9 (provisional) `ActivityNode` gets no generic child collection | PASS | Children go into native structure payloads, as Elsa 4 authoring does |
| §E5 versioning | PASS | No Line A project touched. Additive record members only |
| §E6 naming R1–R8 | PASS | `ReplacesActivityTypeAttribute`, `Elsa3ActivityResolver` (Resolver), `Elsa3ActivityMapEntry`, `Elsa3NodeRewriteKind`, `IElsa3DefinitionConverter`. Nothing exceeds 4 components after `Elsa3`, and no banned words |
| §2.1 Core rules | PASS | No `.Core` gains implementation. The attribute is a contract-shaped type |
| §2.5 / §2.5.1 features | PASS | `Elsa3MappingFeature.ConfigureServices` stays virtual. Converter and resolver are scoped; the map is a singleton because it holds a static table |
| §2.6.2 replacement contracts | PASS | `IElsa3DefinitionConverter` and `IElsa3ActivityMap` are single-implementation contracts. Duplicate registration is diagnosed by the existing DI diagnostics (§2.11) |
| §2.17 duplication beats dependency | PASS | The resolver replaces two duplicated lookups inside one domain. No new shared helper |
| §2.22 / §2.22.1 docs and extension points | ACTION | `Extensions/Elsa3/src/Mapping/EXTENSION_POINTS.md` lists the new contracts. The Clr `README.md`/`EXTENSION_POINTS.md` documents the facet. `docs/glossary/elsa.md` gets "replaced activity type" |
| §2.23.1–§2.23.3 tests and visibility | PASS | Behaviour tests per rule and per code; registration covered by the composition test; implementations internal sealed with InternalsVisibleTo the test assembly |
| §2.23.5 exception boundaries | PASS | `JsonException` and catalog `EntityNotFoundException` are caught at the resolver and turned into diagnostics. Only cancellation escapes |
| §2.24 (draft) sanctioned patterns | PASS | Strategy (node rewrites), replacement contracts, feature registration. No new pattern |
| §2.21.1 refactor preserves behaviour | ACTION | Existing Elsa 3 tests use stub lookups that answer any key. They are rewritten to register real keys, a stated test-setup change. Tests asserting the opaque structure (`Elsa3ImportedActivityStructureTests`) are replaced, because the behaviour they describe is removed (R-6); the PR names them |

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/236-elsa3-activity-map/
├── spec.md
├── member-map.md            # decision record, every member classified
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── converter-api.md
│   ├── diagnostic-codes.md
│   └── replaces-activity-type.md
├── checklists/requirements.md
└── tasks.md                 # /speckit-tasks
```

### Source Code

```text
Essentials/src/Activities/Runtime/Core/Attributes/
└── ReplacesActivityTypeAttribute.cs                        # new (R-2)
Essentials/src/Activities/Design/Reconciliation/Clr/
├── Services/ClrAssemblyScanner.cs                          # + elsa.replaced-activity-types facet
├── README.md, EXTENSION_POINTS.md                          # facet documented
Essentials/tests/Activities/Design/Tests/Unit/ClrAssemblyScannerTests.cs   # facet cases, no-facet hash stability

Extensions/Elsa3/src/Models/
├── Elsa3ActivityDiagnosticCodes.cs                         # new (R-3)
Extensions/Elsa3/src/Mapping/
├── ActivityMap/
│   ├── Elsa3ActivityMapEntry.cs, Elsa3ActivityTarget.cs, Elsa3MemberRule.cs, Elsa3OutcomeRule.cs
│   ├── Elsa3BuiltInActivityMap.cs                          # the table (member-map.md as data)
│   ├── IElsa3ActivityMap.cs
│   └── Rewrites/                                           # IElsa3NodeRewrite strategies (data-model §6)
├── Resolution/
│   ├── Elsa3ActivityResolver.cs                            # R-5, both lookup sites
│   └── Elsa3ReplacedActivityTypeIndex.cs                   # R-2 index
├── Conversion/
│   ├── IElsa3DefinitionConverter.cs, IElsa3NodeConverter.cs
│   ├── Elsa3DefinitionConverter.cs                         # clone → normalize → lower → map
│   ├── Elsa3ActivityNormalizer.cs                          # member rules R1–R9, W, ports → slots
│   ├── Elsa3NativeStructureBuilder.cs                      # R-6, from the target's structure facet
│   └── Models (Elsa3ConversionRequest, Elsa3ConvertedDefinition, Elsa3GraphOutputMapping)
├── Mappings/Elsa3ActivityToState.cs                        # uses resolver + native structure; no throws
├── Services/Elsa3MemoryReferenceGraph.cs                   # uses resolver
├── Services/Elsa3ValueFlowLowerer.cs                       # metadata dictionary instead of import input (R-8)
├── Services/Elsa3WorkflowDefinitionImporter.cs             # calls the converter
├── Services/Elsa3ReusableActivityImportMaterializer.cs     # calls the converter; schema 2, manifest outputMappings + outcomeMappings, wrapper outputs (content only)
├── Conversion/Elsa3OutcomeKey.cs                           # D-4 outcome key rule
├── ActivityMap/Rewrites/Elsa3TerminalPlacementRewrite.cs   # D-1 to D-3, both branches
├── Elsa3MappingFeature.cs, EXTENSION_POINTS.md, README.md
Extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import/
├── Models/ReusableActivityImportPlan.cs                    # + Warnings on mutation and apply result (R-7)
├── Services/ReusableActivityCollectionImporter.cs          # carry warnings; native-structure walk for credential admission
├── Models/Elsa3ImportedActivityStructure.cs                # retired once nothing writes it (R-6)
└── Endpoints/Collections/Apply/Endpoint.cs                 # returns warnings

Extensions/Elsa3/tests/Mapping/Tests/
├── Fixtures/elsa3-activity-surface.json                    # test-owned Elsa 3 surface (R-4)
├── Support/RealActivityCatalog.cs                          # CLR scanner over Elsa 4 activity assemblies
├── Elsa3ActivityMapCoverageTests.cs                        # SC-001
├── Elsa3BuiltInConversionTests.cs                          # US1, SC-002
├── Elsa3ConversionDiagnosticsTests.cs                      # US2, SC-003 (theory per code)
├── Elsa3ReplacedActivityTypeTests.cs                       # US3
├── Elsa3CollectionLoweringTests.cs                         # US4, SC-004, SC-006 (compile level)
├── Elsa3ReusableTerminalTests.cs                           # D-1 to D-7, provider validate + compile
└── (existing tests: stub lookups replaced by real keys)
```

**Structure Decision**:
- All conversion code stays in `Elsa3.Mapping`. `Elsa3.Models` gets only the public code constants spec 235 needs.
- `Elsa3.Activities.Design.Import` gets only the additive `Warnings` members and the credential-walk change.
- The analyzer and the materializer's `Build*` helpers keep their shape; only their content changes (FR-019).
- The Essentials change is one attribute plus one facet in the scanner.

## Implementation order (for /speckit-tasks)

1. **Foundations.**
   - Diagnostic code constants.
   - Map model types.
   - Test-owned Elsa 3 surface fixture.
   - Real-catalog test support.
   - Coverage test, red at first.
2. **The built-in map table.** All entries from member-map.md, until the coverage test is green on both sides.
3. **Resolver and alias pieces.**
   - Resolver (catalog version pick, intrinsics, plan replacements).
   - Essentials attribute and scanner facet with tests.
   - Alias index.
4. **Normalizer, native structure builder and rewrite strategies.**
   - `TerminalPlacement`, both branches: the plain workflow emits `finish`; the reusable workflow emits `Complete`
     (D-1 to D-4). `BoundaryOutcomes` and the `done → done` outcome mapping are assembled per D-5 to D-7.
   - Wire `Elsa3ActivityToState` and `Elsa3MemoryReferenceGraph` to them, removing every input-caused throw.
5. **The converter (FR-015).** Single-definition import moves onto it; existing tests are migrated to real keys.
6. **Collection path.**
   - Lowering per item.
   - Replacements.
   - Warnings channel.
   - `outputMappings`, output variables and wrapper outputs.
   - Schema 2: `ProviderSchemaVersion = "2"`, manifest `outcomeMappings`.
   - Provider validate and compile proof for converted reusables (R-10). No publication; that is #2796.
   - Native-structure credential walk; retire the opaque structure.
7. **Docs.** EXTENSION_POINTS / README / glossary. Run the quickstart.

## Complete and End conversion decisions (maintainer interview, 2026-10-15)

| # | Decision | Rationale |
|---|---|---|
| D-1 | In a reusable workflow, an Elsa 3 `Complete`/`End` at **any depth** becomes one `Elsa.Activities.Graph.Runtime.Activities.Complete` node, input `outcome` = one literal reference key. Its version is resolved from the catalog like any CLR activity. Never `finish`. | Spec 237's contract. Like Elsa 3, it ends the composite from any depth, so the plain-workflow "root flowchart only" limit does not apply here. |
| D-2 | A `Complete` with **several outcomes**, or an outcome set by an expression → `ELS3-ACTIVITY-UNSUPPORTED-MODE`. No outcome, or a one-element list → one outcome. | Elsa 4 completes with one literal outcome; faithful or refuse. |
| D-3 | `End` in a reusable workflow → `Complete` with `outcome = "done"`. In a plain workflow it is unchanged: `finish` in the root flowchart, else `UNSUPPORTED-MODE`. | Elsa 3 `End` completes the composite with `Done`. |
| D-4 | Outcome keys: the contract **name** is the Elsa 3 string verbatim, so caller ports match. The **reference key** is the same string, except `"Done"` → `done`. Matching is ordinal. `"done"` used alongside `"Done"` → `UNSUPPORTED-MODE`. | No key format rule exists; `done` is Elsa 4's natural-outcome key. |
| D-5 | Every converted reusable workflow is **schema 2**. It declares `done`/`Done` emitted, with `outcomeMappings = [done → done]`. | A body that ends without reaching a `Complete` completes with `Done`, as in Elsa 3, and avoids `entry-completed-without-mapping`. |
| D-6 | A `Complete` naming an outcome the definition does not declare: the outcome is declared emitted, plus `ELS3-BEHAVIOUR-OUTCOME-UNDECLARED`. | Elsa 3 emitted it at run time. |
| D-7 | A declared outcome (other than `Done`) that no `Complete` reaches is **dropped** from the contract, plus `ELS3-BEHAVIOUR-OUTCOME-UNREACHED`. | Schema 2 rejects an emitted outcome with no source. A non-emitted outcome gets no caller port either (the publisher's `elsa.outcomes` facet keeps emitted only), so the warning makes the dead port visible. |
| D-8 | Break-based ideas are dropped. Research R-1's "Break → mapped boundary outcome" alternative is marked superseded. Elsa 3 `Break` still maps to `Break` with `BREAK-STOPS-AT-PARALLEL`. | `Complete` replaces the reason for it; spec 237 keeps `Break` unrelated. |
| D-9 | Import publication is split out to #2796. This spec proves reusable output with `GraphActivityProvider.ValidateAsync` + `CompileAsync` in tests. SC-006's run-time check follows #2796. | Keeps this spec in the Elsa 3 mapping context (R-10). |
| D-10 | The reserved `ELS3-ACTIVITY-BOUNDARY-OUTCOME-UNSUPPORTED` is removed. | Never released, so the never-reuse rule is not affected. |

**Caveat that stays.** Most Elsa 3 reusable workflows have a Flowchart root, which reports
`ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING` until #2705. Until then, `Complete` emission is exercised end to end with
non-Flowchart containers (Sequence, loops, Parallel) and reaches real Flowchart-based definitions once #2705 lands.

## Complexity Tracking

None.

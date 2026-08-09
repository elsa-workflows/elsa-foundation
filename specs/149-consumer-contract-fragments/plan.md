# Implementation Plan: Consumer Contract Fragments as Build Output

**Branch**: `1165-consumer-contract-fragments` | **Date**: 2026-08-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/149-consumer-contract-fragments/spec.md` (RFC #1191, sequencing steps 1–2 only)

## Summary

Make consumer-visible authoring contracts a build output: every contributing feature assembly emits a deterministic JSON **contract fragment** at build time (activities, structure kinds, expression surface, intrinsics, feature metadata), embedded as an assembly resource and merged into a committed `docs/contracts/` directory with per-fragment fingerprints and a CI freshness check. The **one-projection rule** is honored by composing the *existing* product descriptor pipeline — `ClrAssemblyScanner` (Reconciliation.Clr), `AuthoringSchemaExporter`, `IActivityStructureHandler`, `IExpressionDescriptorProvider`, the `[ShellFeature]`/manifest-hint metadata — from a new emitter CLI, rather than writing a parallel projection. Gates **G1** (inputs with CLR defaults emit `defaultValue` — fixed in the scanner via IL initializer analysis, flowing to both fragments and the persisted catalog) and **G2** (output descriptors emit `isRequired` — an additive catalog-view fix sourced from the already-populated `OutputDefinition.IsRequired`) land inside that shared pipeline. An equivalence test composes a representative host and asserts catalog endpoint output == merged fragments of enabled features + server-state overlay.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0` via root `Directory.Build.props`)

**Primary Dependencies**: existing product packages (`Elsa.Activities.Design.Reconciliation.Clr` — the CLR descriptor scanner; `Elsa.Workflows.Design.Api` — schema exporter; `Elsa.Workflows.Design.Core` — `IActivityStructureHandler`; `Elsa.Expressions.Core` — expression descriptors; `Elsa.Modularity.Nuplane` — manifest-hint reading); CShells `[ShellFeature]` attribute (feature id + DependsOn); `System.Reflection.MetadataLoadContext` (already in the dependency graph via the scanner); `Mono.Cecil` (tool-only, for post-compile resource embedding — see research R5)

**Storage**: N/A (build artifacts committed to `docs/contracts/`; Groundwork SQLite only inside the equivalence test host)

**Testing**: xunit only (constitutionally pinned — no FluentAssertions); existing test-host composition patterns (`WorkflowsDesignTestHost`, feature-composition tests)

**Target Platform**: build tooling (Windows + Linux CI — determinism across both is a hard requirement) + the existing server catalog endpoint

**Project Type**: build-integrated CLI tool (`tools/contracts/`) + surgical product changes (scanner, one API view, one exporter visibility) + committed generated artifact

**Performance Goals**: emitter adds ≤ ~2s per contributing project build (opt-in set, ~15–30 projects, not all 153); `-- check` completes within the existing CI job budget

**Constraints**: deterministic output (byte-identical across OS/machine/culture: ordinal key order, LF, no BOM, invariant culture, wire-form values); additive-only changes to the served catalog (spec FR-014); no server state in fragments (spec FR-012); no design-ahead to RFC steps 3–5

**Scale/Scope**: 153 src projects, ~103 feature classes; contributing surface today ≈ 20 assemblies (activity libraries, structure-handler features, expression features, the intrinsics provider, feature metadata)

## Constitution Check

*Gates from framework constitution v3.2.0 + Elsa constitution v3.4.0.*

| Gate | Assessment |
|---|---|
| §2.1 three-layer separation | PASS — no new src package in this step (see research R1): the emitter is a **tool** (`tools/contracts/`, sibling convention of `tools/maps/Elsa.Maps.Generator`), outside the feature dependency envelope, referencing product packages the way test projects do. Product changes stay inside their owning packages. |
| §2.24 sanctioned patterns | PASS — no new structural pattern: the tool composes existing catalogued shapes; the scanner change is behavior inside an existing implementation; the view change is additive projection. The MSBuild-target-wrapping-a-CLI shape mirrors the already-consumed external `Elsa.Platform.PackageManifest.Generator` package. |
| §2.6 cross-feature composition | PASS — no new cross-feature coupling; the tool is not a feature and registers nothing. |
| §E2.8 catalog is source of truth | PASS (load-bearing) — G1 is implemented **in the scanner** so the persisted catalog rows gain `defaultValue`; the endpoint keeps serving persisted rows. The fragments are a build-time projection of the same minting code, not a second picker source. |
| §E2.8 Model X reconciliation | ATTENTION — enriching descriptors changes the content hash for the same `(DefinitionId, Version)`; on a pre-existing database the reconciler throws `ActivityVersionHashMismatchException` by design. Mitigation recorded in research R11 and quickstart (fresh-DB-on-rebuild convention; CI images carry new assembly versions). Not a violation — this is Model X working as specified — but it must be surfaced to the consumer-workspace validation. |
| §2.21.1 golden rule of refactoring | PASS with note — existing scanner tests keep passing except where they assert the *absence* of defaults (`DefaultValue == null` for initializer-defaulted inputs); those assertions change because the behavior deliberately changes (feature work, not refactor). Any such test update is called out in the PR. |
| §2.23 unit tests | PLANNED — branch-covered tests for the IL default analyzer, fragment writer/merger/fingerprints, attribution rule; G1/G2 repro tests on `HttpEndpoint`; no new feature classes → no §2.23.1 obligations. |
| §2.23.5 exception boundaries | PLANNED — the emitter wraps reflection/IO failures into tool-domain diagnostics surfaced as canonical MSBuild errors; no raw infrastructure exceptions cross the tool boundary. |
| §E6 naming (R1–R8) | PASS — tool `Elsa.Contracts.Generator` under `tools/contracts/` (mirrors `Elsa.Maps.Generator`); no banned suffixes; no new src type names beyond additive view members. |
| §2.16/§2.16.1 project granularity | PASS — one new tool project + one new test project (`Elsa.Contracts.Tests`); no premature src package (§2.20 Rule 1 spirit): the fragment model types live in the tool until RFC step 5 gives product code a real need for them. |
| §2.22.1 extension-point catalogs | PASS — no new/changed extension points (no new contributor interfaces, events, or replacement contracts). Generated maps must be refreshed (new projects) — task included. |

**Post-design re-check** (after Phase 1): no violations introduced; Complexity Tracking below is empty.

## Project Structure

### Documentation (this feature)

```text
specs/149-consumer-contract-fragments/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions R1–R12
├── data-model.md        # Phase 1 — fragment/manifest entities
├── quickstart.md        # Phase 1 — regenerate/check/test/image commands
├── contracts/
│   └── contract-fragment-schema.md   # Fragment + manifest JSON contract, worked example
└── tasks.md             # Phase 2 (/speckit-tasks — not created by /speckit-plan)
```

### Source Code (repository root)

```text
tools/contracts/Elsa.Contracts.Generator/  # NEW — reflection CLI: merge | check | emit
├── Program.cs                             # command routing (mirrors Elsa.Maps.Generator conventions)
├── FragmentModels.cs                      # fragment + manifest envelope types (schemaVersion'd)
├── FragmentProjector.cs                   # per-assembly projection → fragment (composes product code + per-assembly feature DI)
├── FeatureIndex.cs                        # repo-wide feature id → type map for DependsOn-closure composition
├── TargetAssembly.cs                      # execution loading + dependency probing (target bins + app closures)
├── DeterministicJson.cs                   # ordinal key order, LF, no BOM, invariant culture, sha256: fingerprints
├── Diagnostics.cs                         # canonical MSBuild-format warnings/errors (ELSACT0NN)
├── ContractsMerge.cs                      # project all src assemblies → docs/contracts/ + manifest.json
└── ContractsFreshness.cs                  # check mode (byte-compare incl. manifest)

src/Elsa/Directory.Build.targets           # CHANGED — embeds the COMMITTED fragment as elsa.contract.json (by-existence; research R4)
src/Elsa/Activities/Design/Reconciliation/Clr/Services/
├── ClrAssemblyScanner.cs                  # CHANGED — G1 default ladder + ScanAssembly(assembly, references) overload
└── InitializerDefaultReader.cs            # NEW — SRM-based ctor-IL constant analysis (product code, shared path)
src/Elsa/Activities/Design/Core/Models/InputDefinition.cs               # CHANGED — HasStaticDefault (additive tail)
src/Elsa/Activities/Design/Api/Models/ActivityAuthoringCatalogView.cs   # CHANGED — G2: outputs gain IsRequired + ReferenceKey; inputs gain HasStaticDefault
src/Elsa/Activities/Design/Api/Handlers/ListActivityAuthoringCatalogRequestHandler.cs  # CHANGED — map the new fields
src/Elsa/Workflows/Design/Api/Services/AuthoringSchemaExporter.cs       # CHANGED — internal → public (tool reuses the one schema exporter)
src/Elsa/Expressions/JavaScript/Jint/SandboxSurfaceCatalog.cs           # NEW — declarative sandbox surface (research R10)
src/Elsa/Http/JavaScript/HttpJavaScriptFeature.cs                       # CHANGED — real (previously undeclared) DependsOn on JavaScriptRendering
.gitattributes                             # NEW — docs/contracts/** pinned to LF (byte-compare + fingerprints)

docs/contracts/                            # NEW — committed generated artifact (94 fragments at branch time)
├── README.md                              # convention, regeneration, fingerprint verification, known degradations
├── manifest.json                          # schema version, per-fragment sha256 fingerprints (array), counts
├── fragments/<AssemblyName>.json          # one per contributing assembly
└── submit-schema.json                     # produced by the served submit-schema handler itself

tests/Elsa/Contracts/Tests/                # NEW — Elsa.Contracts.Tests
├── EquivalenceTests.cs                    # catalog output == fragments + overlay; intrinsics; structures; dynamic union
├── FragmentProjectorTests.cs              # per-surface projection incl. G1/G2 repros + determinism
├── DeterministicJsonTests.cs              # ordering/LF/BOM/culture/fingerprint gates
├── ContractIntegrityTests.cs              # fingerprints, embedded==committed, no server state, src-project mapping
└── ContractsFreshnessTests.cs             # check-mode comparison semantics

tests/Elsa/Activities/Design/…             # CHANGED/NEW — scanner G1 tests, HttpEndpoint G1/G2 repros, catalog G2 tests, re-pinned ratchets
tests/Elsa/Expressions/JavaScript/Jint/Tests/SandboxSurfaceCatalogTests.cs  # NEW — catalog ↔ live engine pin

.github/workflows/ci.yml                   # CHANGED — contracts check step inside build-and-test (reuses Release build)
```

**Structure Decision**: build tooling under `tools/contracts/` (deliberate sibling of `tools/maps/`, per RFC "ride the same pipeline"); generation post-build + embedding in-build per research R4 (the Cecil/`ContractFragments.targets` design was replaced after the reference-cycle discovery); product changes confined to the seams the gates require plus the Jint sandbox catalog and one surfaced missing DependsOn; generated artifact under `docs/contracts/`; one new test project.

## Complexity Tracking

No constitution violations requiring justification.

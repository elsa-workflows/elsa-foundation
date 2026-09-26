# Tasks: First reviewed diagnostics feature group

**Input**: [spec](spec.md), [plan](plan.md), [research](research.md), [contract](contracts/diagnostics-group-v1.md)

## Phase 1: Setup

- [x] T001 Verify the issue claim, clean branch, existing spec 174 planner contract, and original catalog pin in `src/essentials/Modularity/Planning/`.

## Phase 2: Foundation

- [x] T002 Preserve `Catalogs/foundation-selection-catalog-v1.json` and publish a v2 immutable group snapshot in `src/essentials/Modularity/Planning/Catalogs/` and `Elsa.Modularity.Planning.csproj`.
- [x] T003 Resolve exact authored bundled pins in `Catalog/FoundationSelectionCatalog.cs` while keeping unknown pins unresolved.

## Phase 3: User Story 1 - Add and inspect diagnostics

**Independent test**: Profile plus group yields 20 exact IDs, four group reasons, and two required edges; a removed base ID stays absent and unresolved.

- [x] T004 [US1] Add repeatable group authoring to `src/essentials/Cli/CompositionInitCommand.cs` through the existing planner.
- [x] T005 [US1] Cover exact group members, dependency evidence, provenance, duplicate/unknown groups, and removal in `tests/essentials/Modularity/Planning/Tests/FoundationSelectionCatalogTests.cs` and `tests/essentials/Cli/Tests/CompositionInitCliTests.cs`.

## Phase 4: User Story 2 - Preserve original pins

**Independent test**: An original v1 composition plans and generates unchanged without a manual catalog path; explicit and unknown mismatches do not silently upgrade.

- [x] T006 [US2] Select the authored bundled snapshot in `src/essentials/Cli/CompositionPlanCommand.cs` and `CompositionGenerateCommand.cs`; preserve explicit `--catalog` behavior.
- [x] T007 [US2] Cover v1 compatibility and mismatch behavior in `tests/essentials/Cli/Tests/CompositionInitCliTests.cs`.

## Phase 5: User Story 3 - Explain deployment limits

**Independent test**: The group reference says which features are selected and identifies resource, migration, host, and tracing checks as separate.

- [x] T008 [US3] Document group use and prerequisites in `docs/reference/diagnostics-ef-group.md` and link it from `docs/reference/embedded-runtime-profile.md`.

## Phase 6: Validation

- [x] T009 Review source and docs against `contracts/diagnostics-group-v1.md`, including unchanged v1 bytes and absence of secret-bearing group fields.
- [x] T010 Run affected project tests, architecture guard, maps check, solution-filter check, and diff review; record exact results on #2066.

T002–T003 precede T004/T006. T005 and T007 validate their respective stories; T008 follows accepted behavior. The issue and PR track review, merge, and post-merge gates outside the code task list.

# Tasks: Groundwork Core Manifest And Planner Kernel

**Input**: Design documents from `specs/013-groundwork-core-manifest-planner/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

## Phase 1: Setup

- [x] T001 Create `src/Groundwork/Core/Groundwork.Core.csproj`
- [x] T002 Create `src/Groundwork/Relational/Groundwork.Relational.csproj` referencing `src/Groundwork/Core/Groundwork.Core.csproj`
- [x] T003 Create `src/Groundwork/Documents/Groundwork.Documents.csproj` referencing `src/Groundwork/Core/Groundwork.Core.csproj`
- [x] T004 Create `tests/Groundwork/Groundwork.Tests/Groundwork.Tests.csproj` referencing the three Groundwork source projects
- [x] T005 Add Groundwork source and test project entries to `Elsa.Server.slnx`

## Phase 2: Foundational Core Types

- [x] T006 [P] Define diagnostic result primitives in `src/Groundwork/Core/Validation/GroundworkDiagnostic.cs`
- [x] T007 [P] Define manifest identity/version records in `src/Groundwork/Core/Manifests/ManifestIdentity.cs`
- [x] T008 [P] Define workload classification enums/records in `src/Groundwork/Core/Workloads/WorkloadClassification.cs`
- [x] T009 [P] Define lifecycle/identity/tenancy/concurrency/serialization/physicalization policies in `src/Groundwork/Core/Manifests/StoragePolicies.cs`
- [x] T010 [P] Define index declarations in `src/Groundwork/Core/Indexing/IndexDeclaration.cs`
- [x] T011 [P] Define portable query declarations in `src/Groundwork/Core/Queries/PortableQueryDeclaration.cs`
- [x] T012 Define `StorageUnit` in `src/Groundwork/Core/Manifests/StorageUnit.cs`
- [x] T013 Define `StorageManifest` in `src/Groundwork/Core/Manifests/StorageManifest.cs`

## Phase 3: User Story 1 - Declare Provider-Neutral Storage Intent (P1)

**Goal**: Framework/application authors can define and validate provider-neutral storage manifests.

**Independent Test**: A generic sample metadata manifest validates without any provider or Elsa references.

- [x] T014 [P] [US1] Add manifest validation tests in `tests/Groundwork/Groundwork.Tests/ManifestValidationTests.cs`
- [x] T015 [P] [US1] Add generic sample manifest factory in `tests/Groundwork/Groundwork.Tests/SampleManifests.cs`
- [x] T016 [US1] Implement manifest validator in `src/Groundwork/Core/Validation/StorageManifestValidator.cs`
- [x] T017 [US1] Add validation result aggregation in `src/Groundwork/Core/Validation/ManifestValidationResult.cs`
- [x] T018 [US1] Add provider-neutrality validation rules in `src/Groundwork/Core/Validation/ProviderNeutralityRules.cs`

## Phase 4: User Story 2 - Produce Relational And Document Plans (P2)

**Goal**: The same validated manifest can produce provider-neutral relational and document plan descriptions.

**Independent Test**: The sample manifest produces both relational and document plans preserving indexes, concurrency, and schema-history intent.

- [x] T019 [P] [US2] Add planner contract tests in `tests/Groundwork/Groundwork.Tests/PlannerContractTests.cs`
- [x] T020 [P] [US2] Define materialization plan concepts in `src/Groundwork/Core/Materialization/MaterializationPlan.cs`
- [x] T021 [P] [US2] Define schema-history entry concepts in `src/Groundwork/Core/Materialization/SchemaHistoryEntry.cs`
- [x] T022 [US2] Define relational plan model in `src/Groundwork/Relational/Planning/RelationalPlan.cs`
- [x] T023 [US2] Implement relational planner in `src/Groundwork/Relational/Planning/RelationalManifestPlanner.cs`
- [x] T024 [US2] Define document plan model in `src/Groundwork/Documents/Planning/DocumentPlan.cs`
- [x] T025 [US2] Implement document planner in `src/Groundwork/Documents/Planning/DocumentManifestPlanner.cs`

## Phase 5: User Story 3 - Validate Provider Capabilities (P3)

**Goal**: Hosts/providers can compare manifests against provider capability reports before materialization.

**Independent Test**: Compatible capability reports allow planning; unsupported required capabilities block planning with structured diagnostics.

- [x] T026 [P] [US3] Add provider capability tests in `tests/Groundwork/Groundwork.Tests/ProviderCapabilityTests.cs`
- [x] T027 [P] [US3] Define provider capability report in `src/Groundwork/Core/Capabilities/ProviderCapabilityReport.cs`
- [x] T028 [P] [US3] Define capability compatibility result in `src/Groundwork/Core/Capabilities/CapabilityCompatibilityResult.cs`
- [x] T029 [US3] Implement capability validator in `src/Groundwork/Core/Capabilities/ProviderCapabilityValidator.cs`
- [x] T030 [US3] Integrate compatibility checks into relational planner in `src/Groundwork/Relational/Planning/RelationalManifestPlanner.cs`
- [x] T031 [US3] Integrate compatibility checks into document planner in `src/Groundwork/Documents/Planning/DocumentManifestPlanner.cs`

## Phase 6: Boundary And Documentation

- [x] T032 [P] Add Groundwork dependency boundary tests in `tests/Groundwork/Groundwork.Tests/GroundworkDependencyBoundaryTests.cs`
- [x] T033 [P] Add feature documentation for Groundwork Core in `src/Groundwork/Core/README.md`
- [x] T034 [P] Add feature documentation for Groundwork Relational in `src/Groundwork/Relational/README.md`
- [x] T035 [P] Add feature documentation for Groundwork Documents in `src/Groundwork/Documents/README.md`

## Phase 7: Validation

- [x] T036 Run `dotnet test tests/Groundwork/Groundwork.Tests/Groundwork.Tests.csproj`
- [x] T037 Run `dotnet test Elsa.Server.slnx --no-restore`
- [x] T038 Update `specs/013-groundwork-core-manifest-planner/quickstart.md` if validation commands need correction

## Dependencies

- Phase 1 must complete before source or test implementation.
- T012 and T013 depend on T006-T011.
- T016-T018 depend on T012-T015.
- T022-T025 depend on T020-T021 and validated manifest models.
- T029 depends on T027-T028.
- T030-T031 depend on T029.
- T036 depends on all implementation and test tasks.

## Parallel Execution Examples

- T006-T011 can run in parallel after project setup.
- T014 and T015 can run in parallel with T016 once core model names are agreed.
- T020 and T021 can run in parallel with T022 and T024.
- T033-T035 can run in parallel after source project structure exists.

## Implementation Strategy

1. Establish project structure and solution wiring.
2. Build core manifest and validation types first.
3. Add tests and sample manifest early to keep the contract concrete.
4. Implement relational and document planners from the same manifest.
5. Add capability validation and architecture boundary tests.
6. Run focused Groundwork tests, then solution-level validation.

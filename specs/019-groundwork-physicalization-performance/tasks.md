# Tasks: Groundwork Physicalization And Performance

## Phase 1: Setup

- [x] T001 Create G7 specification and implementation plan in `specs/019-groundwork-physicalization-performance/`
- [x] T002 Update Speckit and program-goal pointers for G7

## Phase 2: Core Physicalization Model

- [x] T003 Add provider-neutral physicalized field planning in `src/Groundwork/Core/Physicalization/`
- [x] T004 Extend materialization operations to describe optimized physicalization in `src/Groundwork/Core/Materialization/MaterializationPlan.cs`
- [x] T005 Update document planning to emit optimized physicalization operations in `src/Groundwork/Documents/Planning/DocumentManifestPlanner.cs`
- [x] T006 Add core planner/projection tests in `tests/Groundwork/Groundwork.Tests/`

## Phase 3: Relational Optimized Path

- [x] T007 Add relational physicalization naming helpers in `src/Groundwork/Relational/Physicalization/`
- [x] T008 Extend relational materialization to create optimized projection structures in `src/Groundwork/Relational/Materialization/RelationalMaterializerBase.cs`
- [x] T009 Extend relational document store save/update/delete/query to maintain and use optimized projections in `src/Groundwork/Relational/Documents/`
- [x] T010 Add SQLite optimized physicalization tests in `tests/Groundwork/Groundwork.Sqlite.Tests/`

## Phase 4: MongoDB Optimized Path

- [x] T011 Extend MongoDB materialization to create optimized projection indexes in `src/Groundwork/MongoDb/Materialization/MongoDbGroundworkMaterializer.cs`
- [x] T012 Extend MongoDB document store save/update/delete/query to maintain and use optimized projections in `src/Groundwork/MongoDb/Documents/MongoDbDocumentStore.cs`
- [x] T013 Add MongoDB optimized physicalization tests in `tests/Groundwork/Groundwork.MongoDb.Tests/`

## Phase 5: Validation

- [x] T014 Run `dotnet test tests/Groundwork/Groundwork.Tests/Groundwork.Tests.csproj`
- [x] T015 Run `dotnet test tests/Groundwork/Groundwork.Sqlite.Tests/Groundwork.Sqlite.Tests.csproj`
- [x] T016 Run `dotnet test tests/Groundwork/Groundwork.MongoDb.Tests/Groundwork.MongoDb.Tests.csproj`
- [x] T017 Run `dotnet test Elsa.Server.slnx --no-restore`

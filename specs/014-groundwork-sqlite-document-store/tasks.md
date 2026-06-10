# Tasks: Groundwork SQLite Document Store

## Phase 1: Setup

- [x] T001 Create G2 specification and implementation plan in `specs/014-groundwork-sqlite-document-store/`
- [x] T002 Create `src/Groundwork/Sqlite/Groundwork.Sqlite.csproj`
- [x] T003 Create `tests/Groundwork/Groundwork.Sqlite.Tests/Groundwork.Sqlite.Tests.csproj`
- [x] T004 Add SQLite provider and test project entries to `Elsa.Server.slnx`

## Phase 2: Document Store Contracts

- [x] T005 Define document envelope in `src/Groundwork/Documents/Store/DocumentEnvelope.cs`
- [x] T006 Define document query/result contracts in `src/Groundwork/Documents/Store/DocumentStoreQuery.cs`
- [x] T007 Define store result/concurrency types in `src/Groundwork/Documents/Store/DocumentStoreResult.cs`
- [x] T008 Define `IDocumentStore` in `src/Groundwork/Documents/Store/IDocumentStore.cs`

## Phase 3: SQLite Materialization

- [x] T009 Implement SQLite materializer in `src/Groundwork/Sqlite/Materialization/SqliteGroundworkMaterializer.cs`
- [x] T010 Add materializer tests in `tests/Groundwork/Groundwork.Sqlite.Tests/SqliteGroundworkMaterializerTests.cs`

## Phase 4: SQLite Document Operations

- [x] T011 Implement SQLite document store in `src/Groundwork/Sqlite/Documents/SqliteDocumentStore.cs`
- [x] T012 Add save/load/update/delete/index tests in `tests/Groundwork/Groundwork.Sqlite.Tests/SqliteDocumentStoreTests.cs`
- [x] T013 Add undeclared-query, unique-index, and optimistic-concurrency tests in `tests/Groundwork/Groundwork.Sqlite.Tests/SqliteDocumentStoreTests.cs`

## Phase 5: Boundary And Validation

- [x] T014 Add SQLite dependency boundary checks in `tests/Groundwork/Groundwork.Sqlite.Tests/SqliteDependencyBoundaryTests.cs`
- [x] T015 Add provider README in `src/Groundwork/Sqlite/README.md`
- [x] T016 Run `dotnet test tests/Groundwork/Groundwork.Sqlite.Tests/Groundwork.Sqlite.Tests.csproj`
- [x] T017 Run `dotnet test Elsa.Server.slnx --no-restore`

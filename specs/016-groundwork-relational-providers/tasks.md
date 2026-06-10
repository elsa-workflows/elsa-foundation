# Tasks: Groundwork SQL Server And PostgreSQL Providers

## Phase 1: Setup

- [x] T001 Create G4 specification and implementation plan in `specs/016-groundwork-relational-providers/`
- [x] T002 Create `src/Groundwork/SqlServer/Groundwork.SqlServer.csproj`
- [x] T003 Create `src/Groundwork/PostgreSql/Groundwork.PostgreSql.csproj`
- [x] T004 Create `tests/Groundwork/Groundwork.RelationalProviders.Tests/Groundwork.RelationalProviders.Tests.csproj`
- [x] T005 Add provider and test project entries to `Elsa.Server.slnx`

## Phase 2: Shared Relational Support

- [x] T006 Extract relational document-store dialect contract in `src/Groundwork/Relational/Documents/`
- [x] T007 Extract shared relational document-store behavior in `src/Groundwork/Relational/Documents/`
- [x] T008 Keep SQLite tests green after extraction

## Phase 3: SQL Server Provider

- [x] T009 Implement SQL Server materializer
- [x] T010 Implement SQL Server document store
- [x] T011 Add SQL Server provider README

## Phase 4: PostgreSQL Provider

- [x] T012 Implement PostgreSQL materializer
- [x] T013 Implement PostgreSQL document store
- [x] T014 Add PostgreSQL provider README

## Phase 5: Provider Contract Tests

- [x] T015 Add shared provider contract test harness
- [x] T016 Add SQL Server container-backed tests
- [x] T017 Add PostgreSQL container-backed tests
- [x] T018 Add dependency boundary tests

## Phase 6: Validation

- [x] T019 Run `dotnet test tests/Groundwork/Groundwork.RelationalProviders.Tests/Groundwork.RelationalProviders.Tests.csproj`
- [x] T020 Run `dotnet test tests/Groundwork/Groundwork.Sqlite.Tests/Groundwork.Sqlite.Tests.csproj`
- [x] T021 Run `dotnet test Elsa.Server.slnx --no-restore`

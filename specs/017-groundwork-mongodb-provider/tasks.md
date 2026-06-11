# Tasks: Groundwork MongoDB Provider

## Phase 1: Setup

- [x] T001 Create G5 specification and implementation plan in `specs/017-groundwork-mongodb-provider/`
- [x] T002 Create `src/Groundwork/MongoDb/Groundwork.MongoDb.csproj`
- [x] T003 Create `tests/Groundwork/Groundwork.MongoDb.Tests/Groundwork.MongoDb.Tests.csproj`
- [x] T004 Add MongoDB provider and test project entries to `Elsa.Server.slnx`

## Phase 2: MongoDB Provider

- [x] T005 Implement MongoDB materializer
- [x] T006 Implement MongoDB document store
- [x] T007 Add MongoDB provider README

## Phase 3: Provider Tests

- [x] T008 Add MongoDB test manifest factory
- [x] T009 Add materialization/index/schema-history tests
- [x] T010 Add document-store contract tests
- [x] T011 Add dependency boundary tests

## Phase 4: Validation

- [x] T012 Run `dotnet test tests/Groundwork/Groundwork.MongoDb.Tests/Groundwork.MongoDb.Tests.csproj`
- [x] T013 Run `dotnet test tests/Groundwork/Groundwork.Sqlite.Tests/Groundwork.Sqlite.Tests.csproj`
- [x] T014 Run `dotnet test tests/Groundwork/Groundwork.RelationalProviders.Tests/Groundwork.RelationalProviders.Tests.csproj`
- [x] T015 Run `dotnet test Elsa.Server.slnx --no-restore`

# Tasks: Groundwork Runtime-Defined Entities

## Phase 1: Setup

- [x] T001 Create G6 specification and implementation plan in `specs/018-groundwork-runtime-entities/`

## Phase 2: Runtime Entity Bridge

- [x] T002 Add runtime entity definition models
- [x] T003 Add runtime entity manifest factory
- [x] T004 Add runtime entity store wrapper over `IDocumentStore`

## Phase 3: Tests

- [x] T005 Add manifest factory tests
- [x] T006 Add SQLite-backed runtime entity store tests
- [x] T007 Verify Groundwork dependency boundary remains green

## Phase 4: Validation

- [x] T008 Run `dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj`
- [x] T009 Run `dotnet test Elsa.Server.slnx --no-restore`

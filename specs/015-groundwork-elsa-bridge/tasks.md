# Tasks: Groundwork Elsa Bridge

## Phase 1: Setup

- [x] T001 Create G3 specification and implementation plan in `specs/015-groundwork-elsa-bridge/`
- [x] T002 Create `src/Elsa/Persistence/Groundwork/Elsa.Persistence.Groundwork.csproj`
- [x] T003 Create `tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj`
- [x] T004 Add bridge and test project entries to `Elsa.Server.slnx`

## Phase 2: Bridge Contracts And Registration

- [x] T005 Define `GroundworkPersistenceOptions`
- [x] T006 Define `IGroundworkPersistenceProvider`
- [x] T007 Add service registration extension
- [x] T008 Add `GroundworkPersistenceFeature`

## Phase 3: Startup Materialization And Diagnostics

- [x] T009 Define diagnostics snapshot records
- [x] T010 Implement diagnostics service
- [x] T011 Implement `MaterializeGroundworkStartupTask`

## Phase 4: First Module Validation

- [x] T012 Add Secrets-like manifest factory in the Elsa bridge
- [x] T013 Add registration and diagnostics tests
- [x] T014 Add startup materialization tests using SQLite adapter
- [x] T015 Add dependency boundary tests

## Phase 5: Validation

- [x] T016 Run `dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj`
- [x] T017 Run `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- [x] T018 Run `dotnet test Elsa.Server.slnx --no-restore`

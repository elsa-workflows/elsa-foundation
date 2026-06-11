# Feature Specification: Groundwork SQL Server And PostgreSQL Providers

**Feature Branch**: `codex/groundwork-relational-providers`

**Created**: 2026-06-10

**Status**: Draft

**Input**: User description: "Groundwork G4 SQL Server and PostgreSQL providers. Add Groundwork.SqlServer and Groundwork.PostgreSql provider packages that pass the same document/index contract as SQLite. Extract shared relational document-store behavior where useful, keep provider differences isolated, materialize schema history and unique declared indexes, and validate both providers with container-backed integration tests."

## User Scenarios & Testing

### User Story 1 - Use SQL Server As A Groundwork Provider (Priority: P1)

A .NET application can choose SQL Server as the Groundwork relational provider for portable document workloads.

**Why this priority**: SQL Server is a primary Elsa deployment target and must prove the manifest/document contract is not SQLite-specific.

**Independent Test**: Start a SQL Server test container, materialize a manifest, run the shared document-store contract, and verify schema history exists.

**Acceptance Scenarios**:

1. **Given** a Groundwork manifest and SQL Server connection, **When** materialization runs twice, **Then** document, index, and schema-history objects exist and schema history remains idempotent.
2. **Given** saved documents with declared indexes, **When** the shared contract queries, updates, deletes, and checks concurrency, **Then** SQL Server returns the same statuses and results as SQLite.

---

### User Story 2 - Use PostgreSQL As A Groundwork Provider (Priority: P1)

A .NET application can choose PostgreSQL as the Groundwork relational provider for portable document workloads.

**Why this priority**: PostgreSQL is the second relational target in G4 and must pass the same contract without changing generic Groundwork concepts.

**Independent Test**: Start a PostgreSQL test container, materialize a manifest, run the shared document-store contract, and verify schema history exists.

**Acceptance Scenarios**:

1. **Given** a Groundwork manifest and PostgreSQL connection, **When** materialization runs twice, **Then** document, index, and schema-history objects exist and schema history remains idempotent.
2. **Given** saved documents with declared indexes, **When** the shared contract queries, updates, deletes, and checks concurrency, **Then** PostgreSQL returns the same statuses and results as SQLite.

---

### User Story 3 - Keep Provider Differences Isolated (Priority: P2)

A Groundwork maintainer can inspect relational provider code and see that shared document behavior is reused while SQL dialect differences remain provider-local.

**Why this priority**: Adding two relational providers without reuse would make the persistence framework harder to extract and maintain.

**Independent Test**: Review project references and dependency boundary tests to verify provider packages reference generic Groundwork contracts and the shared relational support only.

**Acceptance Scenarios**:

1. **Given** `Groundwork.SqlServer` and `Groundwork.PostgreSql`, **When** dependency boundary tests run, **Then** neither package references Elsa projects.
2. **Given** provider-specific SQL dialects, **When** maintainers inspect materializers, **Then** SQL Server and PostgreSQL isolate only DDL, parameter prefixes, upsert, and paging differences.

### Edge Cases

- Duplicate materialization must not create duplicate schema-history records.
- Unique declared indexes must reject duplicate values in each provider.
- Stale expected versions must not update documents or indexes.
- Delete conflicts must leave documents and indexes untouched.
- Missing providers or unavailable Docker must be surfaced clearly by the test run, not hidden as passing provider validation.

## Requirements

### Functional Requirements

- **FR-001**: Add `Groundwork.SqlServer` and `Groundwork.PostgreSql` provider projects.
- **FR-002**: Both providers MUST materialize document, index, and schema-history storage.
- **FR-003**: Both providers MUST support idempotent materialization.
- **FR-004**: Both providers MUST implement the `IDocumentStore` contract.
- **FR-005**: Both providers MUST maintain declared index rows transactionally with document writes.
- **FR-006**: Both providers MUST enforce unique declared indexes.
- **FR-007**: Both providers MUST reject undeclared index queries.
- **FR-008**: Both providers MUST enforce expected-version optimistic concurrency for save and delete.
- **FR-009**: Shared relational behavior SHOULD be extracted into `Groundwork.Relational` when it removes meaningful duplication.
- **FR-010**: Provider packages MUST not reference Elsa projects.
- **FR-011**: Provider validation MUST use real SQL Server and PostgreSQL databases through container-backed integration tests.
- **FR-012**: Existing SQLite behavior MUST remain green after shared relational extraction.

### Key Entities

- **Relational Document Store Base**: Shared implementation of portable document-store behavior over ADO.NET.
- **Relational Dialect**: Provider-local SQL generation and parameter handling.
- **SQL Server Materializer**: SQL Server provider schema materialization service.
- **PostgreSQL Materializer**: PostgreSQL provider schema materialization service.
- **Provider Contract Tests**: Shared tests run against each relational provider.

## Success Criteria

### Measurable Outcomes

- **SC-001**: SQL Server integration tests pass against a container-backed database.
- **SC-002**: PostgreSQL integration tests pass against a container-backed database.
- **SC-003**: SQLite provider tests still pass after shared relational extraction.
- **SC-004**: Full solution tests pass with the new provider projects included.

## Assumptions

- SQL Server tests may take longer than other provider tests because the container image is heavier.
- The provider packages use ADO.NET directly, not EF Core.
- The first G4 version keeps equality-only portable query support, matching SQLite G2.
- JSON values remain stored as text for relational MVP parity.

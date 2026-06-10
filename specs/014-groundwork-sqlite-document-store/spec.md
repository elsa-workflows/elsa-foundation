# Feature Specification: Groundwork SQLite Document Store

**Feature Branch**: `codex/groundwork-sqlite-document-store`

**Created**: 2026-06-10

**Status**: Draft

**Input**: User description: "Implement G2 SQLite portable document store MVP for Groundwork: document envelope, SQLite tables, generic field indexes, materialization/schema history, save/load/delete, transactional index maintenance, declared-index queries, and optimistic concurrency."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Materialize A SQLite Document Store (Priority: P1)

A provider integrator can take a validated Groundwork document manifest, materialize the required SQLite tables and schema-history records, and verify that the same manifest can be applied repeatedly without corrupting existing data.

**Why this priority**: No document store operation is safe until the provider has deterministic storage shape and history.

**Independent Test**: Materialize the sample manifest against an in-memory SQLite database twice and verify document, index, and schema-history tables exist with one applied manifest version.

**Acceptance Scenarios**:

1. **Given** an empty SQLite database and a valid document manifest, **When** materialization runs, **Then** Groundwork document, index, and schema-history tables are created.
2. **Given** the same manifest has already been materialized, **When** materialization runs again, **Then** no duplicate schema-history row is created.

---

### User Story 2 - Save, Load, Delete, And Maintain Indexes (Priority: P2)

A document-store consumer can save JSON documents, load them by kind/id, delete them, and rely on declared indexes being updated transactionally with the document envelope.

**Why this priority**: This is the smallest end-to-end document persistence path needed before Elsa can validate a real module.

**Independent Test**: Save a document with indexed fields, load it, query by each declared index, update it, verify old index values no longer match, then delete it and verify document and index rows are gone.

**Acceptance Scenarios**:

1. **Given** a materialized store and a new document, **When** the document is saved, **Then** it can be loaded by kind/id and queried through declared indexes.
2. **Given** a saved document is updated with changed indexed values, **When** the update succeeds, **Then** old index values no longer return the document and new index values do.
3. **Given** a saved document is deleted, **When** deletion succeeds, **Then** loading and declared-index queries no longer return it.

---

### User Story 3 - Enforce Portable Query And Concurrency Rules (Priority: P3)

A document-store consumer receives clear failures for unsupported/unindexed queries and stale writes/deletes.

**Why this priority**: Silent scans or lost updates would make the portable contract misleading and unsafe.

**Independent Test**: Attempt an undeclared query and a stale expected-version save/delete and verify structured failures.

**Acceptance Scenarios**:

1. **Given** a query references an undeclared index, **When** it is executed, **Then** the store rejects it clearly instead of scanning all documents.
2. **Given** a document has version `2`, **When** a save or delete expects version `1`, **Then** the operation fails with a concurrency error and does not modify document or index rows.

### Edge Cases

- Missing indexed fields are not written to the generic index table.
- Unique declared indexes must be enforced by SQLite.
- Store operations must execute document and index changes in the same transaction.
- Optimistic concurrency uses the document envelope version, not JSON payload content.
- G2 supports equality queries only; richer operations remain planned for later provider hardening.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Groundwork MUST add a `Groundwork.Sqlite` provider package that references `Groundwork.Core` and `Groundwork.Documents`.
- **FR-002**: The SQLite provider MUST materialize a document envelope table, a generic index table, and a schema-history table.
- **FR-003**: Materialization MUST be idempotent for the same manifest version.
- **FR-004**: The document store MUST save new JSON documents with version `1`.
- **FR-005**: The document store MUST load documents by document kind and id.
- **FR-006**: The document store MUST update existing documents and increment the envelope version.
- **FR-007**: The document store MUST delete documents and remove their index rows transactionally.
- **FR-008**: The document store MUST maintain declared index rows transactionally on save/update/delete.
- **FR-009**: The document store MUST query only by declared indexes and reject undeclared index queries.
- **FR-010**: The SQLite provider MUST enforce unique declared indexes.
- **FR-011**: The document store MUST enforce expected-version optimistic concurrency for save and delete.
- **FR-012**: The tests MUST prove save/load/delete, index maintenance, undeclared-query rejection, unique-index enforcement, and stale-write/delete rejection.
- **FR-013**: The provider MUST remain generic and must not reference Elsa packages.

### Key Entities *(include if feature involves data)*

- **Document Envelope**: Persisted JSON content plus kind, id, schema version, version, and timestamps.
- **Document Index Entry**: Generic index row linking document kind, index name, normalized field value, and document id.
- **Schema History Row**: Applied manifest identity/version/provider record.
- **SQLite Document Store**: Provider implementation for save/load/delete/query operations.
- **SQLite Materializer**: Provider implementation that creates storage tables and records applied manifest versions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: SQLite materialization tests pass and prove idempotent schema-history recording.
- **SC-002**: SQLite document contract tests pass for save, load, update, delete, and equality query by declared indexes.
- **SC-003**: Tests prove undeclared queries fail clearly and do not scan.
- **SC-004**: Tests prove stale expected-version save/delete operations fail without changing persisted data.
- **SC-005**: Tests prove generic Groundwork SQLite projects do not reference `Elsa.*`.

## Assumptions

- G1 Groundwork Core/Document planning has been implemented.
- G2 can use `Microsoft.Data.Sqlite` directly; EF Core is intentionally not used.
- Equality queries are sufficient for the G2 portable query MVP.
- SQLite in-memory databases are acceptable for provider integration tests.

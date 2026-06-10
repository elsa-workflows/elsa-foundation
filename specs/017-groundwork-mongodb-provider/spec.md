# Feature Specification: Groundwork MongoDB Provider

**Feature Branch**: `codex/groundwork-mongodb-provider`

**Created**: 2026-06-10

**Status**: Draft

**Input**: User description: "Groundwork G5 MongoDB provider. Add Groundwork.MongoDb provider package that materializes native collections and indexes from Groundwork manifests, implements the portable IDocumentStore contract over MongoDB collections, records schema history, enforces declared unique indexes, rejects undeclared queries, supports optimistic concurrency, and validates behavior with container-backed MongoDB tests."

## User Scenarios & Testing

### User Story 1 - Materialize MongoDB Native Collections (Priority: P1)

A .NET application can choose MongoDB as a Groundwork provider and materialize native collections and indexes from a manifest.

**Why this priority**: G5 exists to prove the portable manifest contract can map to a document database without relational table/index-row assumptions.

**Independent Test**: Start a MongoDB test container, materialize a manifest twice, and verify the document collection, declared indexes, and schema-history record exist.

**Acceptance Scenarios**:

1. **Given** a manifest with one document storage unit, **When** materialization runs, **Then** a native MongoDB collection exists for that unit.
2. **Given** declared indexes, **When** materialization runs, **Then** MongoDB native indexes exist on the declared content fields.
3. **Given** materialization runs twice, **When** schema history is inspected, **Then** exactly one history record exists for the manifest/provider pair.

---

### User Story 2 - Use MongoDB As A Portable Document Store (Priority: P1)

A Groundwork application can save, load, update, delete, and query documents through the same `IDocumentStore` contract used by relational providers.

**Why this priority**: MongoDB must pass the same portable behavior as SQLite, SQL Server, and PostgreSQL before it can be considered a real provider.

**Independent Test**: Run shared document-store contract tests against a MongoDB container.

**Acceptance Scenarios**:

1. **Given** a new document, **When** it is saved and loaded, **Then** content and version are preserved.
2. **Given** declared indexes, **When** documents are updated or deleted, **Then** index-backed queries reflect current state.
3. **Given** a stale expected version, **When** save or delete is attempted, **Then** the document remains unchanged and a concurrency conflict is returned.

---

### User Story 3 - Keep MongoDB Provider Isolated (Priority: P2)

A Groundwork maintainer can inspect MongoDB provider dependencies and see no Elsa or relational provider coupling.

**Why this priority**: MongoDB should validate the generic Groundwork boundary, not become an Elsa-specific or relationally shaped implementation.

**Independent Test**: Dependency boundary tests verify `Groundwork.MongoDb` references generic Groundwork contracts and MongoDB driver packages only.

**Acceptance Scenarios**:

1. **Given** the MongoDB provider project, **When** dependency tests run, **Then** it does not reference Elsa projects.
2. **Given** MongoDB provider source, **When** maintainers inspect storage, **Then** it uses native collections and indexes rather than relational index tables.

### Edge Cases

- Duplicate materialization must not create duplicate schema-history records.
- Unique declared indexes must reject duplicate indexed values.
- Missing indexed fields must not break unique indexes for documents that omit the field.
- Stale save/delete expected versions must not modify documents.
- Undeclared queries must fail before hitting MongoDB.

## Requirements

### Functional Requirements

- **FR-001**: Add `Groundwork.MongoDb` provider project.
- **FR-002**: MongoDB materialization MUST create one collection per storage unit.
- **FR-003**: MongoDB materialization MUST create native indexes for declared one-field indexes.
- **FR-004**: MongoDB materialization MUST record schema history idempotently.
- **FR-005**: MongoDB provider MUST implement `IDocumentStore`.
- **FR-006**: MongoDB provider MUST enforce unique declared indexes through MongoDB unique indexes.
- **FR-007**: MongoDB provider MUST reject undeclared index queries.
- **FR-008**: MongoDB provider MUST enforce expected-version optimistic concurrency for save and delete.
- **FR-009**: MongoDB provider MUST not reference Elsa projects.
- **FR-010**: MongoDB validation MUST use a real MongoDB database through container-backed integration tests.
- **FR-011**: Existing SQLite and relational provider tests MUST remain green.

### Key Entities

- **MongoDB Materializer**: Creates collections, indexes, and schema-history records from a manifest.
- **MongoDB Document Store**: Implements portable document operations over MongoDB collections.
- **Schema History Collection**: Provider metadata collection for manifest/provider materialization records.
- **Native Collection**: One MongoDB collection per Groundwork storage unit.

## Success Criteria

### Measurable Outcomes

- **SC-001**: MongoDB integration tests pass against a container-backed database.
- **SC-002**: Dependency boundary tests verify `Groundwork.MongoDb` remains Elsa-free.
- **SC-003**: Full solution tests pass with the MongoDB provider project included.

## Assumptions

- G5 supports one-field declared indexes, matching current relational MVP behavior.
- Equality-only portable query support remains the provider baseline.
- MongoDB document content is stored as native BSON under a `content` field.
- Multi-document transactions are not required for G5 because content and index state are stored in one native document plus MongoDB-managed indexes.

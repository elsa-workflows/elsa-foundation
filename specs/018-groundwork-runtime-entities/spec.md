# Feature Specification: Groundwork Runtime-Defined Entities

**Feature Branch**: `codex/groundwork-runtime-entities`

**Created**: 2026-06-10

**Status**: Draft

**Input**: User description: "Groundwork G6 runtime-defined entities. Add an opt-in Elsa.Persistence.Groundwork runtime entity surface that maps published runtime-defined entity definitions and instances onto Groundwork document storage without requiring a physical table per runtime entity. Definitions declare fields and indexes; publishing creates a Groundwork manifest; instances can be saved and queried by declared indexes through IDocumentStore. Validate with SQLite-backed tests."

## User Scenarios & Testing

### User Story 1 - Publish A Runtime Entity Definition (Priority: P1)

An Elsa extension can define a runtime entity type with fields and declared indexes and publish it as a Groundwork manifest.

**Why this priority**: Runtime-defined entities need a stable mapping into Groundwork before instances can be persisted.

**Independent Test**: Create a definition with a unique key index and a non-unique category index, publish it, and verify the manifest contains a definition unit and an instance unit.

**Acceptance Scenarios**:

1. **Given** a runtime entity definition, **When** it is converted to a manifest, **Then** the manifest declares a portable instance storage unit.
2. **Given** declared indexes, **When** the manifest is inspected, **Then** the instance storage unit exposes those indexes as Groundwork indexes.

---

### User Story 2 - Save And Query Runtime Entity Instances (Priority: P1)

An Elsa extension can save runtime-defined entity instances and query them by declared indexes without a physical table per entity type.

**Why this priority**: This validates the main G6 outcome: runtime-defined business data can use portable document storage by default.

**Independent Test**: Materialize the manifest with SQLite, save instances through the runtime entity store, and query instances by declared indexes.

**Acceptance Scenarios**:

1. **Given** a published definition and materialized manifest, **When** an instance is saved, **Then** it can be loaded by id.
2. **Given** declared indexes, **When** instances are queried by index, **Then** matching instances are returned.
3. **Given** a stale expected version, **When** an instance is saved, **Then** a concurrency conflict is returned.

---

### User Story 3 - Preserve The Elsa/Groundwork Boundary (Priority: P2)

Runtime entity concepts can live in the Elsa bridge without leaking Elsa-specific concepts into generic Groundwork packages.

**Why this priority**: Runtime-defined entities are an Elsa validation path, not a generic Groundwork core concept.

**Independent Test**: Dependency boundary tests verify Groundwork packages remain Elsa-free.

**Acceptance Scenarios**:

1. **Given** runtime entity bridge code, **When** dependency tests run, **Then** generic Groundwork projects still do not reference Elsa projects.

### Edge Cases

- Publishing a definition without indexes still creates an instance storage unit.
- Querying an undeclared runtime entity index fails through the underlying `IDocumentStore` contract.
- Duplicate unique index values are rejected by the selected provider.

## Requirements

### Functional Requirements

- **FR-001**: Add runtime-defined entity definition models to `Elsa.Persistence.Groundwork`.
- **FR-002**: Add a manifest factory that maps a runtime entity definition to a Groundwork manifest.
- **FR-003**: Generated manifests MUST include a definition storage unit.
- **FR-004**: Generated manifests MUST include one instance storage unit per runtime entity definition.
- **FR-005**: Runtime entity instance storage MUST use `IDocumentStore`.
- **FR-006**: Runtime entity indexes MUST map to Groundwork declared indexes.
- **FR-007**: Runtime entity instance save/load/query/delete MUST preserve `IDocumentStore` statuses and optimistic concurrency.
- **FR-008**: Tests MUST validate runtime entity instances using a real provider-backed store.
- **FR-009**: Generic Groundwork packages MUST remain Elsa-free.

### Key Entities

- **Runtime Entity Definition**: Elsa-side definition of a runtime business entity type.
- **Runtime Entity Index**: Declared query index on a runtime entity instance field.
- **Runtime Entity Manifest Factory**: Converts a definition into a Groundwork manifest.
- **Runtime Entity Store**: Convenience wrapper over `IDocumentStore` for definition and instance operations.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Tests prove a runtime entity definition creates a manifest with definition and instance storage units.
- **SC-002**: SQLite-backed tests save, load, query, and delete runtime entity instances by declared indexes.
- **SC-003**: Full solution tests pass with the runtime entity bridge included.

## Assumptions

- Runtime-defined entities are implemented in the Elsa bridge because they are an Elsa validation surface.
- G6 uses portable document storage; physicalization is deferred to G7.
- G6 tests use SQLite because provider parity was established in G2-G5.

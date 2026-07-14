# Feature Specification: Groundwork Design Persistence

**Feature Branch**: `093-groundwork-design-persistence`

**Created**: 2026-07-14

**Status**: Draft

**Input**: User description: "Migrate Elsa workflow and activity design persistence to bounded Groundwork routes and remove the EF Core design implementation family after parity gates."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Persist Complete Design Lifecycles (Priority: P1)

As a workflow author, I can create, find, revise, validate, promote, submit, and delete workflow and activity definitions without the behavior changing because the first-party persistence implementation changed.

**Why this priority**: Authoring and version lifecycle correctness is the primary value of design persistence. A provider replacement that loses state, weakens atomicity, or changes results is not usable.

**Independent Test**: Run the existing public workflow- and activity-design contract scenarios against a durable store, restart the host between writes and reads, and verify identical entities, lifecycle outcomes, events, layouts, validations, versions, and errors.

**Acceptance Scenarios**:

1. **Given** no definition with a requested identity exists, **when** an author creates a workflow or activity definition, **then** the definition and its initial related state become visible together or neither becomes visible.
2. **Given** a workflow draft with authored state, layout, and validation results, **when** the author updates, promotes, submits, discards, or permanently deletes it, **then** the same domain rules, ordering, atomicity, and published outcomes apply as before the migration.
3. **Given** committed design data, **when** the application and storage connection are restarted, **then** all public read ports reconstruct the same logical design objects without provider artifacts leaking into the domain model.

---

### User Story 2 - Query Design Data Predictably at Scale (Priority: P1)

As a designer or API consumer, I can list, filter, order, page, count, and resolve exact or latest versions with stable results even when the catalog is large.

**Why this priority**: The migration must eliminate transitional load-all behavior and preserve the bounded query contract; otherwise correctness and latency deteriorate as catalogs grow.

**Independent Test**: Seed representative large catalogs containing nulls, missing optional values, duplicate-looking names, multiple semantic versions, and multiple scopes; execute every public query shape and compare complete results and execution evidence with the accepted oracle.

**Acceptance Scenarios**:

1. **Given** a large mixed catalog, **when** a caller applies any supported filter, ordering, page, or count request, **then** the result set and ordering match the public contract and the scale-bearing work completes in storage rather than by loading the catalog into application memory.
2. **Given** definitions with multiple releases and prereleases, **when** a caller requests an exact or latest version, **then** semantic-version ordering and build-metadata-insensitive identity rules are preserved.
3. **Given** two isolated storage scopes containing identical logical identifiers, **when** either scope queries or mutates its definitions, **then** it cannot observe or affect the other scope.

---

### User Story 3 - Choose One Host Storage Provider (Priority: P2)

As an application host, I can select one supported storage provider for design and runtime persistence and validate its required physical schema before serving traffic.

**Why this priority**: A coherent host-level provider choice is the product benefit behind standardization and removes feature-by-feature migration maintenance.

**Independent Test**: Compose the reference host separately with each mandatory provider, validate or apply the declared schema, run representative design and runtime operations, restart it, and verify that no feature-specific provider choice is required.

**Acceptance Scenarios**:

1. **Given** any mandatory provider in a supported deployment shape, **when** the host selects that provider, **then** every workflow- and activity-design store resolves through the same host-owned storage composition.
2. **Given** missing, stale, conflicting, or unsafe physical schema, **when** startup validation or the deployment pipeline checks readiness, **then** it reports the exact incompatibility and does not silently fall back to an unbounded or in-memory path.
3. **Given** a provider deployment that cannot guarantee a required atomic boundary, **when** the host validates the design persistence manifest, **then** readiness fails before an authoring operation can partially commit related design state.

---

### User Story 4 - Maintain One First-Party Implementation (Priority: P3)

As an Elsa maintainer, I can evolve design persistence contracts once without maintaining an EF-specific implementation, migration set, registration path, and test lane in this repository.

**Why this priority**: Removing duplicated implementation infrastructure is the long-term maintenance outcome, but it is safe only after behavioral and performance evidence is complete.

**Independent Test**: Audit source, projects, packages, registrations, samples, tests, and the resolved dependency graph after the migration and prove that design persistence has one concrete first-party implementation while its core contracts remain infrastructure-neutral.

**Acceptance Scenarios**:

1. **Given** all correctness, provider, restart, schema, and performance gates pass, **when** the design migration is finalized, **then** all design-specific EF implementation projects, migrations, registrations, package references, and EF-only tests are removed.
2. **Given** a feature core project, **when** its direct and transitive dependencies are inspected, **then** it has no dependency on Groundwork or another concrete persistence provider.
3. **Given** a future change that reintroduces an EF dependency into the design persistence graph, **when** repository validation runs, **then** the change fails with the dependency path that violated the boundary.

### Edge Cases

- A multi-aggregate write fails after one staged operation but before commit or acknowledgement.
- The same create, promote, submit, or delete request is retried after an ambiguous transport failure.
- Two callers concurrently create the same definition, update the same draft, or promote from the same latest version.
- A stale writer attempts to replace or delete an aggregate after another writer has committed.
- A provider restarts while physical schema work, a backfill, or an authoring transaction is in progress.
- A declared physical name collides after host naming policy and provider normalization.
- Optional fields are null, absent, empty, or contain values at the declared length boundary.
- A query requests an unsupported predicate, sort, projection, or unbounded result shape.
- Related design state is missing, orphaned, duplicated, or has a corrupt serialized payload.
- A host selects a MongoDB deployment without the transaction capability required by multi-document design operations.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Workflow- and activity-design core modules MUST continue to own provider-neutral persistence contracts, models, commands, queries, and invariants and MUST NOT depend directly or transitively on Groundwork or another concrete persistence provider.
- **FR-002**: Every public workflow- and activity-design persistence contract exercised by the product MUST have exactly one first-party concrete implementation in the completed repository state.
- **FR-003**: The implementation MUST preserve the observable behavior of definition, version, draft, layout, validation, lookup, reconciliation, and lifecycle operations, including their domain events and failure outcomes.
- **FR-004**: Logical operations spanning multiple aggregates MUST be atomic: all participating changes become durable together or none become durable.
- **FR-005**: Retrying an operation after a failure or lost acknowledgement MUST converge on the same authoritative outcome without duplicate versions, duplicate events, orphaned related state, or unintended deletion.
- **FR-006**: Stale and conflicting writes MUST produce explicit domain-level conflict outcomes and MUST NOT overwrite newer state.
- **FR-007**: Every supported list, filter, membership, substring, exact-version, latest-version, ordering, paging, and count request MUST preserve the existing public contract's null, missing-value, comparison, semantic-version, and ordering behavior.
- **FR-008**: Every scale-bearing predicate, ordering, page, count, and relationship lookup MUST execute through a bounded server-side storage route; unbounded client evaluation and load-all fallback are forbidden in production composition.
- **FR-009**: Related aggregate reads MUST remain explicit and bounded and MUST NOT require provider-specific navigation behavior in core contracts.
- **FR-010**: Persisted workflow state, activity descriptors, input/output definitions, design facets, layouts, and validation results MUST round-trip as logical domain content; provider-only storage representations MUST NOT become part of the domain contract.
- **FR-011**: The chosen physical form and declared searchable fields for each design document kind MUST be justified by its stable query and write workload, while canonical serialized content remains authoritative.
- **FR-012**: Storage scope and tenant ownership MUST be enforced on every save, load, update, delete, query, count, and multi-aggregate operation, including direct identity lookups.
- **FR-013**: SQLite, SQL Server, PostgreSQL, and MongoDB MUST pass the same black-box design persistence conformance suite for every capability the product advertises.
- **FR-014**: Provider deployment prerequisites, including transaction requirements, MUST be reported truthfully and MUST fail readiness before serving traffic when a required guarantee is unavailable.
- **FR-015**: The complete design storage declaration MUST support deterministic plan, validation, status, and safe application workflows suitable for both application startup and deployment pipelines.
- **FR-016**: Missing, stale, invalid, or conflicting schema MUST be a visible readiness failure and MUST NOT be treated as an empty store or permission to choose a slower execution path.
- **FR-017**: A host MUST be able to select one provider composition that backs workflow design, activity design, and the already-supported persistence lanes without feature code choosing physical providers.
- **FR-018**: All existing tests whose subjects and objectives remain applicable MUST continue to pass; moving or rewiring tests MUST NOT weaken their asserted behavior.
- **FR-019**: EF may remain only as a temporary correctness and performance oracle while this work is in transition. After all exit gates pass, design EF projects, contexts, mappings, migrations, factories, registrations, packages, and EF-only test infrastructure MUST be deleted from this repository.
- **FR-020**: Repository validation MUST prevent direct or transitive EF Core dependencies from returning to the completed design persistence graph and MUST report the violating dependency path.
- **FR-021**: Operator and extension-point documentation MUST describe provider selection, schema validation/application, registered replacement contracts, failure behavior, deployment prerequisites, and the absence of a first-party EF design implementation.
- **FR-022**: No migration path from an existing EF-backed production database is required; the completed behavior targets greenfield deployments.

### Key Entities

- **Workflow Definition**: The stable identity and descriptive metadata of an authored workflow.
- **Workflow Definition Draft**: Mutable authored workflow state plus its associated layout and validation outcome before promotion.
- **Workflow Definition Version**: An immutable, semantically versioned authored snapshot with exact and latest-version lookup behavior.
- **Workflow Definition Version Layout**: The designer-only visual layout associated with an immutable workflow version.
- **Activity Definition**: The catalog identity and provenance for one activity kind.
- **Activity Definition Version**: An immutable semantic version of an activity descriptor, including inputs, outputs, design facets, and reconciliation identity.
- **Storage Scope**: The isolation boundary within which design identities, uniqueness, reads, writes, and physical routes are evaluated.
- **Design Storage Declaration**: The versioned description of design document kinds, searchable fields, physical forms, names, indexes, and required provider capabilities.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: One shared behavioral suite passes 100% of workflow- and activity-design contract scenarios on SQLite, SQL Server, PostgreSQL, and MongoDB, including restart, concurrency, isolation, and failure-recovery scenarios.
- **SC-002**: Execution evidence for 100% of scale-bearing design query shapes shows bounded storage-side filtering, ordering, paging, and counting with no full-catalog application-memory fallback.
- **SC-003**: All injected partial-failure and lost-acknowledgement scenarios leave either the complete intended aggregate transition or no transition, with zero duplicates and zero orphaned related records.
- **SC-004**: At representative catalog scale, ordinary design-store operations have p95 latency no worse than 1.25 times the accepted oracle, throughput of at least 80% of the oracle, and p99 latency no worse than 2 times the oracle.
- **SC-005**: Any design document kind selecting an entity-style physical form demonstrates a repeatable improvement over both shared and dedicated-document forms on its representative workload.
- **SC-006**: The reference host completes schema readiness checks and representative design create/read/update/delete/version flows with each mandatory provider using one host-level provider choice.
- **SC-007**: After final cleanup, the design persistence source, project, package, registration, test, and resolved dependency graphs contain zero EF Core implementation artifacts, while all design core projects contain zero concrete-provider dependencies.
- **SC-008**: A deliberate reintroduction of a direct or transitive EF dependency is rejected automatically and reports the full violating dependency path.

## Assumptions

- Existing public design contracts, constitutional invariants, and behavior tests are the authoritative compatibility surface; provider-internal structure is not.
- The Groundwork physical storage, bounded query, schema planning, migration CLI, session, tenancy, and provider foundations already landed upstream are available through a versioned dependency before this slice is finalized.
- MongoDB deployments that participate in multi-document design transactions use a topology that supplies the required atomic boundary; unsupported topologies fail readiness rather than receive weaker semantics.
- EF implementations may be used temporarily to establish parity and benchmark evidence but are removed from the repository within this work unit once the exit gates pass.
- The product is greenfield and unreleased, so historical EF database conversion and compatibility packages are outside this feature.
- The Design and Runtime bounded-context split and design-only, runtime-only, and combined deployment shapes remain unchanged.

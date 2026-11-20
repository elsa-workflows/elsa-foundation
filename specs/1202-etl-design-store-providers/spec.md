# Feature Specification: The ETL design store on all four EF engines

**Feature Branch**: `agent/claude/2908-etl-design-providers` (stacked on `agent/claude/2908-rabbitmq-source`)

**Created**: 2026-11-03

**Status**: Implemented — [evidence](evidence/README.md)

**Input**: The [store qualification obligation](https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6323246976) of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908): [ADR 0073 D1](../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md) names SQLite, SQL Server, PostgreSQL and MySQL for first-party durable contracts, and the ETL design and runtime EF bindings support SQLite only. This slice qualifies the design store; the runtime store is the next slice. Part of [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

## Context

The ETL design store keeps mutable definition drafts and immutable published artifacts (`EtlDesignDbContext`, `EfEtlDefinitionStore`, `EfEtlDefinitionCatalog`). Its registration throws `NotSupportedException` for any provider but SQLite, its `[EfModule]` names only a SQLite context, and only a SQLite migration chain exists.

Two behaviours depend on SQLite:
- The catalog pages definitions with `EF.Functions.Collate(row.DefinitionKey, "BINARY")`, a collation name only SQLite knows.
- Publication and draft writes rely on SQLite's single writer: a `Serializable` transaction there is `BEGIN IMMEDIATE`, so concurrent publications of one draft serialise. On PostgreSQL a lost race raises a serialization failure (40001), on SQL Server a deadlock (1205), and on MySQL a deadlock (1213); none is handled, so a caller sees a provider exception where SQLite gives `Published`, `Conflict` or the existing publication.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Keep ETL definitions in the host's database (Priority: P1)

A host that keeps its Elsa state in SQL Server, PostgreSQL or MySQL selects that provider for the ETL design store, and drafts, publications and the catalog behave as they do on SQLite.

**Independent Test**: the design-store contract suite passes against real SQL Server, PostgreSQL and MySQL containers, and against SQLite.

**Acceptance Scenarios**:

1. **Given** the `EtlDesign` module configured with provider `SqlServer`, `PostgreSql` or `MySql`, **When** the host starts with migrations applied, **Then** the store creates, updates and publishes drafts, finds published artifacts, and lists the catalog, with the same results as on SQLite.
2. **Given** definition ids that differ only by case, **When** the catalog pages through them, **Then** every definition appears exactly once, in definition-key order, on every engine.
3. **Given** two writers publishing the same draft revision at once, **When** both commit, **Then** exactly one publication is recorded and the other writer gets `Conflict` or the existing publication, on every engine, never a provider exception.
4. **Given** two writers updating the same draft revision at once, **Then** exactly one update wins and the other gets `Conflict`. (The conditional update already guarantees this on every engine; the scenario is in the contract to keep it so.)

### User Story 2 — Upgrade without losing SQLite history (Priority: P1)

**Acceptance Scenarios**:

1. **Given** an existing SQLite design database, **When** the host upgrades, **Then** the retained SQLite migration chain applies unchanged and no SQLite schema change is made.
2. **Given** a fresh SQL Server, PostgreSQL or MySQL database, **When** the host migrates, **Then** one generated initial migration creates the current design schema, the database validates, and the model matches the migrations (no pending model changes).

### Edge Cases

- **Identity widths.** Draft and artifact keys are 64-character hashes, so keys fit every engine's index limits; the encoded tenant, partition and definition ids (up to 1,200 characters) are payload columns only.
- **Collation.** Key columns (and the indexed schema-version stamps) use the engine's binary collation (`EfOrdinalCollation`), so equality and ordering agree with `StringComparer.Ordinal` and EF's identity map agrees with the server. The pipeline and envelope hashes are compared only in memory. Definition keys are uppercase hexadecimal hashes, which sort alike under any collation, so the binary collation is proven from the generated model and the server's recorded column collation rather than by paging.
- **Retry budget.** A publication that loses a serialization race (a serialization failure, a deadlock or a lock timeout, or SQLite reporting the database busy or locked) is retried as a whole unit a bounded number of times, on every engine; a draft update retries its one conditional statement, and every read outside a publication attempt retries, within the same bound. A real competitor settles on the next attempt through the existing-publication check; running out of attempts means the database kept refusing, which surfaces as the last provider failure, as the store's other storage failures already do, not as `Conflict`.
- **Out of scope.** The ETL runtime store, which needs narrower keys, provider-specific check constraints and retried units across its transitions, is the next slice; until then its registration still refuses other providers.

## Requirements *(mandatory)*

- **FR-001** `EtlDesign` declares SQL Server, PostgreSQL and MySQL contexts beside SQLite, each applying the binary collation (unbounded text takes each provider's default type: `TEXT`, `nvarchar(max)`, `text`, `longtext`); registration selects the context by provider.
- **FR-002** Each new engine has a generated initial migration, made with an ETL-owned design-time tooling project and script (as Rules has), generated as internal classes; the retained SQLite chains are untouched.
- **FR-003** The catalog pages by the column's binary collation, with no provider-specific function in the query.
- **FR-004** Publication retries a transient write conflict (serialization failure, deadlock, lock timeout, a busy or locked SQLite database) as a whole unit within a bounded budget and otherwise keeps its contract outcomes; draft create and update keep their single conditional statements; an update retries its statement on a transient write conflict, and every store and catalog read outside a publication attempt retries one too (on SQL Server a read can be a publication deadlock's victim).
- **FR-005** On each engine the provider suite migrates a fresh database, validates it, and finds no pending model changes.
- **FR-006** A design-store contract suite runs on SQLite in the ETL suite and on the three server engines in a container suite listed in `tools/ci/test-tiers.json`.
- **FR-007** The ETL README, the persistence README and the module's manifest describe the four providers.

## Success Criteria *(mandatory)*

- **SC-001** The contract suite passes on all four engines, including the case-only catalog case and the concurrent publish and update races.
- **SC-002** A fresh database on each engine migrates and validates with no pending model changes, and the generated models carry the binary collation on the key columns.
- **SC-003** Every new guard is bite-proven or named in the evidence README as not bite-proven.
- **SC-004** Existing SQLite design databases and existing artifacts are unchanged.

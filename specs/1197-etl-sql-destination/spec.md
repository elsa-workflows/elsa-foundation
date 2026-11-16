# Feature Specification: Transactional keyed SQL delivery

**Feature Branch**: `agent/claude/2908-sql-destination` (from `main`)

**Created**: 2026-11-03

**Status**: Implemented — [PR #3699](https://github.com/elsa-workflows/elsa-foundation/pull/3699); [evidence](evidence/README.md)

**Input**: The write half of the fourth criterion of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908): "Both PostgreSQL and SQL Server support snapshot/watermark reads and transactional keyed writes, with real provider-specific recovery evidence. Log CDC is deferred." Reads are a later slice. Part of [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

## Context

A v2 pipeline delivers its main route to a JSON Lines file, a CSV file or an HTTP endpoint. None of these is a database.

This slice adds a `sql` destination for PostgreSQL and SQL Server. Each batch is written in one database transaction:
- **Ledger:** a row in a ledger table records the batch's effect identity and content hash.
- **Rows:** each record is upserted by an authored key.

A batch whose effect identity is already in the ledger is not written again. That makes a database write exactly once per effect, even when a worker crashes after the commit and before the runtime records it. This is stronger than the at-least-once guarantee the other destinations give, and the evidence demonstrates it on each engine.

The ledger's primary key also serializes two lease holders that overlap (leases do not grant exclusivity), because the loser's transaction rolls back.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Upsert a pipeline's records into a table (Priority: P1)

An author points a pipeline's main route at a host-approved PostgreSQL or SQL Server binding, a table and a key. Each record lands as a row: inserted when its key is new, updated when it exists.

**Independent Test**: run pipelines into a real PostgreSQL container and a real SQL Server container. The table must hold exactly the latest value per key.

**Acceptance Scenarios**:

1. **Given** an empty table whose key columns carry a unique constraint, **When** a run delivers records, **Then** each key has one row holding the record's mapped fields.
2. **Given** a second run whose records share keys with the first, **When** it delivers, **Then** those rows are updated, new keys are inserted, and other rows are untouched.
3. **Given** a batch that repeats a key, **When** it is written, **Then** the record that comes later in the batch wins, without an error.
   - Each record is its own upsert statement, so keys repeat under the database's own collation.
   - For example, "abc" and "ABC" are the same key on SQL Server's default collation.
4. **Given** a table without a unique constraint or index on exactly the key columns, **When** the first batch is written, **Then** the run fails with `etl.sql-key-constraint-missing`, and nothing is committed.

### User Story 2 — Recover without writing twice (Priority: P1)

A worker dies after committing a batch to the database but before the runtime records the delivery. A replacement delivers the batch again, but the database changes only once.

**Independent Test**: in real worker processes, kill the worker between the database commit and the delivery record. The ledger must hold one row per batch, and the table must equal the fed records.
- The worker fixture writes through the SQLite test engine.
- On real PostgreSQL and SQL Server, the container suite proves the same replay at the writer: an applied effect redelivered writes nothing.

**Acceptance Scenarios**:

1. **Given** a batch whose transaction committed, **When** it is delivered again under the same effect identity, **Then** the writer finds the ledger row, writes nothing, and the runtime records the delivery.
   - This includes a writer that waited on the ledger lock and then saw a unique violation.
2. **Given** a ledger row with the same effect identity but a different content hash, **When** a batch is delivered, **Then** the run fails with `etl.effect-conflict`, and nothing is written.
3. **Given** a statement that fails mid-batch, **Then** the whole transaction rolls back (SQL Server runs it under `SET XACT_ABORT ON`), and neither the ledger row nor any row remains.

### User Story 3 — Fail cleanly or wait (Priority: P1)

**Acceptance Scenarios**:

1. **Given** a deterministic rejection, **Then** the run fails with `etl.sql-write-rejected` and does not retry. Examples: a value the column type refuses, a NOT NULL violation, a missing table or column, or a permission error.
2. **Given** a transient failure, **Then** the writer leaves the route unrecorded and the worker yields, so the batch is retried later. Examples: a connection failure, a timeout, a deadlock or a serialization failure.
   - A database outage pauses the run rather than failing it.
   - Each engine package decides which errors are transient.

### User Story 4 — Author and inspect a SQL destination (Priority: P2)

An author picks a SQL binding for the main route in Studio, names the table and selects key fields. An operator sees a run's recorded SQL deliveries.

**Independent Test**: component tests cover the authoring controls, and a fixture-backed browser test checks them with axe at 320 px and 1024 px.

### Edge Cases

- **Main route only.** The error route's shape is fixed by the runtime, so an error route of kind `sql` is refused with `destination.sql-route-unsupported`.
- **Identifiers.** The compiler enforces the rules for a `sql` destination:
  - The table is `name` or `schema.name`. Each part, and every output field name (they are the columns), matches `[A-Za-z_][A-Za-z0-9_]{0,62}`.
  - Output field names are distinct ignoring case, with `destination.sql-identifier-invalid`.
  - The host binding must allow the table, compared exactly, or resolution fails with `etl.sql-table-not-allowed`. The binding's ledger table must also be an identifier.
  - The dialect quotes every identifier. Values are always parameters.
- **Key.**
  - One to eight distinct output fields, each mapped from a required source field, so a key is never null.
  - A violation is refused with `destination.sql-key-invalid`.
  - Before its first upsert in a batch, the writer checks the catalog for a unique constraint or index on exactly the key columns.
- **Types.** `EtlColumnType` has String, Int64, Decimal and Boolean. The writer reads each output field's type from the pinned pipeline's mapping, not from the JSON body, and binds a typed parameter (DB null for a null value). The table's column types are the operator's choice; a value the engine refuses is a deterministic rejection.
- **Ordering.** The latest value per key is defined within one partition. Records sharing a key across concurrently processed partitions or shards land in an unspecified order. The README states this.
- **Chunks.** Upsert statements are sent in commands that stay under each engine's parameter limit (2,100 on SQL Server), all inside the batch's single transaction.
- **Ledger.**
  - It is the binding's `LedgerTable` (default `elsa_etl_effect_ledger`), holding the effect identity as primary key, run and batch identities, the content hash and the commit time.
  - With `CreateLedger` (the default), the writer creates it on first use. A concurrent creation that loses ("already exists": PostgreSQL 42P07, a pg_type unique violation; SQL Server 2714) counts as created.
  - Without it, a missing ledger fails with `etl.sql-ledger-missing`.
  - The runtime never deletes ledger rows. An operator may delete the rows of terminal runs only; deleting a row whose run is still running can allow a second write. The README states this.
- **PostgreSQL specifics.**
  - The writer creates a ledger only when `to_regclass` finds none, so a role that may only write a pre-created ledger runs no DDL.
  - The key check resolves the table through `search_path`, counts only key columns (an `INCLUDE` index qualifies), and refuses a deferrable constraint.
- **Credentials.** The binding holds a connection string, supplied through host configuration like other Elsa connection strings. Neither the artifact nor the persisted pins hold it. The pinned handle hashes the provider and the target the engine parses out of the connection string (host, port, database and user), plus the allowed tables and ledger settings, never the password.
- **Packages.** `IEtlSqlEngine` is in `Elsa.Etl.Core`, with no driver. The shared writer and bindings are in `Elsa.Etl.Runtime`. `Elsa.Etl.Sql.PostgreSql` (Npgsql) and `Elsa.Etl.Sql.SqlServer` (Microsoft.Data.SqlClient) each register one engine. Without the binding's engine:
  - binding resolution fails at admission with `etl.sql-unavailable`;
  - a worker that claims such a run fails it with the same code.
- **Mixed versions.** A worker without this slice cannot read a `sql` artifact: its preparation claim throws while reading the pinned artifact and is retried, without failing the run, so it can hold up other preparations in its scope (#3709). All workers in a scope must run this slice before a `sql` pipeline is admitted.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** `EtlSqlDestination(Binding, Table, Key)` is a new destination of kind `sql`. `EtlConnectorKind` gains `Sql`, appended, so existing integer-hashed identities are unchanged. Existing artifacts and identities are byte-identical.
- **FR-002** The compiler enforces the identifier, key and main-route rules in Edge Cases.
- **FR-003** `EtlRuntimeFeature.SqlBindings` holds `EtlHostSqlBindingOptions`:
  - scope, alias and revision;
  - role `MainOutput`;
  - provider name;
  - connection string;
  - allowed tables;
  - `LedgerTable` and `CreateLedger`.

  Resolution pins a handle without secrets.
- **FR-004** `IEtlSqlEngine`, implemented by each engine package, provides:
  - the provider name;
  - opening a connection;
  - parsing the connection target;
  - identifier quoting;
  - the single-row upsert, ledger DDL, ledger insert and unique-key catalog query;
  - the classification of an exception as transient, duplicate key, already exists, missing object, missing constraint or rejection;
  - a key count of -1 when the target is not a base table (missing, a view, or another non-table relation).
- **FR-005** The route body of a `sql` route is the batch's mapped records as a JSON array, retained like an HTTP body. The effect identity and body hash are as for other routes.
- **FR-006** The runtime's writer handles one route body in one transaction:
  1. ensure the ledger;
  2. insert the ledger row;
  3. on a duplicate, compare hashes and return `Replay` or refuse with `etl.effect-conflict`;
  4. check the key's unique index;
  5. upsert every record, in order, in chunked commands;
  6. commit.

  Infrastructure exceptions are wrapped at the boundary, as transient (the worker yields) or deterministic (an `EtlEffectException` with a code).
- **FR-007** After `Applied` or `Replay`, the output processor records the route through the route-publication transition. The expected reference is the route body keyed `EtlRuntimeIdentity.ConnectorSqlEffectKey(effectId)`. Every kind switch that today admits only file kinds for publication admits `Sql`:
  - the acceptance store's publication and delivered checks;
  - the output store's effect mechanism, where SQL shares the recorded-publication value (2);
  - the record codec;
  - the runtime inspector;
  - the runtime API's effect states;
  - the output processor's route format, kind maps and dispatch.
- **FR-008** The engines are injected into the connector execution and output processor. A missing engine fails the run with `etl.sql-unavailable`.
- **FR-009** Studio: the `sql` destination kind for the main route, with binding, table and key selection. Inspection shows recorded SQL deliveries.
- **FR-010** The ETL README, the Core README and the affected `EXTENSION_POINTS.md` files describe:
  - the destination;
  - exactly-once per effect, and its ordering and ledger-retention limits;
  - the bindings;
  - the packages;
  - the engine contract.

### Key Entities

- **SQL destination**: an authored table and key on a host-approved SQL binding.
- **Effect ledger**: a table in the target database that holds one row per applied effect identity.
- **SQL engine**: the per-provider adapter that isolates a driver.

## Success Criteria *(mandatory)*

- **SC-001** Against real PostgreSQL 16 and SQL Server 2022 containers, the writer upserts by key as US1 states, including a repeated key in one batch and a missing unique key failing closed. A whole pipeline runs against PostgreSQL in the Workbench journey.
- **SC-002** In real worker processes, using the SQLite test engine, and with the writer-level replay proven on each real engine:
  - a worker killed between the commit and the delivery record leaves exactly one ledger row per batch;
  - the table equals the fed records;
  - the run settles with every record accounted for. The fixture rejects one record on a lookup miss, so its outcome is `BusinessError`.
- **SC-003** On each engine:
  - a replayed batch writes nothing;
  - a hash conflict is refused;
  - a rejected value fails the run without writing;
  - a transient failure yields and later succeeds, on the SQLite test engine. On PostgreSQL the transient classification of its error codes is proven, and on SQL Server it is not tested.
- **SC-004** Every behavioural requirement has a test that fails when its guard is mutated, recorded as a bite-proof, except the guards the evidence README names.
- **SC-005** An actual Workbench journey delivers to a PostgreSQL container from a native Flowchart.
- **SC-006** Existing pipelines keep their bytes and identities. The ETL suite passes, and the new container suite passes in CI's container shard, which runs in the full tier.

## Assumptions and boundaries

- **Deferred:**
  - SQL reads, which are the next slice;
  - log CDC;
  - deletes;
  - target schema creation;
  - MySQL;
  - a SQL connection type in the Connections domain.
- **Not changed:** the ETL runtime and design stores stay on SQLite. Qualifying them on more engines is a separate obligation.

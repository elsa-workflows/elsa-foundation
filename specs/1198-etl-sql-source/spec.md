# Feature Specification: SQL snapshot reads

**Feature Branch**: `agent/claude/2908-sql-source` (stacked on `agent/claude/2908-sql-destination`)

**Created**: 2026-11-03

**Status**: Implemented; [evidence](evidence/README.md)

**Input**: The snapshot half of the read side of the fourth criterion of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908): "Both PostgreSQL and SQL Server support snapshot/watermark reads and transactional keyed writes, with real provider-specific recovery evidence. Log CDC is deferred." Keyed writes are [spec 1197](../1197-etl-sql-destination/spec.md). Watermark (incremental) reads are a later slice. Part of [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

## Context

A v2 pipeline reads a CSV file, a JSON Lines file or a paginated HTTP endpoint. None of these is a database.

This slice adds a `sql` source for PostgreSQL and SQL Server that reads a whole host-approved table as of one point in time:
- **One snapshot.** Preparation selects the declared columns in a single read transaction under the engine's snapshot isolation, ordered by an authored key.
- **One retained input.** The rows are written as a JSON Lines input snapshot, content-hashed and published atomically, then indexed. From there on, the run is a JSON Lines run: two or more leased partitions, file positions, replay and acceptance are unchanged.

Every partition therefore reads the same point in time, however long the run takes, and no database connection is held while records are processed. A worker that dies after the snapshot is published reuses it, so a replacement never reads a second, different snapshot.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Read a table into a pipeline (Priority: P1)

An author points a pipeline's source at a host-approved PostgreSQL or SQL Server binding, a table, its columns and an order key. Every row becomes one record.

**Independent Test**: run pipelines from a real PostgreSQL container and a real SQL Server container. The delivered records must equal the table's rows in key order.

**Acceptance Scenarios**:

1. **Given** a table with rows, **When** a run reads it with two or more partitions, **Then** every row is delivered exactly once to the routes, as a record whose ordinal follows the key order.
2. **Given** an empty table, **When** a run reads it, **Then** the run completes with no records.
3. **Given** a NULL in a column declared required, **When** the run reads it, **Then** that record goes to the error route, as a null required JSON Lines field does, and the other records are delivered.
4. **Given** a column whose database type does not convert to its declared type, **When** preparation reads it, **Then** the run fails with `etl.sql-column-type-unsupported`, naming the column, and no partition becomes runnable.

### User Story 2 — One point in time (Priority: P1)

Writers keep changing the table while the run reads it. The run sees the table as it was at one instant.

**Independent Test**: on each real engine, change the table after preparation and before the partitions run. The delivered records must equal the table before the change.

**Acceptance Scenarios**:

1. **Given** a prepared run, **When** rows are inserted, updated or deleted afterwards, **Then** the run still delivers the rows as of preparation.
2. **Given** SQL Server with `ALLOW_SNAPSHOT_ISOLATION` off, **When** preparation reads, **Then** the run fails with `etl.sql-read-rejected`. The README tells operators to enable it.

### User Story 3 — Recover without a second snapshot (Priority: P1)

**Independent Test**: in real worker processes, kill the worker after the snapshot is published and before the preparation is recorded. The replacement must reuse the published snapshot, even though the table has since changed.

**Acceptance Scenarios**:

1. **Given** a published snapshot whose preparation was not recorded, **When** preparation runs again, **Then** it verifies and reuses that snapshot and does not query the database.
2. **Given** a transient failure while reading, such as a connection failure or a timeout, **Then** the run is not failed. The preparation is left unrecorded and is claimed again once its lease expires.
3. **Given** a deterministic refusal while reading, such as a missing table or column, or a permission error, **Then** the run fails with `etl.sql-read-rejected`.

### User Story 4 — Author and inspect a SQL source (Priority: P2)

An author picks a SQL source binding in Studio, names the table, declares columns and selects key columns. Inspection shows the run's source as a SQL snapshot with its record count.

**Independent Test**: component tests cover the authoring controls, and a fixture-backed browser test checks them with axe at 320 px and 1024 px.

### Edge Cases

- **Identifiers.** The table is `name` or `schema.name`; each part and each column name matches `[A-Za-z_][A-Za-z0-9_]{0,62}`. Column names are distinct ignoring case. A violation is refused with `source.sql-identifier-invalid`. The dialect quotes every identifier.
- **Key.** One to eight distinct declared columns, each declared required, refused otherwise with `source.sql-key-invalid`. The key orders the snapshot; it need not be unique. Rows with equal keys keep the order the engine returns, so the README recommends a unique key for reproducible ordinals.
- **Allowed tables.** The binding role is `Source`. The host binding must allow the table, compared exactly, or resolution fails with `etl.sql-table-not-allowed`, as for destinations.
- **Types.** Each declared column is read as follows; any other database value fails preparation with `etl.sql-column-type-unsupported`.

  | Declared type | Accepted database values |
  |---------------|--------------------------|
  | `String` | text; a GUID; a date, time or timestamp, in ISO 8601 invariant form |
  | `Int64` | 8-, 16-, 32- and 64-bit integers |
  | `Decimal` | exact decimals within .NET `decimal` range, and integers |
  | `Boolean` | booleans (`bit` on SQL Server) |

  Floating-point columns are refused, so a value is never rounded silently.
- **Size.** The snapshot is bounded by the pipeline's `MaxSnapshotBytes` and each record by `MaxRecordBytes`, with `etl.snapshot-too-large` and `etl.source-record-too-large`. The record count is bounded as for JSON Lines input.
- **Isolation.**
  - PostgreSQL reads in a `REPEATABLE READ READ ONLY` transaction, which is snapshot isolation there.
  - SQL Server reads under `SNAPSHOT` isolation, which needs the database option `ALLOW_SNAPSHOT_ISOLATION ON`.
- **Finite only.** A continuous pipeline with a `sql` source is refused with `etl.continuous-source-unsupported`, as for file sources.
- **Credentials.** As for destinations: the binding's connection string is never pinned or exposed, and the handle hashes the secret-free target, the role and the allowed tables.
- **Mixed versions.** A worker without this slice that claims a `sql`-source run fails it with `etl.executable.invalid-pipeline`. All workers in a scope must run this slice before such a pipeline is admitted.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** `EtlSqlSource(Binding, Table, Columns, Key)` is a new source of kind `sql`; `Columns` are `EtlColumn` declarations. Existing artifacts and identities are byte-identical.
- **FR-002** The compiler enforces the identifier and key rules in Edge Cases, and the source column count against `MaxFields`.
- **FR-003** SQL bindings accept the role `Source`. Source and destination bindings share `EtlRuntimeFeature.SqlBindings`, and binding resolution pins a `Sql` source revision with no expected digest.
- **FR-004** `IEtlSqlEngine` gains the snapshot read: the isolation level, a statement selecting the columns ordered by the key, and a session statement that makes the transaction read-only where the engine supports it.
- **FR-005** The connector preparer, for a `sql` source:
  1. looks for a published input snapshot under the run's snapshot identity, and reuses it when present;
  2. otherwise opens a connection, begins a transaction at the engine's snapshot isolation, streams the ordered rows, converts each value by its declared type, and writes one JSON object per row through the JSON Lines snapshot publisher;
  3. indexes the snapshot with the JSON Lines indexer;
  4. returns the snapshot and index as the run's prepared file input.

  Infrastructure exceptions are wrapped at the boundary: a transient one returns no result, so the lease lapses and preparation is retried; a deterministic one returns `etl.sql-read-rejected`.
- **FR-006** From preparation on, a `sql` source is processed as a JSON Lines source whose fields are its columns, each selected by the pointer `/{name}`. Every site that admits a JSON Lines file source admits a `sql` source, with its integrity anchored on the recorded snapshot hash instead of a pinned digest:
  - the preparer's admission checks;
  - the batch builder;
  - the record codec's binding and preparation validation;
  - the executable reader;
  - the row transformer's fields;
  - the output processor's source field types;
  - the runtime API's source input.
- **FR-007** Studio: the `sql` source kind, with binding, table, columns and key. Inspection shows the source as a SQL snapshot.
- **FR-008** The ETL README, the Core README, the engine READMEs and the Core `EXTENSION_POINTS.md` describe the source, its isolation, its recovery and its limits.

### Key Entities

- **SQL source**: an authored table, columns and key on a host-approved SQL binding.
- **Input snapshot**: the run's retained JSON Lines copy of the table, as of one transaction.

## Success Criteria *(mandatory)*

- **SC-001** Against real PostgreSQL 16 and SQL Server 2022 containers, preparation reads a table into a snapshot whose records equal the rows in key order, including NULLs, every accepted type and an empty table, and refuses an unsupported type.
- **SC-002** On each real engine, a change committed after preparation does not reach the run, and a missing table is refused with `etl.sql-read-rejected`. On SQL Server, a database without snapshot isolation is refused.
- **SC-003** In real worker processes, a worker killed between the snapshot publication and the preparation record is replaced, and the replacement reuses the snapshot after the table changed. The run delivers the original rows across two or more partitions.
- **SC-004** A transient read failure leaves the run preparing and a later attempt succeeds, on the SQLite test engine.
- **SC-005** Every behavioural requirement has a test that fails when its guard is mutated, recorded as a bite-proof, except the guards the evidence README names.
- **SC-006** An actual Workbench journey reads a PostgreSQL table into a PostgreSQL table from a native Flowchart.
- **SC-007** Existing pipelines keep their bytes and identities. The ETL suite passes, and the container suite passes in CI's full tier.

## Assumptions and boundaries

- **Deferred:**
  - watermark (incremental) reads and continuous SQL sources, the next slice;
  - authored queries, filters pushed down to the database and joins;
  - log CDC;
  - MySQL.
- **Not changed:** the ETL runtime and design stores stay on SQLite.

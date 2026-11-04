# Feature Specification: SQL watermark reads

**Feature Branch**: `agent/claude/2908-sql-watermark` (stacked on `agent/claude/2908-sql-source`)

**Created**: 2026-11-03

**Status**: Implemented — [PR #3717](https://github.com/elsa-workflows/elsa-foundation/pull/3717); [evidence](evidence/README.md)

**Input**: The watermark half of the read side of the fourth criterion of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908): "Both PostgreSQL and SQL Server support snapshot/watermark reads and transactional keyed writes, with real provider-specific recovery evidence." Snapshot reads are [spec 1198](../1198-etl-sql-source/spec.md); keyed writes are [spec 1197](../1197-etl-sql-destination/spec.md). Part of [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

## Context

A spec 1198 `sql` source reads a whole table as of one point in time. Reading it again reads every row again.

This slice adds an optional watermark column to a `sql` source. A run then reads only the rows whose watermark is above the high watermark of the last completed run of the same definition, source binding and table. The read is still one point-in-time snapshot. The run's own high watermark is committed in the same transaction that settles the run, so a run that faults or is cancelled never advances it, and the next run reads those rows again.

Running such a definition repeatedly, for example from a scheduled workflow, therefore loads a table incrementally.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Load only what changed (Priority: P1)

An author names an `Int64` watermark column whose values become visible in increasing order, for example one stamped by a single writer. The first run reads every row; each later run reads only rows whose watermark rose above what the previous completed run read.

**Independent Test**: on real PostgreSQL and SQL Server, run the same definition twice with rows inserted and updated in between. The second run must deliver exactly the new and changed rows.

**Acceptance Scenarios**:

1. **Given** no completed run of the definition, **When** a run reads the source, **Then** it reads every row, and on completion the checkpoint holds the run's highest watermark.
2. **Given** a checkpoint, **When** a run reads the source, **Then** it reads only rows whose watermark is greater than the checkpoint (and any whose watermark is NULL, which it rejects), and on completion the checkpoint rises to the run's highest watermark.
3. **Given** no row above the checkpoint and none with a NULL watermark, **When** a run reads the source, **Then** it completes with no records and the checkpoint is unchanged.
4. **Given** a run that completes with records rejected to the error route (a business error with `etl.rows-rejected`), **When** it settles, **Then** the checkpoint advances, as every record was routed.

### User Story 2 — Never skip rows after a failure (Priority: P1)

**Acceptance Scenarios**:

1. **Given** a run settled by a failure rather than by completing every record (a fault, a cancellation, or a delivery failure such as `etl.sql-write-rejected`), **When** it settles, **Then** the checkpoint is unchanged, and the next run reads the same rows again.
2. **Given** a worker killed after the snapshot was published, **When** a replacement prepares, **Then** it reuses the snapshot, and the run's high watermark is the snapshot's.
3. **Given** two overlapping runs of the same definition, **When** both settle, **Then** the checkpoint is the higher of their high watermarks; it never moves backwards.

### Edge Cases

- **Watermark column.** It is one of the declared columns, declared `Int64` and required, refused otherwise with `source.sql-watermark-invalid`. A NULL watermark in a row is a record error, as for any required column, and does not count towards the high watermark. The filter keeps such rows, so they are rejected again on every run until fixed, rather than skipped once a checkpoint exists.
- **Ordering.** The snapshot is still ordered by the key. The watermark only filters.
- **Commit order.** Rows are read once their watermark is above the checkpoint; a row committed with a watermark at or below a checkpoint already passed is never read. Values drawn at insert, such as a sequence or identity, can commit out of order under concurrent writers, and a per-row version counter is not monotonic across rows, so neither is a safe watermark. The README requires a column whose values become visible in increasing order. A commit-ordered watermark derived by the engine is deferred (#3719).
- **Identity.** The checkpoint is keyed by the scope, the definition identifier, the source binding alias and its pinned secret-free handle (which hashes the database target and the binding revision), the table and the watermark column. Publishing a new definition version keeps it; changing the alias, the database it points to, the binding revision, the table or the column starts again from all rows.
- **Overlapping runs.** The checkpoint never moves backwards, but runs of one definition must not overlap: a run that finishes last can write an older row over a newer one in a keyed destination, and the checkpoint no longer reads that row. Serialising them is deferred (#3721).
- **Finite only**, as for snapshot reads.
- **Persisted state.** The prepared run gains an optional high watermark, written only when the source has a watermark column, so existing prepared runs keep their bytes. The checkpoint is a new runtime table, added by a migration.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** `EtlSqlSource` gains an optional `Watermark` column name, omitted from JSON when absent, so existing artifacts keep their bytes. The compiler and the worker refuse an invalid watermark with `source.sql-watermark-invalid` and `etl.executable.invalid-pipeline`.
- **FR-002** `IEtlConnectorRuntimeStore` gains `FindConnectorSourceWatermarkAsync`, returning the checkpoint for a key or null; its default implementation returns null.
- **FR-003** The connector execution reads the checkpoint before preparation and passes it to the preparer. The snapshot reader filters `watermark > @floor`, keeping NULL watermarks, when a floor exists, and the preparer records the snapshot's highest non-null watermark on the prepared run.
- **FR-004** When a run with a recorded high watermark completes every record (it settles as succeeded, or as business error with `etl.rows-rejected`), the store raises the checkpoint to that high watermark in the same transaction, unless the checkpoint is already higher. A zero-record run records no high watermark and changes nothing.
- **FR-005** `IEtlSqlEngine.SelectSnapshot` gains the optional watermark column; engines render `WHERE ({watermark} > @watermark OR {watermark} IS NULL)` when it is given, so a row with a NULL watermark is read, and rejected as a record error, on every run.
- **FR-006** Studio offers the watermark column on a `sql` source. The ETL README and engine catalog describe the watermark, its checkpoint and the monotonic-writer requirement.

## Success Criteria *(mandatory)*

- **SC-001** On real PostgreSQL 16 and SQL Server 2022, the reader filters by the floor. The highest watermark is read back from the retained snapshot, whatever the engine, and is proven on the SQLite test engine and by the Workbench journeys' second runs.
- **SC-002** Through the worker and store, two runs of one definition deliver all rows and then only the new and changed rows; a run settled by a delivery failure leaves the checkpoint unchanged; a third run with no change delivers nothing.
- **SC-003** In real worker processes, a worker killed after publishing its snapshot is replaced, and the completed run's checkpoint equals the snapshot's high watermark.
- **SC-004** An actual Workbench journey loads a PostgreSQL table into a PostgreSQL table incrementally across two runs from native Flowcharts, and the same on SQL Server.
- **SC-005** Every new guard has a test that fails when it is mutated, recorded as a bite-proof, except the guards the evidence README names.
- **SC-006** Existing artifacts, prepared runs and identities keep their bytes; the migration applies to an existing runtime database.

## Assumptions and boundaries

- **Deferred:** continuous SQL polling (a continuous `sql` source), watermark types other than `Int64`, a checkpoint reset API, serialising overlapping runs on one checkpoint (#3721), and log CDC.
- **Not changed:** the snapshot read, its isolation and its recovery (spec 1198).

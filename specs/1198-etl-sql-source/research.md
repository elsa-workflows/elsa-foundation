# Research: SQL snapshot reads

## R1 — Materialise the snapshot at preparation

**Decision**: Preparation reads the whole table in one transaction and retains it as a JSON Lines input snapshot. Partitions read that snapshot.

**Alternatives**:
- **Keyset pages read by each partition.** Every page would be its own transaction, so a run would mix points in time. Only a transaction held for the whole run would prevent that, and a transaction cannot outlive a lease.
- **Exported snapshots (`pg_export_snapshot`).** PostgreSQL only, and the exporting transaction must stay open while the importers run.

**Rationale**: one transaction gives one point in time on both engines, and no connection is held while records are processed. Retaining the rows reuses the JSON Lines run unchanged, with its partitions, positions, replay, acceptance and inspection.

## R2 — Snapshot isolation per engine

**Decision**:
- PostgreSQL uses `REPEATABLE READ` with `SET TRANSACTION READ ONLY`, then `LOCK TABLE … IN ACCESS SHARE MODE` before the `SELECT`. `TRUNCATE` and table-rewriting `ALTER TABLE` are not MVCC-safe, and a `SELECT` takes its snapshot before it waits for its table lock, so without the explicit lock a read behind a truncate-and-reload sees an empty table. `LOCK TABLE` takes no snapshot. It needs table-level `SELECT` and a lockable relation, so where PostgreSQL refuses it the read runs without it in a new transaction, rather than refusing a read the role may perform.
- SQL Server uses `SNAPSHOT`.

**Rationale**: PostgreSQL's REPEATABLE READ is snapshot isolation. SQL Server's SNAPSHOT is the only level that reads one point in time without holding shared locks against writers. `SERIALIZABLE` would block writers for the length of the read. SQL Server refuses SNAPSHOT when the database option is off (error 3952); the engine classifies that as a rejection, and the README documents the option.

## R3 — No second snapshot after a crash

**Decision**: Before querying, preparation looks up the snapshot under its deterministic identity (invocation fingerprint, role, pin handle) and reuses it when present.

**Rationale**:
- The chunk store publishes atomically, so a present snapshot is complete.
- A re-read after the table changed would produce different bytes under the same identity, which the store refuses as `etl.effect-conflict`, so without the lookup a crash would fail the run.
- The reused snapshot is re-hashed and re-indexed, and that hash is what the preparation records (R4). After the commit, a damaged file is refused as `etl.snapshot-integrity-failed`.

## R4 — Integrity anchor

**Decision**: A file source's snapshot must match the digest pinned at admission. A SQL source has no such digest, so its anchor is the snapshot hash recorded with the preparation; the batch builder verifies the file against that hash.

**Rationale**: the bytes do not exist until preparation reads them. Once recorded, the preparation is immutable, so every partition verifies the same bytes. The anchor covers the bytes from the commit onwards: a snapshot published but not yet committed is trusted as it stands in the host-approved snapshot root, like any other retained chunk, and a replacement that reuses it records its hash.

## R5 — Typed conversion before retention

**Decision**: Values are converted by declared type while reading, and each row is written as one JSON object. A NULL is written as JSON null.

**Rationale**:
- **Required NULLs.** A NULL in a required column becomes a per-record error, as JSON Lines input does.
- **Unsupported types.** A value of a type the declaration cannot hold fails the whole preparation. That is a schema mismatch, not a bad record.
- **No floating point.** Floating-point columns are refused, so no value is rounded silently.

## R6 — Transient failures at preparation

**Decision**: A transient read failure returns from preparation without settling or recording. The preparation lease lapses and the run is claimed again.

**Rationale**: the runtime has no delayed preparation release. A database outage should pause the run rather than fail it, and lease expiry already retries preparation after a worker crash.

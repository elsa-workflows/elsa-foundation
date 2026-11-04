# Spec 1198 evidence: SQL snapshot reads

Source: the branch `agent/claude/2908-sql-source`, stacked on `agent/claude/2908-sql-destination` (spec 1197). Each section names the revision it was recorded at.

## Behaviour tests

| Requirement | Test |
|---|---|
| FR-001, FR-002: model, compatibility and compiler rules | `EtlSqlSourceTests`:<br>• the round trip and the `sql` kind;<br>• table and column identifiers, including distinctness ignoring case;<br>• a key of 1 to 8 distinct required columns (9 refused), and identifiers of at most 63 characters (a 63-character table and column accepted, a 64-character table and column refused);<br>• the column count against `MaxFields`;<br>• a continuous SQL source refused;<br>• null column or key entries make an unreadable artifact.<br>Existing artifact bytes are pinned by the golden hashes in `EtlV2ArtifactIdentityTests`. |
| FR-003: bindings | `A_source_binding_resolves_a_digest_free_pin_and_refuses_a_table_it_does_not_allow`. |
| FR-004, FR-005: the snapshot read (SQLite test engine) | `EtlSqlSourceTests`:<br>• rows in key order, NULLs, non-ASCII text kept as raw UTF-8;<br>• an empty table;<br>• an unsupported or floating-point column, named, and a missing column;<br>• a published snapshot reused after the table changed, with no second read;<br>• a snapshot a lapsed predecessor publishes mid-read is adopted;<br>• a change committed after the preparation does not reach the batches (`A_change_committed_after_preparation_does_not_reach_the_batches`);<br>• a SQL pin carrying a digest refused at preparation;<br>• a transient failure records nothing and carries its cause, then the next attempt prepares (SC-004 is proven at the preparer; no run-level test drives a transient read through a lease re-claim);<br>• a rejected read, a missing table, and a provider exception outside `DbException` (rejected, not retried);<br>• the snapshot size limit. |
| FR-006: processing as JSON Lines | `A_prepared_snapshot_is_processed_as_json_lines_and_a_required_null_is_a_record_error`.<br>`A_row_beyond_the_record_limit_is_a_record_error_not_a_failed_preparation`.<br>`A_snapshot_that_no_longer_matches_its_recorded_hash_is_refused`: `etl.snapshot-integrity-failed`, and `etl.binding-unavailable` for a digested pin.<br>The worker's re-check: `Reader_refuses_a_sql_source_whose_identifiers_or_key_the_compiler_would_refuse` and `Reader_refuses_a_sql_source_with_columns_equal_ignoring_case_or_a_repeated_key`. |
| The real worker and store (SQLite test engine) | `A_sql_source_reads_its_table_once_and_runs_it_across_two_partitions`: three accepted records and one rejected (the NULL name), two partitions read from one snapshot (the engine counts one read), a `Sql` pin without a digest. It compares names, not order; `A_prepared_snapshot_is_processed_as_json_lines…` checks ordinals.<br>`A_sql_source_delivers_into_a_keyed_sql_table_with_typed_columns` (a SQL source into a SQL destination, typed parameters).<br>`An_empty_sql_table_settles_as_success_with_no_records`.<br>`A_column_the_declaration_cannot_hold_fails_the_run_before_any_partition_exists`.<br>`A_sql_source_without_its_engine_package_is_refused_at_admission`. |
| US3, SC-003: real worker processes (SQLite test engine) | `A_worker_killed_after_publishing_its_snapshot_is_replaced_and_the_replacement_reuses_that_snapshot`:<br>• the worker is killed at the new `BeforePreparationCommit` gate;<br>• the run has two partitions;<br>• the table changes;<br>• the survivor's run delivers the original rows;<br>• the recorded preparation is the predecessor's snapshot, byte for byte.<br>The worker processes write through the SQLite test engine. Reuse happens before any engine call, so it does not depend on the engine. |
| SC-001, SC-002, SC-004: real engines, at the reader | Container suite `Elsa.Etl.Sql.ProviderTests` (48 tests, 27 for this spec), on PostgreSQL 16 and SQL Server 2022. These tests drive `EtlSqlSnapshotReader` directly; preparation and runs on the real engines are the Workbench journeys below:<br>• 16-, 32- and 64-bit integers on PostgreSQL (which has no 8-bit type), 8-, 32- and 64-bit on SQL Server, an exact decimal and a decimal read from an integer column;<br>• a boolean, a GUID, non-ASCII text (raw UTF-8), a timestamp, a zoned timestamp and a date (`yyyy-MM-dd`), with NULLs, in key order;<br>• an empty table, and a 200,000-row table beyond the snapshot limit, which fails with the limit;<br>• PostgreSQL: a read waiting behind a truncate-and-reload sees the reloaded rows, not an empty table, a role with column-level grants only or a materialized view is read without the lock, and a lock wait cut short by `statement_timeout` fails the read rather than reading unlocked;<br>• a floating-point column and a decimal beyond .NET range refused;<br>• a missing table rejected;<br>• an unreachable database classified as transient (no test classifies a driver timeout);<br>• a change committed while the read is paused part-way through 20,000 rows never reaches the snapshot, and the writer is not blocked;<br>• a time of day as ISO 8601 text, and on PostgreSQL an interval, a zoned time of day and infinite dates and timestamps refused (a zoned time with or without a precision), and text that is not valid UTF-8 rejected;<br>• SQL Server: extreme dates, such as a temporal table's 9999-12-31 period end, kept;<br>• SQL Server: a database without snapshot isolation rejected, and an unpaired surrogate already replaced by the driver. |
| FR-007: Studio | Vitest `connector-pipeline-editor`: the SQL source kind, binding, table, columns and key; an existing key's order kept and a renamed key column carried through in place; continuous mode dropped and not offered.<br>Vitest `runInspection`: inspection's "SQL table snapshot" label and its digest wording.<br>Playwright `etl-connector-authoring`: authoring the kind, binding, table and key and saving it, the continuous option hidden, and axe at 320 and 1024 px. |

At `63408195e` (review round 11):
- the ETL suite passed, 1235 of 1235;
- the container suite passed, 48 of 48, locally (one of the six since round 5 is spec 1197's disabled-index test);
- the architecture suite passed, 614 of 614.

At `68ba92078` (review round 7), Studio ETL Vitest passed 119 of 119 and the ETL connector Playwright spec passed 14 of 14. Later commits do not change Studio.

**SC-007 is pending.** CI runs only on pull requests into `main`, and this PR targets `agent/claude/2908-sql-destination` (#3699), so no `Build & test` or `Full tier` run exists for it yet. Once #3699 merges, the PR retargets `main` and CI runs the container suite in its full tier.

## Bite-proofs

[`bite-proofs.json`](bite-proofs.json) has 41 entries, each naming the tests that failed:
- **15 recorded at `51fa1a20b`.** The table-identifier entry was re-recorded at `2fee631a8`, so that it lists all three failing theory rows. 14 bite: the compiler's table identifier, case-insensitive column distinctness and key-required rules; snapshot reuse; transient classification; the float refusal; the size limit; the batch builder's digest-free pin check; the source binding role; the preparer's kind check; the worker's identifier re-check; the codec's digest and hash rules; SQL Server's `SNAPSHOT` level.
- **8 recorded at `f606d68cc`, for review round 1.** All bite: adoption of a concurrently published snapshot; the preparer's digest-free pin check; raw UTF-8 text; the catch-all rejection; the worker's case-insensitive column and repeated-key checks; null SQL entries; the `date` format.
- **3 recorded at `2fee631a8`, for review round 2.** All bite: the interval refusal, the time-of-day format and the SQL column limit.
- **4 for review round 3.**
  - 2 bite, recorded at `1cc03a3d4`: the 8-column key bound and the 63-character identifier bound.
  - 2 do not bite, recorded on `ce00cc26d` with the round's changes not yet committed: keeping the original failure when closing a cancelled reader throws, and the cancel itself. Closing did not throw on either driver at 200,000 rows, so both are defensive.
- **1 recorded at `9bb6fb935`, for review round 4.** It bites: the refusal of a zoned time of day.
- **3 recorded at `6b1c8b1c6`, for review round 5.** All bite: the transformer's and the output processor's SQL source branches, and the refusal of infinite dates.
- **2 recorded at `7f7b177d7`, for review round 6.** Both bite: extreme dates refused only where the engine maps infinity to them, and a zoned time of day with a precision.
- **1 recorded at `b6a886089`, for review round 9.** It bites: the worker's re-check that key columns are required.
- **1 recorded at `23f8b1855`, for review round 9.** It bites: PostgreSQL's table lock before the snapshot.
- **1 recorded at `42572d6af`, for review round 10.** It bites: the fallback to an unlocked read where PostgreSQL refuses the lock.
- **2 for review round 11.** Both bite: at `63408195e`, falling back only for a lock the engine refused (a timed-out lock does not fall back); at `63408195e`, the catch that maps a value the driver cannot materialise to `etl.sql-column-type-unsupported`.

**Did not bite, by design:**
- **PostgreSQL's isolation level.** In PostgreSQL a single `SELECT` already reads one snapshot under `READ COMMITTED`. `REPEATABLE READ READ ONLY` is kept for a future read of more than one statement.
- **The mid-read change test, as a proof of the isolation level.** It passes under `READ COMMITTED` on both engines, because the drivers may already have buffered the rows. It proves that a change committed while the read is paused never reaches the run, and that the writer is not blocked, but not the level. On SQL Server the level is bite-proven by the snapshot-disabled database test instead.
- **The `date` and time-of-day formats on PostgreSQL.** Npgsql returns `DateOnly` and `TimeOnly`, which separate branches write, so the mutated branches serve only SQL Server.

**Tested without a bite-proof, or not tested:**
- **The worker's transient warning.** It is not asserted.
- **The execution's transient return.** Without it, recording would throw, the lease would lapse, and preparation would run again, so the observable behaviour is the same.
- **The command timeout of 0.** It is not tested; no test reads for longer than a driver's default timeout. Nothing bounds a read blocked on a lock; the README states it.
- **Guards tested but not bite-proven:**
  - the compiler's column identifier format, its repeated-key check and its undeclared-key check (the key-required, case-collision, key-count and identifier-length rules are bite-proven);
  - the worker re-check's column identifier format, key count and key declared (its table identifier, case collision, repeated key and key-required checks are bite-proven);
  - the executable reader's SQL column count against `MaxFields`, and the record codec's index bound for a SQL snapshot.
- **Untested guards:**
  - the preparer's SQL shape check (`etl.pipeline-invalid` for missing columns or key, too many columns, or an empty table name);
  - the batch builder's other `SourceAnchor` conditions (kind, alias, handle and revision; only the digest condition is bite-proven);
  - `FileChunkStore.FindAsync` turning an I/O failure into `etl.output-read-failed`;
  - the reader's per-value length check against the remaining snapshot bytes.
- **The engine's session setup before the snapshot transaction.** It is not tested. Neither first-party engine's setup affects a read.
- **The mapping of an over-long line to `etl.snapshot-too-large`.** It is not tested.
- **A type whose assembly is missing** (SqlClient's spatial and hierarchy types) is mapped to `etl.sql-column-type-unsupported`. It is not tested.
- **The runtime API's `SourceInput` for a SQL source.** It is covered only by the Workbench journey's inspection check.
- **A worker claiming a run whose engine is missing.** Only admission is tested.
- **The Studio guards** (FR-007): continuous mode dropped for a `sql` source, the existing key order kept, the "SQL table snapshot" label and the digest wording. Vitest tests each of them, and Playwright the authoring ones, but no bite-proof mutates Studio code.
- **The "a SQL source cannot have HTTP shards" checks** in the preparer and the record codec, and the reader's column-count check against the result set. They are not tested.
- **Process-level kill and reuse on PostgreSQL or SQL Server.** It is not run. The worker fixture writes through the SQLite test engine.
- **A change after preparation and before the partitions run, on PostgreSQL or SQL Server.** It is proven on the SQLite test engine, at the preparer and batch builder (`A_change_committed_after_preparation_does_not_reach_the_batches`), and through worker processes for a change between publication and the preparation record (the kill test). The run reads the retained snapshot whatever the engine.
- **Two SQL source branches without a SQL-specific test:** the compiler's artifact size pre-check (`artifact.too-large` is tested through an oversized mapping only), and `etl.binding-ambiguous` for a SQL source alias that a file or HTTP approval also uses.
- **The preparer's other SQL pin conditions** (an invalid handle or an empty revision; only the digest condition is bite-proven), and its `etl.sql-unavailable` refusal when no SQL bindings are configured. They are not tested.
- **SQL Server 3961** (concurrent DDL under `SNAPSHOT`) classified as transient. It is not tested.
- **The engines' `ORDER BY` and identifier quoting in `SelectSnapshot`.** Key order is checked by the typed container test, but no bite-proof mutates it; quoting is untested beyond lowercase, unreserved names.
- **The SQL source binding alias checks** in the compiler and the worker. No test uses an invalid SQL source alias.
- **The read-only snapshot setup** (`SET TRANSACTION READ ONLY` on PostgreSQL) is not tested; no test attempts a write inside the read.
- **A transient read through a lease re-claim** is proven at the preparer only.

## Actual Workbench journey

[`sql_source_workbench_journey.py`](sql_source_workbench_journey.py) starts its own database container and its own Workbench on ephemeral ports. With `--engine postgresql`, the container is PostgreSQL 16. With `--engine sqlserver`, it is SQL Server 2022, with a database that allows snapshot isolation.

Both ran at `63408195e` on a clean tree. Their summaries are [`workbench-journey-summary.json`](workbench-journey-summary.json) (PostgreSQL) and [`workbench-journey-summary.sqlserver.json`](workbench-journey-summary.sqlserver.json) (SQL Server). Earlier runs passed the same checks: PostgreSQL at `51fa1a20b`, `2fee631a8` and `1cc03a3d4`, `9bb6fb935`, `6b1c8b1c6`, `7f7b177d7`, `21171659f` and `42572d6af`, and SQL Server at `1cc03a3d4`, `9bb6fb935`, `6b1c8b1c6`, `7f7b177d7`, `21171659f` and `42572d6af`. Spec 1199's `c70a668a4` changed only how the script generates the SQL Server container's administrator secret; the SQL Server journey passed again with it at `fe4aca2e8`.

A SQL → SQL pipeline (`people` into `people_copy`, on the same engine) runs twice from native Flowcharts. The source changes between the runs. All six checks pass on each engine:
- the catalog lists both SQL bindings, `Source` and `MainOutput`;
- the first run copies the four rows through `Done`;
- the second run reads the changed table (an update, a delete and an insert) through `Done`. The copy holds the new values, plus the deleted source row, because a SQL destination upserts and never deletes;
- inspection shows a `sql` source with no admission digest and a prepared snapshot hash;
- no accounting is left;
- inspection shows no host path or secret.

The owner's Workbench on port 5095 was running and was not touched.

## Limits

These are stated in the ETL README:
- **Finite only.**
- **No authored queries.** A run reads whole tables, with no filter pushed down.
- **Equal keys.** Rows with equal keys keep the engine's order.
- **Floating-point columns** are refused.
- **SQL Server** needs `ALLOW_SNAPSHOT_ISOLATION ON`, and its driver replaces an unpaired surrogate in `nvarchar` text with U+FFFD.
- **Outages.** A transient outage pauses preparation, with a logged warning naming the exception type, until the database returns. A misconfiguration the engine classifies as transient (for example a wrong SQL Server database name, error 4060) also pauses rather than fails.
- **Memory.** A single value is materialised by the driver before the size check.

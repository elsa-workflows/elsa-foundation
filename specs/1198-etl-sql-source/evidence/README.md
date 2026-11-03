# Spec 1198 evidence: SQL snapshot reads

Source: the branch `agent/claude/2908-sql-source`, stacked on `agent/claude/2908-sql-destination` (spec 1197). Each section names the revision it was recorded at.

## Behaviour tests

| Requirement | Test |
|---|---|
| FR-001, FR-002: model, compatibility and compiler rules | `EtlSqlSourceTests`:<br>• the round trip and the `sql` kind;<br>• existing pipelines keep their hashes;<br>• table and column identifiers, including distinctness ignoring case;<br>• a key of 1 to 8 distinct required columns;<br>• a continuous SQL source refused. |
| FR-003: bindings | `A_source_binding_resolves_a_digest_free_pin_and_refuses_a_table_it_does_not_allow`. |
| FR-004, FR-005: the snapshot read (SQLite test engine) | `EtlSqlSourceTests`:<br>• rows in key order, one JSON object per row, NULLs;<br>• an empty table;<br>• an unsupported type or a floating-point column, naming it, and a missing column;<br>• a published snapshot reused after the table changed, with no second read;<br>• a transient failure records nothing, then the next attempt prepares;<br>• a rejected read and a missing table;<br>• the snapshot size limit. |
| FR-006: processing as JSON Lines | `A_prepared_snapshot_is_processed_as_json_lines_and_a_required_null_is_a_record_error`, and `A_snapshot_that_no_longer_matches_its_recorded_hash_is_refused` (a tampered hash, and a SQL pin that carries a digest).<br>`Reader_refuses_a_sql_source_whose_identifiers_or_key_the_compiler_would_refuse` covers the worker's re-check. |
| The real worker and store | `A_sql_source_reads_its_table_once_and_runs_it_across_two_partitions`: three accepted records, one NULL name on the error route, two partitions, a `Sql` pin without a digest.<br>`A_sql_source_without_its_engine_package_is_refused_at_admission`. |
| US3, SC-003: real worker processes | `A_worker_killed_after_publishing_its_snapshot_is_replaced_and_the_replacement_reuses_that_snapshot`:<br>• the worker is killed at the new `BeforePreparationCommit` gate;<br>• the table changes;<br>• the survivor's run delivers the original rows;<br>• the recorded preparation is the predecessor's snapshot, byte for byte. |
| SC-001, SC-002: real engines | Container suite `Elsa.Etl.Sql.ProviderTests` (29 tests, 9 of them new), on PostgreSQL 16 and SQL Server 2022:<br>• every accepted type (text, decimal, boolean, smallint, GUID, timestamp) with NULLs, in key order;<br>• a floating-point column refused;<br>• a missing table rejected;<br>• a change committed while the read is paused part-way through 20,000 rows does not reach the snapshot, and the writer is not blocked;<br>• SQL Server: a database without snapshot isolation rejected. |
| FR-007: Studio | Vitest (`connector-pipeline-editor`, `runInspection`) and Playwright (`etl-connector-authoring`):<br>• the SQL source kind, binding, table, columns and key;<br>• continuous mode dropped and not offered;<br>• inspection's "SQL table snapshot" label and its digest wording;<br>• axe at 320 and 1024 px. |

At `51fa1a20b`:
- the ETL suite passed, 1217 of 1217 (plus the three executable-reader rows added later);
- the container suite passed, 29 of 29;
- Studio ETL Vitest passed, 117 of 117;
- the ETL connector Playwright spec passed, 14 of 14.

## Bite-proofs

There are 15 entries in [`bite-proofs.json`](bite-proofs.json), all recorded at `51fa1a20b`, each with the names of the tests that failed. 14 bite:
- the compiler's table, column and key rules;
- snapshot reuse;
- transient classification;
- the float refusal;
- the size limit;
- the digest-free SQL pin;
- the source binding role;
- the preparer's kind check;
- the worker's identifier re-check;
- the codec's digest and hash rules;
- SQL Server's `SNAPSHOT` level.

**Did not bite, by design:**
- **PostgreSQL's isolation level.** In PostgreSQL a single `SELECT` already reads one snapshot under `READ COMMITTED`, so the level does not change the result. `REPEATABLE READ READ ONLY` is kept for a future read of more than one statement.
- **SQL Server's level and the mid-read change test.** On SQL Server, only the snapshot-disabled database test bites the level. The mid-read change test also passes under `READ COMMITTED`. It proves that a change during or after the read never reaches the run and that writers are not blocked, not the isolation level itself.

**Tested without a bite-proof, or not tested:**
- **The execution's transient return.** If preparation returned no result and nothing guarded it, recording would throw. The lease would lapse and preparation would run again, so the observable behaviour is the same.
- **Transient read failures on the real engines.** Their classification reuses spec 1197's, which is proven on PostgreSQL only.
- **A worker claiming a run whose engine is missing.** Only admission is tested.

## Actual Workbench journey

[`sql_source_workbench_journey.py`](sql_source_workbench_journey.py) starts its own PostgreSQL 16 container and its own Workbench on ephemeral ports. The summary is in [`workbench-journey-summary.json`](workbench-journey-summary.json). It was built from `51fa1a20b`; the tree held only this evidence, uncommitted.

A SQL → SQL pipeline (`people` into `people_copy`) runs twice from native Flowcharts. The source changes between the runs. All six checks pass:
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
- **SQL Server** needs `ALLOW_SNAPSHOT_ISOLATION ON`.
- **Outages.** A transient outage pauses preparation until the database returns.

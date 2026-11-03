# Spec 1197 evidence: transactional keyed SQL delivery

Source: the branch `agent/claude/2908-sql-destination`. Each section names the revision it was recorded at.

## Behaviour tests

| Requirement | Test |
|---|---|
| FR-001, FR-002: model, compatibility and compiler rules | `EtlSqlDestinationTests`:<br>• the round trip;<br>• connector kinds appended (existing artifact bytes are held by the existing `EtlV2ArtifactIdentityTests` golden hashes);<br>• table and column identifiers, including distinctness ignoring case;<br>• a key of 1 to 8 distinct fields from required source fields;<br>• main route only. |
| FR-003: bindings | `A_binding_pins_a_secret_free_handle_and_refuses_a_table_it_does_not_allow`:<br>• the handle ignores the password but tracks the target;<br>• exact resolution;<br>• `etl.sql-table-not-allowed`;<br>• `etl.binding-revision-unavailable`;<br>• `etl.sql-unavailable`. |
| FR-005, FR-006: the writer protocol (SQLite test engine) | `EtlSqlRouteWriterTests`:<br>• upsert plus a ledger row;<br>• redelivery writes nothing;<br>• update and insert, and a repeated key keeps the later record;<br>• a missing unique key fails closed;<br>• hash conflict;<br>• rollback across commands;<br>• a transient failure yields, then applies;<br>• a missing ledger, and a ledger created between attempts. (Only the container suite drives a concurrent creation.) |
| FR-004–FR-008: the real worker and store | `A_csv_pipeline_upserts_its_accepted_records_into_a_keyed_table_with_one_ledger_row_per_batch` covers two runs, the table and the ledger per run.<br>`A_sql_destination_without_its_engine_package_is_refused_at_admission` covers the missing engine. |
| US2, SC-002: real worker processes | `A_worker_killed_after_its_sql_commit_and_before_the_record_is_replayed_without_writing_twice`:<br>• the killed worker's commit leaves one ledger row;<br>• the survivor replays it;<br>• two ledger rows and two table rows result. |
| SC-001, SC-003: real engines | Container suite `Elsa.Etl.Sql.ProviderTests`, 21 tests, on PostgreSQL 16 and SQL Server 2022:<br>• upsert, update, insert and a repeated key;<br>• replay and conflict;<br>• a missing unique key, including on SQL Server, where `MERGE` would otherwise succeed;<br>• rollback across commands;<br>• concurrent first deliveries;<br>• a missing ledger, and a missing table or a view (a rejection, not a missing key);<br>• PostgreSQL: a least-privilege role with a pre-created ledger, and a key check that follows `search_path` and counts only key columns, while refusing a deferrable constraint (review round 1);<br>• SQL Server: a disabled unique index is not a key;<br>• error classification on PostgreSQL, and SQL Server pool exhaustion. |
| FR-009: Studio | Vitest (`connector-pipeline-editor`) and Playwright (`etl-connector-authoring`): a SQL table for accepted records only, binding, table and key; axe at 320 and 1024 px. |

## Bite-proofs

There are 24, in [`bite-proofs.json`](bite-proofs.json), each with the names of the tests that failed:
- 14 were recorded at `95d02a433`.
- 1 was recorded at `a8f339cd2`.
- 5 were recorded at `221f137cf`, for the review round 1 fixes: ledger DDL only when missing, key columns only, deferrable refused, an exact allow-list, and SQL Server pool exhaustion.
- 2 were recorded at `727316b97`, for the review round 2 fix: each engine reports a missing table as -1, which the writer maps to a rejection rather than a missing key.
- 1 was recorded at `ada38b3f1`, for the review round 5 fix: PostgreSQL treats only ordinary and partitioned tables as targets.
- 1 was recorded at `454f1e7bb`, for a CodeRabbit finding: SQL Server's key check ignores a disabled unique index.

The first run's PostgreSQL deadlock mutation did not bite. Npgsql's `IsTransient` already classifies deadlocks, so the explicit SQLSTATE list was redundant. It was removed, and the remaining guard was re-proven.

**Tested without a bite-proof, or not tested:**
- **Transient failures on SQL Server.** The classification by error number is not tested; only pool exhaustion is.
- **No bite-proof:**
  - the table identifier rule, and the key count and distinctness rules;
  - later-wins ordering (US1 scenario 3), and rollback (US2 scenario 3), both of which are tested;
  - the mappings to `sql-ledger-missing` and `sql-write-rejected`;
  - the inspector, API and codec `Sql` sites.
- **A worker claiming a run whose engine is missing** (FR-008). Only admission is tested.
- **`XACT_ABORT`.** The SQL Server rollback test passes either way, because the writer rolls back explicitly.
- **The 2-second pause** before a transient retry.
- **The executable reader's refusal** of a `sql` error route. The compiler refuses it first.
- **Inspection's "SQL table" label.**

## Actual Workbench journey

[`sql_workbench_journey.py`](sql_workbench_journey.py) starts its own PostgreSQL 16 container and its own Workbench on ephemeral ports. Its summary is in [`workbench-journey-summary.json`](workbench-journey-summary.json). It was built from `a8f339cd2` plus the Workbench's engine references, which `6b4a88f51` then committed unchanged, so the summary records the tree as dirty.

Two JSON Lines → SQL pipelines run from native Flowcharts, and all seven checks pass:
- the catalog lists the SQL binding;
- the first run inserts four rows through `Done`;
- the second run updates two rows and inserts one;
- each batch gets one ledger row (4, then 3);
- inspection shows the `sql` main route;
- no accounting is left;
- inspection shows no host path or secret.

The owner's Workbench on port 5095 was not touched.

## Limits

These are stated in the ETL README:
- **Ordering:** a key shared across concurrent partitions is won by whichever partition writes last.
- **Ledger retention:** operators must not delete a running run's ledger rows.
- **Transient failures:** they pause a run indefinitely.
- **Main route only.**
- **Quoting:** identifiers are quoted exactly as authored, so they are case-sensitive on PostgreSQL. The allow-list compares exactly too.

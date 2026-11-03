# Spec 1199 evidence: SQL watermark reads

Source: the branch `agent/claude/2908-sql-watermark`, stacked on `agent/claude/2908-sql-source` (spec 1198).

## Behaviour tests

| Requirement | Test |
|---|---|
| FR-001: model and compatibility | `A_watermark_must_be_a_declared_required_int64_column_and_is_omitted_when_absent` (an artifact without a watermark keeps its bytes) and `A_prepared_run_without_a_watermark_keeps_its_persisted_bytes`. |
| FR-001: the worker's re-check | `Reader_refuses_a_sql_source_with_columns_equal_ignoring_case_or_a_repeated_key` (a watermark on a text column). |
| FR-003, FR-005: the filtered read and the high watermark | `A_watermark_reads_only_rows_above_the_floor_and_records_the_snapshot_high_watermark` (SQLite: the filter and the high watermark). The container test `A_watermark_floor_reads_only_rows_above_it_in_key_order` proves the filter on PostgreSQL 16 and SQL Server 2022; the high watermark is read back from the retained snapshot, whatever the engine. |
| FR-002, FR-004, US1, US2: checkpoints through the worker and store | `A_watermarked_sql_source_loads_only_new_and_changed_rows_across_runs` (3, then 2, then 0 records; the checkpoint 3, 5, 5).<br>`A_watermarked_run_with_a_rejected_delivery_leaves_the_checkpoint_so_the_next_run_reads_the_rows_again` (a rejected delivery leaves no checkpoint; the next run reads all three rows).<br>`A_cancelled_watermarked_run_leaves_the_checkpoint_so_the_next_run_reads_the_rows_again` (a run cancelled after preparation, through worker processes, leaves no checkpoint; the next run reads all three rows).<br>`A_watermarked_run_that_completes_with_rejected_rows_still_advances_the_checkpoint` (US1 scenario 4).<br>The incremental journey also shows that a checkpoint keyed by another source handle is not found. |
| US2 scenario 2, SC-003: real worker processes | `A_watermarked_run_whose_worker_was_killed_after_publishing_records_the_snapshot_high_watermark`: a row with a higher watermark arrives after the snapshot, and the checkpoint is the snapshot's high watermark, 3. |
| §2.23.5: a corrupt retained snapshot | `A_snapshot_that_no_longer_parses_reports_an_integrity_failure_when_its_high_watermark_is_read` (`InvalidDataException`, which the preparer reports as `etl.snapshot-integrity-failed`). |
| SC-006: the migration | `Connector_run_completion_migration_keeps_prior_finite_rows_and_leaves_their_request_null` seeds a runtime database at `20261021170000` and migrates it to the latest migration, which includes `20261103120000_ConnectorSourceWatermarks`, then checks for pending model changes. The existing migration tests also apply every migration to a new database. |
| FR-006: Studio | Vitest `connector-pipeline-editor` (the selector offers required `int64` columns, follows a rename and drops a column that no longer qualifies) and Playwright `etl-connector-authoring` (the watermark is saved). |

At `fe4aca2e8` (review round 2):
- the ETL suite passed, 1244 of 1244;
- the container suite passed, 50 of 50;
- the architecture suite passed, 614 of 614;
- Studio ETL Vitest passed, 120 of 120.

The Playwright spec passed 14 of 14 at `6f57b43f1`. Since then, Studio changed only the watermark help text, which the spec does not assert.

## Bite-proofs

[`bite-proofs.json`](bite-proofs.json) has 10 entries, and all bite. Three, recorded at `fe4aca2e8`, cover the `WHERE` clause on PostgreSQL and on SQL Server (each made inclusive, which the container test catches) and the integrity mapping of a corrupt snapshot. One, recorded at `6f57b43f1`, covers the source handle in the checkpoint key. The other 6, recorded at `bc5fdb4da`, are:
- the compiler's and the worker's watermark rules;
- the reader's floor;
- the recorded high watermark;
- the checkpoint raise;
- the execution reading the checkpoint.

**Not bite-proven or not tested:**
- **A faulted run leaving the checkpoint unchanged.** A cancelled run and a rejected delivery are tested. A fault settles through the same path, which never raises the checkpoint; only the completion path does, but no test faults a watermarked run.
- **The checkpoint never moving backwards** for overlapping runs. No test completes a run with a lower high watermark after one with a higher one.
- **A zero-record run leaving the checkpoint unchanged.** The third run of the incremental journey shows it, but no bite-proof mutates the zero-record path.
- **A store without checkpoints** (the interface default) reading every row. It is not tested.
- **Guards tested but not bite-proven:**
  - the first-checkpoint insert;
  - the `PreparedJson is null` return in the raise;
  - the high-watermark maximum in `ReadHighWatermarkAsync`;
  - the artifact's null-omission of `watermark`, and the prepared run's null-omission of `sourceWatermark`;
  - the Studio watermark filtering and rename handling.
- **Untested:** the compiler's text budget for the watermark.

## Actual Workbench journeys

[`sql_watermark_workbench_journey.py`](sql_watermark_workbench_journey.py) starts its own database container and its own Workbench on ephemeral ports, with `--engine postgresql` or `--engine sqlserver`. One definition with a `version` watermark runs twice from native Flowcharts:
- the first run copies all four rows;
- one row is then updated and one inserted, both with higher versions;
- the second run reads only those two, with source record count 2.

All six checks pass on both engines. The summaries are [`workbench-journey-summary.json`](workbench-journey-summary.json) and [`workbench-journey-summary.sqlserver.json`](workbench-journey-summary.sqlserver.json). They were recorded at `fe4aca2e8` on a clean tree, with the script as committed. Earlier runs passed the same checks at `cf47717ff` and `6f57b43f1`, before `c70a668a4` changed how the script generates the SQL Server container's administrator secret.

## Limits

These are stated in the ETL README:
- **Finite only.**
- **Watermark type.** The watermark must be an `Int64` column whose values become visible in increasing order. A sequence or identity under concurrent writers can skip rows.
- **Deletes** are not propagated.
- **Overlapping runs** can both read the same rows.

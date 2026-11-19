# Tasks: SQL watermark reads

**Input**: [spec.md](spec.md), [plan.md](plan.md)

- [x] T001 `EtlSqlSource.Watermark` and `EtlConnectorPreparedRun.SourceWatermark`, omitted from JSON when null. Tests: existing artifacts and prepared runs keep their bytes.
- [x] T002 Compiler and worker watermark rules. Tests.
- [x] T003 `IEtlSqlEngine.SelectSnapshot` with a watermark filter; the reader binds the floor; the preparer records the snapshot's high watermark, read back from the published snapshot. Tests on SQLite and both real engines.
- [x] T004 The checkpoint table, its migration and model snapshot; `FindConnectorSourceWatermarkAsync`; the raise in the transaction that completes a run. Execution reads the checkpoint before preparation.
- [x] T005 Journeys: an incremental load across three runs, a run settled by a delivery failure leaves the checkpoint, and a worker killed after publishing its snapshot.
- [x] T006 Studio watermark selector, Vitest and Playwright.
- [x] T007 README and engine catalog.
- [x] T008 Bite-proofs, Workbench journeys on PostgreSQL and SQL Server, evidence README, the whole ETL, architecture and container suites.

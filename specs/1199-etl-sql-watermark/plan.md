# Implementation Plan: SQL watermark reads

**Branch**: `agent/claude/2908-sql-watermark` | **Spec**: [spec.md](spec.md)

## Summary

The spec 1198 snapshot read gains a watermark filter. A checkpoint table keyed by scope, definition, source alias, pinned source handle, table and watermark column holds the high watermark of the last completed run. Execution reads the checkpoint before preparation, the reader filters by it and reports the snapshot's highest watermark, the prepared run records it, and the store raises the checkpoint, in the same transaction, when the run completes every record: it settles as succeeded, or as business error with `etl.rows-rejected`.

## Constitution check

- **Compatibility.** `EtlSqlSource.Watermark` and `EtlConnectorPreparedRun.SourceWatermark` are omitted from JSON when null, so existing artifacts and prepared runs keep their bytes. The checkpoint table is additive, added by a SQLite migration.
- **Public API** (§4.2). `IEtlConnectorRuntimeStore.FindConnectorSourceWatermarkAsync` has a default implementation, so other stores keep compiling. `IEtlSqlEngine.SelectSnapshot` gains a parameter; the interface is unshipped.
- **Wrapping** (§2.23.5). The reader's exception mapping covers the filtered read. Reading the high watermark back from the retained snapshot maps a line that no longer parses to `InvalidDataException`, which the preparer reports as `etl.snapshot-integrity-failed`.
- **Tests.** Compiler and worker rules, the reader's filter and high watermark on SQLite and both real engines, the store's checkpoint transitions, journeys through the worker including a process kill, and the Workbench journey on both engines.

## Changes by project

- **Core:** `EtlSqlSource.Watermark`; `EtlConnectorPreparedRun.SourceWatermark`; `IEtlConnectorRuntimeStore.FindConnectorSourceWatermarkAsync`; `IEtlSqlEngine.SelectSnapshot(..., watermark)`; PublicAPI.
- **Design:** watermark validation.
- **Runtime:** the reader's filter and high watermark; the preparer's floor parameter; execution reads the checkpoint; the executable reader's re-check.
- **Persistence:** `EtlConnectorSourceWatermarkRecord`, its context mapping, migration and snapshot; the store's read and its advance in `CompleteConnectorRunIfReadyAsync`.
- **Engines:** the `WHERE` clause.
- **Studio:** the watermark column selector.
- **Docs:** ETL README, Core `EXTENSION_POINTS.md`, engine READMEs.

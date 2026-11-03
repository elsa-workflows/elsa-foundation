# Implementation Plan: SQL snapshot reads

**Branch**: `agent/claude/2908-sql-source` | **Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md)

## Summary

This slice adds a finite `sql` source:
- **Read.** Preparation reads the table once under the engine's snapshot isolation.
- **Retain.** The rows are written as a JSON Lines input snapshot.
- **Run.** From there the run is a JSON Lines run. Its integrity is anchored on the recorded snapshot hash.
- **Engines.** The engine contract gains the snapshot read. The PostgreSQL and SQL Server engines implement it.

## Technical context

- .NET 10 and ADO.NET, with Npgsql and Microsoft.Data.SqlClient in the engine packages only, as in spec 1197.
- Unit tests read through the SQLite test engine. The container suite proves the read and its isolation on PostgreSQL 16 and SQL Server 2022.

## Constitution check

- **Packages and dependencies.** No new package or reference. The contract stays driver-free.
- **Wrapping** (framework §2.23.5). The snapshot reader wraps driver exceptions, classified by the engine, as a preparation failure or a transient exception with the original as the cause; the preparer maps those to its result.
- **Naming** (§E6). `EtlSqlSource`, `EtlSqlSnapshotReader` and `EtlSqlTransientException` stay within four components (R1). `EtlSqlSource` is an explicit R4 exception: it is a declaration record, not a "returns on request" extension point, and it is named for consistency with the existing `EtlSource` family (`EtlCsvSource`, `EtlHttpJsonSource`, `EtlJsonlSource`); the artifact format fixes only the `kind` discriminators.
- **Public API** (§4.2). `IEtlSqlEngine` gains six required members (`SnapshotIsolation`, `SnapshotSetup`, `SnapshotLock`, `IsLockRefusal`, `SelectSnapshot`, `ExtremeDatesAreInfinite`); it is unshipped (`PublicAPI.Unshipped.txt`), so this is not a breaking change.
- **Compatibility.**
  - Existing artifacts and identities are byte-identical.
  - No migration: the prepared file input and file positions are reused.
  - The source kind is the existing `EtlConnectorKind.Sql`.
- **Extension points.** The Core `EXTENSION_POINTS.md` entry for `IEtlSqlEngine` gains the snapshot read.
- **Tests.**
  - Behaviour tests cover the compiler, the preparer, the batch builder, the journeys and the engines.
  - The container suite is the existing `etl-sql`.

## Changes by project

- **`Elsa.Etl.Core`**
  - `EtlSqlSource` (JSON kind `sql`).
  - `IEtlSqlEngine`: `SnapshotIsolation`, `SnapshotSetup`, `SnapshotLock`, `IsLockRefusal`, `SelectSnapshot` and `ExtremeDatesAreInfinite`.
  - PublicAPI.
- **`Elsa.Etl.Design`**
  - `ValidateSqlSource`, which reuses `IsSqlIdentifier`.
  - Source field counting.
- **`Elsa.Etl.Runtime`**
  - `Preparation/EtlSqlSnapshotReader.cs`: the read, its conversion and the JSON writing.
  - `EtlConnectorPreparer`:
    - a `sql` branch that looks up an existing snapshot;
    - the transient result, which carries neither a preparation nor a diagnostic.
  - `EtlConnectorExecution`: returns on a transient result.
  - `HostSqlBindings`: role `Source`.
  - `HostConnectorBindings`: the `sql` source.
  - `EtlConnectorInputBatchBuilder`, `EtlRowTransformer`, `EtlConnectorOutputProcessor`, `EtlConnectorExecutableReader`: a `sql` source as JSON Lines fields.
  - `EtlSqlSnapshotReader.AsJsonl` and `ProcessingView`, the JSON Lines view of a SQL source.
- **`Elsa.Etl.Persistence.EntityFrameworkCore`**: `EtlConnectorRecordCodec` binding and preparation validation for a `sql` source.
- **`Elsa.Etl.Runtime.Api`**: the source input view.
- **Engines.**
  - PostgreSQL: REPEATABLE READ with `SET TRANSACTION READ ONLY` and an `ACCESS SHARE` table lock before the snapshot.
  - SQL Server: SNAPSHOT, with error 3952 classified as rejected.
- **Studio**
  - the source kind;
  - the editor;
  - inspection;
  - Vitest and Playwright.
- **Tests**
  - unit tests: compiler, preparer, recovery and transient;
  - a journey with a real-process kill at a new gate after snapshot publication;
  - container suite additions.
- **Docs**: the READMEs and `EXTENSION_POINTS.md`.

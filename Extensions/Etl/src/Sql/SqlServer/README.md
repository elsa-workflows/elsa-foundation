# Elsa.Etl.Sql.SqlServer

The SQL Server engine for keyed, transactional ETL SQL delivery ([spec 1197](../../../../../specs/1197-etl-sql-destination/spec.md)) and SQL snapshot reads ([spec 1198](../../../../../specs/1198-etl-sql-source/spec.md)). The feature registers one `IEtlSqlEngine` over Microsoft.Data.SqlClient; the ETL runtime owns the delivery protocol (ledger row, unique-key check, ordered single-row upserts in one transaction) and resolves this engine for SQL bindings whose `Provider` is `sqlserver`.

- **Feature:** `EtlSqlServer`, which depends on `EtlRuntime`. It registers no event handlers, contributes no fan-in events, and registers no tasks.
- **Dialect:** `MERGE … WITH (HOLDLOCK)` under `SET XACT_ABORT ON`, bracketed identifiers, a `sys.indexes` unique-key check.
- **Watermark reads** ([spec 1199](../../../../../specs/1199-etl-sql-watermark/spec.md)): `SelectSnapshot` adds `WHERE ([{watermark}] > @watermark OR [{watermark}] IS NULL)` when a checkpoint exists, so a row with a NULL watermark is still read and rejected as a record error.
- **Snapshot reads:** one `SNAPSHOT` transaction, which reads one point in time without blocking writers. Enable it with `ALTER DATABASE … SET ALLOW_SNAPSHOT_ISOLATION ON`; without it SQL Server raises error 3952 and the run fails with `etl.sql-read-rejected`.
- **Transient errors:** deadlocks, lock and command timeouts, concurrent DDL under `SNAPSHOT` (3961), and connection and availability errors (a fixed error-number list). Everything else that is not a duplicate, an existing object, or a missing object or constraint is a rejection.

# Elsa.Etl.Sql.SqlServer

The SQL Server engine for keyed, transactional ETL SQL delivery ([spec 1197](../../../../../specs/1197-etl-sql-destination/spec.md)). The feature registers one `IEtlSqlEngine` over Microsoft.Data.SqlClient; the ETL runtime owns the delivery protocol (ledger row, unique-key check, ordered single-row upserts in one transaction) and resolves this engine for SQL bindings whose `Provider` is `sqlserver`.

- **Feature:** `EtlSqlServer`, which depends on `EtlRuntime`. It registers no event handlers, contributes no fan-in events, and registers no tasks.
- **Dialect:** `MERGE … WITH (HOLDLOCK)` under `SET XACT_ABORT ON`, bracketed identifiers, a `sys.indexes` unique-key check.
- **Transient errors:** deadlocks, lock and command timeouts, and connection and availability errors (a fixed error-number list). Everything else that is not a duplicate, an existing object, or a missing object or constraint is a rejection.

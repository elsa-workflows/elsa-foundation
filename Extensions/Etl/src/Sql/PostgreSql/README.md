# Elsa.Etl.Sql.PostgreSql

The PostgreSQL engine for keyed, transactional ETL SQL delivery ([spec 1197](../../../../../specs/1197-etl-sql-destination/spec.md)) and SQL snapshot reads ([spec 1198](../../../../../specs/1198-etl-sql-source/spec.md)). The feature registers one `IEtlSqlEngine` over Npgsql; the ETL runtime owns the delivery protocol (ledger row, unique-key check, ordered single-row upserts in one transaction) and resolves this engine for SQL bindings whose `Provider` is `postgresql`.

- **Feature:** `EtlSqlPostgreSql`, which depends on `EtlRuntime`. It registers no event handlers, contributes no fan-in events, and registers no tasks.
- **Dialect:** `INSERT … ON CONFLICT (key) DO UPDATE`, quoted identifiers (case-sensitive), a `pg_index` unique-key check.
- **Snapshot reads:** one `REPEATABLE READ` transaction made `READ ONLY`, which is snapshot isolation in PostgreSQL.
- **Transient errors:** serialization failures, deadlocks, shutdowns, too many connections, connection exceptions (`08xxx`), transient Npgsql errors and timeouts. Everything else that is not a duplicate, an existing object, or a missing object or constraint is a rejection.

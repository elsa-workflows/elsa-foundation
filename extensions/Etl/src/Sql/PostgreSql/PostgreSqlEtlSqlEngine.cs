using System.Data;
using System.Data.Common;
using System.Globalization;
using Elsa.Etl.Core.Runtime;
using Npgsql;

namespace Elsa.Etl.Sql.PostgreSql;

/// <summary>The PostgreSQL engine for keyed ETL SQL delivery and snapshot reads (specs 1197 and 1198), over Npgsql.</summary>
/// <remarks>Identifiers are quoted exactly as authored, so they are case-sensitive here.</remarks>
internal sealed class PostgreSqlEtlSqlEngine : IEtlSqlEngine
{
    public string Provider => "postgresql";

    // Npgsql allows 65,535 parameters per command; stay well below it.
    public int MaxParameters => 30_000;

    public string? SessionSetup => null;

    // REPEATABLE READ is snapshot isolation in PostgreSQL: every statement of the transaction sees one snapshot.
    public IsolationLevel SnapshotIsolation => IsolationLevel.RepeatableRead;

    public string? SnapshotSetup => "SET TRANSACTION READ ONLY";

    // TRUNCATE and table-rewriting ALTER TABLE are not MVCC-safe: a snapshot taken before them sees the rewritten table as
    // empty. LOCK TABLE takes no snapshot, so waiting for it here makes the SELECT's snapshot follow any such rewrite.
    public string? SnapshotLock(string table) => $"LOCK TABLE {Quote(table)} IN ACCESS SHARE MODE";

    // LOCK needs table-level SELECT (42501) and a lockable relation (42809); a materialized view or foreign table is not one.
    public bool IsLockRefusal(Exception exception) => exception is PostgresException
    {
        SqlState: PostgresErrorCodes.InsufficientPrivilege or PostgresErrorCodes.WrongObjectType
    };

    // Npgsql maps infinity and -infinity to the extreme values, so a finite 9999-12-31 cannot be told apart from them.
    public bool ExtremeDatesAreInfinite => true;

    public string SelectSnapshot(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, string? watermarkColumn) =>
        $"SELECT {string.Join(", ", columns.Select(Quote))} FROM {Quote(table)} " +
        (watermarkColumn is null ? "" : $"WHERE ({Quote(watermarkColumn)} > @watermark OR {Quote(watermarkColumn)} IS NULL) ") +
        $"ORDER BY {string.Join(", ", keyColumns.Select(Quote))}";

    public DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);

    public EtlSqlTarget DescribeTarget(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return new(builder.Host ?? string.Empty, builder.Port.ToString(CultureInfo.InvariantCulture), builder.Database ?? string.Empty, builder.Username);
    }

    // PostgreSQL checks CREATE permission before IF NOT EXISTS, so an existing ledger is detected first: a role that may
    // only write a pre-created ledger never runs the DDL.
    public string CreateLedger(string ledgerTable) =>
        $"DO $ledger$ BEGIN IF to_regclass('{Quote(ledgerTable)}') IS NULL THEN CREATE TABLE IF NOT EXISTS {Quote(ledgerTable)} " +
        "(effect_id text PRIMARY KEY, run_id text NOT NULL, batch_id text NOT NULL, content_hash text NOT NULL, applied_at bigint NOT NULL); " +
        "END IF; END $ledger$";

    public string InsertLedger(string ledgerTable) =>
        $"INSERT INTO {Quote(ledgerTable)} (effect_id, run_id, batch_id, content_hash, applied_at) VALUES (@effect, @run, @batch, @hash, @applied)";

    public string ReadLedgerHash(string ledgerTable) => $"SELECT content_hash FROM {Quote(ledgerTable)} WHERE effect_id = @effect";

    // The table resolves through search_path exactly as the statements do; only key columns count (not INCLUDE columns),
    // and a deferrable constraint is excluded because ON CONFLICT cannot use it.
    public string CountUniqueKeys(string table, IReadOnlyList<string> keyColumns)
    {
        var keys = string.Join(", ", keyColumns.Order(StringComparer.Ordinal).Select(column => $"'{column}'"));
        // Only an ordinary or partitioned table is a target: a missing table, a view or a materialized view counts -1.
        return $"SELECT CASE WHEN (SELECT c.relkind FROM pg_class c WHERE c.oid = to_regclass('{Quote(table)}')) IS DISTINCT FROM 'r' " +
               $"AND (SELECT c.relkind FROM pg_class c WHERE c.oid = to_regclass('{Quote(table)}')) IS DISTINCT FROM 'p' THEN -1 ELSE (" +
               $"SELECT COUNT(*) FROM pg_index i WHERE i.indrelid = to_regclass('{Quote(table)}') AND i.indisunique AND i.indpred IS NULL " +
               $"AND i.indnkeyatts = {keyColumns.Count} AND (SELECT array_agg(a.attname::text ORDER BY a.attname::text) FROM pg_attribute a " +
               "WHERE a.attrelid = i.indrelid AND a.attnum = ANY((i.indkey::int2[])[0:i.indnkeyatts - 1])) = " +
               $"ARRAY[{keys}]::text[] AND NOT EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conindid = i.indexrelid AND k.condeferrable)) END";
    }

    public string Upsert(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, int row)
    {
        var updates = columns.Where(column => !keyColumns.Contains(column))
            .Select(column => $"{Quote(column)} = EXCLUDED.{Quote(column)}").ToArray();
        return $"INSERT INTO {Quote(table)} ({string.Join(", ", columns.Select(Quote))}) " +
               $"VALUES ({string.Join(", ", columns.Select(column => Parameter(column, row)))}) " +
               $"ON CONFLICT ({string.Join(", ", keyColumns.Select(Quote))}) " +
               (updates.Length == 0 ? "DO NOTHING" : $"DO UPDATE SET {string.Join(", ", updates)}");
    }

    public string Parameter(string column, int row) => $"@{column}_{row}";

    public EtlSqlErrorKind Classify(Exception exception) => exception switch
    {
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => EtlSqlErrorKind.DuplicateKey,
        PostgresException { SqlState: PostgresErrorCodes.DuplicateTable or PostgresErrorCodes.DuplicateObject } => EtlSqlErrorKind.AlreadyExists,
        PostgresException { SqlState: PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn } => EtlSqlErrorKind.MissingObject,
        PostgresException { SqlState: PostgresErrorCodes.InvalidColumnReference } => EtlSqlErrorKind.MissingConstraint,
        // Npgsql marks serialization failures, deadlocks, shutdowns, resource limits and connection errors transient.
        NpgsqlException { IsTransient: true } => EtlSqlErrorKind.Transient,
        TimeoutException => EtlSqlErrorKind.Transient,
        _ => EtlSqlErrorKind.Rejected
    };

    private static string Quote(string identifier) => string.Join('.', identifier.Split('.').Select(part => $"\"{part}\""));

}

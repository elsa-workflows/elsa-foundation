using System.Data.Common;
using System.Globalization;
using Elsa.Etl.Core.Runtime;
using Npgsql;

namespace Elsa.Etl.Sql.PostgreSql;

/// <summary>The PostgreSQL engine for keyed ETL SQL delivery (spec 1197), over Npgsql.</summary>
/// <remarks>Identifiers are quoted exactly as authored, so they are case-sensitive here.</remarks>
internal sealed class PostgreSqlEtlSqlEngine : IEtlSqlEngine
{
    public string Provider => "postgresql";

    // Npgsql allows 65,535 parameters per command; stay well below it.
    public int MaxParameters => 30_000;

    public string? SessionSetup => null;

    public DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);

    public EtlSqlTarget DescribeTarget(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return new(builder.Host ?? string.Empty, builder.Port.ToString(CultureInfo.InvariantCulture), builder.Database ?? string.Empty, builder.Username);
    }

    public string CreateLedger(string ledgerTable) =>
        $"CREATE TABLE IF NOT EXISTS {Quote(ledgerTable)} (effect_id text PRIMARY KEY, run_id text NOT NULL, " +
        "batch_id text NOT NULL, content_hash text NOT NULL, applied_at bigint NOT NULL)";

    public string InsertLedger(string ledgerTable) =>
        $"INSERT INTO {Quote(ledgerTable)} (effect_id, run_id, batch_id, content_hash, applied_at) VALUES (@effect, @run, @batch, @hash, @applied)";

    public string ReadLedgerHash(string ledgerTable) => $"SELECT content_hash FROM {Quote(ledgerTable)} WHERE effect_id = @effect";

    public string CountUniqueKeys(string table, IReadOnlyList<string> keyColumns)
    {
        var (schema, name) = Split(table);
        var keys = string.Join(", ", keyColumns.Order(StringComparer.Ordinal).Select(column => $"'{column}'"));
        return "SELECT COUNT(*) FROM pg_index i JOIN pg_class c ON c.oid = i.indrelid JOIN pg_namespace n ON n.oid = c.relnamespace " +
               $"WHERE i.indisunique AND i.indpred IS NULL AND c.relname = '{name}' AND n.nspname = {(schema is null ? "current_schema()" : $"'{schema}'")} " +
               $"AND i.indnkeyatts = {keyColumns.Count} AND (SELECT array_agg(a.attname::text ORDER BY a.attname::text) FROM pg_attribute a " +
               $"WHERE a.attrelid = c.oid AND a.attnum = ANY(i.indkey)) = ARRAY[{keys}]::text[]";
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
        PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or
            PostgresErrorCodes.AdminShutdown or PostgresErrorCodes.CrashShutdown or PostgresErrorCodes.CannotConnectNow or
            PostgresErrorCodes.TooManyConnections } => EtlSqlErrorKind.Transient,
        PostgresException postgres when postgres.SqlState.StartsWith("08", StringComparison.Ordinal) => EtlSqlErrorKind.Transient,
        NpgsqlException { IsTransient: true } => EtlSqlErrorKind.Transient,
        TimeoutException => EtlSqlErrorKind.Transient,
        _ => EtlSqlErrorKind.Rejected
    };

    private static string Quote(string identifier) => string.Join('.', identifier.Split('.').Select(part => $"\"{part}\""));

    private static (string? Schema, string Name) Split(string table) =>
        table.IndexOf('.') is var dot and >= 0 ? (table[..dot], table[(dot + 1)..]) : (null, table);
}

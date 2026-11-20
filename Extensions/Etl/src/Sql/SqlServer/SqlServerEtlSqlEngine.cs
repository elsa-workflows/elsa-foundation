using System.Data;
using System.Data.Common;
using Elsa.Etl.Core.Runtime;
using Microsoft.Data.SqlClient;

namespace Elsa.Etl.Sql.SqlServer;

/// <summary>The SQL Server engine for keyed ETL SQL delivery and snapshot reads (specs 1197 and 1198), over Microsoft.Data.SqlClient.</summary>
/// <remarks>
/// Every connection runs under <c>SET XACT_ABORT ON</c>, so a failing statement aborts the whole transaction. The upsert is
/// <c>MERGE … WITH (HOLDLOCK)</c>, which needs a unique index on the key to stay single-row; the writer checks for one.
/// </remarks>
internal sealed class SqlServerEtlSqlEngine : IEtlSqlEngine
{
    // Errors a retry can outlive: deadlock, timeouts, connection establishment and transport, and availability failures.
    private static readonly HashSet<int> TransientNumbers =
        [-2, 0, 2, 20, 40, 53, 64, 121, 233, 1205, 1222, 3961, 4060, 4221, 10053, 10054, 10060, 10061, 10928, 10929, 40143, 40197, 40501,
         40613, 49918, 49919, 49920];

    public string Provider => "sqlserver";

    // SQL Server allows 2,100 parameters per request.
    public int MaxParameters => 2_000;

    public string? SessionSetup => "SET XACT_ABORT ON";

    // SNAPSHOT reads one point in time without blocking writers. It needs ALLOW_SNAPSHOT_ISOLATION ON; without it SQL
    // Server raises 3952, which is a rejection.
    public IsolationLevel SnapshotIsolation => IsolationLevel.Snapshot;

    public string? SnapshotSetup => null;

    public string? SnapshotLock(string table) => null;

    public bool IsLockRefusal(Exception exception) => false;

    // SQL Server has no infinity: 9999-12-31 is a real value, such as a temporal table's open period end.
    public bool ExtremeDatesAreInfinite => false;

    public string SelectSnapshot(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, string? watermarkColumn) =>
        $"SELECT {string.Join(", ", columns.Select(Quote))} FROM {Quote(table)} " +
        (watermarkColumn is null ? "" : $"WHERE ({Quote(watermarkColumn)} > @watermark OR {Quote(watermarkColumn)} IS NULL) ") +
        $"ORDER BY {string.Join(", ", keyColumns.Select(Quote))}";

    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    public EtlSqlTarget DescribeTarget(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        return new(builder.DataSource, null, builder.InitialCatalog, builder.IntegratedSecurity ? "integrated" : builder.UserID);
    }

    public string CreateLedger(string ledgerTable) =>
        $"IF OBJECT_ID(N'{ledgerTable}', N'U') IS NULL CREATE TABLE {Quote(ledgerTable)} (effect_id nvarchar(128) NOT NULL PRIMARY KEY, " +
        "run_id nvarchar(512) NOT NULL, batch_id nvarchar(128) NOT NULL, content_hash char(64) NOT NULL, applied_at bigint NOT NULL)";

    public string InsertLedger(string ledgerTable) =>
        $"INSERT INTO {Quote(ledgerTable)} (effect_id, run_id, batch_id, content_hash, applied_at) VALUES (@effect, @run, @batch, @hash, @applied)";

    public string ReadLedgerHash(string ledgerTable) => $"SELECT content_hash FROM {Quote(ledgerTable)} WHERE effect_id = @effect";

    public string CountUniqueKeys(string table, IReadOnlyList<string> keyColumns)
    {
        var keys = string.Join(", ", keyColumns.Select(column => $"N'{column}'"));
        return $"SELECT CASE WHEN OBJECT_ID(N'{table}', N'U') IS NULL THEN -1 ELSE (" +
               $"SELECT COUNT(*) FROM sys.indexes i WHERE i.object_id = OBJECT_ID(N'{table}') AND i.is_unique = 1 AND i.is_disabled = 0 AND i.has_filter = 0 " +
               "AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id " +
               $"AND ic.is_included_column = 0) = {keyColumns.Count} " +
               "AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
               $"WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0 AND c.name NOT IN ({keys}))) END";
    }

    public string Upsert(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, int row)
    {
        var source = string.Join(", ", columns.Select(column => $"{Parameter(column, row)} AS {Quote(column)}"));
        var on = string.Join(" AND ", keyColumns.Select(column => $"target.{Quote(column)} = source.{Quote(column)}"));
        var updates = columns.Where(column => !keyColumns.Contains(column))
            .Select(column => $"target.{Quote(column)} = source.{Quote(column)}").ToArray();
        var matched = updates.Length == 0 ? string.Empty : $" WHEN MATCHED THEN UPDATE SET {string.Join(", ", updates)}";
        return $"MERGE {Quote(table)} WITH (HOLDLOCK) AS target USING (SELECT {source}) AS source ON ({on}){matched} " +
               $"WHEN NOT MATCHED THEN INSERT ({string.Join(", ", columns.Select(Quote))}) " +
               $"VALUES ({string.Join(", ", columns.Select(column => $"source.{Quote(column)}"))});";
    }

    public string Parameter(string column, int row) => $"@{column}_{row}";

    public EtlSqlErrorKind Classify(Exception exception) => exception switch
    {
        SqlException { Number: 2627 or 2601 } => EtlSqlErrorKind.DuplicateKey,
        SqlException { Number: 2714 } => EtlSqlErrorKind.AlreadyExists,
        SqlException { Number: 208 or 207 } => EtlSqlErrorKind.MissingObject,
        SqlException sql when TransientNumbers.Contains(sql.Number) => EtlSqlErrorKind.Transient,
        // SqlClient reports an exhausted connection pool as InvalidOperationException ("... obtaining a connection from the pool").
        InvalidOperationException pool when pool.Message.Contains("connection from the pool", StringComparison.OrdinalIgnoreCase) => EtlSqlErrorKind.Transient,
        TimeoutException => EtlSqlErrorKind.Transient,
        _ => EtlSqlErrorKind.Rejected
    };

    private static string Quote(string identifier) => string.Join('.', identifier.Split('.').Select(part => $"[{part}]"));
}

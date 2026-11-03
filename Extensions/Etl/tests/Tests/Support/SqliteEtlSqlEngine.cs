using System.Data.Common;
using Elsa.Etl.Core.Runtime;
using Microsoft.Data.Sqlite;

namespace Elsa.Etl.Tests.Support;

/// <summary>
/// A SQLite <see cref="IEtlSqlEngine"/> for testing the SQL delivery protocol without containers (spec 1197). PostgreSQL and
/// SQL Server are proven by the container suite.
/// </summary>
internal sealed class SqliteEtlSqlEngine : IEtlSqlEngine
{
    /// <summary>Thrown by <see cref="FailNextUpsert"/> to simulate a driver failure of a chosen kind.</summary>
    internal sealed class InjectedDbException(EtlSqlErrorKind kind) : DbException("Injected SQL failure.")
    {
        public EtlSqlErrorKind Kind { get; } = kind;
    }

    private EtlSqlErrorKind? _failNextUpsert;

    public string Provider => "sqlite";
    public int MaxParameters { get; init; } = 999;
    public string? SessionSetup => null;

    /// <summary>Makes the next upsert command throw a failure of the given kind once.</summary>
    public void FailNextUpsert(EtlSqlErrorKind kind) => _failNextUpsert = kind;

    public DbConnection CreateConnection(string connectionString) => new FaultingConnection(new SqliteConnection(connectionString), this);

    public EtlSqlTarget DescribeTarget(string connectionString) =>
        new("sqlite", null, new SqliteConnectionStringBuilder(connectionString).DataSource, null);

    public string CreateLedger(string ledgerTable) =>
        $"CREATE TABLE IF NOT EXISTS {Quote(ledgerTable)} (effect_id TEXT PRIMARY KEY, run_id TEXT NOT NULL, " +
        "batch_id TEXT NOT NULL, content_hash TEXT NOT NULL, applied_at INTEGER NOT NULL)";

    public string InsertLedger(string ledgerTable) =>
        $"INSERT INTO {Quote(ledgerTable)} (effect_id, run_id, batch_id, content_hash, applied_at) VALUES (@effect, @run, @batch, @hash, @applied)";

    public string ReadLedgerHash(string ledgerTable) => $"SELECT content_hash FROM {Quote(ledgerTable)} WHERE effect_id = @effect";

    public string CountUniqueKeys(string table, IReadOnlyList<string> keyColumns) =>
        $"SELECT COUNT(*) FROM pragma_index_list('{table}') AS l WHERE l.\"unique\" = 1 AND " +
        $"(SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_index_info(l.name) ORDER BY name)) = " +
        $"'{string.Join(',', keyColumns.Order(StringComparer.Ordinal))}'";

    public string Upsert(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, int row)
    {
        var updates = columns.Where(column => !keyColumns.Contains(column)).Select(column => $"{Quote(column)} = excluded.{Quote(column)}").ToArray();
        return $"INSERT INTO {Quote(table)} ({string.Join(", ", columns.Select(Quote))}) VALUES ({string.Join(", ", columns.Select(column => Parameter(column, row)))}) " +
               $"ON CONFLICT ({string.Join(", ", keyColumns.Select(Quote))}) " +
               (updates.Length == 0 ? "DO NOTHING" : $"DO UPDATE SET {string.Join(", ", updates)}");
    }

    public string Parameter(string column, int row) => $"@{column}_{row}";

    public EtlSqlErrorKind Classify(Exception exception) => exception switch
    {
        InjectedDbException injected => injected.Kind,
        SqliteException { SqliteExtendedErrorCode: 1555 or 2067 } => EtlSqlErrorKind.DuplicateKey,
        SqliteException { SqliteErrorCode: 5 or 6 } => EtlSqlErrorKind.Transient,
        SqliteException sqlite when sqlite.Message.Contains("ON CONFLICT clause does not match", StringComparison.Ordinal) => EtlSqlErrorKind.MissingConstraint,
        SqliteException sqlite when sqlite.Message.Contains("no such table", StringComparison.Ordinal) ||
                                    sqlite.Message.Contains("no such column", StringComparison.Ordinal) ||
                                    sqlite.Message.Contains("has no column named", StringComparison.Ordinal) => EtlSqlErrorKind.MissingObject,
        SqliteException sqlite when sqlite.Message.Contains("already exists", StringComparison.Ordinal) => EtlSqlErrorKind.AlreadyExists,
        _ => EtlSqlErrorKind.Rejected
    };

    private static string Quote(string identifier) => string.Join('.', identifier.Split('.').Select(part => $"\"{part}\""));

    /// <summary>Lets a test inject one upsert failure while every other command reaches SQLite.</summary>
    private sealed class FaultingConnection(SqliteConnection inner, SqliteEtlSqlEngine engine) : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value; }
        public override string Database => inner.Database;
        public override string DataSource => inner.DataSource;
        public override string ServerVersion => inner.ServerVersion;
        public override System.Data.ConnectionState State => inner.State;
        public override void ChangeDatabase(string databaseName) => inner.ChangeDatabase(databaseName);
        public override void Close() => inner.Close();
        public override void Open() => inner.Open();
        public override Task OpenAsync(CancellationToken cancellationToken) => inner.OpenAsync(cancellationToken);
        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel) =>
            new FaultingTransaction(inner.BeginTransaction(System.Data.IsolationLevel.Serializable), this);
        protected override DbCommand CreateDbCommand() => new FaultingCommand(inner.CreateCommand(), engine, this);
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class FaultingTransaction(SqliteTransaction inner, DbConnection connection) : DbTransaction
    {
        public SqliteTransaction Inner => inner;
        public override System.Data.IsolationLevel IsolationLevel => inner.IsolationLevel;
        protected override DbConnection DbConnection => connection;
        public override void Commit() => inner.Commit();
        public override void Rollback() => inner.Rollback();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class FaultingCommand(SqliteCommand inner, SqliteEtlSqlEngine engine, DbConnection connection) : DbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get => inner.CommandText; set => inner.CommandText = value; }
        public override int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
        public override System.Data.CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
        public override bool DesignTimeVisible { get => inner.DesignTimeVisible; set => inner.DesignTimeVisible = value; }
        public override System.Data.UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }
        protected override DbConnection? DbConnection { get => connection; set { } }
        protected override DbParameterCollection DbParameterCollection => inner.Parameters;
        protected override DbTransaction? DbTransaction
        {
            get => null;
            set => inner.Transaction = (value as FaultingTransaction)?.Inner;
        }
        public override void Cancel() => inner.Cancel();
        public override int ExecuteNonQuery()
        {
            if (engine._failNextUpsert is { } kind && inner.CommandText.Contains("ON CONFLICT", StringComparison.Ordinal))
            {
                engine._failNextUpsert = null;
                throw new InjectedDbException(kind);
            }
            return inner.ExecuteNonQuery();
        }
        public override object? ExecuteScalar() => inner.ExecuteScalar();
        public override void Prepare() => inner.Prepare();
        protected override DbParameter CreateDbParameter() => inner.CreateParameter();
        protected override DbDataReader ExecuteDbDataReader(System.Data.CommandBehavior behavior) => inner.ExecuteReader(behavior);
    }
}

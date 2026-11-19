using System.Data.Common;
using Elsa.Etl.Core.Runtime;
using Microsoft.Data.Sqlite;

namespace Elsa.Etl.Tests.Support;

/// <summary>
/// A SQLite <see cref="IEtlSqlEngine"/> for testing the SQL delivery and snapshot protocols without containers (specs 1197 and 1198). PostgreSQL and
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
    private EtlSqlErrorKind? _failNextSnapshot;
    private Exception? _failNextSnapshotWith;

    public string Provider => "sqlite";
    public int MaxParameters { get; init; } = 999;
    public string? SessionSetup => null;

    /// <summary>Makes the next upsert command throw a failure of the given kind once.</summary>
    public void FailNextUpsert(EtlSqlErrorKind kind) => _failNextUpsert = kind;

    /// <summary>Makes the next snapshot read throw a failure of the given kind once (spec 1198).</summary>
    public void FailNextSnapshot(EtlSqlErrorKind kind) => _failNextSnapshot = kind;

    /// <summary>Makes the next snapshot read throw the given exception once, as a provider failure outside the DbException family.</summary>
    public void FailNextSnapshot(Exception exception) => _failNextSnapshotWith = exception;

    /// <summary>The number of snapshot reads issued, so a test can prove a published snapshot was reused.</summary>
    public int SnapshotReads { get; private set; }

    // SQLite's serializable transactions read one point in time.
    public System.Data.IsolationLevel SnapshotIsolation => System.Data.IsolationLevel.Serializable;
    public string? SnapshotSetup => null;
    public string? SnapshotLock(string table) => null;
    public bool IsLockRefusal(Exception exception) => false;
    public bool ExtremeDatesAreInfinite => false;

    public string SelectSnapshot(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, string? watermarkColumn) =>
        $"SELECT {string.Join(", ", columns.Select(Quote))} FROM {Quote(table)} " +
        (watermarkColumn is null ? "" : $"WHERE ({Quote(watermarkColumn)} > @watermark OR {Quote(watermarkColumn)} IS NULL) ") +
        $"ORDER BY {string.Join(", ", keyColumns.Select(Quote))}";

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
        $"SELECT CASE WHEN (SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}') = 0 THEN -1 ELSE (" +
        $"SELECT COUNT(*) FROM pragma_index_list('{table}') AS l WHERE l.\"unique\" = 1 AND " +
        $"(SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_index_info(l.name) ORDER BY name)) = " +
        $"'{string.Join(',', keyColumns.Order(StringComparer.Ordinal))}') END";

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
        protected override DbDataReader ExecuteDbDataReader(System.Data.CommandBehavior behavior)
        {
            if (inner.CommandText.Contains("ORDER BY", StringComparison.Ordinal))
            {
                engine.SnapshotReads++;
                if (engine._failNextSnapshotWith is { } failure)
                {
                    engine._failNextSnapshotWith = null;
                    throw failure;
                }
                if (engine._failNextSnapshot is { } kind)
                {
                    engine._failNextSnapshot = null;
                    throw new InjectedDbException(kind);
                }
            }
            return new TypedReader(inner.ExecuteReader(behavior));
        }
    }

    /// <summary>
    /// Types values by declared column type as PostgreSQL and SQL Server drivers do, since SQLite returns its storage class:
    /// <c>DECIMAL_TEXT</c> as <see cref="decimal"/> (the name gives it text affinity, so SQLite keeps the digits exact) and
    /// <c>BOOLEAN</c> integers as <see cref="bool"/>.
    /// </summary>
    private sealed class TypedReader(SqliteDataReader inner) : DbDataReader
    {
        public override object GetValue(int ordinal)
        {
            var value = inner.GetValue(ordinal);
            return inner.GetDataTypeName(ordinal).ToUpperInvariant() switch
            {
                "DECIMAL_TEXT" when value is string text => decimal.Parse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture),
                "DECIMAL_TEXT" when value is long number => (decimal)number,
                "BOOLEAN" when value is long flag => flag != 0,
                _ => value
            };
        }

        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override int Depth => inner.Depth;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override bool Read() => inner.Read();
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => inner.ReadAsync(cancellationToken);
        public override bool NextResult() => inner.NextResult();
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override Type GetFieldType(int ordinal) => GetValue(ordinal).GetType();
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
            inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
            inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override System.Collections.IEnumerator GetEnumerator() => inner.GetEnumerator();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Preparation;
using Microsoft.Data.SqlClient;

namespace Elsa.Etl.Sql.ProviderTests;

/// <summary>Spec 1198: the SQL snapshot read on each real engine.</summary>
public abstract partial class EtlSqlProviderTests
{
    private static readonly EtlColumn[] SnapshotColumns =
    [
        new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, false), new("score", EtlColumnType.Decimal, false),
        new("active", EtlColumnType.Boolean, false), new("small", EtlColumnType.Int64, false), new("uid", EtlColumnType.String, false),
        new("at", EtlColumnType.String, false), new("whole", EtlColumnType.Int64, false), new("amount", EtlColumnType.Decimal, false),
        new("day", EtlColumnType.String, false), new("zoned", EtlColumnType.String, false), new("clock", EtlColumnType.String, false)
    ];

    /// <summary>A literal for the zoned column: 10:15:30 at UTC+1.</summary>
    protected abstract string ZonedLiteral { get; }

    /// <summary>A table holding one column of each accepted database type, in <see cref="SnapshotColumns"/> order.</summary>
    protected abstract string CreateTypedTableSql(string table);

    /// <summary>A literal true for the engine's boolean column.</summary>
    protected abstract string TrueLiteral { get; }

    [SkippableFact]
    public async Task A_snapshot_reads_every_accepted_type_in_key_order_with_nulls()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await fixture.ExecuteAsync(CreateTypedTableSql(_table));
        await fixture.ExecuteAsync($"INSERT INTO {_table} (id, name, score, active, small, uid, at, whole, amount, day, zoned, clock) VALUES " +
            $"(2, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL), " +
            $"(1, N'Привет', 1.5, {TrueLiteral}, 7, '6f9619ff-8b86-d011-b42d-00c04fc964ff', '2026-11-03 10:15:30', 42, 7, '2026-11-03', {ZonedLiteral}, '10:15:30')");

        var raw = await ReadRawSnapshotAsync(new EtlSqlSource("db", _table, SnapshotColumns, ["id"]));
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line)).ToArray();

        // Non-ASCII text is retained as raw UTF-8, not escaped.
        Assert.Contains("\"name\":\"Привет\"", raw, StringComparison.Ordinal);
        Assert.Equal(2, lines.Length);
        var ada = lines[0].RootElement;
        Assert.Equal((1L, "Привет", 1.5m, true, 7L), (ada.GetProperty("id").GetInt64(), ada.GetProperty("name").GetString(),
            ada.GetProperty("score").GetDecimal(), ada.GetProperty("active").GetBoolean(), ada.GetProperty("small").GetInt64()));
        Assert.Equal((42L, 7m, "2026-11-03", "10:15:30.0000000"), (ada.GetProperty("whole").GetInt64(), ada.GetProperty("amount").GetDecimal(),
            ada.GetProperty("day").GetString(), ada.GetProperty("clock").GetString()));
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 9, 15, 30, TimeSpan.Zero),
            DateTimeOffset.Parse(ada.GetProperty("zoned").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal("6f9619ff-8b86-d011-b42d-00c04fc964ff", ada.GetProperty("uid").GetString());
        Assert.StartsWith("2026-11-03T10:15:30", ada.GetProperty("at").GetString(), StringComparison.Ordinal);
        var nulls = lines[1].RootElement;
        Assert.Equal(2L, nulls.GetProperty("id").GetInt64());
        Assert.All(SnapshotColumns.Skip(1), column => Assert.Equal(JsonValueKind.Null, nulls.GetProperty(column.Name).ValueKind));
    }

    [SkippableFact]
    public async Task A_floating_point_column_is_refused_rather_than_rounded()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL, ratio float NULL)");
        await fixture.ExecuteAsync($"INSERT INTO {_table} (id, ratio) VALUES (1, 0.1)");

        var failure = await Assert.ThrowsAsync<EtlConnectorPreparationFailure>(() => ReadSnapshotAsync(
            new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true), new("ratio", EtlColumnType.Decimal, false)], ["id"])));

        Assert.Equal(("etl.sql-column-type-unsupported", "ratio"), (failure.Diagnostic.Code, failure.Diagnostic.Field));
    }

    [SkippableFact]
    public async Task A_decimal_beyond_dotnet_range_is_refused_and_an_empty_table_reads_no_rows()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL, huge decimal(38, 0) NULL)");
        var source = new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true), new("huge", EtlColumnType.Decimal, false)], ["id"]);

        Assert.Empty(await ReadSnapshotAsync(source));

        await fixture.ExecuteAsync($"INSERT INTO {_table} (id, huge) VALUES (1, 100000000000000000000000000000000000)");
        var failure = await Assert.ThrowsAsync<EtlConnectorPreparationFailure>(() => ReadSnapshotAsync(source));
        Assert.Equal(("etl.sql-column-type-unsupported", "huge"), (failure.Diagnostic.Code, failure.Diagnostic.Field));
    }

    [SkippableFact]
    public async Task An_unreachable_database_is_a_transient_read_failure()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);

        // Nothing listens on port 1, so the connection is refused: an outage that pauses preparation, not a failure.
        await Assert.ThrowsAsync<EtlSqlTransientException>(() => ReadSnapshotAsync(
            new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true)], ["id"]), UnreachableConnectionString));
    }

    /// <summary>The fixture's connection string pointed at a port nothing listens on.</summary>
    protected abstract string UnreachableConnectionString { get; }

    /// <summary>
    /// The read stops at the limit while the server is still streaming a large result: cancelling and closing the reader
    /// must not replace the limit's failure with a cancellation error.
    /// </summary>
    [SkippableFact]
    public async Task A_large_table_beyond_the_snapshot_limit_fails_with_the_limit_not_a_cancellation()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL PRIMARY KEY, name varchar(200) NOT NULL)");
        var filler = new string('x', 150);
        // SQL Server allows at most 1,000 rows per INSERT.
        for (var start = 1; start <= 200_000; start += 1_000)
            await fixture.ExecuteAsync($"INSERT INTO {_table} (id, name) VALUES " + string.Join(", ",
                Enumerable.Range(start, 1_000).Select(id => $"({id}, '{filler}')")));
        var source = new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, true)], ["id"]);

        using var destination = new MemoryStream();
        var failure = await Assert.ThrowsAsync<EtlConnectorPreparationFailure>(() =>
            EtlSqlSnapshotReader.WriteAsync(Binding(fixture.ConnectionString), source, 65_536, destination, default).AsTask());

        Assert.Equal("etl.snapshot-too-large", failure.Diagnostic.Code);
    }

    [SkippableFact]
    public async Task A_watermark_floor_reads_only_rows_above_it_in_key_order()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL PRIMARY KEY, version bigint NULL)");
        await fixture.ExecuteAsync($"INSERT INTO {_table} (id, version) VALUES (1, 5), (2, 9), (3, 7), (4, 6), (5, NULL)");
        var source = new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true), new("version", EtlColumnType.Int64, true)], ["id"])
            { Watermark = "version" };

        // A NULL watermark is kept, so the run rejects it as a record error rather than skipping it.
        Assert.Equal("{\"id\":2,\"version\":9}\n{\"id\":3,\"version\":7}\n{\"id\":5,\"version\":null}\n",
            await ReadRawSnapshotAsync(source, watermarkFloor: 6));
        Assert.Equal(5, (await ReadRawSnapshotAsync(source)).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [SkippableFact]
    public async Task A_missing_table_is_a_read_rejection()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);

        var failure = await Assert.ThrowsAsync<EtlConnectorPreparationFailure>(() => ReadSnapshotAsync(
            new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true)], ["id"])));

        Assert.Equal("etl.sql-read-rejected", failure.Diagnostic.Code);
    }

    /// <summary>
    /// A writer commits changes while the read is paused part-way through a result larger than the drivers buffer. The
    /// writer is not blocked, and the snapshot holds the rows as of the read's start.
    /// </summary>
    [SkippableFact]
    public async Task A_change_committed_while_the_snapshot_is_being_read_does_not_reach_it()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        const int rows = 20_000;
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL PRIMARY KEY, name varchar(200) NOT NULL)");
        var filler = new string('x', 150);
        for (var start = 1; start <= rows; start += 1_000)
            await fixture.ExecuteAsync($"INSERT INTO {_table} (id, name) VALUES " + string.Join(", ",
                Enumerable.Range(start, 1_000).Select(id => $"({id}, '{filler}')")));
        var source = new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, true)], ["id"]);

        var destination = new PausingStream(pauseAfterWrites: 10);
        var read = EtlSqlSnapshotReader.WriteAsync(Binding(fixture.ConnectionString), source, long.MaxValue, destination, default).AsTask();
        await destination.Paused.WaitAsync(TimeSpan.FromSeconds(60));
        // A blocked writer would time out here. This does not prove the isolation level: the drivers may already have
        // buffered the remaining rows, so the test passes under READ COMMITTED too (see the evidence README).
        await fixture.ExecuteAsync($"UPDATE {_table} SET name = 'changed' WHERE id = {rows}; DELETE FROM {_table} WHERE id = {rows - 1}; " +
                                   $"INSERT INTO {_table} (id, name) VALUES ({rows + 1}, 'late')").WaitAsync(TimeSpan.FromSeconds(30));
        destination.Resume();
        await read.WaitAsync(TimeSpan.FromSeconds(120));

        var lines = Encoding.UTF8.GetString(destination.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(rows, lines.Length);
        Assert.Equal($"{{\"id\":{rows},\"name\":\"{filler}\"}}", lines[^1]);
        Assert.Equal($"{{\"id\":{rows - 1},\"name\":\"{filler}\"}}", lines[^2]);
    }

    private async Task<JsonDocument[]> ReadSnapshotAsync(EtlSqlSource source, string? connectionString = null) =>
        (await ReadRawSnapshotAsync(source, connectionString)).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line)).ToArray();

    private async Task<string> ReadRawSnapshotAsync(EtlSqlSource source, string? connectionString = null, long? watermarkFloor = null)
    {
        using var destination = new MemoryStream();
        await EtlSqlSnapshotReader.WriteAsync(Binding(connectionString ?? fixture.ConnectionString), source, long.MaxValue, destination, default,
            watermarkFloor);
        return Encoding.UTF8.GetString(destination.ToArray());
    }

    private protected Task ReadSnapshotOnAsync(string connectionString, EtlSqlSource source) => ReadSnapshotAsync(source, connectionString);

    private protected Task<string> ReadRawSnapshotOnAsync(string connectionString, EtlSqlSource source) => ReadRawSnapshotAsync(source, connectionString);

    private HostSqlResolvedBinding Binding(string connectionString) =>
        new("db", "v1", new string('a', 64), fixture.Engine, connectionString, "ledger", false);

    /// <summary>Collects the snapshot, pausing once after a number of writes until the test resumes it.</summary>
    private sealed class PausingStream(int pauseAfterWrites) : MemoryStream
    {
        private readonly TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writes;

        public Task Paused => _paused.Task;
        public void Resume() => _resumed.TrySetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken);
            if (++_writes == pauseAfterWrites)
            {
                _paused.TrySetResult();
                await _resumed.Task.WaitAsync(cancellationToken);
            }
        }
    }
}

public sealed partial class EtlSqlPostgreSqlProviderTests
{
    protected override string CreateTypedTableSql(string table) =>
        $"CREATE TABLE {table} (id bigint NOT NULL, name text NULL, score numeric(18, 4) NULL, active boolean NULL, " +
        "small smallint NULL, uid uuid NULL, at timestamp NULL, whole integer NULL, amount integer NULL, day date NULL, zoned timestamptz NULL, clock time NULL)";

    protected override string TrueLiteral => "true";
    protected override string ZonedLiteral => "'2026-11-03 10:15:30+01'";

    [SkippableTheory]
    [InlineData("timetz")]
    [InlineData("timetz(0)")]
    public async Task A_zoned_time_of_day_is_refused_rather_than_given_a_placeholder_date(string type)
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL, clock {type} NULL)");
        await fixture.ExecuteAsync($"INSERT INTO {_table} (id, clock) VALUES (1, '10:15:30+01')");

        var failure = await Assert.ThrowsAsync<EtlConnectorPreparationFailure>(() => ReadSnapshotOnAsync(fixture.ConnectionString,
            new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true), new("clock", EtlColumnType.String, false)], ["id"])));

        Assert.Equal(("etl.sql-column-type-unsupported", "clock"), (failure.Diagnostic.Code, failure.Diagnostic.Field));
    }

    /// <summary>
    /// TRUNCATE is not MVCC-safe: a snapshot taken while a truncate-and-reload holds its lock would see the table empty once
    /// the lock is released. The read must wait for the lock before it takes its snapshot.
    /// </summary>
    [SkippableFact]
    public async Task A_read_behind_a_truncate_and_reload_sees_the_reloaded_rows_not_an_empty_table()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL PRIMARY KEY)");
        await fixture.ExecuteAsync($"INSERT INTO {_table} (id) VALUES (1), (2), (3)");
        await using var reload = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await reload.OpenAsync();
        await using var transaction = await reload.BeginTransactionAsync();
        await using (var command = reload.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"TRUNCATE {_table}; INSERT INTO {_table} (id) VALUES (10), (11)";
            await command.ExecuteNonQueryAsync();
        }

        var read = ReadRawSnapshotOnAsync(fixture.ConnectionString, new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true)], ["id"]));
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(read.IsCompleted);
        await transaction.CommitAsync();

        Assert.Equal("{\"id\":10}\n{\"id\":11}\n", await read.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [SkippableFact]
    public async Task A_lock_wait_cut_short_by_a_statement_timeout_fails_the_read_rather_than_reading_unlocked()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL PRIMARY KEY)");
        await fixture.ExecuteAsync($"INSERT INTO {_table} (id) VALUES (1)");
        await using var reload = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await reload.OpenAsync();
        await using var transaction = await reload.BeginTransactionAsync();
        await using (var command = reload.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"TRUNCATE {_table}; INSERT INTO {_table} (id) VALUES (10)";
            await command.ExecuteNonQueryAsync();
        }
        var impatient = new Npgsql.NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Options = "-c statement_timeout=1000" }.ConnectionString;

        // The lock times out at 1 s and the reload commits at 1.5 s. A fallback to an unlocked read would take its snapshot
        // at about 1 s and then wait out the reload, publishing an empty table; a timed-out lock must not fall back.
        var read = Record.ExceptionAsync(() => ReadRawSnapshotOnAsync(impatient,
            new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true)], ["id"])));
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        await transaction.CommitAsync();
        var failure = await read.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(failure is EtlSqlTransientException or EtlConnectorPreparationFailure, failure?.GetType().Name ?? "no failure");
    }

    [SkippableFact]
    public async Task A_role_with_column_grants_only_and_a_materialized_view_still_read_without_the_lock()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var (view, role) = ("matview_" + suffix, "etl_cols_" + suffix);
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL PRIMARY KEY, secret text NULL)");
        await fixture.ExecuteAsync($"INSERT INTO {_table} (id, secret) VALUES (1, 'x'), (2, 'y')");
        await fixture.ExecuteAsync($"CREATE MATERIALIZED VIEW {view} AS SELECT id FROM {_table}");
        // LOCK TABLE needs table-level SELECT and refuses a materialized view; the read itself is allowed in both cases.
        await fixture.ExecuteAsync($"CREATE ROLE {role} LOGIN PASSWORD 'etl'; GRANT USAGE ON SCHEMA public TO {role}; GRANT SELECT (id) ON {_table} TO {role}");
        var limited = new Npgsql.NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Username = role, Password = "etl" }.ConnectionString;
        var columns = new EtlColumn[] { new("id", EtlColumnType.Int64, true) };

        Assert.Equal("{\"id\":1}\n{\"id\":2}\n", await ReadRawSnapshotOnAsync(limited, new EtlSqlSource("db", _table, columns, ["id"])));
        Assert.Equal("{\"id\":1}\n{\"id\":2}\n", await ReadRawSnapshotOnAsync(fixture.ConnectionString, new EtlSqlSource("db", view, columns, ["id"])));
    }

    [SkippableTheory]
    [InlineData("timestamp", "'infinity'")]
    [InlineData("timestamptz", "'-infinity'")]
    [InlineData("date", "'infinity'")]
    public async Task An_infinite_date_or_timestamp_is_refused_rather_than_written_as_an_extreme_date(string type, string literal)
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL, at {type} NULL)");
        await fixture.ExecuteAsync($"INSERT INTO {_table} (id, at) VALUES (1, {literal})");

        var failure = await Assert.ThrowsAsync<EtlConnectorPreparationFailure>(() => ReadSnapshotOnAsync(fixture.ConnectionString,
            new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true), new("at", EtlColumnType.String, false)], ["id"])));

        Assert.Equal(("etl.sql-column-type-unsupported", "at"), (failure.Diagnostic.Code, failure.Diagnostic.Field));
    }

    [SkippableFact]
    public async Task Text_that_is_not_valid_utf8_is_a_rejected_read_not_an_oversized_snapshot()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        var database = "etl_ascii_" + Guid.NewGuid().ToString("N")[..8];
        await fixture.ExecuteAsync($"CREATE DATABASE {database} ENCODING 'SQL_ASCII' LC_COLLATE 'C' LC_CTYPE 'C' TEMPLATE template0");
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = database }.ConnectionString;
        await using (var setup = new Npgsql.NpgsqlConnection(connection))
        {
            await setup.OpenAsync();
            await using var command = setup.CreateCommand();
            command.CommandText = "CREATE TABLE people (id bigint NOT NULL, name text NULL); INSERT INTO people VALUES (1, E'caf\\xe9')";
            await command.ExecuteNonQueryAsync();
        }

        var failure = await Assert.ThrowsAsync<EtlConnectorPreparationFailure>(() => ReadSnapshotOnAsync(connection,
            new EtlSqlSource("db", "people", [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, false)], ["id"])));

        // Npgsql fails the row before the value is read, so the read is rejected as a whole.
        Assert.Equal("etl.sql-read-rejected", failure.Diagnostic.Code);
    }

    [SkippableFact]
    public async Task An_interval_is_a_duration_not_a_time_and_is_refused()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL, span interval NULL)");
        await fixture.ExecuteAsync($"INSERT INTO {_table} (id, span) VALUES (1, '02:03:04')");

        var failure = await Assert.ThrowsAsync<EtlConnectorPreparationFailure>(() => ReadSnapshotOnAsync(fixture.ConnectionString,
            new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true), new("span", EtlColumnType.String, false)], ["id"])));

        Assert.Equal(("etl.sql-column-type-unsupported", "span"), (failure.Diagnostic.Code, failure.Diagnostic.Field));
    }
    protected override string UnreachableConnectionString =>
        new Npgsql.NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Host = "127.0.0.1", Port = 1, Timeout = 5 }.ConnectionString;
}

public sealed partial class EtlSqlServerProviderTests
{
    protected override string CreateTypedTableSql(string table) =>
        $"CREATE TABLE {table} (id bigint NOT NULL, name nvarchar(200) NULL, score decimal(18, 4) NULL, active bit NULL, " +
        "small tinyint NULL, uid uniqueidentifier NULL, at datetime2 NULL, whole int NULL, amount int NULL, day date NULL, zoned datetimeoffset NULL, clock time NULL)";

    protected override string TrueLiteral => "1";
    protected override string ZonedLiteral => "'2026-11-03 10:15:30 +01:00'";
    protected override string UnreachableConnectionString =>
        new SqlConnectionStringBuilder(fixture.ConnectionString) { DataSource = "tcp:127.0.0.1,1", ConnectTimeout = 5, ConnectRetryCount = 0 }.ConnectionString;

    [SkippableFact]
    public async Task Extreme_dates_are_real_values_on_sql_server_and_are_kept()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        // A temporal table's open period ends at 9999-12-31 23:59:59.9999999; SQL Server has no infinity.
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL, period_end datetime2(7) NULL, first_day date NULL)");
        await fixture.ExecuteAsync($"INSERT INTO {_table} (id, period_end, first_day) VALUES (1, '9999-12-31 23:59:59.9999999', '0001-01-01')");

        var raw = await ReadRawSnapshotOnAsync(fixture.ConnectionString, new EtlSqlSource("db", _table,
            [new("id", EtlColumnType.Int64, true), new("period_end", EtlColumnType.String, false), new("first_day", EtlColumnType.String, false)], ["id"]));

        Assert.Equal("{\"id\":1,\"period_end\":\"9999-12-31T23:59:59.9999999\",\"first_day\":\"0001-01-01\"}\n", raw);
    }

    [SkippableFact]
    public async Task An_unpaired_surrogate_arrives_already_replaced_by_the_driver()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await fixture.ExecuteAsync($"CREATE TABLE {_table} (id bigint NOT NULL, name nvarchar(20) NULL)");
        await fixture.ExecuteAsync($"INSERT INTO {_table} (id, name) VALUES (1, N'a' + NCHAR(55296) + N'b')");

        var raw = await ReadRawSnapshotOnAsync(fixture.ConnectionString,
            new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, false)], ["id"]));

        // SqlClient decodes UTF-16 with replacement, so the reader receives U+FFFD and retains it; PostgreSQL never
        // stores an unpaired surrogate in valid UTF-8 text. The README states the limit.
        Assert.Equal("{\"id\":1,\"name\":\"a\uFFFDb\"}\n", raw);
    }

    [SkippableFact]
    public async Task A_database_without_snapshot_isolation_is_a_read_rejection()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        var database = "etl_nosnapshot_" + Guid.NewGuid().ToString("N")[..8];
        await fixture.ExecuteAsync($"CREATE DATABASE {database}");
        var connection = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = database }.ConnectionString;
        await using (var setup = new SqlConnection(connection))
        {
            await setup.OpenAsync();
            await using var command = setup.CreateCommand();
            command.CommandText = "CREATE TABLE people (id bigint NOT NULL PRIMARY KEY); INSERT INTO people VALUES (1)";
            await command.ExecuteNonQueryAsync();
        }

        var failure = await Assert.ThrowsAsync<Elsa.Etl.Runtime.Preparation.EtlConnectorPreparationFailure>(() => ReadSnapshotOnAsync(connection,
            new EtlSqlSource("db", "people", [new("id", EtlColumnType.Int64, true)], ["id"])));

        Assert.Equal("etl.sql-read-rejected", failure.Diagnostic.Code);
    }
}

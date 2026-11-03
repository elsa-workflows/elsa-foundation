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
        await Assert.ThrowsAsync<EtlSqlTransientReadException>(() => ReadSnapshotAsync(
            new EtlSqlSource("db", _table, [new("id", EtlColumnType.Int64, true)], ["id"]), UnreachableConnectionString));
    }

    /// <summary>The fixture's connection string pointed at a port nothing listens on.</summary>
    protected abstract string UnreachableConnectionString { get; }

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

    private async Task<string> ReadRawSnapshotAsync(EtlSqlSource source, string? connectionString = null)
    {
        using var destination = new MemoryStream();
        await EtlSqlSnapshotReader.WriteAsync(Binding(connectionString ?? fixture.ConnectionString), source, long.MaxValue, destination, default);
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

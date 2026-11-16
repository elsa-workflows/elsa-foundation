using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1197: the SQL delivery protocol, on a SQLite test engine.</summary>
public sealed class EtlSqlRouteWriterTests : IDisposable
{
    private static readonly EtlColumn[] Columns =
        [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, true), new("score", EtlColumnType.Decimal, false)];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "etl-sql-writer-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteEtlSqlEngine _engine = new();

    public EtlSqlRouteWriterTests()
    {
        Directory.CreateDirectory(_directory);
        Execute("CREATE TABLE people (id INTEGER NOT NULL, name TEXT NOT NULL, score NUMERIC NULL, CONSTRAINT people_key UNIQUE (id))");
        Execute("CREATE TABLE unkeyed (id INTEGER NOT NULL, name TEXT NOT NULL, score NUMERIC NULL)");
    }

    private string ConnectionString => $"Data Source={Path.Combine(_directory, "target.db")};Pooling=False";

    [Fact]
    public async Task A_batch_upserts_its_records_with_a_ledger_row_and_a_redelivery_writes_nothing()
    {
        var delivery = Delivery("effect-1", """[{"id":1,"name":"Ada","score":1.5},{"id":2,"name":"Bram","score":null}]""");

        Assert.Equal(EtlSqlWriteOutcome.Applied, await Writer().WriteAsync(delivery, default));
        Execute("UPDATE people SET name = 'changed' WHERE id = 1");
        Assert.Equal(EtlSqlWriteOutcome.Replay, await Writer().WriteAsync(delivery, default));

        Assert.Equal(["1|changed|1.5", "2|Bram|"], Rows("people"));
        Assert.Equal(["effect-1|run|batch-effect-1"], Rows("elsa_etl_effect_ledger", "effect_id, run_id, batch_id"));
    }

    [Fact]
    public async Task A_later_batch_updates_existing_keys_inserts_new_ones_and_a_repeated_key_keeps_the_later_record()
    {
        await Writer().WriteAsync(Delivery("effect-1", """[{"id":1,"name":"Ada"},{"id":2,"name":"Bram"}]"""), default);

        await Writer().WriteAsync(Delivery("effect-2", """[{"id":2,"name":"Cleo"},{"id":3,"name":"Dirk"},{"id":3,"name":"Eva"}]"""), default);

        Assert.Equal(["1|Ada|", "2|Cleo|", "3|Eva|"], Rows("people"));
    }

    [Fact]
    public async Task A_table_without_a_unique_key_fails_closed_and_commits_nothing()
    {
        var failure = await Assert.ThrowsAsync<EtlEffectException>(() =>
            Writer().WriteAsync(Delivery("effect-1", """[{"id":1,"name":"Ada"}]""", "unkeyed"), default).AsTask());

        Assert.Equal("etl.sql-key-constraint-missing", failure.Code);
        Assert.Empty(Rows("unkeyed"));
        Assert.Empty(Rows("elsa_etl_effect_ledger", "effect_id"));
    }

    [Fact]
    public async Task A_missing_table_is_a_rejection_not_a_missing_key()
    {
        var failure = await Assert.ThrowsAsync<EtlEffectException>(() =>
            Writer().WriteAsync(Delivery("effect-1", """[{"id":1,"name":"Ada"}]""", "absent"), default).AsTask());

        Assert.Equal("etl.sql-write-rejected", failure.Code);
        Assert.Empty(Rows("elsa_etl_effect_ledger", "effect_id"));
    }

    [Fact]
    public async Task The_same_effect_with_different_content_is_a_conflict()
    {
        await Writer().WriteAsync(Delivery("effect-1", """[{"id":1,"name":"Ada"}]"""), default);

        var failure = await Assert.ThrowsAsync<EtlEffectException>(() =>
            Writer().WriteAsync(Delivery("effect-1", """[{"id":1,"name":"Other"}]""", hash: new string('b', 64)), default).AsTask());

        Assert.Equal("etl.effect-conflict", failure.Code);
        Assert.Equal(["1|Ada|"], Rows("people"));
    }

    [Fact]
    public async Task A_rejected_row_rolls_the_whole_batch_back_across_chunks()
    {
        var engine = new SqliteEtlSqlEngine { MaxParameters = 3 };
        // One row per command: the last command's NOT NULL violation must undo the earlier commands and the ledger row.
        var failure = await Assert.ThrowsAsync<EtlEffectException>(() => new EtlSqlRouteWriter(engine, TimeProvider.System)
            .WriteAsync(Delivery("effect-1", """[{"id":1,"name":"Ada"},{"id":2,"name":"Bram"},{"id":3,"name":null}]"""), default).AsTask());

        Assert.Equal("etl.sql-write-rejected", failure.Code);
        Assert.Empty(Rows("people"));
        Assert.Empty(Rows("elsa_etl_effect_ledger", "effect_id"));
    }

    [Fact]
    public async Task A_transient_failure_yields_without_writing_and_the_next_attempt_applies()
    {
        _engine.FailNextUpsert(EtlSqlErrorKind.Transient);
        var delivery = Delivery("effect-1", """[{"id":1,"name":"Ada"}]""");

        Assert.Equal(EtlSqlWriteOutcome.Yield, await Writer().WriteAsync(delivery, default));
        Assert.Empty(Rows("people"));
        Assert.Empty(Rows("elsa_etl_effect_ledger", "effect_id"));
        Assert.Equal(EtlSqlWriteOutcome.Applied, await Writer().WriteAsync(delivery, default));
        Assert.Equal(["1|Ada|"], Rows("people"));
    }

    [Fact]
    public async Task A_missing_ledger_without_creation_fails_and_a_concurrent_creation_counts_as_created()
    {
        var failure = await Assert.ThrowsAsync<EtlEffectException>(() =>
            Writer().WriteAsync(Delivery("effect-1", """[{"id":1,"name":"Ada"}]""") with { CreateLedger = false }, default).AsTask());
        Assert.Equal("etl.sql-ledger-missing", failure.Code);

        Execute(_engine.CreateLedger("elsa_etl_effect_ledger"));
        Assert.Equal(EtlSqlWriteOutcome.Applied, await Writer().WriteAsync(Delivery("effect-1", """[{"id":1,"name":"Ada"}]"""), default));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private EtlSqlRouteWriter Writer() => new(_engine, TimeProvider.System);

    private EtlSqlRouteDelivery Delivery(string effect, string body, string table = "people", string? hash = null) =>
        new(ConnectionString, "elsa_etl_effect_ledger", true, table, ["id"], Columns, Encoding.UTF8.GetBytes(body),
            effect, "run", "batch-" + effect, hash ?? new string('a', 64));

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private string[] Rows(string table, string columns = "id, name, score")
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM sqlite_master WHERE name = '{table}'";
        if ((long)command.ExecuteScalar()! == 0) return [];
        command.CommandText = $"SELECT {columns} FROM {table} ORDER BY 1";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        return rows.ToArray();
    }
}

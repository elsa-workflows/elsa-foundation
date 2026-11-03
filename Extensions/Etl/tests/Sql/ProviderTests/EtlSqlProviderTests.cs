using System.Data.Common;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Output;
using Npgsql;

namespace Elsa.Etl.Sql.ProviderTests;

/// <summary>Spec 1197: the SQL delivery protocol on each real engine.</summary>
public abstract class EtlSqlProviderTests(EtlSqlProviderFixture fixture)
{
    private static readonly EtlColumn[] Columns =
        [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, true), new("score", EtlColumnType.Decimal, false)];

    private readonly string _table = "people_" + Guid.NewGuid().ToString("N")[..12];
    private readonly string _ledger = "ledger_" + Guid.NewGuid().ToString("N")[..12];

    protected abstract string CreateTableSql(string table, bool keyed);

    [SkippableFact]
    public async Task Records_are_upserted_by_key_and_a_repeated_key_keeps_the_later_record()
    {
        await CreateTableAsync(keyed: true);

        Assert.Equal(EtlSqlWriteOutcome.Applied, await WriteAsync("effect-1", """[{"id":1,"name":"Ada","score":1.5},{"id":2,"name":"Bram","score":null}]"""));
        Assert.Equal(EtlSqlWriteOutcome.Applied, await WriteAsync("effect-2", """[{"id":2,"name":"Cleo","score":2.25},{"id":3,"name":"Dirk"},{"id":3,"name":"Eva"}]"""));

        Assert.Equal(["1|Ada|1.5", "2|Cleo|2.25", "3|Eva|"], Normalize(await fixture.QueryAsync($"SELECT id, name, score FROM {_table} ORDER BY id")));
        Assert.Equal(["effect-1|batch-effect-1", "effect-2|batch-effect-2"],
            await fixture.QueryAsync($"SELECT effect_id, batch_id FROM {_ledger} ORDER BY effect_id"));
    }

    [SkippableFact]
    public async Task A_redelivered_effect_writes_nothing_and_different_content_under_it_is_a_conflict()
    {
        await CreateTableAsync(keyed: true);
        await WriteAsync("effect-1", """[{"id":1,"name":"Ada"}]""");
        await fixture.ExecuteAsync($"UPDATE {_table} SET name = 'changed' WHERE id = 1");

        Assert.Equal(EtlSqlWriteOutcome.Replay, await WriteAsync("effect-1", """[{"id":1,"name":"Ada"}]"""));
        var conflict = await Assert.ThrowsAsync<EtlEffectException>(() => WriteAsync("effect-1", """[{"id":1,"name":"Other"}]""", new string('b', 64)));

        Assert.Equal("etl.effect-conflict", conflict.Code);
        Assert.Equal(["1|changed|"], Normalize(await fixture.QueryAsync($"SELECT id, name, score FROM {_table}")));
    }

    [SkippableFact]
    public async Task A_table_without_a_unique_key_fails_closed_and_commits_nothing()
    {
        await CreateTableAsync(keyed: false);

        var failure = await Assert.ThrowsAsync<EtlEffectException>(() => WriteAsync("effect-1", """[{"id":1,"name":"Ada"}]"""));

        Assert.Equal("etl.sql-key-constraint-missing", failure.Code);
        Assert.Empty(await fixture.QueryAsync($"SELECT id FROM {_table}"));
        Assert.Empty(await fixture.QueryAsync($"SELECT effect_id FROM {_ledger}"));
    }

    [SkippableFact]
    public async Task A_rejected_value_rolls_the_whole_batch_back_across_commands()
    {
        await CreateTableAsync(keyed: true);
        // One row per command: the third command's NOT NULL violation must undo the first two and the ledger row.
        var failure = await Assert.ThrowsAsync<EtlEffectException>(() => WriteAsync("effect-1",
            """[{"id":1,"name":"Ada"},{"id":2,"name":"Bram"},{"id":3,"name":null}]""", engine: new OneRowPerCommand(fixture.Engine)));

        Assert.Equal("etl.sql-write-rejected", failure.Code);
        Assert.Empty(await fixture.QueryAsync($"SELECT id FROM {_table}"));
        Assert.Empty(await fixture.QueryAsync($"SELECT effect_id FROM {_ledger}"));
    }

    [SkippableFact]
    public async Task Concurrent_first_deliveries_both_create_or_find_the_ledger_and_each_effect_applies_once()
    {
        await CreateTableAsync(keyed: true);

        var outcomes = await Task.WhenAll(
            WriteAsync("effect-a", """[{"id":1,"name":"Ada"}]"""),
            WriteAsync("effect-b", """[{"id":2,"name":"Bram"}]"""),
            WriteAsync("effect-a", """[{"id":1,"name":"Ada"}]"""));

        Assert.Equal(2, outcomes.Count(outcome => outcome == EtlSqlWriteOutcome.Applied));
        Assert.Equal(1, outcomes.Count(outcome => outcome == EtlSqlWriteOutcome.Replay));
        Assert.Equal(["effect-a", "effect-b"], await fixture.QueryAsync($"SELECT effect_id FROM {_ledger} ORDER BY effect_id"));
    }

    [SkippableFact]
    public async Task A_missing_ledger_without_creation_fails_without_writing()
    {
        await CreateTableAsync(keyed: true);

        var failure = await Assert.ThrowsAsync<EtlEffectException>(() => WriteAsync("effect-1", """[{"id":1,"name":"Ada"}]""", createLedger: false));

        Assert.Equal("etl.sql-ledger-missing", failure.Code);
        Assert.Empty(await fixture.QueryAsync($"SELECT id FROM {_table}"));
    }

    private async Task CreateTableAsync(bool keyed)
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        await fixture.ExecuteAsync(CreateTableSql(_table, keyed));
    }

    private async Task<EtlSqlWriteOutcome> WriteAsync(string effect, string body, string? hash = null, bool createLedger = true,
        IEtlSqlEngine? engine = null) =>
        await new EtlSqlRouteWriter(engine ?? fixture.Engine, TimeProvider.System).WriteAsync(new EtlSqlRouteDelivery(
            fixture.ConnectionString, _ledger, createLedger, _table, ["id"], Columns, Encoding.UTF8.GetBytes(body),
            effect, "run", "batch-" + effect, hash ?? new string('a', 64)), default);

    // Engines may render 1.5 as 1.50 depending on the column scale; compare decimal values, not their text.
    private static string[] Normalize(string[] rows) => rows.Select(row => string.Join('|', row.Split('|').Select(value =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number) && value.Contains('.')
            ? number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : value))).ToArray();

    /// <summary>The engine with one row's parameters per command, to force a batch across several commands.</summary>
    private sealed class OneRowPerCommand(IEtlSqlEngine inner) : IEtlSqlEngine
    {
        public string Provider => inner.Provider;
        public int MaxParameters => Columns.Length;
        public string? SessionSetup => inner.SessionSetup;
        public DbConnection CreateConnection(string connectionString) => inner.CreateConnection(connectionString);
        public EtlSqlTarget DescribeTarget(string connectionString) => inner.DescribeTarget(connectionString);
        public string CreateLedger(string ledgerTable) => inner.CreateLedger(ledgerTable);
        public string InsertLedger(string ledgerTable) => inner.InsertLedger(ledgerTable);
        public string ReadLedgerHash(string ledgerTable) => inner.ReadLedgerHash(ledgerTable);
        public string CountUniqueKeys(string table, IReadOnlyList<string> keyColumns) => inner.CountUniqueKeys(table, keyColumns);
        public string Upsert(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, int row) => inner.Upsert(table, columns, keyColumns, row);
        public string Parameter(string column, int row) => inner.Parameter(column, row);
        public EtlSqlErrorKind Classify(Exception exception) => inner.Classify(exception);
    }
}

public sealed class EtlSqlPostgreSqlProviderTests(EtlSqlPostgreSqlFixture fixture)
    : EtlSqlProviderTests(fixture), IClassFixture<EtlSqlPostgreSqlFixture>
{
    protected override string CreateTableSql(string table, bool keyed) =>
        $"CREATE TABLE {table} (id bigint NOT NULL, name text NOT NULL, score numeric(18, 4) NULL{(keyed ? $", CONSTRAINT {table}_key UNIQUE (id)" : "")})";

    [Fact]
    public void Deadlocks_serialization_failures_and_lost_connections_are_transient_and_constraint_errors_are_not()
    {
        var engine = new EtlSqlPostgreSqlFixture().Engine;

        Assert.Equal(EtlSqlErrorKind.Transient, engine.Classify(new PostgresException("deadlock", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected)));
        Assert.Equal(EtlSqlErrorKind.Transient, engine.Classify(new PostgresException("serialization", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure)));
        Assert.Equal(EtlSqlErrorKind.Transient, engine.Classify(new PostgresException("connection", "FATAL", "FATAL", "08006")));
        Assert.Equal(EtlSqlErrorKind.Rejected, engine.Classify(new PostgresException("not null", "ERROR", "ERROR", PostgresErrorCodes.NotNullViolation)));
        Assert.Equal(EtlSqlErrorKind.MissingConstraint, engine.Classify(new PostgresException("no conflict target", "ERROR", "ERROR", PostgresErrorCodes.InvalidColumnReference)));
    }
}

public sealed class EtlSqlServerProviderTests(EtlSqlServerFixture fixture)
    : EtlSqlProviderTests(fixture), IClassFixture<EtlSqlServerFixture>
{
    protected override string CreateTableSql(string table, bool keyed) =>
        $"CREATE TABLE {table} (id bigint NOT NULL, name nvarchar(200) NOT NULL, score decimal(18, 4) NULL{(keyed ? $", CONSTRAINT {table}_key UNIQUE (id)" : "")})";
}

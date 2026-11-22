using System.Data.Common;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Output;
using Npgsql;

namespace Elsa.Etl.Sql.ProviderTests;

/// <summary>Spec 1197: the SQL delivery protocol on each real engine.</summary>
public abstract partial class EtlSqlProviderTests(EtlSqlProviderFixture fixture)
{
    private static readonly EtlColumn[] Columns =
        [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, true), new("score", EtlColumnType.Decimal, false)];

    private protected readonly string _table = "people_" + Guid.NewGuid().ToString("N")[..12];
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

    [SkippableFact]
    public async Task A_missing_table_is_a_rejection_not_a_missing_key()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);

        var failure = await Assert.ThrowsAsync<EtlEffectException>(() => WriteAsync("effect-1", """[{"id":1,"name":"Ada"}]"""));

        Assert.Equal("etl.sql-write-rejected", failure.Code);
        Assert.Empty(await fixture.QueryAsync($"SELECT effect_id FROM {_ledger}"));
    }

    [SkippableFact]
    public async Task A_view_as_target_is_a_rejection_on_every_engine()
    {
        await CreateTableAsync(keyed: true);
        var view = "view_" + Guid.NewGuid().ToString("N")[..12];
        await fixture.ExecuteAsync($"CREATE VIEW {view} AS SELECT id, name, score FROM {_table}");

        var failure = await Assert.ThrowsAsync<EtlEffectException>(() => new EtlSqlRouteWriter(fixture.Engine, TimeProvider.System)
            .WriteAsync(new EtlSqlRouteDelivery(fixture.ConnectionString, _ledger, true, view, ["id"], Columns,
                Encoding.UTF8.GetBytes("[{\"id\":1,\"name\":\"Ada\"}]"), "effect-1", "run", "batch-1", new string('a', 64)), default).AsTask());

        Assert.Equal("etl.sql-write-rejected", failure.Code);
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
        public System.Data.IsolationLevel SnapshotIsolation => inner.SnapshotIsolation;
        public string? SnapshotSetup => inner.SnapshotSetup;
        public string? SnapshotLock(string table) => inner.SnapshotLock(table);
        public bool IsLockRefusal(Exception exception) => inner.IsLockRefusal(exception);
        public bool ExtremeDatesAreInfinite => inner.ExtremeDatesAreInfinite;
        public string SelectSnapshot(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, string? watermarkColumn) =>
            inner.SelectSnapshot(table, columns, keyColumns, watermarkColumn);
    }
}

public sealed partial class EtlSqlPostgreSqlProviderTests(EtlSqlPostgreSqlFixture fixture)
    : EtlSqlProviderTests(fixture), IClassFixture<EtlSqlPostgreSqlFixture>
{
    private static readonly EtlColumn[] RowColumns =
        [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, true), new("score", EtlColumnType.Decimal, false)];

    protected override string CreateTableSql(string table, bool keyed) =>
        $"CREATE TABLE {table} (id bigint NOT NULL, name text NOT NULL, score numeric(18, 4) NULL{(keyed ? $", CONSTRAINT {table}_key UNIQUE (id)" : "")})";

    [SkippableFact]
    public async Task A_role_that_may_only_write_a_pre_created_ledger_delivers_without_running_ddl()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var (table, ledger, role) = ("people_" + suffix, "ledger_" + suffix, "etl_" + suffix);
        await fixture.ExecuteAsync(CreateTableSql(table, keyed: true));
        await fixture.ExecuteAsync(fixture.Engine.CreateLedger(ledger));
        // PostgreSQL 15+ grants PUBLIC no CREATE on schema public; this role may only read and write the two tables.
        await fixture.ExecuteAsync($"CREATE ROLE {role} LOGIN PASSWORD 'etl'; GRANT USAGE ON SCHEMA public TO {role}; " +
                                   $"GRANT SELECT, INSERT, UPDATE ON {table}, {ledger} TO {role}");
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Username = role, Password = "etl" }.ConnectionString;

        Assert.Equal(EtlSqlWriteOutcome.Applied, await WriteAsync(connection, ledger, table, "effect-1"));
        Assert.Equal(["1|Ada"], await fixture.QueryAsync($"SELECT id, name FROM {table}"));
    }

    [SkippableFact]
    public async Task The_key_check_follows_search_path_counts_only_key_columns_and_refuses_a_deferrable_constraint()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var (included, deferrable) = ("included_" + suffix, "deferrable_" + suffix);
        // The search_path is "$user", public: with a schema named after the user, unqualified names still resolve to public.
        await fixture.ExecuteAsync($"CREATE TABLE public.{included} (id bigint NOT NULL, name text NOT NULL, score numeric NULL)");
        await fixture.ExecuteAsync($"CREATE UNIQUE INDEX {included}_key ON public.{included} (id) INCLUDE (name)");
        await fixture.ExecuteAsync($"CREATE TABLE public.{deferrable} (id bigint NOT NULL, name text NOT NULL, score numeric NULL, " +
                                   $"CONSTRAINT {deferrable}_key UNIQUE (id) DEFERRABLE INITIALLY IMMEDIATE)");
        await fixture.ExecuteAsync("CREATE SCHEMA postgres");
        try
        {
            Assert.Equal(EtlSqlWriteOutcome.Applied, await WriteAsync(fixture.ConnectionString, "public.ledger_" + suffix, included, "effect-1"));
            var failure = await Assert.ThrowsAsync<EtlEffectException>(() =>
                WriteAsync(fixture.ConnectionString, "public.ledger_" + suffix, deferrable, "effect-2"));

            Assert.Equal("etl.sql-key-constraint-missing", failure.Code);
            Assert.Equal(["1|Ada"], await fixture.QueryAsync($"SELECT id, name FROM public.{included}"));
        }
        finally
        {
            // The schema would capture other tests' unqualified names; drop it so they keep resolving to public.
            await fixture.ExecuteAsync("DROP SCHEMA postgres CASCADE");
        }
    }

    private Task<EtlSqlWriteOutcome> WriteAsync(string connection, string ledger, string table, string effect) =>
        new EtlSqlRouteWriter(fixture.Engine, TimeProvider.System).WriteAsync(new EtlSqlRouteDelivery(connection, ledger, true, table,
            ["id"], RowColumns, Encoding.UTF8.GetBytes("[{\"id\":1,\"name\":\"Ada\"}]"), effect, "run", "batch-" + effect,
            new string('a', 64)), default).AsTask();

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

public sealed partial class EtlSqlServerProviderTests(EtlSqlServerFixture fixture)
    : EtlSqlProviderTests(fixture), IClassFixture<EtlSqlServerFixture>
{
    protected override string CreateTableSql(string table, bool keyed) =>
        $"CREATE TABLE {table} (id bigint NOT NULL, name nvarchar(200) NOT NULL, score decimal(18, 4) NULL{(keyed ? $", CONSTRAINT {table}_key UNIQUE (id)" : "")})";

    [SkippableFact]
    public async Task A_disabled_unique_index_is_not_a_key_and_the_write_fails_closed()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var table = "disabled_" + suffix;
        await fixture.ExecuteAsync($"CREATE TABLE {table} (id bigint NOT NULL, name nvarchar(200) NOT NULL, score decimal(18, 4) NULL)");
        await fixture.ExecuteAsync($"CREATE UNIQUE NONCLUSTERED INDEX {table}_key ON {table} (id); ALTER INDEX {table}_key ON {table} DISABLE");

        // SQL Server keeps a disabled index's definition but no longer enforces it, so MERGE could insert duplicate keys.
        var failure = await Assert.ThrowsAsync<EtlEffectException>(() => new EtlSqlRouteWriter(fixture.Engine, TimeProvider.System)
            .WriteAsync(new EtlSqlRouteDelivery(fixture.ConnectionString, "ledger_" + suffix, true, table, ["id"],
                [new("id", EtlColumnType.Int64, true), new("name", EtlColumnType.String, true), new("score", EtlColumnType.Decimal, false)],
                Encoding.UTF8.GetBytes("[{\"id\":1,\"name\":\"Ada\"}]"), "effect-1", "run", "batch-1", new string('a', 64)), default).AsTask());

        Assert.Equal("etl.sql-key-constraint-missing", failure.Code);
        Assert.Empty(await fixture.QueryAsync($"SELECT id FROM {table}"));
    }

    [Fact]
    public void An_exhausted_connection_pool_is_transient() =>
        Assert.Equal(EtlSqlErrorKind.Transient, new EtlSqlServerFixture().Engine.Classify(new InvalidOperationException(
            "Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool.")));
}

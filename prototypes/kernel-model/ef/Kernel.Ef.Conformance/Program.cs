using Kernel.Ef.Conformance;
using System.Data.Common;
using Kernel.EntityFramework;
using Kernel.Store.Conformance;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySql.Data.MySqlClient;
using Npgsql;

// The store conformance suite against the Entity Framework store. EfStore.cs and Engines.cs are used as they are.
//   dotnet ef/Kernel.Ef.Conformance/bin/Release/net10.0/Kernel.Ef.Conformance.dll sqlite semantic
//   dotnet ... postgres "Host=localhost;Port=<port>;Username=postgres;Password=kernel" [--fence-in-batch|--checked-fence] [--m5] semantic|stress|cost ...
//   dotnet ... sqlserver "Server=localhost,<port>;User Id=sa;Password=...;TrustServerCertificate=True" [--fence-in-batch|--checked-fence] [--rcsi-off] semantic ...
//   dotnet ... mysql "server=localhost;port=<port>;uid=root;pwd=kernel" [--fence-in-batch|--checked-fence] [--rc] semantic ...
// Without a fence flag the engine's default: the fence in the batch on every engine (SQL Server since 16 November, for the owned commit; with --ef-commit it keeps the checked fence, ENGINES.md E9).
// --ef-commit | --owned-commit: who writes the commit, EF's SaveChanges or the store's own SQL (COMMIT-SQL.md); no flag: the engine's default.
// --m5: the mutation that lets a definite error beat a fence that did not match (ENGINES.md E1, MONGO.md M5).
// --fence-ignores-count: the mutation that lets the owned fence pass whatever it matched (COMMIT-SQL.md); semantic-broken must fail with it.

string[] flags = ["--fence-in-batch", "--checked-fence", "--m5", "--rcsi-off", "--rc", "--ef-commit", "--owned-commit", "--fence-ignores-count"];
switch (args)
{
    case ["sqlite", "node-formats", .. var options]:
        await new EfProvider("sqlite", null, options).CheckNodeFormatsAsync();
        return 0;
    case [var engine and ("postgres" or "sqlserver" or "mysql"), var connection, "node-formats", .. var options]:
        await new EfProvider(engine, connection, options).CheckNodeFormatsAsync();
        return 0;
    case ["sqlite", "version-take", .. var options]:
        await new EfProvider("sqlite", null, options).CheckTakeVersionsAsync();
        return 0;
    case [var engine and ("postgres" or "sqlserver" or "mysql"), var connection, "version-take", .. var options]:
        await new EfProvider(engine, connection, options).CheckTakeVersionsAsync();
        return 0;
    case ["sqlite", "version-services", .. var options]:
        await new EfProvider("sqlite", null, options).CheckServiceVersionsAsync();
        return 0;
    case [var engine and ("postgres" or "sqlserver" or "mysql"), var connection, "version-services", .. var options]:
        await new EfProvider(engine, connection, options).CheckServiceVersionsAsync();
        return 0;
    case ["sqlite", "format-migration", .. var options]:
        await new EfProvider("sqlite", null, options).CheckFormatMigrationAsync();
        return 0;
    case [var engine and ("postgres" or "sqlserver" or "mysql"), var connection, "format-migration", .. var options]:
        await new EfProvider(engine, connection, options).CheckFormatMigrationAsync();
        return 0;
    case ["root-admission-writer"]:
        return await GeneratedRootChecks.WriteFromChildAsync();
    case ["postgres", var connection, "root-set", .. var options]:
        await new EfProvider("postgres", connection, options).CheckRootSetAsync();
        return 0;
    case ["postgres", var connection, "root-journal"]:
        await new EfProvider("postgres", connection, []).CheckRootJournalAsync();
        return 0;
    case ["postgres", var connection, "root-direct-send", .. var options]:
        await new EfProvider("postgres", connection, options).CheckDirectSendAsync();
        return 0;
    case ["postgres", var connection, "root-turn", .. var options]:
        await new EfProvider("postgres", connection, options).CheckRootTurnAsync();
        return 0;
    case ["postgres", var connection, "root-checkpoint", .. var options]:
        await new EfProvider("postgres", connection, options).CheckFirstRootAsync();
        return 0;
    case ["postgres", var connection, "root-admission"]:
        await new EfProvider("postgres", connection, []).CheckRootAdmissionAsync();
        return 0;
    case ["sqlite", .. var rest]:
        return await Cli.RunAsync(new EfProvider("sqlite", null, rest), rest.Where(a => !flags.Contains(a)).ToArray());
    case [var engine and ("postgres" or "sqlserver" or "mysql"), var connection, .. var rest]:
        return await Cli.RunAsync(new EfProvider(engine, connection, rest), rest.Where(a => !flags.Contains(a)).ToArray());
    default:
        Console.Error.WriteLine("sqlite <suite command> | postgres|sqlserver|mysql <connection> [--fence-in-batch|--checked-fence] [--ef-commit|--owned-commit] [--m5] [--fence-ignores-count] [--rcsi-off] [--rc] <suite command>");
        return 2;
}

/// <summary>The suite's plug for <see cref="EfStore"/>: SQLite in a file; PostgreSQL, SQL Server or MySQL in a database of its own per store.</summary>
sealed class EfProvider(string engine, string? server, string[] flags) : IStoreProvider
{
    // The fence flag, else the engine's default (EfStore.FenceInBatch).
    private bool FenceInBatch => flags.Contains("--fence-in-batch") || !flags.Contains("--checked-fence") && (Dialect()?.FenceInBatchByDefault(Commit != CommitSql.EntityFramework) ?? true);

    private CommitSql? Commit => flags.Contains("--owned-commit") ? CommitSql.Owned : flags.Contains("--ef-commit") ? CommitSql.EntityFramework : null;

    public string Name => $"entity framework, {engine}, {(FenceInBatch ? "fence in batch" : "checked fence")}, {(Store(() => null!, FenceInBatch).OwnedCommit ? "owned SQL" : "EF SaveChanges")}"
        + (flags.Contains("--m5") ? ", M5 MUTATION" : "") + (flags.Contains("--fence-ignores-count") ? ", FENCE-COUNT MUTATION" : "") + (engine == "sqlserver" ? flags.Contains("--rcsi-off") ? ", locking" : ", RCSI" : "")
        + (engine == "mysql" ? flags.Contains("--rc") ? ", READ COMMITTED" : ", REPEATABLE READ" : "");

    private bool CaseEquivalentTopic => engine is "mysql" or "sqlserver";
    private bool Pg => engine == "postgres";
    private string Engine => engine;

    internal async Task CheckRootSetAsync()
    {
        await using var provided = (Provided)await CreateAsync(2, new());
        await RootSetChecks.Run(provided.CreateContext, (EfStore)provided.Store);
    }

    internal async Task CheckRootJournalAsync()
    {
        await using var provided = (Provided)await CreateAsync(2, new());
        await RootJournalChecks.Run(provided.CreateContext, (EfStore)provided.Store, (EfStore)provided.NewClient());
    }

    internal async Task CheckDirectSendAsync()
    {
        await using var provided = (Provided)await CreateAsync(2, new());
        await DirectSendChecks.Run(provided.CreateContext, (EfStore)provided.Store);
    }

    internal async Task CheckRootTurnAsync()
    {
        await using var provided = (Provided)await CreateAsync(2, new());
        await RootTurnChecks.Run(provided.CreateContext, (EfStore)provided.Store, (EfStore)provided.NewClient());
    }

    internal async Task CheckFirstRootAsync()
    {
        await using var provided = (Provided)await CreateAsync(2, new());
        await FirstRootChecks.Run(provided.CreateContext, (EfStore)provided.Store, (EfStore)provided.NewClient());
    }

    internal async Task CheckRootAdmissionAsync()
    {
        await using var provided = (Provided)await CreateAsync(2, new());
        await GeneratedRootChecks.Run(provided.CreateContext, provided.Store, provided.NewClient());
    }

    public Func<KernelDb> Factory(string connection, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<KernelDb>();
        _ = engine switch
        {
            "postgres" => builder.UseNpgsql(connection),
            "sqlserver" => builder.UseSqlServer(connection, o => o.MaxBatchSize(1000)),     // ENGINES.md E7
            "mysql" => builder.UseMySQL(connection, o => o.MaxBatchSize(1000)),
            _ => builder.UseSqlite(connection),
        };
        var options = builder.AddInterceptors(interceptors).Options;
        return () => new KernelDb(options);
    }

    private EfStore Store(Func<KernelDb> create, bool fenceInBatch, bool unfenced = false) => new(create, engine != "sqlite", fenceInBatch)
    {
        Unfenced = unfenced,
        DefiniteBeatsFence = flags.Contains("--m5"),
        FenceIgnoresCount = flags.Contains("--fence-ignores-count"),
        CommitSql = Commit,
        Engine = Dialect(),
    };

    private Engine? Dialect() => engine switch
    {
        "sqlserver" => new SqlServerEngine { ReadCommittedSnapshot = !flags.Contains("--rcsi-off") },
        "mysql" => new MySqlEngine { Isolation = flags.Contains("--rc") ? System.Data.IsolationLevel.ReadCommitted : null },
        _ => null,
    };

    internal async Task CheckNodeFormatsAsync()
    {
        await using var fixture = (Provided)await CreateAsync(2, new StoreSetup());
        await NodeAdvertisementChecks.Run(fixture.CreateContext, fixture.Store);
    }

    internal async Task CheckTakeVersionsAsync()
    {
        await using var fixture = (Provided)await CreateAsync(2, new StoreSetup());
        await VersionTakeChecks.Run(fixture.CreateContext, fixture.Store, fixture.NewClient());
    }

    internal async Task CheckServiceVersionsAsync()
    {
        await using var fixture = (Provided)await CreateAsync(2, new StoreSetup());
        await VersionServiceChecks.Run(fixture.CreateContext, fixture.Store);
    }

    internal async Task CheckFormatMigrationAsync()
    {
        await using (var fixture = (Provided)await CreateAsync(2, new StoreSetup()))
            await FormatMigrationChecks.Run(fixture.CreateContext, (EfStore)fixture.Store, (EfStore)fixture.NewClient());
        await using var race = (Provided)await CreateAsync(2, new StoreSetup());
        await FormatMigrationRaceChecks.Run(race.CreateContext,
            interceptor => Store(race.CreateAdmin(interceptor), FenceInBatch), (EfStore)race.Store);
    }

    public async Task<ProvidedStore> CreateAsync(int partitions, StoreSetup setup)
    {
        var counter = setup.Counting ? new Counting(Pg) : null;
        IInterceptor[] interceptors = counter is null ? [] : [counter, counter.Transactions];
        string worker, outside, admin;
        string? file = null;
        var database = "kconf_" + Guid.NewGuid().ToString("N")[..12];
        switch (engine)
        {
            case "postgres":
                worker = $"{server};Database={database};Application Name=kernel-worker;Maximum Pool Size=40";
                outside = $"{server};Database={database};Application Name=kernel-outside";
                admin = $"{server};Database={database};Application Name=kernel-admin";
                break;
            case "sqlserver":
                worker = $"{server};Database={database};Application Name=kernel-worker;Max Pool Size=40";
                outside = $"{server};Database={database};Application Name=kernel-outside";
                admin = $"{server};Database={database};Application Name=kernel-admin";
                break;
            case "mysql":
                worker = $"{server};database={database};maximumpoolsize=40";
                outside = $"{server};database={database}";
                admin = $"{server};database={database};pooling=false";
                break;
            default:
                file = Path.Combine(Path.GetTempPath(), $"kernel-conformance-{Guid.NewGuid():N}.db");
                worker = outside = admin = $"Data Source={file};Pooling=False";
                break;
        }
        await Store(Factory(admin), false).CreateAsync(partitions);
        var store = Store(Factory(worker, interceptors), FenceInBatch, setup.BrokenFence);
        return new Provided(this, store, Store(Factory(outside), FenceInBatch), admin, file, counter);
    }

    private sealed class Provided(EfProvider provider, EfStore store, EfStore outside, string admin, string? file, Counting? counter) : ProvidedStore
    {
        internal KernelDb CreateContext() => provider.Factory(admin)();
        public override IKernelStore Store => store;
        public override bool ProbeUnpairedTopicUnits => provider.Engine == "sqlserver";
        public override bool CaseEquivalentTopic => provider.CaseEquivalentTopic;
        public override bool LargeEventKey => provider.Pg;
        public override string? LargestInboxId => provider.Engine switch
        {
            "sqlserver" => "0/a!" + new string('界', 446),
            "mysql" => "0/a!" + string.Concat(Enumerable.Repeat("🌐", 251)),
            _ => null
        };
        public override string MissingConsumeOutcome => "not landed";
        public override IKernelStore NewClient() => outside;
        public override ICostCounter? Counter => counter;
        public override Func<int, Task>? Reopen => partitions => outside.CreateAsync(partitions);
        internal Func<KernelDb> CreateAdmin(params IInterceptor[] interceptors) => provider.Factory(admin, interceptors);

        // PostgreSQL refuses a NUL in text (22021), every time. SQLite, SQL Server and MySQL store any string the
        // instance row can hold: no refused row (the case is skipped).
        public override InstanceRow? Refused(InstanceRow row) => provider.Pg ? row with { State = "\u0000" + row.State } : null;

        private DbConnection Connect() => provider.Engine switch
        {
            "postgres" => new NpgsqlConnection(admin),
            "sqlserver" => new SqlConnection(admin),
            "mysql" => new MySqlConnection(admin),
            _ => new SqliteConnection(admin),
        };

        /// <summary>K1b: a partition row whose owner has no node record, for the missing-record case.</summary>
        public override Func<string, Task>? DeleteNodeRecord => async node =>
        {
            await using var connection = Connect();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = provider.Engine == "mysql" ? "DELETE FROM `Nodes` WHERE `Id` = @node" : "DELETE FROM \"Nodes\" WHERE \"Id\" = @node";
            var parameter = command.CreateParameter();
            (parameter.ParameterName, parameter.Value) = ("@node", node);
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync();
        };

        public override async Task<IAsyncDisposable?> HoldPartitionAsync(int partition)
        {
            var connection = Connect();
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = provider.Engine switch
            {
                "postgres" => $"""SELECT 1 FROM "Partitions" WHERE "Id" = {partition} FOR UPDATE""",
                "sqlserver" => $"""SELECT 1 FROM "Partitions" WITH (UPDLOCK, ROWLOCK) WHERE "Id" = {partition}""",
                "mysql" => $"""SELECT 1 FROM `Partitions` WHERE `Id` = {partition} FOR UPDATE""",
                _ => $"""UPDATE "Partitions" SET "Owner" = "Owner" WHERE "Id" = {partition}""",
            };
            await command.ExecuteNonQueryAsync();
            return new Hold(connection, transaction);
        }

        public override async Task<int> KillAsync(Random random)
        {
            if (provider.Engine != "postgres") return 0;      // the semantic cases do not kill; the engines' stress is EngineRun.cs
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""SELECT pg_terminate_backend(pid) FROM (SELECT pid FROM pg_stat_activity WHERE application_name = 'kernel-worker' AND datname = current_database() AND state <> 'idle' ORDER BY random() LIMIT 1) busy""", connection);
            var killed = 0;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) killed++;
            return killed;
        }

        public override async ValueTask DisposeAsync()
        {
            switch (provider.Engine)
            {
                case "sqlite":
                    SqliteConnection.ClearAllPools();
                    try { File.Delete(file!); } catch (IOException) { }
                    return;
                case "postgres": NpgsqlConnection.ClearAllPools(); break;
                case "sqlserver": SqlConnection.ClearAllPools(); break;
                case "mysql": MySqlConnection.ClearAllPools(); break;
            }
            await using var db = provider.Factory(admin)();
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class Hold(DbConnection connection, DbTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
    }
}

/// <summary>
/// What Entity Framework sends, as FINDINGS section 5 counts it. PostgreSQL: a command or a COMMIT is a round trip;
/// BEGIN rides with the first command. SQLite: BEGIN, each command and COMMIT are each one call into the engine.
/// </summary>
sealed class Counting(bool pg) : DbCommandInterceptor, ICostCounter
{
    public sealed class TransactionCounting : DbTransactionInterceptor
    {
        public int Begun, Ended;
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default) { Interlocked.Increment(ref Begun); return new(result); }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { Interlocked.Increment(ref Ended); return Task.CompletedTask; }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { Interlocked.Increment(ref Ended); return Task.CompletedTask; }
    }

    public readonly TransactionCounting Transactions = new();
    private int _commands;

    public void Reset() => (_commands, Transactions.Begun, Transactions.Ended) = (0, 0, 0);

    public CostReading Read() => new(_commands + Transactions.Ended + (pg ? 0 : Transactions.Begun), Transactions.Ended,
        $"commands {_commands}, begin {Transactions.Begun}, commit or rollback {Transactions.Ended}");

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Interlocked.Increment(ref _commands); return new(result); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Interlocked.Increment(ref _commands); return new(result); }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) { Interlocked.Increment(ref _commands); return new(result); }
}

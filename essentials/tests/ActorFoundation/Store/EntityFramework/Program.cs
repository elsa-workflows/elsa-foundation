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
//   dotnet ../../essentials/tests/ActorFoundation/Store/EntityFramework/bin/Release/net10.0/Kernel.Ef.Conformance.dll sqlite semantic
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
    case ["postgres-rehearsal", .. var role]:
        return await FormatRehearsalProcess.RunAsync(role, (store, token) => store.UpgradeJobGenerationFormatAsync(token));
    case ["postgres-profile-upgrade"]:
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new ArgumentException("Supply the dedicated profile fixture database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var store = new EfStore(() => new KernelDb(options), postgres: true);
        Console.WriteLine(await store.UpgradeJobGenerationFormatAsync() ? "advanced" : "already current");
        return 0;
    }
    case ["sqlite", "checkpoint-byte-count", .. var options]:
        await new EfProvider("sqlite", null, options).CheckCheckpointBytesAsync();
        return 0;
    case [var engine and ("postgres" or "sqlserver" or "mysql"), var connection, "checkpoint-byte-count", .. var options]:
        await new EfProvider(engine, connection, options).CheckCheckpointBytesAsync();
        return 0;
    case ["sqlite", "release-set", .. var options]:
        await new EfProvider("sqlite", null, options).CheckFullReleaseSetAsync();
        return 0;
    case ["sqlite", "job-generation-format", .. var options]:
        await new EfProvider("sqlite", null, options).CheckJobGenerationFormatAsync();
        return 0;
    case [var engine and ("postgres" or "sqlserver" or "mysql"), var connection, "job-generation-format", .. var options]:
        await new EfProvider(engine, connection, options).CheckJobGenerationFormatAsync();
        return 0;
    case ["mysql", var connection, "lease-cancellation", .. var options]:
        await new EfProvider("mysql", connection, options).CheckLeaseCancellationAsync();
        return 0;
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
    case ["postgres", var connection, "root-checkpoint-limit", .. var options]:
        await new EfProvider("postgres", connection, options).CheckRootCheckpointLimitAsync();
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
internal sealed class EfProvider(string engine, string? server, string[] flags) : IStoreProvider
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

    internal async Task CheckFullReleaseSetAsync()
    {
        const int count = Defaults.SqlitePartitions;
        await using var fixture = (Provided)await CreateAsync(count, new StoreSetup());
        var store = fixture.Store;
        var host = DurableHost.ForNode("release-check", store, new WorkflowInstance(Stress.Models()), count);
        var runtime = new PartitionRuntime("release-check", (EfStore)store, count, TimeSpan.FromSeconds(3));
        await runtime.StartAsync(host);
        await runtime.HeartbeatAsync();
        await runtime.BalanceAsync();
        var scopes = Enumerable.Range(0, count).ToArray();
        if (!scopes.All(runtime.Owns)) throw new InvalidOperationException("The shutdown must hold the entire SQLite set.");
        var fences = host.NodeWorker!.Fences.ToArray();
        await runtime.StopAsync();
        await using var db = fixture.CreateContext();
        var stopped = await db.Partitions.AsNoTracking().OrderBy(partition => partition.Id).ToArrayAsync();
        if (stopped.Length != count || stopped.Any(partition => partition.Owner is not null) ||
            !stopped.Select(partition => partition.Epoch).SequenceEqual(fences.Select(fence => fence.Epoch)))
            throw new InvalidOperationException("The SQLite shutdown must release every owner at its original epoch.");
        var taken = await store.TakeAsync(scopes, "successor", Defaults.Lease);
        if (taken.Taken.Count != count) throw new InvalidOperationException("The stale release control must hold the entire SQLite set.");
        var stale = taken.Taken[0];
        var other = taken.Taken[1];
        var current = await store.RetakeAsync(stale.Partition, "successor", stale.Epoch, Defaults.Lease)
            ?? throw new InvalidOperationException("The new epoch is required for the stale release control.");
        var otherCurrent = await store.RetakeAsync(other.Partition, "successor", other.Epoch, Defaults.Lease)
            ?? throw new InvalidOperationException("A matching epoch in another tuple is required for the stale release control.");
        if (current.Epoch != otherCurrent.Epoch) throw new InvalidOperationException("The two newer epochs must match.");
        var releasing = taken.Taken.Select(fence => fence.Partition == other.Partition ? otherCurrent.For(other.Partition) : fence).ToArray();
        await store.ReleaseAsync(releasing);
        var remaining = await db.Partitions.AsNoTracking().Where(partition => partition.Owner != null).SingleAsync();
        if (remaining.Id != stale.Partition || remaining.Owner != "successor" || remaining.Epoch != current.Epoch)
            throw new InvalidOperationException("A stale set release cleared a later owner epoch.");
        Console.WriteLine($"ok SQLite released {count} scopes together and retained the newer epoch in a stale set");
    }

    internal async Task CheckCheckpointBytesAsync()
    {
        await using var fixture = (Provided)await CreateAsync(1, new());
        Console.WriteLine(Name);
        await CheckpointByteChecks.Run(engine, fixture.CreateContext, fixture.Store, fixture.NewClient());
    }

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
        await using var manual = (Provided)await CreateAsync(2, new());
        await RootPreparationDeadlineChecks.RunManual(manual.CreateContext,
            delay => Store(manual.CreateAdmin(delay), FenceInBatch), (EfStore)manual.Store);
        await CheckDeadline(false);
        if (((EfStore)provided.Store).OwnedSetsAvailable) await CheckDeadline(true);

        async Task CheckDeadline(bool grouped)
        {
            await using var deadline = (Provided)await CreateAsync(2, new());
            await RootPreparationDeadlineChecks.Run(deadline.CreateContext,
                delay => Store(deadline.CreateAdmin(delay), FenceInBatch), (EfStore)deadline.Store, grouped);
        }
    }

    internal async Task CheckRootTurnAsync()
    {
        await using var provided = (Provided)await CreateAsync(2, new());
        await RootTurnChecks.Run(provided.CreateContext, (EfStore)provided.Store, (EfStore)provided.NewClient());
    }

    internal async Task CheckRootCheckpointLimitAsync()
    {
        await using var fixture = (Provided)await CreateAsync(1, new());
        Console.WriteLine(Name);
        if (flags.Contains("--job-registry"))
            await RootCheckpointLimitChecks.RunJobs(fixture.CreateContext, (EfStore)fixture.Store, (EfStore)fixture.NewClient());
        else
            await RootCheckpointLimitChecks.Run(fixture.CreateContext, (EfStore)fixture.Store, (EfStore)fixture.NewClient(), flags.Contains("--mixed-text"));
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
        return engine == "postgres" && flags.Contains("--job-registry")
            ? () => KernelDb.ForPostgresRootJobs(options) : () => new KernelDb(options);
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

    internal async Task CheckLeaseCancellationAsync()
    {
        await using var fixture = (Provided)await CreateAsync(1, new StoreSetup());
        await LeaseCancellationChecks.Run(fixture.CreateContext, interceptor => Store(fixture.CreateAdmin(interceptor), FenceInBatch), (EfStore)fixture.Store);
    }

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

    internal async Task CheckJobGenerationFormatAsync()
    {
        await using var fixture = (Provided)await CreateAsync(2, new StoreSetup());
        await JobGenerationFormatChecks.Run(fixture.CreateAdmin(), fixture, () => Store(fixture.CreateAdmin(), FenceInBatch));
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

        public override Task<Committed> StartPartitionContenderAsync(CommitBatch batch)
        {
            if (file is null) return base.StartPartitionContenderAsync(batch);

            // SQLite can block synchronously and has no independent lock-wait view. Bound the busy timeout
            // and keep the holder open until the contender reports the actual transient failure.
            var connection = new SqliteConnectionStringBuilder(admin) { DefaultTimeout = 1 }.ToString();
            var contender = provider.Store(provider.Factory(connection), provider.FenceInBatch, store.Unfenced);
            return Task.Run(async () => await contender.CommitAsync(batch));
        }

        public override async Task<bool> ObservePartitionContentionAsync(Task attempt)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stop = timeout.Token;
            if (file is not null)
            {
                await Task.WhenAny(attempt).WaitAsync(stop);
                return false;
            }

            await using var connection = Connect();
            await connection.OpenAsync(stop);
            await using var command = connection.CreateCommand();
            command.CommandText = provider.Engine switch
            {
                "postgres" => "SELECT COUNT(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'",
                "sqlserver" => "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id <> 0 AND database_id = DB_ID()",
                "mysql" => """
                    SELECT COUNT(*) FROM performance_schema.data_lock_waits w
                    JOIN performance_schema.data_locks l ON l.ENGINE = w.ENGINE AND l.ENGINE_LOCK_ID = w.REQUESTING_ENGINE_LOCK_ID
                    WHERE l.OBJECT_SCHEMA = DATABASE()
                    """,
                _ => throw new InvalidOperationException("The conformance provider requires a lock-wait observation."),
            };
            // Every conformance case has its own fresh database. The contender is the only running writer.
            while (!attempt.IsCompleted)
            {
                if (Convert.ToInt64(await command.ExecuteScalarAsync(stop)) > 0) return true;
                await Task.Delay(20, stop);
            }
            return false;
        }

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
internal sealed class Counting(bool pg) : DbCommandInterceptor, ICostCounter
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

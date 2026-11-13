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
// Without a fence flag the engine's default: the fence in the batch, but the checked fence on SQL Server (ENGINES.md E9).
// --m5: the mutation that lets a definite error beat a fence that did not match (ENGINES.md E1, MONGO.md M5).

string[] flags = ["--fence-in-batch", "--checked-fence", "--m5", "--rcsi-off", "--rc"];
switch (args)
{
    case ["sqlite", .. var rest]:
        return await Cli.RunAsync(new EfProvider("sqlite", null, rest), rest.Where(a => !flags.Contains(a)).ToArray());
    case [var engine and ("postgres" or "sqlserver" or "mysql"), var connection, .. var rest]:
        return await Cli.RunAsync(new EfProvider(engine, connection, rest), rest.Where(a => !flags.Contains(a)).ToArray());
    default:
        Console.Error.WriteLine("sqlite <suite command> | postgres|sqlserver|mysql <connection> [--fence-in-batch|--checked-fence] [--m5] [--rcsi-off] [--rc] <suite command>");
        return 2;
}

/// <summary>The suite's plug for <see cref="EfStore"/>: SQLite in a file; PostgreSQL, SQL Server or MySQL in a database of its own per store.</summary>
sealed class EfProvider(string engine, string? server, string[] flags) : IStoreProvider
{
    // The fence flag, else the engine's default (EfStore.FenceInBatch).
    private bool FenceInBatch => flags.Contains("--fence-in-batch") || !flags.Contains("--checked-fence") && (Dialect()?.FenceInBatchByDefault ?? true);

    public string Name => $"entity framework, {engine}, {(FenceInBatch ? "fence in batch" : "checked fence")}"
        + (flags.Contains("--m5") ? ", M5 MUTATION" : "") + (engine == "sqlserver" ? flags.Contains("--rcsi-off") ? ", locking" : ", RCSI" : "")
        + (engine == "mysql" ? flags.Contains("--rc") ? ", READ COMMITTED" : ", REPEATABLE READ" : "");

    private bool Pg => engine == "postgres";
    private string Engine => engine;

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
        Engine = Dialect(),
    };

    private Engine? Dialect() => engine switch
    {
        "sqlserver" => new SqlServerEngine { ReadCommittedSnapshot = !flags.Contains("--rcsi-off") },
        "mysql" => new MySqlEngine { Isolation = flags.Contains("--rc") ? System.Data.IsolationLevel.ReadCommitted : null },
        _ => null,
    };

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
        public override IKernelStore Store => store;
        public override IKernelStore NewClient() => outside;
        public override ICostCounter? Counter => counter;

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

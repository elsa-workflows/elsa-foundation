using System.Data.Common;
using Kernel;
using Kernel.EntityFramework;
using Kernel.Store.Conformance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

// The store conformance suite against the Entity Framework store. EfStore.cs is used as it is.
//   dotnet ef/Kernel.Ef.Conformance/bin/Release/net10.0/Kernel.Ef.Conformance.dll sqlite semantic
//   dotnet ... postgres "Host=localhost;Port=<port>;Username=postgres;Password=kernel" [--fence-in-batch] semantic|stress|cost ...

switch (args)
{
    case ["sqlite", .. var rest]:
        return await Cli.RunAsync(new EfProvider(null, rest.Contains("--fence-in-batch")), rest.Where(a => a != "--fence-in-batch").ToArray());
    case ["postgres", var connection, .. var rest]:
        return await Cli.RunAsync(new EfProvider(connection, rest.Contains("--fence-in-batch")), rest.Where(a => a != "--fence-in-batch").ToArray());
    default:
        Console.Error.WriteLine("sqlite <suite command> | postgres <connection> [--fence-in-batch] <suite command>");
        return 2;
}

/// <summary>The suite's plug for <see cref="EfStore"/>: SQLite in a file, or PostgreSQL in a database of its own per store.</summary>
sealed class EfProvider(string? postgres, bool fenceInBatch) : IStoreProvider
{
    public string Name => $"entity framework, {(postgres is null ? "sqlite" : "postgresql")}, {(fenceInBatch ? "fence in batch" : "checked fence")}";

    public static Func<KernelDb> Factory(bool pg, string connection, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<KernelDb>();
        if (pg) builder.UseNpgsql(connection);
        else builder.UseSqlite(connection);
        var options = builder.AddInterceptors(interceptors).Options;
        return () => new KernelDb(options);
    }

    public async Task<ProvidedStore> CreateAsync(int partitions, StoreSetup setup)
    {
        var counter = setup.Counting ? new Counting(postgres is not null) : null;
        IInterceptor[] interceptors = counter is null ? [] : [counter, counter.Transactions];
        string worker, outside, admin;
        string? file = null;
        if (postgres is not null)
        {
            var database = "kconf_" + Guid.NewGuid().ToString("N")[..12];
            worker = $"{postgres};Database={database};Application Name=kernel-worker;Maximum Pool Size=40";
            outside = $"{postgres};Database={database};Application Name=kernel-outside";
            admin = $"{postgres};Database={database};Application Name=kernel-admin";
        }
        else
        {
            file = Path.Combine(Path.GetTempPath(), $"kernel-conformance-{Guid.NewGuid():N}.db");
            worker = outside = admin = $"Data Source={file};Pooling=False";
        }
        await new EfStore(Factory(postgres is not null, admin), postgres is not null).CreateAsync(partitions);
        var store = new EfStore(Factory(postgres is not null, worker, interceptors), postgres is not null, fenceInBatch) { Unfenced = setup.BrokenFence };
        return new Provided(postgres is not null, store, new EfStore(Factory(postgres is not null, outside), postgres is not null, fenceInBatch), admin, file, counter);
    }

    private sealed class Provided(bool pg, EfStore store, EfStore outside, string admin, string? file, Counting? counter) : ProvidedStore
    {
        public override IKernelStore Store => store;
        public override IKernelStore NewClient() => outside;
        public override ICostCounter? Counter => counter;

        // PostgreSQL refuses a NUL in text (22021), every time. SQLite stores anything.
        public override InstanceRow? Refused(InstanceRow row) => pg ? row with { State = "\u0000" + row.State } : null;

        public override async Task<IAsyncDisposable?> HoldPartitionAsync(int partition)
        {
            DbConnection connection = pg ? new NpgsqlConnection(admin) : new SqliteConnection(admin);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = pg ? $"""SELECT 1 FROM "Partitions" WHERE "Id" = {partition} FOR UPDATE""" : $"""UPDATE "Partitions" SET "Owner" = "Owner" WHERE "Id" = {partition}""";
            await command.ExecuteNonQueryAsync();
            return new Hold(connection, transaction);
        }

        public override async Task<int> KillAsync(Random random)
        {
            if (!pg) return 0;
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
            if (pg)
            {
                NpgsqlConnection.ClearAllPools();
                await using var db = Factory(true, admin)();
                await db.Database.EnsureDeletedAsync();
            }
            else
            {
                SqliteConnection.ClearAllPools();
                try { File.Delete(file!); } catch (IOException) { }
            }
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

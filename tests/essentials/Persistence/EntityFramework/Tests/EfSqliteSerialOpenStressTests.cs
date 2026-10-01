using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// #2209 under load. Contexts bound through <see cref="EfRelationalProviderBinding"/> open connections to one SQLite file
/// all at once, wave after wave, and no two of them may share a pooled connection or fail.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in, and out of CI by default. It takes minutes, and the race lies between two statements inside Microsoft.Data.Sqlite,
/// so even an ungated run fails only after hundreds of waves. Run it alone:
/// <c>ELSA_SQLITE_POOL_STRESS=1 dotnet test tests/essentials/Persistence/EntityFramework/Tests -c Release --filter "FullyQualifiedName~EfSqliteSerialOpenStressTests"</c>.
/// <c>ELSA_SQLITE_POOL_STRESS_WAVES</c> (default 2,000) sets how many waves run, and <c>ELSA_SQLITE_POOL_STRESS_WIDTH</c>
/// (default 16) how many connections each wave opens. With plain <c>UseSqlite</c> instead of the binding, this load on
/// Microsoft.Data.Sqlite 10.0.10 failed from wave 301 of 3,000 and from wave 326 of 6,000 on an 8-core machine. Most errors
/// were "unable to delete/modify user-function due to active statements", its collation variant, and "database is locked".
/// </para>
/// <para>
/// Each wave starts from an empty pool, cleared while every connection of the previous wave is closed, so every open is a
/// checkout while the pool grows, which is where the race is. Each connection then reads rows while the others open, so a
/// shared handle shows up as SQLite's error, and the handles of a wave's connections are also compared while all of them
/// are open. Half the workers open and read synchronously, half asynchronously, so both paths of the gate run. The class
/// runs in a collection of its own, outside the parallel ones: another test's teardown clears every pool in the process,
/// which the gate does not order (see <see cref="EfSqliteSerialOpenInterceptor"/>).
/// </para>
/// <para>
/// The gate is a workaround until Elsa pins Microsoft.Data.Sqlite 10.0.13 (#2220). This test stays useful after it goes.
/// </para>
/// </remarks>
[Collection(nameof(EfSqliteSerialOpenStressTests))]
public sealed class EfSqliteSerialOpenStressTests : IAsyncDisposable
{
    private const string Variable = "ELSA_SQLITE_POOL_STRESS";
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(30);

    private readonly TemporarySqliteDatabase _database = new("serial-open-stress");

    [SkippableFact]
    public void Concurrent_opens_of_one_file_never_share_a_pooled_connection()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable(Variable) is "1" or "true", $"Set {Variable}=1 to run the SQLite pool stress test (see its class remarks).");
        var waves = Setting("WAVES", 2000);
        var width = Setting("WIDTH", 16);
        var options = Options();
        Seed(options);

        var handles = new IntPtr[width];
        var failures = new ConcurrentQueue<string>();
        var stopping = false;
        using var abandoned = new CancellationTokenSource(Patience);
        using var barrier = new Barrier(width + 1);
        using var pool = new SqliteConnection(_database.ConnectionString);

        void Work(int index)
        {
            try
            {
                while (true)
                {
                    barrier.SignalAndWait(abandoned.Token);
                    if (Volatile.Read(ref stopping))
                        return;
                    StressContext? context = null;
                    try
                    {
                        context = new StressContext(options);
                        OpenAndRead(context, synchronous: index % 2 == 0);
                        handles[index] = ((SqliteConnection)context.Database.GetDbConnection()).Handle!.DangerousGetHandle();
                    }
                    // Any exception counts: the race surfaces as several types (SqliteException, InvalidOperationException, ObjectDisposedException…).
                    catch (Exception exception)
                    {
                        failures.Enqueue($"{exception.GetType().Name}: {exception.Message}");
                    }

                    // All of the wave's connections are open here, and the main thread compares their handles.
                    barrier.SignalAndWait(abandoned.Token);
                    barrier.SignalAndWait(abandoned.Token);
                    try
                    {
                        context?.Dispose();
                    }
                    // Same as above: any exception counts.
                    catch (Exception exception)
                    {
                        failures.Enqueue($"{exception.GetType().Name} on close: {exception.Message}");
                    }

                    handles[index] = IntPtr.Zero;
                    barrier.SignalAndWait(abandoned.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // The wave was abandoned; whatever abandoned it is recorded already.
            }
            catch (Exception exception)
            {
                // A worker that dies leaves the barrier one short; the others are let go rather than left waiting.
                failures.Enqueue($"Worker {index} stopped: {exception}");
                abandoned.Cancel();
            }
        }

        // Dedicated threads, so the barrier never blocks the thread pool the asynchronous half runs on.
        var workers = Enumerable.Range(0, width)
            .Select(index => Task.Factory.StartNew(() => Work(index), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
            .ToArray();
        var completed = 0;
        try
        {
            for (var wave = 0; ; wave++)
            {
                var done = wave == waves || !failures.IsEmpty;
                if (!done)
                    SqliteConnection.ClearPool(pool);
                Volatile.Write(ref stopping, done);
                barrier.SignalAndWait(abandoned.Token);
                if (done)
                    break;

                barrier.SignalAndWait(abandoned.Token);
                var shared = handles.Where(handle => handle != IntPtr.Zero).GroupBy(handle => handle).Count(group => group.Count() > 1);
                if (shared > 0)
                    failures.Enqueue($"Wave {wave}: {shared} pooled connection(s) lent to more than one open connection at once.");
                barrier.SignalAndWait(abandoned.Token);
                barrier.SignalAndWait(abandoned.Token);
                completed = wave + 1;
            }
        }
        catch (OperationCanceledException)
        {
            if (failures.IsEmpty)
                failures.Enqueue($"The run did not finish within {Patience}.");
        }
        finally
        {
            Task.WaitAll(workers, Patience);
        }

        Assert.True(failures.IsEmpty, $"After {completed} of {waves} waves of {width} opens: {string.Join(Environment.NewLine, failures.Take(20))}");
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private DbContextOptions<StressContext> Options()
    {
        var builder = new DbContextOptionsBuilder<StressContext>();
        EfRelationalProviderBinding.UseSqlite(builder, _database.ConnectionString, "__EFMigrationsHistory_SerialOpenStress");
        return builder.Options;
    }

    private static void Seed(DbContextOptions<StressContext> options)
    {
        using var context = new StressContext(options);
        context.Database.EnsureCreated();
        context.Rows.AddRange(Enumerable.Range(1, 200).Select(id => new StressRow { Id = id, Value = $"row-{id}" }));
        context.SaveChanges();
    }

    /// <summary>Opens the context's connection and keeps it open, reading every row with a statement that stays active as it reads.</summary>
    private static void OpenAndRead(StressContext context, bool synchronous)
    {
        if (synchronous)
        {
            context.Database.OpenConnection();
            // Enumerate every row so the reader's statement stays active while the open races.
            foreach (var _ in context.Rows.AsNoTracking())
            {
            }
        }
        else
        {
            context.Database.OpenConnectionAsync().GetAwaiter().GetResult();
            context.Rows.AsNoTracking().ToListAsync().GetAwaiter().GetResult();
        }
    }

    private static int Setting(string suffix, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable($"{Variable}_{suffix}"), out var value) && value > 0 ? value : fallback;

    internal sealed class StressRow
    {
        public int Id { get; set; }

        public string Value { get; set; } = "";
    }

    internal sealed class StressContext(DbContextOptions<StressContext> options) : DbContext(options)
    {
        public DbSet<StressRow> Rows => Set<StressRow>();
    }
}

[CollectionDefinition(nameof(EfSqliteSerialOpenStressTests), DisableParallelization = true)]
public sealed class EfSqliteSerialOpenStressTestCollection;

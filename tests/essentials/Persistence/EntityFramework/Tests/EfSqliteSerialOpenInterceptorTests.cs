using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// #2209: Microsoft.Data.Sqlite 10.0.10 can lend one pooled connection to two opens of a connection string that check out
/// at once (dotnet/efcore#39008), so a SQLite binding opens one connection at a time per connection string. The library's
/// race lies between two of its own statements and cannot be held open from outside, so these tests hold the condition
/// it needs instead. A first open is held inside <c>SqliteConnection.Open</c>, past its checkout, and each test observes
/// what a second open does meanwhile. Contexts are bound as every module's are, through <see cref="EfRelationalProviderBinding"/>.
/// </summary>
public sealed class EfSqliteSerialOpenInterceptorTests : IAsyncDisposable
{
    private const int SqliteCantOpen = 14;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>How long a second open gets to pass a held first one. An ungated pooled open takes milliseconds.</summary>
    private static readonly TimeSpan Overlap = TimeSpan.FromSeconds(2);

    private readonly TemporarySqliteDatabase _database = new("serial-open");
    private readonly TemporarySqliteDatabase _other = new("serial-open-other");
    private readonly ManualResetEventSlim _release = new();
    private readonly List<DbContext> _contexts = [];
    private readonly List<Task> _holds = [];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_second_open_of_the_connection_string_waits_until_the_first_has_opened(bool synchronous)
    {
        var (first, firstOpening) = await HoldOpenAsync(_database.ConnectionString);
        var second = Context(_database.ConnectionString);
        var secondOpened = OpenedSignal(second);

        var secondOpening = Task.Run(() => OpenAsync(second, synchronous));
        await Task.WhenAny(secondOpening, Task.Delay(Overlap));
        Assert.False(secondOpened.Task.IsCompleted, "A second open of the connection string checked out while the first was still opening.");

        _release.Set();
        await firstOpening.WaitAsync(Patience);
        await secondOpening.WaitAsync(Patience);
        Assert.NotEqual(Handle(first), Handle(second));
    }

    [Fact]
    public async Task An_open_of_another_connection_string_does_not_wait()
    {
        await HoldOpenAsync(_database.ConnectionString);
        var other = Context(_other.ConnectionString);

        await other.Database.OpenConnectionAsync().WaitAsync(Patience);

        Assert.Equal(ConnectionState.Open, other.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task An_open_cancelled_while_it_waits_reports_the_cancellation_and_leaves_the_gate_free()
    {
        var (_, firstOpening) = await HoldOpenAsync(_database.ConnectionString);
        var waiting = Context(_database.ConnectionString);
        using var cancellation = new CancellationTokenSource();

        var opening = waiting.Database.OpenConnectionAsync(cancellation.Token);
        Assert.False(opening.IsCompleted, "The open did not wait for the held one.");
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening.WaitAsync(Patience));
        Assert.Equal(ConnectionState.Closed, waiting.Database.GetDbConnection().State);
        _release.Set();
        await firstOpening.WaitAsync(Patience);
        await waiting.Database.OpenConnectionAsync().WaitAsync(Patience);
        Assert.Equal(ConnectionState.Open, waiting.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_open_that_fails_reports_the_engine_error_and_leaves_the_gate_free(bool synchronous)
    {
        // A file in a directory that does not exist cannot be created, so every open of it fails in the engine.
        var unreachable = $"Data Source={Path.Join(Path.GetTempPath(), $"elsa-missing-{Guid.NewGuid():N}", "elsa.db")}";

        // Twice: a gate the first failure kept would leave the second waiting until the patience runs out.
        foreach (var context in new[] { Context(unreachable), Context(unreachable) })
        {
            var failure = await Assert.ThrowsAsync<SqliteException>(() => Task.Run(() => OpenAsync(context, synchronous)).WaitAsync(Patience));
            Assert.Equal(SqliteCantOpen, failure.SqliteErrorCode);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Lets a held open finish before its context goes, so no thread is left waiting on a test that has failed.
        _release.Set();
        await Task.WhenAll(_holds).WaitAsync(Patience);
        foreach (var context in _contexts)
            await context.DisposeAsync();
        await _database.DisposeAsync();
        await _other.DisposeAsync();
        _release.Dispose();
    }

    private DbContext Context(string connectionString)
    {
        var builder = new DbContextOptionsBuilder();
        EfRelationalProviderBinding.UseSqlite(builder, connectionString, "__EFMigrationsHistory_SerialOpen");
        var context = new DbContext(builder.Options);
        _contexts.Add(context);
        return context;
    }

    /// <summary>
    /// Opens a context of <paramref name="connectionString"/> and returns once that open is held inside
    /// <c>SqliteConnection.Open</c>, past its checkout. It stays there until the test sets <see cref="_release"/>.
    /// </summary>
    private async Task<(DbContext Context, Task Opening)> HoldOpenAsync(string connectionString)
    {
        var context = Context(connectionString);
        var held = OpenedSignal(context);
        context.Database.GetDbConnection().StateChange += (_, change) =>
        {
            if (change.CurrentState == ConnectionState.Open)
                _release.Wait();
        };
        var opening = Task.Run(() => context.Database.OpenConnectionAsync());
        _holds.Add(opening);
        await held.Task.WaitAsync(Patience);
        return (context, opening);
    }

    /// <summary>Completes when <paramref name="context"/>'s connection has been checked out and opened, inside <c>SqliteConnection.Open</c>.</summary>
    private static TaskCompletionSource OpenedSignal(DbContext context)
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Database.GetDbConnection().StateChange += (_, change) =>
        {
            if (change.CurrentState == ConnectionState.Open)
                opened.TrySetResult();
        };
        return opened;
    }

    private static async Task OpenAsync(DbContext context, bool synchronous)
    {
        if (synchronous)
            context.Database.OpenConnection();
        else
            await context.Database.OpenConnectionAsync();
    }

    private static IntPtr Handle(DbContext context) => ((SqliteConnection)context.Database.GetDbConnection()).Handle!.DangerousGetHandle();
}

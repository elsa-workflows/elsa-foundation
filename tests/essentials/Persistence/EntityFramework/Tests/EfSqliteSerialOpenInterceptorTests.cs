using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// #2209: Microsoft.Data.Sqlite 10.0.10 can lend one pooled connection to two opens of a connection string that check out
/// at once (dotnet/efcore#39008), so a SQLite binding opens one connection at a time per connection string. The library's
/// race lies between two of its own statements and cannot be held open from outside, so these tests hold the condition
/// it needs instead. A first open is held inside <c>SqliteConnection.Open</c>, past its checkout, and each test observes
/// what a second open does meanwhile. Contexts are bound as every module's are, through <see cref="EfRelationalProviderBinding"/>.
/// The race itself, under load, is <see cref="EfSqliteSerialOpenStressTests"/>, which runs only on request.
/// </summary>
/// <remarks>
/// The gate is a workaround until Elsa pins Microsoft.Data.Sqlite 10.0.13, which carries the upstream fix
/// (dotnet/efcore#39012). <see cref="The_pinned_SQLite_provider_still_needs_the_gate"/> fails as soon as it does, so the
/// gate is removed with the bump (#2220).
/// </remarks>
public sealed class EfSqliteSerialOpenInterceptorTests : IAsyncDisposable
{
    private const int SqliteCantOpen = 14;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>How long a second open gets to pass a held first one. An ungated pooled open takes milliseconds.</summary>
    private static readonly TimeSpan Overlap = TimeSpan.FromSeconds(2);

    /// <summary>The first Microsoft.Data.Sqlite release that records a pooled connection's owner before activating it.</summary>
    private static readonly Version FixedUpstream = new(10, 0, 13);

    private readonly TemporarySqliteDatabase _database = new("serial-open");
    private readonly TemporarySqliteDatabase _other = new("serial-open-other");
    private readonly string _uncreatedDirectory = Path.Join(Path.GetTempPath(), $"elsa-serial-open-{Guid.NewGuid():N}");
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
    public async Task An_open_that_fails_reports_the_engine_error_and_leaves_no_open_behind(bool synchronous)
    {
        // A file in a directory that does not exist yet cannot be created, so every open of it fails in the engine.
        var unreachable = $"Data Source={Path.Join(_uncreatedDirectory, "elsa.db")}";
        var events = new OpenEvents();
        var contexts = new[] { Context(unreachable, events), Context(unreachable, events) };

        // Twice: a gate the first failure kept would leave the second waiting until the patience runs out.
        foreach (var context in contexts)
        {
            var failure = await Assert.ThrowsAsync<SqliteException>(() => OpenOffThreadAsync(context, synchronous));
            Assert.Equal(SqliteCantOpen, failure.SqliteErrorCode);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        Assert.Equal((Opened: 0, Failed: 2), (events.Opened, events.Failed));

        // Once the file can be created, the context that failed opens, and EF reports that one open alone.
        Directory.CreateDirectory(_uncreatedDirectory);
        await OpenOffThreadAsync(contexts[0], synchronous);
        Assert.Equal(ConnectionState.Open, contexts[0].Database.GetDbConnection().State);
        Assert.Equal((Opened: 1, Failed: 2), (events.Opened, events.Failed));
    }

    /// <summary>
    /// #2220: the gate stays only until the pinned provider carries the upstream fix. This fails as soon as either SQLite
    /// package this suite loads reaches that version, so the bump removes the gate rather than leaving it for good.
    /// </summary>
    [Theory]
    [InlineData(typeof(SqliteConnection))]
    [InlineData(typeof(SqliteDbContextOptionsBuilderExtensions))]
    public void The_pinned_SQLite_provider_still_needs_the_gate(Type providerType)
    {
        var assembly = providerType.Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                            ?? throw new InvalidOperationException($"{assembly.GetName().Name} carries no informational version.");
        var version = Version.Parse(informational.Split('+', '-')[0]);

        Assert.True(
            version < FixedUpstream,
            $"{assembly.GetName().Name} {version} carries the upstream fix for the pool race (dotnet/efcore#39012): " +
            "remove EfSqliteSerialOpenInterceptor (elsa-workflows/elsa-foundation#2220).");
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
        TemporarySqliteDatabase.ClearPoolAndDeleteFiles(Path.Join(_uncreatedDirectory, "elsa.db"));
        if (Directory.Exists(_uncreatedDirectory))
            Directory.Delete(_uncreatedDirectory, recursive: true);
        _release.Dispose();
    }

    private DbContext Context(string connectionString, IInterceptor? observer = null)
    {
        var builder = new DbContextOptionsBuilder();
        EfRelationalProviderBinding.UseSqlite(builder, connectionString, "__EFMigrationsHistory_SerialOpen");
        if (observer is not null)
            builder.AddInterceptors(observer);
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

    /// <summary>Opens on a pool thread, so a synchronous open that never returns fails the test instead of hanging it.</summary>
    private static Task OpenOffThreadAsync(DbContext context, bool synchronous) =>
        Task.Run(() => OpenAsync(context, synchronous)).WaitAsync(Patience);

    private static IntPtr Handle(DbContext context) => ((SqliteConnection)context.Database.GetDbConnection()).Handle!.DangerousGetHandle();

    /// <summary>The opens EF reports as done and as failed, for the contexts that carry it after the gate.</summary>
    private sealed class OpenEvents : DbConnectionInterceptor
    {
        private int _opened;
        private int _failed;

        public int Opened => Volatile.Read(ref _opened);

        public int Failed => Volatile.Read(ref _failed);

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Interlocked.Increment(ref _opened);

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _opened);
            return Task.CompletedTask;
        }

        public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) => Interlocked.Increment(ref _failed);

        public override Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _failed);
            return Task.CompletedTask;
        }
    }
}

using System.Data.Common;
using System.Text.RegularExpressions;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// One module's SQLite database in its own file. Every context opens its own connection, so contexts are
/// genuinely concurrent (WAL, with Microsoft.Data.Sqlite retrying a busy database until its timeout) and a test
/// that uses several modules exercises separate databases, exactly as a split host would.
/// </summary>
internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string path;
    private readonly Action<DbContextOptionsBuilder>? configure;

    private SqliteTestDatabase(string path, Action<DbContextOptionsBuilder>? configure)
    {
        this.path = path;
        this.configure = configure;
    }

    public string ConnectionString => $"Data Source={path};Pooling=False;Default Timeout=30";

    /// <param name="configure">Applied to every context this database opens, including the one that creates it.</param>
    public static async Task<SqliteTestDatabase> CreateAsync<TContext>(
        Func<DbContextOptions<TContext>, TContext> create,
        Action<DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
    {
        var database = new SqliteTestDatabase(Path.Join(Path.GetTempPath(), $"elsa-publishing-ef-{Guid.NewGuid():N}.db"), configure);
        await using (var connection = new SqliteConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteNonQueryAsync();
        }

        await using var context = database.Open(create);
        await context.Database.EnsureCreatedAsync();
        return database;
    }

    public TContext Open<TContext>(Func<DbContextOptions<TContext>, TContext> create, params IInterceptor[] interceptors)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>().UseSqlite(ConnectionString);
        configure?.Invoke(options);
        if (interceptors.Length > 0)
            options.AddInterceptors(interceptors);
        return create(options.Options);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(path + suffix);
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; } = current;

    public static TestAccess Scoped(string scope) => new(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));
}

/// <summary>
/// Receipt records hold their diagnostics in lists and dictionaries, which compare by reference, so equivalence
/// is asserted member by member.
/// </summary>
internal static class ReceiptAssert
{
    public static void Equivalent(ActivityPublicationReceipt expected, ActivityPublicationReceipt? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected with { Diagnostics = [] }, actual with { Diagnostics = [] });
        Diagnostics(expected.Diagnostics, actual.Diagnostics);
    }

    public static void Equivalent(ActivityDraftTestRunReceipt expected, ActivityDraftTestRunReceipt? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected with { Failure = null }, actual with { Failure = null });
        Assert.Equal(expected.Failure is null, actual.Failure is null);
        if (expected.Failure is null)
            return;
        Assert.Equal(expected.Failure with { Diagnostics = [] }, actual.Failure! with { Diagnostics = [] });
        Diagnostics(expected.Failure.Diagnostics, actual.Failure!.Diagnostics);
    }

    private static void Diagnostics(IReadOnlyList<ActivityDiagnostic> expected, IReadOnlyList<ActivityDiagnostic> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (left, right) in expected.Zip(actual))
        {
            Assert.Equal(left with { Metadata = null! }, right with { Metadata = null! });
            Assert.Equal(left.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal), right.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }
    }
}

/// <summary>A crash injected at a storage seam, not an error path the code under test chose.</summary>
internal sealed class InjectedCrashException(string message) : Exception(message);

/// <summary>
/// Refuses a save while <paramref name="refuse"/> matches what the context is about to write, so a test can cut
/// a multi-phase operation at an exact boundary.
/// </summary>
internal sealed class RefuseSaveInterceptor(Func<DbContext, bool> refuse) : SaveChangesInterceptor
{
    public int Refused { get; private set; }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Refuse(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Refuse(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Refuse(DbContext? context)
    {
        if (context is null || !refuse(context))
            return;
        Refused++;
        throw new InjectedCrashException("The save was refused at the storage seam.");
    }
}

/// <summary>
/// Runs <paramref name="interleave"/> once, before the first query that reads <paramref name="table"/>, on any
/// provider's identifier quoting (Npgsql leaves lower-case names bare), so a competing commit lands between
/// two reads of the code under test.
/// </summary>
internal sealed class BeforeFirstReadInterceptor(string table, Func<Task> interleave) : DbCommandInterceptor
{
    private readonly Regex source = new($"FROM\\s+[\"\\[`]?{Regex.Escape(table)}[\"\\]`]?(\\s|$)", RegexOptions.CultureInvariant);
    private int remaining = 1;

    public bool Fired => remaining == 0;

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (source.IsMatch(command.CommandText) && Interlocked.Exchange(ref remaining, 0) == 1)
            await interleave();
        return result;
    }
}

/// <summary>
/// Holds each participating context after its first query until every participant has run one, so racing
/// attempts are all past their opening read before any of them writes. Without it a "race" can degrade into
/// sequential attempts on a busy machine.
/// </summary>
internal sealed class RendezvousAfterFirstQueryInterceptor(RendezvousAfterFirstQueryInterceptor.Rendezvous rendezvous) : DbCommandInterceptor
{
    private int remaining = 1;

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref remaining, 0) == 1)
            await rendezvous.ArriveAsync();
        return result;
    }

    internal sealed class Rendezvous(int participants)
    {
        private readonly TaskCompletionSource all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrived;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref arrived) == participants)
                all.TrySetResult();
            return all.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }
}

/// <summary>
/// Runs <paramref name="interleave"/> once, immediately before the context's next save, so a competing write
/// lands exactly between the code under test's read and its write.
/// </summary>
internal sealed class InterleaveBeforeSaveInterceptor(Func<Task> interleave) : SaveChangesInterceptor
{
    private int remaining = 1;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref remaining, 0) == 1)
            await interleave();
        return result;
    }
}

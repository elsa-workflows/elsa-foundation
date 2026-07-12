using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Entities;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Storage;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests;

public sealed class EfCoreStructuredLogStoreTests
{
    private static EfCoreStructuredLogStore NewStore(StructuredLogsTestHost host, Action<StructuredLogsOptions>? configure = null)
    {
        var options = new StructuredLogsOptions();
        configure?.Invoke(options);
        return new EfCoreStructuredLogStore(host, Options.Create(options));
    }

    private static void Seed(StructuredLogsTestHost host, params (long Sequence, LogLevel Level, string Category, string SourceId)[] rows)
    {
        using var db = host.CreateDbContext();
        foreach (var row in rows)
        {
            db.StructuredLogEntries.Add(new PersistedStructuredLogEntry
            {
                Sequence = row.Sequence,
                Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(row.Sequence),
                Level = (int)row.Level,
                Category = row.Category,
                Message = $"m{row.Sequence}",
                SourceId = row.SourceId,
            });
        }

        db.SaveChanges();
    }

    [Fact]
    public async Task GetHighWaterMarkReturnsMaxSequence()
    {
        using var host = StructuredLogsTestHost.Create();
        Seed(host, (5, LogLevel.Information, "c", "s"), (7, LogLevel.Information, "c", "s"), (3, LogLevel.Information, "c", "s"));

        Assert.Equal(7L, await NewStore(host).GetHighWaterMarkAsync());
    }

    [Fact]
    public async Task GetHighWaterMarkReturnsZeroWhenTableMissing()
    {
        using var host = StructuredLogsTestHost.Create(createSchema: false);

        // Must not throw even though the table does not exist yet (pre-migration window).
        Assert.Equal(0L, await NewStore(host).GetHighWaterMarkAsync());
    }

    [Fact]
    public async Task GetHighWaterMarkPropagatesProviderFailureInsteadOfReportingNeverWritten()
    {
        var failure = new InvalidOperationException("provider unavailable");
        var store = new EfCoreStructuredLogStore(
            new ThrowingDbContextFactory(failure),
            Options.Create(new StructuredLogsOptions()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetHighWaterMarkAsync());

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task GetHighWaterMarkDoesNotMisclassifyCorruptTableShapeAsNeverWritten()
    {
        using var host = StructuredLogsTestHost.Create();
        await using (var db = host.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE StructuredLogEntries RENAME COLUMN Sequence TO BrokenSequence;");

        var exception = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            NewStore(host).GetHighWaterMarkAsync());

        Assert.Contains("no such column", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRecentIsNewestLastAndClampedToRequestedCount()
    {
        using var host = StructuredLogsTestHost.Create();
        Seed(host,
            (1, LogLevel.Information, "c", "s"),
            (2, LogLevel.Information, "c", "s"),
            (3, LogLevel.Information, "c", "s"),
            (4, LogLevel.Information, "c", "s"),
            (5, LogLevel.Information, "c", "s"));

        var recent = await NewStore(host).GetRecentAsync(new StructuredLogFilter { MaxCount = 2 });

        Assert.Equal(new[] { 4L, 5L }, recent.Select(e => e.Sequence));
    }

    [Fact]
    public async Task GetRecentAppliesLevelFilter()
    {
        using var host = StructuredLogsTestHost.Create();
        Seed(host, (1, LogLevel.Information, "c", "s"), (2, LogLevel.Error, "c", "s"));

        var recent = await NewStore(host).GetRecentAsync(new StructuredLogFilter { MinimumLevel = LogLevel.Warning });

        Assert.Single(recent);
        Assert.Equal(LogLevel.Error, recent[0].Level);
    }

    [Fact]
    public async Task ReplayPagesThroughTheWholeSnapshotBeyondThePerQueryLimit()
    {
        using var host = StructuredLogsTestHost.Create();
        Seed(host,
            (1, LogLevel.Information, "c", "s"),
            (2, LogLevel.Information, "c", "s"),
            (3, LogLevel.Information, "c", "s"),
            (4, LogLevel.Information, "c", "s"),
            (5, LogLevel.Information, "c", "s"));

        var anchor = (await NewStore(host).GetRecentAsync(StructuredLogFilter.None))[0];
        var pagedStore = NewStore(host, options => options.MaxRecentQuerySize = 2);
        var entries = new List<StructuredLogEntry>();
        StructuredLogReplayCursor? position = anchor.ReplayCursor;
        StructuredLogReadPage page;
        do
        {
            page = await pagedStore.ReadAfterAsync(position, StructuredLogFilter.None, 2);
            entries.AddRange(page.Entries);
            position = page.NextCursor;
        } while (page.HasMore);

        Assert.Equal(new[] { 2L, 3L, 4L, 5L }, entries.Select(x => x.Sequence));
    }

    [Fact]
    public async Task ReadAfterRejectsDefaultCursorAtTheEfStoreBoundary()
    {
        using var host = StructuredLogsTestHost.Create();

        var exception = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            NewStore(host).ReadAfterAsync(default(StructuredLogReplayCursor), StructuredLogFilter.None, 100));

        Assert.Equal("The structured log replay cursor is unavailable.", exception.Message);
    }

    [Fact]
    public async Task TrimToZeroAndRestartPreserveLifetimeLogicalHighWater()
    {
        using var host = StructuredLogsTestHost.Create();
        StructuredLogReplayCursor cursor;
        await using (var beforeRestart = NewStore(host))
        {
            beforeRestart.StartDraining();
            var committed = await beforeRestart.AppendAsync(TestEntries.Create(sequence: 19, message: "trimmed"));
            cursor = committed.ReplayCursor!.Value;
            await beforeRestart.CompleteDrainingAsync();
            await beforeRestart.TrimAsync(0);
        }

        await using var restarted = NewStore(host);

        Assert.Equal(19, await restarted.GetHighWaterMarkAsync());
        Assert.Empty(await restarted.GetRecentAsync(StructuredLogFilter.None));
        await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            restarted.ReadAfterAsync(cursor, StructuredLogFilter.None, 100));
    }

    [Fact]
    public void AppendRejectsTheExactReservedHighWaterRepresentation()
    {
        using var host = StructuredLogsTestHost.Create();
        var store = NewStore(host);
        const string reserved = "__elsa_structured_logs_high_water__";
        var collision = TestEntries.Create(category: reserved, sourceId: reserved) with
        {
            EventName = reserved,
            MessageTemplate = reserved
        };

        Assert.Throws<ArgumentException>(() => store.AppendAsync(collision));
    }

    [Fact]
    public async Task ConcurrentFirstInitializationPreservesTheMaximumWithoutExposingStateRows()
    {
        using var host = StructuredLogsTestHost.Create();
        await using var first = NewStore(host);
        await using var second = NewStore(host);
        first.StartDraining();
        second.StartDraining();

        var commits = await Task.WhenAll(
            first.AppendAsync(TestEntries.Create(sequence: 11, message: "first")).AsTask(),
            second.AppendAsync(TestEntries.Create(sequence: 23, message: "second")).AsTask());
        await Task.WhenAll(first.CompleteDrainingAsync(), second.CompleteDrainingAsync());

        Assert.Equal(23, await NewStore(host).GetHighWaterMarkAsync());
        Assert.Equal(2, (await NewStore(host).GetRecentAsync(StructuredLogFilter.None)).Count);
        Assert.All(commits, committed => Assert.NotNull(committed.ReplayCursor));
    }

    [Fact]
    public async Task FailedBatchCannotAdvanceLifetimeHighWaterBeforeCommit()
    {
        using var host = StructuredLogsTestHost.Create();
        await using (var db = host.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER fail_doomed_entry
                BEFORE INSERT ON StructuredLogEntries
                WHEN NEW.Message = 'doomed'
                BEGIN
                    SELECT RAISE(ABORT, 'doomed');
                END;
                """);
        }

        await using var store = new EfCoreStructuredLogStore(
            host,
            Options.Create(new StructuredLogsOptions()),
            maxRetainedEntries: 100,
            pruneInterval: 100,
            baseRetryDelay: TimeSpan.FromMilliseconds(1));
        var append = store.AppendAsync(TestEntries.Create(sequence: 99, message: "doomed")).AsTask();
        store.StartDraining();

        await Assert.ThrowsAsync<StructuredLogsException>(() => append);

        Assert.Equal(0, await NewStore(host).GetHighWaterMarkAsync());
        await using var verification = host.CreateDbContext();
        Assert.Empty(await verification.StructuredLogEntries.ToListAsync());
    }

    [Fact]
    public async Task DrainPersistsAppendedEntriesRoundTrippingComplexFields()
    {
        using var host = StructuredLogsTestHost.Create();
        var store = NewStore(host);
        store.StartDraining();

        var entry = TestEntries.Create(
            sequence: 0,
            level: LogLevel.Error,
            message: "boom",
            properties: [new LogProperty("user", "alice")],
            scopes: [new LogScope([new LogProperty("op", "checkout")], "scope-text")],
            exception: new LogExceptionDetails("System.InvalidOperationException", "bad state", "at X"));

        store.Append(entry);

        await store.CompleteDrainingAsync();

        var persisted = Assert.Single(await store.GetRecentAsync(StructuredLogFilter.None));
        Assert.Equal("boom", persisted.Message);
        Assert.Equal(LogLevel.Error, persisted.Level);
        Assert.Equal("alice", Assert.Single(persisted.Properties, p => p.Name == "user").Value);
        var scope = Assert.Single(persisted.Scopes);
        Assert.Equal("scope-text", scope.Text);
        Assert.Equal("checkout", Assert.Single(scope.Items, p => p.Name == "op").Value);
        Assert.NotNull(persisted.Exception);
        Assert.Equal("System.InvalidOperationException", persisted.Exception!.Type);

        store.Dispose();
    }

    [Fact]
    public async Task DrainPrunesOldestBeyondRetentionCap()
    {
        using var host = StructuredLogsTestHost.Create();
        // Cap retention at 5 rows, prune after every 4 inserts.
        var store = new EfCoreStructuredLogStore(host, Options.Create(new StructuredLogsOptions()), maxRetainedEntries: 5, pruneInterval: 4);
        store.StartDraining();

        for (var i = 1; i <= 40; i++)
            store.Append(TestEntries.Create(sequence: i, message: $"m{i}"));

        // Deterministic (deflake of the former poll-based variant): once the drain completes, every queued
        // entry is persisted and the final retention prune has run, so the exact cap can be asserted.
        await store.CompleteDrainingAsync();

        Assert.Equal(5, (await store.GetRecentAsync(StructuredLogFilter.None)).Count);

        var newest = await store.GetRecentAsync(new StructuredLogFilter { MaxCount = 1 });
        Assert.Equal(40L, Assert.Single(newest).Sequence);

        store.Dispose();
    }

    [Fact]
    public async Task CompleteDrainingPrunesTailBelowPruneInterval()
    {
        using var host = StructuredLogsTestHost.Create();
        // Six inserts can never reach the prune interval, so only the completion-time prune can enforce the cap.
        var store = new EfCoreStructuredLogStore(host, Options.Create(new StructuredLogsOptions()), maxRetainedEntries: 5, pruneInterval: 100);
        store.StartDraining();

        for (var i = 1; i <= 6; i++)
            store.Append(TestEntries.Create(sequence: i, message: $"m{i}"));

        await store.CompleteDrainingAsync();

        Assert.Equal(5, (await store.GetRecentAsync(StructuredLogFilter.None)).Count);

        var newest = await store.GetRecentAsync(new StructuredLogFilter { MaxCount = 1 });
        Assert.Equal(6L, Assert.Single(newest).Sequence);

        store.Dispose();
    }

    [Fact]
    public async Task CompleteDrainingAsyncThrowsWhenDrainingWasNeverStarted()
    {
        using var host = StructuredLogsTestHost.Create();
        using var store = NewStore(host);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteDrainingAsync());
    }

    [Fact]
    public async Task ShellTerminatorWhenDrainingWasNeverStartedIsNoOp()
    {
        using var host = StructuredLogsTestHost.Create();
        using var store = NewStore(host);

        await new StopStructuredLogDrainingShellTerminator(store).TerminateAsync();
    }

    [Fact]
    public async Task DisposeAsyncPersistsBufferedEntriesBeforeCancelling()
    {
        // The issue #606 shutdown scenario: entries are still queued in the channel when the shell
        // provider disposes the store. The async path must drain them instead of cancelling mid-batch.
        using var host = StructuredLogsTestHost.Create();
        var store = NewStore(host);
        store.StartDraining();

        for (var i = 1; i <= 10; i++)
            store.Append(TestEntries.Create(sequence: i, message: $"m{i}"));

        await store.DisposeAsync();

        await using var restarted = NewStore(host);
        Assert.Equal(10, (await restarted.GetRecentAsync(StructuredLogFilter.None)).Count);
    }

    [Fact]
    public async Task DisposeAsyncClampsNegativeShutdownDrainTimeout()
    {
        // A negative configured window must clamp to zero (immediate hard stop), not surface an
        // ArgumentOutOfRangeException from WaitAsync in the middle of host shutdown.
        using var host = StructuredLogsTestHost.Create();
        var store = NewStore(host, o => o.ShutdownDrainTimeout = TimeSpan.FromSeconds(-1));
        store.StartDraining();
        store.Append(TestEntries.Create(sequence: 1, message: "m1"));

        await store.DisposeAsync();
    }

    [Fact]
    public async Task HardStopCompletesAcceptedAppendWithFailure()
    {
        using var host = StructuredLogsTestHost.Create();
        var factory = new HangingDbContextFactory(host);
        var store = new EfCoreStructuredLogStore(
            factory,
            Options.Create(new StructuredLogsOptions { ShutdownDrainTimeout = TimeSpan.Zero }),
            maxRetainedEntries: 5,
            pruneInterval: 100);
        store.StartDraining();
        var append = store.AppendAsync(TestEntries.Create(sequence: 1, message: "pending")).AsTask();
        await factory.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        await store.DisposeAsync();

        var completed = await Task.WhenAny(append, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(append, completed);
        await Assert.ThrowsAsync<StructuredLogsException>(() => append);
    }

    /// <summary>
    /// Covers the dispose-guard half of issue #403: a second Dispose() call must be a no-op (parity with
    /// EfCoreOpenTelemetryStore) instead of throwing ObjectDisposedException from the already-disposed
    /// CancellationTokenSource. The sync and async paths share the guard, so any later call in either
    /// direction is equally a no-op.
    /// </summary>
    [Fact]
    public async Task DisposeIsIdempotentAcrossSyncAndAsyncPaths()
    {
        using var host = StructuredLogsTestHost.Create();
        var store = NewStore(host);
        store.StartDraining();

        store.Dispose();
        store.Dispose();
        await store.DisposeAsync();
    }

    private sealed class ThrowingDbContextFactory(Exception exception) : IDbContextFactory<StructuredLogsDbContext>
    {
        public StructuredLogsDbContext CreateDbContext() => throw exception;

        public Task<StructuredLogsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<StructuredLogsDbContext>(exception);
    }

    private sealed class HangingDbContextFactory(StructuredLogsTestHost host) : Microsoft.EntityFrameworkCore.IDbContextFactory<StructuredLogsDbContext>
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public StructuredLogsDbContext CreateDbContext() => host.CreateDbContext();

        public async Task<StructuredLogsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}

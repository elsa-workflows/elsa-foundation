using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Entities;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Storage;
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
    public async Task GetAfterReturnsGreaterSequencesOldestFirst()
    {
        using var host = StructuredLogsTestHost.Create();
        Seed(host,
            (1, LogLevel.Information, "c", "s"),
            (2, LogLevel.Information, "c", "s"),
            (3, LogLevel.Information, "c", "s"),
            (4, LogLevel.Information, "c", "s"));

        var after = await NewStore(host).GetAfterAsync(2, StructuredLogFilter.None);

        Assert.Equal(new[] { 3L, 4L }, after.Select(e => e.Sequence));
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

        using var db = host.CreateDbContext();
        Assert.Equal(5, db.StructuredLogEntries.Count());

        var newest = await store.GetRecentAsync(new StructuredLogFilter { MaxCount = 1 });
        Assert.Equal(40L, Assert.Single(newest).Sequence);

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

        using var db = host.CreateDbContext();
        Assert.Equal(10, db.StructuredLogEntries.Count());
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
}

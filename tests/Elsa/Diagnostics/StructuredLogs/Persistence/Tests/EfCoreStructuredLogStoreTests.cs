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
    public void GetHighWaterMarkReturnsMaxSequence()
    {
        using var host = StructuredLogsTestHost.Create();
        Seed(host, (5, LogLevel.Information, "c", "s"), (7, LogLevel.Information, "c", "s"), (3, LogLevel.Information, "c", "s"));

        Assert.Equal(7L, NewStore(host).GetHighWaterMark());
    }

    [Fact]
    public void GetHighWaterMarkReturnsZeroWhenTableMissing()
    {
        using var host = StructuredLogsTestHost.Create(createSchema: false);

        // Must not throw even though the table does not exist yet (pre-migration window).
        Assert.Equal(0L, NewStore(host).GetHighWaterMark());
    }

    [Fact]
    public void GetRecentIsNewestLastAndClampedToRequestedCount()
    {
        using var host = StructuredLogsTestHost.Create();
        Seed(host,
            (1, LogLevel.Information, "c", "s"),
            (2, LogLevel.Information, "c", "s"),
            (3, LogLevel.Information, "c", "s"),
            (4, LogLevel.Information, "c", "s"),
            (5, LogLevel.Information, "c", "s"));

        var recent = NewStore(host).GetRecent(new StructuredLogFilter { MaxCount = 2 });

        Assert.Equal(new[] { 4L, 5L }, recent.Select(e => e.Sequence));
    }

    [Fact]
    public void GetRecentAppliesLevelFilter()
    {
        using var host = StructuredLogsTestHost.Create();
        Seed(host, (1, LogLevel.Information, "c", "s"), (2, LogLevel.Error, "c", "s"));

        var recent = NewStore(host).GetRecent(new StructuredLogFilter { MinimumLevel = LogLevel.Warning });

        Assert.Single(recent);
        Assert.Equal(LogLevel.Error, recent[0].Level);
    }

    [Fact]
    public void GetAfterReturnsGreaterSequencesOldestFirst()
    {
        using var host = StructuredLogsTestHost.Create();
        Seed(host,
            (1, LogLevel.Information, "c", "s"),
            (2, LogLevel.Information, "c", "s"),
            (3, LogLevel.Information, "c", "s"),
            (4, LogLevel.Information, "c", "s"));

        var after = NewStore(host).GetAfter(2, StructuredLogFilter.None);

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
            exception: new LogExceptionInfo("System.InvalidOperationException", "bad state", "at X"));

        store.Append(entry);

        var persisted = await WaitForAsync(() =>
        {
            var recent = store.GetRecent(StructuredLogFilter.None);
            return recent.Count == 1 ? recent[0] : null;
        });

        Assert.NotNull(persisted);
        Assert.Equal("boom", persisted!.Message);
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

        // Eventually the table is pruned down to roughly the retention cap and the newest row survives.
        var pruned = await WaitForConditionAsync(() =>
        {
            using var db = host.CreateDbContext();
            var count = db.StructuredLogEntries.Count();
            return count is > 0 and <= 9; // cap (5) + at most one prune-interval (4) of slack
        });

        Assert.True(pruned);

        var newest = store.GetRecent(new StructuredLogFilter { MaxCount = 1 });
        Assert.Equal(40L, Assert.Single(newest).Sequence);

        store.Dispose();
    }

    private static async Task<bool> WaitForConditionAsync(Func<bool> probe, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (probe())
                return true;
            await Task.Delay(25);
        }

        return probe();
    }

    private static async Task<T?> WaitForAsync<T>(Func<T?> probe, int timeoutMs = 5000) where T : class
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var result = probe();
            if (result is not null)
                return result;
            await Task.Delay(25);
        }

        return probe();
    }
}

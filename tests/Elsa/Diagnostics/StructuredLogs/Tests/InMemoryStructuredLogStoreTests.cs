using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class InMemoryStructuredLogStoreTests
{
    private static StructuredLogEntry Seq(long sequence, string message = "m", LogLevel level = LogLevel.Information) =>
        TestEntries.Create(sequence: sequence, level: level, message: message);

    [Fact]
    public async Task AppendRetainsEntriesAndTracksHighWaterMark()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());

        store.Append(Seq(1, "a"));
        store.Append(Seq(2, "b"));

        var recent = await store.GetRecentAsync(StructuredLogFilter.None);
        Assert.Equal(new[] { 1L, 2L }, recent.Select(e => e.Sequence));
        Assert.Equal(2L, await store.GetHighWaterMarkAsync());
    }

    [Fact]
    public async Task HighWaterMarkIsZeroWhenEmpty()
    {
        Assert.Equal(0L, await new InMemoryStructuredLogStore(TestOptions.Create()).GetHighWaterMarkAsync());
    }

    [Fact]
    public async Task EvictsOldestBeyondCapacity()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create(o => o.BufferCapacity = 2));

        store.Append(Seq(1, "a"));
        store.Append(Seq(2, "b"));
        store.Append(Seq(3, "c"));

        var recent = await store.GetRecentAsync(StructuredLogFilter.None);
        Assert.Equal(new[] { "b", "c" }, recent.Select(e => e.Message));
    }

    [Fact]
    public async Task GetRecentIsNewestAlignedAndClampedToTake()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        for (var i = 0; i < 5; i++)
            store.Append(Seq(i + 1, i.ToString()));

        var recent = await store.GetRecentAsync(new StructuredLogFilter { MaxCount = 2 });

        Assert.Equal(new[] { "3", "4" }, recent.Select(e => e.Message));
    }

    [Fact]
    public async Task GetRecentClampsTakeToMaxRecentQuerySize()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create(o => o.MaxRecentQuerySize = 2));
        for (var i = 0; i < 5; i++)
            store.Append(Seq(i + 1, i.ToString()));

        Assert.Equal(2, (await store.GetRecentAsync(new StructuredLogFilter { MaxCount = 100 })).Count);
    }

    [Fact]
    public async Task GetRecentAppliesFilter()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        store.Append(Seq(1, level: LogLevel.Information));
        store.Append(Seq(2, level: LogLevel.Error));

        var recent = await store.GetRecentAsync(new StructuredLogFilter { MinimumLevel = LogLevel.Warning });

        Assert.Single(recent);
        Assert.Equal(LogLevel.Error, recent[0].Level);
    }

    [Fact]
    public async Task GetRecentWithZeroTakeReturnsEmpty()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        store.Append(Seq(1));

        Assert.Empty(await store.GetRecentAsync(new StructuredLogFilter { MaxCount = 0 }));
    }

    [Fact]
    public async Task GetAfterReturnsOnlyLaterSequencesOldestFirst()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        for (var i = 0; i < 4; i++)
            store.Append(Seq(i + 1, i.ToString()));

        var after = await store.GetAfterAsync(2, StructuredLogFilter.None);

        Assert.Equal(new[] { 3L, 4L }, after.Select(e => e.Sequence));
    }
}

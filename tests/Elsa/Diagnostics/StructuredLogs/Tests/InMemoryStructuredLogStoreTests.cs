using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class InMemoryStructuredLogStoreTests
{
    [Fact]
    public void AppendAssignsMonotonicSequence()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());

        store.Append(TestEntries.Create(message: "a"));
        store.Append(TestEntries.Create(message: "b"));

        var recent = store.GetRecent(StructuredLogFilter.None);
        Assert.Equal(new[] { 1L, 2L }, recent.Select(e => e.Sequence));
    }

    [Fact]
    public void EvictsOldestBeyondCapacity()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create(o => o.BufferCapacity = 2));

        store.Append(TestEntries.Create(message: "a"));
        store.Append(TestEntries.Create(message: "b"));
        store.Append(TestEntries.Create(message: "c"));

        var recent = store.GetRecent(StructuredLogFilter.None);
        Assert.Equal(new[] { "b", "c" }, recent.Select(e => e.Message));
    }

    [Fact]
    public void GetRecentIsNewestAlignedAndClampedToTake()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        for (var i = 0; i < 5; i++)
            store.Append(TestEntries.Create(message: i.ToString()));

        var recent = store.GetRecent(new StructuredLogFilter { MaxCount = 2 });

        Assert.Equal(new[] { "3", "4" }, recent.Select(e => e.Message));
    }

    [Fact]
    public void GetRecentClampsTakeToMaxRecentQuerySize()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create(o => o.MaxRecentQuerySize = 2));
        for (var i = 0; i < 5; i++)
            store.Append(TestEntries.Create(message: i.ToString()));

        var recent = store.GetRecent(new StructuredLogFilter { MaxCount = 100 });

        Assert.Equal(2, recent.Count);
    }

    [Fact]
    public void GetRecentAppliesFilter()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        store.Append(TestEntries.Create(level: LogLevel.Information));
        store.Append(TestEntries.Create(level: LogLevel.Error));

        var recent = store.GetRecent(new StructuredLogFilter { MinimumLevel = LogLevel.Warning });

        Assert.Single(recent);
        Assert.Equal(LogLevel.Error, recent[0].Level);
    }

    [Fact]
    public void GetRecentWithZeroTakeReturnsEmpty()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        store.Append(TestEntries.Create());

        Assert.Empty(store.GetRecent(new StructuredLogFilter { MaxCount = 0 }));
    }

    [Fact]
    public void GetAfterReturnsOnlyLaterSequencesOldestFirst()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        for (var i = 0; i < 4; i++)
            store.Append(TestEntries.Create(message: i.ToString()));

        var after = store.GetAfter(2, StructuredLogFilter.None);

        Assert.Equal(new[] { 3L, 4L }, after.Select(e => e.Sequence));
    }

    [Fact]
    public async Task SubscribeDeliversMatchingEntries()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var enumerator = store.Subscribe(new StructuredLogFilter { MinimumLevel = LogLevel.Warning }, cts.Token)
            .GetAsyncEnumerator(cts.Token);

        store.Append(TestEntries.Create(level: LogLevel.Information, message: "skip"));
        store.Append(TestEntries.Create(level: LogLevel.Error, message: "keep"));

        Assert.True(await enumerator.MoveNextAsync());
        Assert.NotNull(enumerator.Current.Entry);
        Assert.Equal("keep", enumerator.Current.Entry!.Message);

        await cts.CancelAsync();
    }

    [Fact]
    public async Task BackpressureDropsAndEmitsDroppedSignalWithoutBlockingProducer()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create(o => o.SubscriberQueueCapacity = 1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var enumerator = store.Subscribe(StructuredLogFilter.None, cts.Token).GetAsyncEnumerator(cts.Token);

        // Fill the single-slot queue and overflow it without any reader draining — must not block.
        for (var i = 0; i < 5; i++)
            store.Append(TestEntries.Create(message: i.ToString()));

        // First item is the buffered entry; subsequent read surfaces the in-band drop signal.
        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(enumerator.Current.IsEntry);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(enumerator.Current.IsDropped);
        Assert.True(enumerator.Current.Dropped!.DroppedCount > 0);

        await cts.CancelAsync();
    }
}

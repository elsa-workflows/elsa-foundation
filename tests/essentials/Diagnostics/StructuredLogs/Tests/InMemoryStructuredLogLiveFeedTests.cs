using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Live;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class InMemoryStructuredLogLiveFeedTests
{
    [Fact]
    public async Task SubscribeDeliversMatchingEntriesOnly()
    {
        var feed = new InMemoryStructuredLogLiveFeed(TestOptions.Create());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var enumerator = feed.Subscribe(new StructuredLogFilter { MinimumLevel = LogLevel.Warning }, cts.Token)
            .GetAsyncEnumerator(cts.Token);

        feed.Publish(TestEntries.Create(sequence: 1, level: LogLevel.Information, message: "skip"));
        feed.Publish(TestEntries.Create(sequence: 2, level: LogLevel.Error, message: "keep"));

        Assert.True(await enumerator.MoveNextAsync());
        Assert.NotNull(enumerator.Current.Entry);
        Assert.Equal("keep", enumerator.Current.Entry!.Message);

        await cts.CancelAsync();
    }

    [Fact]
    public async Task BackpressureDropsAndEmitsDroppedSignalWithoutBlockingPublisher()
    {
        var feed = new InMemoryStructuredLogLiveFeed(TestOptions.Create(o => o.SubscriberQueueCapacity = 1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var enumerator = feed.Subscribe(StructuredLogFilter.None, cts.Token).GetAsyncEnumerator(cts.Token);

        // Fill the single-slot queue and overflow it without any reader draining — must not block.
        for (var i = 0; i < 5; i++)
            feed.Publish(TestEntries.Create(sequence: i + 1, message: i.ToString()));

        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(enumerator.Current.IsEntry);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(enumerator.Current.IsDropped);
        Assert.True(enumerator.Current.Dropped!.DroppedCount > 0);

        await cts.CancelAsync();
    }
}

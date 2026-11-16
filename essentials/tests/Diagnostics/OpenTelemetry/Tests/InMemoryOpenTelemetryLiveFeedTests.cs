using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Providers.InMemory;
using Elsa.Diagnostics.OpenTelemetry.Services;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public class InMemoryOpenTelemetryLiveFeedTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SubscribeAsync_DeliversPublishedTraceMatchingFilter()
    {
        var options = OptionsFactory.Create(new OpenTelemetryDiagnosticsOptions());
        var feed = new InMemoryOpenTelemetryLiveFeed(options, new OpenTelemetrySourceRegistry(options));
        await using var enumerator = feed.SubscribeAsync(new OpenTelemetryTraceFilter()).GetAsyncEnumerator();

        // Starting the enumerator registers the subscriber before we publish.
        var moveNext = enumerator.MoveNextAsync();

        var trace = new TelemetryTrace("trace-1", "root", "name", Now, Now.AddMilliseconds(5), TimeSpan.FromMilliseconds(5), SpanStatus.Ok, ["resource-1"], [], 1);
        await feed.PublishAsync(new OpenTelemetryBatch([], [trace], [], [], [], []));

        Assert.True(await moveNext);
        Assert.Equal("trace-1", enumerator.Current.Trace?.TraceId);
    }

    [Fact]
    public async Task SubscribeAsync_WhenFilterExcludesTrace_DoesNotDeliver()
    {
        var options = OptionsFactory.Create(new OpenTelemetryDiagnosticsOptions());
        var feed = new InMemoryOpenTelemetryLiveFeed(options, new OpenTelemetrySourceRegistry(options));
        using var cts = new CancellationTokenSource();
        var enumerator = feed.SubscribeAsync(new OpenTelemetryTraceFilter { TraceId = "other" }, cts.Token).GetAsyncEnumerator(cts.Token);

        var moveNext = enumerator.MoveNextAsync().AsTask();

        var trace = new TelemetryTrace("trace-1", "root", "name", Now, Now.AddMilliseconds(5), TimeSpan.FromMilliseconds(5), SpanStatus.Ok, ["resource-1"], [], 1);
        await feed.PublishAsync(new OpenTelemetryBatch([], [trace], [], [], [], []));

        var completed = await Task.WhenAny(moveNext, Task.Delay(200));
        Assert.NotSame(moveNext, completed);

        // Cancel the pending MoveNextAsync before disposing so the async iterator can complete cleanly.
        await cts.CancelAsync();
        try
        {
            await moveNext;
        }
        catch (OperationCanceledException)
        {
        }

        await enumerator.DisposeAsync();
    }
}

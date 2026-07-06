using Xunit;
using Elsa.Events.Channels;
using Elsa.Events.Core.Contracts;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Events.Tests;

/// <summary>
/// IN-5: the background event channel gets a real graceful-shutdown hook. StopAsync completes the
/// channel writer so the read loop drains everything already queued and then exits cleanly, and the
/// host actually calls it on shutdown — exercised end-to-end through the public ITaskManager path.
/// </summary>
public class BackgroundEventPublisherShutdownTests
{
    [Fact]
    public async Task StopAsyncDrainsAllQueuedEventsThenExitsCleanly()
    {
        // Direct on the publisher: N queued, StopAsync completes the writer, the read loop drains all N
        // and returns normally (no cancellation exception).
        var counting = new CountingInlineEventPublisher();
        var services = new ServiceCollection();
        services.AddSingleton<IInlineEventPublisher>(counting);
        var provider = services.BuildServiceProvider();
        var channel = new EventChannel();
        var publisher = new BackgroundEventPublisher(channel, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BackgroundEventPublisher>.Instance);

        const int n = 25;
        for (var i = 0; i < n; i++)
            await channel.Writer.WriteAsync(TestEvents.Background());

        using var host = new CancellationTokenSource();
        var run = publisher.ExecuteAsync(host.Token);

        await publisher.StopAsync(CancellationToken.None);
        await run;

        Assert.Equal(n, counting.Count);
        Assert.True(run.IsCompletedSuccessfully);
        Assert.False(channel.Writer.TryWrite(TestEvents.Background())); // writer completed
    }

    [Fact]
    public async Task HostShutdownDispatchesAllQueuedEventsAndExitsCleanly()
    {
        // Full host wiring through the public surface: TaskManager runs the BackgroundEventPublisher as a
        // registered IBackgroundTask; DisposeAsync (the host shutdown path) calls TaskStateManager.Stop(),
        // which now signals StopAsync BEFORE cancelling the lifetime token. If StopAsync were a dead hook,
        // the writer would never complete and the queued events could be cut off by the token cancel — so
        // "all N dispatched, clean exit" is the wiring proof.
        var counting = new CountingInlineEventPublisher();
        await using var provider = EventTestHosts.BuildProductionLikeProvider(counting);

        var channel = provider.GetRequiredService<IEventChannel>();
        const int n = 25;
        for (var i = 0; i < n; i++)
            await channel.Writer.WriteAsync(TestEvents.Background());

        var taskManager = provider.GetRequiredService<ITaskManager>();
        await taskManager.StartExecutingRegisteredTasks(CancellationToken.None);

        // Host shutdown — must drain the queue via StopAsync before the token is cancelled.
        await ((IAsyncDisposable)taskManager).DisposeAsync();

        Assert.Equal(n, counting.Count);
    }
}

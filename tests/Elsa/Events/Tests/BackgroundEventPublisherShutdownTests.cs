using Xunit;
using Elsa.Events.Channels;
using Elsa.Events.Contexts;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Events.Tests;

/// <summary>
/// IN-5: the background event channel gets a real graceful-shutdown hook. StopAsync completes the
/// channel writer so the read loop drains everything already queued and then exits cleanly, and the
/// host actually calls it on shutdown — exercised end-to-end through the public ITaskManager path.
/// </summary>
public class BackgroundEventPublisherShutdownTests
{
    private sealed record TestEvent : IEvent;

    // The background worker drains queued events through IInlineEventPublisher (its intent-revealing
    // re-dispatch face), so the counting stub stands in for that face.
    private sealed class CountingPublisher : IInlineEventPublisher
    {
        public int Count;
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task StopAsyncDrainsAllQueuedEventsThenExitsCleanly()
    {
        // Direct on the publisher: N queued, StopAsync completes the writer, the read loop drains all N
        // and returns normally (no cancellation exception).
        var counting = new CountingPublisher();
        var services = new ServiceCollection();
        services.AddSingleton<IInlineEventPublisher>(counting);
        var provider = services.BuildServiceProvider();
        var channel = new EventChannel();
        var publisher = new BackgroundEventPublisher(channel, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BackgroundEventPublisher>.Instance);

        const int n = 25;
        for (var i = 0; i < n; i++)
            await channel.Writer.WriteAsync(new EventContext(new TestEvent(), EventPublishingStrategy.Background, null!));

        using var host = new CancellationTokenSource();
        var run = publisher.ExecuteAsync(host.Token);

        await publisher.StopAsync(CancellationToken.None);
        await run;

        Assert.Equal(n, counting.Count);
        Assert.True(run.IsCompletedSuccessfully);
        Assert.False(channel.Writer.TryWrite(new EventContext(new TestEvent(), EventPublishingStrategy.Background, null!))); // writer completed
    }

    [Fact]
    public async Task HostShutdownDispatchesAllQueuedEventsAndExitsCleanly()
    {
        // Full host wiring through the public surface: TaskManager runs the BackgroundEventPublisher as a
        // registered IBackgroundTask; DisposeAsync (the host shutdown path) calls TaskStateManager.Stop(),
        // which now signals StopAsync BEFORE cancelling the lifetime token. If StopAsync were a dead hook,
        // the writer would never complete and the queued events could be cut off by the token cancel — so
        // "all N dispatched, clean exit" is the wiring proof.
        var counting = new CountingPublisher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDistributedLockProvider, NoOpLockProvider>();
        services.AddSingleton<IEventChannel, EventChannel>();
        services.AddSingleton<IInlineEventPublisher>(counting);
        services.AddSingleton<TaskExecutor>();
        services.AddSingleton<ITaskExecutor>(sp => sp.GetRequiredService<TaskExecutor>());
        services.AddSingleton<IBackgroundTaskStarter>(sp => sp.GetRequiredService<TaskExecutor>());
        services.AddSingleton<IBackgroundTask, BackgroundEventPublisher>();
        services.AddSingleton<ITaskManager, TaskManager>();
        var provider = services.BuildServiceProvider();

        var channel = provider.GetRequiredService<IEventChannel>();
        const int n = 25;
        for (var i = 0; i < n; i++)
            await channel.Writer.WriteAsync(new EventContext(new TestEvent(), EventPublishingStrategy.Background, null!));

        var taskManager = provider.GetRequiredService<ITaskManager>();
        await taskManager.StartExecutingRegisteredTasks(CancellationToken.None);

        // Host shutdown — must drain the queue via StopAsync before the token is cancelled.
        await ((IAsyncDisposable)taskManager).DisposeAsync();

        Assert.Equal(n, counting.Count);
    }

    private sealed class NoOpLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new((IDistributedSynchronizationHandle?)new Handle());
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

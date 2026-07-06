using Xunit;
using Elsa.Events.Core.Contracts;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Events.Tests;

/// <summary>
/// Regression guard for the W31 shell-lifetime bug: CShells runs shell initializers inside an
/// <see cref="AsyncServiceScope"/> that it disposes when initialization completes. When
/// <see cref="ITaskManager"/> was registered scoped, that disposal ran <c>TaskManager.DisposeAsync</c>
/// → <c>TaskStateManager.Stop()</c> → <c>BackgroundEventPublisher.StopAsync()</c>, which completes the
/// <b>singleton</b> <see cref="IEventChannel"/> writer. The channel can never be reopened, so every
/// subsequent background/deferred publish threw <c>ChannelClosedException</c> (and the singleton
/// reader loop was torn down, silently dropping events). The fix registers the task-lifecycle services
/// as shell-singletons so they are stopped only at real shell teardown.
///
/// These tests use the production DI lifetimes via <see cref="EventTestHosts.BuildProductionLikeProvider"/>.
/// </summary>
public class BackgroundEventPublisherLifetimeTests
{
    [Fact]
    public async Task ChannelSurvivesInitializerScopeDisposalAndKeepsDispatching()
    {
        var counting = new CountingInlineEventPublisher();
        await using var root = EventTestHosts.BuildProductionLikeProvider(counting);

        // Simulate CShells RunInitializersAsync: resolve the (singleton) TaskManager inside a child
        // scope, start the tasks, then dispose the scope. The singleton TaskManager must NOT be
        // disposed with the scope, so the singleton channel + reader stay alive.
        await using (var initScope = root.CreateAsyncScope())
        {
            var taskManager = initScope.ServiceProvider.GetRequiredService<ITaskManager>();
            await taskManager.StartExecutingRegisteredTasks(CancellationToken.None);
        }

        // A normal request now publishes via the Background strategy against the singleton channel.
        var channel = root.GetRequiredService<IEventChannel>();
        var writeException = await Record.ExceptionAsync(async () =>
            await channel.Writer.WriteAsync(TestEvents.Background()));

        Assert.Null(writeException); // Pre-fix: throws ChannelClosedException.

        // And the still-alive reader loop must actually dispatch it.
        await counting.WaitForAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, counting.Count);

        // Graceful shutdown drain (IN-5) at these singleton lifetimes is covered by
        // BackgroundEventPublisherShutdownTests.HostShutdownDispatchesAllQueuedEventsAndExitsCleanly.
    }
}

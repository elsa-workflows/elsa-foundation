using Elsa.Events.Channels;
using Elsa.Events.Contexts;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Events.Tests;

/// <summary>Shared test doubles + host wiring for the BackgroundEventPublisher test suites.</summary>
internal sealed record TestEvent : IEvent;

internal static class TestEvents
{
    /// <summary>A queued context for the Background strategy, carrying no caller token.</summary>
    public static EventContext Background(CancellationToken enqueueToken = default) =>
        new(new TestEvent(), EventPublishingStrategy.Background, null!, enqueueToken);
}

/// <summary>
/// Stands in for the intent-revealing <see cref="IInlineEventPublisher"/> face the background worker
/// drains through. Records how many events were dispatched and the token each was dispatched under,
/// and exposes an honest <see cref="WaitForAsync"/> barrier that completes only once the requested
/// number of events have actually been dispatched.
/// </summary>
internal sealed class CountingInlineEventPublisher : IInlineEventPublisher
{
    private readonly object _gate = new();
    private readonly List<(int Target, TaskCompletionSource Tcs)> _waiters = [];
    private int _count;

    public int Count
    {
        get { lock (_gate) return _count; }
    }

    public CancellationToken LastToken { get; private set; }

    /// <summary>A task that completes once <paramref name="target"/> events have been dispatched.</summary>
    public Task WaitForAsync(int target)
    {
        lock (_gate)
        {
            if (_count >= target)
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((target, tcs));
            return tcs.Task;
        }
    }

    public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _count++;
            LastToken = cancellationToken;

            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_count < _waiters[i].Target)
                    continue;

                _waiters[i].Tcs.TrySetResult();
                _waiters.RemoveAt(i);
            }
        }

        return Task.CompletedTask;
    }
}

internal sealed class NoOpLockProvider : IDistributedLockProvider
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

internal static class EventTestHosts
{
    /// <summary>
    /// Builds a provider that mirrors the production DI lifetimes from <c>TasksFeature</c> +
    /// <c>EventsFeature</c>: a singleton <see cref="IEventChannel"/> + singleton
    /// <see cref="BackgroundEventPublisher"/> managed by a shell-singleton <see cref="ITaskManager"/>.
    /// <c>ValidateScopes</c> is on so a captive-dependency regression (a singleton capturing a scoped
    /// service) fails the build rather than slipping through.
    /// </summary>
    public static ServiceProvider BuildProductionLikeProvider(IInlineEventPublisher inlinePublisher)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDistributedLockProvider, NoOpLockProvider>();

        // EventsFeature lifetimes.
        services.AddSingleton<IEventChannel, EventChannel>();
        services.AddSingleton(inlinePublisher);

        // TasksFeature lifetimes (shell-singleton).
        services.AddSingleton<TaskExecutor>();
        services.AddSingleton<ITaskExecutor>(sp => sp.GetRequiredService<TaskExecutor>());
        services.AddSingleton<IBackgroundTaskStarter>(sp => sp.GetRequiredService<TaskExecutor>());
        services.AddSingleton<IBackgroundTask, BackgroundEventPublisher>();
        services.AddSingleton<ITaskManager, TaskManager>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}

using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Minimal <see cref="IEventPublisher"/> that runs an optional <see cref="OnPublish"/> hook on each
/// published event and captures nothing else. Tests use the hook to simulate validator
/// contributions onto <c>OnDraftValidating</c> (errors are derived state — the draft store and the
/// promotion gate re-run the gate through this publisher to derive the current error set). Shared
/// across the Groundwork design-persistence test suites.
/// </summary>
public sealed class CapturingEventPublisher : IEventPublisher
{
    public Action<IEvent>? OnPublish { get; set; }

    public Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
    {
        OnPublish?.Invoke(@event);
        return Task.CompletedTask;
    }
}

/// <summary>
/// No-op <see cref="IEventPublisher"/> for store constructions that never derive validation errors
/// (they only read the draft / layout), so no <c>OnDraftValidating</c> contribution is needed.
/// </summary>
public sealed class NoOpEventPublisher : IEventPublisher
{
    public static readonly NoOpEventPublisher Instance = new();

    public Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

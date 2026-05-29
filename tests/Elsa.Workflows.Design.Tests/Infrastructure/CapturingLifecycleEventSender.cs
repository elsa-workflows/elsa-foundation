using Elsa.Mediator.Core.Contracts;
using System.Collections.Concurrent;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

/// <summary>
/// Test stub of <see cref="ILifecycleEventSender"/>. Captures every published lifecycle event
/// synchronously — bypasses the production background channel + worker so test assertions can
/// inspect captured events without timing concerns.
/// </summary>
/// <remarks>
/// Production wiring routes lifecycle events through <c>BackgroundProcessingStrategy</c> to the
/// <c>NotificationsChannel</c>, drained by <c>BackgroundEventPublisher</c>. Tests skip that
/// path; they care about "did the publisher call <c>SendAsync</c> with the expected event?"
/// not about end-to-end channel + worker dispatch (which is covered by integration tests in
/// the future Mediator-hygiene unit per the deferred Mediator middleware test obligation).
/// </remarks>
public sealed class CapturingLifecycleEventSender : ILifecycleEventSender
{
    private readonly ConcurrentQueue<ILifecycleEvent> _events = new();

    public IReadOnlyList<ILifecycleEvent> CapturedEvents => _events.ToArray();

    public T? LastOf<T>() where T : class, ILifecycleEvent => _events.OfType<T>().LastOrDefault();

    public Task Send(ILifecycleEvent lifecycleEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(lifecycleEvent);
        return Task.CompletedTask;
    }

    public Task SendAsync(
        ILifecycleEvent lifecycleEvent,
        IEventPublishingStrategy? strategy,
        CancellationToken cancellationToken = default
    )
    {
        _events.Enqueue(lifecycleEvent);
        return Task.CompletedTask;
    }
}

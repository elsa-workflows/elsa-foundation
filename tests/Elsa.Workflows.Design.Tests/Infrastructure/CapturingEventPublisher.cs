using Elsa.Events.Core.Contracts;
using System.Collections.Concurrent;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

/// <summary>
/// Stub <see cref="IEventPublisher"/> that captures every event passed to <see cref="Publish"/>.
/// Replaces the former split capturing domain-event + lifecycle-event senders now that there is
/// a single Event concept. Used to assert the mutation pipeline dispatches the expected gate +
/// lifecycle events, and to let tests simulate validator contributions via <see cref="OnPublish"/>.
/// </summary>
/// <remarks>
/// Production wiring routes Sequential events synchronously and Background events through the
/// channel + worker. This stub captures every event synchronously regardless of strategy so
/// test assertions can inspect captured events without timing concerns.
/// </remarks>
public sealed class CapturingEventPublisher : IEventPublisher
{
    private readonly ConcurrentQueue<IEvent> _events = new();

    public IReadOnlyList<IEvent> CapturedEvents => _events.ToArray();

    public T? LastOf<T>() where T : class, IEvent => _events.OfType<T>().LastOrDefault();

    /// <summary>
    /// Optional hook invoked on every <see cref="Publish"/> call before the event is enqueued.
    /// Tests use it to simulate validator contributions (e.g. add a <c>ValidationError</c> to
    /// an <c>OnDraftValidating</c> instance) without spinning up the full event pipeline +
    /// handler-invoker middleware.
    /// </summary>
    public Action<IEvent>? OnPublish { get; set; }

    public Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
    {
        OnPublish?.Invoke(@event);
        _events.Enqueue(@event);
        return Task.CompletedTask;
    }
}

using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
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
/// test assertions can inspect captured events without timing concerns. Tests that exercise the
/// event-sourcing seam (SC-012/SC-013) attach subscribers via <see cref="Subscribe{T}"/>; those
/// honour the strategy's resilience contract — Sequential lets handler failures propagate to the
/// publishing caller, Background isolates them (mirroring the production
/// <c>BackgroundEventPublisher</c> worker that catches + logs handler faults).
/// </remarks>
public sealed class CapturingEventPublisher : IEventPublisher
{
    private readonly ConcurrentQueue<IEvent> _events = new();
    private readonly List<Subscription> _subscriptions = [];

    public IReadOnlyList<IEvent> CapturedEvents => _events.ToArray();

    public T? LastOf<T>() where T : class, IEvent => _events.OfType<T>().LastOrDefault();

    /// <summary>
    /// Optional hook invoked on every <see cref="Publish"/> call before the event is enqueued.
    /// Tests use it to simulate validator contributions (e.g. add a <c>ValidationError</c> to
    /// an <c>OnDraftValidating</c> instance) without spinning up the full event pipeline +
    /// handler-invoker middleware.
    /// </summary>
    public Action<IEvent>? OnPublish { get; set; }

    /// <summary>
    /// Register a subscriber for events assignable to <typeparamref name="T"/>, mirroring an
    /// <c>IEventHandler&lt;T&gt;</c> on the production substrate. Dispatched during
    /// <see cref="Publish"/> with the strategy's resilience semantics (see remarks on the class).
    /// </summary>
    public void Subscribe<T>(Func<T, Task> handler) where T : class, IEvent =>
        _subscriptions.Add(new Subscription(typeof(T), e => handler((T)e)));

    public async Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
    {
        OnPublish?.Invoke(@event);
        _events.Enqueue(@event);

        // Honour the resilience contract of the chosen strategy. Background owns its own
        // resilience (fire-and-forget; the worker catches + logs), so a faulty subscriber can
        // never break the publishing caller; Sequential awaits its handlers and lets a throw
        // surface to the caller.
        var shielded = strategy is BackgroundProcessingStrategy;
        foreach (var subscription in _subscriptions.Where(s => s.EventType.IsInstanceOfType(@event)))
        {
            if (shielded)
            {
                try { await subscription.Handler(@event); }
                catch { /* isolated — mirrors BackgroundEventPublisher worker exception handling */ }
            }
            else
            {
                await subscription.Handler(@event);
            }
        }
    }

    private sealed record Subscription(Type EventType, Func<IEvent, Task> Handler);
}

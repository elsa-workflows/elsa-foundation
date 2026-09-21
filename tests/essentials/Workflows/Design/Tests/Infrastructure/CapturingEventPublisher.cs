using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using System.Collections.Concurrent;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

/// <summary>
/// Stub publisher implementing both delivery faces (<see cref="IInlineEventPublisher"/> and
/// <see cref="IDeferredEventPublisher"/>) that captures every published event. Replaces the former
/// split capturing domain-event + lifecycle-event senders now that there is a single Event concept.
/// Used to assert the mutation pipeline dispatches the expected gate + lifecycle events, and to let
/// tests simulate validator contributions via <see cref="OnPublish"/>. One instance satisfies both
/// the inline and deferred ctor dependencies of the commands under test.
/// </summary>
/// <remarks>
/// Production wiring routes inline events synchronously and deferred events through the channel +
/// worker. This stub captures every event synchronously regardless of face so test assertions can
/// inspect captured events without timing concerns. Tests that exercise the event-sourcing seam
/// (SC-012/SC-013) attach subscribers via <see cref="Subscribe{T}"/>; those honour each face's
/// resilience contract — inline lets handler failures propagate to the publishing caller, deferred
/// isolates them (mirroring the production <c>BackgroundEventPublisher</c> worker that catches +
/// logs handler faults).
/// </remarks>
public sealed class CapturingEventPublisher : IInlineEventPublisher, IDeferredEventPublisher
{
    private readonly ConcurrentQueue<IEvent> _events = new();
    private readonly List<Subscription> _subscriptions = [];

    public IReadOnlyList<IEvent> CapturedEvents => _events.ToArray();

    public T? LastOf<T>() where T : class, IEvent => _events.OfType<T>().LastOrDefault();

    /// <summary>
    /// Optional hook invoked on every <see cref="Publish"/> call before the event is enqueued.
    /// Tests use it to simulate validator contributions (e.g. add a <c>ValidationError</c> to
    /// an <c>DraftValidating</c> instance) without spinning up the full event pipeline +
    /// handler-invoker middleware.
    /// </summary>
    public Action<IEvent>? OnPublish { get; set; }

    /// <summary>
    /// Convenience: install an <see cref="OnPublish"/> hook that contributes <paramref name="error"/>
    /// onto every <c>DraftValidating</c> pass, simulating a validator that always emits it. Replaces
    /// the copy-pasted "if DraftValidating add ValidationError" lambda at each call site.
    /// </summary>
    public void ContributeError(ValidationError error) =>
        OnPublish = e =>
        {
            if (e is DraftValidating validating)
                validating.Errors.Add(error);
        };

    /// <summary>
    /// Register a subscriber for events assignable to <typeparamref name="T"/>, mirroring an
    /// <c>IEventHandler&lt;T&gt;</c> on the production substrate. Dispatched during publish with the
    /// resilience semantics of the face it was published through (see remarks on the class).
    /// </summary>
    public void Subscribe<T>(Func<T, Task> handler) where T : class, IEvent =>
        _subscriptions.Add(new Subscription(typeof(T), e => handler((T)e)));

    /// <summary>Inline delivery: subscriber failures propagate to the publishing caller.</summary>
    Task IInlineEventPublisher.Publish(IEvent @event, CancellationToken cancellationToken) =>
        PublishCore(@event, shielded: false);

    /// <summary>Deferred delivery: subscriber failures are isolated (mirrors the worker).</summary>
    Task IDeferredEventPublisher.Publish(IEvent @event, CancellationToken cancellationToken) =>
        PublishCore(@event, shielded: true);

    private async Task PublishCore(IEvent @event, bool shielded)
    {
        OnPublish?.Invoke(@event);
        _events.Enqueue(@event);

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

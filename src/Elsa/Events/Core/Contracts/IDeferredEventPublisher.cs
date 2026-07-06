namespace Elsa.Events.Core.Contracts;

/// <summary>
/// Enqueues an event for out-of-band delivery and returns immediately, before its handlers run.
/// Delivery is shielded (handler failures are caught and logged by the background worker, never
/// surfaced to the publishing caller) and there is no observable result. This is the delivery
/// mode the fire-and-forget notification sites depend on (draft-created / -validated / -discarded).
/// </summary>
/// <remarks>
/// Because the call returns before handlers run, an event whose handlers write state the caller
/// reads back MUST NOT be published this way — use <see cref="IInlineEventPublisher"/> instead.
/// </remarks>
public interface IDeferredEventPublisher
{
    Task Publish(IEvent @event, CancellationToken cancellationToken = default);
}

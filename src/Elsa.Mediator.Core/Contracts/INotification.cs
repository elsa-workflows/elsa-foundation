namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// Marker for a fire-and-don't-need-the-result message: "something happened — react if you want."
/// Contrast with <see cref="IDomainEvent"/> (contribution mechanism — publisher awaits and reads
/// contributions back) and <see cref="ICommand"/> (do-X-give-me-the-result).
/// </summary>
/// <remarks>
/// Notifications are dispatched via <see cref="INotificationSender"/> with a pluggable
/// <see cref="IEventPublishingStrategy"/> (Sequential / Parallel / Background). Subscribers
/// MUST NOT feed back to the publisher — by the time a subscriber sees the notification the
/// publisher may already have returned.
/// </remarks>
public interface INotification
{
}

namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// Publishes notifications via a pluggable <see cref="IEventPublishingStrategy"/>. The default
/// strategy is configured at feature setup; callers may pick a different strategy per call
/// (e.g. <c>NotificationStrategy.Background</c> for fire-and-forget, <c>Sequential</c> for
/// awaited in-order dispatch, <c>Parallel</c> for awaited-fan-out).
/// </summary>
public interface INotificationSender
{
    /// <summary>Publishes the notification using the configured default strategy.</summary>
    Task SendAsync(INotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the notification using the supplied strategy. Pass <c>null</c> to fall
    /// back to the configured default.
    /// </summary>
    Task Send(
        INotification notification,
        IEventPublishingStrategy? strategy,
        CancellationToken cancellationToken = default
    );
}

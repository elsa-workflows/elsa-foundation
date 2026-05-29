using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Notifications.Strategies;

namespace Elsa.Mediator.Notifications.Services;

/// <summary>
/// Delegates to <see cref="INotificationSender"/>. The narrowed surface gives state-management
/// callers a dedicated send point for transition events (separate from general-purpose
/// notifications), and lets the framework intercept lifecycle dispatch later if needs diverge.
/// Default strategy: <see cref="NotificationStrategy.Background"/> — lifecycle subscribers
/// (audit, event-sourcing stream, telemetry) should not block the publisher.
/// </summary>
public sealed class LifecycleEventSender(INotificationSender notificationSender) : ILifecycleEventSender
{
    public Task Send(ILifecycleEvent lifecycleEvent, CancellationToken cancellationToken = default)
        => notificationSender.Send(lifecycleEvent, NotificationStrategy.Background, cancellationToken);

    public Task SendAsync(
        ILifecycleEvent lifecycleEvent,
        IEventPublishingStrategy? strategy,
        CancellationToken cancellationToken = default
    )
        => notificationSender.Send(lifecycleEvent, strategy ?? NotificationStrategy.Background, cancellationToken);
}

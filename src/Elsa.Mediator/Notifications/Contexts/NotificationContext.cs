using Elsa.Mediator.Core.Contracts;

namespace Elsa.Mediator.Notifications.Contexts;

/// <inheritdoc cref="INotificationContext" />
public sealed class NotificationContext(
    INotification notification,
    IEventPublishingStrategy notificationStrategy,
    IServiceProvider serviceProvider,
    CancellationToken cancellationToken = default
)
    : INotificationContext
{
    public INotification Notification { get; init; } = notification;
    public IEventPublishingStrategy NotificationStrategy { get; init; } = notificationStrategy;
    public IServiceProvider ServiceProvider { get; } = serviceProvider;
    public CancellationToken CancellationToken { get; init; } = cancellationToken;
}

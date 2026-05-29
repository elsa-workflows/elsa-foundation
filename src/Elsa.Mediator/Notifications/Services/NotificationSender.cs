using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Notifications.Contexts;

namespace Elsa.Mediator.Notifications.Services;

/// <inheritdoc />
public sealed class NotificationSender(
    IServiceProvider serviceProvider,
    IEventPublishingStrategy defaultPublishingStrategy,
    INotificationPipeline notificationPipeline
)
    : INotificationSender
{
    public Task SendAsync(INotification notification, CancellationToken cancellationToken = default)
        => Send(notification, defaultPublishingStrategy, cancellationToken);

    public Task Send(
        INotification notification,
        IEventPublishingStrategy? strategy,
        CancellationToken cancellationToken = default
    )
    {
        var resolvedStrategy = strategy ?? defaultPublishingStrategy;

        var context = new NotificationContext(
            notification,
            resolvedStrategy,
            serviceProvider,
            cancellationToken
        );

        return notificationPipeline.ExecuteAsync(context);
    }
}

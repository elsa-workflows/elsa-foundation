using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Notifications.Contexts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Mediator.Notifications.Middleware;

/// <summary>
/// Resolves all <c>INotificationHandler&lt;TNotification&gt;</c> for the notification type
/// then hands off to the chosen <see cref="IEventPublishingStrategy"/>.
/// </summary>
public sealed class NotificationHandlerInvokerMiddleware(NotificationMiddlewareDelegate next)
    : INotificationMiddleware
{
    public async ValueTask Invoke(INotificationContext context)
    {
        var notificationType = context.Notification.GetType();
        var handlerType = typeof(INotificationHandler<>).MakeGenericType(notificationType);

        var allHandlers = context.ServiceProvider.GetServices<INotificationHandler>();
        var handlers = allHandlers
            .Where(handlerType.IsInstanceOfType)
            .DistinctBy(h => h.GetType())
            .ToArray();

        var strategyContext = new NotificationStrategyContext(
            context,
            handlers,
            context.ServiceProvider,
            context.CancellationToken
        );

        await context.NotificationStrategy.PublishAsync(strategyContext);

        await next(context);
    }
}

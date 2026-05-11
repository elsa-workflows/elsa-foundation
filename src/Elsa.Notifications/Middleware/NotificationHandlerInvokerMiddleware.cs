using Elsa.Notifications.Contexts;
using Elsa.Notifications.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Notifications.Middleware
{
    public sealed class NotificationHandlerInvokerMiddleware(
        NotificationMiddlewareDelegate next,
        ILogger<NotificationHandlerInvokerMiddleware> logger
    )
        : INotificationMiddleware
    {
        /// <inheritdoc />
        public async ValueTask InvokeAsync(INotificationContext context)
        {
            // Find all handlers for the specified notification.
            var notification = context.Notification;
            var notificationType = notification.GetType();
            var handlerType = typeof(INotificationHandler<>).MakeGenericType(notificationType);
            var serviceProvider = context.ServiceProvider;
            var notificationHandlers = serviceProvider.GetServices<INotificationHandler>();
            var handlers = notificationHandlers
                .Where(handlerType.IsInstanceOfType)
                .DistinctBy(x => x.GetType())
                .ToArray();

            var strategyContext = new NotificationStrategyContext(
                context,
                handlers,
                logger,
                serviceProvider,
                context.CancellationToken
            );
            await context.NotificationStrategy.PublishAsync(strategyContext);

            // Invoke next middleware.
            await next(context);
        }
    }

}
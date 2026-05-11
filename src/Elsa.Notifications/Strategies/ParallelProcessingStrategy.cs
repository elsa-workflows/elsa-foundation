using Elsa.Notifications.Core;
using Elsa.Notifications.Helpers;

namespace Elsa.Notifications.Strategies
{
    /// <summary>
    /// Invokes event handlers in parallel and waits for the result.
    /// </summary>
    public sealed class ParallelProcessingStrategy : IEventPublishingStrategy
    {
        /// <inheritdoc />
        public async Task PublishAsync(INotificationStrategyContext context)
        {
            var notificationContext = context.NotificationContext;
            var notification = notificationContext.Notification;
            var notificationType = notification.GetType();
            var handleMethod = notificationType.GetHandleAsync();
            var tasks = context.Handlers.Select(handler => handler.InvokeAsync(handleMethod, context.NotificationContext)).ToList();

            await Task.WhenAll(tasks);
        }
    }
}

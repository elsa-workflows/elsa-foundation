using Elsa.Notifications.Core;
using Elsa.Notifications.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Notifications.Strategies
{
    /// <summary>
    /// Invokes event handlers in sequence and waits for the result.
    /// </summary>
    public sealed class SequentialProcessingStrategy : IEventPublishingStrategy
    {
        /// <inheritdoc />
        public async Task PublishAsync(INotificationStrategyContext context)
        {
            var notificationContext = context.NotificationContext;
            var notification = notificationContext.Notification;
            var notificationType = notification.GetType();
            var handleMethod = notificationType.GetHandleAsync();

            foreach (var handler in context.Handlers)
                await handler.InvokeAsync(handleMethod, context.NotificationContext);
        }
    }
}

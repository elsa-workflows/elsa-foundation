using Elsa.Notifications.Core;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Notifications.Strategies
{
    /// <summary>
    /// Invokes event handlers in parallel and does not wait for the result.
    /// </summary>
    public sealed class BackgroundProcessingStrategy : IEventPublishingStrategy
    {
        /// <inheritdoc />
        public async Task PublishAsync(INotificationStrategyContext context)
        {
            var notificationsChannel = context.ServiceProvider.GetRequiredService<INotificationsChannel>();

            await notificationsChannel.Writer.WriteAsync(context.NotificationContext, context.CancellationToken);
        }
    }
}

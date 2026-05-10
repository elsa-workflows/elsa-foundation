using Elsa.Notifications.Core;
using Elsa.Notifications.Contexts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Notifications.Services
{
    public sealed class NotificationSender(IServiceProvider serviceProvider, IEventPublishingStrategy defaultPublishingStrategy, INotificationPipeline notificationPipeline)
        : INotificationSender
    {
        /// <inheritdoc />
        public async Task SendAsync(INotification notification, CancellationToken cancellationToken = default)
        {
            await SendAsync(notification, defaultPublishingStrategy, cancellationToken);
        }

        /// <inheritdoc />
        public async Task SendAsync(INotification notification, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
        {
            strategy ??= defaultPublishingStrategy;
            var context = new NotificationContext(notification, strategy, serviceProvider, cancellationToken);
            await notificationPipeline.ExecuteAsync(context);
        }
    }
}

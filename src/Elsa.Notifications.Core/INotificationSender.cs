using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Notifications.Core
{
    /// <summary>
    /// Publishes notifications.
    /// </summary>
    public interface INotificationSender
    {
        /// <summary>
        /// Publishes the given notification.
        /// </summary>
        /// <param name="notification">The notification to publish.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task SendAsync(INotification notification, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the given notification.
        /// </summary>
        /// <param name="notification">The notification to publish.</param>
        /// <param name="strategy"><see cref="SequentialProcessingStrategy"/><see cref="ParallelProcessingStrategy"/></param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task SendAsync(INotification notification, IEventPublishingStrategy? strategy, CancellationToken cancellationToken = default);
    }
}

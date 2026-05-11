using Elsa.Notifications.Core;

namespace Elsa.Notifications.Contexts
{
    /// <summary>
    /// Represents a context for a notification.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="NotificationContext"/> class.
    /// </remarks>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="notificationStrategy">The publishing strategy to use.</param>
    /// <param name="serviceProvider">The service provider to resolve services from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public sealed class NotificationContext(INotification notification, IEventPublishingStrategy notificationStrategy, IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        : INotificationContext
    {

        /// <summary>
        /// Gets the notification to publish.
        /// </summary>
        public INotification Notification { get; init; } = notification;

        /// <summary>
        /// Gets the publishing strategy to use.
        /// </summary>
        public IEventPublishingStrategy NotificationStrategy { get; init; } = notificationStrategy;

        /// <summary>
        /// Gets the service provider used for resolving dependencies within the notification context.
        /// </summary>
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        /// <summary>
        /// Gets the cancellation token.
        /// </summary>
        public CancellationToken CancellationToken { get; init; } = cancellationToken;
    }
}

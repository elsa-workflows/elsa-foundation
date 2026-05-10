namespace Elsa.Notifications.Core
{
    public interface INotificationContext
    {
        /// <summary>
        /// Gets the notification to publish.
        /// </summary>
        public INotification Notification { get; init; }

        /// <summary>
        /// Gets the publishing strategy to use.
        /// </summary>
        public IEventPublishingStrategy NotificationStrategy { get; init; }

        /// <summary>
        /// Gets the service provider used for resolving dependencies within the notification context.
        /// </summary>
        public IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// Gets the cancellation token.
        /// </summary>
        public CancellationToken CancellationToken { get; init; }
    }
}

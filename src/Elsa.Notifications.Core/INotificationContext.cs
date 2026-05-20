namespace Elsa.Notifications.Core
{
    public interface INotificationContext
    {
        /// <summary>
        /// Gets the notification to publish.
        /// </summary>
        INotification Notification { get; init; }

        /// <summary>
        /// Gets the publishing strategy to use.
        /// </summary>
        IEventPublishingStrategy NotificationStrategy { get; init; }

        /// <summary>
        /// Gets the service provider used for resolving dependencies within the notification context.
        /// </summary>
        IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// Gets the cancellation token.
        /// </summary>
        CancellationToken CancellationToken { get; init; }
    }
}

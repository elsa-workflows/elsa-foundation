namespace Elsa.Notifications.Core
{
    /// <summary>
    /// Represents a strategy for publishing events.
    /// </summary>
    public interface IEventPublishingStrategy
    {
        /// <summary>
        /// Publishes an event.
        /// </summary>
        /// <param name="context">The context for publishing the event.</param>
        Task PublishAsync(INotificationStrategyContext context);
    }
}

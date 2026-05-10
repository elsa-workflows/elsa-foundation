namespace Elsa.Notifications.Core
{
    /// <summary>
    /// Represents a notification middleware component.
    /// </summary>
    public interface INotificationMiddleware
    {
        /// <summary>
        /// Invokes the notification middleware.
        /// </summary>
        /// <param name="context">The notification context.</param>
        ValueTask InvokeAsync(INotificationContext context);
    }
}

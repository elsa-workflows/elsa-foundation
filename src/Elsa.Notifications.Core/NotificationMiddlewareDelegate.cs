namespace Elsa.Notifications.Core
{
    /// <summary>
    /// Represents a delegate for a notification middleware.
    /// </summary>
    public delegate ValueTask NotificationMiddlewareDelegate(INotificationContext context);
}

namespace Elsa.Notifications.Enums
{
    /// <summary>
    /// Provides a set of strategies for publishing events.
    /// </summary>
    public enum NotificationStrategy
    {
        /// <summary>
        /// Invokes event handlers in the background and does not wait for the result.
        /// </summary>
        Background,

        /// <summary>
        /// Invokes event handlers in parallel and waits for the result.
        /// </summary>
        Parallel,

        /// <summary>
        /// Invokes event handlers in sequence and waits for the result.
        /// </summary>
        Sequential
    }
}

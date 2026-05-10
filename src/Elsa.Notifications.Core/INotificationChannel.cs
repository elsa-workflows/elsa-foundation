using System.Threading.Channels;

namespace Elsa.Notifications.Core
{
    /// <summary>
    /// A channel that can be used to enqueue notifications.
    /// </summary>
    public interface INotificationsChannel
    {
        /// <summary>
        /// Gets the writer for the notification queue.
        /// </summary>
        ChannelWriter<INotificationContext> Writer { get; }

        /// <summary>
        /// Gets the reader for the notification queue.
        /// </summary>
        ChannelReader<INotificationContext> Reader { get; }
    }
}

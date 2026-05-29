using Elsa.Mediator.Core.Contracts;
using System.Threading.Channels;

namespace Elsa.Mediator.Notifications.Channels;

/// <inheritdoc />
public sealed class NotificationsChannel : INotificationsChannel
{
    private readonly Channel<INotificationContext> _channel = Channel.CreateUnbounded<INotificationContext>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false,
        }
    );

    public ChannelWriter<INotificationContext> Writer => _channel.Writer;
    public ChannelReader<INotificationContext> Reader => _channel.Reader;
}

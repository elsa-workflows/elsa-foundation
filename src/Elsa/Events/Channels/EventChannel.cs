using Elsa.Events.Core.Contracts;
using System.Threading.Channels;

namespace Elsa.Events.Channels;

/// <inheritdoc />
public sealed class EventChannel : IEventChannel
{
    private readonly Channel<IEventContext> _channel = Channel.CreateUnbounded<IEventContext>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false,
        }
    );

    public ChannelWriter<IEventContext> Writer => _channel.Writer;
    public ChannelReader<IEventContext> Reader => _channel.Reader;
}

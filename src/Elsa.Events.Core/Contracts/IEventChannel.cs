using System.Threading.Channels;

namespace Elsa.Events.Core.Contracts;

/// <summary>
/// FIFO channel used by the background publishing strategy to hand events off to a
/// hosted-service worker. Singleton; one writer side (the strategy), one reader side (the
/// worker). FIFO order at enqueue is preserved at dispatch.
/// </summary>
public interface IEventChannel
{
    ChannelWriter<IEventContext> Writer { get; }
    ChannelReader<IEventContext> Reader { get; }
}

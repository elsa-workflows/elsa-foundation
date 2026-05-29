using System.Threading.Channels;

namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// FIFO channel used by the <c>BackgroundProcessingStrategy</c> to hand notifications off to
/// a hosted-service worker. Singleton; one writer side (the strategy), one reader side (the
/// worker). FIFO order at enqueue is preserved at dispatch.
/// </summary>
public interface INotificationsChannel
{
    ChannelWriter<INotificationContext> Writer { get; }
    ChannelReader<INotificationContext> Reader { get; }
}

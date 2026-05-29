using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Mediator.Notifications.Strategies;

/// <summary>
/// Fire-and-forget. Enqueues the notification context on <see cref="INotificationsChannel"/>
/// and returns immediately; the <c>BackgroundEventPublisher</c> hosted-task drains the
/// channel and dispatches each notification via <see cref="SequentialProcessingStrategy"/>.
/// </summary>
public sealed class BackgroundProcessingStrategy : IEventPublishingStrategy
{
    public async Task PublishAsync(INotificationStrategyContext context)
    {
        var channel = context.ServiceProvider.GetRequiredService<INotificationsChannel>();
        await channel.Writer.WriteAsync(context.NotificationContext, context.CancellationToken);
    }
}

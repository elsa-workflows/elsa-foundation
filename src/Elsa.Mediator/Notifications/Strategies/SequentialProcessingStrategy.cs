using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Notifications.Helpers;

namespace Elsa.Mediator.Notifications.Strategies;

/// <summary>Awaited; each handler runs to completion before the next starts.</summary>
public sealed class SequentialProcessingStrategy : IEventPublishingStrategy
{
    public async Task PublishAsync(INotificationStrategyContext context)
    {
        var notification = context.NotificationContext.Notification;
        var handleMethod = notification.GetType().GetHandleAsync();

        foreach (var handler in context.Handlers)
            await handler.InvokeAsync(handleMethod, context.NotificationContext);
    }
}

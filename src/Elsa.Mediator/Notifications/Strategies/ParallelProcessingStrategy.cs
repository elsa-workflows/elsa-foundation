using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Notifications.Helpers;

namespace Elsa.Mediator.Notifications.Strategies;

/// <summary>Awaited fan-out; handlers run in parallel, completion is awaited collectively.</summary>
public sealed class ParallelProcessingStrategy : IEventPublishingStrategy
{
    public async Task PublishAsync(INotificationStrategyContext context)
    {
        var notification = context.NotificationContext.Notification;
        var handleMethod = notification.GetType().GetHandleAsync();

        var tasks = context.Handlers.Select(handler => handler.InvokeAsync(handleMethod, context.NotificationContext));
        await Task.WhenAll(tasks);
    }
}

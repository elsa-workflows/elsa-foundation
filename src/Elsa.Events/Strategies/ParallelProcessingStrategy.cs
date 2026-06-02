using Elsa.Events.Core.Contracts;
using Elsa.Events.Helpers;

namespace Elsa.Events.Strategies;

/// <summary>Awaited fan-out; handlers run in parallel, completion is awaited collectively.</summary>
public sealed class ParallelProcessingStrategy : IEventPublishingStrategy
{
    public async Task PublishAsync(IEventStrategyContext context)
    {
        var @event = context.EventContext.Event;
        var handleMethod = @event.GetType().GetHandleAsync();

        var tasks = context.Handlers.Select(handler => handler.InvokeAsync(handleMethod, context.EventContext));
        await Task.WhenAll(tasks);
    }
}

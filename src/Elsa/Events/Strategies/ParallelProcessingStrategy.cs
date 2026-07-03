using Elsa.Events.Core.Contracts;

namespace Elsa.Events.Strategies;

/// <summary>Awaited fan-out; handlers run in parallel, completion is awaited collectively.</summary>
public sealed class ParallelProcessingStrategy : IEventPublishingStrategy
{
    public async Task PublishAsync(IEventStrategyContext context)
    {
        var tasks = context.Handlers.Select(handler => handler.Invoke(context.EventContext));
        await Task.WhenAll(tasks);
    }
}

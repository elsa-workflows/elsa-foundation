using Elsa.Events.Core.Contracts;
using Elsa.Events.Helpers;

namespace Elsa.Events.Strategies;

/// <summary>
/// The default strategy: awaited, in-order, synchronous. Each handler runs to completion
/// before the next starts, and a throwing handler propagates to — and CAN break — the caller.
/// No exception shielding.
/// </summary>
public sealed class SequentialProcessingStrategy : IEventPublishingStrategy
{
    public async Task PublishAsync(IEventStrategyContext context)
    {
        var @event = context.EventContext.Event;
        var handleMethod = @event.GetType().GetHandleAsync();

        foreach (var handler in context.Handlers)
            await handler.InvokeAsync(handleMethod, context.EventContext);
    }
}

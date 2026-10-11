using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Microsoft.Extensions.Logging;

namespace Elsa.Events.Channels;

/// <summary>
/// The dispatch strategy the background worker drains with: awaited and in order like
/// <see cref="SequentialProcessingStrategy"/>, but every handler is isolated. A failing handler is
/// logged and dispatch moves on to the next one, so one broken subscriber cannot starve the others of
/// a deferred event. This is the framework's "log/handle gracefully and continue" dispatcher failure
/// policy (§2.6.6); <see cref="SequentialProcessingStrategy"/> is "throw immediately".
/// </summary>
internal sealed class IsolatingProcessingStrategy(ILogger logger) : IEventPublishingStrategy
{
    /// <summary>
    /// Awaits each handler in order, logging failures and continuing with the remaining handlers.
    /// Propagates cancellation exceptions when the dispatch token is cancelled.
    /// </summary>
    public async Task PublishAsync(IEventStrategyContext context)
    {
        foreach (var handler in context.Handlers)
        {
            try
            {
                await handler.Invoke(context.EventContext);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                // Host shutdown: stop dispatching rather than log every remaining handler as failed.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Event handler {HandlerType} failed for {EventType}; dispatch continues with the remaining handlers",
                    handler.GetType().Name,
                    context.EventContext.Event.GetType().Name
                );
            }
        }
    }
}

using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Events.Strategies;

/// <summary>
/// Fire-and-forget. Enqueues the event context on <see cref="IEventChannel"/> and returns
/// immediately; the <see cref="Channels.BackgroundEventPublisher"/> hosted-task drains the
/// channel and dispatches each event via <see cref="SequentialProcessingStrategy"/>. This
/// strategy owns its own resilience — the background worker catches and logs handler failures
/// so a fire-and-forget publish can never break the rest of the app.
/// </summary>
public sealed class BackgroundProcessingStrategy : IEventPublishingStrategy
{
    public async Task PublishAsync(IEventStrategyContext context)
    {
        var channel = context.ServiceProvider.GetRequiredService<IEventChannel>();
        await channel.Writer.WriteAsync(context.EventContext, context.CancellationToken);
    }
}

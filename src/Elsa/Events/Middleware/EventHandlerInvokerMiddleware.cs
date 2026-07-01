using Elsa.Events.Core.Contracts;
using Elsa.Events.Contexts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Events.Middleware;

/// <summary>
/// Resolves all <c>IEventHandler&lt;TEvent&gt;</c> for the event type then hands off to the
/// chosen <see cref="IEventPublishingStrategy"/>.
/// </summary>
public sealed class EventHandlerInvokerMiddleware(EventMiddlewareDelegate next)
    : IEventMiddleware
{
    public async ValueTask Invoke(IEventContext context)
    {
        var eventType = context.Event.GetType();
        var handlerType = typeof(IEventHandler<>).MakeGenericType(eventType);

        // Resolve only the handlers registered for this event type. Resolving the non-generic marker set
        // instead would force every event handler in the container to be constructed on every publish.
        var handlers = context.ServiceProvider.GetServices(handlerType)
            .Cast<IEventHandler>()
            .DistinctBy(h => h.GetType())
            .ToArray();

        var strategyContext = new EventStrategyContext(
            context,
            handlers,
            context.ServiceProvider,
            context.CancellationToken
        );

        await context.Strategy.PublishAsync(strategyContext);

        await next(context);
    }
}

using Elsa.Events.Core.Contracts;
using Elsa.Events.Contexts;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace Elsa.Events.Middleware;

/// <summary>
/// Resolves all <c>IEventHandler&lt;TEvent&gt;</c> for the event type then hands off to the
/// chosen <see cref="IEventPublishingStrategy"/>.
/// </summary>
public sealed class EventHandlerInvokerMiddleware(EventMiddlewareDelegate next)
    : IEventMiddleware
{
    // The closed IEventHandler<TEvent> type is stable per event type; cache it so a hot publish path
    // doesn't reconstruct it via reflection on every call.
    private static readonly ConcurrentDictionary<Type, Type> HandlerTypeCache = new();

    public async ValueTask Invoke(IEventContext context)
    {
        var eventType = context.Event.GetType();
        var handlerType = HandlerTypeCache.GetOrAdd(eventType, static t => typeof(IEventHandler<>).MakeGenericType(t));

        // Resolve only the handlers registered for this event type. Resolving the non-generic marker set
        // instead would force every event handler in the container to be constructed on every publish.
        // Registration is idempotent per (event, handler) pair, so DistinctBy is defence-in-depth against a
        // hand-rolled duplicate registration, not the correctness guarantee.
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

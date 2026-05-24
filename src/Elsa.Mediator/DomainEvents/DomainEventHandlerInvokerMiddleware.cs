using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Mediator.DomainEvents;

/// <summary>
/// A middleware component that invokes request handlers.
/// </summary>
public sealed class DomainEventHandlerInvokerMiddleware(DomainEventMiddlewareDelegate next) 
    : IDomainEventMiddleware
{
    /// <inheritdoc />
    public async ValueTask Invoke(IDomainEventContext context)
    {
        // Find all handlers for the specified domain event
        var @event = context.Event;
        var eventType = @event.GetType();
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handlers = context
            .ServiceProvider
            .GetServices(handlerType)
            .ToArray();

        if (handlers.Length == 0)
            return;

        foreach(var handler in handlers)
        {
            var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<>.Handle))!;        

            var task = (Task)handleMethod.Invoke(handler, [@event, context.CancellationToken])!;
            await task;

            // Invoke next middleware.
            await next(context);
        }
    }
}
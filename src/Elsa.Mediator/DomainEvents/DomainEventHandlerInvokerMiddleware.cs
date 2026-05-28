using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Middleware;

namespace Elsa.Mediator.DomainEvents;

/// <summary>
/// Invokes the single handler currently bound on <see cref="IDomainEventContext.CurrentHandler"/>
/// by <see cref="DomainEventHandlerIteratorMiddleware"/>. Per-handler iteration lives
/// upstream in the iterator; per-handler exception shielding lives between the iterator
/// and this invoker (<see cref="DomainEventExceptionShieldingMiddleware"/>).
/// </summary>
public sealed class DomainEventHandlerInvokerMiddleware(DomainEventMiddlewareDelegate next)
    : IDomainEventMiddleware
{
    /// <inheritdoc />
    public async ValueTask Invoke(IDomainEventContext context)
    {
        var handler = context.CurrentHandler;

        if (handler is null)
        {
            await next(context);
            return;
        }

        var @event = context.Event;
        var eventType = @event.GetType();
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<>.Handle))!;

        var task = (Task)handleMethod.Invoke(handler, [@event, context.CancellationToken])!;
        await task;

        await next(context);
    }
}

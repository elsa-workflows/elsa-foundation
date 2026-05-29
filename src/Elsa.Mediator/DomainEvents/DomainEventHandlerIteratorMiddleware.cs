using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Mediator.DomainEvents;

/// <summary>
/// Resolves the handlers registered for the published domain event and iterates them,
/// setting <see cref="IDomainEventContext.CurrentHandler"/> before invoking the rest
/// of the pipeline once per handler.
///
/// Per framework §2.6.1, the dispatcher MUST dispatch every registered handler. This
/// middleware enforces completeness; the per-handler shielding (so a single handler's
/// exception cannot abort the iteration) lives in
/// <see cref="DomainEventExceptionShieldingMiddleware"/>, and the actual handler
/// invocation lives in <see cref="DomainEventHandlerInvokerMiddleware"/>.
/// </summary>
/// <remarks>
/// Handlers are resolved via the non-generic <see cref="IDomainEventHandler"/> marker
/// (which is the surface <c>AddDomainEventHandlersFrom</c> / <c>AddDomainEventHandler</c>
/// register against) and filtered by the closed-generic
/// <c>IDomainEventHandler&lt;TEvent&gt;</c> using <see cref="Type.IsInstanceOfType"/>.
/// This mirrors the request- and command-dispatcher patterns
/// (<c>RequestHandlerInvokerMiddleware</c>, <c>CommandHandlerInvokerMiddleware</c>) so all
/// three mediator dispatchers share one registration story.
/// </remarks>
public sealed class DomainEventHandlerIteratorMiddleware(DomainEventMiddlewareDelegate next)
    : IDomainEventMiddleware
{
    /// <inheritdoc />
    public async ValueTask Invoke(IDomainEventContext context)
    {
        var eventType = context.Event.GetType();
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);

        var handlers = context.ServiceProvider
            .GetServices<IDomainEventHandler>()
            .DistinctBy(x => x.GetType())
            .Where(handlerType.IsInstanceOfType)
            .ToArray();

        if (handlers.Length == 0)
            return;

        try
        {
            foreach (var handler in handlers)
            {
                context.CurrentHandler = handler;
                await next(context);
            }
        }
        finally
        {
            context.CurrentHandler = null;
        }
    }
}

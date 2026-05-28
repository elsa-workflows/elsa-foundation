using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Middleware;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace Elsa.Mediator.DomainEvents;

/// <summary>
/// Catches any exception thrown by the per-handler portion of the pipeline (the
/// <see cref="DomainEventHandlerInvokerMiddleware"/>) and logs it with diagnostic
/// context (handler type + event type). Exceptions are NOT propagated upstream.
///
/// Framework §2.6.1 — "subscriber MUST NEVER break the publisher." Cross-domain
/// coupling exists at the contract level (the event's shape); cross-domain
/// *failure* coupling does not. This middleware is the default mechanism that
/// enforces the rule. An engineer who deliberately wants different semantics
/// (aggregate-throw, fail-fast, etc.) may swap or remove this middleware when
/// composing a custom <see cref="IDomainEventPipeline"/>, but the framework
/// ships the fail-isolated default.
/// </summary>
[UsedImplicitly]
public sealed class DomainEventExceptionShieldingMiddleware(
    DomainEventMiddlewareDelegate next,
    ILogger<DomainEventExceptionShieldingMiddleware> logger) : IDomainEventMiddleware
{
    /// <inheritdoc />
    public async ValueTask Invoke(IDomainEventContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var handlerName = context.CurrentHandler?.GetType().FullName ?? "<unknown>";
            var eventName = context.Event.GetType().FullName ?? "<unknown>";

            logger.LogError(
                ex,
                "Domain-event handler '{HandlerType}' threw while handling '{EventType}'. The exception was caught by the dispatcher; remaining handlers will still run. (Framework §2.6.1 — subscribers must never break the publisher.)",
                handlerName,
                eventName);
        }
    }
}

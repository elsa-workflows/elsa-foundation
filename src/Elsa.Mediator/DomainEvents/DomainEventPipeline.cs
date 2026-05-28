using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Extensions;
using Elsa.Mediator.Core.Middleware;

namespace Elsa.Mediator.DomainEvents;

/// <inheritdoc />
/// <summary>
/// Initializes a new instance of the <see cref="DomainEventPipeline"/> class.
///
/// The default pipeline composition is:
///
/// 1. <see cref="DomainEventHandlerIteratorMiddleware"/> — resolves the handlers
///    for the event and iterates them, setting CurrentHandler per invocation
///    (framework §2.6.1 completeness rule — every registered handler is dispatched).
/// 2. <see cref="DomainEventExceptionShieldingMiddleware"/> — wraps each per-handler
///    invocation in try/catch/log/swallow so a misbehaving subscriber cannot break
///    the publisher (framework §2.6.1 "subscriber MUST NEVER break publisher" rule).
/// 3. <see cref="DomainEventHandlerInvokerMiddleware"/> — invokes the current handler
///    against the event.
///
/// An engineer who wants different semantics (aggregate-throw, fail-fast, etc.)
/// can compose a custom pipeline via <see cref="Setup"/>.
/// </summary>
public sealed class DomainEventPipeline(IServiceProvider serviceProvider) : IDomainEventPipeline
{
    private DomainEventMiddlewareDelegate? _pipeline;

    /// <inheritdoc />
    public DomainEventMiddlewareDelegate Pipeline => _pipeline ??= CreateDefaultPipeline();

    /// <inheritdoc />
    public DomainEventMiddlewareDelegate Setup(Action<IDomainEventPipelineBuilder>? setup = default)
    {
        var builder = new DomainEventPipelineBuilder(serviceProvider);
        setup?.Invoke(builder);
        _pipeline = builder.Build();
        return _pipeline;
    }

    /// <inheritdoc />
    public async Task Execute(IDomainEventContext context) => await Pipeline(context);

    private DomainEventMiddlewareDelegate CreateDefaultPipeline() => Setup(
        x => x
            .UseMiddleware<DomainEventHandlerIteratorMiddleware>()
            .UseMiddleware<DomainEventExceptionShieldingMiddleware>()
            .UseMiddleware<DomainEventHandlerInvokerMiddleware>()
    );
}

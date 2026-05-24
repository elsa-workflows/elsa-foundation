using Elsa.Mediator.Commands;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Extensions;
using Elsa.Mediator.Core.Middleware;
using Elsa.Mediator.Requests;

namespace Elsa.Mediator.DomainEvents;

/// <inheritdoc />
/// <summary>
/// Initializes a new instance of the <see cref="RequestPipeline"/> class.
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
        x => x.UseMiddleware<DomainEventHandlerInvokerMiddleware>()
    );
}
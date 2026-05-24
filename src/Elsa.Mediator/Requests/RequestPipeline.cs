using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Extensions;
using Elsa.Mediator.Core.Middleware;
using Elsa.Mediator.Requests;

namespace Elsa.Mediator.Commands;

/// <inheritdoc />
/// <summary>
/// Initializes a new instance of the <see cref="RequestPipeline"/> class.
/// </summary>
public sealed class RequestPipeline(IServiceProvider serviceProvider) : IRequestPipeline
{
    private RequestMiddlewareDelegate? _pipeline;

    /// <inheritdoc />
    public RequestMiddlewareDelegate Pipeline => _pipeline ??= CreateDefaultPipeline();

    /// <inheritdoc />
    public RequestMiddlewareDelegate Setup(Action<IRequestPipelineBuilder>? setup = default)
    {
        var builder = new RequestPipelineBuilder(serviceProvider);
        setup?.Invoke(builder);
        _pipeline = builder.Build();
        return _pipeline;
    }

    /// <inheritdoc />
    public async Task Execute(IRequestContext context) => await Pipeline(context);

    private RequestMiddlewareDelegate CreateDefaultPipeline() => Setup(
        x => x.UseMiddleware<RequestHandlerInvokerMiddleware>()
    );
}
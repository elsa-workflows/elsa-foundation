using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Services;
using Elsa.Mediator.Middleware;
using Elsa.Pipelines.Core.Contracts;
using Elsa.Pipelines.Core.Extensions;

namespace Elsa.Mediator.Requests;

/// <summary>
/// The request dispatch pipeline: handler invoker only (no logging middleware, preserving the prior
/// request behaviour). A thin default composition over the shared <see cref="MessagePipeline"/>.
/// </summary>
public sealed class RequestPipeline(IServiceProvider serviceProvider) : MessagePipeline(serviceProvider)
{
    /// <inheritdoc />
    protected override PipelineDelegate<IMessageContext> CreateDefaultPipeline() => Setup(builder => builder
        .UseMiddleware<IMessageContext, HandlerInvokerMiddleware>());
}

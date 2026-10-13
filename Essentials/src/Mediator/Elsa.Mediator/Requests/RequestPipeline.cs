using Elsa.Mediator.Middleware;
using Elsa.Mediator.Pipeline;
using Elsa.Pipelines.Core.Contracts;
using Elsa.Pipelines.Core.Extensions;

namespace Elsa.Mediator.Requests;

/// <summary>
/// The request dispatch pipeline: handler invoker only (no logging middleware, preserving the prior
/// request behaviour). A thin default composition over the shared <see cref="MessagePipeline"/>.
/// </summary>
internal sealed class RequestPipeline(IServiceProvider serviceProvider) : MessagePipeline(serviceProvider)
{
    /// <inheritdoc />
    protected override PipelineDelegate<IMessageContext> CreateDefaultPipeline() => Setup(builder => builder
        .UseMiddleware<IMessageContext, HandlerInvokerMiddleware>());
}

using Elsa.Mediator.Middleware;
using Elsa.Mediator.Pipeline;
using Elsa.Pipelines.Core.Contracts;
using Elsa.Pipelines.Core.Extensions;

namespace Elsa.Mediator.Commands;

/// <summary>
/// The command dispatch pipeline: handler invoker followed by command logging. A thin default
/// composition over the shared <see cref="MessagePipeline"/>.
/// </summary>
internal sealed class CommandPipeline(IServiceProvider serviceProvider) : MessagePipeline(serviceProvider)
{
    /// <inheritdoc />
    protected override PipelineDelegate<IMessageContext> CreateDefaultPipeline() => Setup(builder => builder
        .UseMiddleware<IMessageContext, HandlerInvokerMiddleware>()
        .UseMiddleware<IMessageContext, LoggingMiddleware>());
}

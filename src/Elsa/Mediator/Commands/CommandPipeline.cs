using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Services;
using Elsa.Mediator.Middleware;
using Elsa.Pipelines.Core.Contracts;
using Elsa.Pipelines.Core.Extensions;

namespace Elsa.Mediator.Commands;

/// <summary>
/// The command dispatch pipeline: handler invoker followed by command logging. A thin default
/// composition over the shared <see cref="MessagePipeline"/>.
/// </summary>
public sealed class CommandPipeline(IServiceProvider serviceProvider) : MessagePipeline(serviceProvider)
{
    /// <inheritdoc />
    protected override PipelineDelegate<IMessageContext> CreateDefaultPipeline() => Setup(builder => builder
        .UseMiddleware<IMessageContext, HandlerInvokerMiddleware>()
        .UseMiddleware<IMessageContext, LoggingMiddleware>());
}

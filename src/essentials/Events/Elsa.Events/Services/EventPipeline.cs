using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Events.Middleware;
using Elsa.Pipelines.Core;
using Elsa.Pipelines.Core.Contracts;

namespace Elsa.Events.Services;

/// <inheritdoc />
public sealed class EventPipeline(IServiceProvider serviceProvider) : IEventPipeline
{
    private volatile PipelineDelegate<IEventContext>? _pipeline;

    public PipelineDelegate<IEventContext> Pipeline => _pipeline ??= CreateDefaultPipeline();

    public PipelineDelegate<IEventContext> Setup(Action<IPipelineBuilder<IEventContext>> setup)
    {
        var builder = new PipelineBuilder<IEventContext>(serviceProvider);
        setup(builder);
        _pipeline = builder.Build();
        return _pipeline;
    }

    public Task ExecuteAsync(IEventContext context) => Pipeline(context).AsTask();

    // Default pipeline = handler invoker only. No exception-shielding middleware: a default
    // (Sequential) publish is synchronous and CAN break the caller. Resilience is the
    // Background strategy's responsibility, not a global middleware concern.
    private PipelineDelegate<IEventContext> CreateDefaultPipeline() => Setup(builder =>
    {
        builder.UseMiddleware<EventHandlerInvokerMiddleware>();
    });
}

using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Events.Core.Middleware;
using Elsa.Events.Middleware;

namespace Elsa.Events.Services;

/// <inheritdoc />
public sealed class EventPipeline(IServiceProvider serviceProvider) : IEventPipeline
{
    private volatile EventMiddlewareDelegate? _pipeline;

    public EventMiddlewareDelegate Pipeline => _pipeline ??= CreateDefaultPipeline();

    public EventMiddlewareDelegate Setup(Action<IEventPipelineBuilder>? setup = null)
    {
        var builder = new EventPipelineBuilder(serviceProvider);
        setup?.Invoke(builder);
        _pipeline = builder.Build();
        return _pipeline;
    }

    public Task ExecuteAsync(IEventContext context) => Pipeline(context).AsTask();

    // Default pipeline = handler invoker only. No exception-shielding middleware: a default
    // (Sequential) publish is synchronous and CAN break the caller. Resilience is the
    // Background strategy's responsibility, not a global middleware concern.
    private EventMiddlewareDelegate CreateDefaultPipeline() => Setup(builder =>
    {
        builder.UseMiddleware<EventHandlerInvokerMiddleware>();
    });
}

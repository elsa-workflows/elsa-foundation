using Elsa.Events.Core.Contracts;

namespace Elsa.Events.Core.Middleware;

/// <inheritdoc />
public sealed class EventPipelineBuilder : IEventPipelineBuilder
{
    private readonly List<Func<EventMiddlewareDelegate, EventMiddlewareDelegate>> _components = [];

    public EventPipelineBuilder(IServiceProvider serviceProvider)
    {
        ApplicationServices = serviceProvider;
    }

    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

    public IServiceProvider ApplicationServices { get; }

    public IEventPipelineBuilder Use(Func<EventMiddlewareDelegate, EventMiddlewareDelegate> middleware)
    {
        _components.Add(middleware);
        return this;
    }

    public EventMiddlewareDelegate Build()
    {
        EventMiddlewareDelegate pipeline = _ => new ValueTask();

        for (var i = _components.Count - 1; i >= 0; i--)
            pipeline = _components[i](pipeline);

        return pipeline;
    }
}

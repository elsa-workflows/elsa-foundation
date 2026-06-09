namespace Elsa.Events.Core.Contracts;

/// <summary>Fluent builder for composing the event middleware pipeline.</summary>
public interface IEventPipelineBuilder
{
    IDictionary<string, object?> Properties { get; }

    IServiceProvider ApplicationServices { get; }

    IEventPipelineBuilder Use(Func<EventMiddlewareDelegate, EventMiddlewareDelegate> middleware);

    EventMiddlewareDelegate Build();
}

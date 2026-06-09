namespace Elsa.Events.Core.Contracts;

/// <summary>The composed event dispatch pipeline. One instance per process (singleton).</summary>
public interface IEventPipeline
{
    /// <summary>Replaces the pipeline composition with the supplied setup.</summary>
    EventMiddlewareDelegate Setup(Action<IEventPipelineBuilder> setup);

    /// <summary>The composed delegate. Lazy-initialised to the default pipeline on first read.</summary>
    EventMiddlewareDelegate Pipeline { get; }

    /// <summary>Dispatches the given context through the composed pipeline.</summary>
    Task ExecuteAsync(IEventContext context);
}

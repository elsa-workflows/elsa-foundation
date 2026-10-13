using Elsa.Pipelines.Core.Contracts;

namespace Elsa.Events.Core.Contracts;

/// <summary>The composed event dispatch pipeline. One instance per process (singleton).</summary>
public interface IEventPipeline
{
    /// <summary>
    /// Composes the supplied middleware in front of handler dispatch, replacing any earlier composition. The handler
    /// invoker always ends the pipeline, so a setup cannot drop dispatch; a middleware that does not call its next
    /// delegate stops the event before any handler sees it.
    /// </summary>
    PipelineDelegate<IEventContext> Setup(Action<IPipelineBuilder<IEventContext>> setup);

    /// <summary>The composed delegate. Lazy-initialized to the default pipeline on first read.</summary>
    PipelineDelegate<IEventContext> Pipeline { get; }

    /// <summary>Dispatches the given context through the composed pipeline.</summary>
    Task ExecuteAsync(IEventContext context);
}

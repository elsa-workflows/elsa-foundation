using Elsa.Pipelines.Core.Contracts;

namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// The composed mediator dispatch pipeline. A single implementation backs both the command and the
/// request sender; each sender constructs the pipeline with its own default middleware.
/// </summary>
public interface IMessagePipeline
{
    /// <summary>
    /// (Re)composes the pipeline from the supplied setup and returns the composed delegate.
    /// Replace semantics: each call builds a fresh pipeline from a new builder.
    /// </summary>
    PipelineDelegate<IMessageContext> Setup(Action<IPipelineBuilder<IMessageContext>>? setup = null);

    /// <summary>The composed delegate. Lazily initialised to the default pipeline on first read.</summary>
    PipelineDelegate<IMessageContext> Pipeline { get; }

    /// <summary>Dispatches the given context through the composed pipeline.</summary>
    Task Execute(IMessageContext context);
}

using Elsa.Pipelines.Core;
using Elsa.Pipelines.Core.Contracts;

namespace Elsa.Mediator.Pipeline;

/// <summary>
/// The single mediator pipeline implementation. Commands and requests differ only in their default
/// middleware composition, supplied by the concrete subclass; everything else — the shared
/// <see cref="PipelineBuilder{TContext}"/>, replace-semantic <see cref="Setup"/>, and execution — is
/// common. Replace semantics: each <see cref="Setup"/> call composes a fresh pipeline from a new
/// builder (see the Mediator EXTENSION_POINTS.md).
/// </summary>
internal abstract class MessagePipeline(IServiceProvider serviceProvider)
{
    private volatile PipelineDelegate<IMessageContext>? _pipeline;

    /// <summary>The composed delegate. Lazily initialized to the default pipeline on first read.</summary>
    public PipelineDelegate<IMessageContext> Pipeline => _pipeline ??= CreateDefaultPipeline();

    /// <summary>
    /// (Re)composes the pipeline from the supplied setup and returns the composed delegate.
    /// Replace semantics: each call builds a fresh pipeline from a new builder.
    /// </summary>
    public PipelineDelegate<IMessageContext> Setup(Action<IPipelineBuilder<IMessageContext>>? setup = null)
    {
        var builder = new PipelineBuilder<IMessageContext>(serviceProvider);
        setup?.Invoke(builder);
        return _pipeline = builder.Build();
    }

    /// <summary>Dispatches the given context through the composed pipeline.</summary>
    public Task Execute(IMessageContext context) => Pipeline(context).AsTask();

    /// <summary>Composes the default pipeline for this message kind.</summary>
    protected abstract PipelineDelegate<IMessageContext> CreateDefaultPipeline();
}

using Elsa.Mediator.Core.Contracts;
using Elsa.Pipelines.Core;
using Elsa.Pipelines.Core.Contracts;

namespace Elsa.Mediator.Core.Services;

/// <summary>
/// The single mediator pipeline implementation. Commands and requests differ only in their default
/// middleware composition, supplied by the concrete subclass; everything else — the shared
/// <see cref="PipelineBuilder{TContext}"/>, replace-semantic <see cref="Setup"/>, and execution — is
/// common. Replace semantics: each <see cref="Setup"/> call composes a fresh pipeline from a new
/// builder (see the Mediator EXTENSION_POINTS.md).
/// </summary>
public abstract class MessagePipeline(IServiceProvider serviceProvider) : IMessagePipeline
{
    private volatile PipelineDelegate<IMessageContext>? _pipeline;

    /// <inheritdoc />
    public PipelineDelegate<IMessageContext> Pipeline => _pipeline ??= CreateDefaultPipeline();

    /// <inheritdoc />
    public PipelineDelegate<IMessageContext> Setup(Action<IPipelineBuilder<IMessageContext>>? setup = null)
    {
        var builder = new PipelineBuilder<IMessageContext>(serviceProvider);
        setup?.Invoke(builder);
        return _pipeline = builder.Build();
    }

    /// <inheritdoc />
    public Task Execute(IMessageContext context) => Pipeline(context).AsTask();

    /// <summary>Composes the default pipeline for this message kind.</summary>
    protected abstract PipelineDelegate<IMessageContext> CreateDefaultPipeline();
}

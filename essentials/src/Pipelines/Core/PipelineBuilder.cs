using Elsa.Pipelines.Core.Contracts;

namespace Elsa.Pipelines.Core;

/// <summary>
/// The single pipeline builder implementation shared by every per-message pipeline. Composes
/// middleware components into one <see cref="PipelineDelegate{TContext}"/> by folding them in
/// reverse registration order, so the first-registered middleware runs first.
/// </summary>
/// <remarks>
/// Setup semantic: this builder <em>accumulates</em> the components handed to it. Callers that want
/// <em>replace</em> semantics (a fresh composition per <c>Setup()</c>) construct a new builder per
/// call — which is the one documented semantic across the codebase's pipelines. See each pipeline's
/// <c>Setup</c> and the domain <c>EXTENSION_POINTS.md</c>.
/// </remarks>
/// <typeparam name="TContext">The message context flowing through the pipeline.</typeparam>
public sealed class PipelineBuilder<TContext>(IServiceProvider applicationServices) : IPipelineBuilder<TContext>
{
    private readonly List<Func<PipelineDelegate<TContext>, PipelineDelegate<TContext>>> _components = [];

    /// <inheritdoc />
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

    /// <inheritdoc />
    public IServiceProvider ApplicationServices { get; } = applicationServices;

    /// <inheritdoc />
    public IPipelineBuilder<TContext> Use(Func<PipelineDelegate<TContext>, PipelineDelegate<TContext>> middleware)
    {
        _components.Add(middleware);
        return this;
    }

    /// <inheritdoc />
    public PipelineDelegate<TContext> Build()
    {
        PipelineDelegate<TContext> pipeline = _ => default;

        for (var i = _components.Count - 1; i >= 0; i--)
            pipeline = _components[i](pipeline);

        return pipeline;
    }
}

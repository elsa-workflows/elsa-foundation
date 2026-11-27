namespace Elsa.Pipelines.Core.Contracts;

/// <summary>
/// The single, message-agnostic pipeline builder shape. Commands, requests, and events all compose
/// their middleware through this one builder (closed over their own context type), replacing the
/// three copy-pasted, diverged per-message builders.
/// </summary>
/// <typeparam name="TContext">The message context flowing through the pipeline.</typeparam>
public interface IPipelineBuilder<TContext>
{
    /// <summary>Gets a property bag for sharing data between middleware components.</summary>
    IDictionary<string, object?> Properties { get; }

    /// <summary>Gets the service provider used to activate middleware.</summary>
    IServiceProvider ApplicationServices { get; }

    /// <summary>Appends a middleware component to the pipeline.</summary>
    IPipelineBuilder<TContext> Use(Func<PipelineDelegate<TContext>, PipelineDelegate<TContext>> middleware);

    /// <summary>Builds the composed pipeline delegate.</summary>
    PipelineDelegate<TContext> Build();
}

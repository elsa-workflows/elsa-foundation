
namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// Represents a pipeline for processing commands. The pipeline is responsible for orchestrating the execution of registered middleware in sequence.
/// </summary>
public interface ICommandPipeline
{
    /// <summary>
    /// Invokes the pipeline.
    /// </summary>
    /// <param name="context">The command context.</param>
    Task Execute(ICommandContext context);
}

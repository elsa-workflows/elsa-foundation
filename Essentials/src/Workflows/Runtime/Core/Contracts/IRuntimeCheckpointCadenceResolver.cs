using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Resolves the effective checkpoint cadence for a workflow execution (ADR 0032 R5). The runtime reads the authored
/// cadence off the pinned executable and applies it over the host default; it never reads a Design store (§E2.2).
/// </summary>
public interface IRuntimeCheckpointCadenceResolver
{
    /// <summary>
    /// Resolves the cadence for the run the <paramref name="envelope"/> targets. Prefers the run's own per-run stamp
    /// (present from the workflow-started checkpoint onward); on the start drain resolves from the pinned executable.
    /// </summary>
    ValueTask<ResolvedCheckpointCadence> ResolveAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the effective cadence from an already-loaded executable's authored cadence over the host default. Used
    /// at start to compute the per-run stamp when the executable is already in hand.
    /// </summary>
    ResolvedCheckpointCadence Resolve(WorkflowExecutable? executable);
}

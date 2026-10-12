using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Resolves the persistence mode of a workflow execution. The runtime reads the workflow's chosen mode off the pinned
/// executable and applies it over the host default; it never reads a Design store.
/// </summary>
public interface IRuntimePersistenceModeResolver
{
    /// <summary>
    /// Resolves the mode for the run the <paramref name="envelope"/> targets. Prefers the mode the run recorded when it
    /// started; on the first drain resolves from the pinned executable.
    /// </summary>
    ValueTask<ResolvedPersistenceMode> ResolveAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the mode from an already-loaded executable's chosen mode over the host default. Used at start to record
    /// the run's mode when the executable is already in hand.
    /// </summary>
    PersistenceMode Resolve(WorkflowExecutable? executable);
}

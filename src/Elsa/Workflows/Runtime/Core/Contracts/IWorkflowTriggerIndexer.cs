using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Writes the durable trigger index for a published workflow executable (W7, E3-1). Invoked from the publish
/// flow after the executable is stored; it replaces the artifact's prior trigger bindings with the current set
/// so a republished version's triggers supersede the previous version's.
/// </summary>
/// <remarks>
/// Per the publish-consistency ruling, indexing runs inside the publish flow and a failure fails the publish:
/// there is no silently unindexed published trigger. Because a republish deletes-then-writes the artifact's
/// bindings and the executable save is itself an upsert, retrying a failed publish heals any partial write.
/// </remarks>
public interface IWorkflowTriggerIndexer
{
    /// <summary>Replaces the artifact's trigger bindings with the ones extracted from <paramref name="executable"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="executable"/> is <see langword="null"/>.</exception>
    /// <exception cref="WorkflowTriggerPreflightException">Trigger preflight fails before index mutation.</exception>
    ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default);
}

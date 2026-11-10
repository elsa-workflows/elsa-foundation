using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts.Alterations;

/// <summary>
/// Actor-side quiescence proof for migration. Implementations must inspect every durable execution lane that can
/// mutate or resume an execution; a positive proof is consumed immediately by the same single-writer checkpoint.
/// </summary>
public interface IWorkflowMigrationQuiescenceProbe
{
    /// <summary>Reports whether anything other than the caller could still execute, deliver to or resume the run.</summary>
    /// <param name="workflowExecutionId">The run to prove quiescent.</param>
    /// <param name="currentActorFence">
    /// The fence of the ownership lease the caller holds on the run. The proof exempts that lease, unless it stands
    /// in for an owner the caller took the run from. In the composition the runtime registers, an alteration job
    /// holds such a lease (<c>IRuntimeExecutionOwnershipContextAccessor.Current</c>), so a handler that passes null
    /// is told that its own lease is an active owner.
    /// </param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    ValueTask<WorkflowMigrationQuiescenceReport> ProbeAsync(
        string workflowExecutionId,
        RuntimeExecutionFence? currentActorFence,
        CancellationToken cancellationToken = default);
}

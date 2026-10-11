using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts.Alterations;

/// <summary>
/// Actor-side quiescence proof for migration. Implementations must inspect every durable execution lane that can
/// mutate or resume an execution; a positive proof is consumed immediately by the same single-writer checkpoint.
/// </summary>
public interface IWorkflowMigrationQuiescenceProbe
{
    ValueTask<WorkflowMigrationQuiescenceReport> ProbeAsync(
        string workflowExecutionId,
        RuntimeExecutionFence? currentActorFence,
        CancellationToken cancellationToken = default);
}

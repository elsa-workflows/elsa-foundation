using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Single-provider runtime replacement port for trusted test-owned capture frame access.</summary>
public interface IRuntimeTestContextFrameAccess
{
    ValueTask<RuntimeTestCaptureTarget?> FindCaptureTargetAsync(string workflowExecutionId,
        string activityExecutionId, string scopeId, CancellationToken cancellationToken = default);

    WorkflowExecutionState ApplyWrites(WorkflowExecutionState workflow, IReadOnlyCollection<RuntimeTestFrameWrite> writes);
}

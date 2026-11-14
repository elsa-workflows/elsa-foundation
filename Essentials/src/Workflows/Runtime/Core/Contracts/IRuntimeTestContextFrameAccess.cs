using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Runtime integration contract for trusted test-owned capture frame access.</summary>
/// <remarks>
/// Public for the Activities Runtime completion adapters. The current process-private interpreter
/// requires the canonical runtime owner; replacement providers are not qualified.
/// </remarks>
public interface IRuntimeTestContextFrameAccess
{
    ValueTask<RuntimeTestCaptureTarget?> FindCaptureTargetAsync(string workflowExecutionId,
        string activityExecutionId, string scopeId, CancellationToken cancellationToken = default);

    WorkflowExecutionState ApplyWrites(WorkflowExecutionState workflow, IReadOnlyCollection<RuntimeTestFrameWrite> writes);
}

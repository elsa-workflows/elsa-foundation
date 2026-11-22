using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Provider-owned, atomic deletion of a finished execution and its run-owned inspection state.</summary>
/// <remarks>
/// Implementations must check the candidate's concurrency token and terminal status within the same
/// ownership/checkpoint fence as cleanup. The guarantee is against Runtime's canonical checkpoint writers and
/// retained scheduler-continuation delivery. Independent custom writes to raw sidecar stores must honor the same
/// ownership protocol; bypassing checkpoint ownership is not a supported concurrent write path. Shared artifacts, trigger definitions, linked dispatches and
/// operational replay/delivery receipts are outside this history-deletion boundary. A failed operation
/// must not report success or release the execution's retention root before cleanup has completed.
/// Custom or mixed persistence compositions that cannot provide this guarantee must remain unsupported.
/// </remarks>
public interface IWorkflowExecutionDeletionStore
{
    bool IsSupported { get; }

    ValueTask<WorkflowExecutionDeletionCandidate?> ReadAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    ValueTask<WorkflowExecutionDeletionResult> TryDeleteTerminalAsync(WorkflowExecutionDeletionCandidate candidate, CancellationToken cancellationToken = default);
}

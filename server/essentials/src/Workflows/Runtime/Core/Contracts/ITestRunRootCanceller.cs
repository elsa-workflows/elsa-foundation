using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Requests ordinary runtime cancellation for one root owned by an existing TestRun scope.</summary>
public interface ITestRunRootCanceller
{
    /// <summary>
    /// Enqueues a deterministic, at-least-once root cancel when the execution exists and is nonterminal.
    /// Returns null when no execution was admitted or the execution is already terminal.
    /// </summary>
    ValueTask<WorkflowExecutionCommandDispatchResult?> RequestCancelAsync(
        string testRunId,
        string workflowExecutionId,
        WorkflowExecutionPartition partition,
        CancellationToken cancellationToken = default);
}

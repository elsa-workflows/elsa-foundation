using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Routes TestRun root cancellation through the execution's ordinary single-writer mailbox.</summary>
internal sealed class TestRunRootCanceller(
    IWorkflowExecutionStateStore executions,
    IWorkflowExecutionActorProvider actors,
    IRuntimeExecutionIdGenerator runtimeIds,
    TimeProvider timeProvider) : ITestRunRootCanceller
{
    public async ValueTask<WorkflowExecutionCommandDispatchResult?> RequestCancelAsync(
        string testRunId,
        string workflowExecutionId,
        WorkflowExecutionPartition partition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(partition);

        var execution = await executions.FindAsync(workflowExecutionId, cancellationToken);
        if (execution is null || execution.Status.IsTerminal())
            return null;

        var now = timeProvider.GetUtcNow();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime.testRunId"] = testRunId,
            ["runtime.cancellationSource"] = "workflow-designer-test-run"
        };
        var command = new WorkflowExecutionCommand(
            runtimeIds.NewWorkflowExecutionCommandId(),
            workflowExecutionId,
            WorkflowExecutionCommandKind.Cancel,
            now,
            Payload: null,
            Metadata: metadata);
        var envelope = new WorkflowExecutionCommandEnvelope(
            runtimeIds.NewWorkflowExecutionCommandEnvelopeId(),
            workflowExecutionId,
            command,
            $"workflow-test-run:{testRunId}:cancel",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            now,
            metadata: metadata,
            partition: partition);
        var actor = await actors.GetAgentAsync(new(
            workflowExecutionId,
            WorkflowExecutionActorActivationReason.ControlPlaneCommand,
            now,
            "workflow-designer-test-run",
            WorkflowExecutionActorCapabilities.None,
            metadata,
            partition), cancellationToken);
        var result = await actor.EnqueueAsync(envelope, cancellationToken);
        if (result.Status == WorkflowExecutionCommandDispatchStatus.Rejected)
            throw new InvalidOperationException(result.Reason ?? "The workflow root cancellation request was rejected.");
        return result;
    }
}

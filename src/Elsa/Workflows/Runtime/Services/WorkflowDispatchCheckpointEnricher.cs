using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Mirrors a child terminal checkpoint into its linked detached-dispatch lifecycle.</summary>
public sealed class WorkflowDispatchCheckpointEnricher(IWorkflowDispatchStore store) : IRuntimeCheckpointCommitEnricher
{
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();

        // The base store predates #678 and remains a supported third-party extension point. Providers that have not
        // adopted the additive query capability continue checkpointing without terminal projection.
        if (store is not IWorkflowDispatchQueryStore queryStore)
            return commit;

        if (commit.StateChanges.WorkflowExecution?.State is not { } workflowExecution ||
            !workflowExecution.Status.IsTerminal())
            return commit;

        var targetStatus = workflowExecution.Status switch
        {
            WorkflowExecutionStatus.Completed => WorkflowDispatchStatus.Completed,
            WorkflowExecutionStatus.Faulted => WorkflowDispatchStatus.Faulted,
            WorkflowExecutionStatus.Cancelled => WorkflowDispatchStatus.Cancelled,
            _ => throw new InvalidOperationException($"Unsupported terminal workflow execution status '{workflowExecution.Status}'.")
        };
        var records = (await queryStore.QueryAsync(new WorkflowDispatchQuery(
                childWorkflowExecutionId: workflowExecution.WorkflowExecutionId), cancellationToken))
            .ToDictionary(record => record.DispatchId, StringComparer.Ordinal);

        if (records.Count == 0)
            return commit;

        // Checkpoint occurrence is the replay-stable timestamp. Always emit the identical terminal projection even
        // when the store already contains it, so the commit fingerprint is unchanged on replay.
        var terminalAt = commit.Checkpoint.OccurredAt;
        var merged = commit.StateChanges.WorkflowDispatches
            .ToDictionary(change => change.StateId, StringComparer.Ordinal);
        foreach (var record in records.Values)
        {
            var terminal = record.TransitionTo(targetStatus, terminalAt);
            var change = new RuntimeStateChange<WorkflowDispatchRecord>(
                terminal.DispatchId,
                RuntimeStateChangeOperation.Upsert,
                terminal,
                new Dictionary<string, string>());
            if (merged.TryGetValue(terminal.DispatchId, out var existing) &&
                !WorkflowDispatchLifecycle.RecordsEqual(existing.State, terminal))
                throw new InvalidOperationException($"Workflow dispatch '{terminal.DispatchId}' has conflicting checkpoint lifecycle projections.");
            merged[terminal.DispatchId] = change;
        }

        var stateChanges = commit.StateChanges.WithWorkflowDispatches(merged.Values.ToArray());
        return commit with { StateChanges = stateChanges };
    }
}

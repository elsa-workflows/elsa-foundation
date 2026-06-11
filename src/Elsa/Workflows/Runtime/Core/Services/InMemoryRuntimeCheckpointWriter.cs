using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryRuntimeCheckpointWriter : IRuntimeCheckpointWriter
{
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Dictionary<string, RuntimeCheckpointWriteRecord> _writes = new(StringComparer.Ordinal);
    private readonly IWorkflowExecutionStateStore? _workflowExecutionStateStore;
    private readonly IActivityExecutionStateStore? _activityExecutionStateStore;

    public InMemoryRuntimeCheckpointWriter(
        IWorkflowExecutionStateStore? workflowExecutionStateStore = null,
        IActivityExecutionStateStore? activityExecutionStateStore = null)
    {
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _activityExecutionStateStore = activityExecutionStateStore;
    }

    public async ValueTask WriteAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            lock (_syncRoot)
            {
                if (_writes.ContainsKey(commit.CommitId))
                    return;
            }

            ValidateWorkflowExecutionStateChange(commit.StateChanges.WorkflowExecution);
            ValidateActivityExecutionStateChanges(commit);
            await ApplyWorkflowExecutionStateChangeAsync(commit.StateChanges.WorkflowExecution, cancellationToken);
            await ApplyActivityExecutionStateChangesAsync(commit.StateChanges.ActivityExecutions, cancellationToken);

            lock (_syncRoot)
            {
                _writes.Add(commit.CommitId, new RuntimeCheckpointWriteRecord(commit, decision));
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public IReadOnlyCollection<RuntimeCheckpointWriteRecord> ListWrites()
    {
        lock (_syncRoot)
        {
            return _writes.Values.ToArray();
        }
    }

    private async ValueTask ApplyWorkflowExecutionStateChangeAsync(
        RuntimeStateChange<WorkflowExecutionState>? stateChange,
        CancellationToken cancellationToken)
    {
        if (_workflowExecutionStateStore is null || stateChange is null)
            return;

        await _workflowExecutionStateStore.SaveAsync(stateChange.State, cancellationToken);
    }

    private async ValueTask ApplyActivityExecutionStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_activityExecutionStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
            await _activityExecutionStateStore.SaveAsync(stateChange.State, cancellationToken);
    }

    private void ValidateWorkflowExecutionStateChange(RuntimeStateChange<WorkflowExecutionState>? stateChange)
    {
        if (_workflowExecutionStateStore is null || stateChange is null)
            return;

        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The in-memory checkpoint writer can only project workflow execution state '{RuntimeStateChangeOperation.Upsert}' changes.");

        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Workflow execution state change StateId must match WorkflowExecutionState.WorkflowExecutionId.");
    }

    private void ValidateActivityExecutionStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_activityExecutionStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.ActivityExecutions)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint writer can only project activity execution state '{RuntimeStateChangeOperation.Upsert}' changes.");

            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.Execution.ActivityExecutionId))
                throw new InvalidOperationException("Activity execution state change StateId must match ActivityExecution.ActivityExecutionId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.Execution.WorkflowExecutionId))
                throw new InvalidOperationException("Activity execution state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }
}

public sealed record RuntimeCheckpointWriteRecord(
    RuntimeCheckpointCommit Commit,
    RuntimeCheckpointPersistenceDecision Decision);

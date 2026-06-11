using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryRuntimeCheckpointWriter : IRuntimeCheckpointWriter
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, RuntimeCheckpointWriteRecord> _writes = new(StringComparer.Ordinal);
    private readonly IWorkflowExecutionStateStore? _workflowExecutionStateStore;

    public InMemoryRuntimeCheckpointWriter()
    {
    }

    public InMemoryRuntimeCheckpointWriter(IWorkflowExecutionStateStore workflowExecutionStateStore)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);

        _workflowExecutionStateStore = workflowExecutionStateStore;
    }

    public async ValueTask WriteAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateWorkflowExecutionStateChange(commit.StateChanges.WorkflowExecution);

        var recorded = false;
        lock (_syncRoot)
        {
            recorded = _writes.TryAdd(commit.CommitId, new RuntimeCheckpointWriteRecord(commit, decision));
        }

        if (recorded)
            await ApplyWorkflowExecutionStateChangeAsync(commit.StateChanges.WorkflowExecution, cancellationToken);
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

    private void ValidateWorkflowExecutionStateChange(RuntimeStateChange<WorkflowExecutionState>? stateChange)
    {
        if (_workflowExecutionStateStore is null || stateChange is null)
            return;

        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The in-memory checkpoint writer can only project workflow execution state '{RuntimeStateChangeOperation.Upsert}' changes.");

        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Workflow execution state change StateId must match WorkflowExecutionState.WorkflowExecutionId.");
    }
}

public sealed record RuntimeCheckpointWriteRecord(
    RuntimeCheckpointCommit Commit,
    RuntimeCheckpointPersistenceDecision Decision);

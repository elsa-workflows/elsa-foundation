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
    private readonly IBookmarkStateStore? _bookmarkStateStore;
    private readonly IDurableValueStateStore? _durableValueStateStore;
    private readonly IIncidentStateStore? _incidentStateStore;
    private readonly IOperationalStateStore? _operationalStateStore;
    private readonly ISchedulerStateStore? _schedulerStateStore;

    public InMemoryRuntimeCheckpointWriter(
        IWorkflowExecutionStateStore? workflowExecutionStateStore = null,
        IActivityExecutionStateStore? activityExecutionStateStore = null,
        IBookmarkStateStore? bookmarkStateStore = null)
        : this(workflowExecutionStateStore, activityExecutionStateStore, bookmarkStateStore, null)
    {
    }

    public InMemoryRuntimeCheckpointWriter(
        IWorkflowExecutionStateStore? workflowExecutionStateStore,
        IActivityExecutionStateStore? activityExecutionStateStore,
        IBookmarkStateStore? bookmarkStateStore,
        IDurableValueStateStore? durableValueStateStore)
        : this(workflowExecutionStateStore, activityExecutionStateStore, bookmarkStateStore, durableValueStateStore, null)
    {
    }

    public InMemoryRuntimeCheckpointWriter(
        IWorkflowExecutionStateStore? workflowExecutionStateStore,
        IActivityExecutionStateStore? activityExecutionStateStore,
        IBookmarkStateStore? bookmarkStateStore,
        IDurableValueStateStore? durableValueStateStore,
        IIncidentStateStore? incidentStateStore)
        : this(workflowExecutionStateStore, activityExecutionStateStore, bookmarkStateStore, durableValueStateStore, incidentStateStore, null)
    {
    }

    public InMemoryRuntimeCheckpointWriter(
        IWorkflowExecutionStateStore? workflowExecutionStateStore,
        IActivityExecutionStateStore? activityExecutionStateStore,
        IBookmarkStateStore? bookmarkStateStore,
        IDurableValueStateStore? durableValueStateStore,
        IIncidentStateStore? incidentStateStore,
        IOperationalStateStore? operationalStateStore)
        : this(workflowExecutionStateStore, activityExecutionStateStore, bookmarkStateStore, durableValueStateStore, incidentStateStore, operationalStateStore, null)
    {
    }

    public InMemoryRuntimeCheckpointWriter(
        IWorkflowExecutionStateStore? workflowExecutionStateStore,
        IActivityExecutionStateStore? activityExecutionStateStore,
        IBookmarkStateStore? bookmarkStateStore,
        IDurableValueStateStore? durableValueStateStore,
        IIncidentStateStore? incidentStateStore,
        IOperationalStateStore? operationalStateStore,
        ISchedulerStateStore? schedulerStateStore)
    {
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _bookmarkStateStore = bookmarkStateStore;
        _durableValueStateStore = durableValueStateStore;
        _incidentStateStore = incidentStateStore;
        _operationalStateStore = operationalStateStore;
        _schedulerStateStore = schedulerStateStore;
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
            ValidateSchedulerStateChange(commit);
            ValidateActivityExecutionStateChanges(commit);
            ValidateBookmarkStateChanges(commit);
            ValidateDurableValueStateChanges(commit);
            ValidateIncidentStateChanges(commit);
            ValidateOperationalStateChanges(commit);
            await ApplyWorkflowExecutionStateChangeAsync(commit.StateChanges.WorkflowExecution, cancellationToken);
            await ApplySchedulerStateChangeAsync(commit.StateChanges.Scheduler, cancellationToken);
            await ApplyActivityExecutionStateChangesAsync(commit.StateChanges.ActivityExecutions, cancellationToken);
            await ApplyBookmarkStateChangesAsync(commit.StateChanges.Bookmarks, cancellationToken);
            await ApplyDurableValueStateChangesAsync(commit.StateChanges.DurableValues, cancellationToken);
            await ApplyIncidentStateChangesAsync(commit.StateChanges.Incidents, cancellationToken);
            await ApplyOperationalStateChangesAsync(commit.StateChanges.Operational, cancellationToken);

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

    private async ValueTask ApplySchedulerStateChangeAsync(
        RuntimeStateChange<SchedulerState>? stateChange,
        CancellationToken cancellationToken)
    {
        if (_schedulerStateStore is null || stateChange is null)
            return;

        await _schedulerStateStore.SaveAsync(stateChange.State, cancellationToken);
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

    private async ValueTask ApplyBookmarkStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_bookmarkStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Delete)
            {
                await _bookmarkStateStore.DeleteAsync(
                    stateChange.State.WorkflowExecutionId,
                    stateChange.State.BookmarkId,
                    cancellationToken);
                continue;
            }

            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _bookmarkStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected bookmark state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyDurableValueStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_durableValueStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Delete)
            {
                await _durableValueStateStore.DeleteAsync(
                    stateChange.State.WorkflowExecutionId,
                    stateChange.State.DurableValueId,
                    cancellationToken);
                continue;
            }

            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _durableValueStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected durable value state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyIncidentStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_incidentStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Append)
            {
                var added = await _incidentStateStore.TryAddAsync(stateChange.State, cancellationToken);
                if (!added)
                    throw new InvalidOperationException($"Incident state '{stateChange.State.IncidentId}' already exists for workflow execution '{stateChange.State.WorkflowExecutionId}'.");

                continue;
            }

            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _incidentStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected incident state change operation '{stateChange.Operation}' reached apply phase.");
        }
    }

    private async ValueTask ApplyOperationalStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<OperationalState>> stateChanges,
        CancellationToken cancellationToken)
    {
        if (_operationalStateStore is null)
            return;

        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Upsert)
            {
                await _operationalStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Unexpected operational state change operation '{stateChange.Operation}' reached apply phase.");
        }
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

    private void ValidateSchedulerStateChange(RuntimeCheckpointCommit commit)
    {
        if (_schedulerStateStore is null || commit.StateChanges.Scheduler is null)
            return;

        var stateChange = commit.StateChanges.Scheduler;

        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The in-memory checkpoint writer can only project scheduler state '{RuntimeStateChangeOperation.Upsert}' changes.");

        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Scheduler state change StateId must match SchedulerState.WorkflowExecutionId.");

        if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Scheduler state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
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

    private void ValidateBookmarkStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_bookmarkStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.Bookmarks)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Upsert and not RuntimeStateChangeOperation.Delete)
                throw new InvalidOperationException($"The in-memory checkpoint writer can only project bookmark state '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");

            // RuntimeCheckpointStateChangeSet also enforces this; the writer repeats it to keep the projection boundary self-validating.
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.BookmarkId))
                throw new InvalidOperationException("Bookmark state change StateId must match BookmarkState.BookmarkId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Bookmark state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private void ValidateDurableValueStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_durableValueStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.DurableValues)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Upsert and not RuntimeStateChangeOperation.Delete)
                throw new InvalidOperationException($"The in-memory checkpoint writer can only project durable value state '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");

            // RuntimeCheckpointStateChangeSet also enforces this; the writer repeats it to keep the projection boundary self-validating.
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.DurableValueId))
                throw new InvalidOperationException("Durable value state change StateId must match DurableValueState.DurableValueId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Durable value state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private void ValidateIncidentStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_incidentStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.Incidents)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Append and not RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint writer can only project incident state '{RuntimeStateChangeOperation.Append}' or '{RuntimeStateChangeOperation.Upsert}' changes.");

            // RuntimeCheckpointStateChangeSet also enforces this; the writer repeats it to keep the projection boundary self-validating.
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.IncidentId))
                throw new InvalidOperationException("Incident state change StateId must match IncidentState.IncidentId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Incident state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private void ValidateOperationalStateChanges(RuntimeCheckpointCommit commit)
    {
        if (_operationalStateStore is null)
            return;

        foreach (var stateChange in commit.StateChanges.Operational)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The in-memory checkpoint writer can only project operational state '{RuntimeStateChangeOperation.Upsert}' changes.");

            // RuntimeCheckpointStateChangeSet also enforces this; the writer repeats it to keep the projection boundary self-validating.
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.OperationalStateId))
                throw new InvalidOperationException("Operational state change StateId must match OperationalState.OperationalStateId.");

            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Operational state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }
}

public sealed record RuntimeCheckpointWriteRecord(
    RuntimeCheckpointCommit Commit,
    RuntimeCheckpointPersistenceDecision Decision);

using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IRuntimeCheckpointWriter"/> for the Groundwork bridge.
/// </summary>
/// <remarks>
/// <para>
/// Groundwork's document store is autonomous per operation in the preview packages: a single
/// <c>SaveAsync</c>/<c>DeleteAsync</c> commits independently and there is no cross-document
/// transaction. The runtime checkpoint contract does not require cross-store atomicity — the
/// reference <see cref="InMemoryRuntimeCheckpointWriter"/> applies the seam stores sequentially and
/// relies on idempotent redelivery keyed by <see cref="RuntimeCheckpointCommit.CommitId"/>. This
/// writer follows the same model but makes it durable:
/// </para>
/// <list type="bullet">
/// <item>The commit is applied through the host-selected seam stores (Groundwork-backed when this
/// bridge is composed). Upserts and deletes are naturally idempotent; the incident append is treated
/// idempotently so a redelivered commit does not fail.</item>
/// <item>A marker document keyed by <c>CommitId</c> is written last. On entry, an existing marker
/// short-circuits the commit, giving restart-safe dedup that the in-memory writer's in-process set
/// cannot. A crash before the marker is written leaves a partially-applied commit that is completed by
/// re-applying the same commit (at-least-once), because every apply step is idempotent.</item>
/// </list>
/// </remarks>
public sealed class GroundworkRuntimeCheckpointWriter : IRuntimeCheckpointWriter
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly IDocumentStore _commitLedger;
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly ISchedulerStateStore _schedulerStateStore;
    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly IBookmarkStateStore _bookmarkStateStore;
    private readonly IDurableValueStateStore _durableValueStateStore;
    private readonly IIncidentStateStore _incidentStateStore;
    private readonly IOperationalStateStore _operationalStateStore;

    public GroundworkRuntimeCheckpointWriter(
        IDocumentStore commitLedger,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        ISchedulerStateStore schedulerStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IBookmarkStateStore bookmarkStateStore,
        IDurableValueStateStore durableValueStateStore,
        IIncidentStateStore incidentStateStore,
        IOperationalStateStore operationalStateStore)
    {
        ArgumentNullException.ThrowIfNull(commitLedger);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(schedulerStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(bookmarkStateStore);
        ArgumentNullException.ThrowIfNull(durableValueStateStore);
        ArgumentNullException.ThrowIfNull(incidentStateStore);
        ArgumentNullException.ThrowIfNull(operationalStateStore);
        _commitLedger = commitLedger;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _schedulerStateStore = schedulerStateStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _bookmarkStateStore = bookmarkStateStore;
        _durableValueStateStore = durableValueStateStore;
        _incidentStateStore = incidentStateStore;
        _operationalStateStore = operationalStateStore;
    }

    public async ValueTask WriteAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.CommitId);
        cancellationToken.ThrowIfCancellationRequested();

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            if (await IsCommittedAsync(commit.CommitId, cancellationToken))
                return;

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

            await MarkCommittedAsync(commit, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async ValueTask<bool> IsCommittedAsync(string commitId, CancellationToken cancellationToken)
    {
        var envelope = await _commitLedger.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
            commitId,
            cancellationToken);
        return envelope is not null;
    }

    private async ValueTask MarkCommittedAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        var marker = new CheckpointCommitMarker(commit.CommitId, commit.WorkflowExecutionId, commit.Checkpoint.OccurredAt);
        var content = JsonSerializer.Serialize(marker, GroundworkRuntimeJson.Options);
        await _commitLedger.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
                commit.CommitId,
                ElsaRuntimeStorageManifest.SchemaVersion,
                content),
            cancellationToken);
    }

    private async ValueTask ApplyWorkflowExecutionStateChangeAsync(
        RuntimeStateChange<WorkflowExecutionState>? stateChange,
        CancellationToken cancellationToken)
    {
        if (stateChange is null)
            return;
        await _workflowExecutionStateStore.SaveAsync(stateChange.State, cancellationToken);
    }

    private async ValueTask ApplySchedulerStateChangeAsync(
        RuntimeStateChange<SchedulerState>? stateChange,
        CancellationToken cancellationToken)
    {
        if (stateChange is null)
            return;
        await _schedulerStateStore.SaveAsync(stateChange.State, cancellationToken);
    }

    private async ValueTask ApplyActivityExecutionStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
            await _activityExecutionStateStore.SaveAsync(stateChange.State, cancellationToken);
    }

    private async ValueTask ApplyBookmarkStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Delete)
            {
                await _bookmarkStateStore.DeleteAsync(stateChange.State.WorkflowExecutionId, stateChange.State.BookmarkId, cancellationToken);
                continue;
            }
            await _bookmarkStateStore.SaveAsync(stateChange.State, cancellationToken);
        }
    }

    private async ValueTask ApplyDurableValueStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
        {
            if (stateChange.Operation == RuntimeStateChangeOperation.Delete)
            {
                await _durableValueStateStore.DeleteAsync(stateChange.State.WorkflowExecutionId, stateChange.State.DurableValueId, cancellationToken);
                continue;
            }
            await _durableValueStateStore.SaveAsync(stateChange.State, cancellationToken);
        }
    }

    private async ValueTask ApplyIncidentStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
        {
            // Append and Upsert both resolve to a durable write. Unlike the in-memory writer, a redelivered
            // Append is not an error: a crash may have applied the incident without writing the commit
            // marker, so re-applying the same commit must be idempotent. TryAdd-then-Save guarantees the
            // incident exists without throwing on a second pass.
            if (stateChange.Operation == RuntimeStateChangeOperation.Append)
            {
                var added = await _incidentStateStore.TryAddAsync(stateChange.State, cancellationToken);
                if (!added)
                    await _incidentStateStore.SaveAsync(stateChange.State, cancellationToken);
                continue;
            }
            await _incidentStateStore.SaveAsync(stateChange.State, cancellationToken);
        }
    }

    private async ValueTask ApplyOperationalStateChangesAsync(
        IReadOnlyCollection<RuntimeStateChange<OperationalState>> stateChanges,
        CancellationToken cancellationToken)
    {
        foreach (var stateChange in stateChanges)
            await _operationalStateStore.SaveAsync(stateChange.State, cancellationToken);
    }

    private static void ValidateWorkflowExecutionStateChange(RuntimeStateChange<WorkflowExecutionState>? stateChange)
    {
        if (stateChange is null)
            return;
        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The Groundwork checkpoint writer can only project workflow execution state '{RuntimeStateChangeOperation.Upsert}' changes.");
        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Workflow execution state change StateId must match WorkflowExecutionState.WorkflowExecutionId.");
    }

    private static void ValidateSchedulerStateChange(RuntimeCheckpointCommit commit)
    {
        if (commit.StateChanges.Scheduler is null)
            return;
        var stateChange = commit.StateChanges.Scheduler;
        if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
            throw new InvalidOperationException($"The Groundwork checkpoint writer can only project scheduler state '{RuntimeStateChangeOperation.Upsert}' changes.");
        if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Scheduler state change StateId must match SchedulerState.WorkflowExecutionId.");
        if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
            throw new InvalidOperationException("Scheduler state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
    }

    private static void ValidateActivityExecutionStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.ActivityExecutions)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project activity execution state '{RuntimeStateChangeOperation.Upsert}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.Execution.ActivityExecutionId))
                throw new InvalidOperationException("Activity execution state change StateId must match ActivityExecution.ActivityExecutionId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.Execution.WorkflowExecutionId))
                throw new InvalidOperationException("Activity execution state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private static void ValidateBookmarkStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.Bookmarks)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Upsert and not RuntimeStateChangeOperation.Delete)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project bookmark state '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.BookmarkId))
                throw new InvalidOperationException("Bookmark state change StateId must match BookmarkState.BookmarkId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Bookmark state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private static void ValidateDurableValueStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.DurableValues)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Upsert and not RuntimeStateChangeOperation.Delete)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project durable value state '{RuntimeStateChangeOperation.Upsert}' or '{RuntimeStateChangeOperation.Delete}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.DurableValueId))
                throw new InvalidOperationException("Durable value state change StateId must match DurableValueState.DurableValueId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Durable value state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private static void ValidateIncidentStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.Incidents)
        {
            if (stateChange.Operation is not RuntimeStateChangeOperation.Append and not RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project incident state '{RuntimeStateChangeOperation.Append}' or '{RuntimeStateChangeOperation.Upsert}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.IncidentId))
                throw new InvalidOperationException("Incident state change StateId must match IncidentState.IncidentId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Incident state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private static void ValidateOperationalStateChanges(RuntimeCheckpointCommit commit)
    {
        foreach (var stateChange in commit.StateChanges.Operational)
        {
            if (stateChange.Operation != RuntimeStateChangeOperation.Upsert)
                throw new InvalidOperationException($"The Groundwork checkpoint writer can only project operational state '{RuntimeStateChangeOperation.Upsert}' changes.");
            if (!StringComparer.Ordinal.Equals(stateChange.StateId, stateChange.State.OperationalStateId))
                throw new InvalidOperationException("Operational state change StateId must match OperationalState.OperationalStateId.");
            if (!StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, stateChange.State.WorkflowExecutionId))
                throw new InvalidOperationException("Operational state change WorkflowExecutionId must match the checkpoint workflow execution ID.");
        }
    }

    private sealed record CheckpointCommitMarker(string CommitId, string WorkflowExecutionId, DateTimeOffset OccurredAt);
}

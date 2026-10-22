using System.Runtime.CompilerServices;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Values;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>Atomically removes terminal execution history from the coherent in-memory Runtime composition.</summary>
public sealed class InMemoryWorkflowExecutionDeletionStore : IWorkflowExecutionDeletionStore
{
    private static readonly ConditionalWeakTable<WorkflowExecutionState, CandidateToken> CandidateTokens = new();

    private readonly InMemoryRuntimeCheckpointStoreState _checkpointState;
    private readonly IRuntimeCheckpointCommitStore _checkpointStore;
    private readonly IActivityExecutionInspectionStore _inspectionReader;
    private readonly IActivityExecutionHierarchyStore _hierarchyReader;
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly IExecutionLivenessStateStore _executionLivenessStateStore;
    private readonly InMemoryWorkflowExecutionStateStore? _inMemoryWorkflowExecutionStateStore;
    private readonly InMemoryActivityExecutionStateStore? _activityExecutionStateStore;
    private readonly InMemoryActivityExecutionInspectionStore? _activityExecutionInspectionStore;
    private readonly RuntimeInMemoryActivityExecutionHierarchyStore? _activityExecutionHierarchyStore;
    private readonly InMemoryBookmarkStateStore? _bookmarkStateStore;
    private readonly InMemoryDurableValueStateStore? _durableValueStateStore;
    private readonly InMemorySchedulerStateStore? _schedulerStateStore;
    private readonly InMemoryDurableTimerStore? _durableTimerStore;
    private readonly InMemoryWorkflowSchedulerWorkQueue? _schedulerWorkQueue;
    private readonly InMemoryIncidentStateStore? _incidentStateStore;
    private readonly InMemoryWorkflowSchedulerPoisonStore? _schedulerPoisonStore;
    private readonly InMemoryWorkflowHoldStateStore? _workflowHoldStateStore;

    public InMemoryWorkflowExecutionDeletionStore(
        InMemoryRuntimeCheckpointStoreState checkpointState,
        IRuntimeCheckpointCommitStore checkpointStore,
        IActivityExecutionInspectionStore inspectionReader,
        IActivityExecutionHierarchyStore hierarchyReader,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IExecutionLivenessStateStore executionLivenessStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IActivityExecutionInspectionWriter activityExecutionInspectionWriter,
        IActivityExecutionHierarchyWriter activityExecutionHierarchyWriter,
        IBookmarkStateStore bookmarkStateStore,
        IDurableValueStateStore durableValueStateStore,
        ISchedulerStateStore schedulerStateStore,
        IDurableTimerStore durableTimerStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IIncidentStateStore incidentStateStore,
        IWorkflowSchedulerPoisonStore schedulerPoisonStore,
        IWorkflowHoldStateStore workflowHoldStateStore)
    {
        ArgumentNullException.ThrowIfNull(checkpointState);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(executionLivenessStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionInspectionWriter);
        ArgumentNullException.ThrowIfNull(activityExecutionHierarchyWriter);
        ArgumentNullException.ThrowIfNull(bookmarkStateStore);
        ArgumentNullException.ThrowIfNull(durableValueStateStore);
        ArgumentNullException.ThrowIfNull(schedulerStateStore);
        ArgumentNullException.ThrowIfNull(durableTimerStore);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(incidentStateStore);
        ArgumentNullException.ThrowIfNull(schedulerPoisonStore);
        ArgumentNullException.ThrowIfNull(workflowHoldStateStore);

        _checkpointState = checkpointState;
        _checkpointStore = checkpointStore;
        _inspectionReader = inspectionReader;
        _hierarchyReader = hierarchyReader;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _executionLivenessStateStore = executionLivenessStateStore;
        _inMemoryWorkflowExecutionStateStore = workflowExecutionStateStore as InMemoryWorkflowExecutionStateStore;
        _activityExecutionStateStore = activityExecutionStateStore as InMemoryActivityExecutionStateStore;
        _activityExecutionInspectionStore = activityExecutionInspectionWriter as InMemoryActivityExecutionInspectionStore;
        _activityExecutionHierarchyStore = activityExecutionHierarchyWriter as RuntimeInMemoryActivityExecutionHierarchyStore;
        _bookmarkStateStore = bookmarkStateStore as InMemoryBookmarkStateStore;
        _durableValueStateStore = durableValueStateStore as InMemoryDurableValueStateStore;
        _schedulerStateStore = schedulerStateStore as InMemorySchedulerStateStore;
        _durableTimerStore = durableTimerStore as InMemoryDurableTimerStore;
        _schedulerWorkQueue = schedulerWorkQueue as InMemoryWorkflowSchedulerWorkQueue;
        _incidentStateStore = incidentStateStore as InMemoryIncidentStateStore;
        _schedulerPoisonStore = schedulerPoisonStore as InMemoryWorkflowSchedulerPoisonStore;
        _workflowHoldStateStore = workflowHoldStateStore as InMemoryWorkflowHoldStateStore;
    }

    public bool IsSupported =>
        _inMemoryWorkflowExecutionStateStore is { } executionStore &&
        ReferenceEquals(executionStore.CheckpointState, _checkpointState) &&
        _checkpointStore is InMemoryRuntimeCheckpointCommitStore checkpointStore && ReferenceEquals(checkpointStore.State, _checkpointState) &&
        ReferenceEquals(_inspectionReader, _activityExecutionInspectionStore) &&
        ReferenceEquals(_hierarchyReader, _activityExecutionHierarchyStore) &&
        ReferenceEquals(executionStore.ExecutionLivenessStateStore, _executionLivenessStateStore) &&
        _executionLivenessStateStore is InMemoryExecutionLivenessStateStore &&
        _activityExecutionStateStore is not null &&
        _activityExecutionInspectionStore is not null &&
        _activityExecutionHierarchyStore is not null &&
        _bookmarkStateStore is not null &&
        _durableValueStateStore is not null &&
        _schedulerStateStore is not null &&
        _durableTimerStore is not null &&
        _schedulerWorkQueue is not null &&
        _incidentStateStore is not null &&
        _schedulerPoisonStore is not null &&
        _workflowHoldStateStore is not null;

    public async ValueTask<WorkflowExecutionDeletionCandidate?> ReadAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var state = await _workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken);
        return state is null ? null : new WorkflowExecutionDeletionCandidate(state, GetConcurrencyToken(state));
    }

    public ValueTask<WorkflowExecutionDeletionResult> TryDeleteTerminalAsync(
        WorkflowExecutionDeletionCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(candidate.State);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSupported)
            return ValueTask.FromResult(WorkflowExecutionDeletionResult.Unsupported);

        var tokenIsValid = StringComparer.Ordinal.Equals(
            candidate.ConcurrencyToken,
            GetConcurrencyToken(candidate.State));
        return _inMemoryWorkflowExecutionStateStore!.TryDeleteTerminalAsync(
            candidate,
            tokenIsValid,
            () => DeleteRunOwnedState(candidate.State.WorkflowExecutionId),
            cancellationToken);
    }

    private void DeleteRunOwnedState(string workflowExecutionId)
    {
        // These are the runtime-owned per-execution projections. Operational receipts, shared definitions, triggers,
        // dispatch records, test scopes and independent execution evidence deliberately remain outside this boundary.
        // The owner has already passed cancellation before acquiring the mutation gates. Keep this phase synchronous
        // so cancellation cannot leave a partially purged execution behind.
        _activityExecutionStateStore!.DeleteForWorkflowExecution(workflowExecutionId);
        _activityExecutionInspectionStore!.DeleteForWorkflowExecution(workflowExecutionId);
        _activityExecutionHierarchyStore!.DeleteForWorkflowExecution(workflowExecutionId);
        _bookmarkStateStore!.DeleteForWorkflowExecution(workflowExecutionId);
        _durableValueStateStore!.DeleteForWorkflowExecution(workflowExecutionId);
        _schedulerStateStore!.DeleteForWorkflowExecution(workflowExecutionId);
        _durableTimerStore!.DeleteForWorkflowExecution(workflowExecutionId);
        _schedulerWorkQueue!.DeleteForWorkflowExecution(workflowExecutionId);
        _incidentStateStore!.DeleteForWorkflowExecution(workflowExecutionId);
        _schedulerPoisonStore!.DeleteForWorkflowExecution(workflowExecutionId);
        _workflowHoldStateStore!.DeleteForWorkflowExecution(workflowExecutionId);
    }

    private static string GetConcurrencyToken(WorkflowExecutionState state) =>
        CandidateTokens.GetValue(state, static _ => new CandidateToken(Guid.NewGuid().ToString("N"))).Value;

    private sealed record CandidateToken(string Value);
}

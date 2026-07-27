using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Core.Contracts.Alterations;

/// <summary>
/// Trusted handler contract used by the alteration actor after descriptor resolution. Implementations may inspect and
/// update the projected state and stage opaque, checkpoint-bound changes, but must not persist or dispatch work while
/// preflighting.
/// </summary>
public interface IWorkflowAlterationPreflightHandler : IWorkflowAlterationHandler
{
    ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(
        WorkflowAlterationPreflightContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves one exact stable envelope identity to one trusted preflight handler.</summary>
public interface IWorkflowAlterationHandlerResolver
{
    IWorkflowAlterationPreflightHandler Resolve(WorkflowAlterationEnvelope envelope);
}

/// <summary>Processes an <see cref="WorkflowExecutionCommandKind.AlterWorkflow"/> command inside its single-writer actor.</summary>
public interface IWorkflowAlterationActorCommandExecutor
{
    ValueTask ExecuteAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>Mutable in-memory projection shared by every preflight in one target job.</summary>
public interface IWorkflowAlterationProjectedState
{
    bool TryGetOriginal<TState>(out TState? state) where TState : class;
    TState GetRequiredOriginal<TState>() where TState : class;
    bool TryGet<TState>(out TState? state) where TState : class;
    TState GetRequired<TState>() where TState : class;
    void Set<TState>(TState state) where TState : class;
}

/// <summary>Collects staged changes that the single alteration checkpoint will apply after complete preflight.</summary>
public interface IWorkflowAlterationStagingWorkspace
{
    IReadOnlyList<IWorkflowAlterationStagedChange> StagedChanges { get; }
    void Stage(IWorkflowAlterationStagedChange change);
}

/// <summary>One opaque, handler-owned state change that may be applied only by the alteration checkpoint collaborator.</summary>
public interface IWorkflowAlterationStagedChange
{
    string ChangeKey { get; }
}

/// <summary>
/// A staged change that can contribute runtime state to the alteration checkpoint. The handler still performs no I/O:
/// it supplies a deterministic change to a builder owned by the actor-side checkpoint writer.
/// </summary>
public interface IWorkflowAlterationRuntimeCheckpointStagedChange : IWorkflowAlterationStagedChange
{
    void Apply(WorkflowAlterationRuntimeCheckpointStateBuilder builder);
}

/// <summary>
/// Narrow mutable builder for the workflow-root portion of an alteration checkpoint. Additional runtime state families
/// are deliberately added here only when a built-in alteration proves the corresponding atomic staging requirement.
/// </summary>
public sealed class WorkflowAlterationRuntimeCheckpointStateBuilder(WorkflowExecutionState workflowExecution)
{
    private readonly Dictionary<string, RuntimePostCommitIntent> _postCommitIntents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeStateChange<ActivityExecutionState>> _activityExecutions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeStateChange<ActivityExecutionInspectionProjection>> _activityExecutionInspections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivityScopeCleanupRequest> _activityScopeCleanups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkflowDispatchCancellationRequest> _workflowDispatchCancellations = new(StringComparer.Ordinal);

    public WorkflowExecutionState WorkflowExecution { get; private set; } = workflowExecution ?? throw new ArgumentNullException(nameof(workflowExecution));

    public void SetWorkflowExecution(WorkflowExecutionState workflowExecution)
    {
        ArgumentNullException.ThrowIfNull(workflowExecution);
        if (!StringComparer.Ordinal.Equals(WorkflowExecution.WorkflowExecutionId, workflowExecution.WorkflowExecutionId))
            throw new ArgumentException("An alteration staged change cannot replace a different workflow execution.", nameof(workflowExecution));
        WorkflowExecution = workflowExecution;
    }

    /// <summary>Activity state changes to persist in the same alteration checkpoint.</summary>
    public IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> ActivityExecutions => _activityExecutions.Values;

    /// <summary>Activity inspection projections to persist in the same alteration checkpoint.</summary>
    public IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> ActivityExecutionInspections => _activityExecutionInspections.Values;

    /// <summary>Continuation intents that become durable in the same checkpoint as the alteration terminal evidence.</summary>
    public IReadOnlyCollection<RuntimePostCommitIntent> PostCommitIntents => _postCommitIntents.Values;

    /// <summary>Targeted activity-owned resources to reclaim atomically with a supersession or cancellation.</summary>
    public IReadOnlyCollection<ActivityScopeCleanupRequest> ActivityScopeCleanups => _activityScopeCleanups.Values;

    /// <summary>Provider-resolved child-dispatch cancellations that belong to this workflow checkpoint.</summary>
    public IReadOnlyCollection<WorkflowDispatchCancellationRequest> WorkflowDispatchCancellations => _workflowDispatchCancellations.Values;

    public void AddActivityExecution(RuntimeStateChange<ActivityExecutionState> change) =>
        Add(_activityExecutions, change, "activity execution");

    public void AddActivityExecutionInspection(RuntimeStateChange<ActivityExecutionInspectionProjection> change) =>
        Add(_activityExecutionInspections, change, "activity execution inspection");

    public void AddPostCommitIntent(RuntimePostCommitIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!StringComparer.Ordinal.Equals(intent.WorkflowExecutionId, WorkflowExecution.WorkflowExecutionId))
            throw new ArgumentException("An alteration post-commit intent must target the altered workflow execution.", nameof(intent));
        if (!_postCommitIntents.TryAdd(intent.IntentId, intent))
            throw new InvalidOperationException($"The alteration checkpoint already contains post-commit intent '{intent.IntentId}'.");
    }

    public void AddActivityScopeCleanup(ActivityScopeCleanupRequest cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        if (!StringComparer.Ordinal.Equals(cleanup.WorkflowExecutionId, WorkflowExecution.WorkflowExecutionId))
            throw new ArgumentException("An alteration scope cleanup must target the altered workflow execution.", nameof(cleanup));
        if (!_activityScopeCleanups.TryAdd(cleanup.ExecutionScopeId, cleanup))
            throw new InvalidOperationException($"The alteration checkpoint already contains a cleanup for execution scope '{cleanup.ExecutionScopeId}'.");
    }

    public void AddWorkflowDispatchCancellation(WorkflowDispatchCancellationRequest cancellation)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        if (!StringComparer.Ordinal.Equals(cancellation.ParentWorkflowExecutionId, WorkflowExecution.WorkflowExecutionId))
            throw new ArgumentException("An alteration dispatch cancellation must have the altered workflow as its parent.", nameof(cancellation));
        if (!_workflowDispatchCancellations.TryAdd(cancellation.DispatchId, cancellation))
            throw new InvalidOperationException($"The alteration checkpoint already contains dispatch cancellation '{cancellation.DispatchId}'.");
    }

    private static void Add<TState>(Dictionary<string, RuntimeStateChange<TState>> destination, RuntimeStateChange<TState> change, string stateFamily)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentException.ThrowIfNullOrWhiteSpace(change.StateId);
        if (!destination.TryAdd(change.StateId, change))
            throw new InvalidOperationException($"An alteration staged duplicate {stateFamily} change '{change.StateId}'.");
    }
}

/// <summary>Atomically persists the terminal job evidence with the workflow checkpoint and its staged changes.</summary>
public interface IWorkflowAlterationCheckpointWriter
{
    ValueTask<WorkflowAlterationCheckpointWriteResult> CommitAsync(
        WorkflowAlterationCheckpointWriteRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkflowAlterationCheckpointWriteResult?> FindCommittedAsync(
        string checkpointCommitId,
        CancellationToken cancellationToken = default);
}

/// <summary>Signals that a checkpoint may have committed but its acknowledgement did not reach the worker.</summary>
public sealed class WorkflowAlterationCheckpointAcknowledgementLostException(string checkpointCommitId)
    : Exception($"Checkpoint acknowledgement was lost for '{checkpointCommitId}'.")
{
    public string CheckpointCommitId { get; } = checkpointCommitId;
}

/// <summary>Inputs handed to the one atomic alteration checkpoint after all handler preflights succeed or fail.</summary>
public sealed record WorkflowAlterationCheckpointWriteRequest(
    WorkflowAlterationJobState Job,
    IReadOnlyCollection<IWorkflowAlterationStagedChange> StagedChanges,
    WorkflowAlterationJobTerminalChange TerminalJobChange,
    IWorkflowAlterationProjectedState ProjectedState);

/// <summary>Durable evidence returned by the alteration checkpoint collaborator.</summary>
public sealed record WorkflowAlterationCheckpointWriteResult(
    string CheckpointCommitId,
    WorkflowAlterationJobTerminalChange TerminalJobChange);

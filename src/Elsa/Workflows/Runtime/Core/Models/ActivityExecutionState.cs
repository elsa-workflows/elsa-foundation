using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Durable identity for one concrete execution of one executable activity node.
/// </summary>
public sealed record ActivityExecution(
    string ActivityExecutionId,
    string WorkflowExecutionId,
    string ExecutableNodeId,
    string AuthoredActivityId,
    string ActivityType,
    string ActivityTypeVersion);

/// <summary>
/// Lifecycle and relationship state for an activity execution.
/// </summary>
[method: JsonConstructor]
public sealed record ActivityExecutionState(
    ActivityExecution Execution,
    ActivityExecutionStatus Status,
    string? SubStatus,
    long ExecutionSequence,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? SchedulingActivityExecutionId,
    string? ParentActivityExecutionId,
    string? BranchId,
    string? IterationId,
    ActivitySchedulingProvenance Provenance,
    int? CallStackDepth,
    IReadOnlyCollection<string> BookmarkIds,
    IReadOnlyCollection<string> IncidentIds,
    int FaultCount,
    int AggregateFaultCount,
    IReadOnlyDictionary<string, string> Metadata,
    int DocumentVersion = ActivityExecutionValueFlowDocumentVersions.Current,
    ActivityInvocationContractIdentity? ContractIdentity = null,
    ActivityInputSnapshot? InputSnapshot = null,
    IReadOnlyCollection<ActivityAttempt>? Attempts = null,
    ActivityPrivateState? PrivateState = null,
    ActivityCompletion? Completion = null,
    NormalizedActivityFault? Fault = null,
    IReadOnlyCollection<ActivityTriggerRegistration>? TriggerRegistrations = null,
    IReadOnlyCollection<ActivityTriggerDelivery>? TriggerDeliveries = null,
    ActivityExecutionValueFlowDocumentVersionGuard? ValueFlowCompatibility = null,
    string? ExecutionScopeId = null,
    ActivityExecutionAttemptLineage? Attempt = null)
{
    public string InvocationId => Execution.ActivityExecutionId;

    /// <summary>
    /// The container or iteration frame owned by this structural activity activation, when any.
    /// </summary>
    public VariableFrameState? VariableFrame { get; init; }

    /// <summary>
    /// The loop-iteration frame introduced around this activation by its scheduling parent, when any.
    /// It is separate from <see cref="VariableFrame"/> because a loop body can itself be a container:
    /// the iteration frame is then the lexical parent of the body's container frame.
    /// </summary>
    public VariableFrameState? IterationVariableFrame { get; init; }

    /// <summary>Typed scheduler-carried values used to activate <see cref="IterationVariableFrame"/>.</summary>
    public LoopIterationScopeRequest? IterationFrameRequest { get; init; }

    public void EnsureValueFlowCompatible()
    {
        if (DocumentVersion != ActivityExecutionValueFlowDocumentVersions.Current)
            throw new InvalidOperationException($"Activity invocation '{InvocationId}' carries value-flow document version {DocumentVersion}; this runtime requires version {ActivityExecutionValueFlowDocumentVersions.Current}.");

        ValueFlowCompatibility?.EnsureCompatible();
    }

    public ActivityExecutionState(
        ActivityExecution Execution,
        ActivityExecutionStatus Status,
        string? SubStatus,
        DateTimeOffset ScheduledAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        string? SchedulingActivityExecutionId,
        string? ParentActivityExecutionId,
        string? BranchId,
        string? IterationId,
        int? CallStackDepth,
        IReadOnlyCollection<string> BookmarkIds,
        IReadOnlyCollection<string> IncidentIds,
        int FaultCount,
        int AggregateFaultCount,
        IReadOnlyDictionary<string, string> Metadata)
        : this(
            Execution,
            Status,
            SubStatus,
            ExecutionSequence: 0,
            ScheduledAt,
            StartedAt,
            CompletedAt,
            SchedulingActivityExecutionId,
            ParentActivityExecutionId,
            BranchId,
            IterationId,
            ActivitySchedulingProvenance.From(
                Execution.WorkflowExecutionId,
                ParentActivityExecutionId,
                SchedulingActivityExecutionId,
                BranchId,
                IterationId,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: null),
            CallStackDepth,
            BookmarkIds,
            IncidentIds,
            FaultCount,
            AggregateFaultCount,
            Metadata)
    {
    }
}

public enum ActivityExecutionStatus
{
    Scheduled,
    Running,
    Waiting,
    Suspended,
    Completed,
    Faulted,
    Cancelled,
    Recovered
}

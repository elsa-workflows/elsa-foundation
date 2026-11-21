using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>At most the original schedule and terminal owner facts, retained on their existing timer source until acknowledged.</summary>
public sealed record DurableTimerHistoryResponsibility
{
    public DurableTimerHistoryResponsibility(RuntimeHistoryCapturePolicy policy, IReadOnlyList<RuntimeHistoryEvent> pendingEvents, bool retired)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(pendingEvents);
        if (pendingEvents.Count > 2 || pendingEvents.Any(e => e is null) ||
            pendingEvents.Count(e => e.Name == "TimerScheduled") > 1 || pendingEvents.Count(e => e.Name != "TimerScheduled") > 1 ||
            pendingEvents.Select(e => e.HistoryEventId).Distinct(StringComparer.Ordinal).Count() != pendingEvents.Count)
            throw new ArgumentException("A timer retains only its unique schedule and terminal history responsibility.", nameof(pendingEvents));
        foreach (var e in pendingEvents)
        {
            RuntimeHistorySafety.ValidateObservation(new(policy, e));
            if (!DurableTimerHistoryFacts.IsTimerObservation(e.Name) || (!retired && e.Name != "TimerScheduled") ||
                e.WorkflowExecutionId != pendingEvents[0].WorkflowExecutionId || e.ActivityExecutionId != pendingEvents[0].ActivityExecutionId ||
                e.ExecutionScopeId != pendingEvents[0].ExecutionScopeId ||
                e.Metadata["history.timerId"] != pendingEvents[0].Metadata["history.timerId"] ||
                e.Metadata["history.scheduledAt"] != pendingEvents[0].Metadata["history.scheduledAt"])
                throw new ArgumentException("Timer history must retain one source and at most one terminal fact after retirement.", nameof(pendingEvents));
        }
        Policy = policy; PendingEvents = Array.AsReadOnly(pendingEvents.ToArray()); Retired = retired;
    }
    public RuntimeHistoryCapturePolicy Policy { get; }
    public IReadOnlyList<RuntimeHistoryEvent> PendingEvents { get; }
    public bool Retired { get; }
    private readonly DurableTimerNoRecordCompletion? _noRecordCompletion;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DurableTimerNoRecordCompletion? NoRecordCompletion
    {
        get => _noRecordCompletion;
        init
        {
            if (value is not null && (!Retired || PendingEvents.Count != 0))
                throw new ArgumentException("A no-record receipt requires a retired timer without remaining event responsibility.");
            _noRecordCompletion = value;
        }
    }
}

/// <summary>Compact timer-owner idempotency proof; it carries no retained event content or user data.</summary>
public sealed record DurableTimerNoRecordCompletion
{
    [JsonConstructor]
    public DurableTimerNoRecordCompletion(string checkpointId, DateTimeOffset completedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        if (checkpointId.Length > 1024) throw new ArgumentException("Completion identity exceeds its bound.", nameof(checkpointId));
        CheckpointId = checkpointId; CompletedAt = completedAt;
    }
    public string CheckpointId { get; }
    public DateTimeOffset CompletedAt { get; }
}

/// <summary>Shared shape check. The owning provider separately fences and checks native/head absence atomically.</summary>
public static class RuntimeHistoryNoRecordCompletion
{
    public static WorkflowExecutionState Validate(RuntimeCheckpointCommit completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (completion.ExpectedFence is null || completion.RetainSchedulerWork || completion.Checkpoint.Name != RuntimeCheckpointNames.WorkflowCompleted ||
            completion.StateChanges.WorkflowExecution is not { Operation: RuntimeStateChangeOperation.Upsert, State.Status: WorkflowExecutionStatus.Completed } workflow ||
            workflow.State.WorkflowExecutionId != completion.WorkflowExecutionId || workflow.State.CompletedAt is null ||
            workflow.State.HistoryCapturePolicy is null ||
            workflow.State.SystemMetadata.GetValueOrDefault(RuntimeMetadataKeys.PersistenceMode) != nameof(PersistenceMode.Ephemeral))
            throw new InvalidOperationException("Source discharge requires the actual fenced completed Ephemeral workflow decision.");
        return workflow.State;
    }
}

public sealed record DurableTimerHistoryQuery : RuntimeStorePageRequest
{
    public DurableTimerHistoryQuery(int limit = DefaultLimit, string? continuationToken = null) : base(limit, continuationToken) { }
}

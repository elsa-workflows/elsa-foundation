namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Safe deterministic facts owned by the durable timer source, independent from native checkpoint coverage.</summary>
public static class DurableTimerHistoryFacts
{
    public static IReadOnlyList<string> Kinds { get; } = Array.AsReadOnly(new[] { "TimerScheduled", "TimerDispatched", "TimerRetired" });

    public static RuntimeHistoryObservation? CreateScheduled(DurableTimer timer, RuntimeHistoryCapturePolicy policy,
        RuntimeHistoryCausation? cause = null) => Create(timer, policy, "TimerScheduled", timer.CreatedAt, null, cause);

    public static RuntimeHistoryObservation? CreateCancelled(DurableTimer timer, DateTimeOffset occurredAt,
        RuntimeHistoryCausation? cause = null) => timer.HistoryResponsibility is { } responsibility
        ? Create(timer, responsibility.Policy, "TimerRetired", occurredAt, "scope-cancelled", cause) : null;

    public static RuntimeHistoryObservation? CreateDispatched(DurableTimer timer, DateTimeOffset occurredAt,
        BookmarkResumeDispatchStatus status, RuntimeHistoryCausation? cause = null) => timer.HistoryResponsibility is { } responsibility
        ? Create(timer, responsibility.Policy, "TimerDispatched", occurredAt, status.ToString(), cause) : null;

    public static RuntimeHistoryObservation? CreateRetired(DurableTimer timer, DateTimeOffset occurredAt,
        string reasonCode, RuntimeHistoryCausation? cause = null) => timer.HistoryResponsibility is { } responsibility
        ? Create(timer, responsibility.Policy, "TimerRetired", occurredAt, reasonCode, cause) : null;

    private static RuntimeHistoryObservation? Create(DurableTimer timer, RuntimeHistoryCapturePolicy policy, string name,
        DateTimeOffset at, string? reason, RuntimeHistoryCausation? cause)
    {
        ArgumentNullException.ThrowIfNull(timer); ArgumentNullException.ThrowIfNull(policy);
        if (!policy.SupportedKinds.Contains(name, StringComparer.Ordinal)) return null;
        if (reason?.Length > 128) throw new ArgumentOutOfRangeException(nameof(reason));
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["history.timerId"] = timer.TimerId,
            ["history.scheduledAt"] = timer.DueTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        };
        if (reason is not null) metadata["history.reasonCode"] = reason;
        var e = new RuntimeHistoryEvent("timer-history:" + RuntimeHistoryIdentity.CreateBinding([timer.WorkflowExecutionId, timer.TimerId, name]),
            timer.WorkflowExecutionId, 0, RuntimeHistoryEventCategory.SchedulerLifecycle, name, at,
            timer.ActivityExecutionId, metadata: metadata)
        {
            Authority = RuntimeHistoryAuthority.DeliveryObservation, ExecutionScopeId = timer.ExecutionScopeId, Causation = cause
        };
        RuntimeHistorySafety.ValidateMetadata(e);
        return new(policy, e);
    }
}

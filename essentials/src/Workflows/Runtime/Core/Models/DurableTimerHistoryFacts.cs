using System.Globalization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Safe deterministic observations retained by the actual timer owner, independent of native checkpoint coverage.</summary>
public static class DurableTimerHistoryFacts
{
    public static IReadOnlyList<string> Kinds { get; } = Array.AsReadOnly(new[] { "TimerScheduled", "TimerDispatched", "TimerRetired" });

    public static RuntimeHistoryObservation? CreateScheduled(DurableTimer timer, RuntimeHistoryCapturePolicy policy,
        RuntimeHistoryCausation? cause = null)
    {
        ArgumentNullException.ThrowIfNull(timer);
        return Create(timer, policy, "TimerScheduled", timer.CreatedAt, null, cause);
    }

    public static RuntimeHistoryObservation? CreateCancelled(DurableTimer timer, DateTimeOffset occurredAt,
        RuntimeHistoryCausation? cause = null) => CreateRetired(timer, occurredAt, "scope-cancelled", cause);

    public static RuntimeHistoryObservation? CreateDispatched(DurableTimer timer, DateTimeOffset occurredAt,
        BookmarkResumeDispatchStatus status, RuntimeHistoryCausation? cause = null)
    {
        ArgumentNullException.ThrowIfNull(timer);
        if (status is not (BookmarkResumeDispatchStatus.Dispatched or BookmarkResumeDispatchStatus.Duplicate))
            throw new ArgumentOutOfRangeException(nameof(status), "A timer dispatch observation requires an actual dispatched or duplicate handoff.");
        return timer.HistoryResponsibility is { } responsibility
            ? Create(timer, responsibility.Policy, "TimerDispatched", occurredAt, status.ToString(), cause) : null;
    }

    public static RuntimeHistoryObservation? CreateRetired(DurableTimer timer, DateTimeOffset occurredAt,
        string reasonCode, RuntimeHistoryCausation? cause = null)
    {
        ArgumentNullException.ThrowIfNull(timer);
        if (!IsRetirementReason(reasonCode)) throw new ArgumentOutOfRangeException(nameof(reasonCode));
        return timer.HistoryResponsibility is { } responsibility
            ? Create(timer, responsibility.Policy, "TimerRetired", occurredAt, reasonCode, cause) : null;
    }

    internal static bool IsTimerObservation(string name) => name is "TimerScheduled" or "TimerDispatched" or "TimerRetired";

    internal static void ValidateObservation(RuntimeHistoryEvent e)
    {
        if (!e.Metadata.TryGetValue("history.timerId", out var timer) || string.IsNullOrWhiteSpace(timer) || timer.Length > 1024 ||
            !e.Metadata.TryGetValue("history.scheduledAt", out var due) ||
            !DateTimeOffset.TryParseExact(due, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            e.Metadata.Keys.Any(key => key is not ("history.timerId" or "history.scheduledAt" or "history.reasonCode")) ||
            e.HistoryEventId != "timer-history:" + RuntimeHistoryIdentity.CreateBinding([e.WorkflowExecutionId, timer, e.Name]) ||
            e.BookmarkId is not null || e.IncidentId is not null || e.AttemptId is not null || e.OccurredAtKnown == false ||
            (e.Name == "TimerScheduled" ? e.Metadata.ContainsKey("history.reasonCode") :
                !e.Metadata.TryGetValue("history.reasonCode", out var reason) || (e.Name == "TimerDispatched"
                    ? reason is not (nameof(BookmarkResumeDispatchStatus.Dispatched) or nameof(BookmarkResumeDispatchStatus.Duplicate))
                    : !IsRetirementReason(reason))))
            throw new InvalidOperationException("Timer observations require the exact owner identity, due time and closed disposition.");
    }

    private static bool IsRetirementReason(string? reason) => reason is "scope-cancelled" or nameof(BookmarkResumeDispatchStatus.NotFound)
        or nameof(BookmarkResumeDispatchStatus.WorkflowExecutionMissing) or nameof(BookmarkResumeDispatchStatus.ExecutableMissing);

    private static RuntimeHistoryObservation? Create(DurableTimer timer, RuntimeHistoryCapturePolicy policy, string name,
        DateTimeOffset at, string? reason, RuntimeHistoryCausation? cause)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.SupportedKinds.Contains(name, StringComparer.Ordinal)) return null;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["history.timerId"] = timer.TimerId,
            ["history.scheduledAt"] = timer.DueTime.ToString("O", CultureInfo.InvariantCulture)
        };
        if (reason is not null) metadata["history.reasonCode"] = reason;
        var e = new RuntimeHistoryEvent("timer-history:" + RuntimeHistoryIdentity.CreateBinding([timer.WorkflowExecutionId, timer.TimerId, name]),
            timer.WorkflowExecutionId, 0, RuntimeHistoryEventCategory.SchedulerLifecycle, name, at,
            timer.ActivityExecutionId, metadata: metadata)
        { Authority = RuntimeHistoryAuthority.DeliveryObservation, ExecutionScopeId = timer.ExecutionScopeId, Causation = cause };
        var observation = new RuntimeHistoryObservation(policy, e);
        RuntimeHistorySafety.ValidateObservation(observation);
        return observation;
    }
}

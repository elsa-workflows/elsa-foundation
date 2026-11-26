using System.Globalization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>Safe facts from the scope's typed native decision, never from a diagnostic projection.</summary>
internal static class CompensationHistory
{
    internal static readonly RuntimeHistorySourceFactKind[] Kinds =
    [
        RuntimeHistorySourceFactKind.CompensationRegistered, RuntimeHistorySourceFactKind.CompensationStarted,
        RuntimeHistorySourceFactKind.CompensationHandlerScheduled, RuntimeHistorySourceFactKind.CompensationHandlerCompleted,
        RuntimeHistorySourceFactKind.CompensationHandlerFaulted, RuntimeHistorySourceFactKind.CompensationConfirmed,
        RuntimeHistorySourceFactKind.CompensationResolved, RuntimeHistorySourceFactKind.CompensationCompleted,
        RuntimeHistorySourceFactKind.CompensationAbandoned
    ];

    internal static IEnumerable<RuntimeHistorySourceFact> Facts(CompensationScopeState before,
        CompensationScopeState after, string scope, DateTimeOffset at)
    {
        RuntimeHistorySourceFact Fact(RuntimeHistorySourceFactKind kind, string subject, string? entry = null,
            string? related = null, string? attempt = null, string? incident = null, string? reason = null) =>
            new(kind, RuntimeHistoryIdentity.CreateBinding([scope, subject]), at, scope, attempt,
                relatedActivityExecutionId: related, incidentId: incident, compensationEntryId: entry, reasonCode: reason);

        if (before.Cause is null && after.Cause is { } cause)
            yield return Fact(RuntimeHistorySourceFactKind.CompensationStarted, scope, related: cause.ActivityExecutionId,
                incident: cause.IncidentId, reason: cause.Kind);

        var previous = before.Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        foreach (var entry in after.Entries)
        {
            previous.TryGetValue(entry.Id, out var old);
            if (old is null)
                yield return Fact(RuntimeHistorySourceFactKind.CompensationRegistered, entry.Id, entry.Id,
                    entry.ForwardActivityExecutionId, reason: entry.ForwardOutcome == CompensationForwardOutcome.Uncertain ? "outcome-unknown" : "forward-completed");
            for (var index = 0; index < entry.HandlerAttempts.Count; index++)
            {
                var attempt = entry.HandlerAttempts[index];
                var prior = old?.HandlerAttempts.ElementAtOrDefault(index);
                var ordinal = (index + 1).ToString(CultureInfo.InvariantCulture);
                var subject = RuntimeHistoryIdentity.CreateBinding([entry.Id, ordinal]);
                if (prior is null)
                    yield return Fact(RuntimeHistorySourceFactKind.CompensationHandlerScheduled, subject, entry.Id,
                        attempt.ActivityExecutionId, ordinal, reason: attempt.NotBefore is null ? "scheduled" : "delayed-retry-scheduled");
                if (attempt.Result != prior?.Result && attempt.Result is CompensationAttemptResult.Completed or CompensationAttemptResult.Faulted)
                    yield return Fact(attempt.Result == CompensationAttemptResult.Completed
                            ? RuntimeHistorySourceFactKind.CompensationHandlerCompleted : RuntimeHistorySourceFactKind.CompensationHandlerFaulted,
                        subject, entry.Id, attempt.ActivityExecutionId, ordinal, attempt.IncidentId);
            }
            if (entry.Status != old?.Status && entry.Status == CompensationEntryStatus.Confirmed)
                yield return Fact(RuntimeHistorySourceFactKind.CompensationConfirmed, entry.Id, entry.Id, entry.ForwardActivityExecutionId);
            if (entry.Resolution is { } resolution && old?.Resolution is null)
                yield return Fact(RuntimeHistorySourceFactKind.CompensationResolved, entry.Id, entry.Id,
                    entry.ForwardActivityExecutionId, incident: resolution.IncidentId,
                    reason: resolution.Outcome == CompensationResolutionOutcome.EffectAbsent ? "effect-absent" : "effect-accepted");
        }
        // A confirmation of zero eligible entries is still an actual decision.
        foreach (var confirmation in after.Confirmations.Where(item => item.EntryIds.Count == 0 &&
                     !before.Confirmations.Any(prior => prior.ActivityExecutionId == item.ActivityExecutionId)))
            yield return Fact(RuntimeHistorySourceFactKind.CompensationConfirmed, confirmation.ActivityExecutionId,
                related: confirmation.ActivityExecutionId, reason: "no-eligible-entries");
        if (before.Phase != after.Phase && after.Phase is CompensationScopePhase.Completed or CompensationScopePhase.Compensated)
            yield return Fact(RuntimeHistorySourceFactKind.CompensationCompleted, scope,
                reason: after.Phase == CompensationScopePhase.Compensated ? "compensated" : "forward-completed");
        if (before.Abandonment is null && after.Abandonment is { } abandonment)
            yield return Fact(RuntimeHistorySourceFactKind.CompensationAbandoned, scope, abandonment.EntryId,
                incident: abandonment.IncidentId, reason: "operator-abandoned");
    }
}

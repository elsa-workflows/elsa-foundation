using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

internal sealed partial class TriggerManagement
{
    private async ValueTask ValidateReservationChangeAsync(TriggerAggregate current, TriggerAggregate proposed, CancellationToken ct)
    {
        // Stop-only changes must remain possible when a source is unavailable. This is advisory;
        // the physical resource writer still arbitrates every reservation race at commit.
        if (TriggerStopOnlyChange.IsSafe(current, proposed) ||
            !proposed.Trigger.HasActivated && proposed.Trigger.DesiredState != TriggerDesiredState.Enabled) return;
        var planner = reservationPlanners?.SingleOrDefault(item => item.TypeKey == proposed.Trigger.TypeKey);
        if (planner is null) return;
        if (reservations?.SupportsReservations != true)
            throw new TriggerOperationException("unavailable", "The actual source reservation cannot be inspected in this composition.");
        var descriptors = await planner.PlanAsync(access.Scope, current, new("preview-reservation", Fingerprint(proposed), access.Subject,
            current.Trigger.Revision, proposed.Trigger, proposed.Subscriptions), ct);
        foreach (var descriptor in descriptors)
        {
            RuntimeIngressReservationPage page;
            try { page = await reservations.ListInScopeAsync(access.Scope.Persistence,
                new(descriptor.StimulusType, 2, StimulusHash: descriptor.StimulusHash), ct); }
            catch (NotSupportedException)
            { throw new TriggerOperationException("unavailable", "The actual source reservation cannot be inspected in this composition."); }
            if (page.Next is not null || page.Items.Any(row => row.Scope != access.Scope ||
                row.OwnerKind != RuntimeIngressReservation.StandaloneTriggerOwner || row.OwnerId != proposed.Trigger.Id))
                throw new TriggerOperationException("route-conflict", "This source route is already reserved by another resource.");
        }
    }

    private async ValueTask<(TriggerHttpView View, bool Ready)> HttpReadinessAsync(TriggerAggregate aggregate, JsonElement http,
        List<TriggerFinding> findings, CancellationToken ct)
    {
        var trigger = aggregate.Trigger; var mode = Text(http, "mode");
        TriggerHttpView View(string state, string? endpoint = null) => new(mode,
            mode == "AsyncFanOut" ? OptionalText(http, "admissionPolicy") ?? "RequireAll" : null, state, OptionalText(trigger.Configuration, "path"), endpoint);
        if (trigger.DesiredState == TriggerDesiredState.Deleted || !trigger.HasActivated && trigger.DesiredState != TriggerDesiredState.Enabled)
            return (View("Unreserved"), true);
        var planner = reservationPlanners?.SingleOrDefault(item => item.TypeKey == trigger.TypeKey);
        if (planner is null || reservations?.SupportsReservations != true)
        {
            findings.Add(new("ReservationUnavailable", "The actual source reservation cannot be inspected in this composition."));
            return (View("Unavailable"), false);
        }
        var descriptors = await planner.PlanAsync(access.Scope, aggregate, new("readiness", Fingerprint(new { trigger.Id, trigger.Revision }),
            access.Subject, trigger.Revision, trigger, aggregate.Subscriptions), ct);
        if (descriptors.Count == 0)
        {
            findings.Add(new("ReservationMissing", "The expected source reservation is unavailable."));
            return (View("Unreserved"), false);
        }
        string? endpoint = null;
        foreach (var expected in descriptors)
        {
            var actual = await reservations.FindAsync(access.Scope, expected.StimulusType, expected.StimulusHash, ct);
            if (actual is null)
            {
                findings.Add(new("ReservationMissing", "The expected source reservation is unavailable."));
                return (View("Unreserved"), false);
            }
            if (actual.OwnerKind != RuntimeIngressReservation.StandaloneTriggerOwner || actual.OwnerId != trigger.Id)
            {
                findings.Add(new("ReservationConflict", "This source route is reserved by another resource."));
                return (View("Conflict"), false);
            }
            endpoint = actual.Metadata.GetValueOrDefault("http:template");
            if (actual.Scope != access.Scope || actual.OwnerRevision != trigger.Revision || actual.ConfigurationRevision != trigger.ConfigurationRevision)
            {
                findings.Add(new("ReservationGenerationChanged", "The actual source reservation changed; reload the resource."));
                return (View("Unavailable", endpoint), false);
            }
        }
        return (View("Reserved", endpoint), true);
    }

    private async ValueTask<TriggerScheduleView?> ScheduleReadinessAsync(StandaloneTrigger trigger, List<TriggerFinding> findings, CancellationToken ct)
    {
        if (types.Find(trigger.TypeKey)?.Family is not ("Timer" or "Cron")) return null;
        if (schedules is null)
        {
            findings.Add(new("ScheduleUnavailable", "The actual source schedule cannot be inspected in this composition."));
            return null;
        }
        var id = RecurringTriggerSchedule.BuildResourceId(access.Scope.Persistence, trigger.Id, access.Scope.TenantId);
        RecurringTriggerScheduleObservation? observation = null;
        RecurringTriggerSchedule? schedule;
        try { observation = await schedules.ObserveAsync(id, ct); schedule = observation?.Schedule; }
        catch (NotSupportedException)
        {
            schedule = await schedules.FindAsync(id, ct);
            findings.Add(new("ScheduleRecoveryStateUnavailable", "Schedule cursor inspection is available; recovery facts are unavailable."));
        }
        if (schedule is null)
        {
            if (trigger.HasActivated || trigger.DesiredState == TriggerDesiredState.Enabled)
                findings.Add(new("ScheduleMissing", "No actual source schedule is available for this resource."));
            return null;
        }
        if (schedule.Resource is not { } owner || owner.TriggerId != trigger.Id || owner.Scope != access.Scope.Persistence ||
            owner.TenantId != access.Scope.TenantId || owner.SourceTypeKey != trigger.TypeKey || owner.ConfigurationRevision != trigger.ConfigurationRevision)
        {
            findings.Add(new("ScheduleGenerationChanged", "The actual source schedule changed; reload the resource."));
            return null;
        }
        string? offset = null;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(owner.TimeZoneId);
            var value = zone.GetUtcOffset(schedule.NextOccurrence);
            offset = (value < TimeSpan.Zero ? "-" : "+") + value.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        { findings.Add(new("ScheduleTimezoneUnavailable", "The captured schedule timezone is unavailable on this server.")); }
        var overdue = schedule.IsActive && schedule.NextOccurrence <= time.GetUtcNow();
        if (overdue) findings.Add(new("ScheduleOverdue", "The persisted due occurrence is overdue and remains retained until accepted."));
        if (observation is { FailureCount: > 0 })
            findings.Add(new(observation.RetryAt > time.GetUtcNow() ? "ScheduleRetryBackoff" : "ScheduleRetryWaiting",
                "The due occurrence has recorded failed attempts and remains retained for recovery."));
        return new(owner.Generation, owner.TimeZoneId, schedule.NextOccurrence.ToUniversalTime(), offset,
            "OneCatchUpThenFuture", "NextFutureOccurrence", schedule.IsActive, overdue,
            observation?.FailureCount, observation?.RetryAt, observation?.OccurrenceInFlight);
    }
}

using System.Globalization;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

internal sealed partial class TriggerManagement
{
    private async ValueTask AddScheduleImpactAsync(TriggerAggregate current, TriggerAggregate proposed,
        List<TriggerImpact> impact, CancellationToken ct)
    {
        var planners = (schedulePlanners ?? []).Where(planner => planner.Supports(current.Trigger.TypeKey)).ToArray();
        if (planners.Length == 0 && types.Find(current.Trigger.TypeKey)?.Family is not ("Timer" or "Cron")) return;
        string? before = null;
        void Unavailable(string reason, string? after = null) => impact.Add(new("Next firing", before, after,
            "Advisory next firing unavailable: " + reason + " No firing time is reserved by preview."));
        if (schedules is null) { Unavailable("the actual schedule reader is unavailable."); return; }
        RecurringTriggerSchedule? cursor;
        try
        {
            var id = RecurringTriggerSchedule.BuildResourceId(access.Scope.Persistence, current.Trigger.Id, access.Scope.TenantId);
            cursor = await schedules.FindAsync(id, ct);
            if (cursor is not null && !OwnsSchedule(cursor, current.Trigger))
            { Unavailable("the stored schedule does not match this resource and scope."); return; }
            before = cursor is null ? "Not scheduled (no current cursor)" : ScheduleCursorText(cursor);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { Unavailable("the actual schedule cursor or its timezone cannot be inspected."); return; }
        if (planners.Length != 1)
        { Unavailable(planners.Length == 0 ? "the source schedule planner is not installed." : "several source schedule planners claim this type."); return; }
        try
        {
            var now = time.GetUtcNow();
            var write = new TriggerStoreWrite("preview-schedule", Fingerprint(proposed), access.Subject,
                current.Trigger.Revision, proposed.Trigger, proposed.Subscriptions);
            var plan = planners[0].Plan(access.Scope, current, write, cursor, now);
            if (!Enum.IsDefined(plan.Action) || (plan.Action == StandaloneScheduleAction.Replace) != (plan.Replacement is not null) ||
                plan.Replacement is { } replacement && (!OwnsSchedule(replacement, proposed.Trigger) ||
                    replacement.Resource!.Generation != proposed.Trigger.Revision || replacement.Resource.ConfigurationRevision != proposed.Trigger.ConfigurationRevision ||
                    !replacement.IsActive || proposed.Trigger.DesiredState != TriggerDesiredState.Enabled))
            { Unavailable("the source returned a schedule plan outside this resource mutation."); return; }
            var after = plan.Action switch
            {
                StandaloneScheduleAction.Replace => ScheduleCursorText(plan.Replacement!),
                StandaloneScheduleAction.Preserve => before,
                StandaloneScheduleAction.Deactivate => cursor is null ? "No scheduled firing (paused)" : "Paused; saved cursor: " + ScheduleCursorText(cursor),
                StandaloneScheduleAction.Remove => "No future firing (schedule removed)",
                _ => throw new InvalidOperationException("Unsupported schedule action.")
            };
            impact.Add(new("Next firing", before, after,
                $"Action: {plan.Action}. Advisory calculation at {now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}; " +
                "the source rechecks the schedule when this change commits. Preview does not reserve a firing time."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TriggerOperationException exception) when (exception.Code == "validation" && exception.Message == "This recurrence has no future firing.")
        { Unavailable("this recurrence has no future firing.", "No future firing"); }
        catch (Exception) { Unavailable("the source cannot calculate a supported proposed firing for these settings."); }
    }

    private bool OwnsSchedule(RecurringTriggerSchedule schedule, StandaloneTrigger trigger) =>
        trigger.TenantId == access.Scope.TenantId && schedule.Resource is { } owner &&
        owner.TriggerId == trigger.Id && owner.SourceTypeKey == trigger.TypeKey && owner.Scope == access.Scope.Persistence &&
        owner.TenantId == access.Scope.TenantId && owner.Generation <= trigger.Revision && owner.ConfigurationRevision == trigger.ConfigurationRevision &&
        !string.IsNullOrWhiteSpace(owner.SourceFamily) && !string.IsNullOrWhiteSpace(owner.TimeZoneId) &&
        schedule.ScheduleId == RecurringTriggerSchedule.BuildResourceId(access.Scope.Persistence, trigger.Id, access.Scope.TenantId) &&
        schedule.ArtifactId is null && schedule.ExecutableNodeId is null && schedule.StimulusType is null && schedule.StimulusHash is null &&
        schedule.ActivationId is null && schedule.SlotId is null;

    private static string ScheduleCursorText(RecurringTriggerSchedule schedule)
    {
        var zone = schedule.Resource!.TimeZoneId;
        var offset = TimeZoneInfo.FindSystemTimeZoneById(zone).GetUtcOffset(schedule.NextOccurrence);
        var offsetText = (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        return schedule.NextOccurrence.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) + " | " + zone + " | " + offsetText;
    }
}

using System.Data;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfRecurringTriggerScheduleStore
{
    public async ValueTask<bool> ExhaustClaimAsync(RecurringTriggerOccurrenceClaim claim, CancellationToken cancellationToken = default)
    {
        if (claim.Schedule.Resource is null || await LoadHeldAsync(claim, cancellationToken) is not { } held) return false;
        context.RecurringTriggerSchedules.Attach(held.Row);
        context.RecurringTriggerSchedules.Remove(held.Row);
        try { await context.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
        finally { context.ChangeTracker.Clear(); }
    }

    internal bool Uses(RuntimeDbContext expected, PersistenceScope scope) => ReferenceEquals(context, expected) && RequireScope() == scope.Value;

    internal async ValueTask<DateTimeOffset> FenceStandaloneOccurrenceAsync(RecurringTriggerOccurrenceClaim claim, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Resource occurrence fencing requires the source owner's SERIALIZABLE transaction.");
        var scope = RequireScope();
        var row = await context.RecurringTriggerSchedules.AsNoTracking().SingleOrDefaultAsync(item => item.Id == Id(scope, claim.Schedule.ScheduleId), cancellationToken);
        if (row is null || claim.Schedule.Resource is null || !claim.Schedule.IsActive ||
            row.Revision < claim.Revision || row.ClaimOwnerId != Encode(claim.OwnerId) || row.ClaimToken != claim.FencingToken ||
            row.VisibleAfterUtcTicks is not { } deadline || deadline <= clock.GetUtcNow().UtcTicks || !SchedulesEqual(Read(row, scope), claim.Schedule))
            throw new TriggerOperationException("conflict", "The resource schedule occurrence is no longer held by this source.");
        if (claim.Schedule.Resource.Scope.Value != scope)
            throw new InvalidDataException("The occurrence owner differs from its actual physical schedule scope.");
        if (await context.RecurringTriggerSchedules.Where(item => item.Id == row.Id && item.Revision == row.Revision &&
                item.ContentJson == row.ContentJson && item.ClaimOwnerId == row.ClaimOwnerId && item.ClaimToken == row.ClaimToken && item.VisibleAfterUtcTicks == deadline)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Revision, row.Revision), cancellationToken) != 1 || deadline <= clock.GetUtcNow().UtcTicks)
            throw new TriggerOperationException("conflict", "The occurrence lease changed or expired before source acceptance.");
        return new(deadline, TimeSpan.Zero);
    }

    internal async ValueTask StageStandaloneScheduleWriteAsync(TriggerScope scope, TriggerStoreWrite write, TriggerAggregate? previous,
        IStandaloneSchedulePlanner planner, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null || RequireScope() != scope.Persistence.Value)
            throw new InvalidOperationException("A resource schedule projection requires the resource owner's scoped transaction.");
        if (write.Trigger.TenantId != scope.TenantId || previous is not null && previous.Trigger.TenantId != scope.TenantId)
            throw new InvalidDataException("The resource schedule requires the exact resource tenant facts.");
        var scheduleId = RecurringTriggerSchedule.BuildResourceId(scope.Persistence, write.Trigger.Id, scope.TenantId);
        var row = await context.RecurringTriggerSchedules.AsNoTracking().SingleOrDefaultAsync(item => item.Id == Id(scope.Persistence.Value, scheduleId), cancellationToken);
        var current = row is null ? null : Read(row, scope.Persistence.Value, scheduleId);
        if (current is not null && (current.Resource is not { } currentOwner || currentOwner.TriggerId != write.Trigger.Id || currentOwner.Scope != scope.Persistence || currentOwner.TenantId != scope.TenantId))
            throw new InvalidDataException("The recurring identity is owned by another schedule.");
        var plan = planner.Plan(scope, previous, write, current, clock.GetUtcNow());
        if (!Enum.IsDefined(plan.Action) || (plan.Action == StandaloneScheduleAction.Replace) != (plan.Replacement is not null))
            throw new InvalidDataException("The module returned an invalid recurring schedule plan.");
        if (plan.Action == StandaloneScheduleAction.Preserve || row is null && (plan.Action is StandaloneScheduleAction.Remove or StandaloneScheduleAction.Deactivate)) return;
        if (row is not null) context.RecurringTriggerSchedules.Attach(row);
        if (plan.Action == StandaloneScheduleAction.Remove) { context.RecurringTriggerSchedules.Remove(row!); return; }
        var replacement = plan.Action == StandaloneScheduleAction.Deactivate ? current! with { IsActive = false } : plan.Replacement!;
        if (replacement.Resource is not { } owner || owner.TriggerId != write.Trigger.Id || owner.Scope != scope.Persistence || owner.TenantId != scope.TenantId ||
            replacement.ScheduleId != scheduleId || plan.Action == StandaloneScheduleAction.Replace &&
            (owner.Generation != write.Trigger.Revision || owner.ConfigurationRevision != write.Trigger.ConfigurationRevision ||
             !replacement.IsActive || write.Trigger.DesiredState != TriggerDesiredState.Enabled))
            throw new InvalidDataException("The prepared schedule differs from the actual resource mutation.");
        if (row is null)
        {
            row = ToEntity(replacement, scope.Persistence.Value, Id(scope.Persistence.Value, scheduleId), 1);
            context.RecurringTriggerSchedules.Add(row);
        }
        else
        {
            var revision = row.Revision;
            Copy(row, replacement, scope.Persistence.Value, checked(revision + 1));
            context.Entry(row).Property(item => item.Revision).OriginalValue = revision;
            context.Entry(row).State = EntityState.Modified;
        }
        row.ClaimOwnerId = null; row.ClaimedAtUtcTicks = null; row.ClaimedAtOffsetMinutes = null;
        row.VisibleAfterUtcTicks = null; row.VisibleAfterOffsetMinutes = null; row.FailureCount = 0;
    }
}

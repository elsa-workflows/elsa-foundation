using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

public sealed partial class InMemoryRecurringTriggerScheduleStore
{
    public ValueTask<bool> ExhaustClaimAsync(RecurringTriggerOccurrenceClaim claim, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            if (claim.Schedule.Resource is null || !TryGetCurrent(claim, out _)) return ValueTask.FromResult(false);
            Remove(claim.Schedule.ScheduleId);
            return ValueTask.FromResult(true);
        }
    }

    /// <summary>The caller already owns the actual native state write gate; no asynchronous callback may escape this gate.</summary>
    internal T ExecuteUnderStandaloneOccurrenceClaim<T>(RecurringTriggerOccurrenceClaim claim, TimeProvider clock, Func<T> accept)
        => ExecuteUnderStandaloneOccurrenceClaim(claim, clock, _ => accept());

    /// <summary>Supplies the actual held deadline for a fresh final check at the physical source insertion.</summary>
    internal T ExecuteUnderStandaloneOccurrenceClaim<T>(RecurringTriggerOccurrenceClaim claim, TimeProvider clock, Func<DateTimeOffset, T> accept)
    {
        ArgumentNullException.ThrowIfNull(claim); ArgumentNullException.ThrowIfNull(clock); ArgumentNullException.ThrowIfNull(accept);
        lock (_syncRoot)
        {
            if (claim.Schedule.Resource is null || !claim.Schedule.IsActive || !_claims.TryGetValue(claim.Schedule.ScheduleId, out var current) ||
                current.OwnerId != claim.OwnerId || current.FencingToken != claim.FencingToken || current.Revision < claim.Revision ||
                current.VisibleAfter is not { } deadline || deadline <= clock.GetUtcNow() ||
                !_schedules.TryGetValue(claim.Schedule.ScheduleId, out var schedule) || schedule != claim.Schedule)
                throw new TriggerOperationException("conflict", "The resource schedule occurrence is no longer held by this source.");
            return accept(deadline);
        }
    }

    /// <summary>Installs resource and recurring projection synchronously under the caller's native state write gate.</summary>
    internal T ExecuteStandaloneScheduleWrite<T>(TriggerScope scope, TriggerStoreWrite write, TriggerAggregate? previous,
        IStandaloneSchedulePlanner planner, TimeProvider clock, Func<T> commitResource)
    {
        ArgumentNullException.ThrowIfNull(planner); ArgumentNullException.ThrowIfNull(commitResource);
        lock (_syncRoot)
        {
            var id = RecurringTriggerSchedule.BuildResourceId(scope.Persistence, write.Trigger.Id, scope.TenantId);
            var current = _schedules.GetValueOrDefault(id);
            if (current is not null && (current.Resource is not { } currentOwner || currentOwner.TriggerId != write.Trigger.Id || currentOwner.Scope != scope.Persistence || currentOwner.TenantId != scope.TenantId))
                throw new InvalidDataException("The recurring identity is owned by another schedule.");
            var plan = planner.Plan(scope, previous, write, current, clock.GetUtcNow());
            CheckPlan(scope, write, plan);
            var result = commitResource();
            switch (plan.Action)
            {
                case StandaloneScheduleAction.Preserve: break;
                case StandaloneScheduleAction.Replace:
                    _schedules[id] = plan.Replacement!; _claims.Remove(id); break;
                case StandaloneScheduleAction.Deactivate:
                    if (current is not null) _schedules[id] = current with { IsActive = false };
                    _claims.Remove(id); break;
                case StandaloneScheduleAction.Remove: Remove(id); break;
            }
            return result;
        }
    }

    private static void CheckPlan(TriggerScope scope, TriggerStoreWrite write, StandaloneSchedulePlan plan)
    {
        if (!Enum.IsDefined(plan.Action) || (plan.Action == StandaloneScheduleAction.Replace) != (plan.Replacement is not null) ||
            plan.Replacement is { } replacement && (replacement.Resource is not { } owner || owner.TriggerId != write.Trigger.Id ||
                owner.Scope != scope.Persistence || owner.TenantId != scope.TenantId || owner.Generation != write.Trigger.Revision || owner.ConfigurationRevision != write.Trigger.ConfigurationRevision ||
                replacement.ScheduleId != RecurringTriggerSchedule.BuildResourceId(scope.Persistence, write.Trigger.Id, scope.TenantId) ||
                replacement.ArtifactId is not null || replacement.ExecutableNodeId is not null || replacement.ActivationId is not null ||
                replacement.SlotId is not null || replacement.StimulusType is not null || replacement.StimulusHash is not null ||
                !replacement.IsActive || write.Trigger.DesiredState != TriggerDesiredState.Enabled))
            throw new InvalidDataException("The prepared schedule projection differs from its exact resource mutation.");
    }
}

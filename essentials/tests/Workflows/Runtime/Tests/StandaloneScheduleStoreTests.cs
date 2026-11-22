using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class StandaloneScheduleStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TriggerScope Scope = new(new("tenant-a"), "tenant-a");

    [Fact]
    public async Task Native_observation_reads_actual_backoff_and_claim_facts_without_changing_the_occurrence()
    {
        var clock = new FakeTimeProvider(Now);
        IRecurringTriggerScheduleStore schedules = new InMemoryRecurringTriggerScheduleStore(clock);
        var schedule = RecurringTriggerSchedule.ForResource(new("observed-resource", 1, 1, "timer", "Elsa.StandaloneTimer",
            new("physical-shell"), "actual-tenant"), RecurringScheduleKind.Interval, "PT5M", Now, Now);
        await schedules.SaveAsync(schedule);
        var initial = (await schedules.ObserveAsync(schedule.ScheduleId))!;
        Assert.Equal(0, initial.FailureCount); Assert.Null(initial.RetryAt); Assert.False(initial.OccurrenceInFlight);
        var claim = Assert.Single(await schedules.ClaimDueAsync(new("pump", Now, TimeSpan.FromMinutes(1), 10)));
        var inFlight = (await schedules.ObserveAsync(schedule.ScheduleId))!;
        Assert.True(inFlight.OccurrenceInFlight); Assert.Null(inFlight.RetryAt);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True((await schedules.ObserveAsync(schedule.ScheduleId))!.OccurrenceInFlight);
        var retry = clock.GetUtcNow().AddMinutes(3);
        Assert.True(await schedules.ReleaseClaimAsync(claim, retry));
        var blocked = (await schedules.ObserveAsync(schedule.ScheduleId))!;
        Assert.Equal(1, blocked.FailureCount); Assert.Equal(retry, blocked.RetryAt); Assert.False(blocked.OccurrenceInFlight);
        Assert.Equal(schedule, blocked.Schedule);
        Assert.Empty(await schedules.ClaimDueAsync(new("early", retry.AddSeconds(-1), TimeSpan.FromMinutes(1), 10)));
        var resumed = Assert.Single(await schedules.ClaimDueAsync(new("retry", retry, TimeSpan.FromMinutes(1), 10)));
        _ = await schedules.ObserveAsync(schedule.ScheduleId);
        Assert.True(await schedules.SettleClaimAsync(resumed, retry.AddMinutes(5)));
        var settled = (await schedules.ObserveAsync(schedule.ScheduleId))!;
        Assert.Equal(0, settled.FailureCount); Assert.Null(settled.RetryAt); Assert.False(settled.OccurrenceInFlight);
        Assert.Null(await schedules.ObserveAsync("absent"));
    }

    [Fact]
    public async Task Native_resource_conflict_does_not_install_the_prepared_schedule_generation()
    {
        var state = new InMemoryRuntimeCheckpointStoreState(); var clock = new FakeTimeProvider(Now);
        var resources = new InMemoryStandaloneTriggerStore(state, new Access(), clock);
        var schedules = new InMemoryRecurringTriggerScheduleStore(clock);
        var first = Write(1);
        await state.ExecuteWriteGateAsync(_ => ValueTask.FromResult(schedules.ExecuteStandaloneScheduleWrite(Scope, first, null,
            new Planner(Resource(1)), clock, () => resources.StageWrite(Scope, first, default))));
        var original = (await resources.FindAsync(Scope, "resource"))!;
        var bad = Write(2) with { ExpectedRevision = 0 };

        await Assert.ThrowsAsync<TriggerOperationException>(() => state.ExecuteWriteGateAsync(_ => ValueTask.FromResult(
            schedules.ExecuteStandaloneScheduleWrite(Scope, bad, original, new Planner(Resource(2)), clock,
                () => resources.StageWrite(Scope, bad, default)))).AsTask());

        Assert.Equal(1, (await resources.FindAsync(Scope, "resource"))!.Trigger.Revision);
        Assert.Equal(1, (await schedules.FindAsync(Resource(1).ScheduleId))!.Resource!.Generation);
        Assert.Null(await resources.FindMutationAsync(Scope, "operator", bad.RequestId));
    }

    [Fact]
    public async Task Native_occurrence_authority_tracks_actual_deadline_and_is_revoked_by_a_new_generation()
    {
        var state = new InMemoryRuntimeCheckpointStoreState(); var clock = new FakeTimeProvider(Now);
        var schedules = new InMemoryRecurringTriggerScheduleStore(clock);
        await schedules.SaveAsync(Resource(1));
        var claim = Assert.Single(await schedules.ClaimDueAsync(new("pump", Now, TimeSpan.FromMinutes(1), 10)));
        clock.Advance(TimeSpan.FromSeconds(30));
        var renewed = (await schedules.RenewClaimAsync(claim, clock.GetUtcNow(), TimeSpan.FromMinutes(2)))!;
        clock.Advance(TimeSpan.FromSeconds(45));
        Assert.True(claim.VisibleAfter <= clock.GetUtcNow());
        var deadline = await state.ExecuteWriteGateAsync(_ => ValueTask.FromResult(schedules.ExecuteUnderStandaloneOccurrenceClaim(claim, clock, value => value)));
        Assert.Equal(renewed.VisibleAfter, deadline);
        await schedules.SaveAsync(Resource(2));
        var accepted = false;
        await Assert.ThrowsAsync<TriggerOperationException>(() => state.ExecuteWriteGateAsync(_ => ValueTask.FromResult(
            schedules.ExecuteUnderStandaloneOccurrenceClaim(renewed, clock, () => accepted = true))).AsTask());
        Assert.False(accepted);
        Assert.Equal(2, (await schedules.FindAsync(Resource(2).ScheduleId))!.Resource!.Generation);
    }

    private static TriggerStoreWrite Write(long revision) => new("write-" + revision, "fingerprint-" + revision, "operator", revision == 1 ? null : revision - 1,
        new("resource", "Timer", "timer", 1, JsonSerializer.SerializeToElement(new { interval = "PT5M" }), revision, 1, 1, TriggerDesiredState.Enabled,
            new(TriggerManagementKind.Studio), Now, Now, "creator", tenantId: Scope.TenantId),
        [new("subscription", "resource", "definition", "default", "native", 1, 1, true, [], new(TriggerManagementKind.Studio))]);
    private static RecurringTriggerSchedule Resource(long generation) => RecurringTriggerSchedule.ForResource(new("resource", generation, 1,
        "timer", "Elsa.StandaloneTimer", Scope.Persistence, Scope.TenantId), RecurringScheduleKind.Interval, "PT5M", Now, Now);
    private sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(Scope.Persistence); }
    private sealed class Planner(RecurringTriggerSchedule replacement) : IStandaloneSchedulePlanner
    {
        public bool Supports(string typeKey) => typeKey == "timer";
        public StandaloneSchedulePlan Plan(TriggerScope scope, TriggerAggregate? previous, TriggerStoreWrite write, RecurringTriggerSchedule? current, DateTimeOffset now) =>
            new(StandaloneScheduleAction.Replace, replacement);
    }
}

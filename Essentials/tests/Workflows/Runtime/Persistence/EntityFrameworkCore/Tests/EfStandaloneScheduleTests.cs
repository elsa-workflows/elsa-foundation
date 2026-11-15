using System.Data;
using System.Text.Json;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfStandaloneScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TriggerScope Scope = new(new("default"), null);

    [Fact]
    public async Task Scoped_observation_reads_actual_claim_columns_and_failure_backoff_without_a_write_or_lease_inference()
    {
        await using var host = await Harness.OpenAsync();
        IRecurringTriggerScheduleStore schedules = new EfRecurringTriggerScheduleStore(host.Context, new ObservationAccess(), new AdmissionCodec(), host.Clock);
        var schedule = RecurringTriggerSchedule.ForResource(new("observed-resource", 1, 1, "timer", "Elsa.StandaloneTimer",
            new("physical-shell"), "actual-tenant"), RecurringScheduleKind.Interval, "PT5M", Now, Now);
        await schedules.SaveAsync(schedule);
        var before = await host.Context.RecurringTriggerSchedules.AsNoTracking().SingleAsync();
        var initial = (await schedules.ObserveAsync(schedule.ScheduleId))!;
        Assert.Equal(schedule, initial.Schedule); Assert.Equal(0, initial.FailureCount); Assert.Null(initial.RetryAt); Assert.False(initial.OccurrenceInFlight);
        var after = await host.Context.RecurringTriggerSchedules.AsNoTracking().SingleAsync();
        Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.ContentJson, after.ContentJson);
        Assert.False(host.Context.ChangeTracker.HasChanges());
        var claim = Assert.Single(await schedules.ClaimDueAsync(new("pump", Now, TimeSpan.FromMinutes(1), 10)));
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        var expiredOwner = (await schedules.ObserveAsync(schedule.ScheduleId))!;
        Assert.True(expiredOwner.OccurrenceInFlight); Assert.Null(expiredOwner.RetryAt);
        var retryAt = host.Clock.GetUtcNow().AddMinutes(3);
        Assert.True(await schedules.ReleaseClaimAsync(claim, retryAt));
        var failed = (await schedules.ObserveAsync(schedule.ScheduleId))!;
        Assert.Equal(1, failed.FailureCount); Assert.Equal(retryAt, failed.RetryAt); Assert.False(failed.OccurrenceInFlight);
        Assert.Equal("actual-tenant", failed.Schedule.Resource!.TenantId);
        Assert.Equal("physical-shell", failed.Schedule.Resource.Scope.Value);
        var held = Assert.Single(await schedules.ClaimDueAsync(new("retry", retryAt, TimeSpan.FromMinutes(1), 10)));
        _ = await schedules.ObserveAsync(schedule.ScheduleId);
        Assert.True(await schedules.SettleClaimAsync(held, retryAt.AddMinutes(5)));
        Assert.Equal(0, (await schedules.ObserveAsync(schedule.ScheduleId))!.FailureCount);
        Assert.Null(await schedules.ObserveAsync("absent"));
    }

    private sealed class ObservationAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("physical-shell")); }

    [Fact]
    public async Task Resource_and_schedule_commit_together_and_resumption_fences_the_old_overdue_generation()
    {
        await using var host = await Harness.OpenAsync();
        await host.WriteAsync(1, StandaloneScheduleAction.Replace, generation: 1, next: Now.AddDays(-2));
        var schedule = (await host.Schedules.FindAsync(host.Id))!;
        Assert.Null(schedule.ArtifactId); Assert.Null(schedule.ExecutableNodeId); Assert.Null(schedule.StimulusType); Assert.Null(schedule.StimulusHash);
        Assert.Equal(RuntimeOperationalStateEfModule.StandaloneScheduleSchemaVersion,
            (await host.Context.RecurringTriggerSchedules.AsNoTracking().SingleAsync()).SchemaVersion);
        var claim = Assert.Single(await host.Schedules.ClaimDueAsync(new("pump", Now, TimeSpan.FromMinutes(1), 10)));
        await host.WriteAsync(2, StandaloneScheduleAction.Deactivate);
        Assert.False(await host.Schedules.SettleClaimAsync(claim, Now.AddMinutes(5)));
        await host.WriteAsync(3, StandaloneScheduleAction.Replace, generation: 3, next: Now.AddMinutes(5));
        var resumed = (await host.Schedules.FindAsync(host.Id))!;
        Assert.Equal(3, resumed.Resource!.Generation);
        Assert.Equal(Now.AddMinutes(5), resumed.NextOccurrence);
        Assert.NotEqual(schedule.BuildOccurrenceKey(), resumed.BuildOccurrenceKey());
        await using var transaction = await host.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<TriggerOperationException>(() => host.Schedules.FenceStandaloneOccurrenceAsync(claim, host.Clock, default).AsTask());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Projection_failure_rolls_back_resource_and_mutation_receipt_before_retry()
    {
        await using var host = await Harness.OpenAsync();
        var write = host.Write(1, TriggerDesiredState.Enabled);
        await using (var transaction = await host.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await host.Resources.StageWriteAsync(Scope, write, default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.Schedules.StageStandaloneScheduleWriteAsync(Scope, write, null,
                new Planner(() => throw new InvalidOperationException("Projection refused before commit.")), host.Clock, default).AsTask());
            await transaction.RollbackAsync(); host.Context.ChangeTracker.Clear();
        }
        Assert.Null(await host.Resources.FindAsync(Scope, "schedule-resource"));
        Assert.Null(await host.Resources.FindMutationAsync(Scope, "operator", write.RequestId));
        Assert.Null(await host.Schedules.FindAsync(host.Id));
        await host.WriteAsync(1, StandaloneScheduleAction.Replace, 1, Now.AddMinutes(5));
        Assert.Equal(1, (await host.Resources.FindAsync(Scope, "schedule-resource"))!.Trigger.Revision);
        Assert.NotNull(await host.Schedules.FindAsync(host.Id));
    }

    [Fact]
    public async Task Occurrence_acceptance_fences_same_token_renewals_and_rejects_an_expired_lease()
    {
        await using var host = await Harness.OpenAsync();
        await host.WriteAsync(1, StandaloneScheduleAction.Replace, 1, Now);
        var original = Assert.Single(await host.Schedules.ClaimDueAsync(new("pump", Now, TimeSpan.FromMinutes(1), 10)));
        host.Clock.Advance(TimeSpan.FromSeconds(30));
        var renewed = (await host.Schedules.RenewClaimAsync(original, host.Clock.GetUtcNow(), TimeSpan.FromMinutes(2)))!;
        host.Clock.Advance(TimeSpan.FromSeconds(45));
        Assert.True(original.VisibleAfter <= host.Clock.GetUtcNow());
        await using (var transaction = await host.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var actualDeadline = await host.Schedules.FenceStandaloneOccurrenceAsync(original, host.Clock, default);
            Assert.Equal(renewed.VisibleAfter, actualDeadline);
            await transaction.CommitAsync();
        }
        host.Clock.Advance(TimeSpan.FromMinutes(3));
        await using (var transaction = await host.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<TriggerOperationException>(() => host.Schedules.FenceStandaloneOccurrenceAsync(renewed, host.Clock, default).AsTask());
            await transaction.RollbackAsync();
        }
        Assert.Equal(Now, (await host.Schedules.FindAsync(host.Id))!.NextOccurrence);
    }

    private sealed class Planner(Func<StandaloneSchedulePlan> plan) : IStandaloneSchedulePlanner
    {
        public bool Supports(string typeKey) => typeKey == "timer";
        public StandaloneSchedulePlan Plan(TriggerScope scope, TriggerAggregate? previous, TriggerStoreWrite write, RecurringTriggerSchedule? current, DateTimeOffset now) => plan();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly TemporarySqliteDatabase database;
        public RuntimeSqliteDbContext Context { get; }
        public FakeTimeProvider Clock { get; } = new(Now);
        public EfRecurringTriggerScheduleStore Schedules { get; }
        public EfStandaloneTriggerStore Resources { get; }
        public string Id => RecurringTriggerSchedule.BuildResourceId(Scope.Persistence, "schedule-resource", Scope.TenantId);
        private Harness(TemporarySqliteDatabase database, RuntimeSqliteDbContext context)
        {
            this.database = database; Context = context;
            Schedules = new(context, new AdmissionAccess(), new AdmissionCodec(), Clock);
            Resources = new(context, new AdmissionAccess(), Clock);
        }
        public static async Task<Harness> OpenAsync()
        {
            var database = new TemporarySqliteDatabase("standalone-schedule");
            var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(database.ConnectionStringWith("Pooling=False")).Options);
            await context.Database.EnsureCreatedAsync();
            return new(database, context);
        }
        public TriggerStoreWrite Write(long revision, TriggerDesiredState desired) => new("write-" + revision, "fingerprint-" + revision, "operator", revision == 1 ? null : revision - 1,
            new("schedule-resource", "Schedule", "timer", 1, JsonSerializer.SerializeToElement(new { interval = "PT5M" }), revision, 1, 1, desired,
                new(TriggerManagementKind.Studio), Now, Now, "creator"),
            [new("subscription", "schedule-resource", "definition", "default", "native", 1, 1, true, [], new(TriggerManagementKind.Studio))]);
        public async Task WriteAsync(long revision, StandaloneScheduleAction action, long generation = 1, DateTimeOffset? next = null)
        {
            var write = Write(revision, action == StandaloneScheduleAction.Deactivate ? TriggerDesiredState.Paused : TriggerDesiredState.Enabled);
            var prior = await Resources.FindAsync(Scope, write.Trigger.Id);
            var replacement = action == StandaloneScheduleAction.Replace ? RecurringTriggerSchedule.ForResource(new(write.Trigger.Id, generation, 1,
                "timer", "Elsa.StandaloneTimer", Scope.Persistence, null), RecurringScheduleKind.Interval, "PT5M", next ?? Now.AddMinutes(5), Now) : null;
            await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Resources.StageWriteAsync(Scope, write, default);
            await Schedules.StageStandaloneScheduleWriteAsync(Scope, write, prior, new Planner(() => new(action, replacement)), Clock, default);
            await Context.SaveChangesAsync(); await transaction.CommitAsync(); Context.ChangeTracker.Clear();
        }
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await database.DisposeAsync(); }
    }
}

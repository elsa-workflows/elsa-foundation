using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Pump_failure_reason_survives_a_fresh_store_reader_and_renewal_then_clears_only_on_fenced_settlement(bool ef, bool unavailable)
    {
        await using var harness = await Harness.Create(ef);
        var store = harness.ScheduleStore();
        var schedule = FailureSchedule(); await store.SaveAsync(schedule);
        var source = new FailingScheduleSource(unavailable);
        var pump = new RecurringTriggerPumpTask(store, new InMemoryWorkflowTriggerBindingStore(), new UnusedScheduleRouter(),
            new RecurringScheduleCalculator(), Options.Create(new RecurringTriggerPumpOptions
            { SweepInterval = TimeSpan.FromSeconds(10), MaxBackoffInterval = TimeSpan.FromMinutes(5), ClaimVisibilityTimeout = TimeSpan.FromMinutes(1) }),
            harness.Clock, NullLogger<RecurringTriggerPumpTask>.Instance, source);

        await pump.ExecuteAsync(default);

        var reader = harness.ScheduleStore();
        var saved = (await reader.ObserveAsync(schedule.ScheduleId))!;
        var reason = unavailable ? RecurringTriggerScheduleFailureReason.AdmissionUnavailable : RecurringTriggerScheduleFailureReason.NoEligibleSubscribers;
        Assert.Equal(reason, saved.FailureReason); Assert.Equal(1, saved.FailureCount);
        Assert.Equal(Now.AddSeconds(10), saved.RetryAt); Assert.False(saved.OccurrenceInFlight);
        Assert.Equal(schedule.Resource, saved.Schedule.Resource);
        Assert.Equal(schedule.NextOccurrence, saved.Schedule.NextOccurrence);
        Assert.Equal(schedule.BuildOccurrenceKey(), saved.Schedule.BuildOccurrenceKey());
        Assert.Equal(schedule.BuildOccurrenceKey(), Assert.Single(source.Keys));
        Assert.Empty(await harness.Outbox.GetDeliverableAsync(new(Now, 10)));
        if (ef)
        {
            var row = await harness.Context!.RecurringTriggerSchedules.AsNoTracking().SingleAsync();
            using var content = JsonDocument.Parse(row.ContentJson);
            Assert.Equal(reason.ToString(), content.RootElement.GetProperty("failureReason").GetString());
        }
        Assert.Empty(await reader.ClaimDueAsync(new("too-early", Now, TimeSpan.FromMinutes(1), 1)));
        harness.Clock.Advance(TimeSpan.FromSeconds(10));
        source.Unavailable = !unavailable;
        await pump.ExecuteAsync(default);
        var changed = (await harness.ScheduleStore().ObserveAsync(schedule.ScheduleId))!;
        reason = unavailable ? RecurringTriggerScheduleFailureReason.NoEligibleSubscribers : RecurringTriggerScheduleFailureReason.AdmissionUnavailable;
        Assert.Equal(reason, changed.FailureReason); Assert.Equal(2, changed.FailureCount);
        Assert.Equal(Now.AddSeconds(30), changed.RetryAt);
        Assert.Equal(schedule.BuildOccurrenceKey(), changed.Schedule.BuildOccurrenceKey());
        Assert.Equal(2, source.Keys.Count); Assert.All(source.Keys, key => Assert.Equal(schedule.BuildOccurrenceKey(), key));
        harness.Clock.Advance(TimeSpan.FromSeconds(20));
        var claim = Assert.Single(await reader.ClaimDueAsync(new("peer", harness.Clock.GetUtcNow(), TimeSpan.FromMinutes(1), 1)));
        var renewed = (await reader.RenewClaimAsync(claim, harness.Clock.GetUtcNow(), TimeSpan.FromMinutes(2)))!;
        Assert.Equal(reason, (await harness.ScheduleStore().ObserveAsync(schedule.ScheduleId))!.FailureReason);
        Assert.False(await reader.SettleClaimAsync(claim, Now.AddHours(1))); // Renewal fenced the previous revision.
        Assert.True(await reader.SettleClaimAsync(renewed, Now.AddHours(1)));
        var settled = (await harness.ScheduleStore().ObserveAsync(schedule.ScheduleId))!;
        Assert.Null(settled.FailureReason); Assert.Null(settled.Schedule.FailureReason);
        Assert.Equal(0, settled.FailureCount); Assert.Null(settled.RetryAt); Assert.False(settled.OccurrenceInFlight);
        Assert.Equal(Now.AddHours(1), settled.Schedule.NextOccurrence); Assert.Equal(schedule.Resource, settled.Schedule.Resource);
        Assert.False(await reader.ReleaseClaimWithFailureAsync(renewed, Now.AddHours(2), reason));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Legacy_unclassified_release_keeps_its_schedule_content_bytes_and_does_not_invent_a_cause(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var store = harness.ScheduleStore(); var schedule = FailureSchedule(); await store.SaveAsync(schedule);
        var original = ef ? (await harness.Context!.RecurringTriggerSchedules.AsNoTracking().SingleAsync()).ContentJson : JsonSerializer.Serialize(schedule);
        var claim = Assert.Single(await store.ClaimDueAsync(new("legacy", Now, TimeSpan.FromMinutes(1), 1)));
        Assert.True(await store.ReleaseClaimAsync(claim, Now.AddSeconds(10)));
        var observed = (await harness.ScheduleStore().ObserveAsync(schedule.ScheduleId))!;
        Assert.Equal(1, observed.FailureCount); Assert.Null(observed.FailureReason);
        var retained = ef ? (await harness.Context!.RecurringTriggerSchedules.AsNoTracking().SingleAsync()).ContentJson : JsonSerializer.Serialize(observed.Schedule);
        Assert.Equal(original, retained);
        using var content = JsonDocument.Parse(retained);
        Assert.False(content.RootElement.TryGetProperty("failureReason", out _));
        Assert.False(content.RootElement.TryGetProperty("FailureReason", out _));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_stale_admission_failure_cannot_label_a_new_schedule_generation(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var store = harness.ScheduleStore(); var schedule = FailureSchedule(); await store.SaveAsync(schedule);
        var claim = Assert.Single(await store.ClaimDueAsync(new("old", Now, TimeSpan.FromMinutes(1), 1)));
        var owner = schedule.Resource!;
        var replacement = schedule with { Resource = new(owner.TriggerId, 8, owner.ConfigurationRevision, owner.SourceTypeKey,
            owner.SourceFamily, owner.Scope, owner.TenantId, owner.TimeZoneId), NextOccurrence = Now.AddHours(1) };
        await store.SaveAsync(replacement);
        Assert.False(await store.ReleaseClaimWithFailureAsync(claim, Now.AddSeconds(10), RecurringTriggerScheduleFailureReason.NoEligibleSubscribers));
        var observed = (await harness.ScheduleStore().ObserveAsync(schedule.ScheduleId))!;
        Assert.Equal(replacement, observed.Schedule); Assert.Null(observed.FailureReason);
        Assert.Equal(0, observed.FailureCount); Assert.Null(observed.RetryAt);
    }

    private static RecurringTriggerSchedule FailureSchedule() => RecurringTriggerSchedule.ForResource(
        new("schedule-failure", 7, 1, "timer", "Test.Schedule", new(PersistenceScope.DefaultValue), null),
        RecurringScheduleKind.Interval, "PT5M", Now.AddHours(-1), Now.AddDays(-1));

    private sealed class FailingScheduleSource(bool unavailable) : IStandaloneScheduleSource
    {
        public bool Unavailable { get; set; } = unavailable;
        public List<string> Keys { get; } = [];
        public bool Supports(string sourceTypeKey, string sourceFamily) => sourceTypeKey == "timer" && sourceFamily == "Test.Schedule";
        public ValueTask<RuntimeIngressAdmissionReceipt> AdmitAsync(PersistenceScope scope, RecurringTriggerOccurrenceClaim claim, CancellationToken cancellationToken = default)
        {
            Assert.Equal(claim.Schedule.Resource!.Scope, scope);
            Keys.Add(claim.Schedule.BuildOccurrenceKey());
            return ValueTask.FromException<RuntimeIngressAdmissionReceipt>(Unavailable
                ? new IOException("Private storage failure must not enter an operator finding.")
                : new StandaloneTriggerAdmissionRefusedException("no-eligible-subscribers", []));
        }
    }
    private sealed class UnusedScheduleRouter : IStimulusRouter
    {
        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A resource-owned schedule must use its installed source.");
    }
    private sealed partial class Harness
    {
        private InMemoryRecurringTriggerScheduleStore? failureSchedules;
        public IRecurringTriggerScheduleStore ScheduleStore() => connection is null ? failureSchedules ??= new(Clock)
            : new EfRecurringTriggerScheduleStore(Open(), new Accessor(new(PersistenceScope.DefaultValue)), new Codec(), Clock);
    }
}

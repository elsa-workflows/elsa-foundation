using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

/// <summary>Cursor decisions at the installed source-module seam; provider acceptance itself is qualified in provider tests.</summary>
public sealed class StandaloneRecurringPumpTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Known_zero_and_unknown_failures_keep_one_overdue_occurrence_with_backoff_then_skip_backlog_after_acceptance(bool unknown)
    {
        var clock = new FakeTimeProvider(Now);
        var store = new InMemoryRecurringTriggerScheduleStore(clock);
        var schedule = Resource(Now.AddDays(-3)); await store.SaveAsync(schedule);
        var keys = new List<string>();
        var source = new Source(claim =>
        {
            keys.Add(claim.Schedule.BuildOccurrenceKey());
            if (keys.Count == 1)
            {
                if (unknown) throw new IOException("Temporary source preparation failure.");
                throw new StandaloneTriggerAdmissionRefusedException("no-eligible-subscribers", []);
            }
            var accepted = Accepted(claim);
            clock.Advance(TimeSpan.FromHours(4)); // A delayed acknowledgement must not create a backlog.
            return accepted;
        });
        var pump = Pump(store, source, clock);

        await pump.ExecuteAsync(default);
        Assert.Equal(schedule.NextOccurrence, (await store.FindAsync(schedule.ScheduleId))!.NextOccurrence);
        var failed = (await store.ObserveAsync(schedule.ScheduleId))!;
        Assert.Equal(unknown ? RecurringTriggerScheduleFailureReason.AdmissionUnavailable : RecurringTriggerScheduleFailureReason.NoEligibleSubscribers,
            failed.FailureReason);
        Assert.Equal(1, failed.FailureCount);
        Assert.Equal(Now.AddSeconds(10), failed.RetryAt);
        Assert.Equal(schedule.Resource, failed.Schedule.Resource);
        Assert.Equal(schedule.BuildOccurrenceKey(), failed.Schedule.BuildOccurrenceKey());
        await pump.ExecuteAsync(default); Assert.Single(keys);
        clock.Advance(TimeSpan.FromSeconds(10));
        await pump.ExecuteAsync(default);

        Assert.Equal(2, keys.Count); Assert.Equal(keys[0], keys[1]);
        Assert.Equal(clock.GetUtcNow().AddMinutes(5), (await store.FindAsync(schedule.ScheduleId))!.NextOccurrence);
        var recovered = (await store.ObserveAsync(schedule.ScheduleId))!;
        Assert.Null(recovered.FailureReason); Assert.Null(recovered.Schedule.FailureReason);
        Assert.Equal(0, recovered.FailureCount); Assert.Null(recovered.RetryAt);
        await pump.ExecuteAsync(default); Assert.Equal(2, keys.Count);
    }

    [Fact]
    public async Task Exhausting_an_accepted_old_occurrence_cannot_delete_a_generation_rearmed_while_the_source_was_running()
    {
        var clock = new FakeTimeProvider(Now);
        var store = new InMemoryRecurringTriggerScheduleStore(clock);
        var old = Resource(Now.AddMinutes(-1), kind: RecurringScheduleKind.Cron);
        await store.SaveAsync(old);
        var replacement = Resource(Now.AddHours(1), generation: 2, kind: RecurringScheduleKind.Cron);
        var source = new Source(claim =>
        {
            store.SaveAsync(replacement).GetAwaiter().GetResult();
            return Accepted(claim);
        });
        var pump = Pump(store, source, clock, new Exhausted());

        await pump.ExecuteAsync(default);

        Assert.Equal(replacement, await store.FindAsync(old.ScheduleId));
    }

    [Fact]
    public async Task A_backward_clock_after_acceptance_never_rewinds_the_resource_cursor()
    {
        var clock = new AdjustableClock(Now); var store = new InMemoryRecurringTriggerScheduleStore(clock);
        var schedule = Resource(Now); await store.SaveAsync(schedule);
        var source = new Source(claim => { var accepted = Accepted(claim); clock.Now = Now.AddHours(-1); return accepted; });
        await Pump(store, source, clock).ExecuteAsync(default);
        Assert.Equal(schedule.NextOccurrence.AddMinutes(5), (await store.FindAsync(schedule.ScheduleId))!.NextOccurrence);
    }
    private sealed class AdjustableClock(DateTimeOffset initial) : TimeProvider
    {
        internal DateTimeOffset Now = initial;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static RecurringTriggerPumpTask Pump(IRecurringTriggerScheduleStore store, IStandaloneScheduleSource source,
        TimeProvider clock, IRecurringScheduleCalculator? calculator = null) => new(store, new InMemoryWorkflowTriggerBindingStore(),
        new UnusedRouter(), calculator ?? new RecurringScheduleCalculator(), Microsoft.Extensions.Options.Options.Create(new RecurringTriggerPumpOptions
        { SweepInterval = TimeSpan.FromSeconds(10), MaxBackoffInterval = TimeSpan.FromMinutes(5), ClaimVisibilityTimeout = TimeSpan.FromMinutes(1) }),
        clock, NullLogger<RecurringTriggerPumpTask>.Instance, source);

    private static RecurringTriggerSchedule Resource(DateTimeOffset due, long generation = 1, RecurringScheduleKind kind = RecurringScheduleKind.Interval) =>
        RecurringTriggerSchedule.ForResource(new("trigger", generation, 1, kind == RecurringScheduleKind.Interval ? "timer" : "cron", "Test.Schedule",
            new(PersistenceScope.DefaultValue), null), kind, kind == RecurringScheduleKind.Interval ? "PT5M" : "0 9 * * *", due, Now.AddDays(-4));

    private static RuntimeIngressAdmissionReceipt Accepted(RecurringTriggerOccurrenceClaim claim)
    {
        var schedule = claim.Schedule; var owner = schedule.Resource!;
        var actor = new WorkflowExecutionAuthoritySnapshot("runtime.recurring-trigger", "trigger");
        var payload = JsonSerializer.SerializeToElement(new { dueAt = schedule.NextOccurrence });
        var metadata = JsonSerializer.SerializeToElement(new { });
        var schema = new ValueTypeDescriptor("Test.Schedule", schemaVersion: 1);
        var source = new RuntimeStandaloneIngressSource(1, "admission", "occurrence", Now, owner.Scope, null, new(owner.Scope.Value), owner.TriggerId,
            owner.Generation, 1, owner.SourceFamily, schedule.BuildOccurrenceKey(), RuntimeStandaloneIngressSource.ComputeRequestFingerprint(owner.Scope, null,
                owner.TriggerId, owner.SourceFamily, schedule.BuildOccurrenceKey(), schema, RuntimeIngressAdmission.ComputeSubmittedIntent(payload, metadata), actor), schema,
            JsonSerializer.SerializeToElement(new { payload, metadata, trigger = new { typeKey = owner.SourceTypeKey, configurationRevision = owner.ConfigurationRevision } }, Web),
            RuntimeIngressAdmissionPolicy.Independent, actor);
        var identity = new RuntimeSubscriptionStartIdentity(1, owner.Scope, source.OutboxItemId, "subscription");
        var material = new WorkflowExecutableIdentity("artifact", "producer", "version", "1", "hash");
        var recipient = new RuntimeSubscriptionRecipient(identity.Digest, "subscription", 1, 1, "definition", "native", "slot", 1, "activation", material,
            new("reference", "definition", "version", "1", "definition", "version", "1", "activation", "slot"),
            RuntimeIngressAdmission.ComputeInputContractFingerprint(new(1, [])), 1, 1, new Dictionary<string, JsonElement>(), actor, null, new(owner.Scope.Value), identity);
        var delivery = new RuntimeSubscriptionDelivery(1, source.OutboxItemId, source.Fingerprint, owner.Scope, [recipient], [], [new(recipient.RecipientId, RuntimeRecipientPhase.Pending)]);
        return new(RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, delivery, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1))), false);
    }
    private sealed class Source(Func<RecurringTriggerOccurrenceClaim, RuntimeIngressAdmissionReceipt> admit) : IStandaloneScheduleSource
    {
        public bool Supports(string sourceTypeKey, string sourceFamily) => sourceFamily == "Test.Schedule";
        public ValueTask<RuntimeIngressAdmissionReceipt> AdmitAsync(PersistenceScope scope, RecurringTriggerOccurrenceClaim claim, CancellationToken cancellationToken = default) => ValueTask.FromResult(admit(claim));
    }
    private sealed class UnusedRouter : IStimulusRouter
    {
        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Resource schedules must never route through workflow bindings.");
    }
    private sealed class Exhausted : IRecurringScheduleCalculator
    {
        public DateTimeOffset? ComputeNext(RecurringScheduleKind kind, string expression, DateTimeOffset after) => null;
    }
}

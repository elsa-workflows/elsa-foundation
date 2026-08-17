using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class RecurringTriggerPumpTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryWorkflowTriggerBindingStore _bindingStore = new();

    [Fact]
    public async Task Sweep_FiresDueSchedule_StartOnly_WithExpectedRequestShape()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        await SeedAsync(store, Schedule("s1", Now.AddMinutes(-1)));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        var request = Assert.Single(router.Requests);
        Assert.Equal("Timer", request.StimulusType);
        Assert.Equal("hash-s1", request.StimulusHash);
        Assert.Equal(StimulusRoutingMode.StartOnly, request.Mode);
        Assert.Equal("runtime.recurring-trigger", request.RequestedBy);
        // Idempotency key is scoped to the claimed occurrence so a duplicate delivery cannot double-start it.
        Assert.Equal($"recurring:s1:{Now.AddMinutes(-1).UtcTicks}", request.IdempotencyKey);
        // Owner scoping: the fire pre-matches the schedule's own binding so the router never hash-broadcasts it.
        Assert.Equal("node-s1", Assert.Single(request.MatchedTriggerBindings!).ExecutableNodeId);
    }

    [Fact]
    public async Task Sweep_DropsOccurrence_WhenOwningBindingIsMissing()
    {
        // Index drift (e.g. mid-republish): the claimed occurrence is dropped rather than hash-broadcast to
        // whatever other artifacts share the stimulus hash; the cursor stays advanced (at-most-once).
        var store = new InMemoryRecurringTriggerScheduleStore();
        await store.SaveAsync(Schedule("s1", Now.AddMinutes(-1), expression: "PT1M"));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Empty(router.Requests);
        Assert.Equal(Now.AddMinutes(1), (await store.FindAsync("s1"))!.NextOccurrence);
    }

    [Fact]
    public async Task Sweep_AdvancesCursorPastNow_FiringAtMostOnce_NoBacklogReplay()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        // Due 10 minutes ago on a 1-minute interval: a naive previous+interval walk would fire ~10 times.
        await SeedAsync(store, Schedule("s1", Now.AddMinutes(-10), expression: "PT1M"));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        // Exactly one fire, and the cursor is advanced to the first occurrence strictly after now (not +1m from
        // the stale cursor), so the next sweep at the same instant finds nothing due.
        Assert.Single(router.Requests);
        var advanced = await store.FindAsync("s1");
        Assert.Equal(Now.AddMinutes(1), advanced!.NextOccurrence);

        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Single(router.Requests);
    }

    [Fact]
    public async Task Sweep_DoesNotFire_ScheduleNotYetDue()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        await store.SaveAsync(Schedule("future", Now.AddMinutes(10)));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Empty(router.Requests);
    }

    [Fact]
    public async Task Sweep_ParksScheduleWithoutDeletingIt_WhenCronExhausted()
    {
        // Feb 30 never occurs: ComputeNext returns null. The pump used to DELETE the row here — a write to
        // activation-owned serving state from outside any activation lifecycle (FR-B-006 writer census,
        // finding 2). It now parks the row: never due again, but still there for the coordinator to own.
        var store = new InMemoryRecurringTriggerScheduleStore();
        await store.SaveAsync(Schedule("dead", Now.AddMinutes(-1), kind: RecurringScheduleKind.Cron, expression: "0 0 30 2 *"));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Empty(router.Requests);
        var parked = await store.FindAsync("dead");
        Assert.NotNull(parked);
        Assert.Equal(RecurringTriggerPumpTask.NeverOccurs, parked.NextOccurrence);
        Assert.Empty(await store.ListDueAsync(DateTimeOffset.MaxValue.AddDays(-1), 10));
    }

    [Fact]
    public async Task Sweep_ParksScheduleWithoutDeletingIt_WhenExpressionInvalid()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        await store.SaveAsync(Schedule("bad", Now.AddMinutes(-1), expression: "not-a-duration"));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Empty(router.Requests);
        var parked = await store.FindAsync("bad");
        Assert.NotNull(parked);
        Assert.Equal(RecurringTriggerPumpTask.NeverOccurs, parked.NextOccurrence);
    }

    [Fact]
    public async Task Sweep_ParkingIsIdempotent_AndAnAlreadyParkedScheduleIsNeverDueAgain()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        await store.SaveAsync(Schedule("bad", Now.AddMinutes(-1), expression: "not-a-duration"));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(store, router);

        await pump.ExecuteAsync(CancellationToken.None);
        await pump.ExecuteAsync(CancellationToken.None);

        var parked = await store.FindAsync("bad");
        Assert.Equal(RecurringTriggerPumpTask.NeverOccurs, parked!.NextOccurrence);
        Assert.Empty(router.Requests);
    }

    [Fact]
    public async Task ParkedActivationOwnedSchedule_SurvivesRestoreAndCompensation()
    {
        // The point of parking instead of deleting: the row is still part of its activation's projection, so a
        // later restore (compensation re-activating the predecessor) finds it and can serve it again. A deleted
        // row was gone for good — the coordinator would re-activate an empty projection and the workflow's
        // recurring start would be silently lost.
        var store = new InMemoryRecurringTriggerScheduleStore();
        var schedule = PublicationSchedule("bad", "activation-1") with { Expression = "not-a-duration" };
        await store.PrepareActivationAsync("activation-1", [schedule]);
        await store.ActivateAsync("activation-1", replacedActivationId: null);
        var router = new FakeRouter();
        var (pump, _) = CreatePump(store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        var parked = await store.FindAsync(schedule.ScheduleId);
        Assert.NotNull(parked);
        Assert.Equal("activation-1", parked.ActivationId);
        Assert.Equal("slot-default", parked.SlotId);

        // Compensation restores the activation's projection; the parked row is still a member of it.
        await store.ActivateAsync("activation-1", replacedActivationId: null);
        var restored = Assert.Single(await store.ListByActivationAsync("activation-1"));
        Assert.Equal(schedule.ScheduleId, restored.ScheduleId);
        Assert.True(restored.IsActive);
    }

    [Fact]
    public async Task Sweep_AdvancesTheFireCursorWithoutTouchingActivationState()
    {
        // TryAdvanceAsync is the carved-out operational write: it moves the cursor and nothing else. Activation
        // ownership, slot and serving visibility are the coordinator's, and a fire must not disturb them.
        var store = new InMemoryRecurringTriggerScheduleStore();
        var schedule = PublicationSchedule("s1", "activation-1") with { Expression = "PT1M" };
        await store.PrepareActivationAsync("activation-1", [schedule]);
        await store.ActivateAsync("activation-1", replacedActivationId: null);
        await _bindingStore.SaveAsync(Binding(schedule));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Single(router.Requests);
        var advanced = await store.FindAsync(schedule.ScheduleId);
        Assert.Equal(Now.AddMinutes(1), advanced!.NextOccurrence);
        Assert.Equal("activation-1", advanced.ActivationId);
        Assert.Equal("slot-default", advanced.SlotId);
        Assert.True(advanced.IsActive);
        Assert.Equal(schedule.Expression, advanced.Expression);
    }

    [Fact]
    public async Task Sweep_NeverThrows_WhenRouterThrows_ButCursorAlreadyAdvanced()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        await SeedAsync(store, Schedule("s1", Now.AddMinutes(-1), expression: "PT1M"));
        var router = new FakeRouter { Throw = new InvalidOperationException("boom") };
        var (pump, _) = CreatePump(store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        // At-most-once: the occurrence was claimed (cursor advanced) before the throwing dispatch, so it is not
        // replayed; the next occurrence still stands.
        var advanced = await store.FindAsync("s1");
        Assert.Equal(Now.AddMinutes(1), advanced!.NextOccurrence);
    }

    [Fact]
    public async Task Sweep_NeverThrows_WhenStoreThrows_AndWidensInterval()
    {
        var store = new ThrowingScheduleStore();
        var (pump, _) = CreatePump(store, new FakeRouter());

        Assert.Equal(TimeSpan.FromSeconds(10), pump.CurrentSweepInterval);
        await pump.ExecuteAsync(CancellationToken.None); // 1 failure -> 10s * 2^0 = 10s
        await pump.ExecuteAsync(CancellationToken.None); // 2 failures -> 10s * 2^1 = 20s
        Assert.True(pump.CurrentSweepInterval > TimeSpan.FromSeconds(10));

        store.Healthy = true;
        await pump.ExecuteAsync(CancellationToken.None); // clean -> reset
        Assert.Equal(TimeSpan.FromSeconds(10), pump.CurrentSweepInterval);
    }

    [Fact]
    public async Task Sweep_BoundsFires_ByMaxSchedulesPerTick()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        for (var i = 0; i < 5; i++)
            await SeedAsync(store, Schedule($"s{i}", Now.AddMinutes(-1)));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(store, router, maxSchedulesPerTick: 2);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, router.Requests.Count);
    }

    [Fact]
    public async Task DueSchedules_SwitchOnlyWhenPublicationAuthorityChanges()
    {
        var store = new InMemoryRecurringTriggerScheduleStore();
        var oldSchedule = PublicationSchedule("old", "publication-old");
        var candidateSchedule = PublicationSchedule("new", "publication-new");

        await store.PrepareActivationAsync("publication-old", [oldSchedule]);
        await store.PrepareActivationAsync("publication-new", [candidateSchedule]);
        Assert.Empty(await store.ListDueAsync(Now, 10));

        await store.ActivateAsync("publication-old", replacedActivationId: null);
        Assert.Equal("publication-old", Assert.Single(await store.ListDueAsync(Now, 10)).ActivationId);

        await store.ActivateAsync("publication-new", "publication-old");
        Assert.Equal("publication-new", Assert.Single(await store.ListDueAsync(Now, 10)).ActivationId);

        // Compensation restores the retired projection and makes the failed candidate invisible again.
        await store.ActivateAsync("publication-old", "publication-new");
        Assert.Equal("publication-old", Assert.Single(await store.ListDueAsync(Now, 10)).ActivationId);
    }

    private (RecurringTriggerPumpTask Pump, MutableTimeProvider Clock) CreatePump(
        IRecurringTriggerScheduleStore store,
        FakeRouter router,
        int maxSchedulesPerTick = 100)
    {
        var clock = new MutableTimeProvider(Now);
        var options = Microsoft.Extensions.Options.Options.Create(new RecurringTriggerPumpOptions
        {
            SweepInterval = TimeSpan.FromSeconds(10),
            MaxBackoffInterval = TimeSpan.FromMinutes(5),
            MaxSchedulesPerTick = maxSchedulesPerTick
        });
        var pump = new RecurringTriggerPumpTask(
            store, _bindingStore, router, new RecurringScheduleCalculator(), options, clock, NullLogger<RecurringTriggerPumpTask>.Instance);
        return (pump, clock);
    }

    // Saves the schedule together with the trigger binding it owns — a fire only dispatches when its owning
    // binding exists in the index.
    private async Task SeedAsync(IRecurringTriggerScheduleStore store, RecurringTriggerSchedule schedule)
    {
        await store.SaveAsync(schedule);
        await _bindingStore.SaveAsync(new WorkflowTriggerBinding(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(schedule.ArtifactId, schedule.ExecutableNodeId, schedule.StimulusHash),
            ArtifactId: schedule.ArtifactId,
            DefinitionId: "definition-1",
            ArtifactVersion: "1.0.0",
            ArtifactHash: "sha256:artifact",
            ExecutableNodeId: schedule.ExecutableNodeId,
            StimulusType: schedule.StimulusType,
            StimulusHash: schedule.StimulusHash,
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: Now,
            ActivationId: schedule.ActivationId,
            SlotId: schedule.SlotId));
    }

    private static WorkflowTriggerBinding Binding(RecurringTriggerSchedule schedule) =>
        new(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(
                schedule.ActivationId!, schedule.ArtifactId, schedule.ExecutableNodeId, schedule.StimulusHash),
            ArtifactId: schedule.ArtifactId,
            DefinitionId: "definition-1",
            ArtifactVersion: "1.0.0",
            ArtifactHash: "sha256:artifact",
            ExecutableNodeId: schedule.ExecutableNodeId,
            StimulusType: schedule.StimulusType,
            StimulusHash: schedule.StimulusHash,
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: Now,
            ActivationId: schedule.ActivationId,
            SlotId: schedule.SlotId);

    private static RecurringTriggerSchedule Schedule(
        string id,
        DateTimeOffset next,
        RecurringScheduleKind kind = RecurringScheduleKind.Interval,
        string expression = "PT5M") => new(
        ScheduleId: id,
        ArtifactId: "artifact-1",
        ExecutableNodeId: $"node-{id}",
        StimulusType: "Timer",
        StimulusHash: $"hash-{id}",
        Kind: kind,
        Expression: expression,
        NextOccurrence: next,
        CreatedAt: Now);

    private static RecurringTriggerSchedule PublicationSchedule(string id, string activationId) =>
        Schedule(id, Now.AddMinutes(-1)) with
        {
            ScheduleId = RecurringTriggerSchedule.BuildId(activationId, "artifact-1", $"node-{id}"),
            ActivationId = activationId,
            SlotId = "slot-default",
            IsActive = false
        };

    private sealed class FakeRouter : IStimulusRouter
    {
        public List<StimulusDispatchRequest> Requests { get; } = new();
        public Exception? Throw { get; set; }

        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Throw is not null)
                throw Throw;
            return new ValueTask<StimulusRoutingResult>(new StimulusRoutingResult([], []));
        }
    }

    private sealed class ThrowingScheduleStore : IRecurringTriggerScheduleStore
    {
        public bool Healthy { get; set; }

        public ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default) =>
            new(schedule);

        public ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) =>
            Healthy
                ? new ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>>(Array.Empty<RecurringTriggerSchedule>())
                : throw new InvalidOperationException("store down");

        public ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default) =>
            new((RecurringTriggerSchedule?)null);

        public ValueTask<bool> TryAdvanceAsync(string scheduleId, DateTimeOffset expectedNextOccurrence, DateTimeOffset newNextOccurrence, CancellationToken cancellationToken = default) =>
            new(false);

        public ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}

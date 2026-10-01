using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class RecurringTriggerPumpTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);
    // The first failure's backoff: SweepInterval * 2^0.
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(10);

    private readonly InMemoryWorkflowTriggerBindingStore _bindingStore = new();
    private readonly InMemoryRecurringTriggerScheduleStore _store = new();

    [Fact]
    public async Task Sweep_FiresDueSchedule_StartOnly_WithExpectedRequestShape()
    {
        await SeedAsync(Schedule("s1", Now.AddMinutes(-1)));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(_store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        var request = Assert.Single(router.Requests);
        Assert.Equal("Timer", request.StimulusType);
        Assert.Equal("hash-s1", request.StimulusHash);
        Assert.Equal(StimulusRoutingMode.StartOnly, request.Mode);
        Assert.Equal("runtime.recurring-trigger", request.RequestedBy);
        // Idempotency key is scoped to the occurrence so a repeated fire of it cannot double-start it.
        Assert.Equal(Key("s1", Now.AddMinutes(-1)), request.IdempotencyKey);
        // Owner scoping: the fire pre-matches the schedule's own binding so the router never hash-broadcasts it.
        Assert.Equal("node-s1", Assert.Single(request.MatchedTriggerBindings!).ExecutableNodeId);
    }

    [Fact]
    public async Task Sweep_KeysASlotScopedOccurrence_ByItsTrigger_AndStartsItWhicheverArtifactServesIt()
    {
        // #2198: the key names the trigger (slot, node, stimulus), not the publication, so a republish's fire of an occurrence
        // and the replaced publication's fire of it converge on one start.
        var schedule = PublicationSchedule("s1", "publication-a") with { IsActive = true };
        await SeedAsync(schedule);
        var router = new FakeRouter();
        var (pump, _) = CreatePump(_store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        var request = Assert.Single(router.Requests);
        Assert.Equal($"recurring:slot-default:node-s1:hash-s1:{schedule.NextOccurrence.UtcTicks}", request.IdempotencyKey);
        Assert.Equal(StimulusStartKeyScope.Occurrence, request.StartKeyScope);
    }

    [Fact]
    public async Task Sweep_KeepsOccurrence_WhenOwningBindingIsMissing_AndFiresItOnceTheBindingIsBack()
    {
        // Index drift (e.g. mid-republish): the occurrence is neither hash-broadcast to whatever other artifacts share the
        // stimulus hash nor dropped; it stays in the cursor and fires against the refreshed index (#2198).
        var due = Now.AddMinutes(-1);
        await _store.SaveAsync(Schedule("s1", due, expression: "PT1M"));
        var router = new FakeRouter();
        var (pump, clock) = CreatePump(_store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Empty(router.Requests);
        Assert.Equal(due, (await _store.FindAsync("s1"))!.NextOccurrence);

        await SaveBindingAsync(Schedule("s1", due));
        clock.Advance(FirstBackoff);
        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(Key("s1", due), Assert.Single(router.Requests).IdempotencyKey);
        Assert.Equal(clock.GetUtcNow().AddMinutes(1), (await _store.FindAsync("s1"))!.NextOccurrence);
    }

    [Fact]
    public async Task Sweep_FiresTheDueOccurrenceOnce_AndAdvancesPastNow_NoBacklogReplay()
    {
        // Due 10 minutes ago on a 1-minute interval: a naive previous+interval walk would fire ~10 times.
        await SeedAsync(Schedule("s1", Now.AddMinutes(-10), expression: "PT1M"));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(_store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        // Exactly one fire, and the cursor is advanced to the first occurrence strictly after now (not +1m from
        // the stale cursor), so the next sweep at the same instant finds nothing due.
        Assert.Single(router.Requests);
        var advanced = await _store.FindAsync("s1");
        Assert.Equal(Now.AddMinutes(1), advanced!.NextOccurrence);

        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Single(router.Requests);
    }

    [Fact]
    public async Task Sweep_DoesNotFire_ScheduleNotYetDue()
    {
        await _store.SaveAsync(Schedule("future", Now.AddMinutes(10)));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(_store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Empty(router.Requests);
    }

    [Fact]
    public async Task Sweep_RoutesTheLastOccurrence_ThenDeletesTheSchedule_WhenCronIsExhausted()
    {
        // Feb 30 never occurs: ComputeNext returns null, so the occurrence in the cursor is the last one. It is still fired
        // (#2198), and only then is the schedule removed rather than left due.
        var last = Now.AddMinutes(-1);
        await SeedAsync(Schedule("dead", last, kind: RecurringScheduleKind.Cron, expression: "0 0 30 2 *"));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(_store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(Key("dead", last), Assert.Single(router.Requests).IdempotencyKey);
        Assert.Null(await _store.FindAsync("dead"));
    }

    [Fact]
    public async Task Sweep_KeepsTheLastOccurrence_WhenItsRouteThrows_AndDeletesTheScheduleOnlyOnceItIsRouted()
    {
        // The direction that could pass for success: an exhausted schedule whose last fire failed must not be deleted.
        var last = Now.AddMinutes(-1);
        await SeedAsync(Schedule("dead", last, kind: RecurringScheduleKind.Cron, expression: "0 0 30 2 *"));
        var router = new FakeRouter { Throw = new InvalidOperationException("boom") };
        var (pump, clock) = CreatePump(_store, router);

        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(last, (await _store.FindAsync("dead"))!.NextOccurrence);

        router.Throw = null;
        clock.Advance(FirstBackoff);
        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal([Key("dead", last), Key("dead", last)], router.Requests.Select(request => request.IdempotencyKey));
        Assert.Null(await _store.FindAsync("dead"));
    }

    [Fact]
    public async Task Sweep_DeletesSchedule_WhenExpressionInvalid()
    {
        await _store.SaveAsync(Schedule("bad", Now.AddMinutes(-1), expression: "not-a-duration"));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(_store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Empty(router.Requests);
        Assert.Null(await _store.FindAsync("bad"));
    }

    [Fact]
    public async Task Sweep_KeepsOccurrence_WhenRouterThrows_AndRetriesItAfterBackoff()
    {
        var due = Now.AddMinutes(-1);
        await SeedAsync(Schedule("s1", due, expression: "PT1M"));
        var router = new FakeRouter { Throw = new InvalidOperationException("boom") };
        var (pump, clock) = CreatePump(_store, router);

        // A failed start never escapes the sweep, and it does not drop the occurrence (#2198).
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(due, (await _store.FindAsync("s1"))!.NextOccurrence);

        // Released with backoff: the next sweep at the same instant leaves it alone.
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Single(router.Requests);

        router.Throw = null;
        clock.Advance(FirstBackoff);
        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal([Key("s1", due), Key("s1", due)], router.Requests.Select(request => request.IdempotencyKey));
        Assert.Equal(clock.GetUtcNow().AddMinutes(1), (await _store.FindAsync("s1"))!.NextOccurrence);
    }

    [Fact]
    public async Task Sweep_ThatDiesBeforeRouting_LeavesTheOccurrenceToAPeerOnceTheLeaseLapses()
    {
        var due = Now.AddMinutes(-1);
        await SeedAsync(Schedule("s1", due, expression: "PT1M"));
        var (dying, _) = CreatePump(_store, new FakeRouter { DieBeforeRouting = true });
        var peerRouter = new FakeRouter();
        var (peer, peerClock) = CreatePump(_store, peerRouter);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dying.ExecuteAsync(CancellationToken.None));
        Assert.Equal(due, (await _store.FindAsync("s1"))!.NextOccurrence);

        // The dead node's claim still holds the occurrence until its lease lapses.
        await peer.ExecuteAsync(CancellationToken.None);
        Assert.Empty(peerRouter.Requests);

        peerClock.Advance(Lease + TimeSpan.FromSeconds(1));
        await peer.ExecuteAsync(CancellationToken.None);

        Assert.Equal(Key("s1", due), Assert.Single(peerRouter.Requests).IdempotencyKey);
        Assert.Equal(peerClock.GetUtcNow().AddMinutes(1), (await _store.FindAsync("s1"))!.NextOccurrence);
    }

    [Fact]
    public async Task Sweep_ThatDiesAfterRouting_IsRepeatedByAPeerWithTheSameKey()
    {
        var due = Now.AddMinutes(-1);
        await SeedAsync(Schedule("s1", due, expression: "PT1M"));
        var dyingRouter = new FakeRouter { Throw = new OperationCanceledException("host stopping") };
        var (dying, _) = CreatePump(_store, dyingRouter);
        var peerRouter = new FakeRouter();
        var (peer, peerClock) = CreatePump(_store, peerRouter);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dying.ExecuteAsync(CancellationToken.None));
        peerClock.Advance(Lease + TimeSpan.FromSeconds(1));
        await peer.ExecuteAsync(CancellationToken.None);

        // The repeat carries the first fire's key, which is what lets the keyed start converge on its execution.
        Assert.Equal(Key("s1", due), Assert.Single(dyingRouter.Requests).IdempotencyKey);
        Assert.Equal(Key("s1", due), Assert.Single(peerRouter.Requests).IdempotencyKey);
    }

    [Fact]
    public async Task TwoPumps_OnOneOccurrence_RouteItOnce()
    {
        await SeedAsync(Schedule("s1", Now.AddMinutes(-1)));
        var peerRouter = new FakeRouter();
        var (peer, _) = CreatePump(_store, peerRouter);
        var router = new FakeRouter { WhileRouting = () => peer.ExecuteAsync(CancellationToken.None) };
        var (pump, _) = CreatePump(_store, router);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Single(router.Requests);
        Assert.Empty(peerRouter.Requests);
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
        for (var i = 0; i < 5; i++)
            await SeedAsync(Schedule($"s{i}", Now.AddMinutes(-1)));
        var router = new FakeRouter();
        var (pump, _) = CreatePump(_store, router, maxSchedulesPerTick: 2);

        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, router.Requests.Count);
    }

    [Fact]
    public async Task DueSchedules_SwitchOnlyWhenPublicationAuthorityChanges()
    {
        var oldSchedule = PublicationSchedule("old", "publication-old");
        var candidateSchedule = PublicationSchedule("new", "publication-new");

        await _store.PrepareActivationAsync("publication-old", [oldSchedule]);
        await _store.PrepareActivationAsync("publication-new", [candidateSchedule]);
        Assert.Empty(await ActiveActivationsAsync(oldSchedule, candidateSchedule));

        await _store.ActivateAsync("publication-old", replacedActivationId: null);
        Assert.Equal("publication-old", Assert.Single(await ActiveActivationsAsync(oldSchedule, candidateSchedule)));

        await _store.ActivateAsync("publication-new", "publication-old");
        Assert.Equal("publication-new", Assert.Single(await ActiveActivationsAsync(oldSchedule, candidateSchedule)));

        // Compensation restores the retired projection and makes the failed candidate invisible again.
        await _store.ActivateAsync("publication-old", "publication-new");
        Assert.Equal("publication-old", Assert.Single(await ActiveActivationsAsync(oldSchedule, candidateSchedule)));
    }

    private async Task<IReadOnlyList<string?>> ActiveActivationsAsync(params RecurringTriggerSchedule[] schedules)
    {
        var active = new List<string?>();
        foreach (var schedule in schedules)
        {
            if (await _store.FindAsync(schedule.ScheduleId) is { IsActive: true } current)
                active.Add(current.ActivationId);
        }

        return active;
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
            MaxSchedulesPerTick = maxSchedulesPerTick,
            ClaimVisibilityTimeout = Lease
        });
        var pump = new RecurringTriggerPumpTask(
            store, _bindingStore, router, new RecurringScheduleCalculator(), options, clock, NullLogger<RecurringTriggerPumpTask>.Instance);
        return (pump, clock);
    }

    // Saves the schedule together with the trigger binding it owns — a fire only dispatches when its owning
    // binding exists in the index.
    private async Task SeedAsync(RecurringTriggerSchedule schedule)
    {
        await _store.SaveAsync(schedule);
        await SaveBindingAsync(schedule);
    }

    private async Task SaveBindingAsync(RecurringTriggerSchedule schedule) =>
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

    private static string Key(string scheduleId, DateTimeOffset occurrence) => $"recurring:{scheduleId}:{occurrence.UtcTicks}";

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

    /// <summary>
    /// Records each route. <see cref="DieBeforeRouting"/> and an <see cref="OperationCanceledException"/> in
    /// <see cref="Throw"/> stand in for a node that stops before or after its route, so nothing after it runs.
    /// </summary>
    private sealed class FakeRouter : IStimulusRouter
    {
        public List<StimulusDispatchRequest> Requests { get; } = new();
        public Exception? Throw { get; set; }
        public bool DieBeforeRouting { get; init; }
        public Func<Task>? WhileRouting { get; init; }

        public async ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
        {
            if (DieBeforeRouting)
                throw new OperationCanceledException("host stopping");
            Requests.Add(request);
            if (WhileRouting is { } whileRouting)
                await whileRouting();
            if (Throw is not null)
                throw Throw;
            return new StimulusRoutingResult([], []);
        }
    }

    private sealed class ThrowingScheduleStore : IRecurringTriggerScheduleStore
    {
        public bool Healthy { get; set; }

        public ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default) =>
            new(schedule);

        // Healthy, it holds nothing, so it grants no claim and every claim it is handed is stale.
        public ValueTask<IReadOnlyCollection<RecurringTriggerOccurrenceClaim>> ClaimDueAsync(RecurringTriggerOccurrenceClaimRequest request, CancellationToken cancellationToken = default) =>
            Available<IReadOnlyCollection<RecurringTriggerOccurrenceClaim>>([]);

        public ValueTask<RecurringTriggerOccurrenceClaim?> RenewClaimAsync(RecurringTriggerOccurrenceClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) =>
            Available<RecurringTriggerOccurrenceClaim?>(null);

        public ValueTask<bool> SettleClaimAsync(RecurringTriggerOccurrenceClaim claim, DateTimeOffset nextOccurrence, CancellationToken cancellationToken = default) =>
            Available(false);

        public ValueTask<bool> ReleaseClaimAsync(RecurringTriggerOccurrenceClaim claim, DateTimeOffset visibleAt, CancellationToken cancellationToken = default) =>
            Available(false);

        public ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default) =>
            new((RecurringTriggerSchedule?)null);

        private ValueTask<T> Available<T>(T result) => Healthy ? new(result) : throw new InvalidOperationException("store down");

        public ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<WorkflowActivationProjectionState> FindActivationStateAsync(string activationId, CancellationToken cancellationToken = default) =>
            new(WorkflowActivationProjectionState.Missing);

        public ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(string slotId, CancellationToken cancellationToken = default) =>
            new([]);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}

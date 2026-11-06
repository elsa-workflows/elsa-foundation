using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

/// <summary>
/// Normal Delay typed transitions and production clock/router/owner over an atomic test-only scope/time
/// provider. This does not qualify durable storage, a real checkpoint race or advertised production support.
/// </summary>
public sealed class ControlledWorkflowTimeTests
{
    private static readonly DateTimeOffset Wall = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Epoch = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ordinary_and_legacy_test_drive_use_wall_without_policy_source()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Workflows.SaveAsync((await fixture.WorkflowAsync()) with { TestEntry = null });
        fixture.Source.Policy = null;
        var clock = fixture.Clock(sources: []);
        var router = fixture.Router(sources: []);
        await ExecuteAsync(new Delay(router, fixture.WallClock, clock), fixture.Context);
        var timer = Assert.Single(await fixture.Ordinary.ListAsync(fixture.WorkflowId));
        Assert.Equal(Wall.AddDays(1), timer.DueTime);
        Assert.False(timer.BusinessTime!.IsControlled);
        Assert.Equal(0, fixture.Store.TimerCount);
    }

    [Fact]
    public async Task Selected_wall_policy_is_explicit_and_preserves_ordinary_timer_path()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Source.Policy = new(fixture.Entry, WorkflowTestTimeMode.Wall);
        await ExecuteAsync(new Delay(fixture.Router(), fixture.WallClock, fixture.Clock()), fixture.Context);
        Assert.Equal(Wall.AddDays(1), Assert.Single(await fixture.Ordinary.ListAsync(fixture.WorkflowId)).DueTime);
        Assert.Equal(0, fixture.Store.TimerCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Selected_entry_requires_one_policy_source(int count)
    {
        var fixture = await Fixture.CreateAsync();
        var sources = Enumerable.Repeat<IRuntimeTestTimePolicySource>(fixture.Source, count).ToArray();
        var error = await Assert.ThrowsAsync<WorkflowTestTimeUnavailableException>(() =>
            fixture.Clock(sources).ReadAsync(fixture.Context).AsTask());
        Assert.Equal("test-time-policy-source-unavailable", error.Code);
        Assert.Empty(await fixture.Ordinary.ListAsync(fixture.WorkflowId));
    }

    [Fact]
    public async Task Missing_or_other_fingerprint_policy_and_missing_clock_do_not_select_wall()
    {
        var fixture = await Fixture.CreateAsync();
        var policy = fixture.Source.Policy;
        fixture.Source.Policy = null;
        Assert.Equal("test-time-policy-unavailable", (await Assert.ThrowsAsync<WorkflowTestTimeUnavailableException>(() =>
            fixture.Clock().ReadAsync(fixture.Context).AsTask())).Code);
        fixture.Source.Policy = new(new WorkflowTestEntry(fixture.Entry.ContextId, "other",
            fixture.Entry.Executable, fixture.Entry.ExecutableNodeId, fixture.Entry.Scope, fixture.Entry.Authority),
            WorkflowTestTimeMode.Controlled, "clock-A", Epoch);
        Assert.Equal("test-time-policy-unavailable", (await Assert.ThrowsAsync<WorkflowTestTimeUnavailableException>(() =>
            fixture.Clock().ReadAsync(fixture.Context).AsTask())).Code);
        fixture.Source.Policy = policy;
        Assert.Equal("test-clock-unavailable", (await Assert.ThrowsAsync<WorkflowTestTimeUnavailableException>(() =>
            fixture.Clock().ReadAsync(fixture.Context).AsTask())).Code);
        Assert.Empty(await fixture.Ordinary.ListAsync(fixture.WorkflowId));
    }

    [Fact]
    public async Task Old_delay_constructor_cannot_schedule_wall_computation_into_controlled_policy()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Owner().InitializeAsync(fixture.Entry);
        var error = await Assert.ThrowsAsync<WorkflowTestTimeUnavailableException>(() =>
            ExecuteAsync(new Delay(fixture.Router(), fixture.WallClock), fixture.Context));
        Assert.Equal("test-timer-clock-required", error.Code);
        Assert.Equal(0, fixture.Store.TimerCount);
        Assert.Empty(await fixture.Ordinary.ListAsync(fixture.WorkflowId));
    }

    [Fact]
    public async Task Controlled_timer_never_enters_wall_list_claim_or_next_due_and_fresh_services_keep_context()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Owner().InitializeAsync(fixture.Entry);
        await ExecuteAsync(new Delay(fixture.Router(), fixture.WallClock, fixture.Clock()), fixture.Context);
        Assert.Empty(await fixture.Ordinary.ListDueAsync(Wall.AddYears(50), 1));
        Assert.Empty(await fixture.Ordinary.ClaimDueAsync(new("ordinary-pump", Wall.AddYears(50), TimeSpan.FromMinutes(1), 1)));
        Assert.Null(await fixture.Ordinary.GetNextDueTimeAsync());
        Assert.Equal(Epoch.AddDays(1), fixture.Store.SingleTimer().DueTime);
        Assert.Equal(fixture.Context.InvocationId, fixture.Store.SingleTimer().ActivityExecutionId);
        Assert.Equal(Epoch, (await fixture.Clock().ReadAsync(fixture.Context)).Instant);
        Assert.Equal(1, fixture.Store.TimerCount);
    }

    [Fact]
    public async Task Wrong_clock_stamp_and_unknown_invocation_are_refused()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Owner().InitializeAsync(fixture.Entry);
        var time = await fixture.Clock().ReadAsync(fixture.Context);
        var timer = Timer(fixture.Context, time) with { BusinessTime = new(Epoch, "other", time.ContextFingerprint, time.ClockId, 1) };
        Assert.Equal("test-timer-clock-required", (await Assert.ThrowsAsync<WorkflowTestTimeUnavailableException>(() =>
            fixture.Router().ScheduleAsync(timer).AsTask())).Code);
        Assert.Equal("test-time-invocation-unavailable", (await Assert.ThrowsAsync<WorkflowTestTimeUnavailableException>(() =>
            fixture.Clock().ReadAsync(new(fixture.WorkflowId, "other", "attempt", "delay", CancellationToken.None)).AsTask())).Code);
        Assert.Equal(0, fixture.Store.TimerCount);
    }

    [Fact]
    public async Task Scenario_leap_cannot_steal_wall_claim_or_advance_another_scope()
    {
        var fixture = await Fixture.CreateAsync();
        var second = await Fixture.CreateAsync("B", fixture.Store);
        await fixture.Owner().InitializeAsync(fixture.Entry);
        await second.Owner().InitializeAsync(second.Entry);
        await ExecuteAsync(new Delay(fixture.Router(), fixture.WallClock, fixture.Clock()), fixture.Context);
        await ExecuteAsync(new Delay(second.Router(), second.WallClock, second.Clock()), second.Context);
        var advanced = await fixture.Owner().AdvanceAsync(fixture.Entry, "advance-A", 1, Epoch.AddYears(40));
        var claim = Assert.Single(await fixture.Store.ClaimDueAsync(fixture.Policy, "worker-1", Wall, TimeSpan.FromMinutes(1), 1));
        Assert.Equal(Wall, claim.Claim.ClaimedAt);
        Assert.Equal(Wall.AddMinutes(1), claim.Claim.VisibleAfter);
        await fixture.Owner().AdvanceAsync(fixture.Entry, "advance-A-again", advanced.Revision, Epoch.AddYears(100));
        Assert.Empty(await fixture.Store.ClaimDueAsync(fixture.Policy, "worker-2", Wall, TimeSpan.FromMinutes(1), 1));
        Assert.Empty(await fixture.Store.ClaimDueAsync(second.Policy, "worker-2", Wall, TimeSpan.FromMinutes(1), 1));
        Assert.Equal(Epoch, (await second.Clock().ReadAsync(second.Context)).Instant);
        Assert.False(fixture.Entry.Scope.IsExpired(Wall));
    }

    [Fact]
    public async Task Timer_saved_after_advance_keeps_pending_scan_and_prebookmark_not_found_never_uses_scenario_grace()
    {
        var fixture = await Fixture.CreateAsync();
        var owner = fixture.Owner();
        await owner.InitializeAsync(fixture.Entry);
        var readThenAdvance = new BeforeScheduleClock(fixture.Clock(), async () =>
            await owner.AdvanceAsync(fixture.Entry, "advance-before-save", 1, Epoch.AddYears(40)));
        var suspended = await ExecuteAsync(new Delay(fixture.Router(), fixture.WallClock, readThenAdvance), fixture.Context);
        Assert.Single(await fixture.Store.ListPendingAsync(Wall, 1));
        Assert.Equal(1, (await owner.SweepAsync()).Pending);
        Assert.Equal(1, fixture.Store.TimerCount);
        fixture.Lookup.Ready = true;
        fixture.WallClock.Advance(TimeSpan.FromMilliseconds(101));
        Assert.Equal(1, (await fixture.Owner().SweepAsync()).Dispatched);
        Assert.Equal(0, fixture.Store.TimerCount);
        var request = Assert.Single(fixture.Dispatcher.Requests);
        Assert.NotNull(request.TestRunDelivery);
        Assert.Equal(fixture.Entry.Scope.ScopeId, request.TestRunDelivery.TestRunId);
        Assert.Equal(fixture.Lookup.Bookmark.BookmarkId, request.TestRunDelivery.WaitId);
        var resumed = await ((IStatefulActivity<ActivityUnit, DelayState, DurableTimerElapsed>)new Delay(fixture.Router(), fixture.WallClock, fixture.Clock()))
            .ResumeAsync(new(fixture.Context, (DelayState)suspended.State!, new("timer:" + fixture.Context.InvocationId), "registration", "delivery"));
        Assert.IsAssignableFrom<IActivityCompletionTransition>(resumed);
    }

    [Fact]
    public async Task Consumed_receipt_reconciles_response_loss_without_a_second_delivery()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Owner().InitializeAsync(fixture.Entry);
        await ExecuteAsync(new Delay(fixture.Router(), fixture.WallClock, fixture.Clock()), fixture.Context);
        await fixture.Owner().AdvanceAsync(fixture.Entry, "advance", 1, Epoch.AddDays(2));
        fixture.Lookup.Ready = true;
        fixture.Dispatcher.LoseResponse = true;
        Assert.Equal(1, (await fixture.Owner().SweepAsync()).Pending);
        Assert.Equal(1, fixture.Store.TimerCount);
        fixture.Lookup.Ready = false;
        fixture.WallClock.Advance(TimeSpan.FromMilliseconds(101));
        Assert.Equal(1, (await fixture.Owner().SweepAsync()).Dispatched);
        Assert.Single(fixture.Dispatcher.Requests);
        Assert.Equal(0, fixture.Store.TimerCount);
    }

    [Fact]
    public async Task Scope_closure_fences_scheduling_and_owner_cleans_only_its_timers()
    {
        var fixture = await Fixture.CreateAsync();
        var second = await Fixture.CreateAsync("B", fixture.Store);
        await fixture.Owner().InitializeAsync(fixture.Entry);
        await second.Owner().InitializeAsync(second.Entry);
        var stamp = await fixture.Clock().ReadAsync(fixture.Context);
        await ExecuteAsync(new Delay(fixture.Router(), fixture.WallClock, fixture.Clock()), fixture.Context);
        await ExecuteAsync(new Delay(second.Router(), second.WallClock, second.Clock()), second.Context);
        await fixture.Store.CloseAsync(new WorkflowTestScopeCloseRequest(fixture.Entry.Scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown, Wall));
        await Assert.ThrowsAsync<TestScopeAdmissionException>(() =>
            fixture.Store.ScheduleAsync(fixture.Policy, Timer(fixture.Context, stamp), Wall).AsTask());
        Assert.Equal(1, (await fixture.Owner().SweepAsync()).Retired);
        Assert.Equal(1, fixture.Store.TimerCount);
        Assert.Empty(fixture.Dispatcher.Requests);
        Assert.Equal(Epoch, (await second.Clock().ReadAsync(second.Context)).Instant);
    }

    private static DurableTimer Timer(ActivityExecutionContext context, WorkflowBusinessTime read) => new(
        "timer:" + context.InvocationId, context.WorkflowExecutionId, "DurableTimer", "timer:" + context.InvocationId,
        read.Instant.AddDays(1), read.Instant, ActivityExecutionId: context.InvocationId) { BusinessTime = read };

    private static async Task<IStatefulActivitySuspensionTransition> ExecuteAsync(Delay delay, ActivityExecutionContext context)
    {
        delay.Duration = TimeSpan.FromDays(1);
        return Assert.IsAssignableFrom<IStatefulActivitySuspensionTransition>(
            await ((IStatefulActivity<ActivityUnit, DelayState, DurableTimerElapsed>)delay).ExecuteAsync(context));
    }

    private sealed class BeforeScheduleClock(IWorkflowBusinessClock inner, Func<Task> afterRead) : IWorkflowBusinessClock
    {
        public async ValueTask<WorkflowBusinessTime> ReadAsync(ActivityExecutionContext context, CancellationToken cancellationToken = default)
        {
            var time = await inner.ReadAsync(context, cancellationToken);
            await afterRead();
            return time;
        }
    }

    private sealed class PolicySource(WorkflowTestTimePolicy policy) : IRuntimeTestTimePolicySource
    {
        internal WorkflowTestTimePolicy? Policy { get; set; } = policy;
        public ValueTask<WorkflowTestTimePolicy?> FindAsync(string contextId, string contextFingerprint, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Policy?.Entry.ContextId == contextId && Policy.Entry.ContextFingerprint == contextFingerprint ? Policy : null);
    }

    private sealed class WaitLookup(BookmarkState bookmark) : IBookmarkStimulusLookup
    {
        internal bool Ready { get; set; }
        internal BookmarkState Bookmark { get; } = bookmark;
        public ValueTask<BookmarkStimulusLookupResult> FindAsync(BookmarkStimulusLookupRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Ready ? new BookmarkStimulusLookupResult(BookmarkStimulusLookupStatus.Found, Bookmark) :
                new BookmarkStimulusLookupResult(BookmarkStimulusLookupStatus.NotFound, reason: "Suspension has not committed."));
    }

    private sealed class ReceiptDispatcher(IWorkflowExecutionStateStore workflows) : IBookmarkResumeDispatcher
    {
        internal bool LoseResponse { get; set; }
        internal List<BookmarkResumeDispatchRequest> Requests { get; } = [];
        public async ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var state = await workflows.FindAsync(request.WorkflowExecutionId, cancellationToken);
            await workflows.SaveAsync(request.TestRunDelivery!.Record(state!), cancellationToken);
            if (LoseResponse) throw new IOException("Response lost after committed receipt.");
            return new(BookmarkResumeDispatchStatus.Duplicate, request.WorkflowExecutionId, reason: "Acknowledged exact normal test receipt.");
        }
    }

    private sealed class Fixture
    {
        private Fixture(string id, AtomicTimeStore store)
        {
            Store = store;
            WorkflowId = "workflow-" + id;
            Context = new(WorkflowId, "invocation-" + id, "attempt-1", "delay", CancellationToken.None);
            Entry = new("context-" + id, "fingerprint-" + id, new("artifact", "definition", "version", "1", "hash"),
                "delay", new("scope-" + id, Wall.AddMinutes(10), "tenant", new("partition")), new("system", "initiator"));
            Policy = new(Entry, WorkflowTestTimeMode.Controlled, "clock-" + id, Epoch);
            Source = new(Policy);
            Lookup = new(new("wait-" + id, WorkflowId, Context.InvocationId, Context.ExecutableNodeId,
                "resume-target:durable-timer", "DurableTimer", "timer:" + Context.InvocationId, null,
                new Dictionary<string, string>(), Wall, null));
            Dispatcher = new(Workflows);
        }

        internal string WorkflowId { get; }
        internal ActivityExecutionContext Context { get; }
        internal WorkflowTestEntry Entry { get; }
        internal WorkflowTestTimePolicy Policy { get; }
        internal PolicySource Source { get; }
        internal AtomicTimeStore Store { get; }
        internal FakeTimeProvider WallClock { get; } = new(Wall);
        internal IWorkflowExecutionStateStore Workflows { get; } = new InMemoryWorkflowExecutionStateStore();
        internal IActivityExecutionStateStore Activities { get; } = new InMemoryActivityExecutionStateStore();
        internal InMemoryDurableTimerStore Ordinary { get; } = new();
        internal WaitLookup Lookup { get; }
        internal ReceiptDispatcher Dispatcher { get; }
        internal async Task<WorkflowExecutionState> WorkflowAsync() =>
            await Workflows.FindAsync(WorkflowId) ?? throw new InvalidOperationException("Fixture workflow is absent.");
        private WorkflowTestTimePolicyResolver Resolver(IRuntimeTestTimePolicySource[]? sources = null) =>
            new(Workflows, Activities, Store, sources ?? [Source], WallClock);
        internal WorkflowBusinessClock Clock(IRuntimeTestTimePolicySource[]? sources = null) => new(Resolver(sources), Store, WallClock);
        internal TestAwareDurableTimerScheduler Router(IRuntimeTestTimePolicySource[]? sources = null) =>
            new(Resolver(sources), Store, new DurableTimerScheduler(Ordinary), WallClock);
        internal ControlledWorkflowTimerOwner Owner() => new(Resolver(), Store, Workflows, Activities, Store, Lookup, Dispatcher, WallClock);

        internal static async Task<Fixture> CreateAsync(string id = "A", AtomicTimeStore? store = null)
        {
            var fixture = new Fixture(id, store ?? new());
            await fixture.Store.CreateAsync(fixture.Entry.Scope, Wall);
            await fixture.Workflows.SaveAsync(new(fixture.WorkflowId, fixture.Entry.Executable, WorkflowExecutionStatus.Running,
                null, Wall, Wall, Wall, null, null, null, "tenant", new Dictionary<string, string>())
                { RunKind = WorkflowRunKind.TestRun, TestScope = fixture.Entry.Scope, TestEntry = fixture.Entry,
                    Authority = fixture.Entry.Authority, Partition = fixture.Entry.Scope.Partition });
            await fixture.Activities.SaveAsync(new(new(fixture.Context.InvocationId, fixture.WorkflowId, "delay", "delay", "Delay", "1"),
                ActivityExecutionStatus.Running, null, Wall, Wall, null, null, null, null, null, null, [], [], 0, 0, new Dictionary<string, string>()));
            return fixture;
        }
    }

    /// <summary>Test-only provider; one lock is the shared normal scope/time admission transaction.</summary>
    private sealed class AtomicTimeStore : IControlledWorkflowTimeStore, IWorkflowTestScopeStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, WorkflowTestScopeRecord> _scopes = [];
        private readonly Dictionary<string, Clock> _clocks = [];
        internal int TimerCount { get { lock (_gate) return _clocks.Values.Sum(x => x.Timers.Count); } }
        internal DurableTimer SingleTimer() { lock (_gate) return _clocks.Values.SelectMany(x => x.Timers.Values).Single().Timer; }
        private Clock Require(WorkflowTestTimePolicy policy)
        {
            if (!_clocks.TryGetValue(policy.Entry.ContextId, out var clock) || !policy.Matches(clock.State.Policy))
                throw new WorkflowTestTimeUnavailableException("test-clock-unavailable", "Clock is unavailable.");
            return clock;
        }
        private void Open(WorkflowTestTimePolicy policy, DateTimeOffset observedAt) =>
            WorkflowTestScopeAdmission.EnsureOpen(_scopes.GetValueOrDefault(policy.Entry.Scope.ScopeId), policy.Entry.Scope, observedAt);

        public ValueTask<ControlledWorkflowClockState> CreateClockAsync(WorkflowTestTimePolicy policy, DateTimeOffset observedAt, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Open(policy, observedAt);
                if (!_clocks.ContainsKey(policy.Entry.ContextId)) _clocks.Add(policy.Entry.ContextId, new(new(policy, policy.InitialTime!.Value, 1)));
                var clock = Require(policy);
                if (clock.State.Closed) throw new InvalidOperationException("Clock is closed.");
                return ValueTask.FromResult(clock.State);
            }
        }
        public ValueTask<ControlledWorkflowClockState?> FindClockAsync(WorkflowTestTimePolicy policy, CancellationToken cancellationToken = default)
        {
            lock (_gate) return ValueTask.FromResult(_clocks.TryGetValue(policy.Entry.ContextId, out var clock) && policy.Matches(clock.State.Policy) ? clock.State : null);
        }
        public ValueTask<DurableTimer> ScheduleAsync(WorkflowTestTimePolicy policy, DurableTimer timer, DateTimeOffset observedAt, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Open(policy, observedAt);
                var clock = Require(policy);
                if (clock.State.Closed || timer.BusinessTime is not { } stamp || !policy.MatchesClock(stamp) ||
                    !clock.Reads.TryGetValue(stamp.ClockRevision!.Value, out var readAt) || readAt != stamp.Instant || timer.CreatedAt != readAt)
                    throw new InvalidOperationException("Clock read does not belong to this admitted policy.");
                var key = (timer.WorkflowExecutionId, timer.TimerId);
                if (!clock.Timers.TryGetValue(key, out var row)) clock.Timers.Add(key, row = new(timer));
                return ValueTask.FromResult(row.Timer);
            }
        }
        public ValueTask<ControlledWorkflowClockState> AdvanceAsync(WorkflowTestTimePolicy policy, string operationId, long expectedRevision,
            DateTimeOffset advanceTo, DateTimeOffset observedAt, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Open(policy, observedAt);
                var clock = Require(policy);
                if (clock.Operations.TryGetValue(operationId, out var prior))
                {
                    if (prior.Expected != expectedRevision || prior.To != advanceTo) throw new InvalidOperationException("Operation conflict.");
                    return ValueTask.FromResult(prior.Result);
                }
                if (clock.State.Closed || clock.State.Revision != expectedRevision || advanceTo < clock.State.CurrentTime)
                    throw new InvalidOperationException("Clock CAS or monotonicity conflict.");
                clock.State = clock.State with { CurrentTime = advanceTo, Revision = checked(clock.State.Revision + 1) };
                clock.Reads.Add(clock.State.Revision, advanceTo);
                clock.Operations.Add(operationId, (expectedRevision, advanceTo, clock.State));
                return ValueTask.FromResult(clock.State);
            }
        }
        public ValueTask<IReadOnlyList<WorkflowTestTimePolicy>> ListPendingAsync(DateTimeOffset observedAt, int limit, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                IReadOnlyList<WorkflowTestTimePolicy> result = _clocks.Values.Where(clock => clock.Timers.Count > 0 &&
                    (_scopes[clock.State.Policy.Entry.Scope.ScopeId].State != WorkflowTestScopeState.Open ||
                     clock.State.Policy.Entry.Scope.IsExpired(observedAt) || clock.Timers.Values.Any(x =>
                         x.Timer.DueTime <= clock.State.CurrentTime && (x.VisibleAt is null || x.VisibleAt <= observedAt))))
                    .OrderBy(x => x.State.Policy.Entry.ContextId, StringComparer.Ordinal).Take(limit).Select(x => x.State.Policy).ToArray();
                return ValueTask.FromResult(result);
            }
        }
        public ValueTask<IReadOnlyList<ControlledWorkflowTimerClaim>> ClaimDueAsync(WorkflowTestTimePolicy policy, string ownerId,
            DateTimeOffset observedAt, TimeSpan visibility, int limit, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Open(policy, observedAt);
                var clock = Require(policy);
                IReadOnlyList<ControlledWorkflowTimerClaim> claims = clock.Timers.Values.Where(row => !clock.State.Closed &&
                    row.Timer.DueTime <= clock.State.CurrentTime && (row.VisibleAt is null || row.VisibleAt <= observedAt))
                    .OrderBy(row => row.Timer.DueTime).ThenBy(row => row.Timer.TimerId, StringComparer.Ordinal).Take(limit).Select(row =>
                    {
                        row.Owner = ownerId; row.Token++; row.Revision++; row.ClaimedAt = observedAt; row.VisibleAt = observedAt.Add(visibility);
                        return Claim(policy, row);
                    }).ToArray();
                return ValueTask.FromResult(claims);
            }
        }
        public ValueTask<ControlledWorkflowTimerTransition> RenewClaimAsync(ControlledWorkflowTimerClaim claim, DateTimeOffset observedAt,
            TimeSpan visibility, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Open(claim.Policy, observedAt);
                if (Current(claim) is not { } row) return Stale();
                row.VisibleAt = observedAt.Add(visibility); row.Revision++;
                return Applied(claim.Policy, row);
            }
        }
        public ValueTask<ControlledWorkflowTimerTransition> BindDeliveryAsync(ControlledWorkflowTimerClaim claim,
            TestRunDeliveryReceipt delivery, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (Current(claim) is not { } row) return Stale();
                if (row.Delivery is not null && row.Delivery != delivery) throw new InvalidOperationException("Delivery conflict.");
                row.Delivery = delivery; row.Revision++;
                return Applied(claim.Policy, row);
            }
        }
        public ValueTask<ControlledWorkflowTimerTransition> CompleteClaimAsync(ControlledWorkflowTimerClaim claim, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (Current(claim) is null) return Stale();
                Require(claim.Policy).Timers.Remove((claim.Claim.Timer.WorkflowExecutionId, claim.Claim.Timer.TimerId));
                return ValueTask.FromResult(new ControlledWorkflowTimerTransition(RuntimeDurableTimerClaimTransitionStatus.Succeeded));
            }
        }
        public ValueTask<ControlledWorkflowTimerTransition> ReleaseClaimAsync(ControlledWorkflowTimerClaim claim, DateTimeOffset visibleAt, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (Current(claim) is not { } row) return Stale();
                row.Owner = null; row.VisibleAt = visibleAt; row.Revision++;
                return ValueTask.FromResult(new ControlledWorkflowTimerTransition(RuntimeDurableTimerClaimTransitionStatus.Succeeded));
            }
        }
        public ValueTask<ControlledWorkflowTimeCleanupResult> CloseAsync(WorkflowTestTimePolicy policy, DateTimeOffset observedAt,
            int limit, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var scope = _scopes[policy.Entry.Scope.ScopeId];
                if (scope.State == WorkflowTestScopeState.Open && !scope.Scope.IsExpired(observedAt)) throw new InvalidOperationException("Scope is open.");
                var clock = Require(policy); clock.State = clock.State with { Closed = true };
                var keys = clock.Timers.Keys.Take(limit).ToArray();
                foreach (var key in keys) clock.Timers.Remove(key);
                return ValueTask.FromResult(new ControlledWorkflowTimeCleanupResult(keys.Length, clock.Timers.Count));
            }
        }
        private Row? Current(ControlledWorkflowTimerClaim claim)
        {
            var clock = Require(claim.Policy);
            return clock.Timers.TryGetValue((claim.Claim.Timer.WorkflowExecutionId, claim.Claim.Timer.TimerId), out var row) &&
                row.Revision == claim.Claim.Revision && row.Token == claim.Claim.FencingToken && row.Owner == claim.Claim.OwnerId ? row : null;
        }
        private static ControlledWorkflowTimerClaim Claim(WorkflowTestTimePolicy policy, Row row) =>
            new(policy, new(row.Timer, row.Owner!, row.Token, row.Revision, row.ClaimedAt, row.VisibleAt!.Value, 0), row.Delivery);
        private static ValueTask<ControlledWorkflowTimerTransition> Applied(WorkflowTestTimePolicy policy, Row row) =>
            ValueTask.FromResult(new ControlledWorkflowTimerTransition(RuntimeDurableTimerClaimTransitionStatus.Succeeded, Claim(policy, row)));
        private static ValueTask<ControlledWorkflowTimerTransition> Stale() =>
            ValueTask.FromResult(new ControlledWorkflowTimerTransition(RuntimeDurableTimerClaimTransitionStatus.Stale));

        public ValueTask<WorkflowTestScopeRecord> CreateAsync(WorkflowTestScope scope, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var record = WorkflowTestScopeTransitions.Create(_scopes.GetValueOrDefault(scope.ScopeId), scope, createdAt);
                _scopes[scope.ScopeId] = record; return ValueTask.FromResult(record);
            }
        }
        public ValueTask<WorkflowTestScopeRecord?> FindAsync(string scopeId, CancellationToken cancellationToken = default)
        {
            lock (_gate) return ValueTask.FromResult(_scopes.GetValueOrDefault(scopeId));
        }
        public ValueTask<WorkflowTestScopeCloseResult> CloseAsync(WorkflowTestScopeCloseRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var result = WorkflowTestScopeTransitions.Close(_scopes.GetValueOrDefault(request.ScopeId), request);
                if (result.Record is not null) _scopes[request.ScopeId] = result.Record;
                return ValueTask.FromResult(result);
            }
        }
        public ValueTask<WorkflowTestScopeRecord> CompleteAsync(string scopeId, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var result = WorkflowTestScopeTransitions.Complete(_scopes[scopeId], completedAt);
                _scopes[scopeId] = result; return ValueTask.FromResult(result);
            }
        }
        public ValueTask<WorkflowTestScopePage> QueryAsync(WorkflowTestScopePageQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This focused test provider exposes no background lifecycle query.");
        private sealed class Clock(ControlledWorkflowClockState state)
        {
            internal ControlledWorkflowClockState State { get; set; } = state;
            internal Dictionary<long, DateTimeOffset> Reads { get; } = new() { [state.Revision] = state.CurrentTime };
            internal Dictionary<string, (long Expected, DateTimeOffset To, ControlledWorkflowClockState Result)> Operations { get; } = [];
            internal Dictionary<(string Workflow, string Timer), Row> Timers { get; } = [];
        }
        private sealed class Row(DurableTimer timer)
        {
            internal DurableTimer Timer { get; } = timer;
            internal string? Owner { get; set; }
            internal long Token { get; set; }
            internal long Revision { get; set; } = 1;
            internal DateTimeOffset ClaimedAt { get; set; }
            internal DateTimeOffset? VisibleAt { get; set; }
            internal TestRunDeliveryReceipt? Delivery { get; set; }
        }
    }
}

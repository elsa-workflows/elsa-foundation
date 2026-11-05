using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeHistoryNoRecordCompletionTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true,
        RuntimeHistoryCapture.Kinds.Concat(DurableTimerHistoryFacts.Kinds).ToArray());
    private static readonly WorkflowExecutableIdentity Executable = new("artifact", "definition", "revision", "1", "hash");

    [Fact]
    public async Task Active_source_anywhere_in_run_refuses_before_clearing_any_retired_prefix()
    {
        var f = await Fixture.CreateAsync();
        await f.AddTimerAsync("retired-first", retired: true);
        await f.AddTimerAsync("active-last", retired: false);

        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.Timers.DischargeUnrecordedCompletionAsync(f.Completion, 1).AsTask());

        Assert.Equal(2, (await f.Timers.ListPendingHistoryAsync(new(10))).Items.Count);
        Assert.Empty(f.Checkpoints.NoRecordTimerCompletionRuns);
        Assert.Null(await f.Executions.FindAsync("run"));
    }

    [Fact]
    public async Task Bounded_discharge_retains_first_receipts_and_deterministic_reexecution_never_recreates_a_deadline()
    {
        var f = await Fixture.CreateAsync();
        var originals = new List<DurableTimer>();
        for (var index = 0; index < 5; index++) originals.Add(await f.AddTimerAsync("timer-" + index, retired: true));

        Assert.False(await f.Timers.DischargeUnrecordedCompletionAsync(f.Completion, 2));
        Assert.Equal(3, (await f.Timers.ListPendingHistoryAsync(new(10))).Items.Count);
        Assert.False(await f.Timers.DischargeUnrecordedCompletionAsync(f.Completion, 2));
        Assert.True(await f.Timers.DischargeUnrecordedCompletionAsync(f.Completion, 2));
        Assert.True(await f.Timers.DischargeUnrecordedCompletionAsync(f.Completion with { Checkpoint = f.Completion.Checkpoint with { CheckpointId = "later-retry" } }, 2));
        foreach (var timer in originals) await f.Timers.SaveAsync(timer);

        Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
        Assert.Empty(await f.Timers.ClaimDueAsync(new("pump", At.AddHours(1), TimeSpan.FromMinutes(1), 10)));
        Assert.All(f.TimerState.Scopes["scope"].Timers.Values, source =>
        {
            Assert.Null(source.Timer.Input); Assert.Null(source.Timer.Metadata);
            Assert.Equal(f.Completion.Checkpoint.CheckpointId, source.Timer.HistoryResponsibility!.NoRecordCompletion!.CheckpointId);
            Assert.Empty(source.Timer.HistoryResponsibility.PendingEvents);
        });
        Assert.Null(await f.Executions.FindAsync("run"));
        Assert.Empty(f.Checkpoints.HistoryHeads);
        Assert.Empty(f.Store.ListCommits());
    }

    [Fact]
    public async Task Partial_discharge_followed_by_fault_before_timer_reexecution_marks_first_native_history_incomplete()
    {
        var f = await Fixture.CreateAsync();
        await f.AddTimerAsync("one", retired: true); await f.AddTimerAsync("two", retired: true);
        Assert.False(await f.Timers.DischargeUnrecordedCompletionAsync(f.Completion, 1));
        var faulted = f.Workflow with { Status = WorkflowExecutionStatus.Faulted };
        var fault = f.Completion with { CommitId = "first-fault", Checkpoint = f.Completion.Checkpoint with
            { CheckpointId = "first-fault", Name = RuntimeCheckpointNames.WorkflowFaulted, HistoryStartsAtAdmission = true },
            StateChanges = new(new("run", RuntimeStateChangeOperation.Upsert, faulted, new Dictionary<string, string>()), null, [], [], [], [], []) };
        var captured = await new RuntimeHistoryCapture(f.Executions).EnrichAsync(fault);

        await f.Store.CommitAsync(captured, new(RuntimeCheckpointPersistenceMode.Immediate));

        var head = f.Checkpoints.HistoryHeads[("scope", "run")];
        Assert.True(head.HasGaps);
        Assert.True(head.CapturedFromStart);
        Assert.True(await f.Timers.HasPendingHistoryAsync("run"));
        Assert.Contains(f.Checkpoints.HistoryEvents[("scope", "run")], e => e.Name == "WorkflowFaulted");
        Assert.DoesNotContain(f.Checkpoints.HistoryEvents[("scope", "run")], e => e.Name == "WorkflowStarted");
    }

    [Fact]
    public async Task Stale_fence_and_wrong_scope_cannot_discharge_source_obligations()
    {
        var f = await Fixture.CreateAsync(); await f.AddTimerAsync("timer", retired: true);
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => f.Timers.DischargeUnrecordedCompletionAsync(
            f.Completion with { ExpectedFence = new("stale", "owner", 1) }).AsTask());
        var wrongScope = new InMemoryDurableTimerStore(f.TimerState, new Access("other"), new FakeTimeProvider(At), f.Checkpoints, f.Executions, f.Liveness);
        await Assert.ThrowsAsync<InvalidOperationException>(() => wrongScope.DischargeUnrecordedCompletionAsync(f.Completion).AsTask());
        Assert.True(await f.Timers.HasPendingHistoryAsync("run"));
        Assert.Empty(f.Checkpoints.NoRecordTimerCompletionRuns);
    }

    [Fact]
    public async Task Missing_timer_owner_blocks_no_record_queue_advance_without_creating_native_state()
    {
        var f = await Fixture.CreateAsync(); await f.AddTimerAsync("timer", retired: true);
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(new("start", "run", "command", WorkflowExecutionCommandKind.Start, "envelope", "key", At, At));
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = new RuntimeCoalescingSession("run", queue, new() { RecordCompletion = false }, mode: new(PersistenceMode.Ephemeral, false));
        await session.ConsumePumpedOverlayItemAsync("start", CancellationToken.None);
        using var scope = accessor.Push(session);
        var store = new CoalescingRuntimeCheckpointCommitStore(new(f.Store), accessor);

        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => store.CommitAsync(f.Completion, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());

        Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("run")));
        Assert.True(await f.Timers.HasPendingHistoryAsync("run"));
        Assert.Empty(f.Store.ListCommits());
    }

    [Fact]
    public async Task Discharge_before_lost_queue_ack_is_safe_to_repeat_without_history_module()
    {
        var f = await Fixture.CreateAsync(); var original = await f.AddTimerAsync("timer", retired: true);
        var queue = new FailingQueue();
        await queue.EnqueueAsync(new("start", "run", "command", WorkflowExecutionCommandKind.Start, "envelope", "key", At, At));
        async ValueTask CompleteAsync()
        {
            var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
            var session = new RuntimeCoalescingSession("run", queue, new() { RecordCompletion = false }, mode: new(PersistenceMode.Ephemeral, false));
            await session.ConsumePumpedOverlayItemAsync("start", CancellationToken.None);
            using var scope = accessor.Push(session);
            var store = new CoalescingRuntimeCheckpointCommitStore(new(f.Store), accessor) { NoRecordTimerSource = () => f.Timers };
            await store.CommitAsync(f.Completion, new(RuntimeCheckpointPersistenceMode.Immediate));
        }
        await Assert.ThrowsAsync<IOException>(() => CompleteAsync().AsTask());
        Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
        Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("run")));
        await f.Timers.SaveAsync(original);
        queue.FailDelete = false;
        await CompleteAsync();
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("run")));
        Assert.Empty(await f.Timers.ClaimDueAsync(new("pump", At.AddHours(1), TimeSpan.FromMinutes(1), 10)));
        Assert.Empty(f.Store.ListCommits()); Assert.Empty(f.Checkpoints.HistoryHeads);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData(PersistenceScope.DefaultValue)]
    public async Task Unqualified_winner_lookup_cannot_bypass_hidden_source_access_and_qualified_replay_survives_expired_lease(string scope)
    {
        var f = await Fixture.CreateAsync();
        await f.Executions.SaveAsync(f.Workflow with { Status = WorkflowExecutionStatus.Running, CompletedAt = null, TenantId = scope });
        var qualified = new InMemoryDurableTimerStore(f.TimerState, new Access(scope), new FakeTimeProvider(At), f.Checkpoints, f.Executions, f.Liveness);
        var timer = new DurableTimer("protected", "run", "Timer", "route", At, At, JsonSerializer.SerializeToElement("protected-body"));
        timer = timer with { HistoryResponsibility = new(Policy, [DurableTimerHistoryFacts.CreateScheduled(timer, Policy)!.Event], false) };
        await qualified.SaveAsync(timer);
        var unqualified = new InMemoryDurableTimerStore(f.TimerState, new Access(scope), new FakeTimeProvider(At));
        Assert.False(unqualified.SupportsHistoryResponsibility);
        Assert.Null(await unqualified.FindAsync("run", "protected"));
        Assert.Empty(await unqualified.ListDueAsync(At.AddDays(1), 10));
        Assert.Empty((await unqualified.ListPageAsync(new("run"))).Items);
        await Assert.ThrowsAsync<NotSupportedException>(() => unqualified.SaveAsync(timer with { HistoryResponsibility = null }).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => unqualified.ListPendingHistoryAsync(new()).AsTask());
        if (scope == PersistenceScope.DefaultValue)
        {
            var fallback = new InMemoryDurableTimerStore(f.TimerState, null, new FakeTimeProvider(At));
            Assert.Null(await fallback.FindAsync("run", "protected"));
            await Assert.ThrowsAsync<NotSupportedException>(() => fallback.SaveAsync(timer with { HistoryResponsibility = null }).AsTask());
        }
        var qualifiedExpired = new InMemoryDurableTimerStore(f.TimerState, new Access(scope), new FakeTimeProvider(At.AddDays(2)), f.Checkpoints, f.Executions, f.Liveness);
        Assert.Equal("protected-body", (await qualifiedExpired.SaveAsync(timer)).Input!.Value.GetString());
    }

    private sealed class Access(string scope = "scope") : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(scope)); }
    private sealed class Lease : IWorkflowExecutableRootWriteLeaseManager
    { public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) => write(cancellationToken); }
    private sealed class Fixture
    {
        internal InMemoryRuntimeCheckpointStoreState Checkpoints { get; } = new();
        internal InMemoryDurableTimerStoreState TimerState { get; } = new();
        internal InMemoryExecutionLivenessStateStore Liveness { get; } = new();
        internal InMemoryWorkflowExecutionStateStore Executions { get; }
        internal InMemoryDurableTimerStore Timers { get; }
        internal InMemoryRuntimeCheckpointCommitStore Store { get; }
        internal WorkflowExecutionState Workflow { get; } = new("run", Executable, WorkflowExecutionStatus.Completed, null, At, At, At, At, null, null, "scope",
            new Dictionary<string, string> { [RuntimeMetadataKeys.PersistenceMode] = nameof(PersistenceMode.Ephemeral) })
        { HistoryCapturePolicy = Policy, HistoryCaptureEnabledAtAdmission = true };
        internal RuntimeCheckpointCommit Completion { get; }
        private Fixture()
        {
            Executions = new(Checkpoints, Liveness);
            Timers = new(TimerState, new Access(), new FakeTimeProvider(At), Checkpoints, Executions, Liveness);
            Store = new(workflowExecutionStateStore: Executions, operationalStateStore: Liveness, state: Checkpoints,
                persistenceAccessContextAccessor: new Access(), timeProvider: new FakeTimeProvider(At), rootWriteLeaseManager: new Lease());
            Completion = new("completion", new("completed-checkpoint", RuntimeCheckpointNames.WorkflowCompleted, "run", At, [], new Dictionary<string, string>()),
                new(new("run", RuntimeStateChangeOperation.Upsert, Workflow, new Dictionary<string, string>()), null, [], [], [], [], []), [], new Dictionary<string, string>())
            { ExpectedFence = new("lease", "owner", 1) };
        }
        internal static async ValueTask<Fixture> CreateAsync()
        {
            var f = new Fixture();
            await f.Liveness.SaveAsync(new(RuntimeExecutionOwnershipStateId.For("run"), "run", new("lease", "run", "owner", At, At.AddDays(1), 1), null, null, null));
            return f;
        }
        internal async ValueTask<DurableTimer> AddTimerAsync(string id, bool retired)
        {
            var timer = new DurableTimer(id, "run", "timer", "protected-routing", At, At,
                JsonSerializer.SerializeToElement("protected-payload"), new Dictionary<string, string> { ["protected"] = "metadata" });
            var schedule = DurableTimerHistoryFacts.CreateScheduled(timer, Policy)!;
            timer = timer with { HistoryResponsibility = new(Policy, [schedule.Event], false) };
            await Timers.SaveAsync(timer);
            if (retired) await Timers.RetireWithHistoryAsync("run", id, DurableTimerHistoryFacts.CreateRetired(timer, At, "helper-completed")!);
            return timer;
        }
    }
    private sealed class FailingQueue : IWorkflowSchedulerWorkQueue
    {
        private readonly InMemoryWorkflowSchedulerWorkQueue _inner = new();
        internal bool FailDelete { get; set; } = true;
        public bool SupportsTargetedDeletion => true;
        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) => _inner.EnqueueAsync(workItem, cancellationToken);
        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) => _inner.ListAsync(query, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => _inner.DequeueAsync(workflowExecutionId, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) => _inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) => FailDelete
            ? ValueTask.FromException<bool>(new IOException("Injected queue acknowledgment failure")) : _inner.DeleteAsync(workflowExecutionId, workItemId, cancellationToken);
    }
}

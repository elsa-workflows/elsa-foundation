using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;

using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class InMemoryDurableTimerHistoryStoreTests
{
    private static readonly RuntimeHistoryCapturePolicy NoRecordPolicy = new("runtime-history-v3", 3, true,
        ["Transition", "WorkflowCompleted", "WorkflowFaulted", "TimerScheduled", "TimerDispatched", "TimerRetired"]);

    private static RuntimeCheckpointCommit Completion()
    {
        var workflow = new WorkflowExecutionState("run", new("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Completed, null, At, At, At, At, null, null, "scope-a",
            new Dictionary<string, string> { [RuntimeMetadataKeys.PersistenceMode] = nameof(PersistenceMode.Ephemeral) })
            { HistoryCapturePolicy = NoRecordPolicy, HistoryCaptureEnabledAtAdmission = true };
        return new("completion", new("completed", RuntimeCheckpointNames.WorkflowCompleted, "run", At, [], new Dictionary<string, string>()),
            new(new("run", RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()), null, [], [], [], [], []),
            [], new Dictionary<string, string>()) { ExpectedFence = new("lease", "owner", 1) };
    }

    private static async Task<DurableTimer> AddUnrecordedTimerAsync(Fixture f, string id, bool retired = true)
    {
        var timer = Timer(id, captured: false);
        timer = await f.Timers.SaveAsync(timer with
            { HistoryResponsibility = new(NoRecordPolicy, [DurableTimerHistoryFacts.CreateScheduled(timer, NoRecordPolicy)!.Event], false) });
        if (retired) await f.Timers.RetireWithHistoryAsync("run", id, DurableTimerHistoryFacts.CreateRetired(timer, At, "scope-cancelled")!);
        return timer;
    }

    [Fact]
    public async Task No_record_completion_refuses_any_active_source_before_clearing_a_retired_prefix()
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        await AddUnrecordedTimerAsync(f, "retired"); await AddUnrecordedTimerAsync(f, "active", retired: false);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.Timers.DischargeUnrecordedCompletionAsync(Completion(), 1).AsTask());
        Assert.Equal(2, (await f.Timers.ListPendingHistoryAsync(new())).Items.Count);
        Assert.Empty(f.State.TimerSources.NoRecordCompletionRuns); Assert.Empty(f.Commits.ListCommits());
    }

    [Fact]
    public async Task Bounded_no_record_discharge_keeps_first_receipts_and_never_recreates_a_deadline()
    {
        var f = await Fixture.CreateAsync(native: false, head: false); var originals = new List<DurableTimer>();
        for (var i = 0; i < 5; i++) originals.Add(await AddUnrecordedTimerAsync(f, "timer-" + i));
        Assert.False(await f.Timers.DischargeUnrecordedCompletionAsync(Completion(), 2));
        Assert.Equal(3, (await f.Timers.ListPendingHistoryAsync(new())).Items.Count);
        Assert.False(await f.Timers.DischargeUnrecordedCompletionAsync(Completion(), 2));
        Assert.True(await f.Timers.DischargeUnrecordedCompletionAsync(Completion(), 2));
        var retry = Completion() with { Checkpoint = Completion().Checkpoint with { CheckpointId = "later" } };
        Assert.True(await f.Timers.DischargeUnrecordedCompletionAsync(retry, 2));
        foreach (var original in originals)
        {
            var saved = await f.Timers.SaveAsync(original);
            Assert.Equal("completed", saved.HistoryResponsibility!.NoRecordCompletion!.CheckpointId);
            Assert.Empty(saved.HistoryResponsibility.PendingEvents); Assert.Null(saved.Input); Assert.Null(saved.Metadata);
        }
        Assert.Empty(await f.Timers.ListDueAsync(At.AddHours(1), 10)); Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
        Assert.Null(await f.Executions.FindAsync("run")); Assert.Empty(f.State.HistoryHeads); Assert.Empty(f.Commits.ListCommits());
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => AddUnrecordedTimerAsync(f, "new"));
    }

    [Theory]
    [InlineData("native")] [InlineData("head")] [InlineData("tombstone")]
    [InlineData("scope")] [InlineData("partition")] [InlineData("policy")]
    [InlineData("stale-fence")] [InlineData("expired")] [InlineData("disabled")]
    [InlineData("retain-work")] [InlineData("not-completed")] [InlineData("missing-fence")]
    [InlineData("incomplete-policy")]
    public async Task Invalid_no_record_authority_leaves_the_original_source_owed(string mode)
    {
        var f = await Fixture.CreateAsync(native: false, head: false); await AddUnrecordedTimerAsync(f, "timer");
        var completion = Completion(); var workflow = completion.StateChanges.WorkflowExecution!.State;
        if (mode == "native") await f.SaveNativeAsync();
        if (mode == "head") f.State.HistoryHeads[("scope-a", "run")] = new("run", 0, 0, "retained", At, NoRecordPolicy, true, false, 0);
        if (mode == "tombstone") await f.Liveness.SaveAsync(new(RuntimeExecutionOwnershipStateId.For("run"), "run",
            new("lease", "run", "owner", At, At.AddHours(1), 1), null, null, null, metadata:
            new Dictionary<string, string> { [RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone] = "true" }));
        if (mode == "scope") f.Access.Current = PersistenceAccessContext.Scoped(new("scope-b"));
        if (mode == "partition") workflow = workflow with { Partition = new("scope-b") };
        if (mode is "policy" or "incomplete-policy") workflow = workflow with
        {
            HistoryCapturePolicy = new("runtime-history-v3", 3, mode == "incomplete-policy", mode == "incomplete-policy" ? ["TimerScheduled"] : NoRecordPolicy.SupportedKinds)
        };
        if (mode == "disabled") workflow = workflow with { HistoryCaptureEnabledAtAdmission = false };
        if (mode == "not-completed") workflow = workflow with { Status = WorkflowExecutionStatus.Faulted };
        completion = completion with { StateChanges = new(new("run", RuntimeStateChangeOperation.Upsert, workflow,
            new Dictionary<string, string>()), null, [], [], [], [], []) };
        if (mode == "stale-fence") completion = completion with { ExpectedFence = new("other", "owner", 1) };
        if (mode == "missing-fence") completion = completion with { ExpectedFence = null };
        if (mode == "expired") f.Clock.Advance(TimeSpan.FromHours(2));
        if (mode == "retain-work") completion = completion with { RetainSchedulerWork = true };
        if (mode is "stale-fence" or "expired")
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => f.Timers.DischargeUnrecordedCompletionAsync(completion).AsTask());
        else
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => f.Timers.DischargeUnrecordedCompletionAsync(completion).AsTask());
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
        Assert.True(await f.Timers.HasPendingHistoryAsync("run"));
        Assert.Null((await f.Timers.ListPendingHistoryAsync(new())).Items.Single().HistoryResponsibility!.NoRecordCompletion);
        Assert.Null(await f.Timers.FindAsync("run", "timer"));
        Assert.Empty(f.State.TimerSources.NoRecordCompletionRuns);
    }

    [Fact]
    public async Task Partial_discharge_then_first_fault_preserves_the_gap_on_later_native_history()
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        await AddUnrecordedTimerAsync(f, "one"); await AddUnrecordedTimerAsync(f, "two");
        Assert.False(await f.Timers.DischargeUnrecordedCompletionAsync(Completion(), 1));
        var completion = Completion(); var workflow = completion.StateChanges.WorkflowExecution!.State with { Status = WorkflowExecutionStatus.Faulted };
        var fault = completion with { CommitId = "first-fault", Checkpoint = completion.Checkpoint with
            { CheckpointId = "first-fault", Name = RuntimeCheckpointNames.WorkflowFaulted, HistoryStartsAtAdmission = true },
            StateChanges = new(new("run", RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()), null, [], [], [], [], []) };
        // Supplied semantic input: qualify the actual native history owner/gap fold, without
        // claiming timer-profile enrollment or the separately owned capture/producer pipeline.
        await f.Commits.CommitAsync(WithFaultBatch(fault), new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.True(f.State.HistoryHeads[("scope-a", "run")].HasGaps);
        Assert.True(await f.Timers.HasPendingHistoryAsync("run"));
        var next = fault with { CommitId = "later-fault", Checkpoint = fault.Checkpoint with { CheckpointId = "later-fault" } };
        await f.Commits.CommitAsync(WithFaultBatch(next), new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.True(f.State.HistoryHeads[("scope-a", "run")].HasGaps);
    }

    private static RuntimeCheckpointCommit WithFaultBatch(RuntimeCheckpointCommit commit, RuntimeHistoryCapturePolicy? policy = null) => commit with
    {
        HistoryBatches = [new(policy ?? NoRecordPolicy, commit.Checkpoint.CheckpointId, At, true,
            [new(new RuntimeHistoryEvent(commit.Checkpoint.CheckpointId + ":event", "run", 0, RuntimeHistoryEventCategory.WorkflowLifecycle,
                "WorkflowFaulted", At, checkpointId: commit.Checkpoint.CheckpointId) { Authority = RuntimeHistoryAuthority.CommittedTransition }, 0)])]
    };

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_no_record_discard_advances_queue_only_after_qualified_source_discharge(bool qualified)
    {
        var f = await Fixture.CreateAsync(native: false, head: false); await AddUnrecordedTimerAsync(f, "timer");
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(new("start", "run", "command", WorkflowExecutionCommandKind.Start, "envelope", "key", At, At));
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = new RuntimeCoalescingSession("run", queue, new() { RecordCompletion = false }, mode: new(PersistenceMode.Ephemeral, false));
        await session.ConsumePumpedOverlayItemAsync("start", CancellationToken.None); using var scope = accessor.Push(session);
        var store = new CoalescingRuntimeCheckpointCommitStore(new(f.Commits), accessor, null, [], qualified ? f.Timers : null);
        if (qualified)
        {
            await store.CommitAsync(Completion(), new(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("run"))); Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
        }
        else
        {
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => store.CommitAsync(Completion(), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
            Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("run"))); Assert.True(await f.Timers.HasPendingHistoryAsync("run"));
        }
        Assert.Null(await f.Executions.FindAsync("run")); Assert.Empty(f.State.HistoryHeads); Assert.Empty(f.Commits.ListCommits());
    }

    [Fact]
    public async Task Bounded_actual_discard_keeps_queue_owed_until_the_remaining_source_prefix_is_discharged()
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        for (var i = 0; i < 257; i++) await AddUnrecordedTimerAsync(f, "timer-" + i);
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(new("start", "run", "command", WorkflowExecutionCommandKind.Start, "envelope", "key", At, At));
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var session = new RuntimeCoalescingSession("run", queue, new() { RecordCompletion = false }, mode: new(PersistenceMode.Ephemeral, false));
            await session.ConsumePumpedOverlayItemAsync("start", CancellationToken.None); using var scope = accessor.Push(session);
            var store = new CoalescingRuntimeCheckpointCommitStore(new(f.Commits), accessor, null, [], f.Timers);
            if (attempt == 0)
            {
                await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => store.CommitAsync(Completion(), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
                Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("run")));
                Assert.Single((await f.Timers.ListPendingHistoryAsync(new())).Items);
            }
            else
            {
                await store.CommitAsync(Completion(), new(RuntimeCheckpointPersistenceMode.Immediate));
                Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("run"))); Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
            }
        }
        Assert.Empty(f.Commits.ListCommits()); Assert.Null(await f.Executions.FindAsync("run"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Access_change_or_cancellation_while_waiting_for_discharge_leaves_sources_owed(bool cancel)
    {
        var f = await Fixture.CreateAsync(native: false, head: false); await AddUnrecordedTimerAsync(f, "timer");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = f.State.ExecuteWriteGateAsync(async _ => { entered.SetResult(); await release.Task; return true; }).AsTask();
        await entered.Task; using var cancellation = new CancellationTokenSource();
        var discharge = f.Timers.DischargeUnrecordedCompletionAsync(Completion(), cancellationToken: cancellation.Token).AsTask();
        if (cancel) cancellation.Cancel(); else f.Access.Current = PersistenceAccessContext.Scoped(new("scope-b"));
        release.SetResult(); await holder;
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discharge);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => discharge);
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
        Assert.True(await f.Timers.HasPendingHistoryAsync("run")); Assert.Empty(f.State.TimerSources.NoRecordCompletionRuns);
    }

    [Fact]
    public async Task A_real_native_recovery_can_admit_a_new_source_under_its_current_policy()
    {
        var f = await Fixture.CreateAsync(native: false, head: false); var original = await AddUnrecordedTimerAsync(f, "original");
        Assert.True(await f.Timers.DischargeUnrecordedCompletionAsync(Completion()));
        var native = Completion().StateChanges.WorkflowExecution!.State with { Status = WorkflowExecutionStatus.Suspended, CompletedAt = null };
        await f.Executions.SaveAsync(native);
        var next = await AddUnrecordedTimerAsync(f, "recovered", retired: false);
        Assert.False(next.HistoryResponsibility!.Retired); Assert.True(await f.Timers.HasPendingHistoryAsync("run"));
        Assert.Equal("completed", (await f.Timers.SaveAsync(original)).HistoryResponsibility!.NoRecordCompletion!.CheckpointId);
        Assert.Null(await f.Timers.FindAsync("run", "original"));
    }

    [Fact]
    public async Task Empty_actual_discard_closes_new_pre_head_sources_without_inventing_a_gap()
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(new("start", "run", "command", WorkflowExecutionCommandKind.Start, "envelope", "key", At, At));
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = new RuntimeCoalescingSession("run", queue, new() { RecordCompletion = false }, mode: new(PersistenceMode.Ephemeral, false));
        await session.ConsumePumpedOverlayItemAsync("start", CancellationToken.None); using var scope = accessor.Push(session);
        var store = new CoalescingRuntimeCheckpointCommitStore(new(f.Commits), accessor, null, [], f.Timers);
        await store.CommitAsync(Completion(), new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("run")));
        Assert.Empty(f.State.TimerSources.NoRecordCompletionRuns);
        Assert.Equal("completed", f.State.TimerSources.NoRecordClosures["run"].CheckpointId);
        Assert.True(await f.Timers.DischargeUnrecordedCompletionAsync(Completion() with { Checkpoint = Completion().Checkpoint with { CheckpointId = "later" } }));
        Assert.Equal("completed", f.State.TimerSources.NoRecordClosures["run"].CheckpointId);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => AddUnrecordedTimerAsync(f, "too-late"));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.Timers.SaveAsync(Timer("legacy-too-late", captured: false)).AsTask());
        var completion = Completion();
        var fault = completion with { CommitId = "empty-first-fault", Checkpoint = completion.Checkpoint with
            { CheckpointId = "empty-first-fault", Name = RuntimeCheckpointNames.WorkflowFaulted, HistoryStartsAtAdmission = true },
            StateChanges = new(new("run", RuntimeStateChangeOperation.Upsert,
                completion.StateChanges.WorkflowExecution!.State with { Status = WorkflowExecutionStatus.Faulted }, new Dictionary<string, string>()), null, [], [], [], [], []) };
        await f.Commits.CommitAsync(WithFaultBatch(fault), new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.False(f.State.HistoryHeads[("scope-a", "run")].HasGaps);
    }

    [Theory]
    [InlineData("state")] [InlineData("clock")]
    public async Task A_foreign_coherent_timer_owner_cannot_discharge_the_actual_native_queue(string mismatch)
    {
        var f = await Fixture.CreateAsync(native: false, head: false); await AddUnrecordedTimerAsync(f, "owed");
        var foreignState = mismatch == "clock" ? f.State : new InMemoryRuntimeCheckpointStoreState();
        var foreignNative = mismatch == "clock" ? f.Executions : new InMemoryWorkflowExecutionStateStore(foreignState, f.Liveness);
        var foreignTimer = new InMemoryDurableTimerStore(foreignState, f.Access, foreignNative, f.Liveness,
            mismatch == "clock" ? new FakeTimeProvider(At) : f.Clock);
        if (mismatch == "clock") f.Clock.Advance(TimeSpan.FromHours(2));
        Assert.True(foreignTimer.SupportsHistoryResponsibility); Assert.False(foreignTimer.CanDischargeUnrecordedCompletion(f.Commits));
        Assert.True(f.Timers.CanDischargeUnrecordedCompletion(f.Commits));
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(new("start", "run", "command", WorkflowExecutionCommandKind.Start, "envelope", "key", At, At));
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = new RuntimeCoalescingSession("run", queue, new() { RecordCompletion = false }, mode: new(PersistenceMode.Ephemeral, false));
        await session.ConsumePumpedOverlayItemAsync("start", CancellationToken.None); using var scope = accessor.Push(session);
        var store = new CoalescingRuntimeCheckpointCommitStore(new(f.Commits), accessor, null, [], foreignTimer);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => store.CommitAsync(Completion(), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("run")));
        Assert.True(await f.Timers.HasPendingHistoryAsync("run")); Assert.Empty(foreignState.TimerSources.NoRecordClosures);
        Assert.Empty(f.Commits.ListCommits()); Assert.Null(await f.Executions.FindAsync("run"));
    }

    [Theory]
    [InlineData("scope")] [InlineData("policy")]
    public async Task First_native_history_cannot_reassign_the_original_timer_source_owner(string mismatch)
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        await AddUnrecordedTimerAsync(f, "one"); await AddUnrecordedTimerAsync(f, "two");
        Assert.False(await f.Timers.DischargeUnrecordedCompletionAsync(Completion(), 1));
        var policy = mismatch == "policy" ? new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, false, NoRecordPolicy.SupportedKinds) : NoRecordPolicy;
        var completion = Completion(); var workflow = completion.StateChanges.WorkflowExecution!.State with
            { Status = WorkflowExecutionStatus.Faulted, TenantId = null, Partition = null, HistoryCapturePolicy = policy };
        var fault = completion with { CommitId = "foreign-first-fault", Checkpoint = completion.Checkpoint with
            { CheckpointId = "foreign-first-fault", Name = RuntimeCheckpointNames.WorkflowFaulted, HistoryStartsAtAdmission = true },
            StateChanges = new(new("run", RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()), null, [], [], [], [], []) };
        if (mismatch == "scope") f.Access.Current = PersistenceAccessContext.Scoped(new("scope-b"));
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => f.Commits.CommitAsync(WithFaultBatch(fault, policy), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Empty(f.State.HistoryHeads); Assert.Empty(f.Commits.ListCommits()); Assert.Null(await f.Executions.FindAsync("run"));
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
        Assert.Single((await f.Timers.ListPendingHistoryAsync(new())).Items);
        Assert.Equal("scope-a", f.State.TimerSources.SourceOwners["run"].Scope);
    }

    [Fact]
    public async Task Invalid_completion_receipt_input_publishes_no_empty_source_owner_or_closure()
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        var completion = Completion() with { Checkpoint = Completion().Checkpoint with { CheckpointId = new string('x', 1025) } };
        await Assert.ThrowsAsync<ArgumentException>(() => f.Timers.DischargeUnrecordedCompletionAsync(completion).AsTask());
        Assert.Empty(f.State.TimerSources.SourceOwners); Assert.Empty(f.State.TimerSources.NoRecordClosures);
        Assert.Empty(f.State.HistoryHeads); Assert.Empty(f.Commits.ListCommits());
    }

    private static RuntimeCheckpointCommit WithoutCaptureChoice(RuntimeCheckpointCommit completion) => completion with
    {
        StateChanges = new(new("run", RuntimeStateChangeOperation.Upsert, completion.StateChanges.WorkflowExecution!.State with
            { HistoryCapturePolicy = null, HistoryCaptureEnabledAtAdmission = null }, new Dictionary<string, string>()), null, [], [], [], [], [])
    };

    [Theory]
    [InlineData("correct")] [InlineData("legacy")] [InlineData("foreign")]
    public async Task Clearing_the_incoming_capture_choice_cannot_waive_the_actual_native_timer_owner(string selected)
    {
        var f = await Fixture.CreateAsync(native: false, head: false); await AddUnrecordedTimerAsync(f, "owed");
        IDurableTimerStore timer = f.Timers;
        if (selected == "legacy") timer = new InMemoryDurableTimerStore();
        if (selected == "foreign")
        {
            var state = new InMemoryRuntimeCheckpointStoreState();
            timer = new InMemoryDurableTimerStore(state, f.Access, new InMemoryWorkflowExecutionStateStore(state, f.Liveness), f.Liveness, f.Clock);
        }
        Assert.True(await ((IRuntimeCheckpointCommitStore)f.Commits).RequiresUnrecordedTimerDischargeAsync("run"));
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(new("start", "run", "command", WorkflowExecutionCommandKind.Start, "envelope", "key", At, At));
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = new RuntimeCoalescingSession("run", queue, new() { RecordCompletion = false }, mode: new(PersistenceMode.Ephemeral, false));
        await session.ConsumePumpedOverlayItemAsync("start", CancellationToken.None); using var scope = accessor.Push(session);
        var store = new CoalescingRuntimeCheckpointCommitStore(new(f.Commits), accessor, null, [], timer);
        var failure = await Record.ExceptionAsync(() => store.CommitAsync(WithoutCaptureChoice(Completion()), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        if (selected == "correct") Assert.IsType<InvalidOperationException>(failure);
        else Assert.IsType<RuntimeSchedulerWorkUnavailableException>(failure);
        Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("run")));
        Assert.True(await f.Timers.HasPendingHistoryAsync("run")); Assert.Empty(f.State.TimerSources.NoRecordClosures);
        Assert.Empty(f.Commits.ListCommits()); Assert.Null(await f.Executions.FindAsync("run"));
    }

    [Fact]
    public async Task Legacy_unrecorded_completion_without_a_timer_owner_keeps_its_existing_queue_behavior()
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(new("start", "run", "command", WorkflowExecutionCommandKind.Start, "envelope", "key", At, At));
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = new RuntimeCoalescingSession("run", queue, new() { RecordCompletion = false }, mode: new(PersistenceMode.Ephemeral, false));
        await session.ConsumePumpedOverlayItemAsync("start", CancellationToken.None); using var scope = accessor.Push(session);
        var store = new CoalescingRuntimeCheckpointCommitStore(new(f.Commits), accessor, null, [], new InMemoryDurableTimerStore());
        await store.CommitAsync(WithoutCaptureChoice(Completion()), new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("run")));
        Assert.Empty(f.State.TimerSources.SourceOwners); Assert.Empty(f.State.TimerSources.NoRecordClosures);
        Assert.Empty(f.Commits.ListCommits()); Assert.Null(await f.Executions.FindAsync("run"));
    }

    [Theory]
    [InlineData("global")] [InlineData("missing-access")]
    public async Task Scope_free_legacy_native_admission_cannot_bypass_a_retained_timer_capture_choice(string mode)
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        await AddUnrecordedTimerAsync(f, "one"); await AddUnrecordedTimerAsync(f, "two");
        Assert.False(await f.Timers.DischargeUnrecordedCompletionAsync(Completion(), 1));
        var writer = mode == "missing-access" ? new InMemoryRuntimeCheckpointCommitStore(workflowExecutionStateStore: f.Executions,
            operationalStateStore: f.Liveness, state: f.State, timeProvider: f.Clock, rootWriteLeaseManager: new Lease()) : f.Commits;
        if (mode == "global") f.Access.Current = PersistenceAccessContext.Global;
        var raw = WithoutCaptureChoice(Completion());
        raw = raw with { StateChanges = new(new("run", RuntimeStateChangeOperation.Upsert,
            raw.StateChanges.WorkflowExecution!.State with { TenantId = null, Partition = null }, new Dictionary<string, string>()), null, [], [], [], [], []) };
        if (mode == "missing-access")
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => writer.CommitAsync(raw, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        else
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => writer.CommitAsync(raw, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Empty(f.State.HistoryHeads); Assert.Null(await f.Executions.FindAsync("run")); Assert.Empty(writer.ListCommits());
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
        Assert.Single((await f.Timers.ListPendingHistoryAsync(new())).Items);
    }
}

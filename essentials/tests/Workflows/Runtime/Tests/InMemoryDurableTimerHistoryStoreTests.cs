using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Actual memory timer-owner/observer controls with supplied native checkpoint inputs; no pump, activity or durable-provider proof.</summary>
public sealed partial class InMemoryDurableTimerHistoryStoreTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true,
        ["Transition", "TimerScheduled", "TimerDispatched", "TimerRetired"]);
    private static DurableTimer Timer(string id = "timer", string run = "run", bool captured = true)
    {
        var timer = new DurableTimer(id, run, "Timer", "stimulus", At.AddMinutes(1), At,
            JsonSerializer.SerializeToElement(new { secret = "original" }), new Dictionary<string, string> { ["secret"] = "original" },
            ActivityExecutionId: "activity", ExecutionScopeId: "activity-scope");
        return captured ? timer with { HistoryResponsibility = new(Policy, [DurableTimerHistoryFacts.CreateScheduled(timer, Policy)!.Event], false) } : timer;
    }

    [Fact]
    public async Task Legacy_default_refuses_new_capture_and_keeps_existing_timer_behavior()
    {
        var legacy = new InMemoryDurableTimerStore(); Assert.False(legacy.SupportsHistoryResponsibility);
        await Assert.ThrowsAsync<NotSupportedException>(() => legacy.SaveAsync(Timer()).AsTask());
        var original = await legacy.SaveAsync(Timer(captured: false));
        Assert.Same(original, await legacy.SaveAsync(original with { DueTime = At.AddDays(2) }));
        await legacy.DeleteAsync("run", "timer"); Assert.Null(await legacy.FindAsync("run", "timer"));
    }

    [Theory]
    [InlineData("global")] [InlineData("privileged")] [InlineData("native")] [InlineData("liveness")]
    public async Task Capability_requires_the_exact_native_liveness_chain_and_ordinary_scope(string mismatch)
    {
        var f = new Fixture();
        if (mismatch == "global") f.Access.Current = PersistenceAccessContext.Global;
        if (mismatch == "privileged") f.Access.Current = PersistenceAccessContext.PrivilegedScoped(new("scope-a"), new("timer-tests"));
        var store = new InMemoryDurableTimerStore(f.State, f.Access,
            mismatch == "native" ? new InMemoryWorkflowExecutionStateStore() : f.Executions,
            mismatch == "liveness" ? new InMemoryExecutionLivenessStateStore() : f.Liveness, f.Clock);
        Assert.False(store.SupportsHistoryResponsibility);
        await Assert.ThrowsAsync<NotSupportedException>(() => store.SaveAsync(Timer()).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => store.SaveAsync(Timer(captured: false)).AsTask());
    }

    [Fact]
    public async Task Unqualified_internal_adapter_cannot_insert_a_legacy_timer_in_another_pinned_scope()
    {
        var f = await Fixture.CreateAsync(); await f.Timers.SaveAsync(Timer());
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-b"));
        var wrong = new InMemoryDurableTimerStore(f.State, f.Access, f.Executions, new InMemoryExecutionLivenessStateStore(), f.Clock);
        await Assert.ThrowsAsync<NotSupportedException>(() => wrong.SaveAsync(Timer("injected", captured: false)).AsTask());
        Assert.Empty(await f.NewAdapter().ListDueAsync(At.AddMinutes(1), 10));
        Assert.False(await f.NewAdapter().HasPendingHistoryAsync("run"));
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
        Assert.Single((await f.Timers.ListPendingHistoryAsync(new())).Items);
    }

    [Theory]
    [InlineData("schedule-only")] [InlineData("no-retirement")] [InlineData("no-dispatch")]
    public async Task A_partial_timer_vocabulary_cannot_admit_an_unretirable_source(string missing)
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        string[] kinds = missing switch
        {
            "schedule-only" => ["TimerScheduled"],
            "no-retirement" => ["TimerScheduled", "TimerDispatched"],
            _ => ["TimerScheduled", "TimerRetired"]
        };
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true, kinds);
        var timer = Timer(captured: false);
        timer = timer with { HistoryResponsibility = new(policy, [DurableTimerHistoryFacts.CreateScheduled(timer, policy)!.Event], false) };
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Timers.SaveAsync(timer).AsTask());
        Assert.Null(await f.Timers.FindAsync("run", "timer")); Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
    }

    [Fact]
    public async Task Scheduling_before_the_native_head_keeps_responsibility_without_fabricating_history()
    {
        var f = await Fixture.CreateAsync(native: false, head: false); var timer = await f.Timers.SaveAsync(Timer());
        Assert.True(f.Timers.SupportsHistoryResponsibility); Assert.True(await f.Timers.HasPendingHistoryAsync("run"));
        Assert.Same(timer, Assert.Single((await f.Timers.ListPendingHistoryAsync(new())).Items));
        Assert.Null(await f.Reader.FindHeadAsync("run"));
        Assert.False((await f.Writer.RecordAsync(new(Policy, timer.HistoryResponsibility!.PendingEvents[0]))).Recorded);
        Assert.False(await f.Timers.AcknowledgeHistoryAsync("run", "timer", timer.HistoryResponsibility.PendingEvents[0].HistoryEventId));
        Assert.Single((await f.Timers.ListPendingHistoryAsync(new())).Items);
    }

    [Fact]
    public async Task The_first_unrecorded_source_pins_its_scope_before_any_native_header_exists()
    {
        var f = await Fixture.CreateAsync(native: false, head: false); await f.Timers.SaveAsync(Timer());
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-b"));
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => f.NewAdapter().SaveAsync(Timer("foreign")).AsTask());
        Assert.Empty((await f.Timers.ListPendingHistoryAsync(new())).Items);
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
        Assert.Single((await f.Timers.ListPendingHistoryAsync(new())).Items);
    }

    [Fact]
    public async Task Adapter_reconstruction_replays_the_first_source_and_never_reactivates_retirement()
    {
        var f = await Fixture.CreateAsync(); var original = await f.Timers.SaveAsync(Timer());
        var next = f.NewAdapter();
        Assert.Same(original, await next.SaveAsync(Timer() with { DueTime = At.AddDays(1) }));
        await Assert.ThrowsAsync<NotSupportedException>(() => next.DeleteAsync("run", "timer").AsTask());
        await next.RetireWithHistoryAsync("run", "timer", DurableTimerHistoryFacts.CreateCancelled(original, At)!);
        await next.DeleteAsync("run", "timer"); f.Clock.Advance(TimeSpan.FromDays(2));
        var replay = await f.NewAdapter().SaveAsync(Timer());
        Assert.True(replay.HistoryResponsibility!.Retired); Assert.Equal(original.DueTime, replay.DueTime);
        Assert.Null(await next.FindAsync("run", "timer")); Assert.Null(await next.GetNextDueTimeAsync());
        Assert.Empty(await next.ListDueAsync(f.Clock.GetUtcNow(), 10));
        Assert.Empty(await next.ClaimDueAsync(new("pump", f.Clock.GetUtcNow(), TimeSpan.FromMinutes(1), 10)));
        Assert.Empty((await next.ListPageAsync(new("run"))).Items);
        Assert.Equal(2, Assert.Single((await next.ListPendingHistoryAsync(new())).Items).HistoryResponsibility!.PendingEvents.Count);
    }

    [Theory]
    [InlineData("expired")] [InlineData("terminal")] [InlineData("policy")] [InlineData("disabled")]
    public async Task New_source_admission_refuses_expired_or_incompatible_native_authority(string mismatch)
    {
        var f = await Fixture.CreateAsync(); var native = (await f.Executions.FindAsync("run"))!;
        if (mismatch == "expired") f.Clock.Advance(TimeSpan.FromHours(2));
        else await f.Executions.SaveAsync(mismatch switch
        {
            "terminal" => native with { Status = WorkflowExecutionStatus.Completed, CompletedAt = At },
            "policy" => native with { HistoryCapturePolicy = null },
            _ => native with { HistoryCaptureEnabledAtAdmission = false }
        });
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.Timers.SaveAsync(Timer()).AsTask());
        Assert.Empty((await f.Timers.ListPendingHistoryAsync(new())).Items);
    }

    [Fact]
    public async Task Native_deletion_tombstone_refuses_new_source_without_restoring_it()
    {
        var f = await Fixture.CreateAsync();
        await f.Liveness.ExecuteWorkflowDeletionAtomicAsync("run", _ => ValueTask.FromResult(true));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.Timers.SaveAsync(Timer()).AsTask());
        Assert.Empty((await f.Timers.ListPendingHistoryAsync(new())).Items);
    }

    [Fact]
    public async Task An_explicit_foreign_native_partition_is_refused_even_with_no_logical_tenant()
    {
        var f = await Fixture.CreateAsync(); var native = (await f.Executions.FindAsync("run"))!;
        await f.Executions.SaveAsync(native with { TenantId = null, Partition = new("foreign") });
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Timers.SaveAsync(Timer()).AsTask());
        Assert.Empty((await f.Timers.ListPendingHistoryAsync(new())).Items);
    }

    [Fact]
    public async Task Legacy_winner_stays_uncaptured_but_new_timer_under_a_pinned_timer_profile_cannot_drop_responsibility()
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        var original = await f.Timers.SaveAsync(Timer(captured: false));
        Assert.Same(original, await f.Timers.SaveAsync(Timer()));
        Assert.Null(original.HistoryResponsibility);
        await f.SaveNativeAsync();
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.Timers.SaveAsync(Timer("new", captured: false)).AsTask());
        Assert.Same(original, await f.Timers.SaveAsync(Timer(captured: false)));
    }

    [Fact]
    public async Task Acknowledgement_requires_the_committed_semantic_receipt_and_preserves_a_live_claim()
    {
        var f = await Fixture.CreateAsync(); var timer = await f.Timers.SaveAsync(Timer());
        var claim = Assert.Single(await f.Timers.ClaimDueAsync(new("pump", At.AddMinutes(1), TimeSpan.FromMinutes(1), 1)));
        var schedule = new RuntimeHistoryObservation(Policy, timer.HistoryResponsibility!.PendingEvents[0]);
        Assert.False(await f.Timers.AcknowledgeHistoryAsync("run", "timer", schedule.Event.HistoryEventId));
        var recorded = await f.Writer.RecordAsync(schedule); Assert.True(recorded.Recorded);
        Assert.True(await f.NewAdapter().AcknowledgeHistoryAsync("run", "timer", schedule.Event.HistoryEventId));
        Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
        var terminal = DurableTimerHistoryFacts.CreateDispatched(timer, At.AddMinutes(1), BookmarkResumeDispatchStatus.Dispatched)!;
        Assert.True((await f.Timers.CompleteClaimWithHistoryAsync(claim, terminal)).Succeeded);
        Assert.True(await f.Timers.HasPendingHistoryAsync("run"));
        Assert.True((await f.Writer.RecordAsync(terminal)).Recorded);
        Assert.True(await f.Timers.AcknowledgeHistoryAsync("run", "timer", terminal.Event.HistoryEventId));
        Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
        var receipt = await f.Timers.SaveAsync(Timer());
        Assert.True(receipt.HistoryResponsibility!.Retired); Assert.Empty(receipt.HistoryResponsibility.PendingEvents);
        Assert.Null(receipt.Input); Assert.Null(receipt.Metadata); Assert.Null(receipt.PayloadType); Assert.Null(receipt.ProviderId);
        var head = (await f.Reader.FindHeadAsync("run"))!; Assert.Equal(3, head.LastSequence);
        Assert.Equal("seed", head.LastPhysicalCommitId); Assert.Single(f.Commits.ListCommits());
    }

    [Theory]
    [InlineData("fingerprint")] [InlineData("position")] [InlineData("event")] [InlineData("lower-bound")]
    public async Task Corrupt_acknowledgement_receipts_leave_the_source_owed(string corruption)
    {
        var f = await Fixture.CreateAsync(); var timer = await f.Timers.SaveAsync(Timer());
        var e = timer.HistoryResponsibility!.PendingEvents[0]; await f.Writer.RecordAsync(new(Policy, e));
        // Deliberate retained-state corruption fixture; the real observation writer created the original receipt.
        var key = ("scope-a", "run", e.HistoryEventId);
        if (corruption == "fingerprint") f.State.HistoryCandidateFingerprints[key] = "wrong";
        else if (corruption == "position") f.State.HistoryReceiptPositions[key] = 0;
        else if (corruption == "event") f.State.HistoryEventsById.Remove(key);
        else
        {
            var owner = ("scope-a", "run"); var head = f.State.HistoryHeads[owner];
            f.State.HistoryHeads[owner] = head with { FirstSequence = head.LastSequence + 1 };
        }
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => f.Timers.AcknowledgeHistoryAsync("run", "timer", e.HistoryEventId).AsTask());
        Assert.True(await f.Timers.HasPendingHistoryAsync("run"));
    }

    [Fact]
    public async Task Stale_claim_cannot_retire_and_legacy_completion_cannot_discard_captured_responsibility()
    {
        var f = await Fixture.CreateAsync(); var timer = await f.Timers.SaveAsync(Timer());
        var old = Assert.Single(await f.Timers.ClaimDueAsync(new("old", At.AddMinutes(1), TimeSpan.FromMinutes(1), 1)));
        var current = Assert.Single(await f.Timers.ClaimDueAsync(new("current", At.AddMinutes(2), TimeSpan.FromMinutes(1), 1)));
        var terminal = DurableTimerHistoryFacts.CreateDispatched(timer, At.AddMinutes(2), BookmarkResumeDispatchStatus.Duplicate)!;
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Stale, (await f.Timers.CompleteClaimWithHistoryAsync(old, terminal)).Status);
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Timers.CompleteClaimAsync(current).AsTask());
        Assert.NotNull(await f.Timers.FindAsync("run", "timer"));
        Assert.True((await f.Timers.CompleteClaimWithHistoryAsync(current, terminal)).Succeeded);
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.AlreadyApplied, (await f.Timers.CompleteClaimWithHistoryAsync(old, terminal)).Status);
        Assert.Equal(2, Assert.Single((await f.Timers.ListPendingHistoryAsync(new())).Items).HistoryResponsibility!.PendingEvents.Count);
    }

    [Theory]
    [InlineData("activity")] [InlineData("scope")] [InlineData("due")] [InlineData("scheduled")]
    public async Task Retirement_validates_exact_source_even_after_the_schedule_was_acknowledged(string mismatch)
    {
        var f = await Fixture.CreateAsync(); var timer = await f.Timers.SaveAsync(Timer()); var scheduled = timer.HistoryResponsibility!.PendingEvents[0];
        await f.Writer.RecordAsync(new(Policy, scheduled)); await f.Timers.AcknowledgeHistoryAsync("run", "timer", scheduled.HistoryEventId);
        var foreign = mismatch switch
        {
            "activity" => timer with { ActivityExecutionId = "foreign" },
            "scope" => timer with { ExecutionScopeId = "foreign" },
            "due" => timer with { DueTime = At.AddDays(1) },
            _ => timer
        };
        var observation = mismatch == "scheduled" ? new RuntimeHistoryObservation(Policy, scheduled) : DurableTimerHistoryFacts.CreateCancelled(foreign, At)!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Timers.RetireWithHistoryAsync("run", "timer", observation).AsTask());
        Assert.NotNull(await f.Timers.FindAsync("run", "timer")); Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
    }

    [Fact]
    public async Task Pending_pages_are_bounded_scope_bound_and_stable_when_a_prior_timer_is_acknowledged()
    {
        var f = await Fixture.CreateAsync();
        foreach (var id in new[] { "one", "two", "three" }) await f.Timers.SaveAsync(Timer(id));
        var first = await f.Timers.ListPendingHistoryAsync(new(1)); var firstTimer = Assert.Single(first.Items);
        Assert.NotNull(first.NextContinuationToken);
        var e = firstTimer.HistoryResponsibility!.PendingEvents[0]; await f.Writer.RecordAsync(new(Policy, e));
        await f.Timers.AcknowledgeHistoryAsync("run", firstTimer.TimerId, e.HistoryEventId);
        var second = await f.NewAdapter().ListPendingHistoryAsync(new(1, first.NextContinuationToken));
        Assert.Single(second.Items); Assert.NotEqual(firstTimer.TimerId, second.Items[0].TimerId);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Timers.ListPendingHistoryAsync(new(2, first.NextContinuationToken)).AsTask());
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-b"));
        Assert.Empty((await f.Timers.ListPendingHistoryAsync(new())).Items);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Timers.ListPendingHistoryAsync(new(1, first.NextContinuationToken)).AsTask());
    }

    [Fact]
    public async Task Access_change_while_waiting_for_native_gate_publishes_no_source()
    {
        var f = await Fixture.CreateAsync(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = f.State.ExecuteWriteGateAsync(async _ => { entered.SetResult(); await release.Task; return true; }).AsTask();
        await entered.Task;
        var save = f.Timers.SaveAsync(Timer()).AsTask(); f.Access.Current = PersistenceAccessContext.Scoped(new("scope-b"));
        release.SetResult(); await holder;
        await Assert.ThrowsAsync<InvalidOperationException>(() => save);
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
        Assert.Empty((await f.Timers.ListPendingHistoryAsync(new())).Items);
    }

    [Theory]
    [InlineData("other-scope")] [InlineData("global")] [InlineData("privileged")]
    [InlineData("foreign-owner")] [InlineData("legacy-adapter")]
    public async Task Actual_native_terminal_deletion_clears_indexed_timer_scopes_before_removing_the_run(string maintenance)
    {
        var access = new Access(); var clock = new FakeTimeProvider(At); var services = new ServiceCollection();
        services.AddSingleton<IPersistenceAccessContextAccessor>(access); services.AddSingleton<TimeProvider>(clock);
        services.AddWorkflowRuntime();
        services.AddScoped<IDurableTimerStore>(provider => maintenance == "legacy-adapter" ? new InMemoryDurableTimerStore() : new InMemoryDurableTimerStore(
            maintenance == "foreign-owner" ? new InMemoryRuntimeCheckpointStoreState() : provider.GetRequiredService<InMemoryRuntimeCheckpointStoreState>(), access, provider.GetRequiredService<IWorkflowExecutionStateStore>(),
            provider.GetRequiredService<IExecutionLivenessStateStore>(), clock));
        using var host = services.BuildServiceProvider(validateScopes: true); using var scope = host.CreateScope(); var sp = scope.ServiceProvider;
        var native = sp.GetRequiredService<IWorkflowExecutionStateStore>(); var liveness = sp.GetRequiredService<IExecutionLivenessStateStore>();
        var timers = new InMemoryDurableTimerStore(sp.GetRequiredService<InMemoryRuntimeCheckpointStoreState>(), access, native, liveness, clock);
        var initial = new WorkflowExecutionState("run", new("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, "scope-a", new Dictionary<string, string>())
            { HistoryCapturePolicy = Policy, HistoryCaptureEnabledAtAdmission = true };
        await native.SaveAsync(initial);
        await liveness.SaveAsync(new(RuntimeExecutionOwnershipStateId.For("run"), "run", new("lease", "run", "owner", At, At.AddHours(1), 1), null, null, null));
        await timers.SaveAsync(Timer()); await timers.SaveAsync(Timer("unrelated", "other", captured: false));
        await native.SaveAsync(initial with { Status = WorkflowExecutionStatus.Completed, CompletedAt = At });
        var deletion = sp.GetRequiredService<IWorkflowExecutionDeletionStore>(); var candidate = (await deletion.ReadAsync("run"))!;
        access.Current = maintenance switch
        {
            "global" => PersistenceAccessContext.Global,
            "privileged" => PersistenceAccessContext.PrivilegedScoped(new("scope-b"), new("timer-cleanup-test")),
            _ => PersistenceAccessContext.Scoped(new("scope-b"))
        };
        if (maintenance is "foreign-owner" or "legacy-adapter")
        {
            Assert.Equal(WorkflowExecutionDeletionResult.Unsupported, await deletion.TryDeleteTerminalAsync(candidate));
            Assert.NotNull(await native.FindAsync("run"));
            access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
            Assert.True(await timers.HasPendingHistoryAsync("run")); Assert.NotNull(await timers.FindAsync("run", "timer"));
            return;
        }
        Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await deletion.TryDeleteTerminalAsync(candidate));
        Assert.Null(await native.FindAsync("run"));
        access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
        Assert.False(await timers.HasPendingHistoryAsync("run")); Assert.Empty((await timers.ListPendingHistoryAsync(new())).Items);
        Assert.Null(await timers.FindAsync("run", "timer"));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => timers.SaveAsync(Timer()).AsTask());
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => timers.SaveAsync(Timer("late", captured: false)).AsTask());
        Assert.NotNull(await timers.FindAsync("other", "unrelated"));
    }

    [Theory]
    [InlineData("other-scope")] [InlineData("global")] [InlineData("privileged")]
    public async Task Direct_native_deletion_clears_its_actual_shared_timer_owner(string maintenance)
    {
        var f = await Fixture.CreateAsync(); await f.Timers.SaveAsync(Timer());
        f.Access.Current = maintenance switch
        {
            "global" => PersistenceAccessContext.Global,
            "privileged" => PersistenceAccessContext.PrivilegedScoped(new("scope-b"), new("timer-cleanup-test")),
            _ => PersistenceAccessContext.Scoped(new("scope-b"))
        };
        Assert.True(await f.Executions.DeleteAsync("run"));
        f.Access.Current = PersistenceAccessContext.Scoped(new("scope-a"));
        Assert.Null(await f.Timers.FindAsync("run", "timer")); Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
        Assert.Empty((await f.Timers.ListPendingHistoryAsync(new())).Items);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.Timers.SaveAsync(Timer()).AsTask());
    }

    private sealed class Fixture
    {
        internal InMemoryRuntimeCheckpointStoreState State { get; } = new();
        internal Access Access { get; } = new();
        internal FakeTimeProvider Clock { get; } = new(At);
        internal InMemoryExecutionLivenessStateStore Liveness { get; } = new();
        internal InMemoryWorkflowExecutionStateStore Executions { get; }
        internal InMemoryRuntimeCheckpointCommitStore Commits { get; }
        internal InMemoryRuntimeHistoryStore Reader { get; }
        internal InMemoryRuntimeHistoryObservationWriter Writer { get; }
        internal InMemoryDurableTimerStore Timers { get; }
        internal Fixture()
        {
            Executions = new(State, Liveness); Timers = NewAdapter();
            Commits = new(workflowExecutionStateStore: Executions, operationalStateStore: Liveness, state: State,
                persistenceAccessContextAccessor: Access, timeProvider: Clock, rootWriteLeaseManager: new Lease());
            Reader = new(State, Access, new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
                { SigningKey = "timer-owner-tests-signing-key-at-least-32-bytes" })));
            Writer = new(State, Access, Executions, Clock);
        }
        internal InMemoryDurableTimerStore NewAdapter() => new(State, Access, Executions, Liveness, Clock);
        internal async Task SaveNativeAsync()
        {
            await Executions.SaveAsync(new("run", new("artifact", "definition", "version", "1", "hash"),
                WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, "scope-a", new Dictionary<string, string>())
                { HistoryCapturePolicy = Policy, HistoryCaptureEnabledAtAdmission = true });
        }
        internal static async Task<Fixture> CreateAsync(bool native = true, bool head = true)
        {
            var f = new Fixture();
            await f.Liveness.SaveAsync(new(RuntimeExecutionOwnershipStateId.For("run"), "run",
                new("lease", "run", "owner", At, At.AddHours(1), 1), null, null, null));
            if (native) await f.SaveNativeAsync();
            if (head) await f.Commits.CommitAsync(new("seed", new("checkpoint", "test", "run", At, [], new Dictionary<string, string>()),
                new(null, null, [], [], [], [], []), [], new Dictionary<string, string>())
                { HistoryBatches = [new(Policy, "checkpoint", At, true, [new(new RuntimeHistoryEvent("initial", "run", 0,
                    RuntimeHistoryEventCategory.ActivityLifecycle, "Transition", At, checkpointId: "checkpoint")
                    { Authority = RuntimeHistoryAuthority.CommittedTransition }, 0)])] }, new(RuntimeCheckpointPersistenceMode.Immediate));
            return f;
        }
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new("scope-a")); }
    private sealed class Lease : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) => write(cancellationToken);
    }
}

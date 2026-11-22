using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

// Actual SQLite owner/transaction controls. These do not substitute for migrations, pump or four-provider proof.
public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    private static readonly RuntimeHistoryCapturePolicy TimerHistoryPolicy = new("runtime-history-v3", 3, true,
        ["TimerScheduled", "TimerDispatched", "TimerRetired"]);

    [Fact]
    public async Task Timer_history_before_native_retains_original_source_across_context_restart_without_inventing_a_head()
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        var original = await f.Store.SaveAsync(HistoryTimer());
        Assert.True(f.Store.SupportsHistoryResponsibility); Assert.True(await f.Store.HasPendingHistoryAsync("workflow-a"));
        Assert.False(await f.Store.AcknowledgeHistoryAsync("workflow-a", "timer", original.HistoryResponsibility!.PendingEvents[0].HistoryEventId));
        Assert.Empty(await f.Context.Set<RuntimeHistoryHeadEntity>().ToArrayAsync());
        await using var restarted = f.Database.Open("tenant-a");
        var store = new EfDurableTimerStore(restarted, f.Access, new NoopContinuationCodec(), f.Clock);
        var replay = await store.SaveAsync(HistoryTimer() with { DueTime = OccurredAt.AddDays(2) });
        Assert.Equal(original.DueTime, replay.DueTime);
        Assert.Equal(original.HistoryResponsibility.PendingEvents[0].HistoryEventId,
            Assert.Single((await store.ListPendingHistoryAsync(new())).Items).HistoryResponsibility!.PendingEvents[0].HistoryEventId);
    }

    [Fact]
    public async Task Timer_history_retirement_is_first_wins_and_never_reactivates_due_or_claim_indexes()
    {
        await using var f = await TimerHistoryFixture.Create(); var timer = await f.Store.SaveAsync(HistoryTimer());
        await f.Store.DeleteAsync("workflow-a", "timer");
        await f.Store.RetireWithHistoryAsync("workflow-a", "timer", DurableTimerHistoryFacts.CreateDispatched(timer, OccurredAt, BookmarkResumeDispatchStatus.Dispatched)!);
        var replay = await f.Store.SaveAsync(HistoryTimer());
        Assert.True(replay.HistoryResponsibility!.Retired); Assert.Equal("TimerRetired", replay.HistoryResponsibility.PendingEvents[1].Name);
        Assert.Null(await f.Store.FindAsync("workflow-a", "timer")); Assert.Null(await f.Store.GetNextDueTimeAsync());
        Assert.Empty(await f.Store.ListDueAsync(OccurredAt.AddDays(2), 10));
        Assert.Empty(await f.Store.ClaimDueAsync(new("pump", OccurredAt.AddDays(2), TimeSpan.FromMinutes(1), 10)));
        Assert.Empty((await f.Store.ListPageAsync(new("workflow-a"))).Items);
        Assert.Equal(2, Assert.Single((await f.Store.ListPendingHistoryAsync(new())).Items).HistoryResponsibility!.PendingEvents.Count);
    }

    [Fact]
    public async Task Timer_history_claim_requires_an_actual_terminal_fact_and_preserves_the_original_schedule()
    {
        await using var f = await TimerHistoryFixture.Create(); var timer = await f.Store.SaveAsync(HistoryTimer());
        var claim = Assert.Single(await f.Store.ClaimDueAsync(new("pump", OccurredAt.AddMinutes(2), TimeSpan.FromMinutes(1), 10)));
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Store.CompleteClaimAsync(claim).AsTask());
        Assert.NotNull(await f.Store.FindAsync("workflow-a", "timer"));
        var result = await f.Store.CompleteClaimWithHistoryAsync(claim,
            DurableTimerHistoryFacts.CreateDispatched(timer, OccurredAt.AddMinutes(2), BookmarkResumeDispatchStatus.Dispatched)!);
        Assert.True(result.Succeeded); Assert.Null(await f.Store.FindAsync("workflow-a", "timer"));
        Assert.Equal(new[] { "TimerScheduled", "TimerDispatched" },
            Assert.Single((await f.Store.ListPendingHistoryAsync(new())).Items).HistoryResponsibility!.PendingEvents.Select(e => e.Name));
    }

    [Theory]
    [InlineData("expired")] [InlineData("terminal")] [InlineData("policy")] [InlineData("disabled")]
    [InlineData("partition")] [InlineData("missing-lease")] [InlineData("no-capture")]
    public async Task Timer_history_admission_refuses_invalid_original_authority_without_a_source(string mode)
    {
        await using var f = await TimerHistoryFixture.Create(mode: mode);
        var timer = mode == "no-capture" ? HistoryTimer() with { HistoryResponsibility = null } : HistoryTimer();
        if (mode == "expired") await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => f.Store.SaveAsync(timer).AsTask());
        else if (mode == "partition") await Assert.ThrowsAnyAsync<InvalidOperationException>(() => f.Store.SaveAsync(timer).AsTask());
        else await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.Store.SaveAsync(timer).AsTask());
        Assert.Empty(await f.Context.DurableTimers.ToArrayAsync());
        Assert.All(await f.Context.ExecutionLivenessStates.ToArrayAsync(), row => Assert.Null(row.TimerHistoryOwnerJson));
    }

    [Fact]
    public async Task Timer_history_acknowledges_only_after_the_exact_committed_observation_and_keeps_live_claim()
    {
        await using var f = await TimerHistoryFixture.Create(); var timer = await f.Store.SaveAsync(HistoryTimer());
        await f.CreateHead();
        var observation = new RuntimeHistoryObservation(TimerHistoryPolicy, timer.HistoryResponsibility!.PendingEvents[0]);
        Assert.False(await f.Store.AcknowledgeHistoryAsync("workflow-a", "timer", observation.Event.HistoryEventId));
        var reader = new EfRuntimeHistoryStore(f.Context, f.Access, new NoopContinuationCodec(), RuntimeHistoryEfModule.Chain);
        var writer = new EfRuntimeHistoryMutationStore(f.Context, f.Access, reader, f.Clock, RuntimeHistoryEfModule.Chain);
        Assert.True((await writer.RecordAsync(observation)).Recorded);
        var claim = Assert.Single(await f.Store.ClaimDueAsync(new("pump", OccurredAt.AddMinutes(2), TimeSpan.FromMinutes(1), 10)));
        Assert.True(await f.Store.AcknowledgeHistoryAsync("workflow-a", "timer", observation.Event.HistoryEventId));
        Assert.False(await f.Store.HasPendingHistoryAsync("workflow-a"));
        Assert.True((await f.Store.RenewClaimAsync(claim, OccurredAt.AddMinutes(2), TimeSpan.FromMinutes(1))).Succeeded);
    }

    [Fact]
    public async Task Timer_history_no_record_discharge_is_bounded_durable_and_keeps_the_first_compact_receipt()
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        for (var i = 0; i < 3; i++) { await f.Store.SaveAsync(HistoryTimer("timer-" + i)); await f.Store.DeleteAsync("workflow-a", "timer-" + i); }
        Assert.True(f.Store.CanDischargeUnrecordedCompletion(f.Writer));
        Assert.False(await f.Store.DischargeUnrecordedCompletionAsync(f.Completion(), 1));
        Assert.Equal(2, (await f.Store.ListPendingHistoryAsync(new())).Items.Count);
        Assert.False(await f.Store.DischargeUnrecordedCompletionAsync(f.Completion(), 1));
        Assert.True(await f.Store.DischargeUnrecordedCompletionAsync(f.Completion(), 1));
        Assert.True(await f.Store.DischargeUnrecordedCompletionAsync(f.Completion() with { Checkpoint = f.Completion().Checkpoint with { CheckpointId = "later" } }, 1));
        await using var restarted = f.Database.Open("tenant-a");
        var store = new EfDurableTimerStore(restarted, f.Access, new NoopContinuationCodec(), f.Clock);
        var replay = await store.SaveAsync(HistoryTimer("timer-0"));
        Assert.Equal("completed", replay.HistoryResponsibility!.NoRecordCompletion!.CheckpointId);
        Assert.Empty(replay.HistoryResponsibility.PendingEvents); Assert.Null(replay.Input); Assert.Null(replay.Metadata);
        Assert.Empty(await restarted.WorkflowExecutionStates.ToArrayAsync()); Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Empty(await restarted.Set<RuntimeHistoryHeadEntity>().ToArrayAsync());
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => store.SaveAsync(HistoryTimer("late")).AsTask());
    }

    [Fact]
    public async Task Timer_history_no_record_refuses_active_sources_before_mutating_the_retired_prefix()
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        await f.Store.SaveAsync(HistoryTimer("retired")); await f.Store.DeleteAsync("workflow-a", "retired");
        await f.Store.SaveAsync(HistoryTimer("active"));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.Store.DischargeUnrecordedCompletionAsync(f.Completion(), 1).AsTask());
        Assert.Equal(2, (await f.Store.ListPendingHistoryAsync(new())).Items.Count);
        var owner = EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(), "tenant-a", "workflow-a");
        Assert.Null(owner!.Completion); Assert.False(owner.LostFacts);
    }

    [Fact]
    public async Task Timer_history_empty_no_record_completion_closes_admission_without_inventing_a_gap()
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        Assert.True(await f.Store.DischargeUnrecordedCompletionAsync(f.Completion()));
        var owner = EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(), "tenant-a", "workflow-a");
        Assert.Equal("completed", owner!.Completion!.CheckpointId); Assert.False(owner.LostFacts);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.Store.SaveAsync(HistoryTimer()).AsTask());
        Assert.Empty(await f.Context.DurableTimers.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timer_history_scope_cleanup_preserves_owed_retirement_in_the_native_transaction(bool rollback)
    {
        await using var f = await TimerHistoryFixture.Create(); await f.Store.SaveAsync(HistoryTimer());
        await using (var transaction = await f.Context.Database.BeginTransactionAsync())
        {
            await EfRuntimeCheckpointActivityScopeCleanupParticipantStaging.StageAsync(f.Context,
                [new("workflow-a", "activity-scope", ["activity"], [], ["timer"], [])], "tenant-a", OccurredAt.AddSeconds(2), CancellationToken.None);
            await f.Context.SaveChangesAsync();
            if (rollback) await transaction.RollbackAsync(); else await transaction.CommitAsync();
        }
        f.Context.ChangeTracker.Clear();
        var source = Assert.Single((await f.Store.ListPendingHistoryAsync(new())).Items);
        Assert.Equal(!rollback, source.HistoryResponsibility!.Retired);
        Assert.Equal(rollback ? 1 : 2, source.HistoryResponsibility.PendingEvents.Count);
        if (rollback) Assert.NotNull(await f.Store.FindAsync("workflow-a", "timer"));
        else { Assert.Null(await f.Store.FindAsync("workflow-a", "timer")); Assert.Equal(OccurredAt.AddSeconds(2), source.HistoryResponsibility.PendingEvents[1].OccurredAt); }
    }

    [Theory]
    [InlineData("native")] [InlineData("stale-fence")] [InlineData("disabled")] [InlineData("policy")]
    [InlineData("partition")] [InlineData("not-completed")] [InlineData("missing-fence")] [InlineData("retain-work")]
    public async Task Timer_history_invalid_no_record_authority_leaves_all_original_facts_owed(string mode)
    {
        await using var f = await TimerHistoryFixture.Create(native: mode == "native");
        await f.Store.SaveAsync(HistoryTimer()); await f.Store.DeleteAsync("workflow-a", "timer");
        var completion = f.Completion(); var workflow = completion.StateChanges.WorkflowExecution!.State;
        if (mode == "disabled") workflow = workflow with { HistoryCaptureEnabledAtAdmission = false };
        if (mode == "policy") workflow = workflow with { HistoryCapturePolicy = new("changed", 1, true, TimerHistoryPolicy.SupportedKinds) };
        if (mode == "partition") workflow = workflow with { Partition = new("other") };
        if (mode == "not-completed") workflow = workflow with { Status = WorkflowExecutionStatus.Faulted };
        completion = completion with { StateChanges = new(new("workflow-a", RuntimeStateChangeOperation.Upsert, workflow,
            new Dictionary<string, string>()), null, [], [], [], [], []) };
        if (mode == "stale-fence") completion = completion with { ExpectedFence = new("foreign", "owner", 1) };
        if (mode == "missing-fence") completion = completion with { ExpectedFence = null };
        if (mode == "retain-work") completion = completion with { RetainSchedulerWork = true };
        var error = await Record.ExceptionAsync(() => f.Store.DischargeUnrecordedCompletionAsync(completion).AsTask());
        if (mode == "stale-fence") Assert.IsType<RuntimeStaleFencingTokenException>(error);
        else if (mode == "policy") Assert.IsType<RuntimeHistoryIntegrityException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        var source = Assert.Single((await f.Store.ListPendingHistoryAsync(new())).Items);
        Assert.Equal(2, source.HistoryResponsibility!.PendingEvents.Count); Assert.Null(source.HistoryResponsibility.NoRecordCompletion);
        Assert.Null(EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(), "tenant-a", "workflow-a")!.Completion);
        Assert.Empty(await f.Context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timer_history_first_native_recovery_retains_the_original_choice_and_only_actual_lost_facts_make_a_gap(bool lostFacts)
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        if (lostFacts) { await f.Store.SaveAsync(HistoryTimer()); await f.Store.DeleteAsync("workflow-a", "timer"); }
        Assert.True(await f.Store.DischargeUnrecordedCompletionAsync(f.Completion()));
        var workflow = f.Completion().StateChanges.WorkflowExecution!.State with { Status = WorkflowExecutionStatus.Faulted, CompletedAt = null };
        var commit = Commit("first-fault") with { ExpectedFence = f.Completion().ExpectedFence,
            StateChanges = new(new("workflow-a", RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()), null, [], [], [], [], []) };
        var cleared = workflow with { HistoryCapturePolicy = null, HistoryCaptureEnabledAtAdmission = false };
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => f.Writer.CommitAsync(commit with
            { StateChanges = new(new("workflow-a", RuntimeStateChangeOperation.Upsert, cleared, new Dictionary<string, string>()), null, [], [], [], [], []) }, Decision()).AsTask());
        Assert.Empty(await f.Context.RuntimeCheckpointCommits.ToArrayAsync());
        await f.Writer.CommitAsync(commit with { HistoryBatches = [new(TimerHistoryPolicy, commit.Checkpoint.CheckpointId, OccurredAt, true, [])] }, Decision());
        var head = new EfRuntimeHistoryProjectionMapping(RuntimeHistoryEfModule.Chain).Read(
            await f.Context.Set<RuntimeHistoryHeadEntity>().AsNoTracking().SingleAsync(), "tenant-a", "workflow-a");
        Assert.Equal(lostFacts, head.HasGaps); Assert.True(head.CapturedFromStart);
    }

    [Fact]
    public async Task Timer_history_rows_adopt_the_protected_format_and_a_predecessor_reader_refuses_them()
    {
        await using var f = await TimerHistoryFixture.Create(); await f.Store.SaveAsync(HistoryTimer());
        var timer = await f.Context.DurableTimers.AsNoTracking().SingleAsync();
        var owner = await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeOperationalStateEfModule.TimerHistorySchemaVersion, timer.SchemaVersion);
        Assert.Equal(RuntimeOperationalStateEfModule.TimerHistorySchemaVersion, owner.SchemaVersion);
        Assert.Contains(RuntimeOperationalStateEfModule.Chain.ContentColumns,
            column => column.Entity == typeof(ExecutionLivenessStateEntity) && column.Name == nameof(owner.TimerHistoryOwnerJson));
        // A bounded predecessor declaration exercises the shared skew contract, not an old-binary process witness.
        var oldReader = EfSchemaChain.For(new("PredecessorTimerReader", "Workflows.Runtime", "1.1.0", typeof(EfDurableTimerStore).Assembly)
            { ContentColumns = [new(typeof(DurableTimerEntity), nameof(timer.ContentJson))] });
        Assert.Throws<EfSchemaVersionSkewException>(() => oldReader.Upcast<DurableTimerEntity>(timer.SchemaVersion, (nameof(timer.ContentJson), timer.ContentJson)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timer_history_authority_cannot_be_mislabeled_as_predecessor_content(bool ownership)
    {
        await using var f = await TimerHistoryFixture.Create(); await f.Store.SaveAsync(HistoryTimer());
        if (ownership)
        {
            await f.Context.ExecutionLivenessStates.ExecuteUpdateAsync(update => update.SetProperty(row => row.SchemaVersion, "1.1.0"));
            await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => f.Store.SaveAsync(HistoryTimer("injected")).AsTask());
        }
        else
        {
            await f.Context.DurableTimers.ExecuteUpdateAsync(update => update.SetProperty(row => row.SchemaVersion, "1.1.0"));
            await Assert.ThrowsAsync<InvalidDataException>(() => f.Store.FindAsync("workflow-a", "timer").AsTask());
        }
        Assert.Single(await f.Context.DurableTimers.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_deletion_prevents_late_uncaptured_timer_admission_even_without_a_prior_timer_owner(bool callerTransaction)
    {
        await using var f = await TimerHistoryFixture.Create();
        var executions = new EfWorkflowExecutionStateStore(f.Context, f.Access, new NoopContinuationCodec());
        Assert.True(await executions.DeleteAsync("workflow-a"));
        Assert.Empty(await f.Context.WorkflowExecutionStates.AsNoTracking().ToArrayAsync());
        Assert.Null((await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).TimerHistoryOwnerJson);
        await using var transaction = callerTransaction ? await f.Context.Database.BeginTransactionAsync() : null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Store.SaveAsync(HistoryTimer("late") with { HistoryResponsibility = null }).AsTask());
        Assert.Empty(await f.Context.DurableTimers.AsNoTracking().ToArrayAsync());
        Assert.Empty((await f.Store.ListPendingHistoryAsync(new())).Items);
        if (transaction is not null) await transaction.RollbackAsync();
    }

    private static DurableTimer HistoryTimer(string id = "timer")
    {
        var timer = new DurableTimer(id, "workflow-a", "Timer", "stimulus", OccurredAt.AddMinutes(1), OccurredAt,
            JsonSerializer.SerializeToElement(new { secret = "original" }), new Dictionary<string, string> { ["secret"] = "original" },
            ActivityExecutionId: "activity", ExecutionScopeId: "activity-scope");
        return timer with { HistoryResponsibility = new(TimerHistoryPolicy, [DurableTimerHistoryFacts.CreateScheduled(timer, TimerHistoryPolicy)!.Event], false) };
    }

    private sealed class TimerHistoryFixture(TestDatabase database, RuntimeSqliteDbContext context, FixedAccessor access,
        FixedTimeProvider clock, RuntimeExecutionLease lease) : IAsyncDisposable
    {
        internal TestDatabase Database => database;
        internal RuntimeSqliteDbContext Context => context;
        internal FixedAccessor Access => access;
        internal FixedTimeProvider Clock => clock;
        internal EfDurableTimerStore Store { get; } = new(context, access, new NoopContinuationCodec(), clock);
        internal EfRuntimeCheckpointCommitStore Writer { get; } = new(context, access, clock,
            rootWriteLeaseManager: new PassThroughRootWriteLeaseManager());
        internal static async Task<TimerHistoryFixture> Create(bool native = true, string? mode = null)
        {
            var db = await TestDatabase.CreateAsync(); var context = db.Open("tenant-a"); var access = new FixedAccessor("tenant-a");
            var clock = new FixedTimeProvider(OccurredAt); var lease = new RuntimeExecutionLease("lease", "workflow-a", "owner", mode == "expired" ? OccurredAt.AddHours(-1) : OccurredAt,
                mode == "expired" ? OccurredAt : OccurredAt.AddHours(1), 1);
            if (mode != "missing-lease") await new EfExecutionLivenessStateStore(context, access, new NoopContinuationCodec())
                .SaveAsync(new(RuntimeExecutionOwnershipStateId.For("workflow-a"), "workflow-a", lease, null, null, null));
            if (native)
            {
                var state = Execution("workflow-a", "tenant-a") with
                {
                    HistoryCapturePolicy = mode == "policy" ? new("other", 1, true, TimerHistoryPolicy.SupportedKinds) : TimerHistoryPolicy,
                    HistoryCaptureEnabledAtAdmission = mode != "disabled",
                    Status = mode == "terminal" ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running,
                    Partition = mode == "partition" ? new("other-scope") : null
                };
                context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", "workflow-a"), 1));
                await context.SaveChangesAsync(); context.ChangeTracker.Clear();
            }
            return new(db, context, access, clock, lease);
        }
        internal async Task CreateHead()
        {
            var commit = Commit("initial") with { ExpectedFence = lease.ToFence() };
            await Writer.CommitAsync(commit with { HistoryBatches = [new(TimerHistoryPolicy, commit.Checkpoint.CheckpointId, OccurredAt, true, [])] }, Decision());
        }
        internal RuntimeCheckpointCommit Completion()
        {
            var state = new WorkflowExecutionState("workflow-a", new("artifact", "definition", "version", "1", "hash"),
                WorkflowExecutionStatus.Completed, null, OccurredAt, OccurredAt, OccurredAt, OccurredAt, null, null, "tenant-a",
                new Dictionary<string, string> { [RuntimeMetadataKeys.PersistenceMode] = nameof(PersistenceMode.Ephemeral) })
                { HistoryCapturePolicy = TimerHistoryPolicy, HistoryCaptureEnabledAtAdmission = true };
            return new("completion", new("completed", RuntimeCheckpointNames.WorkflowCompleted, "workflow-a", OccurredAt, [], new Dictionary<string, string>()),
                new(new("workflow-a", RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>()), null, [], [], [], [], []),
                [], new Dictionary<string, string>()) { ExpectedFence = lease.ToFence() };
        }
        public async ValueTask DisposeAsync() { await context.DisposeAsync(); await database.DisposeAsync(); }
    }
}

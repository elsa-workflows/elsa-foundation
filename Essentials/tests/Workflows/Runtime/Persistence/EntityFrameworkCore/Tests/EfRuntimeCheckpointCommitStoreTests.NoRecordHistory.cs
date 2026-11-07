using System.Text.Json;
using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task No_record_discharge_is_bounded_fenced_and_preserves_compact_first_wins_source_receipts()
    {
        await using var db = await TestDatabase.CreateAsync(); await using var context = db.Open("tenant-a");
        var lease = await SeedNoRecordOwnerAsync(context);
        var store = NoRecordTimerStore(context);
        for (var index = 0; index < 3; index++) await SaveRetiredNoRecordTimerAsync(store, "no-record-" + index);
        var completion = NoRecordCompletion(lease);
        Assert.False(await store.DischargeUnrecordedCompletionAsync(completion, 1));
        Assert.True(await store.HasPendingHistoryAsync("workflow-a"));
        Assert.False(await store.DischargeUnrecordedCompletionAsync(completion, 1));
        Assert.True(await store.DischargeUnrecordedCompletionAsync(completion, 1));
        Assert.False(await store.HasPendingHistoryAsync("workflow-a"));
        Assert.Empty(await context.WorkflowExecutionStates.ToArrayAsync()); Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync()); Assert.Empty(await context.RuntimeHistoryHeads.ToArrayAsync());
        var rows = await context.DurableTimers.AsNoTracking().ToArrayAsync(); Assert.Equal(3, rows.Length);
        foreach (var row in rows)
        {
            var timer = EfDurableTimerStore.ReadChecked(row, "tenant-a", "workflow-a");
            Assert.True(row.HistoryRetired); Assert.False(row.HistoryPending); Assert.Empty(timer.HistoryResponsibility!.PendingEvents);
            Assert.Equal(completion.Checkpoint.CheckpointId, timer.HistoryResponsibility.NoRecordCompletion!.CheckpointId);
            Assert.Equal(completion.StateChanges.WorkflowExecution!.State.CompletedAt, timer.HistoryResponsibility.NoRecordCompletion.CompletedAt);
            Assert.Null(timer.Input); Assert.Null(timer.Metadata); Assert.Null(timer.PayloadType); Assert.Null(timer.ProviderId);
        }
        Assert.True(await store.DischargeUnrecordedCompletionAsync(completion with { Checkpoint = completion.Checkpoint with { CheckpointId = "later-retry" } }, 1));
        var winner = await store.SaveAsync(HistoryTimer("no-record-0") with { CreatedAt = OccurredAt.AddDays(1) });
        Assert.True(winner.HistoryResponsibility!.Retired); Assert.Equal(completion.Checkpoint.CheckpointId, winner.HistoryResponsibility.NoRecordCompletion!.CheckpointId);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => store.SaveAsync(HistoryTimer("new-after-completion")).AsTask());
        Assert.Empty(await store.ListDueAsync(OccurredAt.AddDays(2), 100));
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task No_record_active_source_preflight_preserves_every_retired_responsibility()
    {
        await using var db = await TestDatabase.CreateAsync(); await using var context = db.Open("tenant-a");
        var lease = await SeedNoRecordOwnerAsync(context); var store = NoRecordTimerStore(context);
        await SaveRetiredNoRecordTimerAsync(store, "retired-first"); await store.SaveAsync(HistoryTimer("active-last"));
        var beforeFence = (await context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).Revision;
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => store.DischargeUnrecordedCompletionAsync(NoRecordCompletion(lease), 1).AsTask());
        Assert.Equal(beforeFence, (await context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).Revision);
        var pending = await store.ListPendingHistoryAsync(new()); Assert.Equal(2, pending.Items.Count);
        Assert.All(pending.Items, timer => Assert.Null(timer.HistoryResponsibility!.NoRecordCompletion));
        Assert.All(await context.DurableTimers.AsNoTracking().ToArrayAsync(), row => Assert.True(row.HistoryPending));
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task No_record_discharge_rejects_stale_foreign_and_recorded_owners_without_erasing_source()
    {
        await using var db = await TestDatabase.CreateAsync(); await using var context = db.Open("tenant-a");
        var lease = await SeedNoRecordOwnerAsync(context); var store = NoRecordTimerStore(context);
        await SaveRetiredNoRecordTimerAsync(store, "held-source"); var completion = NoRecordCompletion(lease);
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.DischargeUnrecordedCompletionAsync(completion with { ExpectedFence = new("other", "other", lease.FencingToken) }).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => NoRecordTimerStore(context, "tenant-b").DischargeUnrecordedCompletionAsync(completion).AsTask());
        await RecordTimerHistoryOwnerAsync(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DischargeUnrecordedCompletionAsync(completion).AsTask());
        Assert.True(await store.HasPendingHistoryAsync("workflow-a"));
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task First_fault_history_after_unrecorded_completion_reports_gap_before_any_timer_reexecution()
    {
        await using var db = await TestDatabase.CreateAsync(); await using var context = db.Open("tenant-a");
        var lease = await SeedNoRecordOwnerAsync(context); var timers = NoRecordTimerStore(context);
        await SaveRetiredNoRecordTimerAsync(timers, "discarded-before-fault");
        Assert.True(await timers.DischargeUnrecordedCompletionAsync(NoRecordCompletion(lease)));
        var workflow = Execution("workflow-a", "tenant-a") with { Status = WorkflowExecutionStatus.Faulted, HistoryCapturePolicy = TimerHistoryPolicy, HistoryCaptureEnabledAtAdmission = true };
        var checkpoint = new RuntimeCheckpoint("initial-fault", "WorkflowFaulted", "workflow-a", OccurredAt, [], new Dictionary<string, string>()) { HistoryStartsAtAdmission = true };
        var e = new RuntimeHistoryEvent("initial-fault-event", "workflow-a", 0, RuntimeHistoryEventCategory.WorkflowLifecycle, "WorkflowFaulted", OccurredAt, checkpointId: checkpoint.CheckpointId) { Authority = RuntimeHistoryAuthority.CommittedTransition };
        var changes = new RuntimeCheckpointStateChangeSet(new("workflow-a", RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()), null, [], [], [], [], []);
        var commit = new RuntimeCheckpointCommit("fault-after-discard", checkpoint, changes, [], new Dictionary<string, string>())
        { ExpectedFence = lease.ToFence(), HistoryBatches = [new(TimerHistoryPolicy, checkpoint.CheckpointId, OccurredAt, true, [new(e, 0)])] };
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager()).CommitAsync(commit, Decision());
        var page = await HistoryStore(context, new FixedAccessor("tenant-a")).ReadPageAsync(new("workflow-a"));
        Assert.True(page.Head!.HasGaps); Assert.True(page.Head.CapturedFromStart); Assert.Equal("WorkflowFaulted", Assert.Single(page.Items).Name);
        Assert.NotNull(EfDurableTimerStore.ReadChecked(await context.DurableTimers.AsNoTracking().SingleAsync(), "tenant-a").HistoryResponsibility!.NoRecordCompletion);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Timer_ack_requires_the_exact_recorded_semantic_receipt_and_retains_purged_receipt_proof()
    {
        await using var db = await TestDatabase.CreateAsync(); await using var context = db.Open("tenant-a");
        await SeedNoRecordOwnerAsync(context);
        var timers = NoRecordTimerStore(context); var timer = await timers.SaveAsync(HistoryTimer("ack-proof"));
        var scheduled = Assert.Single(timer.HistoryResponsibility!.PendingEvents);
        Assert.False(await timers.AcknowledgeHistoryAsync("workflow-a", timer.TimerId, scheduled.HistoryEventId));
        await RecordTimerHistoryOwnerAsync(context);
        Assert.False(await timers.AcknowledgeHistoryAsync("workflow-a", timer.TimerId, scheduled.HistoryEventId));
        var journal = HistoryStore(context, new FixedAccessor("tenant-a")); Assert.True((await journal.RecordAsync(new(TimerHistoryPolicy, scheduled))).Recorded);
        var receipt = await context.RuntimeHistoryEvents.SingleAsync(x => x.HistoryEventId == EfRuntimeOperationalStoreSupport.Encode(scheduled.HistoryEventId));
        var observationFingerprint = receipt.CandidateFingerprint;
        receipt.CandidateFingerprint = RuntimeHistoryIdentity.Compute(new RuntimeHistoryCandidate(scheduled, 0));
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => timers.AcknowledgeHistoryAsync("workflow-a", timer.TimerId, scheduled.HistoryEventId).AsTask());
        Assert.True(await timers.HasPendingHistoryAsync("workflow-a"));
        receipt = await context.RuntimeHistoryEvents.SingleAsync(x => x.HistoryEventId == EfRuntimeOperationalStoreSupport.Encode(scheduled.HistoryEventId));
        receipt.CandidateFingerprint = observationFingerprint; await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        await journal.PurgeThroughAsync("workflow-a", 100);
        Assert.True(await timers.AcknowledgeHistoryAsync("workflow-a", timer.TimerId, scheduled.HistoryEventId));
        Assert.False(await timers.HasPendingHistoryAsync("workflow-a"));
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task New_active_timer_cannot_omit_its_schedule_responsibility_before_native_history_exists()
    {
        await using var db = await TestDatabase.CreateAsync(); await using var context = db.Open("tenant-a");
        await SeedNoRecordOwnerAsync(context);
        var empty = HistoryTimer("missing-schedule") with { HistoryResponsibility = new(TimerHistoryPolicy, [], false) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => NoRecordTimerStore(context).SaveAsync(empty).AsTask());
        Assert.Empty(await context.DurableTimers.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task No_record_discharge_rolls_back_source_and_fence_when_later_source_write_fails()
    {
        await using var db = await TestDatabase.CreateAsync(); var failure = new NoRecordWriteFailure();
        await using var context = db.Open("tenant-a", failure);
        var lease = await SeedNoRecordOwnerAsync(context); var store = NoRecordTimerStore(context);
        await SaveRetiredNoRecordTimerAsync(store, "failed-source");
        var before = (await context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).Revision;
        failure.Arm();
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => store.DischargeUnrecordedCompletionAsync(NoRecordCompletion(lease)).AsTask());
        Assert.Equal(before, (await context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).Revision);
        var source = Assert.Single((await store.ListPendingHistoryAsync(new())).Items);
        Assert.Equal(2, source.HistoryResponsibility!.PendingEvents.Count); Assert.Null(source.HistoryResponsibility.NoRecordCompletion);
        Assert.True(await store.DischargeUnrecordedCompletionAsync(NoRecordCompletion(lease)));
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task No_record_discharge_reconciles_lost_commit_acknowledgement_from_compact_first_receipt()
    {
        await using var db = await TestDatabase.CreateAsync(); var failure = new NoRecordAcknowledgementLoss();
        await using var context = db.Open("tenant-a", failure);
        var lease = await SeedNoRecordOwnerAsync(context); var store = NoRecordTimerStore(context);
        await SaveRetiredNoRecordTimerAsync(store, "ack-lost-source");
        var completion = NoRecordCompletion(lease); failure.Arm();
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => store.DischargeUnrecordedCompletionAsync(completion).AsTask());
        Assert.False(await store.HasPendingHistoryAsync("workflow-a"));
        Assert.True(await store.DischargeUnrecordedCompletionAsync(completion with { Checkpoint = completion.Checkpoint with { CheckpointId = "later-physical-retry" } }));
        var timer = EfDurableTimerStore.ReadChecked(await context.DurableTimers.AsNoTracking().SingleAsync(), "tenant-a");
        Assert.Equal(completion.Checkpoint.CheckpointId, timer.HistoryResponsibility!.NoRecordCompletion!.CheckpointId);
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync()); Assert.Empty(await context.WorkflowExecutionStates.ToArrayAsync());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task New_captured_source_refuses_foreign_pending_changes_and_transactions_without_flushing_them()
    {
        await using var db = await TestDatabase.CreateAsync(); await using var context = db.Open("tenant-a");
        await SeedNoRecordOwnerAsync(context); var store = NoRecordTimerStore(context);
        var original = await store.SaveAsync(HistoryTimer("existing-owner-source"));
        var pending = context.SchedulerStates.Add(SchedulerRow("tenant-a", "foreign-pending"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(HistoryTimer("must-not-flush")).AsTask());
        Assert.Equal(EntityState.Added, pending.State); Assert.Empty(await context.SchedulerStates.AsNoTracking().ToArrayAsync());
        pending.State = EntityState.Detached;
        await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(HistoryTimer("must-not-join")).AsTask());
        Assert.Equal(original.TimerId, (await store.SaveAsync(HistoryTimer("existing-owner-source"))).TimerId);
        Assert.Single(await context.DurableTimers.AsNoTracking().ToArrayAsync());
        await transaction.RollbackAsync();
    }

    private static EfDurableTimerStore NoRecordTimerStore(RuntimeDbContext context, string scope = "tenant-a") => new(context, new FixedAccessor(scope),
        new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "no-record-history-source-signing-key-32" })), new FixedTimeProvider(OccurredAt));
    private static async Task SaveRetiredNoRecordTimerAsync(EfDurableTimerStore store, string id)
    {
        var timer = HistoryTimer(id) with { Input = JsonDocument.Parse("{\"private\":true}").RootElement.Clone(), Metadata = new Dictionary<string, string> { ["private"] = "value" }, ProviderId = "private-provider" };
        timer = await store.SaveAsync(timer);
        await store.RetireWithHistoryAsync("workflow-a", id, DurableTimerHistoryFacts.CreateCancelled(timer, OccurredAt)!);
    }
    private static async Task<RuntimeExecutionLease> SeedNoRecordOwnerAsync(RuntimeDbContext context)
    {
        var lease = new RuntimeExecutionLease("no-record-lease", "workflow-a", "owner", OccurredAt.AddMinutes(-1), OccurredAt.AddHours(1), 1);
        var state = new ExecutionLivenessState(RuntimeExecutionOwnershipStateId.For("workflow-a"), "workflow-a", lease, null, null, null);
        context.ExecutionLivenessStates.Add(EfExecutionLivenessStateStore.ToEntity(state, "tenant-a", 1));
        await context.SaveChangesAsync(); context.ChangeTracker.Clear(); return lease;
    }
    private static RuntimeCheckpointCommit NoRecordCompletion(RuntimeExecutionLease lease)
    {
        var state = Execution("workflow-a", "tenant-a") with { Status = WorkflowExecutionStatus.Completed, CompletedAt = OccurredAt.AddMinutes(1), HistoryCapturePolicy = TimerHistoryPolicy,
            HistoryCaptureEnabledAtAdmission = true, SystemMetadata = new Dictionary<string, string> { [RuntimeMetadataKeys.PersistenceMode] = nameof(PersistenceMode.Ephemeral) } };
        var checkpoint = new RuntimeCheckpoint("actual-unrecorded-completion", "WorkflowCompleted", "workflow-a", state.CompletedAt.Value, [], new Dictionary<string, string>());
        return new("unrecorded-physical", checkpoint, new(new("workflow-a", RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>()), null, [], [], [], [], []), [], new Dictionary<string, string>()) { ExpectedFence = lease.ToFence() };
    }

    private sealed class NoRecordWriteFailure : DbCommandInterceptor
    {
        private bool armed;
        public void Arm() => armed = true;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (armed && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("elsa_runtime_durable_timer", StringComparison.Ordinal))
            { armed = false; throw new IOException("Simulated retired source write failure."); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class NoRecordAcknowledgementLoss : DbTransactionInterceptor
    {
        private bool armed;
        public void Arm() => armed = true;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (armed) { armed = false; throw new IOException("Simulated no-record completion acknowledgement loss."); }
            return Task.CompletedTask;
        }
    }
}

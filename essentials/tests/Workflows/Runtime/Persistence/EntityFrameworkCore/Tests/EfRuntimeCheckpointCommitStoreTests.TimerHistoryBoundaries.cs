using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData("json")] [InlineData("cause")] [InlineData("physical-owner")] [InlineData("purged")]
    public async Task Timer_history_acknowledgement_checks_retained_content_and_preserves_legitimate_purged_receipts(string mode)
    {
        await using var f = await TimerHistoryFixture.Create(); var timer = await f.Store.SaveAsync(HistoryTimer()); await f.CreateHead();
        var observation = new RuntimeHistoryObservation(TimerHistoryPolicy, timer.HistoryResponsibility!.PendingEvents[0]);
        var reader = new EfRuntimeHistoryStore(f.Context, f.Access, new NoopContinuationCodec(), RuntimeHistoryEfModule.Chain);
        var writer = new EfRuntimeHistoryMutationStore(f.Context, f.Access, reader, f.Clock, RuntimeHistoryEfModule.Chain);
        Assert.True((await writer.RecordAsync(observation)).Recorded);
        if (mode == "purged")
        {
            Assert.Equal(1, await writer.PurgeThroughAsync("workflow-a", long.MaxValue));
            Assert.True(await f.Store.AcknowledgeHistoryAsync("workflow-a", "timer", observation.Event.HistoryEventId));
            Assert.False(await f.Store.HasPendingHistoryAsync("workflow-a")); return;
        }
        var row = await f.Context.Set<RuntimeHistoryEventEntity>().AsNoTracking().SingleAsync();
        var recorded = RuntimeArtifactJson.Deserialize<RuntimeHistoryEvent>(row.ContentJson);
        var changed = new RuntimeHistoryEvent(recorded.HistoryEventId, recorded.WorkflowExecutionId, recorded.Sequence, recorded.Category,
            recorded.Name, recorded.OccurredAt, recorded.ActivityExecutionId, recorded.ExecutableNodeId, recorded.CheckpointId,
            recorded.IncidentId, recorded.Payload, recorded.Metadata)
        {
            OccurredAtKnown = recorded.OccurredAtKnown, CheckpointOrdinal = recorded.CheckpointOrdinal, SchemaVersion = recorded.SchemaVersion,
            Authority = recorded.Authority, BookmarkId = recorded.BookmarkId, AttemptId = recorded.AttemptId,
            ParentActivityExecutionId = recorded.ParentActivityExecutionId, ActivityAncestryKnown = recorded.ActivityAncestryKnown,
            ExecutionScopeId = recorded.ExecutionScopeId, IterationId = recorded.IterationId, RecordedAt = recorded.RecordedAt,
            Causation = mode == "cause" ? new("foreign-event", "foreign-run", "foreign-delivery") : recorded.Causation,
            PhysicalCommitId = mode == "physical-owner" ? "foreign-owner" : recorded.PhysicalCommitId
        };
        var content = mode == "json" ? "{invalid" : RuntimeArtifactJson.Serialize(changed);
        await f.Context.Set<RuntimeHistoryEventEntity>().ExecuteUpdateAsync(update => update.SetProperty(item => item.ContentJson, content));
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => f.Store.AcknowledgeHistoryAsync("workflow-a", "timer", observation.Event.HistoryEventId).AsTask());
        Assert.True(await f.Store.HasPendingHistoryAsync("workflow-a"));
        Assert.Single(Assert.Single((await f.Store.ListPendingHistoryAsync(new())).Items).HistoryResponsibility!.PendingEvents);
    }

    [Fact]
    public async Task Timer_history_capability_refuses_a_model_that_ignores_owner_receipt_updates()
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        await using var context = new IgnoredTimerOwnerContext(new DbContextOptionsBuilder<IgnoredTimerOwnerContext>().UseSqlite(f.Database.Connection).Options);
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock);
        Assert.False(store.SupportsHistoryResponsibility);
        await Assert.ThrowsAsync<NotSupportedException>(() => store.SaveAsync(HistoryTimer()).AsTask());
        Assert.Empty(await f.Context.DurableTimers.AsNoTracking().ToArrayAsync());
        Assert.Null((await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).TimerHistoryOwnerJson);
    }

    [Theory]
    [InlineData("pending")] [InlineData("list")] [InlineData("ack")]
    [InlineData("retire")] [InlineData("claim")] [InlineData("no-record")] [InlineData("schedule")] [InlineData("native-probe")]
    public async Task Timer_history_public_ports_preserve_provider_failure_causes_without_claiming_an_acknowledgement(string operation)
    {
        await using var f = await TimerHistoryFixture.Create(native: false); var timer = await f.Store.SaveAsync(HistoryTimer());
        var claim = Assert.Single(await f.Store.ClaimDueAsync(new("pump", OccurredAt.AddMinutes(2), TimeSpan.FromMinutes(1), 1)));
        var fault = new TimerHistoryFault(); await using var context = f.Database.Open("tenant-a", fault);
        var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock); fault.Armed = true;
        async Task Invoke()
        {
            switch (operation)
            {
                case "pending": await store.HasPendingHistoryAsync("workflow-a"); break;
                case "list": await store.ListPendingHistoryAsync(new()); break;
                case "ack": await store.AcknowledgeHistoryAsync("workflow-a", "timer", timer.HistoryResponsibility!.PendingEvents[0].HistoryEventId); break;
                case "retire": await store.RetireWithHistoryAsync("workflow-a", "timer", DurableTimerHistoryFacts.CreateCancelled(timer, OccurredAt)!); break;
                case "claim": await store.CompleteClaimWithHistoryAsync(claim, DurableTimerHistoryFacts.CreateDispatched(timer, OccurredAt, BookmarkResumeDispatchStatus.Dispatched)!); break;
                case "no-record": await store.DischargeUnrecordedCompletionAsync(f.Completion()); break;
                case "native-probe": await ((IRuntimeCheckpointCommitStore)new EfRuntimeCheckpointCommitStore(context, f.Access, f.Clock))
                    .RequiresUnrecordedTimerDischargeAsync("workflow-a"); break;
                default: await store.SaveAsync(HistoryTimer("next")); break;
            }
        }
        var error = await Record.ExceptionAsync(Invoke);
        Exception? contextual = error;
        if (operation != "pending") contextual = Assert.IsType<RuntimeSchedulerWorkUnavailableException>(error).InnerException;
        if (operation == "list") Assert.Same(fault.Failure, contextual);
        else
        {
            var unavailable = Assert.IsType<RuntimeHistoryUnavailableException>(contextual);
            Assert.Equal("workflow-a", unavailable.WorkflowExecutionId); Assert.Same(fault.Failure, unavailable.InnerException);
        }
        var source = Assert.Single((await f.Store.ListPendingHistoryAsync(new())).Items);
        Assert.False(source.HistoryResponsibility!.Retired); Assert.Single(source.HistoryResponsibility.PendingEvents);
        Assert.Null(context.Database.CurrentTransaction); Assert.False(context.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("ack")] [InlineData("retire")] [InlineData("claim")] [InlineData("delete")]
    public async Task Timer_history_mutations_refuse_a_scope_change_while_the_actual_provider_read_is_pending(string operation)
    {
        await using var f = await TimerHistoryFixture.Create(); var timer = await f.Store.SaveAsync(HistoryTimer()); await f.CreateHead();
        var observation = new RuntimeHistoryObservation(TimerHistoryPolicy, timer.HistoryResponsibility!.PendingEvents[0]);
        var reader = new EfRuntimeHistoryStore(f.Context, f.Access, new NoopContinuationCodec(), RuntimeHistoryEfModule.Chain);
        await new EfRuntimeHistoryMutationStore(f.Context, f.Access, reader, f.Clock, RuntimeHistoryEfModule.Chain).RecordAsync(observation);
        var claim = Assert.Single(await f.Store.ClaimDueAsync(new("pump", OccurredAt.AddMinutes(2), TimeSpan.FromMinutes(1), 1)));
        var access = new TimerHistoryAccess(); var held = new HeldTimerHistoryRead(operation == "ack" ? "elsa_runtime_history_event" : "elsa_runtime_durable_timer");
        await using var context = f.Database.Open("tenant-a", held);
        var store = new EfDurableTimerStore(context, access, new NoopContinuationCodec(), f.Clock);
        async Task Invoke()
        {
            if (operation == "ack") await store.AcknowledgeHistoryAsync("workflow-a", "timer", observation.Event.HistoryEventId);
            else if (operation == "retire") await store.RetireWithHistoryAsync("workflow-a", "timer", DurableTimerHistoryFacts.CreateCancelled(timer, OccurredAt)!);
            else if (operation == "delete") await store.DeleteAsync("workflow-a", "timer");
            else await store.CompleteClaimWithHistoryAsync(claim, DurableTimerHistoryFacts.CreateDispatched(timer, OccurredAt, BookmarkResumeDispatchStatus.Dispatched)!);
        }
        var pending = Invoke();
        try { await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30)); access.Current = PersistenceAccessContext.Scoped(new("tenant-b")); }
        finally { held.Proceed.TrySetResult(); }
        var failure = await Record.ExceptionAsync(() => pending);
        var source = Assert.Single((await f.Store.ListPendingHistoryAsync(new())).Items);
        Assert.False(source.HistoryResponsibility!.Retired); Assert.Single(source.HistoryResponsibility.PendingEvents);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.True((await f.Store.RenewClaimAsync(claim, OccurredAt.AddMinutes(2), TimeSpan.FromMinutes(1))).Succeeded);
        Assert.Null(context.Database.CurrentTransaction); Assert.False(context.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Timer_history_preserves_caller_transactions_for_a_proven_uncaptured_native_admission()
    {
        await using var f = await TimerHistoryFixture.Create();
        var row = await f.Context.WorkflowExecutionStates.AsNoTracking().SingleAsync();
        var original = RuntimeArtifactJson.Deserialize<WorkflowExecutionState>(row.ContentJson) with
            { HistoryCapturePolicy = null, HistoryCaptureEnabledAtAdmission = false };
        var content = RuntimeArtifactJson.Serialize(original);
        await f.Context.WorkflowExecutionStates.ExecuteUpdateAsync(update => update.SetProperty(item => item.ContentJson, content));
        await using (var caller = f.Database.Open("tenant-a"))
        {
            await using var transaction = await caller.Database.BeginTransactionAsync();
            var store = new EfDurableTimerStore(caller, f.Access, new NoopContinuationCodec(), f.Clock);
            await store.SaveAsync(HistoryTimer() with { HistoryResponsibility = null });
            await transaction.CommitAsync();
        }
        Assert.NotNull(await f.Store.FindAsync("workflow-a", "timer")); Assert.False(await f.Store.HasPendingHistoryAsync("workflow-a"));
        Assert.Null((await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).TimerHistoryOwnerJson);
    }

    private sealed class IgnoredTimerOwnerContext(DbContextOptions<IgnoredTimerOwnerContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder model) => model.Entity<ExecutionLivenessStateEntity>()
            .Property(row => row.TimerHistoryOwnerJson).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
    private sealed class TimerHistoryAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new("tenant-a")); }
    private sealed class TimerHistoryFault : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal Exception Failure { get; init; } = new IOException("controlled timer source provider failure");
        internal Action? BeforeThrow { get; init; }
        private void Check() { if (Armed) { Armed = false; BeforeThrow?.Invoke(); throw Failure; } }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default) { Check(); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default) { Check(); return ValueTask.FromResult(result); }
    }
    private sealed class HeldTimerHistoryRead(string table) : DbCommandInterceptor
    {
        private int armed = 1;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Proceed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken token = default)
        {
            if (command.CommandText.Contains(table, StringComparison.Ordinal) && Interlocked.Exchange(ref armed, 0) == 1)
            { Entered.TrySetResult(); await Proceed.Task.WaitAsync(token); }
            return result;
        }
    }
}

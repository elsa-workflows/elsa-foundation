using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Fact]
    public async Task Timer_history_native_probe_keeps_malformed_liveness_in_domain_integrity_handling()
    {
        await using var f = await TimerHistoryFixture.Create(native: false); await f.Store.SaveAsync(HistoryTimer());
        await f.Context.ExecutionLivenessStates.ExecuteUpdateAsync(update => update.SetProperty(row => row.ContentJson, "{invalid"));
        var failure = await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() =>
            ((IRuntimeCheckpointCommitStore)f.Writer).RequiresUnrecordedTimerDischargeAsync("workflow-a").AsTask());
        Assert.Contains("workflow-a", failure.Message); Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.True(await f.Store.HasPendingHistoryAsync("workflow-a"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timer_history_native_discard_preserves_the_actual_queue_when_owner_mapping_is_unavailable(bool legacy)
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        if (!legacy) await f.Store.SaveAsync(HistoryTimer());
        await using var context = new UnmappedTimerOwnerContext(new DbContextOptionsBuilder<UnmappedTimerOwnerContext>().UseSqlite(f.Database.Connection).Options);
        IRuntimeCheckpointCommitStore native = legacy ? f.Writer : new EfRuntimeCheckpointCommitStore(context, f.Access, f.Clock);
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(new("start", "workflow-a", "command", WorkflowExecutionCommandKind.Start, "envelope", "key", OccurredAt, OccurredAt));
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = new RuntimeCoalescingSession("workflow-a", queue, new() { RecordCompletion = false }, mode: new(PersistenceMode.Ephemeral, false));
        await session.ConsumePumpedOverlayItemAsync("start", CancellationToken.None); using var scope = accessor.Push(session);
        var writer = new CoalescingRuntimeCheckpointCommitStore(new(native), accessor, null, [], f.Store);
        var completed = f.Completion(); var cleared = completed.StateChanges.WorkflowExecution!.State with
            { HistoryCapturePolicy = null, HistoryCaptureEnabledAtAdmission = false };
        completed = completed with { StateChanges = new(new("workflow-a", RuntimeStateChangeOperation.Upsert, cleared,
            new Dictionary<string, string>()), null, [], [], [], [], []) };
        if (legacy)
        {
            await writer.CommitAsync(completed, Decision()); Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("workflow-a")));
            Assert.Null((await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).TimerHistoryOwnerJson);
        }
        else
        {
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => writer.CommitAsync(completed, Decision()).AsTask());
            Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("workflow-a")));
            Assert.True(await f.Store.HasPendingHistoryAsync("workflow-a"));
            Assert.Null(EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(), "tenant-a", "workflow-a")!.Completion);
        }
        Assert.Empty(await f.Context.RuntimeCheckpointCommits.ToArrayAsync()); Assert.Empty(await f.Context.WorkflowExecutionStates.ToArrayAsync());
    }

    [Fact]
    public async Task Timer_history_first_native_cannot_erase_a_source_choice_through_an_unmapped_owner_column()
    {
        await using var f = await TimerHistoryFixture.Create(native: false); await f.Store.SaveAsync(HistoryTimer());
        await using var context = new UnmappedTimerOwnerContext(new DbContextOptionsBuilder<UnmappedTimerOwnerContext>().UseSqlite(f.Database.Connection).Options);
        var store = new EfRuntimeCheckpointCommitStore(context, f.Access, f.Clock, rootWriteLeaseManager: new PassThroughRootWriteLeaseManager());
        var workflow = f.Completion().StateChanges.WorkflowExecution!.State with
            { Status = WorkflowExecutionStatus.Faulted, HistoryCapturePolicy = null, HistoryCaptureEnabledAtAdmission = false };
        var commit = Commit("blind-first-native") with { ExpectedFence = f.Completion().ExpectedFence,
            StateChanges = new(new("workflow-a", RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()), null, [], [], [], [], []) };
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => store.CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(await f.Context.RuntimeCheckpointCommits.ToArrayAsync()); Assert.Empty(await f.Context.WorkflowExecutionStates.ToArrayAsync());
        Assert.True(await f.Store.HasPendingHistoryAsync("workflow-a"));
    }

    [Theory]
    [InlineData("pending", false)] [InlineData("pending", true)] [InlineData("native-probe", false)] [InlineData("native-probe", true)]
    [InlineData("schedule", false)] [InlineData("schedule", true)] [InlineData("no-record", false)] [InlineData("no-record", true)]
    public async Task Timer_history_request_cancellation_takes_precedence_over_independent_provider_cancellation(string operation, bool task)
    {
        await TimerCancellationControl(operation, task, requested: true);
    }

    [Theory]
    [InlineData("pending", false)] [InlineData("pending", true)] [InlineData("native-probe", false)] [InlineData("native-probe", true)]
    [InlineData("schedule", false)] [InlineData("schedule", true)] [InlineData("no-record", false)] [InlineData("no-record", true)]
    public async Task Timer_history_unrequested_provider_cancellation_remains_contextual_availability(string operation, bool task)
    {
        await TimerCancellationControl(operation, task, requested: false);
    }

    private static async Task TimerCancellationControl(string operation, bool task, bool requested)
    {
        await using var f = await TimerHistoryFixture.Create(native: false); await f.Store.SaveAsync(HistoryTimer());
        using var cancellation = new CancellationTokenSource();
        var fault = new TimerHistoryFault { Failure = task ? new TaskCanceledException("provider timeout") : new OperationCanceledException("provider timeout"),
            BeforeThrow = requested ? cancellation.Cancel : null };
        await using var context = f.Database.Open("tenant-a", fault); var store = new EfDurableTimerStore(context, f.Access, new NoopContinuationCodec(), f.Clock); fault.Armed = true;
        async Task Invoke()
        {
            if (operation == "pending") await store.HasPendingHistoryAsync("workflow-a", cancellation.Token);
            else if (operation == "native-probe") await ((IRuntimeCheckpointCommitStore)new EfRuntimeCheckpointCommitStore(context, f.Access, f.Clock))
                .RequiresUnrecordedTimerDischargeAsync("workflow-a", cancellation.Token);
            else if (operation == "schedule") await store.SaveAsync(HistoryTimer("next"), cancellation.Token);
            else await store.DischargeUnrecordedCompletionAsync(f.Completion(), cancellationToken: cancellation.Token);
        }
        if (requested)
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(Invoke); Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        else
        {
            var error = await Record.ExceptionAsync(Invoke);
            var unavailable = operation == "pending" ? Assert.IsType<RuntimeHistoryUnavailableException>(error) :
                Assert.IsType<RuntimeHistoryUnavailableException>(Assert.IsType<RuntimeSchedulerWorkUnavailableException>(error).InnerException);
            Assert.Same(fault.Failure, unavailable.InnerException);
        }
        Assert.True(await f.Store.HasPendingHistoryAsync("workflow-a")); Assert.Null(context.Database.CurrentTransaction); Assert.False(context.ChangeTracker.HasChanges());
    }

    private sealed class UnmappedTimerOwnerContext(DbContextOptions<UnmappedTimerOwnerContext> options) : RuntimeDbContext(options)
    { protected override void ConfigureProvider(ModelBuilder model) => model.Entity<ExecutionLivenessStateEntity>().Ignore(row => row.TimerHistoryOwnerJson); }
}

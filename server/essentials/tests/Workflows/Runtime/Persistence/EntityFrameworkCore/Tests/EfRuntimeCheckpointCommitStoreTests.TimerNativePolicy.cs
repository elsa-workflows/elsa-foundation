using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timer_first_native_coordination_preserves_pending_caller_changes_and_transaction_rollback(bool existingAddress)
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        const string run = "workflow-pending";
        if (existingAddress)
        {
            await using var setup = await f.Context.Database.BeginTransactionAsync();
            await EfTimerHistoryOwner.CoordinateFirstNativeAsync(f.Context, "tenant-a", run, default);
            await setup.CommitAsync();
        }
        var prior = await f.Context.ExecutionLivenessStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == EfTimerHistoryOwner.Id("tenant-a", run));
        var state = Execution(run, "tenant-a");
        var pending = EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", run), 1);
        f.Context.WorkflowExecutionStates.Add(pending);
        await using (var transaction = await f.Context.Database.BeginTransactionAsync())
        {
            await EfTimerHistoryOwner.CoordinateFirstNativeAsync(f.Context, "tenant-a", run, default);
            Assert.Equal(EntityState.Added, f.Context.Entry(pending).State);
            Assert.False(await f.Context.WorkflowExecutionStates.AsNoTracking().AnyAsync(row => row.Id == pending.Id));
            Assert.NotNull(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == EfTimerHistoryOwner.Id("tenant-a", run)));
            await transaction.RollbackAsync();
        }
        f.Context.ChangeTracker.Clear();
        Assert.False(await f.Context.WorkflowExecutionStates.AsNoTracking().AnyAsync(row => row.Id == pending.Id));
        var retained = await f.Context.ExecutionLivenessStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == EfTimerHistoryOwner.Id("tenant-a", run));
        Assert.Equal(prior?.Revision, retained?.Revision);
        Assert.Equal(prior?.ContentJson, retained?.ContentJson);
    }

    [Theory]
    [InlineData("clear")] [InlineData("replace")] [InlineData("known-choice")]
    public async Task Timer_history_public_native_save_cannot_replace_original_capture_admission(string mode)
    {
        await using var f = await TimerHistoryFixture.Create(); await f.Store.SaveAsync(HistoryTimer());
        var executions = new EfWorkflowExecutionStateStore(f.Context, f.Access, new NoopContinuationCodec());
        var original = (await executions.FindAsync("workflow-a"))!;
        var changed = mode switch
        {
            "clear" => original with { HistoryCapturePolicy = null, HistoryCaptureEnabledAtAdmission = false },
            "replace" => original with { HistoryCapturePolicy = new("replacement", 1, true, TimerHistoryPolicy.SupportedKinds) },
            _ => original with { HistoryCaptureEnabledAtAdmission = null }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => executions.SaveAsync(changed).AsTask());
        f.Context.ChangeTracker.Clear();
        var retained = (await executions.FindAsync("workflow-a"))!;
        Assert.True(TimerHistoryPolicy.IsEquivalentTo(retained.HistoryCapturePolicy)); Assert.True(retained.HistoryCaptureEnabledAtAdmission);
        Assert.True(await f.Store.HasPendingHistoryAsync("workflow-a"));
        var missing = new RuntimeCheckpointCommit("missing-history", new("missing-history", "ordinary", "workflow-a", OccurredAt, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [], new Dictionary<string, string>()) { ExpectedFence = f.Completion().ExpectedFence };
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.Writer.CommitAsync(missing, Decision()).AsTask());
        Assert.Empty(await f.Context.RuntimeCheckpointCommits.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(null)] [InlineData(false)]
    public async Task Timer_history_public_native_save_cannot_retrofit_a_preexisting_uncaptured_choice(bool? enabled)
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        var executions = new EfWorkflowExecutionStateStore(f.Context, f.Access, new NoopContinuationCodec());
        var original = Execution("workflow-a", "tenant-a") with { HistoryCaptureEnabledAtAdmission = enabled };
        await executions.SaveAsync(original);
        await Assert.ThrowsAsync<InvalidOperationException>(() => executions.SaveAsync(original with
            { HistoryCapturePolicy = TimerHistoryPolicy, HistoryCaptureEnabledAtAdmission = true }).AsTask());
        f.Context.ChangeTracker.Clear(); var retained = (await executions.FindAsync("workflow-a"))!;
        Assert.Null(retained.HistoryCapturePolicy); Assert.Equal(enabled, retained.HistoryCaptureEnabledAtAdmission);
        Assert.Null((await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).TimerHistoryOwnerJson);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Timer_history_public_first_native_save_cannot_bypass_the_independent_source_owner(bool discharged, bool clearPolicy)
    {
        await using var f = await TimerHistoryFixture.Create(native: false);
        await f.Store.SaveAsync(HistoryTimer());
        if (discharged)
        {
            await f.Store.DeleteAsync("workflow-a", "timer");
            Assert.True(await f.Store.DischargeUnrecordedCompletionAsync(f.Completion()));
        }
        var executions = new EfWorkflowExecutionStateStore(f.Context, f.Access, new NoopContinuationCodec());
        var proposed = Execution("workflow-a", "tenant-a") with
            { HistoryCapturePolicy = clearPolicy ? null : TimerHistoryPolicy, HistoryCaptureEnabledAtAdmission = !clearPolicy };
        await Assert.ThrowsAsync<NotSupportedException>(() => executions.SaveAsync(proposed).AsTask());
        f.Context.ChangeTracker.Clear(); Assert.Null(await executions.FindAsync("workflow-a"));
        Assert.Equal(!discharged, await f.Store.HasPendingHistoryAsync("workflow-a"));
        var owner = EfTimerHistoryOwner.Read(await f.Context.ExecutionLivenessStates.AsNoTracking().SingleAsync(), "tenant-a", "workflow-a")!;
        Assert.Equal(discharged, owner.LostFacts); Assert.Equal(discharged, owner.Completion is not null);
    }

    [Fact]
    public async Task Timer_history_public_native_save_accepts_equivalent_original_policy_and_ordinary_state_changes()
    {
        await using var f = await TimerHistoryFixture.Create();
        var executions = new EfWorkflowExecutionStateStore(f.Context, f.Access, new NoopContinuationCodec());
        var original = (await executions.FindAsync("workflow-a"))!;
        var changed = original with { UpdatedAt = OccurredAt.AddMinutes(1), HistoryCapturePolicy = new(TimerHistoryPolicy.PolicyId,
            TimerHistoryPolicy.Version, TimerHistoryPolicy.Required, TimerHistoryPolicy.SupportedKinds.ToArray()) };
        await executions.SaveAsync(changed); f.Context.ChangeTracker.Clear();
        var retained = (await executions.FindAsync("workflow-a"))!;
        Assert.Equal(changed.UpdatedAt, retained.UpdatedAt); Assert.True(changed.HistoryCapturePolicy.IsEquivalentTo(retained.HistoryCapturePolicy));
    }
}

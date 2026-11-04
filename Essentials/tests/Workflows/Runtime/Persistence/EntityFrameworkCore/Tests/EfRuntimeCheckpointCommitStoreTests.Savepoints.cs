using System.Data.Common;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData(true, "success")]
    [InlineData(false, "success")]
    [InlineData(true, "failure")]
    [InlineData(false, "failure")]
    [InlineData(true, "cancel")]
    [InlineData(false, "cancel")]
    [InlineData(true, "ambiguous")]
    [InlineData(false, "ambiguous")]
    public async Task Owned_checkpoint_avoids_savepoints_and_restores_context_setting(bool initialSetting, string outcome)
    {
        await using var database = await TestDatabase.CreateAsync();
        var savepoints = new CheckpointSavepoints();
        var failure = new FailMarkerInsertInterceptor();
        var cancellation = new CancelMarkerSave();
        var acknowledgement = new CommitAcknowledgementLossInterceptor();
        await using var context = database.Open("tenant-a", savepoints, failure, cancellation, acknowledgement);
        context.Database.AutoSavepointsEnabled = initialSetting;
        // Both saves write: a participant before the marker. Failure at the marker must undo this first save.
        context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
        var commit = Commit("savepoint-control");

        if (outcome == "failure") failure.Arm();
        if (outcome == "cancel") cancellation.Armed = true;
        if (outcome == "ambiguous") acknowledgement.Arm();

        if (outcome == "failure")
            await Assert.ThrowsAsync<DbUpdateException>(() => store.CommitAsync(commit, Decision()).AsTask());
        else if (outcome == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CommitAsync(commit, Decision()).AsTask());
        else
            await store.CommitAsync(commit, Decision());

        Assert.Equal(initialSetting, context.Database.AutoSavepointsEnabled);
        Assert.Equal(0, savepoints.Created);
        await using (var verification = database.Open("tenant-a"))
        {
            var expected = outcome is "success" or "ambiguous" ? 1 : 0;
            Assert.Equal(expected, await verification.SchedulerStates.CountAsync());
            Assert.Equal(expected, await verification.RuntimeCheckpointCommits.CountAsync());
        }

        // A later caller-owned transaction on the same context must retain its original EF behavior.
        await using var next = await context.Database.BeginTransactionAsync();
        var later = SchedulerRow("tenant-a", "another-workflow");
        later.Id = "later-scheduler-row";
        context.SchedulerStates.Add(later);
        await context.SaveChangesAsync();
        Assert.Equal(initialSetting ? 1 : 0, savepoints.Created);
        await next.RollbackAsync();
    }

    private sealed class CheckpointSavepoints : DbTransactionInterceptor
    {
        public int Created { get; private set; }

        public override Task CreatedSavepointAsync(DbTransaction transaction, TransactionEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Created++;
            return Task.CompletedTask;
        }
    }

    private sealed class CancelMarkerSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        private int _saves;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && ++_saves == 2)
            {
                Armed = false;
                throw new OperationCanceledException("Cancel after participant writes, before marker save.");
            }
            return ValueTask.FromResult(result);
        }
    }
}

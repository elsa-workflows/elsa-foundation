using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData("same")] [InlineData("consume")] [InlineData("save")]
    public async Task Timer_native_cleanup_keeps_activity_inspection_and_consumption_atomic_when_tracking_changes(string boundary)
    {
        await using var f = await TimerHistoryFixture.Create();
        await f.Store.SaveAsync(HistoryTimer());
        await f.CreateHead();
        var activity = CheckpointActivity("activity") with
        { Status = ActivityExecutionStatus.Suspended, CompletedAt = null, ExecutionScopeId = "activity-scope" };
        var inspection = ActivityExecutionInspectionProjection.FromState(activity, "checkpoint-1", OccurredAt);
        await new EfActivityExecutionStateStore(f.Context, f.Access, new NoopContinuationCodec()).SaveAsync(activity);
        await new EfActivityExecutionInspectionStore(f.Context, f.Access, new NoopContinuationCodec()).SaveAsync(inspection);
        var queue = new EfSchedulerWorkQueueStore(f.Context, f.Access, new NoopContinuationCodec());
        await queue.EnqueueAsync(SchedulerWork("cancel-scope"));
        var claim = (await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
            "workflow-a", "worker-a", OccurredAt, TimeSpan.FromMinutes(1))))!;
        f.Context.ChangeTracker.Clear();

        var commit = f.Completion() with
        {
            CommitId = "cancel-tracking",
            StateChanges = new RuntimeCheckpointStateChangeSet(null, null,
                [new("activity", RuntimeStateChangeOperation.Upsert,
                    activity with { Status = ActivityExecutionStatus.Cancelled, CompletedAt = OccurredAt }, new Dictionary<string, string>())],
                [], [], [], [],
                activityExecutionInspections: [new("activity", RuntimeStateChangeOperation.Upsert,
                    inspection with { Status = ActivityExecutionStatus.Cancelled, CompletedAt = OccurredAt }, new Dictionary<string, string>())],
                activityScopeCleanups: [new("workflow-a", "activity-scope", ["activity"], [], ["timer"], [])])
                .WithConsumedSchedulerWorkItems([ConsumedSchedulerWorkItem.FromClaim(claim)])
        };
        commit = commit with { HistoryBatches = [new(TimerHistoryPolicy, commit.Checkpoint.CheckpointId, OccurredAt, true, [])] };
        var drift = new NativeCleanupTrackingDrift(boundary);
        await using (var context = f.Database.Open("tenant-a", drift))
        {
            var writer = new EfRuntimeCheckpointCommitStore(context, f.Access, f.Clock,
                rootWriteLeaseManager: new PassThroughRootWriteLeaseManager());
            var failure = await Record.ExceptionAsync(async () => await writer.CommitAsync(commit, Decision()));
            Assert.True(drift.Consumed);
            if (boundary == "same") Assert.Null(failure);
            else { Assert.True(drift.Changed); Assert.IsType<NotSupportedException>(failure); }
            Assert.Null(context.Database.CurrentTransaction);
        }
        await using var read = f.Database.Open("tenant-a");
        var state = await new EfActivityExecutionStateStore(read, f.Access, new NoopContinuationCodec()).FindAsync("workflow-a", "activity");
        var projection = await new EfActivityExecutionInspectionStore(read, f.Access, new NoopContinuationCodec()).FindAsync("workflow-a", "activity");
        var expected = boundary == "same" ? ActivityExecutionStatus.Cancelled : ActivityExecutionStatus.Suspended;
        Assert.Equal(expected, state!.Status); Assert.Equal(expected, projection!.Status);
        var retained = Assert.Single((await new EfDurableTimerStore(read, f.Access, new NoopContinuationCodec(), f.Clock)
            .ListPendingHistoryAsync(new())).Items);
        Assert.Equal(boundary == "same", retained.HistoryResponsibility!.Retired);
        Assert.Equal(boundary == "same" ? 2 : 1, retained.HistoryResponsibility.PendingEvents.Count);
        Assert.Equal(boundary == "same" ? 0 : 1, await read.SchedulerWorkItems.CountAsync());
        Assert.Equal(boundary == "same" ? 2 : 1, await read.RuntimeCheckpointCommits.CountAsync());
    }

    [Fact]
    public async Task Queue_head_discovery_reads_replacement_content_and_allows_reenqueue_after_peer_removal()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var access = new FixedAccessor("tenant-a");
        var reader = new EfSchedulerWorkQueueStore(context, access, new NoopContinuationCodec());
        var original = SchedulerWork("reused-head");
        await reader.EnqueueAsync(original);
        Assert.Equal(original.EnvelopeId, Assert.Single(await reader.ListNextWorkItemsAsync(["workflow-a"])).Value.EnvelopeId);
        await using var peerContext = database.Open("tenant-a");
        var peer = new EfSchedulerWorkQueueStore(peerContext, access, new NoopContinuationCodec());
        Assert.True(await peer.DeleteAsync("workflow-a", original.WorkItemId));
        var replacement = new RuntimeSchedulerWorkItem(original.WorkItemId, "workflow-a", "replacement-command",
            original.CommandKind, "replacement-envelope", "replacement-enqueue", original.EnqueuedAt, original.RecordedAt, original.Sequence);
        await peer.EnqueueAsync(replacement);
        Assert.Equal(replacement.EnvelopeId, Assert.Single(await reader.ListNextWorkItemsAsync(["workflow-a"])).Value.EnvelopeId);
        Assert.True(await peer.DeleteAsync("workflow-a", original.WorkItemId));
        await reader.EnqueueAsync(original);
        await using var fresh = database.Open("tenant-a");
        var verified = new EfSchedulerWorkQueueStore(fresh, access, new NoopContinuationCodec());
        Assert.Equal(original.EnvelopeId, Assert.Single(await verified.ListNextWorkItemsAsync(["workflow-a"])).Value.EnvelopeId);
    }

    private sealed class NativeCleanupTrackingDrift(string boundary) : DbCommandInterceptor, ISaveChangesInterceptor
    {
        internal bool Consumed { get; private set; }
        internal bool Changed { get; private set; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("scheduler_work", StringComparison.OrdinalIgnoreCase))
            {
                Consumed = true;
                if (boundary == "consume") Disable(data.Context!);
            }
            return ValueTask.FromResult(result);
        }
        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Consumed && boundary == "save") Disable(data.Context!);
            return ValueTask.FromResult(result);
        }
        private void Disable(DbContext context)
        { Changed = true; context.ChangeTracker.AutoDetectChangesEnabled = false; }
    }
}

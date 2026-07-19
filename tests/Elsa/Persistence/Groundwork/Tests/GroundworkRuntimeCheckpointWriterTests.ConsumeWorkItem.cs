using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

// WU-1 / spec 105: the claimed scheduler work item's fence-checked deletion is folded into the checkpoint commit's
// unit-of-work, and a stale claimant's commit fails claim-lost without persisting anything.
public sealed partial class GroundworkRuntimeCheckpointWriterTests
{
    private static readonly DateTimeOffset ConsumeNow = DateTimeOffset.UnixEpoch.AddHours(1);

    [Fact]
    public async Task ConsumedWorkItem_IsDeletedInsideTheCheckpointCommit()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var queue = new GroundworkWorkflowSchedulerWorkQueue(store, GroundworkTestSerialization.Serializer);
        await queue.EnqueueAsync(ConsumeWorkItem());
        var claim = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-1", "owner-a", ConsumeNow, TimeSpan.FromSeconds(30)));
        Assert.NotNull(claim);

        var result = await CreateWriter(store).CommitAsync(BuildConsumeCommit("commit-consume", claim!), Decision);

        Assert.Equal(["consume-work-1"], result.ConsumedSchedulerWorkItemIds);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf-1"))).Items); // gone, in the same commit
        Assert.NotNull(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-consume"));

        // Redelivery is idempotent: the marker records the consumed id and returns it without re-deleting.
        var replay = await CreateWriter(store).CommitAsync(BuildConsumeCommit("commit-consume", claim!), Decision);
        Assert.Equal(["consume-work-1"], replay.ConsumedSchedulerWorkItemIds);
    }

    [Fact]
    public async Task ConsumedWorkItem_WhenReclaimedBySuccessor_FailsClaimLostAndPersistsNothing()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var queue = new GroundworkWorkflowSchedulerWorkQueue(store, GroundworkTestSerialization.Serializer);
        await queue.EnqueueAsync(ConsumeWorkItem());

        var staleClaim = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-1", "owner-a", ConsumeNow, TimeSpan.FromSeconds(30)));
        Assert.NotNull(staleClaim);
        // Successor reclaims after the visibility window (fencing token advances).
        var successorClaim = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-1", "owner-b", ConsumeNow.AddMinutes(1), TimeSpan.FromSeconds(30)));
        Assert.NotNull(successorClaim);
        Assert.NotEqual(staleClaim!.FencingToken, successorClaim!.FencingToken);

        await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(
            () => CreateWriter(store).CommitAsync(BuildConsumeCommit("commit-stale", staleClaim), Decision).AsTask());

        // Nothing persisted: no marker, and the successor-owned item survives.
        Assert.Null(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-stale"));
        Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf-1"))).Items);
    }

    private static RuntimeSchedulerWorkItem ConsumeWorkItem() =>
        new(
            "consume-work-1", "wf-1", "command-1", WorkflowExecutionCommandKind.RunSchedulerWork,
            "envelope-1", "key-1", ConsumeNow, ConsumeNow);

    private static RuntimeCheckpointCommit BuildConsumeCommit(string commitId, RuntimeSchedulerWorkClaim claim)
    {
        var stateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [])
            .WithConsumedSchedulerWorkItems([ConsumedSchedulerWorkItem.FromClaim(claim)]);
        var checkpoint = new RuntimeCheckpoint(
            CheckpointId: $"cp-{commitId}",
            Name: "checkpoint",
            WorkflowExecutionId: "wf-1",
            OccurredAt: ConsumeNow,
            ActivityExecutionIds: [],
            Metadata: new Dictionary<string, string>());
        return new RuntimeCheckpointCommit(commitId, checkpoint, stateChanges, PostCommitIntents: [], Metadata: new Dictionary<string, string>());
    }
}

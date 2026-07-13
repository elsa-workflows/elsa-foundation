using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

// Cross-provider contract for IRuntimeCheckpointCommitStore: a checkpoint carrying post-commit work must be
// applied so the outbox can deliver it, and the store must acknowledge every item it was handed. Every commit
// store implementation derives from this so the post-commit round-trip can never silently regress on one
// provider while passing on another (the regression that stalled all Groundwork-backed workflow execution).
public abstract class RuntimeCheckpointCommitStorePostCommitContract
{
    private static readonly RuntimeCheckpointPersistenceDecision Decision = new(RuntimeCheckpointPersistenceMode.Immediate);

    protected abstract (IRuntimeCheckpointCommitStore Writer, IRuntimePostCommitOutboxStore Outbox) Create();

    [Fact]
    public async Task Commit_With_PostCommitWork_Is_Acknowledged_And_Deliverable()
    {
        var (writer, outbox) = Create();
        var commit = CommitWithPostCommitIntent("wfexec-1", "commit-1", "intent-1");

        var result = await writer.CommitAsync(commit, Decision);

        // The store acknowledges every outbox item it was handed (the committer enforces this invariant).
        Assert.Equal(["commit-1:intent-1"], result.PendingPostCommitWorkIds);

        // And the very item it persisted is now deliverable by the outbox reader.
        var deliverable = await outbox.GetDeliverableAsync(
            new RuntimePostCommitOutboxQuery(DateTimeOffset.UtcNow, limit: 10, workflowExecutionId: "wfexec-1", intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        var item = Assert.Single(deliverable);
        Assert.Equal("commit-1:intent-1", item.OutboxItemId);
        Assert.Equal("intent-1", item.Intent.IntentId);
    }

    [Fact]
    public async Task Commit_Without_PostCommitWork_Has_Nothing_Deliverable()
    {
        var (writer, outbox) = Create();

        var result = await writer.CommitAsync(CommitWithPostCommitIntent("wfexec-1", "commit-1", intentId: null), Decision);

        Assert.Empty(result.PendingPostCommitWorkIds);
        Assert.Empty(await outbox.GetDeliverableAsync(
            new RuntimePostCommitOutboxQuery(DateTimeOffset.UtcNow, limit: 10, workflowExecutionId: "wfexec-1")));
    }

    // Mirrors what RuntimeCheckpointCommitter does: fold the intents into the applied change set via the shared factory.
    private static RuntimeCheckpointCommit CommitWithPostCommitIntent(string workflowExecutionId, string commitId, string? intentId)
    {
        var intents = intentId is null
            ? Array.Empty<RuntimePostCommitIntent>()
            :
            [
                new RuntimePostCommitIntent(
                    intentId: intentId,
                    workflowExecutionId: workflowExecutionId,
                    kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
                    recordedAt: DateTimeOffset.UnixEpoch,
                    activityExecutionId: null,
                    idempotencyKey: null,
                    payload: null)
            ];

        var checkpoint = new RuntimeCheckpoint(
            CheckpointId: $"cp-{commitId}",
            Name: "checkpoint",
            WorkflowExecutionId: workflowExecutionId,
            OccurredAt: DateTimeOffset.UnixEpoch,
            ActivityExecutionIds: [],
            Metadata: new Dictionary<string, string>());

        var commit = new RuntimeCheckpointCommit(
            commitId,
            checkpoint,
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            PostCommitIntents: intents,
            Metadata: new Dictionary<string, string>());

        return commit with { StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(commit)) };
    }
}

public sealed class InMemoryRuntimeCheckpointCommitStorePostCommitContractTests : RuntimeCheckpointCommitStorePostCommitContract
{
    protected override (IRuntimeCheckpointCommitStore Writer, IRuntimePostCommitOutboxStore Outbox) Create()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        return (store, store); // one object implements both seams
    }
}

public sealed class GroundworkRuntimeCheckpointCommitStorePostCommitContractTests : RuntimeCheckpointCommitStorePostCommitContract
{
    protected override (IRuntimeCheckpointCommitStore Writer, IRuntimePostCommitOutboxStore Outbox) Create()
    {
        IDocumentStore store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var writer = new GroundworkRuntimeCheckpointWriter(
            store,
            GroundworkTestSerialization.Serializer,
            new GroundworkWorkflowExecutionStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkSchedulerStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkActivityExecutionStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkBookmarkStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkDurableValueStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkIncidentStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkExecutionLivenessStateStore(store, GroundworkTestSerialization.Serializer),
            PassThroughRootWriteLeaseManager.Instance);
        return (writer, new GroundworkRuntimePostCommitOutboxStore(store, GroundworkTestSerialization.Serializer));
    }
}

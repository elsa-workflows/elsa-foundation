using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkIncidentResolutionOutcomePersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeCheckpointPersistenceDecision Decision =
        new(RuntimeCheckpointPersistenceMode.Immediate);

    [Fact]
    public async Task Direct_save_preserves_a_committed_outcome_and_allows_only_an_identical_replay()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var store = new GroundworkIncidentStateStore(documentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Incident(Outcome(IncidentResolutionActionKinds.FaultWorkflow)));
        await store.SaveAsync(Incident(Outcome(IncidentResolutionActionKinds.FaultWorkflow)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(Incident(outcome: null)).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Incident(Outcome("Contoso.ReplacedOutcome"))).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Incident(Outcome(IncidentResolutionActionKinds.FaultWorkflow), IncidentStatus.Open)).AsTask());

        var persisted = await store.FindAsync("wf-1", "incident-1");
        Assert.NotNull(persisted);
        Assert.Equal(IncidentResolutionActionKinds.FaultWorkflow, persisted.ResolutionOutcome!.ActionKind);
        Assert.Equal(IncidentStatus.Blocking, persisted.Status);
    }

    [Fact]
    public async Task Direct_save_cannot_change_the_resolution_time_of_a_committed_outcome()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var store = new GroundworkIncidentStateStore(documentStore, GroundworkTestSerialization.Serializer);
        var outcome = Outcome("Contoso.Resolve");
        await store.SaveAsync(Incident(outcome, IncidentStatus.Resolved, Now.AddMinutes(2)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Incident(outcome, IncidentStatus.Resolved, Now.AddMinutes(3))).AsTask());

        Assert.Equal(Now.AddMinutes(2), (await store.FindAsync("wf-1", "incident-1"))!.ResolvedAt);
    }

    [Fact]
    public async Task Checkpoint_write_cannot_mutate_a_committed_outcome_or_its_lifecycle_effect()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var incidents = new GroundworkIncidentStateStore(documentStore, GroundworkTestSerialization.Serializer);
        await incidents.SaveAsync(Incident(Outcome(IncidentResolutionActionKinds.FaultWorkflow)));
        var writer = NewWriter(documentStore);

        await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(() =>
            writer.CommitAsync(Commit("commit-clear", Incident(outcome: null)), Decision).AsTask());
        await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(() =>
            writer.CommitAsync(
                Commit("commit-open", Incident(Outcome(IncidentResolutionActionKinds.FaultWorkflow), IncidentStatus.Open)),
                Decision).AsTask());

        Assert.Equal(
            IncidentResolutionActionKinds.FaultWorkflow,
            (await incidents.FindAsync("wf-1", "incident-1"))!.ResolutionOutcome!.ActionKind);
        Assert.Null(await documentStore.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
            "commit-clear"));
        Assert.Null(await documentStore.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
            "commit-open"));
    }

    private static GroundworkRuntimeCheckpointWriter NewWriter(IDocumentStore store)
    {
        var access = GroundworkTestAccess.DefaultAccessContextAccessor;
        return new GroundworkRuntimeCheckpointWriter(
            store,
            GroundworkTestSerialization.Serializer,
            access,
            new GroundworkWorkflowExecutionStateStore(store, GroundworkTestSerialization.Serializer, access),
            new GroundworkSchedulerStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkActivityExecutionStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkBookmarkStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkDurableValueStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkIncidentStateStore(store, GroundworkTestSerialization.Serializer),
            new GroundworkExecutionLivenessStateStore(store, GroundworkTestSerialization.Serializer),
            PassThroughRootWriteLeaseManager.Instance);
    }

    private static RuntimeCheckpointCommit Commit(string commitId, IncidentState incident) =>
        new(
            commitId,
            new RuntimeCheckpoint(
                $"checkpoint:{commitId}",
                RuntimeCheckpointNames.IncidentResolutionBatchApplied,
                incident.WorkflowExecutionId,
                Now,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents:
                [
                    new RuntimeStateChange<IncidentState>(
                        incident.IncidentId,
                        RuntimeStateChangeOperation.Upsert,
                        incident,
                        new Dictionary<string, string>())
                ],
                operational: []),
            [],
            new Dictionary<string, string>());

    private static IncidentState Incident(
        IncidentResolutionOutcome? outcome,
        IncidentStatus status = IncidentStatus.Blocking,
        DateTimeOffset? resolvedAt = null) =>
        new(
            incidentId: "incident-1",
            workflowExecutionId: "wf-1",
            activityExecutionId: "activity-1",
            executableNodeId: "node-1",
            severity: IncidentSeverity.Error,
            status: status,
            resolutionOutcome: outcome,
            failureType: "ActivityFaulted",
            message: "Activity failed.",
            createdAt: Now,
            resolvedAt: resolvedAt,
            metadata: new Dictionary<string, string>());

    private static IncidentResolutionOutcome Outcome(string actionKind) =>
        new(
            actionKind,
            Now.AddMinutes(1),
            new IncidentStrategyReference("Fault", "1"),
            systemSource: null,
            metadata: new Dictionary<string, string> { ["phase"] = "Resolve" });
}

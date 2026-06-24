using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkActivityExecutionInspectionStoreTests
{
    [Fact]
    public async Task Store_RoundTrips_And_Lists_In_Execution_Sequence_Order()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var store = new GroundworkActivityExecutionInspectionStore(documentStore);
        await store.SaveAsync(Projection("wf-1", "ae-2", sequence: 2));
        await store.SaveAsync(Projection("wf-1", "ae-1", sequence: 1));

        var found = await store.FindAsync("wf-1", "ae-1");
        var listed = await store.ListAsync("wf-1");

        Assert.NotNull(found);
        Assert.Equal("ae-1", found.ActivityExecutionId);
        Assert.Collection(
            listed,
            projection => Assert.Equal("ae-1", projection.ActivityExecutionId),
            projection => Assert.Equal("ae-2", projection.ActivityExecutionId));
    }

    [Fact]
    public async Task CheckpointWriter_Persists_Inspection_Projection()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var inspectionStore = new GroundworkActivityExecutionInspectionStore(documentStore);
        var writer = new GroundworkRuntimeCheckpointWriter(
            documentStore,
            new GroundworkWorkflowExecutionStateStore(documentStore),
            new GroundworkSchedulerStateStore(documentStore),
            new GroundworkActivityExecutionStateStore(documentStore),
            inspectionStore,
            new GroundworkBookmarkStateStore(documentStore),
            new GroundworkDurableValueStateStore(documentStore),
            new GroundworkIncidentStateStore(documentStore),
            new GroundworkOperationalStateStore(documentStore));
        var projection = Projection("wf-1", "ae-1", sequence: 1);
        var commit = new RuntimeCheckpointCommit(
            CommitId: "commit-1",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "checkpoint-1",
                Name: "ActivityStarted",
                WorkflowExecutionId: "wf-1",
                OccurredAt: DateTimeOffset.UnixEpoch,
                ActivityExecutionIds: ["ae-1"],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: "ae-1",
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: projection,
                        Metadata: new Dictionary<string, string>())
                ]),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

        await writer.WriteAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.NotNull(await inspectionStore.FindAsync("wf-1", "ae-1"));
    }

    private static ActivityExecutionInspectionProjection Projection(
        string workflowExecutionId,
        string activityExecutionId,
        long sequence) =>
        new(
            ActivityExecutionId: activityExecutionId,
            WorkflowExecutionId: workflowExecutionId,
            ExecutableNodeId: $"node-{activityExecutionId}",
            AuthoredActivityId: "authored-a",
            ActivityType: "Elsa.Test",
            ActivityTypeVersion: "1.0.0",
            Status: ActivityExecutionStatus.Completed,
            SubStatus: null,
            ExecutionSequence: sequence,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: DateTimeOffset.UnixEpoch,
            FirstCheckpointId: "checkpoint-first",
            LastCheckpointId: "checkpoint-last",
            LastCommittedAt: DateTimeOffset.UnixEpoch,
            Provenance: ActivitySchedulingProvenance.From(
                workflowExecutionId,
                parentActivityExecutionId: null,
                schedulingActivityExecutionId: null,
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "test"),
            OutcomeNames: ["Done"],
            Bookmarks: [],
            Incidents: [],
            ValueSnapshots: [],
            Metadata: new Dictionary<string, string>());
}

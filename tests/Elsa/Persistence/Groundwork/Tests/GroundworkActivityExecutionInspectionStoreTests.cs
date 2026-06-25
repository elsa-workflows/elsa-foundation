using System.Text.Json;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
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
        var listed = await store.ListSummariesAsync("wf-1");

        Assert.NotNull(found);
        Assert.Equal("ae-1", found.ActivityExecutionId);
        Assert.Collection(
            listed,
            projection => Assert.Equal("ae-1", projection.ActivityExecutionId),
            projection => Assert.Equal("ae-2", projection.ActivityExecutionId));
    }

    [Fact]
    public async Task ListSummariesAsync_Returns_Lightweight_Summaries_With_Evidence_Counts()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var store = new GroundworkActivityExecutionInspectionStore(documentStore);
        await store.SaveAsync(Projection("wf-1", "ae-1", sequence: 1) with
        {
            ValueSnapshots =
            [
                new ActivityExecutionInspectionValueSnapshot(
                    "Input",
                    ActivityExecutionInspectionValueSubject.ActivityInput,
                    RuntimePayloadCaptureMode.Payload,
                    Type: null,
                    DateTimeOffset.UnixEpoch,
                    Payload: JsonSerializer.SerializeToElement("large-payload"),
                    "test",
                    IsSensitive: false,
                    Metadata: new Dictionary<string, string>())
            ]
        });

        var summary = Assert.Single(await store.ListSummariesAsync("wf-1"));

        Assert.Equal("ae-1", summary.ActivityExecutionId);
        Assert.Equal(1, summary.ValueSnapshotCount);
    }

    [Fact]
    public async Task ListSummariesAsync_Does_Not_Deserialize_Full_Projection_When_Summary_Is_Present()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var projection = Projection("wf-1", "ae-1", sequence: 1) with
        {
            ValueSnapshots =
            [
                new ActivityExecutionInspectionValueSnapshot(
                    "Input",
                    ActivityExecutionInspectionValueSubject.ActivityInput,
                    RuntimePayloadCaptureMode.Payload,
                    Type: null,
                    DateTimeOffset.UnixEpoch,
                    Payload: JsonSerializer.SerializeToElement("large-payload"),
                    "test",
                    IsSensitive: false,
                    Metadata: new Dictionary<string, string>())
            ]
        };
        var document = new
        {
            WorkflowExecutionId = projection.WorkflowExecutionId,
            AuthoredActivityId = projection.AuthoredActivityId,
            Summary = ActivityExecutionInspectionSummaryProjection.FromProjection(projection),
            Projection = new { Invalid = "not a projection" }
        };
        await documentStore.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
                DocumentId.Compose(projection.WorkflowExecutionId, projection.ActivityExecutionId),
                ElsaRuntimeStorageManifest.SchemaVersion,
                JsonSerializer.Serialize(document, GroundworkRuntimeJson.Options)));
        var store = new GroundworkActivityExecutionInspectionStore(documentStore);

        var summary = Assert.Single(await store.ListSummariesAsync("wf-1"));

        Assert.Equal("ae-1", summary.ActivityExecutionId);
        Assert.Equal(1, summary.ValueSnapshotCount);
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

    [Fact]
    public async Task SaveAsync_Wraps_DocumentStore_Exception()
    {
        var store = new GroundworkActivityExecutionInspectionStore(new ThrowingDocumentStore(new InvalidOperationException("Provider failure.")));

        var exception = await Assert.ThrowsAsync<GroundworkActivityExecutionInspectionStoreException>(
            () => store.SaveAsync(Projection("wf-1", "ae-1", sequence: 1)).AsTask());

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("wf-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ae-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindAsync_Wraps_Json_Projection_Mapping_Exception()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        await documentStore.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
                DocumentId.Compose("wf-1", "ae-1"),
                ElsaRuntimeStorageManifest.SchemaVersion,
                "{"));
        var store = new GroundworkActivityExecutionInspectionStore(documentStore);

        var exception = await Assert.ThrowsAsync<GroundworkActivityExecutionInspectionStoreException>(
            () => store.FindAsync("wf-1", "ae-1").AsTask());

        Assert.IsType<JsonException>(exception.InnerException);
        Assert.Contains("wf-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ae-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListSummariesAsync_Wraps_DocumentStore_Exception()
    {
        var store = new GroundworkActivityExecutionInspectionStore(new ThrowingDocumentStore(new InvalidOperationException("Provider failure.")));

        var exception = await Assert.ThrowsAsync<GroundworkActivityExecutionInspectionStoreException>(
            () => store.ListSummariesAsync("wf-1").AsTask());

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("wf-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckpointWriter_Wraps_CommitLedger_Load_Exception()
    {
        var documentStore = new ThrowingDocumentStore(new InvalidOperationException("Provider failure."));
        var writer = NewCheckpointWriter(documentStore);
        var commit = InspectionCommit(Projection("wf-1", "ae-1", sequence: 1));

        var exception = await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(
            () => writer.WriteAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("commit-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("wf-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckpointWriter_Rolls_Back_State_When_CommitMarker_Save_Fails()
    {
        var innerStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var documentStore = new CommitMarkerFailingDocumentStore(innerStore);
        var writer = NewCheckpointWriter(documentStore);
        var state = ActivityState("wf-1", "ae-1");
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
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: "ae-1",
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: state,
                        Metadata: new Dictionary<string, string>())
                ],
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

        var exception = await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(
            () => writer.WriteAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());

        Assert.Contains("commit-1", exception.Message, StringComparison.Ordinal);
        Assert.Null(await innerStore.LoadAsync(ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind, DocumentId.Compose("wf-1", "ae-1")));
        Assert.Null(await innerStore.LoadAsync(ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind, DocumentId.Compose("wf-1", "ae-1")));
        Assert.Null(await innerStore.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "commit-1"));
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

    private static ActivityExecutionState ActivityState(string workflowExecutionId, string activityExecutionId) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: activityExecutionId,
                WorkflowExecutionId: workflowExecutionId,
                ExecutableNodeId: "node-a",
                AuthoredActivityId: "authored-a",
                ActivityType: "Elsa.Test",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ExecutionSequence: 1,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.From(
                workflowExecutionId,
                parentActivityExecutionId: null,
                schedulingActivityExecutionId: null,
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "test"),
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static GroundworkRuntimeCheckpointWriter NewCheckpointWriter(IDocumentStore documentStore) =>
        new(
            documentStore,
            new GroundworkWorkflowExecutionStateStore(documentStore),
            new GroundworkSchedulerStateStore(documentStore),
            new GroundworkActivityExecutionStateStore(documentStore),
            new GroundworkActivityExecutionInspectionStore(documentStore),
            new GroundworkBookmarkStateStore(documentStore),
            new GroundworkDurableValueStateStore(documentStore),
            new GroundworkIncidentStateStore(documentStore),
            new GroundworkOperationalStateStore(documentStore));

    private static RuntimeCheckpointCommit InspectionCommit(ActivityExecutionInspectionProjection projection) =>
        new(
            CommitId: "commit-1",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "checkpoint-1",
                Name: "ActivityStarted",
                WorkflowExecutionId: projection.WorkflowExecutionId,
                OccurredAt: DateTimeOffset.UnixEpoch,
                ActivityExecutionIds: [projection.ActivityExecutionId],
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
                        StateId: projection.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: projection,
                        Metadata: new Dictionary<string, string>())
                ]),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    private sealed class ThrowingDocumentStore(Exception exception) : IDocumentStore
    {
        public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class CommitMarkerFailingDocumentStore(InMemoryDocumentStore innerStore) : IDocumentStore
    {
        public TransactionBoundary TransactionBoundary => innerStore.TransactionBoundary;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            innerStore.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            innerStore.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            innerStore.DeleteAsync(request, cancellationToken);

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            innerStore.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            innerStore.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            innerStore.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            innerStore.AnyAsync(query, cancellationToken);

        public async Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            new CommitMarkerFailingUnitOfWork(await innerStore.BeginAsync(scope, cancellationToken));
    }

    private sealed class CommitMarkerFailingUnitOfWork(IDocumentUnitOfWork innerUnitOfWork) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            request.DocumentKind == ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind
                ? throw new InvalidOperationException("Commit marker save failed.")
                : innerUnitOfWork.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            innerUnitOfWork.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            innerUnitOfWork.LoadAsync(documentKind, id, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            innerUnitOfWork.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            innerUnitOfWork.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() =>
            innerUnitOfWork.DisposeAsync();
    }
}

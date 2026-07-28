using System.Text.Json;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Serialization;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkActivityExecutionInspectionStoreTests
{
    [Fact]
    public async Task Store_RoundTrips_And_Lists_In_Execution_Sequence_Order()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var store = new GroundworkActivityExecutionInspectionStore(documentStore, GroundworkTestSerialization.Serializer);
        await store.SaveAsync(Projection("wf-1", "ae-2", sequence: 2));
        await store.SaveAsync(Projection("wf-1", "ae-1", sequence: 1));

        var found = await store.FindAsync("wf-1", "ae-1");
        var listed = await store.ListAllSummariesAsync("wf-1");

        Assert.NotNull(found);
        Assert.Equal("ae-1", found.ActivityExecutionId);
        Assert.Collection(
            listed,
            projection => Assert.Equal("ae-1", projection.ActivityExecutionId),
            projection => Assert.Equal("ae-2", projection.ActivityExecutionId));
    }

    [Fact]
    public async Task ListSummariesPageAsync_Uses_Finite_Ordered_Cursor_And_Exact_Count()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var boundedStore = new RuntimeTestBoundedDocumentStore(documentStore);
        var store = new GroundworkActivityExecutionInspectionStore(
            documentStore,
            GroundworkTestSerialization.Serializer,
            boundedStore);
        await store.SaveAsync(Projection("wf-1", "ae-2", sequence: 2));
        await store.SaveAsync(Projection("wf-1", "ae-1", sequence: 1));
        await store.SaveAsync(Projection("wf-2", "ae-other", sequence: 0));

        var first = await store.ListSummariesPageAsync(
            new ActivityExecutionInspectionSummaryPageQuery("wf-1", limit: 1));
        var second = await store.ListSummariesPageAsync(
            new ActivityExecutionInspectionSummaryPageQuery(
                "wf-1",
                limit: 1,
                first.NextContinuationToken));

        Assert.Equal(2, first.TotalCount);
        Assert.Equal("ae-1", Assert.Single(first.Items).ActivityExecutionId);
        Assert.NotNull(first.NextContinuationToken);
        Assert.Equal(2, second.TotalCount);
        Assert.Equal("ae-2", Assert.Single(second.Items).ActivityExecutionId);
        Assert.Null(second.NextContinuationToken);
    }

    [Fact]
    public async Task SaveAsync_RejectsProviderVersionChangeBetweenLoadAndWrite()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var seedStore = new GroundworkActivityExecutionInspectionStore(documentStore, GroundworkTestSerialization.Serializer);
        await seedStore.SaveAsync(Projection("wf-1", "ae-1", sequence: 1));

        var competingStore = new GroundworkActivityExecutionInspectionStore(documentStore, GroundworkTestSerialization.Serializer);
        var interceptingStore = new InterceptingDocumentStore(documentStore)
        {
            OnBeforeSave = async request =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind, request.DocumentKind);
                Assert.Equal(DocumentId.Compose("wf-1", "ae-1"), request.Id);
                Assert.Equal(1, request.ExpectedVersion);
                await competingStore.SaveAsync(Projection("wf-1", "ae-1", sequence: 2));
            }
        };
        var store = new GroundworkActivityExecutionInspectionStore(interceptingStore, GroundworkTestSerialization.Serializer);

        var exception = await Assert.ThrowsAsync<GroundworkActivityExecutionInspectionStoreException>(() =>
            store.SaveAsync(Projection("wf-1", "ae-1", sequence: 3)).AsTask());

        Assert.Contains("ConcurrencyConflict", exception.InnerException?.Message, StringComparison.Ordinal);
        var winner = await seedStore.FindAsync("wf-1", "ae-1");
        Assert.Equal(2, winner!.ExecutionSequence);
    }

    [Fact]
    public async Task ListSummariesAsync_Returns_Lightweight_Summaries_With_Evidence_Counts()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var store = new GroundworkActivityExecutionInspectionStore(documentStore, GroundworkTestSerialization.Serializer);
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

        var summary = Assert.Single(await store.ListAllSummariesAsync("wf-1"));

        Assert.Equal("ae-1", summary.ActivityExecutionId);
        Assert.Equal(1, summary.ValueSnapshotCount);
    }

    [Fact]
    public async Task ListSummariesAsync_Does_Not_Deserialize_Full_Projection_When_Summary_Is_Present()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
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
                ElsaRuntimeDocumentVersions.Stamp(ElsaRuntimeDocumentVersions.CurrentFor(ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind)),
                GroundworkTestSerialization.Serializer.SerializeForComparison(document)));
        var store = new GroundworkActivityExecutionInspectionStore(documentStore, GroundworkTestSerialization.Serializer);

        var summary = Assert.Single(await store.ListAllSummariesAsync("wf-1"));

        Assert.Equal("ae-1", summary.ActivityExecutionId);
        Assert.Equal(1, summary.ValueSnapshotCount);
    }

    [Fact]
    public async Task CheckpointWriter_Persists_Inspection_Projection()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var inspectionStore = new GroundworkActivityExecutionInspectionStore(documentStore, GroundworkTestSerialization.Serializer);
        var writer = new GroundworkRuntimeCheckpointWriter(
            documentStore,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            new GroundworkWorkflowExecutionStateStore(documentStore, GroundworkTestSerialization.Serializer, GroundworkTestAccess.DefaultAccessContextAccessor),
            new GroundworkSchedulerStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkActivityExecutionStateStore(documentStore, GroundworkTestSerialization.Serializer),
            inspectionStore,
            new GroundworkBookmarkStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkDurableValueStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkIncidentStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkExecutionLivenessStateStore(documentStore, GroundworkTestSerialization.Serializer),
            PassThroughRootWriteLeaseManager.Instance);
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

        await writer.CommitAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.NotNull(await inspectionStore.FindAsync("wf-1", "ae-1"));
    }

    [Fact]
    public async Task SaveAsync_Wraps_DocumentStore_Exception()
    {
        var store = new GroundworkActivityExecutionInspectionStore(new ThrowingDocumentStore(new InvalidOperationException("Provider failure.")), GroundworkTestSerialization.Serializer);

        var exception = await Assert.ThrowsAsync<GroundworkActivityExecutionInspectionStoreException>(
            () => store.SaveAsync(Projection("wf-1", "ae-1", sequence: 1)).AsTask());

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("wf-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ae-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindAsync_Wraps_Json_Projection_Mapping_Exception()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        await documentStore.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
                DocumentId.Compose("wf-1", "ae-1"),
                ElsaRuntimeDocumentVersions.Stamp(ElsaRuntimeDocumentVersions.CurrentFor(
                    ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind)),
                "{"));
        var store = new GroundworkActivityExecutionInspectionStore(documentStore, GroundworkTestSerialization.Serializer);

        var exception = await Assert.ThrowsAsync<GroundworkActivityExecutionInspectionStoreException>(
            () => store.FindAsync("wf-1", "ae-1").AsTask());

        var versionException = Assert.IsType<DocumentSchemaVersionException>(exception.InnerException);
        Assert.Equal(DocumentSchemaVersionFailure.InvalidContent, versionException.Failure);
        Assert.IsAssignableFrom<JsonException>(versionException.InnerException);
        Assert.Contains("wf-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ae-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListSummariesAsync_Wraps_DocumentStore_Exception()
    {
        var failure = new InvalidOperationException("Provider failure.");
        var store = new GroundworkActivityExecutionInspectionStore(
            new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized()),
            GroundworkTestSerialization.Serializer,
            new ThrowingBoundedDocumentStore(failure));

        var exception = await Assert.ThrowsAsync<GroundworkActivityExecutionInspectionStoreException>(
            () => store.ListAllSummariesAsync("wf-1").AsTask());

        Assert.Same(failure, exception.InnerException);
        Assert.Equal("Provider failure.", exception.InnerException!.Message);
        Assert.Contains("wf-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckpointWriter_Wraps_CommitLedger_Load_Exception()
    {
        var documentStore = new ThrowingDocumentStore(new InvalidOperationException("Provider failure."));
        var writer = NewCheckpointWriter(documentStore);
        var commit = InspectionCommit(Projection("wf-1", "ae-1", sequence: 1));

        var exception = await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(
            () => writer.CommitAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("commit-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("wf-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckpointWriter_Wraps_UnitOfWork_Begin_Exception()
    {
        var documentStore = new BeginFailingDocumentStore(
            new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized()),
            new InvalidOperationException("Transaction begin failed."));
        var writer = NewCheckpointWriter(documentStore);
        var commit = InspectionCommit(Projection("wf-1", "ae-1", sequence: 1));

        var exception = await Assert.ThrowsAsync<GroundworkRuntimeCheckpointWriterException>(
            () => writer.CommitAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("commit-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("wf-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckpointWriter_Rolls_Back_State_When_CommitMarker_Save_Fails()
    {
        var innerStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
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
            () => writer.CommitAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());

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
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            new GroundworkWorkflowExecutionStateStore(documentStore, GroundworkTestSerialization.Serializer, GroundworkTestAccess.DefaultAccessContextAccessor),
            new GroundworkSchedulerStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkActivityExecutionStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkActivityExecutionInspectionStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkBookmarkStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkDurableValueStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkIncidentStateStore(documentStore, GroundworkTestSerialization.Serializer),
            new GroundworkExecutionLivenessStateStore(documentStore, GroundworkTestSerialization.Serializer),
            PassThroughRootWriteLeaseManager.Instance);

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
        public DocumentStoreAccess Access { get; } = GroundworkTestAccess.DefaultScoped;

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

    private sealed class ThrowingBoundedDocumentStore(Exception exception) : IBoundedDocumentStore
    {
        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class BeginFailingDocumentStore(InMemoryDocumentStore innerStore, Exception exception) : IDocumentStore
    {
        public TransactionBoundary TransactionBoundary => innerStore.TransactionBoundary;
        public DocumentStoreAccess Access => innerStore.Access;

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

        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class CommitMarkerFailingDocumentStore(InMemoryDocumentStore innerStore) : IDocumentStore
    {
        public TransactionBoundary TransactionBoundary => innerStore.TransactionBoundary;
        public DocumentStoreAccess Access => innerStore.Access;

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

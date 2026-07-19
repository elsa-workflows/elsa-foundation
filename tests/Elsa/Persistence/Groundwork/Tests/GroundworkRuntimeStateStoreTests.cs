using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using System.Text.Json;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

// Contract assertions for the document-shaped runtime store bridges. Each runs against two host-selected
// providers (real Groundwork SQLite and an in-memory document store). Identical behavior proves the
// bridges are provider-neutral, and round-tripping proves the runtime state survives serialization.
public sealed class GroundworkRuntimeStateStoreTests
{
    [Fact]
    public async Task Workflow_execution_write_rejects_explicit_wrong_tenant_before_store_io()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var store = new GroundworkWorkflowExecutionStateStore(
            documentStore,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.AccessContext("tenant-a"));
        var state = WorkflowState("wf-1", WorkflowExecutionStatus.Running) with { TenantId = "tenant-b" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(state).AsTask());

        Assert.Equal(0, documentStore.SaveCount);
        Assert.Empty(documentStore.Snapshot(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind));
    }

    [Fact]
    public async Task Workflow_execution_query_rejects_explicit_wrong_tenant_before_provider_query()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var store = new GroundworkWorkflowExecutionStateStore(
            documentStore,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.AccessContext("tenant-a"),
            documentStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 10, TenantId: "tenant-b")).AsTask());

        Assert.Equal(0, documentStore.DocumentQueryCount);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ActivityExecutionState_RoundTrips_And_Lists_By_Workflow(string provider)
    {
        await using var fixture = CreateStore(provider);
        IActivityExecutionStateStore store = new GroundworkActivityExecutionStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(ActivityState("wf-1", "ae-1"));
        await store.SaveAsync(ActivityState("wf-1", "ae-2"));
        await store.SaveAsync(ActivityState("wf-2", "ae-3"));

        var found = await store.FindAsync("wf-1", "ae-1");
        Assert.NotNull(found);
        Assert.Equal("ae-1", found!.Execution.ActivityExecutionId);
        Assert.Equal("node-ae-1", found.Execution.ExecutableNodeId);
        Assert.Equal(ActivityExecutionStatus.Running, found.Status);

        Assert.Null(await store.FindAsync("wf-1", "missing"));

        var forWf1 = await store.ListAllAsync("wf-1");
        Assert.Equal(new[] { "ae-1", "ae-2" }, forWf1.Select(x => x.Execution.ActivityExecutionId).OrderBy(x => x));
        Assert.Single(await store.ListAllAsync("wf-2"));
        Assert.Equal(2, await store.CountAsync("wf-1"));
        Assert.Equal(1, await store.CountAsync("wf-2"));
    }

    [Fact]
    public async Task ActivityExecutionState_Save_RejectsProviderVersionChangeBetweenLoadAndWrite()
    {
        await using var fixture = CreateStore("memory");
        IActivityExecutionStateStore seedStore = new GroundworkActivityExecutionStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        await seedStore.SaveAsync(ActivityState("wf-1", "ae-1"));

        var competingStore = new GroundworkActivityExecutionStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var interceptingStore = new InterceptingDocumentStore(fixture.DocumentStore)
        {
            OnBeforeSave = async request =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind, request.DocumentKind);
                Assert.Equal(DocumentId.Compose("wf-1", "ae-1"), request.Id);
                Assert.Equal(1, request.ExpectedVersion);
                await competingStore.SaveAsync(ActivityState("wf-1", "ae-1", ActivityExecutionStatus.Completed));
            }
        };
        IActivityExecutionStateStore store = new GroundworkActivityExecutionStateStore(
            interceptingStore,
            GroundworkTestSerialization.Serializer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(ActivityState("wf-1", "ae-1", ActivityExecutionStatus.Faulted)).AsTask());

        Assert.Contains("ConcurrencyConflict", exception.Message, StringComparison.Ordinal);
        var winner = await seedStore.FindAsync("wf-1", "ae-1");
        Assert.Equal(ActivityExecutionStatus.Completed, winner!.Status);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ActivityExecutionState_ListByParent_Equals_ListWhereParent(string provider)
    {
        await using var fixture = CreateStore(provider);
        IActivityExecutionStateStore store = new GroundworkActivityExecutionStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        // Two children under composite "parent-a", one under "parent-b", one orphan (no parent), all in wf-1;
        // plus a child under "parent-a" in a DIFFERENT workflow (wf-2) to prove the (wf, parent) scoping.
        await store.SaveAsync(ChildState("wf-1", "ae-a1", "parent-a"));
        await store.SaveAsync(ChildState("wf-1", "ae-a2", "parent-a"));
        await store.SaveAsync(ChildState("wf-1", "ae-b1", "parent-b"));
        await store.SaveAsync(ChildState("wf-1", "ae-orphan", parentActivityExecutionId: null));
        await store.SaveAsync(ChildState("wf-2", "ae-a3", "parent-a"));

        var byParent = await store.ListAllByParentAsync("wf-1", "parent-a");
        var expected = (await store.ListAllAsync("wf-1"))
            .Where(s => s.ParentActivityExecutionId == "parent-a");

        Assert.Equal(
            expected.Select(s => s.Execution.ActivityExecutionId).OrderBy(x => x),
            byParent.Select(s => s.Execution.ActivityExecutionId).OrderBy(x => x));
        Assert.Equal(new[] { "ae-a1", "ae-a2" }, byParent.Select(s => s.Execution.ActivityExecutionId).OrderBy(x => x));

        // The wf-2 child under the same parent id must not leak into wf-1's parent-scoped read (defensive wf filter).
        Assert.DoesNotContain(byParent, s => s.Execution.ActivityExecutionId == "ae-a3");

        // A parent with no children returns empty, not the whole workflow.
        Assert.Empty(await store.ListAllByParentAsync("wf-1", "parent-missing"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ActivityExecutionState_Pages_Are_Finite_Ordered_And_Scope_Bound(string provider)
    {
        await using var fixture = CreateStore(provider);
        IActivityExecutionStateStore store = new GroundworkActivityExecutionStateStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore);
        await store.SaveAsync(ChildState("wf-1", "ae-c", "parent-a"));
        await store.SaveAsync(ChildState("wf-1", "ae-a", "parent-a"));
        await store.SaveAsync(ChildState("wf-1", "ae-b", "parent-a"));
        await store.SaveAsync(ChildState("wf-2", "ae-d", "parent-a"));

        var first = await store.ListByParentPageAsync(
            new ActivityExecutionStateParentPageQuery("wf-1", "parent-a", limit: 2));
        var second = await store.ListByParentPageAsync(
            new ActivityExecutionStateParentPageQuery(
                "wf-1",
                "parent-a",
                limit: 2,
                continuationToken: first.NextContinuationToken));

        Assert.Equal(["ae-a", "ae-b"], first.Items.Select(x => x.Execution.ActivityExecutionId));
        Assert.NotNull(first.NextContinuationToken);
        Assert.Equal(["ae-c"], second.Items.Select(x => x.Execution.ActivityExecutionId));
        Assert.Null(second.NextContinuationToken);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task WorkflowExecutionState_RoundTrips_Replaces_And_Lists_All(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutionStateStore store = new GroundworkWorkflowExecutionStateStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            fixture.BoundedDocumentStore);

        await store.SaveAsync(WorkflowState("wf-1", WorkflowExecutionStatus.Running));
        await store.SaveAsync(WorkflowState("wf-2", WorkflowExecutionStatus.Pending));
        await store.SaveAsync(WorkflowState("wf-1", WorkflowExecutionStatus.Completed, WorkflowRunKind.PublishedRun));

        var found = await store.FindAsync("wf-1");
        Assert.NotNull(found);
        Assert.Equal(WorkflowExecutionStatus.Completed, found!.Status);
        Assert.Equal("artifact-wf-1", found.PinnedExecutable.ArtifactId);
        Assert.Equal(WorkflowRunKind.PublishedRun, found.RunKind);

        Assert.Null(await store.FindAsync("missing"));

        var all = await store.ListAsync();
        Assert.Equal(new[] { "wf-1", "wf-2" }, all.Select(x => x.WorkflowExecutionId).OrderBy(x => x));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task DurableValueState_RoundTrips_Lists_And_Deletes(string provider)
    {
        await using var fixture = CreateStore(provider);
        IDurableValueStateStore store = new GroundworkDurableValueStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(DurableValue("wf-1", "dv-1"));
        await store.SaveAsync(DurableValue("wf-1", "dv-2"));

        var found = await store.FindAsync("wf-1", "dv-1");
        Assert.NotNull(found);
        Assert.Equal("value-dv-1", found!.ValueId);
        Assert.Equal(42, found.InlineValue!.Value.GetInt32());

        Assert.Equal(2, (await store.ListAllDurableValueStatesAsync("wf-1")).Count);
        Assert.True(await store.DeleteAsync("wf-1", "dv-1"));
        Assert.False(await store.DeleteAsync("wf-1", "dv-1"));
        Assert.Null(await store.FindAsync("wf-1", "dv-1"));
        Assert.Single(await store.ListAllDurableValueStatesAsync("wf-1"));
    }

    [Fact]
    public async Task DurableValueState_Save_RejectsProviderVersionChangeBetweenLoadAndWrite()
    {
        await using var fixture = CreateStore("memory");
        IDurableValueStateStore seedStore = new GroundworkDurableValueStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        await seedStore.SaveAsync(DurableValue("wf-1", "dv-1", value: 42));

        var competingStore = new GroundworkDurableValueStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var interceptingStore = new InterceptingDocumentStore(fixture.DocumentStore)
        {
            OnBeforeSave = async request =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.DurableValueStateDocumentKind, request.DocumentKind);
                Assert.Equal(DocumentId.Compose("wf-1", "dv-1"), request.Id);
                Assert.Equal(1, request.ExpectedVersion);
                await competingStore.SaveAsync(DurableValue("wf-1", "dv-1", value: 84));
            }
        };
        IDurableValueStateStore store = new GroundworkDurableValueStateStore(
            interceptingStore,
            GroundworkTestSerialization.Serializer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(DurableValue("wf-1", "dv-1", value: 126)).AsTask());

        Assert.Contains("ConcurrencyConflict", exception.Message, StringComparison.Ordinal);
        var winner = await seedStore.FindAsync("wf-1", "dv-1");
        Assert.Equal(84, winner!.InlineValue!.Value.GetInt32());
    }

    [Fact]
    public async Task DurableValueState_Delete_RejectsProviderVersionChangeBetweenLoadAndDelete()
    {
        await using var fixture = CreateStore("memory");
        IDurableValueStateStore seedStore = new GroundworkDurableValueStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        await seedStore.SaveAsync(DurableValue("wf-1", "dv-1", value: 42));

        var competingStore = new GroundworkDurableValueStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var interceptingStore = new InterceptingDocumentStore(fixture.DocumentStore)
        {
            OnBeforeDelete = async request =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.DurableValueStateDocumentKind, request.DocumentKind);
                Assert.Equal(DocumentId.Compose("wf-1", "dv-1"), request.Id);
                Assert.Equal(1, request.ExpectedVersion);
                await competingStore.SaveAsync(DurableValue("wf-1", "dv-1", value: 84));
            }
        };
        IDurableValueStateStore store = new GroundworkDurableValueStateStore(
            interceptingStore,
            GroundworkTestSerialization.Serializer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.DeleteAsync("wf-1", "dv-1").AsTask());

        Assert.Contains("ConcurrencyConflict", exception.Message, StringComparison.Ordinal);
        var winner = await seedStore.FindAsync("wf-1", "dv-1");
        Assert.Equal(84, winner!.InlineValue!.Value.GetInt32());
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SchedulerState_RoundTrips_With_WorkItems_And_Lists_All(string provider)
    {
        await using var fixture = CreateStore(provider);
        ISchedulerStateStore store = new GroundworkSchedulerStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Scheduler("wf-1", version: 3));
        await store.SaveAsync(Scheduler("wf-2", version: 1));

        var found = await store.FindAsync("wf-1");
        Assert.NotNull(found);
        Assert.Equal(3, found!.Version);
        var work = Assert.Single(found.PendingWork);
        Assert.Equal("work-wf-1", work.WorkItemId);
        Assert.Equal("node-wf-1", work.ExecutableNodeId);

        Assert.Null(await store.FindAsync("missing"));
        Assert.Equal(2, (await store.ListAsync()).Count);
    }

    [Fact]
    public async Task SchedulerState_Save_RejectsProviderVersionChangeBetweenLoadAndWrite()
    {
        await using var fixture = CreateStore("memory");
        ISchedulerStateStore seedStore = new GroundworkSchedulerStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        await seedStore.SaveAsync(Scheduler("wf-1", version: 1));

        var competingStore = new GroundworkSchedulerStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var interceptingStore = new InterceptingDocumentStore(fixture.DocumentStore)
        {
            OnBeforeSave = async request =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.SchedulerStateDocumentKind, request.DocumentKind);
                Assert.Equal("wf-1", request.Id);
                Assert.Equal(1, request.ExpectedVersion);
                await competingStore.SaveAsync(Scheduler("wf-1", version: 5));
            }
        };
        ISchedulerStateStore store = new GroundworkSchedulerStateStore(
            interceptingStore,
            GroundworkTestSerialization.Serializer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Scheduler("wf-1", version: 2)).AsTask());

        Assert.Contains("ConcurrencyConflict", exception.Message, StringComparison.Ordinal);
        var winner = await seedStore.FindAsync("wf-1");
        Assert.Equal(5, winner!.Version);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task OperationalState_RoundTrips_Lists_By_Workflow_And_All(string provider)
    {
        await using var fixture = CreateStore(provider);
        IExecutionLivenessStateStore store = new GroundworkExecutionLivenessStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Operational("wf-1", "op-1"));
        await store.SaveAsync(Operational("wf-1", "op-2"));
        await store.SaveAsync(Operational("wf-2", "op-3"));

        var found = await store.FindAsync("wf-1", "op-1");
        Assert.NotNull(found);
        Assert.Equal("op-1", found!.OperationalStateId);

        Assert.Equal(2, (await store.ListAsync("wf-1")).Count);
        Assert.Equal(3, (await store.ListAllAsync()).Count);
        Assert.Null(await store.FindAsync("wf-1", "missing"));
    }

    [Fact]
    public async Task OperationalState_PagesUseDeclaredOrderedRoutes()
    {
        await using var fixture = CreateStore("memory");
        var queries = new EmptyRecordingBoundedDocumentStore();
        IExecutionLivenessStateStore store = new GroundworkExecutionLivenessStateStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            queries);

        await store.ListPageAsync(new ExecutionLivenessStatePageQuery("wf-1", limit: 17, continuationToken: "next"));
        await store.ListAllPageAsync(new RuntimeStorePageRequest(limit: 13, continuationToken: "all-next"));

        Assert.Collection(
            queries.Queries,
            query =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.PageExecutionLivenessStatesByWorkflowExecutionQuery, query.QueryIdentity);
                Assert.Equal(17, query.Take);
                Assert.Equal("next", query.Continuation);
                Assert.Equal(ElsaRuntimeStorageManifest.ExecutionLivenessOperationalStateIdField, Assert.Single(query.Order).Path);
            },
            query =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.PageExecutionLivenessStatesQuery, query.QueryIdentity);
                Assert.Equal(13, query.Take);
                Assert.Equal("all-next", query.Continuation);
                Assert.Equal(
                    [
                        ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                        ElsaRuntimeStorageManifest.ExecutionLivenessOperationalStateIdField
                    ],
                    query.Order.Select(order => order.Path));
            });
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task OperationalState_ConditionalWrites_PreserveWinnerAndRevision(string provider)
    {
        await using var fixture = CreateStore(provider);
        IExecutionLivenessStateStore store = new GroundworkExecutionLivenessStateStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer);
        var first = OperationalWithValue("wf-1", "ownership:wf-1", "first");
        var second = OperationalWithValue("wf-1", "ownership:wf-1", "second");

        var created = await store.TrySaveAsync(first, expectedRevision: 0);
        var duplicate = await store.TrySaveAsync(second, expectedRevision: 0);
        var loaded = await store.FindVersionedAsync("wf-1", "ownership:wf-1");
        var updated = await store.TrySaveAsync(second, expectedRevision: loaded!.Revision);
        var stale = await store.TrySaveAsync(first, expectedRevision: loaded.Revision);
        var missing = await store.TrySaveAsync(
            OperationalWithValue("wf-1", "missing", "missing"),
            expectedRevision: 1);

        Assert.Equal(ExecutionLivenessStateWriteStatus.Saved, created.Status);
        Assert.Equal(1, created.Revision);
        Assert.Equal(ExecutionLivenessStateWriteStatus.RevisionConflict, duplicate.Status);
        Assert.Equal("first", loaded.State.Metadata["value"]);
        Assert.Equal(ExecutionLivenessStateWriteStatus.Saved, updated.Status);
        Assert.Equal(2, updated.Revision);
        Assert.Equal(ExecutionLivenessStateWriteStatus.RevisionConflict, stale.Status);
        Assert.Equal(ExecutionLivenessStateWriteStatus.NotFound, missing.Status);
        Assert.Equal("second", (await store.FindAsync("wf-1", "ownership:wf-1"))!.Metadata["value"]);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ControlPlaneState_RoundTrips_Scoped_And_Global(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowHoldStateStore store = new GroundworkWorkflowHoldStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(new WorkflowHoldState("cp-1", "wf-1"));
        await store.SaveAsync(new WorkflowHoldState("cp-2", "wf-1"));
        await store.SaveAsync(new WorkflowHoldState("cp-global", workflowExecutionId: null));

        var found = await store.FindAsync("cp-1");
        Assert.NotNull(found);
        Assert.Equal("wf-1", found!.WorkflowExecutionId);

        var scoped = await store.ListForWorkflowExecutionAsync("wf-1");
        Assert.Equal(new[] { "cp-1", "cp-2" }, scoped.Select(x => x.ControlPlaneStateId).OrderBy(x => x));

        // The global state has no workflow execution id and must be excluded from the per-workflow query
        // but present in the unfiltered list.
        Assert.DoesNotContain(scoped, x => x.ControlPlaneStateId == "cp-global");
        Assert.Equal(3, (await store.ListAllAsync()).Count);
    }

    [Fact]
    public async Task ControlPlaneState_Save_RejectsProviderVersionChangeBetweenLoadAndWrite()
    {
        await using var fixture = CreateStore("memory");
        IWorkflowHoldStateStore seedStore = new GroundworkWorkflowHoldStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        await seedStore.SaveAsync(WorkflowHold("cp-1", "wf-1", "first"));

        var competingStore = new GroundworkWorkflowHoldStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var interceptingStore = new InterceptingDocumentStore(fixture.DocumentStore)
        {
            OnBeforeSave = async request =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind, request.DocumentKind);
                Assert.Equal("cp-1", request.Id);
                Assert.Equal(1, request.ExpectedVersion);
                await competingStore.SaveAsync(WorkflowHold("cp-1", "wf-1", "competing"));
            }
        };
        IWorkflowHoldStateStore store = new GroundworkWorkflowHoldStateStore(
            interceptingStore,
            GroundworkTestSerialization.Serializer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(WorkflowHold("cp-1", "wf-1", "caller")).AsTask());

        Assert.Contains("ConcurrencyConflict", exception.Message, StringComparison.Ordinal);
        var winner = await seedStore.FindAsync("cp-1");
        Assert.Equal("competing", winner!.Metadata["value"]);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task IncidentState_TryAdd_Is_InsertOnly_And_ListBlocking_Filters(string provider)
    {
        await using var fixture = CreateStore(provider);
        IIncidentStateStore store = new GroundworkIncidentStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        Assert.True(await store.TryAddAsync(Incident("wf-1", "inc-1", IncidentStatus.Open)));
        // Second insert of the same key must fail; the original must remain untouched.
        Assert.False(await store.TryAddAsync(Incident("wf-1", "inc-1", IncidentStatus.Blocking)));
        Assert.Equal(IncidentStatus.Open, (await store.FindAsync("wf-1", "inc-1"))!.Status);

        await store.SaveAsync(Incident("wf-1", "inc-2", IncidentStatus.Blocking));
        await store.SaveAsync(Incident("wf-1", "inc-1", IncidentStatus.Blocking)); // upsert promotes inc-1

        Assert.Equal(2, (await store.ListAsync("wf-1")).Count);
        Assert.Equal(2, await store.CountAsync("wf-1"));
        var blocking = await store.ListBlockingAsync("wf-1");
        Assert.Equal(new[] { "inc-1", "inc-2" }, blocking.Select(x => x.IncidentId).OrderBy(x => x));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task IncidentState_LongFaultIdentity_RoundTrips_And_Remains_InsertOnly(string provider)
    {
        await using var fixture = CreateStore(provider);
        IIncidentStateStore store = new GroundworkIncidentStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var workItemId = $"work:{new string('x', 450)}";
        var incidentId = $"incident:{workItemId}:activity-1:Faulted";
        Assert.True(incidentId.Length > 450, $"Expected the regression identity to exceed 450 code units, but observed {incidentId.Length}.");

        Assert.True(await store.TryAddAsync(Incident("wf-1", incidentId, IncidentStatus.Open)));
        Assert.False(await store.TryAddAsync(Incident("wf-1", incidentId, IncidentStatus.Blocking)));
        Assert.Equal(IncidentStatus.Open, (await store.FindAsync("wf-1", incidentId))!.Status);

        await store.SaveAsync(Incident("wf-1", incidentId, IncidentStatus.Blocking));

        Assert.Equal(IncidentStatus.Blocking, (await store.FindAsync("wf-1", incidentId))!.Status);
        Assert.Equal(incidentId, Assert.Single(await store.ListAsync("wf-1")).IncidentId);
    }

    [Fact]
    public async Task IncidentState_Save_RejectsProviderVersionChangeBetweenLoadAndWrite()
    {
        await using var fixture = CreateStore("memory");
        IIncidentStateStore seedStore = new GroundworkIncidentStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        await seedStore.SaveAsync(Incident("wf-1", "inc-1", IncidentStatus.Open, "first"));

        var interceptingStore = new InterceptingDocumentStore(fixture.DocumentStore)
        {
            OnBeforeSave = async request =>
            {
                Assert.Equal(1, request.ExpectedVersion);
                var (schemaVersion, content) = GroundworkTestSerialization.Serializer.Serialize(
                    ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
                    Incident("wf-1", "inc-1", IncidentStatus.Blocking, "competing"));
                var competingWrite = await fixture.DocumentStore.SaveAsync(new SaveDocumentRequest(
                    request.DocumentKind,
                    request.Id,
                    schemaVersion,
                    content));
                Assert.Equal(DocumentStoreWriteStatus.Saved, competingWrite.Status);
            }
        };
        IIncidentStateStore store = new GroundworkIncidentStateStore(
            interceptingStore,
            GroundworkTestSerialization.Serializer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Incident("wf-1", "inc-1", IncidentStatus.Blocking, "caller")).AsTask());

        Assert.Contains("ConcurrencyConflict", exception.Message, StringComparison.Ordinal);
        var winner = await seedStore.FindAsync("wf-1", "inc-1");
        Assert.Equal(IncidentStatus.Blocking, winner!.Status);
        Assert.Equal("competing", winner.Message);
    }

    [Fact]
    public async Task IncidentState_PhysicalIdentityCollision_FailsClosed()
    {
        await using var fixture = CreateStore("sqlite");
        IIncidentStateStore store = new GroundworkIncidentStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var incidentId = $"incident:work:{new string('x', 450)}:activity-1:Faulted";
        var logicalDocumentId = DocumentId.Compose("wf-1", incidentId);
        var physicalDocumentId = GroundworkPhysicalDocumentIdTestData.PhysicalAliasFor(logicalDocumentId);
        var wrongState = Incident("wf-wrong", "incident-wrong", IncidentStatus.Open);
        var (schemaVersion, content) = GroundworkTestSerialization.Serializer.Serialize(
            ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
            wrongState);
        await fixture.DocumentStore.SaveAsync(new SaveDocumentRequest(
            ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
            physicalDocumentId,
            schemaVersion,
            content));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.FindAsync("wf-1", incidentId));

        Assert.Contains("physical document identity collision", exception.Message, StringComparison.Ordinal);
    }

    private static ActivityExecutionState ActivityState(
        string workflowExecutionId,
        string activityExecutionId,
        ActivityExecutionStatus status = ActivityExecutionStatus.Running) => new(
        new ActivityExecution(activityExecutionId, workflowExecutionId, $"node-{activityExecutionId}", "authored", "Elsa.Log", "1.0.0"),
        status,
        SubStatus: null,
        ScheduledAt: DateTimeOffset.UnixEpoch,
        StartedAt: DateTimeOffset.UnixEpoch,
        CompletedAt: null,
        SchedulingActivityExecutionId: null,
        ParentActivityExecutionId: null,
        BranchId: null,
        IterationId: null,
        CallStackDepth: 0,
        BookmarkIds: [],
        IncidentIds: [],
        FaultCount: 0,
        AggregateFaultCount: 0,
        Metadata: new Dictionary<string, string>());

    private static ActivityExecutionState ChildState(string workflowExecutionId, string activityExecutionId, string? parentActivityExecutionId) => new(
        new ActivityExecution(activityExecutionId, workflowExecutionId, $"node-{activityExecutionId}", "authored", "Elsa.Log", "1.0.0"),
        ActivityExecutionStatus.Running,
        SubStatus: null,
        ScheduledAt: DateTimeOffset.UnixEpoch,
        StartedAt: DateTimeOffset.UnixEpoch,
        CompletedAt: null,
        SchedulingActivityExecutionId: parentActivityExecutionId,
        ParentActivityExecutionId: parentActivityExecutionId,
        BranchId: null,
        IterationId: null,
        CallStackDepth: 0,
        BookmarkIds: [],
        IncidentIds: [],
        FaultCount: 0,
        AggregateFaultCount: 0,
        Metadata: new Dictionary<string, string>());

    private static WorkflowExecutionState WorkflowState(
        string workflowExecutionId,
        WorkflowExecutionStatus status,
        WorkflowRunKind runKind = WorkflowRunKind.Unknown) => new(
        workflowExecutionId,
        new WorkflowExecutableIdentity(
            ArtifactId: $"artifact-{workflowExecutionId}",
            DefinitionId: "definition-1",
            DefinitionVersionId: "version-1",
            ArtifactVersion: "1",
            ArtifactHash: $"hash-{workflowExecutionId}"),
        status,
        SubStatus: null,
        CreatedAt: DateTimeOffset.UnixEpoch,
        StartedAt: null,
        UpdatedAt: null,
        CompletedAt: null,
        CorrelationId: null,
        ParentWorkflowExecutionId: null,
        TenantId: null,
        SystemMetadata: new Dictionary<string, string>())
        {
            RunKind = runKind
        };

    private static DurableValueState DurableValue(string workflowExecutionId, string durableValueId, int value = 42) => new(
        durableValueId,
        workflowExecutionId,
        $"value-{durableValueId}",
        new RuntimeValueTypeDescriptor("int", null, null),
        DurableValueLifecycle.Instance,
        DurableValueStorage.Inline,
        Json(value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        externalReference: null,
        sourceActivityExecutionId: null,
        capturedAt: DateTimeOffset.UnixEpoch,
        metadata: new Dictionary<string, string>());

    private static WorkflowHoldState WorkflowHold(string controlPlaneStateId, string? workflowExecutionId, string value) =>
        new(
            controlPlaneStateId,
            workflowExecutionId,
            activeHolds: null,
            releasedHolds: null,
            metadata: new Dictionary<string, string>
            {
                ["value"] = value
            });

    private static SchedulerState Scheduler(string workflowExecutionId, long version) => new(
        workflowExecutionId,
        version,
        pendingWork:
        [
            new ScheduledActivityWorkItem(
                WorkItemId: $"work-{workflowExecutionId}",
                WorkflowExecutionId: workflowExecutionId,
                ExecutableNodeId: $"node-{workflowExecutionId}",
                ActivityExecutionId: null,
                SchedulingActivityExecutionId: null,
                BranchId: null,
                IterationId: null,
                EnqueuedAt: DateTimeOffset.UnixEpoch,
                Reason: "scheduled")
        ]);

    private static ExecutionLivenessState Operational(string workflowExecutionId, string operationalStateId) => new(
        operationalStateId,
        workflowExecutionId,
        executionLease: null,
        heartbeat: null,
        drain: null,
        interruptedExecution: null);

    private static ExecutionLivenessState OperationalWithValue(
        string workflowExecutionId,
        string operationalStateId,
        string value) =>
        new(
            operationalStateId,
            workflowExecutionId,
            executionLease: null,
            heartbeat: null,
            drain: null,
            interruptedExecution: null,
            metadata: new Dictionary<string, string> { ["value"] = value });

    private static IncidentState Incident(
        string workflowExecutionId,
        string incidentId,
        IncidentStatus status,
        string message = "boom") => new(
        incidentId,
        workflowExecutionId,
        activityExecutionId: null,
        executableNodeId: null,
        IncidentSeverity.Error,
        status,
        IncidentResolutionAction.None,
        failureType: "System.Exception",
        message: message,
        createdAt: DateTimeOffset.UnixEpoch,
        resolvedAt: null);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);

    private sealed class InterceptingDocumentStore(IDocumentStore inner) : IDocumentStore
    {
        public Func<SaveDocumentRequest, Task>? OnBeforeSave { get; set; }
        public Func<DeleteDocumentRequest, Task>? OnBeforeDelete { get; set; }
        public DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public async Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
        {
            if (OnBeforeSave is { } hook)
            {
                OnBeforeSave = null;
                await hook(request);
            }

            return await inner.SaveAsync(request, cancellationToken);
        }

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public async Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
        {
            if (OnBeforeDelete is { } hook)
            {
                OnBeforeDelete = null;
                await hook(request);
            }

            return await inner.DeleteAsync(request, cancellationToken);
        }

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            inner is IBoundedDocumentStore boundedStore
                ? boundedStore.QueryAsync(query, cancellationToken)
                : throw new NotSupportedException();

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            inner is IBoundedDocumentStore boundedStore
                ? boundedStore.CountAsync(query, cancellationToken)
                : throw new NotSupportedException();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            inner is IBoundedDocumentStore boundedStore
                ? boundedStore.FirstOrDefaultAsync(query, cancellationToken)
                : throw new NotSupportedException();

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            inner is IBoundedDocumentStore boundedStore
                ? boundedStore.AnyAsync(query, cancellationToken)
                : throw new NotSupportedException();

        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            inner.BeginAsync(scope, cancellationToken);
    }
}

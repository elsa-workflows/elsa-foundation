using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using System.Text.Json;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

// Contract assertions for the document-shaped runtime store bridges. Each runs against two host-selected
// providers (real Groundwork SQLite and an in-memory document store). Identical behavior proves the
// bridges are provider-neutral, and round-tripping proves the runtime state survives serialization.
public sealed class GroundworkRuntimeStateStoreTests
{
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

        var forWf1 = await store.ListAsync("wf-1");
        Assert.Equal(new[] { "ae-1", "ae-2" }, forWf1.Select(x => x.Execution.ActivityExecutionId).OrderBy(x => x));
        Assert.Single(await store.ListAsync("wf-2"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task WorkflowExecutionState_RoundTrips_Replaces_And_Lists_All(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutionStateStore store = new GroundworkWorkflowExecutionStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(WorkflowState("wf-1", WorkflowExecutionStatus.Running));
        await store.SaveAsync(WorkflowState("wf-2", WorkflowExecutionStatus.Pending));
        await store.SaveAsync(WorkflowState("wf-1", WorkflowExecutionStatus.Completed));

        var found = await store.FindAsync("wf-1");
        Assert.NotNull(found);
        Assert.Equal(WorkflowExecutionStatus.Completed, found!.Status);
        Assert.Equal("artifact-wf-1", found.PinnedExecutable.ArtifactId);

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

        Assert.Equal(2, (await store.ListAsync("wf-1")).Count);
        Assert.True(await store.DeleteAsync("wf-1", "dv-1"));
        Assert.False(await store.DeleteAsync("wf-1", "dv-1"));
        Assert.Null(await store.FindAsync("wf-1", "dv-1"));
        Assert.Single(await store.ListAsync("wf-1"));
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
        var blocking = await store.ListBlockingAsync("wf-1");
        Assert.Equal(new[] { "inc-1", "inc-2" }, blocking.Select(x => x.IncidentId).OrderBy(x => x));
    }

    private static ActivityExecutionState ActivityState(string workflowExecutionId, string activityExecutionId) => new(
        new ActivityExecution(activityExecutionId, workflowExecutionId, $"node-{activityExecutionId}", "authored", "Elsa.Log", "1.0.0"),
        ActivityExecutionStatus.Running,
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

    private static WorkflowExecutionState WorkflowState(string workflowExecutionId, WorkflowExecutionStatus status) => new(
        workflowExecutionId,
        new WorkflowExecutableIdentity(
            ArtifactId: $"artifact-{workflowExecutionId}",
            DefinitionId: "definition-1",
            DefinitionVersionId: "version-1",
            ArtifactVersion: "1",
            ArtifactHash: $"hash-{workflowExecutionId}",
            Source: null),
        status,
        SubStatus: null,
        CreatedAt: DateTimeOffset.UnixEpoch,
        StartedAt: null,
        UpdatedAt: null,
        CompletedAt: null,
        CorrelationId: null,
        ParentWorkflowExecutionId: null,
        TenantId: null,
        SystemMetadata: new Dictionary<string, string>());

    private static DurableValueState DurableValue(string workflowExecutionId, string durableValueId) => new(
        durableValueId,
        workflowExecutionId,
        $"value-{durableValueId}",
        new RuntimeValueTypeDescriptor("int", null, null),
        DurableValueLifecycle.Instance,
        DurableValueStorage.Inline,
        Json("42"),
        externalReference: null,
        sourceActivityExecutionId: null,
        capturedAt: DateTimeOffset.UnixEpoch,
        metadata: new Dictionary<string, string>());

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

    private static IncidentState Incident(string workflowExecutionId, string incidentId, IncidentStatus status) => new(
        incidentId,
        workflowExecutionId,
        activityExecutionId: null,
        executableNodeId: null,
        IncidentSeverity.Error,
        status,
        IncidentResolutionAction.None,
        failureType: "System.Exception",
        message: "boom",
        createdAt: DateTimeOffset.UnixEpoch,
        resolvedAt: null);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);
}

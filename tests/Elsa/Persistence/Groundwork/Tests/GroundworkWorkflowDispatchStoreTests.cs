using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowDispatchStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Save_find_and_equivalent_replay_round_trip(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture);
        var record = Pending("parent-1", "activity-1");

        await store.SaveAsync(record);
        var replay = await store.SaveAsync(record);

        Assert.True(WorkflowDispatchLifecycle.RecordsEqual(record, replay));
        Assert.True(WorkflowDispatchLifecycle.RecordsEqual(record, (await store.FindAsync(record.DispatchId))!));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Legal_transition_is_saved_and_regression_is_rejected(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture);
        var pending = Pending("parent-1", "activity-1");
        var started = pending.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(1));
        var completed = started.TransitionTo(WorkflowDispatchStatus.Completed, Now.AddSeconds(2));

        await store.SaveAsync(pending);
        await store.SaveAsync(started);
        await store.SaveAsync(completed);

        Assert.Equal(WorkflowDispatchStatus.Completed, (await store.FindAsync(pending.DispatchId))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.SaveAsync(started));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Conflicting_immutable_context_is_rejected(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture);
        var pending = Pending("parent-1", "activity-1");
        await store.SaveAsync(pending);

        var conflicting = new WorkflowDispatchRecord(
            pending.DispatchId,
            pending.ParentWorkflowExecutionId,
            pending.ParentActivityExecutionId,
            pending.ChildWorkflowExecutionId,
            pending.ChildExecutable,
            pending.ChildSource,
            pending.Mode,
            WorkflowDispatchStatus.Started,
            "different-correlation",
            pending.TenantId,
            pending.Partition,
            pending.RunKind,
            pending.Authority,
            pending.InputDescriptors,
            pending.CreatedAt,
            Now.AddSeconds(1));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.SaveAsync(conflicting));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Retention_roots_include_all_distinct_nonterminal_child_artifacts(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture);
        var pending = Pending("parent-1", "activity-1");
        var startedPending = Pending("parent-2", "activity-2");
        var started = startedPending.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(1));
        var terminalPending = Pending("parent-3", "activity-3");
        var terminalStarted = terminalPending.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(1));
        var terminal = terminalStarted.TransitionTo(WorkflowDispatchStatus.Completed, Now.AddSeconds(2));

        await store.SaveAsync(pending);
        await store.SaveAsync(startedPending);
        await store.SaveAsync(started);
        await store.SaveAsync(terminalPending);
        await store.SaveAsync(terminalStarted);
        await store.SaveAsync(terminal);

        Assert.Equal(
            new[] { pending.ChildExecutable.ArtifactId, started.ChildExecutable.ArtifactId }.Order(StringComparer.Ordinal),
            await store.ListPinnedExecutableArtifactIdsAsync());
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Queries_by_parent_child_and_status_are_stable_and_bounded(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture);
        var first = Pending("parent-1", "activity-1");
        var second = Pending("parent-1", "activity-2");
        var third = Pending("parent-2", "activity-1");
        await store.SaveAsync(first);
        await store.SaveAsync(second);
        await store.SaveAsync(third);
        await store.SaveAsync(second.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(1)));

        var parent = await store.QueryAsync(new WorkflowDispatchQuery(parentWorkflowExecutionId: "parent-1", take: 1));
        var child = await store.QueryAsync(new WorkflowDispatchQuery(childWorkflowExecutionId: third.ChildWorkflowExecutionId));
        var status = await store.QueryAsync(new WorkflowDispatchQuery(status: WorkflowDispatchStatus.Started));

        Assert.Equal(first.DispatchId, Assert.Single(parent).DispatchId);
        Assert.Equal(third.DispatchId, Assert.Single(child).DispatchId);
        Assert.Equal(second.DispatchId, Assert.Single(status).DispatchId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Delete_is_idempotent(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture);
        var record = Pending("parent-1", "activity-1");
        await store.SaveAsync(record);

        await store.DeleteAsync(record.DispatchId);
        await store.DeleteAsync(record.DispatchId);

        Assert.Null(await store.FindAsync(record.DispatchId));
    }

    [Fact]
    public async Task Explicit_tenant_mismatch_fails_before_provider_io()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var dispatches = new GroundworkWorkflowDispatchStore(
            store,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.AccessContext("tenant-a"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dispatches.SaveAsync(Pending("parent-1", "activity-1", tenantId: "tenant-b")));

        Assert.Equal(0, store.LoadCount);
    }

    private static GroundworkWorkflowDispatchStore CreateStore(GroundworkDocumentStoreFixture fixture) => new(
        fixture.DocumentStore,
        GroundworkTestSerialization.Serializer,
        GroundworkTestAccess.DefaultAccessContextAccessor,
        fixture.BoundedDocumentStore);

    internal static WorkflowDispatchRecord Pending(
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        string? tenantId = null)
    {
        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, parentActivityExecutionId);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parentWorkflowExecutionId,
            parentActivityExecutionId,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity(
                $"artifact-{parentActivityExecutionId}",
                "definition-child",
                "version-child",
                "1",
                $"hash-{parentActivityExecutionId}"),
            new WorkflowExecutableSourceProvenance(
                $"source-{parentActivityExecutionId}",
                "WorkflowDefinitionVersion",
                "version-child",
                "1",
                "definition-child",
                "version-child",
                "1",
                "publication-child",
                "slot-child"),
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            correlationId: null,
            tenantId,
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot(parentWorkflowExecutionId, "initiator-1"),
            [new WorkflowDispatchInputDescriptor("orderId", "string")],
            Now,
            Now,
            new Dictionary<string, string> { ["safe-code"] = "dispatch" });
    }
}

using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

// Behavioral assertions for the document-backed post-commit outbox bridge. The bridge reproduces the
// authoritative in-memory lifecycle (pending -> delivered / retryable / final) durably, so the same
// assertions run against both the real Groundwork SQLite provider and the in-memory document store.
public sealed class GroundworkRuntimePostCommitOutboxStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SavePending_Then_GetDeliverable_Returns_Item(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1"));

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10));
        Assert.Equal(new[] { "item-1" }, deliverable.Select(x => x.OutboxItemId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SavePending_Is_Idempotent_For_Same_Intent(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1"));
        await store.SavePendingAsync(Pending("item-1", "wf-1")); // no throw

        Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Nested_scheduler_identity_beyond_portable_document_limit_round_trips(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var sourceWorkItemId = string.Join(':', Enumerable.Repeat("schedule:node-sequence:start:activity:invoke", 6));
        var targetWorkItemId = $"{sourceWorkItemId}:schedule-child:node-sync-endpoint:activity-scheduled";
        var commitId = $"commit:wf-1:{sourceWorkItemId}:activity-started";
        var intent = new RuntimePostCommitIntent(
            intentId: $"{sourceWorkItemId}:post-commit:{targetWorkItemId}",
            workflowExecutionId: "wf-1",
            kind: "scheduler",
            recordedAt: Now,
            activityExecutionId: "activity-1",
            idempotencyKey: null,
            payload: null);
        var logicalOutboxItemId = RuntimePostCommitOutboxItems.OutboxItemId(commitId, intent);
        Assert.True(logicalOutboxItemId.Length > 450, $"Expected the regression identity to exceed 450 code units, but observed {logicalOutboxItemId.Length}.");
        var pending = new RuntimePostCommitOutboxItem(
            logicalOutboxItemId,
            intent,
            RuntimePostCommitOutboxStatus.Pending,
            Now,
            Now);

        await store.SavePendingAsync(pending);
        await store.SavePendingAsync(pending);

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10));
        Assert.Equal(logicalOutboxItemId, Assert.Single(deliverable).OutboxItemId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Exact_lookup_uses_the_scoped_physical_identity_mapping(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var outboxItemId = new string('x', 451);
        await store.SavePendingAsync(Pending(outboxItemId, "wf-lookup"));

        var found = await ((IPostCommitOutboxLookupStore)store).FindAsync(outboxItemId);

        Assert.NotNull(found);
        Assert.Equal(outboxItemId, found.OutboxItemId);
        Assert.Equal("wf-lookup", found.Intent.WorkflowExecutionId);
        Assert.Null(await ((IPostCommitOutboxLookupStore)store).FindAsync("missing"));
    }

    [Theory]
    [InlineData(450)]
    [InlineData(451)]
    public async Task Portable_identity_boundary_round_trips(int identityLength)
    {
        await using var fixture = CreateStore("sqlite");
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var outboxItemId = new string('x', identityLength);

        await store.SavePendingAsync(Pending(outboxItemId, "wf-1"));

        Assert.Equal(outboxItemId, Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10))).OutboxItemId);
    }

    [Fact]
    public async Task Physical_alias_collision_fails_closed()
    {
        await using var fixture = CreateStore("sqlite");
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var longLogicalId = new string('x', 451);
        var collidingShortLogicalId = GroundworkPhysicalDocumentIdTestData.PhysicalAliasFor(longLogicalId);
        await store.SavePendingAsync(Pending(longLogicalId, "wf-long"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.SavePendingAsync(Pending(collidingShortLogicalId, "wf-short")));

        Assert.Contains("physical document identity collision", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SavePending_With_Conflicting_Intent_Throws(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1", kind: "a"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.SavePendingAsync(Pending("item-1", "wf-1", kind: "b")));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task RecordDelivered_Makes_Item_Terminal_And_Undeliverable(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1"));
        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.Delivered, Now));

        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10)));
        // A second result on a terminal item is rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.Delivered, Now)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task RetryableFailure_Becomes_Deliverable_After_Delay_Then_Final(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        var retry = new RuntimePostCommitRetryPolicy(maxAttempts: 2, delay: TimeSpan.FromMinutes(5));
        await store.SavePendingAsync(Pending("item-1", "wf-1", retryPolicy: retry));

        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.FailedRetryable, Now, "boom"));

        // Not yet available (within the retry delay).
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10)));
        // Available again after the delay window.
        var afterDelay = Now.AddMinutes(6);
        Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(afterDelay, 10)));

        // Exhausting the retry budget promotes the failure to final and removes it from the deliverable set.
        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.FailedRetryable, afterDelay, "boom again"));
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(afterDelay.AddMinutes(10), 10)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task GetDeliverable_Filters_By_Workflow_Execution(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1"));
        await store.SavePendingAsync(Pending("item-2", "wf-2"));

        var forWf1 = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10, workflowExecutionId: "wf-1"));
        Assert.Equal(new[] { "item-1" }, forWf1.Select(x => x.OutboxItemId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task GetDeliverable_Filters_By_Intent_Kind(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1", kind: "scheduler"));
        await store.SavePendingAsync(Pending("item-2", "wf-1", kind: "signal"));

        var schedulerItems = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10, intentKind: "scheduler"));
        Assert.Equal(new[] { "item-1" }, schedulerItems.Select(x => x.OutboxItemId));
    }

    [Fact]
    public async Task GetDeliverable_SelectsEarliestCandidateAcrossBoundedProviderPages()
    {
        await using var fixture = CreateStore("memory");
        var store = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore);
        for (var index = 0; index < 100; index++)
            await store.SavePendingAsync(PendingAt($"a-{index:D3}", Now.AddHours(1)));
        await store.SavePendingAsync(PendingAt("z-earliest", Now));

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now.AddHours(2), 1));

        Assert.Equal("z-earliest", Assert.Single(deliverable).OutboxItemId);
    }

    [Fact]
    public async Task Pending_Item_Survives_Restart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-outbox-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
                await store.SavePendingAsync(Pending("item-1", "wf-1"));
            }

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
                var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10));
                Assert.Equal(new[] { "item-1" }, deliverable.Select(x => x.OutboxItemId));
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Expired_claim_is_reclaimed_with_a_higher_fencing_token_and_stale_owner_is_rejected(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore);
        await store.SavePendingAsync(Pending("item-1", "wf-1"));

        var first = Assert.Single(await store.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-1", Now, TimeSpan.FromMinutes(1), 1)));
        var second = Assert.Single(await store.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-2", Now.AddMinutes(2), TimeSpan.FromMinutes(1), 1)));

        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, second.Item.Status);
        Assert.Equal(first.FencingToken + 1, second.FencingToken);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.RecordDeliveryResultAsync(
                first,
                new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(2))));

        await store.RecordDeliveryResultAsync(
            second,
            new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(2)));
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now.AddHours(1), 10)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Final_child_start_failure_atomically_completes_claim_and_projects_dispatch_failed(string provider)
    {
        await using var fixture = CreateStore(provider);
        var access = GroundworkTestAccess.DefaultAccessContextAccessor;
        var outbox = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore,
            access);
        var dispatches = new GroundworkWorkflowDispatchStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            access,
            fixture.BoundedDocumentStore);
        var pendingDispatch = GroundworkWorkflowDispatchStoreTests.Pending("parent-1", "activity-1");
        await dispatches.SaveAsync(pendingDispatch);
        await outbox.SavePendingAsync(PendingDispatch("item-1", pendingDispatch));
        var claim = Assert.Single(await outbox.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-1", Now, TimeSpan.FromMinutes(1), 1)));
        var failure = new RuntimePostCommitOutboxDeliveryResult(
            "item-1",
            RuntimePostCommitOutboxStatus.FailedRetryable,
            Now.AddSeconds(1),
            "classified-child-start-failure");

        await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            failure,
            pendingDispatch.TransitionToDispatchFailed(Now.AddSeconds(1))));

        Assert.Equal(
            WorkflowDispatchStatus.DispatchFailed,
            (await dispatches.FindAsync(pendingDispatch.DispatchId))!.Status);
        Assert.Empty(await outbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now.AddHours(1), 10)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Invalid_dispatch_projection_rolls_back_outbox_completion(string provider)
    {
        await using var fixture = CreateStore(provider);
        var access = GroundworkTestAccess.DefaultAccessContextAccessor;
        var outbox = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore,
            access);
        var dispatches = new GroundworkWorkflowDispatchStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            access,
            fixture.BoundedDocumentStore);
        var pendingDispatch = GroundworkWorkflowDispatchStoreTests.Pending("parent-1", "activity-1");
        await dispatches.SaveAsync(pendingDispatch);
        await outbox.SavePendingAsync(PendingDispatch("item-1", pendingDispatch));
        var claim = Assert.Single(await outbox.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-1", Now, TimeSpan.FromMinutes(1), 1)));
        var safeFailure = pendingDispatch.TransitionToDispatchFailed(Now.AddSeconds(1));
        var invalidProjection = new WorkflowDispatchRecord(
            pendingDispatch.DispatchId,
            pendingDispatch.ParentWorkflowExecutionId,
            pendingDispatch.ParentActivityExecutionId,
            pendingDispatch.ChildWorkflowExecutionId,
            pendingDispatch.ChildExecutable,
            pendingDispatch.ChildSource,
            pendingDispatch.Mode,
            WorkflowDispatchStatus.DispatchFailed,
            "conflicting-correlation",
            pendingDispatch.TenantId,
            pendingDispatch.Partition,
            pendingDispatch.RunKind,
            pendingDispatch.Authority,
            pendingDispatch.InputDescriptors,
            pendingDispatch.CreatedAt,
            Now.AddSeconds(1),
            safeFailure.Metadata);
        var failure = new RuntimePostCommitOutboxDeliveryResult(
            "item-1",
            RuntimePostCommitOutboxStatus.FailedRetryable,
            Now.AddSeconds(1),
            "classified-child-start-failure");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
                claim,
                failure,
                invalidProjection)));

        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatches.FindAsync(pendingDispatch.DispatchId))!.Status);
        await outbox.RecordDeliveryResultAsync(
            claim,
            new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(2)));
    }

    private static RuntimePostCommitOutboxItem Pending(
        string outboxItemId,
        string workflowExecutionId,
        string kind = "publish",
        RuntimePostCommitRetryPolicy? retryPolicy = null) => new(
        outboxItemId: outboxItemId,
        intent: new RuntimePostCommitIntent(
            intentId: $"intent-{outboxItemId}",
            workflowExecutionId: workflowExecutionId,
            kind: kind,
            recordedAt: Now,
            activityExecutionId: null,
            idempotencyKey: null,
            payload: null),
        status: RuntimePostCommitOutboxStatus.Pending,
        recordedAt: Now,
        availableAt: Now,
        retryPolicy: retryPolicy);

    private static RuntimePostCommitOutboxItem PendingAt(string outboxItemId, DateTimeOffset recordedAt) => new(
        outboxItemId,
        new RuntimePostCommitIntent(
            $"intent-{outboxItemId}",
            "wf-paged",
            "publish",
            recordedAt,
            activityExecutionId: null,
            idempotencyKey: null,
            payload: null),
        RuntimePostCommitOutboxStatus.Pending,
        recordedAt,
        Now);

    private static RuntimePostCommitOutboxItem PendingDispatch(
        string outboxItemId,
        WorkflowDispatchRecord dispatch) => new(
        outboxItemId,
        new RuntimePostCommitIntent(
            new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId).StartIntentId,
            dispatch.ParentWorkflowExecutionId,
            "elsa.dispatch-workflow.start-child.v1",
            Now,
            dispatch.ParentActivityExecutionId,
            new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId).StartIdempotencyKey,
            payload: null,
            metadata: new Dictionary<string, string> { ["runtime.dispatchId"] = dispatch.DispatchId }),
        RuntimePostCommitOutboxStatus.Pending,
        Now,
        Now,
        RuntimePostCommitRetryPolicy.None);

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);
}

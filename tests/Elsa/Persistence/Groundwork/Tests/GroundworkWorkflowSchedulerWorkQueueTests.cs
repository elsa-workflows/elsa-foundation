using System.Text.Json;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowSchedulerWorkQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    // The SAME contract assertions run against two host-selected providers (real Groundwork SQLite
    // and an in-memory document store). Identical behavior proves the bridge is provider-neutral.
    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Enqueue_List_Dequeue_PreservesFifoOrderPerWorkflowExecution(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));
        await queue.EnqueueAsync(NewWorkItem(3));
        await queue.EnqueueAsync(NewWorkItem(9, workflowExecutionId: "wfexec-2"));

        var listed = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
        Assert.Equal(new[] { "work-1", "work-2", "work-3" }, listed.Items.Select(item => item.WorkItemId));

        var limited = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1", limit: 2));
        Assert.Equal(new[] { "work-1", "work-2" }, limited.Items.Select(item => item.WorkItemId));

        Assert.Equal("work-1", (await queue.DequeueAsync("wfexec-1"))!.WorkItemId);
        Assert.Equal("work-2", (await queue.DequeueAsync("wfexec-1"))!.WorkItemId);
        Assert.Equal("work-3", (await queue.DequeueAsync("wfexec-1"))!.WorkItemId);
        Assert.Null(await queue.DequeueAsync("wfexec-1"));

        // The other workflow execution's queue is untouched.
        Assert.Equal("work-9", (await queue.DequeueAsync("wfexec-2"))!.WorkItemId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task List_returns_one_capped_cursor_page_in_deterministic_queue_order(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer);
        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));
        await queue.EnqueueAsync(NewWorkItem(3));

        var first = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1", limit: 2));
        var second = await queue.ListAsync(new RuntimeSchedulerWorkQuery(
            "wfexec-1",
            limit: 2,
            continuationToken: first.NextContinuationToken));

        Assert.Equal(["work-1", "work-2"], first.Items.Select(item => item.WorkItemId));
        Assert.Equal(["work-3"], second.Items.Select(item => item.WorkItemId));
        Assert.NotNull(first.NextContinuationToken);
        Assert.Null(second.NextContinuationToken);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1", limit: 501)).AsTask());
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Enqueue_IsIdempotentPerWorkflowExecutionAndWorkItemId(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        var first = NewWorkItem(1, commandId: "command-1");
        var duplicate = NewWorkItem(1, commandId: "command-duplicate");

        var enqueued = await queue.EnqueueAsync(first);
        var deduplicated = await queue.EnqueueAsync(duplicate);

        Assert.Equal("command-1", enqueued.CommandId);
        Assert.Equal("command-1", deduplicated.CommandId);
        var stored = Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items);
        Assert.Equal("command-1", stored.CommandId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Enqueue_ReEnqueuingANonHeadItem_KeepsQueueContentsAndOrderUnchanged(string provider)
    {
        // ADR 0031: redelivery de-duplication is a mandated queue-provider contract — re-enqueueing an item
        // already present for a workflow execution must add no duplicate and must not reorder the FIFO.
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));
        await queue.EnqueueAsync(NewWorkItem(3));

        // Redeliver the middle item with a different command payload; the original must win and stay in place.
        var redelivered = await queue.EnqueueAsync(NewWorkItem(2, commandId: "command-redelivered"));

        var items = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
        Assert.Equal(new[] { "work-1", "work-2", "work-3" }, items.Select(item => item.WorkItemId));
        Assert.Equal("command-2", redelivered.CommandId);
        Assert.Equal("command-2", items.ElementAt(1).CommandId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task WorkItemId_Beyond_Portable_Document_Limit_RoundTrips_And_Remains_Idempotent(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var workItemId = $"work:{new string('x', 450)}";
        Assert.True(workItemId.Length > 450, $"Expected the regression identity to exceed 450 code units, but observed {workItemId.Length}.");

        var first = NewWorkItem(1, workItemId: workItemId, commandId: "command-first");
        var duplicate = NewWorkItem(1, workItemId: workItemId, commandId: "command-duplicate");

        Assert.Equal("command-first", (await queue.EnqueueAsync(first)).CommandId);
        Assert.Equal("command-first", (await queue.EnqueueAsync(duplicate)).CommandId);
        Assert.Equal(workItemId, Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items).WorkItemId);
        Assert.Equal(workItemId, (await queue.DequeueAsync("wfexec-1"))!.WorkItemId);
        Assert.Null(await queue.DequeueAsync("wfexec-1"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Delete_RemovesWorkItemIdBeyondPortableDocumentLimit(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var workItemId = $"work:{new string('x', 450)}";

        await queue.EnqueueAsync(NewWorkItem(1, workItemId: workItemId));

        Assert.True(await queue.DeleteAsync("wfexec-1", workItemId));
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items);
        Assert.False(await queue.DeleteAsync("wfexec-1", workItemId));
    }

    [Fact]
    public async Task Physical_identity_collision_fails_closed()
    {
        await using var fixture = CreateStore("sqlite");
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var workItem = NewWorkItem(1, workItemId: $"work:{new string('x', 450)}");
        var logicalDocumentId = DocumentId.Compose(workItem.WorkflowExecutionId, workItem.WorkItemId);
        var physicalDocumentId = GroundworkPhysicalDocumentIdTestData.PhysicalAliasFor(logicalDocumentId);
        var wrongItem = NewWorkItem(2, workflowExecutionId: "wfexec-wrong", workItemId: "work-wrong");
        var wrongEnvelope = new
        {
            Collection = ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            WorkflowExecutionId = wrongItem.WorkflowExecutionId,
            Item = wrongItem
        };
        var (schemaVersion, content) = GroundworkTestSerialization.Serializer.Serialize(
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            wrongEnvelope);
        await fixture.DocumentStore.SaveAsync(new SaveDocumentRequest(
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            physicalDocumentId,
            schemaVersion,
            content));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await queue.EnqueueAsync(workItem));

        Assert.Contains("physical document identity collision", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_physical_identity_collision_fails_closed()
    {
        await using var fixture = CreateStore("sqlite");
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var workItem = NewWorkItem(1, workItemId: $"work:{new string('x', 450)}");
        var logicalDocumentId = DocumentId.Compose(workItem.WorkflowExecutionId, workItem.WorkItemId);
        var physicalDocumentId = GroundworkPhysicalDocumentIdTestData.PhysicalAliasFor(logicalDocumentId);
        var wrongItem = NewWorkItem(2, workflowExecutionId: "wfexec-wrong", workItemId: "work-wrong");
        var wrongEnvelope = new
        {
            Collection = ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            WorkflowExecutionId = wrongItem.WorkflowExecutionId,
            Item = wrongItem
        };
        var (schemaVersion, content) = GroundworkTestSerialization.Serializer.Serialize(
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            wrongEnvelope);
        await fixture.DocumentStore.SaveAsync(new SaveDocumentRequest(
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            physicalDocumentId,
            schemaVersion,
            content));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await queue.DeleteAsync(workItem.WorkflowExecutionId, workItem.WorkItemId));

        Assert.Contains("physical document identity collision", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task QueueSurvivesRestart_QueuedItemsRoundTripThroughNewBridgeInstance(string provider)
    {
        await using var fixture = CreateStore(provider);

        // First "process": enqueue and crash (bridge instance discarded, documents remain).
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));

        // Second "process": a fresh bridge over the same store sees the full backlog.
        IWorkflowSchedulerWorkQueue restarted = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var recovered = await restarted.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Equal(new[] { "work-1", "work-2" }, recovered.Items.Select(item => item.WorkItemId));
        var head = recovered.Items.First();
        Assert.Equal("command-1", head.CommandId);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, head.CommandKind);
        Assert.Equal("envelope-1", head.EnvelopeId);
        Assert.Equal("wfexec-1:command-1", head.IdempotencyKey);
        Assert.Equal(Now, head.EnqueuedAt);
        Assert.Equal(1, head.Sequence);
        Assert.Equal("work-1", head.Payload!.Value.GetProperty("workItemId").GetString());
        Assert.Equal("test", head.CommandMetadata["source"]);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ListPendingWorkflowExecutionIds_ReturnsDistinctOrderedBacklog(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        Assert.Empty(await queue.ListPendingWorkflowExecutionIdsAsync(10));

        await queue.EnqueueAsync(NewWorkItem(1, workflowExecutionId: "wfexec-b"));
        await queue.EnqueueAsync(NewWorkItem(2, workflowExecutionId: "wfexec-b"));
        await queue.EnqueueAsync(NewWorkItem(3, workflowExecutionId: "wfexec-a"));

        Assert.Equal(new[] { "wfexec-a", "wfexec-b" }, await queue.ListPendingWorkflowExecutionIdsAsync(10));
        Assert.Equal(new[] { "wfexec-a" }, await queue.ListPendingWorkflowExecutionIdsAsync(1));

        await queue.DequeueAsync("wfexec-a");
        Assert.Equal(new[] { "wfexec-b" }, await queue.ListPendingWorkflowExecutionIdsAsync(10));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => queue.ListPendingWorkflowExecutionIdsAsync(0).AsTask());
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ListPendingWorkflowExecutionIds_deduplicates_in_the_provider_before_applying_the_limit(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer);
        await queue.EnqueueAsync(NewWorkItem(1, workflowExecutionId: "wfexec-a"));
        await queue.EnqueueAsync(NewWorkItem(2, workflowExecutionId: "wfexec-a"));
        await queue.EnqueueAsync(NewWorkItem(3, workflowExecutionId: "wfexec-a"));
        await queue.EnqueueAsync(NewWorkItem(4, workflowExecutionId: "wfexec-b"));

        var pending = await queue.ListPendingWorkflowExecutionIdsAsync(2);

        Assert.Equal(["wfexec-a", "wfexec-b"], pending);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.ListPendingWorkflowExecutionIdsAsync(501).AsTask());
    }

    [Fact]
    public async Task ListPendingWorkflowExecutionIds_UsesOneBoundedOrderedProviderQuery()
    {
        await using var fixture = CreateStore("memory");
        var boundedStore = new EmptyRecordingBoundedDocumentStore();
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            boundedStore);

        Assert.Empty(await queue.ListPendingWorkflowExecutionIdsAsync(7));

        var query = Assert.Single(boundedStore.Queries);
        Assert.Equal(ElsaRuntimeStorageManifest.ListPendingSchedulerWorkflowExecutionsQuery, query.QueryIdentity);
        Assert.Equal(7, query.Take);
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(ElsaRuntimeStorageManifest.CollectionField, comparison.Path);
        Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
        Assert.Equal(
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            Assert.Single(comparison.Values));
        Assert.Collection(
            query.Order,
            workflow => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflow.Path),
            order => Assert.Equal(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField, order.Path));
        Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, query.LatestPerKeyPath);
        Assert.Null(query.Continuation);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Ids_WithSeparatorCharacters_DoNotCollide(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        // "a:b" + "c" and "a" + "b:c" would collide without id escaping.
        await queue.EnqueueAsync(NewWorkItem(1, workflowExecutionId: "a:b", workItemId: "c"));
        await queue.EnqueueAsync(NewWorkItem(2, workflowExecutionId: "a", workItemId: "b:c"));

        Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("a:b"))).Items);
        Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("a"))).Items);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ConcurrentClaim_GrantsExactlyOneOwnerAndKeepsLaterWorkBehindTheHead(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue first = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        IWorkflowSchedulerWorkQueue second = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        await first.EnqueueAsync(NewWorkItem(1));
        await first.EnqueueAsync(NewWorkItem(2));

        var claims = await Task.WhenAll(
            first.ClaimAsync(new("wfexec-1", "owner-a", Now, TimeSpan.FromMinutes(1))).AsTask(),
            second.ClaimAsync(new("wfexec-1", "owner-b", Now, TimeSpan.FromMinutes(1))).AsTask());

        var winner = Assert.Single(claims.Where(claim => claim is not null))!;
        Assert.Equal("work-1", winner.Item.WorkItemId);
        Assert.Null(await first.ClaimAsync(new("wfexec-1", "owner-c", Now.AddSeconds(1), TimeSpan.FromMinutes(1))));
        Assert.Equal(new[] { "work-1", "work-2" }, (await first.ListAsync(new("wfexec-1"))).Items.Select(item => item.WorkItemId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ExpiredClaim_IsReclaimedWithNewFence_AndStaleCompletionCannotRemoveSuccessorWork(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        await queue.EnqueueAsync(NewWorkItem(1));

        var first = await queue.ClaimAsync(new("wfexec-1", "owner-a", Now, TimeSpan.FromSeconds(10)));
        var second = await queue.ClaimAsync(new("wfexec-1", "owner-b", Now.AddSeconds(11), TimeSpan.FromMinutes(1)));
        Assert.NotNull(first);
        Assert.NotNull(second);

        Assert.True(second!.FencingToken > first!.FencingToken);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale, (await queue.CompleteClaimAsync(first)).Status);
        Assert.Single((await queue.ListAsync(new("wfexec-1"))).Items);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, (await queue.CompleteClaimAsync(second)).Status);
        Assert.Empty((await queue.ListAsync(new("wfexec-1"))).Items);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task RenewalAdvancesRevision_AndOnlyTheRefreshedClaimCanComplete(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        await queue.EnqueueAsync(NewWorkItem(1));
        var claim = await queue.ClaimAsync(new("wfexec-1", "owner-a", Now, TimeSpan.FromSeconds(10)));
        Assert.NotNull(claim);

        var renewal = await queue.RenewClaimAsync(claim!, Now.AddSeconds(5), TimeSpan.FromMinutes(1));
        var renewed = renewal.Claim;
        Assert.NotNull(renewed);

        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, renewal.Status);
        Assert.True(renewed!.Revision > claim.Revision);
        Assert.Equal(claim.FencingToken, renewed.FencingToken);
        Assert.Null(await queue.ClaimAsync(new("wfexec-1", "owner-b", Now.AddSeconds(11), TimeSpan.FromMinutes(1))));
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale, (await queue.CompleteClaimAsync(claim)).Status);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, (await queue.CompleteClaimAsync(renewed)).Status);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ReleaseCanDelayRetry_AndCompletionIsIdempotentAcrossBridgeRestart(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        await queue.EnqueueAsync(NewWorkItem(1));
        var claim = await queue.ClaimAsync(new("wfexec-1", "owner-a", Now, TimeSpan.FromMinutes(1)));
        Assert.NotNull(claim);

        Assert.Equal(
            RuntimeSchedulerWorkClaimTransitionStatus.Succeeded,
            (await queue.ReleaseClaimAsync(claim!, Now.AddMinutes(2))).Status);
        Assert.Null(await queue.ClaimAsync(new("wfexec-1", "owner-b", Now.AddMinutes(1), TimeSpan.FromMinutes(1))));

        IWorkflowSchedulerWorkQueue restarted = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var retried = await restarted.ClaimAsync(new("wfexec-1", "owner-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1)));
        Assert.NotNull(retried);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, (await restarted.CompleteClaimAsync(retried!)).Status);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.AlreadyApplied, (await queue.CompleteClaimAsync(retried)).Status);
    }

    private static RuntimeSchedulerWorkItem NewWorkItem(
        int index,
        string workflowExecutionId = "wfexec-1",
        string? commandId = null,
        string? workItemId = null)
    {
        using var document = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        return new(
            workItemId: workItemId ?? $"work-{index}",
            workflowExecutionId: workflowExecutionId,
            commandId: commandId ?? $"command-{index}",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"{workflowExecutionId}:command-{index}",
            enqueuedAt: Now,
            recordedAt: Now.AddMilliseconds(index),
            sequence: index,
            payload: document.RootElement.Clone(),
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" });
    }

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);
}

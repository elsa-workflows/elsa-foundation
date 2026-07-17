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
        Assert.Equal(new[] { "work-1", "work-2", "work-3" }, listed.Select(item => item.WorkItemId));

        var limited = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1", limit: 2));
        Assert.Equal(new[] { "work-1", "work-2" }, limited.Select(item => item.WorkItemId));

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
        var stored = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal("command-1", stored.CommandId);
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
        Assert.Equal(workItemId, Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).WorkItemId);
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
        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
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

        Assert.Equal(new[] { "work-1", "work-2" }, recovered.Select(item => item.WorkItemId));
        var head = recovered.First();
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
    public async Task Ids_WithSeparatorCharacters_DoNotCollide(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerWorkQueue queue = new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        // "a:b" + "c" and "a" + "b:c" would collide without id escaping.
        await queue.EnqueueAsync(NewWorkItem(1, workflowExecutionId: "a:b", workItemId: "c"));
        await queue.EnqueueAsync(NewWorkItem(2, workflowExecutionId: "a", workItemId: "b:c"));

        Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("a:b")));
        Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("a")));
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

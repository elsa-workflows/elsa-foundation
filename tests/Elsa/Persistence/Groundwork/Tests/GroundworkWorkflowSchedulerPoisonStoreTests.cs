using System.Text.Json;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowSchedulerPoisonStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Record_Find_List_RoundTripsPerWorkflowExecution(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerPoisonStore store = new GroundworkWorkflowSchedulerPoisonStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.RecordAsync(NewRecord(2));
        await store.RecordAsync(NewRecord(1));
        await store.RecordAsync(NewRecord(9, workflowExecutionId: "wfexec-2"));

        var found = await store.FindAsync("wfexec-1", "work-1");
        Assert.NotNull(found);
        Assert.Equal("handler-1", found.HandlerName);
        Assert.Equal("boom-1", found.Fault.Message);
        Assert.Equal(new RuntimeFaultInfo("System.ArgumentException", "inner-1"), found.InnerFault);
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, found.Disposition);
        Assert.Equal("test", found.Metadata["source"]);

        var listed = await store.ListAsync("wfexec-1");
        Assert.Equal(new[] { "work-1", "work-2" }, listed.Select(record => record.WorkItemId));
        Assert.Equal("work-9", Assert.Single(await store.ListAsync("wfexec-2")).WorkItemId);
        Assert.Null(await store.FindAsync("wfexec-1", "missing"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Record_ReplacesExistingPoisonRecordForSameWorkItem(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerPoisonStore store = new GroundworkWorkflowSchedulerPoisonStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.RecordAsync(NewRecord(1, message: "first", failureCount: 1));
        await store.RecordAsync(NewRecord(1, message: "second", failureCount: 2));

        var found = await store.FindAsync("wfexec-1", "work-1");
        Assert.NotNull(found);
        Assert.Equal("second", found.Fault.Message);
        Assert.Equal(2, found.FailureCount);
        Assert.Single(await store.ListAsync("wfexec-1"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task StoreSurvivesRestart_RecordsRoundTripThroughNewBridgeInstance(string provider)
    {
        await using var fixture = CreateStore(provider);

        IWorkflowSchedulerPoisonStore firstProcess = new GroundworkWorkflowSchedulerPoisonStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        await firstProcess.RecordAsync(NewRecord(1, disposition: RuntimeSchedulerPoisonDisposition.RetryScheduled, nextRetryOffset: TimeSpan.FromMinutes(5)));

        IWorkflowSchedulerPoisonStore restarted = new GroundworkWorkflowSchedulerPoisonStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var recovered = await restarted.FindAsync("wfexec-1", "work-1");

        Assert.NotNull(recovered);
        Assert.Equal(RuntimeSchedulerPoisonDisposition.RetryScheduled, recovered.Disposition);
        Assert.Equal(Now.AddMinutes(5), recovered.NextRetryAt);
        Assert.Equal("System.InvalidOperationException", recovered.Fault.ExceptionType);
    }

    [Fact]
    public async Task Physical_identity_collision_fails_closed()
    {
        await using var fixture = CreateStore("sqlite");
        IWorkflowSchedulerPoisonStore store = new GroundworkWorkflowSchedulerPoisonStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var record = NewRecord(1, workItemId: $"work:{new string('x', 450)}");
        var logicalDocumentId = DocumentId.Compose(record.WorkflowExecutionId, record.WorkItemId);
        var physicalDocumentId = GroundworkPhysicalDocumentIdTestData.PhysicalAliasFor(logicalDocumentId);
        var wrongRecord = NewRecord(2, workflowExecutionId: "wfexec-wrong", workItemId: "work-wrong");
        var wrongEnvelope = new
        {
            Collection = ElsaRuntimeStorageManifest.SchedulerPoisonDocumentKind,
            WorkflowExecutionId = wrongRecord.WorkflowExecutionId,
            Record = wrongRecord
        };
        var (schemaVersion, content) = GroundworkTestSerialization.Serializer.Serialize(
            ElsaRuntimeStorageManifest.SchedulerPoisonDocumentKind,
            wrongEnvelope);
        await fixture.DocumentStore.SaveAsync(new SaveDocumentRequest(
            ElsaRuntimeStorageManifest.SchedulerPoisonDocumentKind,
            physicalDocumentId,
            schemaVersion,
            content));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.FindAsync(record.WorkflowExecutionId, record.WorkItemId));

        Assert.Contains("physical document identity collision", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Ids_WithSeparatorCharacters_DoNotCollide(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowSchedulerPoisonStore store = new GroundworkWorkflowSchedulerPoisonStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.RecordAsync(NewRecord(1, workflowExecutionId: "a:b", workItemId: "c"));
        await store.RecordAsync(NewRecord(2, workflowExecutionId: "a", workItemId: "b:c"));

        Assert.Single(await store.ListAsync("a:b"));
        Assert.Single(await store.ListAsync("a"));
    }

    private static RuntimeSchedulerPoisonRecord NewRecord(
        int index,
        string workflowExecutionId = "wfexec-1",
        string? workItemId = null,
        string? message = null,
        int? failureCount = null,
        RuntimeSchedulerPoisonDisposition disposition = RuntimeSchedulerPoisonDisposition.Poisoned,
        TimeSpan? nextRetryOffset = null)
    {
        using var payload = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        return new(
            workflowExecutionId,
            workItemId ?? $"work-{index}",
            WorkflowExecutionCommandKind.RunSchedulerWork,
            $"handler-{index}",
            new RuntimeFaultInfo("System.InvalidOperationException", message ?? $"boom-{index}", "stack"),
            failureCount ?? index,
            disposition,
            Now.AddMilliseconds(index),
            Now.AddMilliseconds(index * 2),
            disposition == RuntimeSchedulerPoisonDisposition.RetryScheduled
                ? Now + (nextRetryOffset ?? TimeSpan.FromMinutes(1))
                : null,
            new Dictionary<string, string>
            {
                ["source"] = "test",
                ["payload"] = payload.RootElement.GetProperty("workItemId").GetString()!
            },
            innerFault: new RuntimeFaultInfo("System.ArgumentException", $"inner-{index}"));
    }

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);
}

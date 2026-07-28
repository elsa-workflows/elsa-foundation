using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Regression coverage for the fixture adapter's scheduler-work keyset continuations: pages must
/// resume strictly after the last returned row instead of re-applying an offset over the re-run
/// live query, so concurrent inserts before (or removals behind) the page boundary neither
/// re-serve nor skip rows.
/// </summary>
public sealed class GroundworkDocumentStoreFixtureContinuationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SchedulerWorkPage_DoesNotReServeRowsWhenAnEarlierItemIsInsertedBetweenPages()
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("memory");
        var queue = NewQueue(fixture);
        await queue.EnqueueAsync(NewWorkItem(2));
        await queue.EnqueueAsync(NewWorkItem(4));

        var first = await fixture.BoundedDocumentStore.QueryAsync(SchedulerWorkPageQuery(take: 1));
        // Sorts before the page boundary; offset paging would re-serve work-2 on the second page.
        await queue.EnqueueAsync(NewWorkItem(1));
        var second = await fixture.BoundedDocumentStore.QueryAsync(
            SchedulerWorkPageQuery(take: 1, continuation: first.NextContinuation));

        Assert.Equal(["work-2"], first.Documents.Select(WorkItemIdOf));
        Assert.Equal(["work-4"], second.Documents.Select(WorkItemIdOf));
        Assert.NotNull(first.NextContinuation);
        Assert.Null(second.NextContinuation);
    }

    [Fact]
    public async Task SchedulerWorkPage_DoesNotSkipRowsWhenAServedItemIsRemovedBetweenPages()
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("memory");
        var queue = NewQueue(fixture);
        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));
        await queue.EnqueueAsync(NewWorkItem(3));

        var first = await fixture.BoundedDocumentStore.QueryAsync(SchedulerWorkPageQuery(take: 1));
        // Consuming the already-served head shrinks the result set; offset paging would skip work-2.
        Assert.True(await queue.DeleteAsync("wfexec-1", "work-1"));
        var second = await fixture.BoundedDocumentStore.QueryAsync(
            SchedulerWorkPageQuery(take: 1, continuation: first.NextContinuation));

        Assert.Equal(["work-1"], first.Documents.Select(WorkItemIdOf));
        Assert.Equal(["work-2"], second.Documents.Select(WorkItemIdOf));
        Assert.NotNull(second.NextContinuation);
    }

    [Fact]
    public async Task PendingSchedulerExecutionsPage_DoesNotReServeRowsWhenAnEarlierExecutionAppearsBetweenPages()
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("memory");
        var queue = NewQueue(fixture);
        await queue.EnqueueAsync(NewWorkItem(1, workflowExecutionId: "wfexec-b"));
        await queue.EnqueueAsync(NewWorkItem(2, workflowExecutionId: "wfexec-c"));

        var first = await fixture.BoundedDocumentStore.QueryAsync(PendingExecutionsPageQuery(take: 1));
        // Sorts before the page boundary; offset paging would re-serve wfexec-b on the second page.
        await queue.EnqueueAsync(NewWorkItem(3, workflowExecutionId: "wfexec-a"));
        var second = await fixture.BoundedDocumentStore.QueryAsync(
            PendingExecutionsPageQuery(take: 1, continuation: first.NextContinuation));

        Assert.Equal(["wfexec-b"], first.Documents.Select(WorkflowExecutionIdOf));
        Assert.Equal(["wfexec-c"], second.Documents.Select(WorkflowExecutionIdOf));
        Assert.NotNull(first.NextContinuation);
        Assert.Null(second.NextContinuation);
    }

    [Fact]
    public async Task SchedulerWorkPage_RejectsForeignAndMalformedContinuations()
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("memory");
        var queue = NewQueue(fixture);
        await queue.EnqueueAsync(NewWorkItem(1));
        await queue.EnqueueAsync(NewWorkItem(2));

        var schedulerWorkPage = await fixture.BoundedDocumentStore.QueryAsync(SchedulerWorkPageQuery(take: 1));
        var schedulerWorkPrefix =
            $"groundwork-test:{ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind}:{ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery}:";

        // A scheduler-work token does not resume the pending-executions query.
        await Assert.ThrowsAsync<InvalidDocumentQueryContinuationException>(() =>
            fixture.BoundedDocumentStore.QueryAsync(
                PendingExecutionsPageQuery(take: 1, continuation: schedulerWorkPage.NextContinuation)));
        // Non-base64 payloads and wrong keyset shapes fail closed.
        await Assert.ThrowsAsync<InvalidDocumentQueryContinuationException>(() =>
            fixture.BoundedDocumentStore.QueryAsync(
                SchedulerWorkPageQuery(take: 1, continuation: $"{schedulerWorkPrefix}not-base64!")));
        await Assert.ThrowsAsync<InvalidDocumentQueryContinuationException>(() =>
            fixture.BoundedDocumentStore.QueryAsync(SchedulerWorkPageQuery(
                take: 1,
                continuation: schedulerWorkPrefix + Convert.ToBase64String("single-field"u8))));
    }

    private static IWorkflowSchedulerWorkQueue NewQueue(GroundworkDocumentStoreFixture fixture) =>
        new GroundworkWorkflowSchedulerWorkQueue(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

    private static DocumentQuery SchedulerWorkPageQuery(int take, string? continuation = null) =>
        new(
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
            [
                DocumentQueryClause.Of(DocumentQueryComparison.StartsWith(
                    ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField,
                    $"{StableHash("wfexec-1")}."))
            ],
            take: take,
            continuation: continuation);

    private static DocumentQuery PendingExecutionsPageQuery(int take, string? continuation = null) =>
        new(
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            ElsaRuntimeStorageManifest.ListPendingSchedulerWorkflowExecutionsQuery,
            [
                DocumentQueryClause.Of(DocumentQueryComparison.StartsWith(
                    ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                    "wfexec-"))
            ],
            take: take,
            continuation: continuation);

    private static string WorkItemIdOf(DocumentEnvelope document)
    {
        using var content = JsonDocument.Parse(document.ContentJson);
        return content.RootElement.GetProperty("item").GetProperty("workItemId").GetString()!;
    }

    private static string WorkflowExecutionIdOf(DocumentEnvelope document)
    {
        using var content = JsonDocument.Parse(document.ContentJson);
        return content.RootElement.GetProperty(ElsaRuntimeStorageManifest.WorkflowExecutionIdField).GetString()!;
    }

    private static string StableHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static RuntimeSchedulerWorkItem NewWorkItem(int index, string workflowExecutionId = "wfexec-1")
    {
        using var document = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        return new(
            workItemId: $"work-{index}",
            workflowExecutionId: workflowExecutionId,
            commandId: $"command-{index}",
            commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"{workflowExecutionId}:command-{index}",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: index,
            payload: document.RootElement.Clone());
    }
}

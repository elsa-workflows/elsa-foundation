using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeBookmarkStateStoreTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InMemoryBookmarkStateStore_SavesFindsListsAndDeletesByWorkflowExecution()
    {
        var store = new InMemoryBookmarkStateStore();
        var first = NewBookmark("bookmark-1", "wfexec-1", "actexec-1");
        var second = NewBookmark("bookmark-2", "wfexec-1", "actexec-2");
        var otherWorkflow = NewBookmark("bookmark-1", "wfexec-2", "actexec-3");

        await store.SaveAsync(first);
        await store.SaveAsync(second);
        await store.SaveAsync(otherWorkflow);

        Assert.Equal(first, await store.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Equal(2, (await store.ListAsync("wfexec-1")).Count);

        Assert.True(await store.DeleteAsync("wfexec-1", "bookmark-1"));
        Assert.Null(await store.FindAsync("wfexec-1", "bookmark-1"));
        Assert.NotNull(await store.FindAsync("wfexec-2", "bookmark-1"));
        Assert.False(await store.DeleteAsync("wfexec-1", "bookmark-1"));
    }

    private BookmarkState NewBookmark(string bookmarkId, string workflowExecutionId, string activityExecutionId) =>
        new(
            BookmarkId: bookmarkId,
            WorkflowExecutionId: workflowExecutionId,
            ActivityExecutionId: activityExecutionId,
            ExecutableNodeId: "node-1",
            ResumeTargetId: "node-resume-1",
            StimulusType: "delivery-status",
            StimulusHash: $"sha256:delivery-status:{workflowExecutionId}:{bookmarkId}",
            Payload: Json("""{"expected":"delivered"}"""),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: _now,
            ExpiresAt: null);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

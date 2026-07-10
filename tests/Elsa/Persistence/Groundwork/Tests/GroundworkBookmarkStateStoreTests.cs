using System.Text.Json;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkBookmarkStateStoreTests
{
    // The SAME contract assertions run against two host-selected providers (real Groundwork SQLite
    // and an in-memory document store). Identical behavior proves the bridge is provider-neutral.
    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task RoundTrips_Across_Providers(string provider)
    {
        await using var fixture = CreateStore(provider);
        IBookmarkStateStore store = new GroundworkBookmarkStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        var a1 = Bookmark("wf-1", "bm-a", stimulus: "Http", payload: new { url = "/orders" });
        var a2 = Bookmark("wf-1", "bm-b", stimulus: "Timer");
        var b1 = Bookmark("wf-2", "bm-a", stimulus: "Signal");

        await store.SaveAsync(a1);
        await store.SaveAsync(a2);
        await store.SaveAsync(b1);

        var found = await store.FindAsync("wf-1", "bm-a");
        Assert.NotNull(found);
        Assert.Equal("Http", found!.StimulusType);
        Assert.Equal("bm-a", found.BookmarkId);
        Assert.Equal("wf-1", found.WorkflowExecutionId);
        Assert.Equal("/orders", found.Payload!.Value.GetProperty("url").GetString());
        Assert.Equal("v1", found.Metadata["tag"]);

        var wf1 = await store.ListAsync("wf-1");
        Assert.Equal(2, wf1.Count);
        Assert.Equal(new[] { "bm-a", "bm-b" }, wf1.Select(s => s.BookmarkId).OrderBy(x => x));

        var wf2 = await store.ListAsync("wf-2");
        Assert.Single(wf2);
        Assert.Equal("bm-a", wf2.Single().BookmarkId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Save_Replaces_Existing_State(string provider)
    {
        await using var fixture = CreateStore(provider);
        IBookmarkStateStore store = new GroundworkBookmarkStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Bookmark("wf-1", "bm-a", stimulus: "Http"));
        await store.SaveAsync(Bookmark("wf-1", "bm-a", stimulus: "Timer"));

        var found = await store.FindAsync("wf-1", "bm-a");
        Assert.Equal("Timer", found!.StimulusType);
        Assert.Single(await store.ListAsync("wf-1"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Delete_Removes_State_And_Reports_Existence(string provider)
    {
        await using var fixture = CreateStore(provider);
        IBookmarkStateStore store = new GroundworkBookmarkStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Bookmark("wf-1", "bm-a", stimulus: "Http"));

        Assert.True(await store.DeleteAsync("wf-1", "bm-a"));
        Assert.False(await store.DeleteAsync("wf-1", "bm-a"));
        Assert.Null(await store.FindAsync("wf-1", "bm-a"));
        Assert.Empty(await store.ListAsync("wf-1"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Find_Returns_Null_When_Absent(string provider)
    {
        await using var fixture = CreateStore(provider);
        IBookmarkStateStore store = new GroundworkBookmarkStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        Assert.Null(await store.FindAsync("missing", "missing"));
        Assert.Empty(await store.ListAsync("missing"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ListByStimulusType_ReturnsEveryBookmarkOfType_AcrossExecutionsAndHashes(string provider)
    {
        // Spec 089 D (T004a): the Groundwork type-scoped scan returns every bookmark of a stimulus type regardless
        // of hash/execution, narrowing out other types — mirroring the sibling trigger-binding-store by-type scan.
        await using var fixture = CreateStore(provider);
        IBookmarkStimulusIndex index = new GroundworkBookmarkStateStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var store = (IBookmarkStateStore)index;

        await store.SaveAsync(Bookmark("wf-1", "bm-a", stimulus: "HttpEndpoint", stimulusHash: "h1"));
        await store.SaveAsync(Bookmark("wf-2", "bm-b", stimulus: "HttpEndpoint", stimulusHash: "h2"));
        await store.SaveAsync(Bookmark("wf-3", "bm-c", stimulus: "Event", stimulusHash: "h3"));

        var http = await index.ListByStimulusTypeAsync("HttpEndpoint");
        Assert.Equal(new[] { "bm-a", "bm-b" }, http.Select(b => b.BookmarkId).OrderBy(x => x));

        Assert.Empty(await index.ListByStimulusTypeAsync("Signal"));
    }

    private static BookmarkState Bookmark(string workflowExecutionId, string bookmarkId, string stimulus, object? payload = null, string stimulusHash = "hash-1")
    {
        JsonElement? payloadElement = payload is null ? null : JsonSerializer.SerializeToElement(payload);
        return new BookmarkState(
            bookmarkId,
            workflowExecutionId,
            ActivityExecutionId: "ae-1",
            ExecutableNodeId: "node-1",
            ResumeTargetId: "resume-1",
            StimulusType: stimulus,
            StimulusHash: stimulusHash,
            Payload: payloadElement,
            Metadata: new Dictionary<string, string> { ["tag"] = "v1" },
            CreatedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null);
    }

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);
}

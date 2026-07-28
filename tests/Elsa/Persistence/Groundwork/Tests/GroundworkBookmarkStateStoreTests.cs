using System.Text.Json;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
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
        IBookmarkStateStore store = CreateBridge(fixture);

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

        var wf1 = await store.ListAllBookmarkStatesAsync("wf-1");
        Assert.Equal(2, wf1.Count);
        Assert.Equal(new[] { "bm-a", "bm-b" }, wf1.Select(s => s.BookmarkId).OrderBy(x => x));

        var wf2 = await store.ListAllBookmarkStatesAsync("wf-2");
        Assert.Single(wf2);
        Assert.Equal("bm-a", wf2.Single().BookmarkId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Save_Replaces_Existing_State(string provider)
    {
        await using var fixture = CreateStore(provider);
        IBookmarkStateStore store = CreateBridge(fixture);

        await store.SaveAsync(Bookmark("wf-1", "bm-a", stimulus: "Http"));
        await store.SaveAsync(Bookmark("wf-1", "bm-a", stimulus: "Timer"));

        var found = await store.FindAsync("wf-1", "bm-a");
        Assert.Equal("Timer", found!.StimulusType);
        Assert.Single(await store.ListAllBookmarkStatesAsync("wf-1"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Delete_Removes_State_And_Reports_Existence(string provider)
    {
        await using var fixture = CreateStore(provider);
        IBookmarkStateStore store = CreateBridge(fixture);

        await store.SaveAsync(Bookmark("wf-1", "bm-a", stimulus: "Http"));

        Assert.True(await store.DeleteAsync("wf-1", "bm-a"));
        Assert.False(await store.DeleteAsync("wf-1", "bm-a"));
        Assert.Null(await store.FindAsync("wf-1", "bm-a"));
        Assert.Empty(await store.ListAllBookmarkStatesAsync("wf-1"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Find_Returns_Null_When_Absent(string provider)
    {
        await using var fixture = CreateStore(provider);
        IBookmarkStateStore store = CreateBridge(fixture);

        Assert.Null(await store.FindAsync("missing", "missing"));
        Assert.Empty(await store.ListAllBookmarkStatesAsync("missing"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ListByStimulusType_ReturnsEveryBookmarkOfType_AcrossExecutionsAndHashes(string provider)
    {
        // Spec 089 D (T004a): the Groundwork type-scoped scan returns every bookmark of a stimulus type regardless
        // of hash/execution, narrowing out other types — mirroring the sibling trigger-binding-store by-type scan.
        await using var fixture = CreateStore(provider);
        IBookmarkStimulusIndex index = CreateBridge(fixture);
        var store = (IBookmarkStateStore)index;

        await store.SaveAsync(Bookmark("wf-1", "bm-a", stimulus: "HttpEndpoint", stimulusHash: "h1"));
        await store.SaveAsync(Bookmark("wf-2", "bm-b", stimulus: "HttpEndpoint", stimulusHash: "h2"));
        await store.SaveAsync(Bookmark("wf-3", "bm-c", stimulus: "Event", stimulusHash: "h3"));

        var first = await index.ListByStimulusTypePageAsync(
            new BookmarkStimulusTypePageQuery("HttpEndpoint", limit: 1));
        var second = await index.ListByStimulusTypePageAsync(
            new BookmarkStimulusTypePageQuery(
                "HttpEndpoint",
                limit: 1,
                continuationToken: first.NextContinuationToken));
        var http = first.Items.Concat(second.Items).ToArray();
        Assert.Equal(new[] { "bm-a", "bm-b" }, http.Select(b => b.BookmarkId).OrderBy(x => x));
        Assert.NotNull(first.NextContinuationToken);
        Assert.Null(second.NextContinuationToken);

        Assert.Empty(await index.ListAllByStimulusTypeAsync("Signal"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ListByStimulus_ReturnsOnlyMatchingTypeAndHash_AcrossExecutions(string provider)
    {
        await using var fixture = CreateStore(provider);
        IBookmarkStimulusIndex index = CreateBridge(fixture);
        var store = (IBookmarkStateStore)index;

        await store.SaveAsync(Bookmark("wf-1", "bm-a", stimulus: "HttpEndpoint", stimulusHash: "shared"));
        await store.SaveAsync(Bookmark("wf-2", "bm-b", stimulus: "Signal", stimulusHash: "shared"));
        await store.SaveAsync(Bookmark("wf-3", "bm-c", stimulus: "HttpEndpoint", stimulusHash: "other"));

        var bookmarks = await index.ListAllByStimulusAsync("HttpEndpoint", "shared");

        Assert.Equal("bm-a", Assert.Single(bookmarks).BookmarkId);
    }

    [Fact]
    public async Task ListByStimulus_UsesDeclaredCompositeRoute()
    {
        await using var fixture = CreateStore("memory");
        var queries = new RecordingBoundedDocumentStore();
        IBookmarkStimulusIndex index = new GroundworkBookmarkStateStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            queries);

        await index.ListByStimulusPageAsync(
            new BookmarkStimulusPageQuery("HttpEndpoint", "hash-1", limit: 17));

        var query = Assert.Single(queries.Observed);
        Assert.Equal(ElsaRuntimeStorageManifest.BookmarkStateDocumentKind, query.DocumentKind);
        Assert.Equal(ElsaRuntimeStorageManifest.ListBookmarksByStimulusAndTypeQuery, query.QueryIdentity);
        Assert.Collection(
            query.Clauses.SelectMany(clause => clause.Comparisons),
            stimulus =>
            {
                Assert.Equal(
                    ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyField,
                    stimulus.Path);
                Assert.Equal(QueryComparisonOperator.Equal, stimulus.Operator);
                Assert.Equal(
                    "e9e2ec447250dc1aabece4a962f95d1259036ff2f3d858f254eb7a90d14fc973",
                    Assert.Single(stimulus.Values));
            });
        Assert.Equal(17, query.Take);
        Assert.Collection(
            query.Order,
            workflow => Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflow.Path),
            bookmark => Assert.Equal(ElsaRuntimeStorageManifest.BookmarkIdField, bookmark.Path));
        Assert.Null(query.Skip);
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
        GroundworkDocumentStoreFixture.Create(
            provider,
            new RuntimeGroundworkStorageManifestSource()
                .CreateDeclarationAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult()
                .Manifest);

    private static GroundworkBookmarkStateStore CreateBridge(GroundworkDocumentStoreFixture fixture) =>
        new(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore);

    private sealed class RecordingBoundedDocumentStore : IBoundedDocumentStore
    {
        public List<DocumentQuery> Observed { get; } = [];

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            Observed.Add(query);
            return Task.FromResult(new DocumentQueryResult([], 0));
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

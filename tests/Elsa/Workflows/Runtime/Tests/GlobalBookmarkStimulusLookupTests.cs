using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class GlobalBookmarkStimulusLookupTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FindWaiting_ReturnsMatchesAcrossExecutions_OrderedDeterministically()
    {
        var store = new InMemoryBookmarkStateStore();
        var lookup = new GlobalBookmarkStimulusLookup(store);
        await store.SaveAsync(Bookmark("bk-b", executionId: "wfexec-2", createdAt: _now.AddMinutes(-1)));
        await store.SaveAsync(Bookmark("bk-a", executionId: "wfexec-1", createdAt: _now.AddMinutes(-2)));
        await store.SaveAsync(Bookmark("bk-other-type", executionId: "wfexec-3", stimulusType: "other"));

        var result = await lookup.FindWaitingAsync(Request());

        // wfexec-1 created earlier sorts before wfexec-2; the other-type bookmark is filtered out entirely.
        Assert.Equal(["wfexec-1", "wfexec-2"], result.WorkflowExecutionIds);
        Assert.Equal(["bk-a", "bk-b"], result.Matches.Select(match => match.BookmarkId));
    }

    [Fact]
    public async Task FindWaiting_ExcludesExpiredBookmarks()
    {
        var store = new InMemoryBookmarkStateStore();
        var lookup = new GlobalBookmarkStimulusLookup(store);
        await store.SaveAsync(Bookmark("bk-expired", executionId: "wfexec-1", expiresAt: _now.AddSeconds(-1)));
        await store.SaveAsync(Bookmark("bk-live", executionId: "wfexec-2", expiresAt: _now.AddMinutes(5)));

        var result = await lookup.FindWaitingAsync(Request());

        Assert.Equal(["wfexec-2"], result.WorkflowExecutionIds);
    }

    [Fact]
    public async Task FindWaiting_WhenCorrelationSupplied_ReturnsOnlyMatchingCorrelation()
    {
        var store = new InMemoryBookmarkStateStore();
        var lookup = new GlobalBookmarkStimulusLookup(store);
        await store.SaveAsync(Bookmark("bk-corr", executionId: "wfexec-1", correlationId: "order-7"));
        await store.SaveAsync(Bookmark("bk-nocorr", executionId: "wfexec-2"));
        await store.SaveAsync(Bookmark("bk-othercorr", executionId: "wfexec-3", correlationId: "order-9"));

        var result = await lookup.FindWaitingAsync(Request(correlationId: "order-7"));

        Assert.Equal(["wfexec-1"], result.WorkflowExecutionIds);
    }

    [Fact]
    public async Task FindWaiting_WhenNoCorrelationSupplied_IgnoresCorrelationMetadata()
    {
        var store = new InMemoryBookmarkStateStore();
        var lookup = new GlobalBookmarkStimulusLookup(store);
        await store.SaveAsync(Bookmark("bk-corr", executionId: "wfexec-1", correlationId: "order-7"));
        await store.SaveAsync(Bookmark("bk-nocorr", executionId: "wfexec-2"));

        var result = await lookup.FindWaitingAsync(Request());

        Assert.Equal(["wfexec-1", "wfexec-2"], result.WorkflowExecutionIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task FindWaitingByType_ReturnsSnapshotsWithMetadata_AcrossHashesAndExecutions()
    {
        // Spec 089 D (T004b): the type-scoped lookup returns waiting-bookmark SNAPSHOTS (incl. Metadata) for a
        // stimulus type regardless of hash — the mid-flow HttpEndpoint route-table resolver / middleware read the
        // durable route template + endpoint options off the returned Metadata.
        var store = new InMemoryBookmarkStateStore();
        var lookup = new GlobalBookmarkStimulusLookup(store);
        await store.SaveAsync(Bookmark("bk-a", executionId: "wfexec-1", stimulusType: "HttpEndpoint", stimulusHash: "sha256:http:/a:GET", createdAt: _now.AddMinutes(-2), metadata: new() { ["http:template"] = "/a" }));
        await store.SaveAsync(Bookmark("bk-b", executionId: "wfexec-2", stimulusType: "HttpEndpoint", stimulusHash: "sha256:http:/b:POST", createdAt: _now.AddMinutes(-1), metadata: new() { ["http:template"] = "/b" }));
        await store.SaveAsync(Bookmark("bk-other", executionId: "wfexec-3", stimulusType: "Event"));

        var result = await lookup.FindWaitingByTypeAsync(new GlobalBookmarkStimulusTypeLookupRequest("HttpEndpoint", _now));

        Assert.Equal(["bk-a", "bk-b"], result.Matches.Select(match => match.BookmarkId));
        Assert.Equal(["/a", "/b"], result.Matches.Select(match => match.Metadata["http:template"]));
    }

    [Fact]
    public async Task FindWaitingByType_ExcludesExpiredBookmarks()
    {
        // Expiry pin: filtering stays in the lookup layer (the raw index scan is unfiltered).
        var store = new InMemoryBookmarkStateStore();
        var lookup = new GlobalBookmarkStimulusLookup(store);
        await store.SaveAsync(Bookmark("bk-expired", executionId: "wfexec-1", stimulusType: "HttpEndpoint", expiresAt: _now.AddSeconds(-1)));
        await store.SaveAsync(Bookmark("bk-live", executionId: "wfexec-2", stimulusType: "HttpEndpoint", expiresAt: _now.AddMinutes(5)));

        var result = await lookup.FindWaitingByTypeAsync(new GlobalBookmarkStimulusTypeLookupRequest("HttpEndpoint", _now));

        Assert.Equal(["bk-live"], result.Matches.Select(match => match.BookmarkId));
    }

    [Fact]
    public async Task InMemoryStore_ListByStimulusType_ReturnsEveryTypeMatch_RegardlessOfHash_AndIsUnfilteredByExpiry()
    {
        // Spec 089 D (T004a): the raw index type-scan returns every bookmark of the type across hashes/executions,
        // WITHOUT expiry filtering (the documented raw-read contract — expiry lives in the lookup layer).
        var store = new InMemoryBookmarkStateStore();
        await store.SaveAsync(Bookmark("bk-a", executionId: "wfexec-1", stimulusType: "HttpEndpoint", stimulusHash: "h1"));
        await store.SaveAsync(Bookmark("bk-expired", executionId: "wfexec-2", stimulusType: "HttpEndpoint", stimulusHash: "h2", expiresAt: _now.AddSeconds(-1)));
        await store.SaveAsync(Bookmark("bk-other", executionId: "wfexec-3", stimulusType: "Event", stimulusHash: "h3"));

        var matches = await store.ListAllByStimulusTypeAsync("HttpEndpoint");

        Assert.Equal(["bk-a", "bk-expired"], matches.Select(match => match.BookmarkId).OrderBy(id => id, StringComparer.Ordinal));
    }

    private GlobalBookmarkStimulusLookupRequest Request(string? correlationId = null) =>
        new("Event", "sha256:event:hello", _now, correlationId);

    private BookmarkState Bookmark(
        string bookmarkId,
        string executionId,
        string stimulusType = "Event",
        string stimulusHash = "sha256:event:hello",
        string? correlationId = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null,
        Dictionary<string, string>? metadata = null)
    {
        metadata ??= new Dictionary<string, string>();
        if (correlationId is not null)
            metadata[RuntimeMetadataKeys.CorrelationId] = correlationId;

        return new BookmarkState(
            BookmarkId: bookmarkId,
            WorkflowExecutionId: executionId,
            ActivityExecutionId: $"actexec-{bookmarkId}",
            ExecutableNodeId: "node-wait",
            ResumeTargetId: "resume-target:event",
            StimulusType: stimulusType,
            StimulusHash: stimulusHash,
            Payload: null,
            Metadata: metadata,
            CreatedAt: createdAt ?? _now.AddMinutes(-1),
            ExpiresAt: expiresAt);
    }
}

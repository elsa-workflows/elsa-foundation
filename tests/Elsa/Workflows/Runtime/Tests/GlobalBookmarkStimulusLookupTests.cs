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

    private GlobalBookmarkStimulusLookupRequest Request(string? correlationId = null) =>
        new("Event", "sha256:event:hello", _now, correlationId);

    private BookmarkState Bookmark(
        string bookmarkId,
        string executionId,
        string stimulusType = "Event",
        string stimulusHash = "sha256:event:hello",
        string? correlationId = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null)
    {
        var metadata = new Dictionary<string, string>();
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

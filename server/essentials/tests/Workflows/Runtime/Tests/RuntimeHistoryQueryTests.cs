using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Seeded projection/query controls only; not checkpoint or authorization-handler qualification.</summary>
public sealed class RuntimeHistoryQueryTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("test-history-v1", 1, true, ["ActivityStarted"]);

    [Fact]
    public async Task OpaqueAuthorizationIdentitiesDoNotCollideThroughJsonReplacement()
    {
        var fixture = new Fixture();
        fixture.Append(1); fixture.Append(2);
        var page = await fixture.Reader.ReadPageAsync(new("run", limit: 1, accessBinding: "\ud800"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await fixture.Reader.ReadPageAsync(
            new("run", 1, page.NextContinuationToken, accessBinding: "\ud801")));
    }

    [Fact]
    public async Task SnapshotPagesExcludeAppendsAndCatchUpKeepsOriginalSequence()
    {
        var fixture = new Fixture();
        for (var n = 1; n <= 5; n++) fixture.Append(n);
        var first = await fixture.Reader.ReadPageAsync(new("run", limit: 2));
        Assert.Equal([1L, 2L], first.Items.Select(item => item.Sequence));
        Assert.Equal(5, first.UpperSequence);
        fixture.Append(6);
        var second = await fixture.Reader.ReadPageAsync(new("run", 2, first.NextContinuationToken));
        var third = await fixture.Reader.ReadPageAsync(new("run", 2, second.NextContinuationToken));
        Assert.Equal([3L, 4L], second.Items.Select(item => item.Sequence));
        Assert.Equal(5, Assert.Single(third.Items).Sequence);
        Assert.Null(third.NextContinuationToken);
        var catchUp = await fixture.Reader.ReadPageAsync(new("run", 2, third.CatchUpToken));
        Assert.Equal(6, Assert.Single(catchUp.Items).Sequence);
        Assert.Equal(6, catchUp.UpperSequence);
        Assert.Equal(At, catchUp.Items[0].OccurredAt); // equal observation clocks never determine ordering
    }

    [Fact]
    public async Task EmptySearchWindowsAdvanceUntilALaterMatch()
    {
        var fixture = new Fixture();
        for (var n = 1; n <= 100; n++) fixture.Append(n, name: n == 70 ? "TargetReached" : "ActivityStarted");
        var first = await fixture.Reader.ReadPageAsync(new("run", limit: 10, search: "TargetReached"));
        Assert.Empty(first.Items);
        Assert.Equal(40, first.ScannedCount);
        Assert.Equal(40, first.ScannedThroughSequence);
        Assert.NotNull(first.NextContinuationToken);
        var second = await fixture.Reader.ReadPageAsync(new("run", 10, first.NextContinuationToken, search: "TargetReached"));
        Assert.Equal(70, Assert.Single(second.Items).Sequence);
        Assert.Equal(80, second.ScannedThroughSequence);
        var third = await fixture.Reader.ReadPageAsync(new("run", 10, second.NextContinuationToken, search: "TargetReached"));
        Assert.Empty(third.Items);
        Assert.Null(third.NextContinuationToken);
        Assert.Equal(100, third.ScannedThroughSequence);
    }

    [Fact]
    public async Task ContinuationRejectsTamperingAndChangedAuthorizationScopeOrQuery()
    {
        var fixture = new Fixture();
        for (var n = 1; n <= 5; n++) fixture.Append(n);
        var first = await fixture.Reader.ReadPageAsync(new("run", limit: 2, accessBinding: "subject-a"));
        var token = Assert.IsType<string>(first.NextContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await fixture.Reader.ReadPageAsync(new("run", 3, token, accessBinding: "subject-a")));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await fixture.Reader.ReadPageAsync(new("run", 2, token, accessBinding: "subject-b")));
        fixture.Access.Scope = "other";
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await fixture.Reader.ReadPageAsync(new("run", 2, token, accessBinding: "subject-a")));
        fixture.Access.Scope = "scope-a";
        var position = token.IndexOf('.') + 1;
        var tampered = token[..position] + (token[position] == 'A' ? 'B' : 'A') + token[(position + 1)..];
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await fixture.Reader.ReadPageAsync(new("run", 2, tampered, accessBinding: "subject-a")));
    }

    [Fact]
    public async Task ExactLaterEventAndActivityIndexDoNotRequireLoadingEarlierHistory()
    {
        var fixture = new Fixture();
        for (var n = 1; n <= 10_000; n++) fixture.Append(n, activity: n % 2 == 0 ? "even" : "odd");
        Assert.Equal(10_000, (await fixture.Reader.FindEventAsync("run", "event-10000"))?.Sequence);
        var page = await fixture.Reader.ReadPageAsync(new("run", limit: 10, activityExecutionId: "even"));
        Assert.Equal(Enumerable.Range(1, 10).Select(n => (long)n * 2), page.Items.Select(item => item.Sequence));
        Assert.Equal(10, page.ScannedCount);
        fixture.Access.Scope = "scope-b";
        Assert.Null(await fixture.Reader.FindEventAsync("run", "event-10000"));
        Assert.Null(await fixture.Reader.FindHeadAsync("run"));
        Assert.Empty((await fixture.Reader.ReadPageAsync(new("run"))).Items);
    }

    [Fact]
    public async Task DescendantScopeIncludesOnlyActualParentLineage()
    {
        var fixture = new Fixture();
        fixture.Append(1, activity: "parent");
        fixture.Append(2, activity: "child", parent: "parent");
        fixture.Append(3, activity: "grandchild", parent: "child");
        fixture.Append(4, activity: "parallel", parent: "other-parent");
        var exact = await fixture.Reader.ReadPageAsync(new("run", activityExecutionId: "parent"));
        Assert.Equal(1, Assert.Single(exact.Items).Sequence);
        var subtree = await fixture.Reader.ReadPageAsync(new("run", activityExecutionId: "parent", includeDescendants: true));
        Assert.Equal([1L, 2L, 3L], subtree.Items.Select(item => item.Sequence));
    }

    [Fact]
    public async Task UncapturedParentDoesNotBecomeCompleteLookingEmptyDescendantScope()
    {
        var fixture = new Fixture();
        fixture.Append(1, activity: "child", parent: "uncaptured-parent");
        var page = await fixture.Reader.ReadPageAsync(new("run", activityExecutionId: "grandparent", includeDescendants: true));
        Assert.Empty(page.Items);
        Assert.False(page.ScopeComplete);
        Assert.NotNull(page.ScopeLimitation);
        Assert.True((await fixture.Reader.ReadPageAsync(new("run"))).ScopeComplete);
    }

    [Fact]
    public async Task ExpiredActiveBookmarkRemainsRecordedActiveAndReadDoesNotEmitAnEvent()
    {
        var fixture = new Fixture();
        fixture.Append(1, activity: "review", bookmark: "approval");
        fixture.Bookmark("approval", "review", 1, RuntimeHistoryBookmarkStatus.Active, expires: At.AddMinutes(-1));
        var page = await fixture.Reader.ListBookmarksAsync(new("run", activityExecutionId: "review"));
        var bookmark = Assert.Single(page.Items);
        Assert.Equal(RuntimeHistoryBookmarkStatus.Active, bookmark.Status);
        Assert.Equal(At.AddMinutes(-1), bookmark.Bookmark.ExpiresAt);
        Assert.Equal(1, (await fixture.Reader.FindHeadAsync("run"))?.LastSequence);
        Assert.Single((await fixture.Reader.ReadPageAsync(new("run"))).Items);
    }

    [Fact]
    public async Task BookmarkFilteredEmptyWindowsAndUnknownCreationRemainHonest()
    {
        var fixture = new Fixture();
        for (var n = 1; n <= 70; n++)
        {
            fixture.Append(n, activity: "review", bookmark: $"bookmark-{n}");
            fixture.Bookmark($"bookmark-{n}", "review", n, n == 70 ? RuntimeHistoryBookmarkStatus.Consumed : RuntimeHistoryBookmarkStatus.Active);
        }
        var first = await fixture.Reader.ListBookmarksAsync(new("run", limit: 10, activityExecutionId: "review", status: RuntimeHistoryBookmarkStatus.Consumed));
        Assert.Empty(first.Items);
        Assert.NotNull(first.NextContinuationToken);
        var second = await fixture.Reader.ListBookmarksAsync(new("run", 10, first.NextContinuationToken,
            activityExecutionId: "review", status: RuntimeHistoryBookmarkStatus.Consumed));
        Assert.Equal("bookmark-70", Assert.Single(second.Items).Bookmark.Identity.BookmarkId);
        var key = (fixture.Access.Scope, "run");
        fixture.State.HistoryHeads[key] = fixture.State.HistoryHeads[key] with { CapturedFromStart = false, HasGaps = true };
        var bookmarkKey = (fixture.Access.Scope, "run", "bookmark-70");
        fixture.State.HistoryBookmarks[bookmarkKey] = fixture.State.HistoryBookmarks[bookmarkKey] with
        {
            CreatedHistoryEventId = null,
            PriorState = RuntimeHistoryCreationEvidence.CreationMetadata
        };
        var unknown = await fixture.Reader.ListBookmarksAsync(new("run", activityExecutionId: "review", status: RuntimeHistoryBookmarkStatus.Consumed));
        Assert.Null(unknown.TotalCount);
        Assert.Null(Assert.Single(unknown.Items).CreatedHistoryEventId);
    }

    [Fact]
    public async Task SearchNeverExaminesArbitraryMetadataAndReadsHonorCancellation()
    {
        var fixture = new Fixture();
        fixture.Append(1, extraMetadata: new Dictionary<string, string> { ["internal"] = "private-marker" });
        Assert.Empty((await fixture.Reader.ReadPageAsync(new("run", search: "private-marker"))).Items);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.Reader.ReadPageAsync(new("run"), cancelled.Token));
    }

    [Fact]
    public async Task MissingRetainedEventCannotBecomeACompleteLookingRange()
    {
        var fixture = new Fixture();
        fixture.Append(1);
        fixture.Append(3);
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () =>
            await fixture.Reader.ReadPageAsync(new("run")));
    }

    [Fact]
    public async Task ACorruptExactProjectionCannotReturnAnotherRunsEvent()
    {
        var fixture = new Fixture();
        fixture.Append(1);
        fixture.State.HistoryEventsById[(fixture.Access.Scope, "run", "event-1")] =
            new("event-1", "another-run", 1, RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", At);
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () =>
            await fixture.Reader.FindEventAsync("run", "event-1"));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(2L)]
    public async Task ExactEventCannotExposeAnUnacknowledgedPosition(long invalidPosition)
    {
        var fixture = new Fixture();
        fixture.Append(1);
        fixture.State.HistoryEventsById[(fixture.Access.Scope, "run", "event-1")] =
            new("event-1", "run", invalidPosition, RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", At);
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () =>
            await fixture.Reader.FindEventAsync("run", "event-1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_exact_event_distinguishes_unpurged_receipts_from_acknowledged_purge(bool purged)
    {
        var fixture = new Fixture();
        fixture.Append(1);
        var key = (fixture.Access.Scope, "run");
        fixture.State.HistoryEventsById.Remove((fixture.Access.Scope, "run", "event-1"));
        Assert.Null(await fixture.Reader.FindEventAsync("run", "unknown-event"));
        if (purged)
        {
            fixture.State.HistoryHeads[key] = fixture.State.HistoryHeads[key] with { FirstSequence = 2, PurgedThroughSequence = 1 };
            fixture.State.HistoryEvents[key].Clear();
            Assert.Null(await fixture.Reader.FindEventAsync("run", "event-1"));
        }
        else
            await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () => await fixture.Reader.FindEventAsync("run", "event-1"));
    }

    [Fact]
    public async Task ExactBookmarkCannotExposeAnUnacknowledgedLifecycle()
    {
        var fixture = new Fixture();
        fixture.Append(1, bookmark: "approval");
        fixture.Bookmark("approval", "activity", 1, RuntimeHistoryBookmarkStatus.Active);
        var key = (fixture.Access.Scope, "run", "approval");
        fixture.State.HistoryBookmarks[key] = fixture.State.HistoryBookmarks[key] with { LastSequence = 2 };
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () =>
            await fixture.Reader.FindBookmarkAsync("run", "approval"));
    }

    [Fact]
    public async Task Exact_late_kind_uses_its_owned_index_instead_of_scanning_ten_thousand_rows()
    {
        var fixture = new Fixture();
        for (var sequence = 1; sequence <= 10000; sequence++) fixture.Append(sequence, name: sequence >= 9998 ? "rare-kind" : "ActivityStarted");
        var page = await fixture.Reader.ReadPageAsync(new("run", limit: 10, name: "rare-kind"));
        Assert.Equal([9998L, 9999L, 10000L], page.Items.Select(e => e.Sequence));
        Assert.Equal(3, page.ScannedCount);
        Assert.Null(page.NextContinuationToken);
    }

    private sealed class Fixture
    {
        public InMemoryRuntimeCheckpointStoreState State { get; } = new();
        public Accessor Access { get; } = new();
        public InMemoryRuntimeHistoryStore Reader { get; }

        public Fixture() => Reader = new(State, Access, new HmacRuntimeRecoveryContinuationCodec(Options.Create(
            new RuntimeRecoveryContinuationOptions { SigningKey = "history-query-tests-signing-key-at-least-32-bytes" })));

        public void Append(long sequence, string activity = "activity", string? parent = null,
            string? bookmark = null, string name = "ActivityStarted", IReadOnlyDictionary<string, string>? extraMetadata = null)
        {
            var key = (Access.Scope, "run");
            var item = new RuntimeHistoryEvent($"event-{sequence}", "run", sequence,
                RuntimeHistoryEventCategory.ActivityLifecycle, name, At, activity,
                checkpointId: $"logical-{sequence}", metadata: extraMetadata)
            {
                Authority = RuntimeHistoryAuthority.CommittedTransition,
                PhysicalCommitId = $"physical-{sequence}", RecordedAt = At,
                ParentActivityExecutionId = parent, BookmarkId = bookmark
            };
            State.PublishHistoryEvent(Access.Scope, item, "seed:" + sequence);
            State.HistoryParents[(Access.Scope, "run", activity)] = parent;
            State.HistoryHeads[key] = new("run", 1, sequence, item.PhysicalCommitId!, At, Policy, true, false, 0);
        }

        public void Bookmark(string id, string activity, long firstSequence, RuntimeHistoryBookmarkStatus status,
            DateTimeOffset? expires = null)
        {
            var summary = new ActivityExecutionBookmarkSummary(new(id, "run", activity, "continue"), "task", "safe-hash", At, expires);
            var value = new RuntimeHistoryBookmark(summary, "review-node", status, $"event-{firstSequence}",
                null, $"event-{firstSequence}", firstSequence, firstSequence, null, null, null, RuntimeHistoryCreationEvidence.CreationEvent);
            State.PublishHistoryBookmark(Access.Scope, value);
        }
    }

    private sealed class Accessor : IPersistenceAccessContextAccessor
    {
        public string Scope { get; set; } = "scope-a";
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(Scope));
    }
}

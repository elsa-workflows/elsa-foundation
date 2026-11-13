using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeHistoryStoreTests
{
    [Fact]
    public async Task Exact_events_and_bookmarks_share_their_committed_head_and_preserve_original_time_and_links()
    {
        await using var db = await Database.Create(); await db.Append(new Fact("event", "a", KnownTime: false),
            new("created", "a", "BookmarkCreated", Mutation: Mutation("b", "a")));
        await using var context = db.Open(); var store = db.Store(context);
        var detail = await store.FindEventDetailsAsync("run", "event"); Assert.Equal(2, detail.Head!.LastSequence);
        Assert.Equal("event", detail.Event!.HistoryEventId); Assert.Equal(1, detail.Event.Sequence); Assert.False(detail.Event.OccurredAtKnown);
        Assert.Equal("event", (await store.FindEventAsync("run", "event"))!.HistoryEventId);
        var bookmark = await store.FindBookmarkDetailsAsync("run", "b");
        Assert.Equal(2, bookmark.Head!.LastSequence); Assert.Equal("created", bookmark.Bookmark!.CreatedHistoryEventId);
        Assert.Equal(RuntimeHistoryBookmarkStatus.Active, (await store.FindBookmarkAsync("run", "b"))!.Status);
        Assert.Null(await store.FindEventAsync("run", "missing")); Assert.Null(await store.FindBookmarkAsync("run", "missing"));
        Assert.Null(await store.FindHeadAsync("missing-run")); Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Bookmark_inventory_uses_maintained_current_counts_and_keeps_old_window_counts_unknown()
    {
        await using var db = await Database.Create();
        await db.Append(Enumerable.Range(0, 6).Select(index => new Fact("created-" + index, index % 2 == 0 ? "a" : "b", "BookmarkCreated",
            Mutation: Mutation("b-" + index, index % 2 == 0 ? "a" : "b"))).ToArray());
        await using var context = db.Open(); var store = db.Store(context);
        var first = await store.ListBookmarksAsync(new("run", 2)); Assert.Equal(6, first.TotalCount); Assert.Equal(6, first.ActiveCount);
        Assert.Equal(new[] { "b-0", "b-1" }, first.Items.Select(item => item.Bookmark.Identity.BookmarkId));
        await db.Append(new Fact("consumed", "a", "BookmarkConsumed", Mutation: Mutation("b-0", "a", RuntimeHistoryBookmarkStatus.Consumed)));
        var old = await store.ListBookmarksAsync(new("run", 2, first.NextContinuationToken)); Assert.Null(old.TotalCount); Assert.Null(old.ActiveCount);
        var current = await store.ListBookmarksAsync(new("run", 20)); Assert.Equal(6, current.TotalCount); Assert.Equal(5, current.ActiveCount);
        var active = await store.ListBookmarksAsync(new("run", 20, status: RuntimeHistoryBookmarkStatus.Active));
        Assert.Equal(5, active.TotalCount); Assert.Equal(5, active.Items.Count);
        var ended = await store.ListBookmarksAsync(new("run", 20, status: RuntimeHistoryBookmarkStatus.Consumed));
        Assert.Null(ended.TotalCount); Assert.Equal(5, ended.ActiveCount); Assert.Equal("b-0", Assert.Single(ended.Items).Bookmark.Identity.BookmarkId);
        var owned = await store.ListBookmarksAsync(new("run", 20, activityExecutionId: "a")); Assert.Equal(3, owned.TotalCount); Assert.Equal(2, owned.ActiveCount);
        var descendants = await store.ListBookmarksAsync(new("run", 20, activityExecutionId: "a", includeDescendants: true));
        Assert.Null(descendants.TotalCount); Assert.Null(descendants.ActiveCount);
    }

    [Fact]
    public async Task Empty_filtered_bookmark_windows_advance_by_first_recorded_position()
    {
        await using var db = await Database.Create();
        await db.Append(Enumerable.Range(0, 9).Select(index => new Fact("created-" + index, "a", "BookmarkCreated", Mutation: Mutation("b-" + index, "a"))).ToArray());
        await using var context = db.Open(); var store = db.Store(context);
        var first = await store.ListBookmarksAsync(new("run", 1, status: RuntimeHistoryBookmarkStatus.Consumed)); Assert.Empty(first.Items); Assert.NotNull(first.NextContinuationToken);
        var next = await store.ListBookmarksAsync(new("run", 1, first.NextContinuationToken, status: RuntimeHistoryBookmarkStatus.Consumed));
        Assert.Empty(next.Items); Assert.NotEqual(first.NextContinuationToken, next.NextContinuationToken);
        var final = await store.ListBookmarksAsync(new("run", 1, next.NextContinuationToken, status: RuntimeHistoryBookmarkStatus.Consumed)); Assert.Empty(final.Items); Assert.Null(final.NextContinuationToken);
    }

    [Fact]
    public async Task Acknowledged_purge_preserves_lifecycle_metadata_without_inventing_live_handle_authority()
    {
        await using var db = await Database.Create();
        await db.Append(new Fact("created", "a", "BookmarkCreated", Mutation: Mutation("b", "a")), new("activity", "a"));
        await db.Purge(1); await using var context = db.Open(); var store = db.Store(context);
        var retired = await store.FindEventDetailsAsync("run", "created"); Assert.Null(retired.Event); Assert.Equal(1, retired.Head!.PurgedThroughSequence);
        var detail = await store.FindBookmarkDetailsAsync("run", "b"); Assert.Null(detail.Bookmark!.CreatedHistoryEventId);
        Assert.Equal(RuntimeHistoryCreationEvidence.CreationMetadata, detail.Bookmark.PriorState);
        // This recorded expiry is decades in the past; a reader cannot synthesize an owning mutation.
        Assert.Equal(RuntimeHistoryBookmarkStatus.Active, detail.Bookmark.Status);
        var inventory = await store.ListBookmarksAsync(new("run")); Assert.Null(inventory.TotalCount); Assert.Null(inventory.ActiveCount);
        Assert.Equal("b", Assert.Single(inventory.Items).Bookmark.Identity.BookmarkId);
        var page = await store.ReadPageAsync(new("run")); Assert.Equal("activity", Assert.Single(page.Items).HistoryEventId); Assert.Equal(2, page.ScannedThroughSequence);
    }

    [Theory]
    [InlineData("gap")] [InlineData("capture-late")] [InlineData("missing-kind")]
    public async Task Incomplete_capture_cannot_expose_a_maintained_count_as_a_known_complete_inventory(string limitation)
    {
        await using var db = await Database.Create(); await db.Append(new Fact("created", "a", "BookmarkCreated", Mutation: Mutation("b", "a")));
        await db.ChangeHead(head => limitation switch
        { "gap" => head with { HasGaps = true }, "capture-late" => head with { CapturedFromStart = false },
          _ => head with { Policy = new("limited", 1, true, ["ActivityStarted"]) } });
        await using var context = db.Open(); var page = await db.Store(context).ListBookmarksAsync(new("run")); Assert.Null(page.TotalCount); Assert.Null(page.ActiveCount);
    }

    [Fact]
    public async Task Missing_count_is_zero_only_when_the_complete_owned_inventory_really_has_no_rows()
    {
        await using var db = await Database.Create(); await db.Append(new Fact("activity", "a"));
        await using (var read = db.Open())
        { var page = await db.Store(read).ListBookmarksAsync(new("run")); Assert.Equal(0, page.TotalCount); Assert.Equal(0, page.ActiveCount); }
        await db.Append(new Fact("created", "a", "BookmarkCreated", Mutation: Mutation("b", "a")));
        await using (var corrupt = db.Open()) await corrupt.Set<RuntimeHistoryBookmarkCountEntity>().ExecuteDeleteAsync();
        await using var context = db.Open(); await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => db.Store(context).ListBookmarksAsync(new("run")).AsTask());
    }

    [Theory]
    [InlineData("interior")] [InlineData("tail")] [InlineData("unacknowledged-purge")] [InlineData("missing-head")]
    public async Task Missing_or_unacknowledged_retained_positions_are_hard_integrity_failures(string fault)
    {
        await using var db = await Database.Create(); await db.Append(new Fact("a", "a"), new("b", "b"), new("c", "c"));
        await using (var corrupt = db.Open())
        {
            if (fault == "missing-head") await corrupt.Set<RuntimeHistoryHeadEntity>().ExecuteDeleteAsync();
            else if (fault == "unacknowledged-purge")
            { var row = await corrupt.Set<RuntimeHistoryEventEntity>().SingleAsync(item => item.Sequence == 2); row.IsPurged = true; row.ContentJson = ""; await corrupt.SaveChangesAsync(); }
            else await corrupt.Set<RuntimeHistoryEventEntity>().Where(item => item.Sequence == (fault == "tail" ? 3 : 2)).ExecuteDeleteAsync();
        }
        await using var context = db.Open(); await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => db.Store(context).ReadPageAsync(new("run")).AsTask());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Schema_skew_and_readable_corrupt_content_keep_their_distinct_failure_classifications(bool future)
    {
        await using var db = await Database.Create(); await db.Append(new Fact("a", "a"));
        await using (var corrupt = db.Open())
        { var row = await corrupt.Set<RuntimeHistoryEventEntity>().SingleAsync(); row.ContentJson = "{broken"; if (future) row.SchemaVersion = "9.0.0"; await corrupt.SaveChangesAsync(); }
        await using var context = db.Open(); var store = db.Store(context);
        if (future) await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => store.FindEventDetailsAsync("run", "a").AsTask());
        else
        { var failure = await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => store.FindEventDetailsAsync("run", "a").AsTask()); Assert.IsAssignableFrom<JsonException>(failure.InnerException); }
    }
}

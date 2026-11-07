using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "RunHistory")]
    public async Task Cleanup_history_requires_an_exact_physical_bookmark_source(bool changed)
    {
        await using var db = await TestDatabase.CreateAsync(); await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        var source = CheckpointBookmark("cleanup-proof", "original"); var summary = ActivityExecutionBookmarkSummary.From(source);
        if (changed) await new EfBookmarkStateStore(context, new FixedAccessor("tenant-a")).SaveAsync(source with { StimulusHash = "changed" });
        var removed = CleanupCandidate("cleanup-remove", "BookmarkRemoved", summary, RuntimeHistoryBookmarkStatus.Removed);
        var commit = CleanupHistoryCommit("cleanup-missing", summary, [new(HistoryPolicy, "cleanup-remove", OccurredAt, false, [removed])]);
        var exception = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision()).AsTask());
        Assert.Equal("runtime.history.cleanup-source-unavailable", exception.Code);
        Assert.Empty(await context.RuntimeHistoryHeads.ToArrayAsync()); Assert.Empty(await context.RuntimeHistoryEvents.ToArrayAsync()); Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Exact_cleanup_supersession_keeps_successor_and_replays_without_requiring_erased_source()
    {
        await using var db = await TestDatabase.CreateAsync(); await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        var source = CheckpointBookmark("cleanup-proof", "original"); var summary = ActivityExecutionBookmarkSummary.From(source);
        await new EfBookmarkStateStore(context, new FixedAccessor("tenant-a")).SaveAsync(source);
        var candidate = CleanupCandidate("cleanup-supersede", "BookmarkSuperseded", summary, RuntimeHistoryBookmarkStatus.Superseded, "successor-activity");
        var commit = CleanupHistoryCommit("cleanup-success", summary, [new(HistoryPolicy, "cleanup-supersede", OccurredAt, false, [candidate])]);
        var writer = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
        await writer.CommitAsync(commit, Decision());
        Assert.Empty(await context.Bookmarks.ToArrayAsync());
        var projection = await HistoryStore(context, new FixedAccessor("tenant-a")).FindBookmarkAsync("workflow-a", "cleanup-proof");
        Assert.Equal(RuntimeHistoryBookmarkStatus.Superseded, projection!.Status); Assert.Equal("successor-activity", projection.SupersededByActivityExecutionId);
        await writer.CommitAsync(commit with { CommitId = "cleanup-physical-replay" }, Decision());
        Assert.Single(await context.RuntimeHistoryEvents.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "RunHistory")]
    public async Task Folded_cleanup_can_use_earlier_unpublished_creation_only_until_its_terminal_disposition(bool interveningTerminal)
    {
        await using var db = await TestDatabase.CreateAsync(); await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        var summary = ActivityExecutionBookmarkSummary.From(CheckpointBookmark("cleanup-overlay", "original"));
        var created = CleanupCandidate("overlay-created", "BookmarkCreated", summary, RuntimeHistoryBookmarkStatus.Active);
        var removed = CleanupCandidate("overlay-removed", "BookmarkRemoved", summary, RuntimeHistoryBookmarkStatus.Removed);
        var batches = new List<RuntimeHistoryBatch> { new(HistoryPolicy, "overlay-created", OccurredAt, true, [created]) };
        if (interveningTerminal) batches.Add(new(HistoryPolicy, "overlay-consumed", OccurredAt, false,
            [CleanupCandidate("overlay-consumed", "BookmarkConsumed", summary, RuntimeHistoryBookmarkStatus.Consumed)]));
        batches.Add(new(HistoryPolicy, "overlay-removed", OccurredAt, false, [removed]));
        var commit = CleanupHistoryCommit("overlay-physical", summary, batches);
        var writer = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
        if (interveningTerminal)
        {
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => writer.CommitAsync(commit, Decision()).AsTask());
            Assert.Empty(await context.RuntimeHistoryHeads.ToArrayAsync());
        }
        else
        {
            await writer.CommitAsync(commit, Decision());
            Assert.Empty(await context.Bookmarks.ToArrayAsync());
            var page = await HistoryStore(context, new FixedAccessor("tenant-a")).ReadPageAsync(new("workflow-a"));
            Assert.Equal(["BookmarkCreated", "BookmarkRemoved"], page.Items.Select(e => e.Name));
            Assert.Equal(RuntimeHistoryBookmarkStatus.Removed, (await HistoryStore(context, new FixedAccessor("tenant-a")).FindBookmarkAsync("workflow-a", "cleanup-overlay"))!.Status);
        }
    }

    private static RuntimeHistoryCandidate CleanupCandidate(string checkpoint, string name, ActivityExecutionBookmarkSummary summary,
        RuntimeHistoryBookmarkStatus status, string? successor = null)
    {
        var e = new RuntimeHistoryEvent(RuntimeHistoryIdentity.CreateId(checkpoint, name, summary.Identity.BookmarkId), summary.Identity.WorkflowExecutionId, 0,
            RuntimeHistoryEventCategory.BookmarkLifecycle, name, OccurredAt, summary.Identity.ActivityExecutionId, checkpointId: checkpoint)
        { Authority = RuntimeHistoryAuthority.CommittedTransition, BookmarkId = summary.Identity.BookmarkId };
        return new(e, 0, new(summary, status) { RelatedActivityExecutionId = successor });
    }
    private static RuntimeCheckpointCommit CleanupHistoryCommit(string physical, ActivityExecutionBookmarkSummary summary, IReadOnlyList<RuntimeHistoryBatch> batches)
    {
        var cleanup = new ActivityScopeCleanupRequest("workflow-a", "scope-a", [summary.Identity.ActivityExecutionId], [summary.Identity.BookmarkId], [], []) { HistoryBookmarks = [summary] };
        return Commit(physical) with { StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [], null, null, null, [cleanup], null), HistoryBatches = batches };
    }
}

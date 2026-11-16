using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Seeded exact-projection snapshot controls, not checkpoint/native admission or API authorization proof.</summary>
public sealed class RuntimeHistoryDetailsTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true, ["BookmarkCreated", "BookmarkRemoved"]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Details_capture_one_ordinary_scope_for_both_target_and_head(bool bookmark)
    {
        var access = new Access(switchAfterFirst: true);
        var state = Seed();
        IRuntimeHistoryDetailsStore reader = Reader(state, access);
        if (bookmark)
        {
            var result = await reader.FindBookmarkDetailsAsync("run", "bookmark");
            Assert.Equal(1, result.Head!.LastSequence);
            Assert.Equal(RuntimeHistoryBookmarkStatus.Active, result.Bookmark!.Status);
            Assert.Equal(result.Head.LastSequence, result.Bookmark.LastSequence);
        }
        else
        {
            var result = await reader.FindEventDetailsAsync("run", "event");
            Assert.Equal(1, result.Head!.LastSequence);
            Assert.Equal(result.Head.LastSequence, result.Event!.Sequence);
            Assert.Equal("BookmarkCreated", result.Event.Name);
        }
        Assert.Equal(1, access.Reads); // A second independent query would select scope-b and mix its later head.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_reads_do_not_reserve_the_native_writer_gate(bool bookmark)
    {
        var state = Seed();
        var reader = Reader(state, new Access());
        await state.WriteGate.WaitAsync();
        try
        {
            if (bookmark)
            {
                var pending = reader.FindBookmarkDetailsAsync("run", "bookmark");
                Assert.True(pending.IsCompletedSuccessfully);
                Assert.NotNull((await pending).Bookmark);
            }
            else
            {
                var pending = reader.FindEventDetailsAsync("run", "event");
                Assert.True(pending.IsCompletedSuccessfully);
                Assert.NotNull((await pending).Event);
            }
        }
        finally { state.WriteGate.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_event_snapshot_distinguishes_unknown_and_purged_content_from_missing_unpurged_receipts(bool purged)
    {
        var state = Seed();
        var reader = Reader(state, new Access());
        var missing = await reader.FindEventDetailsAsync("run", "unknown");
        Assert.NotNull(missing.Head);
        Assert.Null(missing.Event);
        state.HistoryEventsById.Remove(("scope-a", "run", "event"));
        if (purged)
        {
            state.HistoryHeads[("scope-a", "run")] = state.HistoryHeads[("scope-a", "run")] with { FirstSequence = 2, PurgedThroughSequence = 1 };
            var details = await reader.FindEventDetailsAsync("run", "event");
            Assert.Equal(1, details.Head!.PurgedThroughSequence);
            Assert.Null(details.Event);
        }
        else
            await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(async () => await reader.FindEventDetailsAsync("run", "event"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_detail_read_requires_an_ordinary_scope(bool privileged)
    {
        var context = privileged ? PersistenceAccessContext.PrivilegedScoped(new("scope-a"), new("maintenance")) : PersistenceAccessContext.Global;
        var reader = Reader(Seed(), new Access(forced: context));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.FindEventDetailsAsync("run", "event"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.FindBookmarkDetailsAsync("run", "bookmark"));
    }

    [Fact]
    public async Task Caller_cancellation_precedes_scope_or_projection_access()
    {
        var access = new Access();
        var reader = Reader(Seed(), access);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.FindEventDetailsAsync("run", "event", cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.FindBookmarkDetailsAsync("run", "bookmark", cancelled.Token));
        Assert.Equal(0, access.Reads);
    }

    [Fact]
    public async Task Common_event_snapshot_preserves_unknown_kind_version_and_unknown_occurrence()
    {
        var state = Seed();
        var original = state.HistoryEventsById[("scope-a", "run", "event")];
        state.HistoryEventsById[("scope-a", "run", "event")] = new RuntimeHistoryEvent(
            original.HistoryEventId, original.WorkflowExecutionId, original.Sequence, original.Category,
            "future.kind", original.OccurredAt, original.ActivityExecutionId, original.ExecutableNodeId,
            original.CheckpointId, original.IncidentId, original.Payload, original.Metadata)
        {
            SchemaVersion = 99, OccurredAtKnown = false, CheckpointOrdinal = original.CheckpointOrdinal,
            Authority = original.Authority, BookmarkId = original.BookmarkId, AttemptId = original.AttemptId,
            ParentActivityExecutionId = original.ParentActivityExecutionId, ActivityAncestryKnown = original.ActivityAncestryKnown,
            ExecutionScopeId = original.ExecutionScopeId, IterationId = original.IterationId, Causation = original.Causation,
            PhysicalCommitId = original.PhysicalCommitId, RecordedAt = original.RecordedAt
        };
        var details = await Reader(state, new Access()).FindEventDetailsAsync("run", "event");
        Assert.Equal("future.kind", details.Event!.Name);
        Assert.Equal(99, details.Event.SchemaVersion);
        Assert.False(details.Event.OccurredAtKnown);
        Assert.NotNull(details.Event.RecordedAt);
        Assert.Equal(1, details.Head!.LastSequence);
    }

    [Fact]
    public async Task A_later_bookmark_projection_does_not_mutate_an_already_returned_detail_snapshot()
    {
        var state = Seed();
        var reader = Reader(state, new Access());
        var before = await reader.FindBookmarkDetailsAsync("run", "bookmark");
        lock (state.SyncRoot)
        {
            state.HistoryHeads[("scope-a", "run")] = state.HistoryHeads[("scope-a", "run")] with { LastSequence = 2 };
            state.HistoryBookmarks[("scope-a", "run", "bookmark")] = state.HistoryBookmarks[("scope-a", "run", "bookmark")] with
                { Status = RuntimeHistoryBookmarkStatus.Removed, LastSequence = 2, EndedHistoryEventId = "ended" };
        }
        var after = await reader.FindBookmarkDetailsAsync("run", "bookmark");
        Assert.Equal(1, before.Head!.LastSequence);
        Assert.Equal(RuntimeHistoryBookmarkStatus.Active, before.Bookmark!.Status);
        Assert.Equal(1, before.Bookmark.LastSequence);
        Assert.Equal(2, after.Head!.LastSequence);
        Assert.Equal(RuntimeHistoryBookmarkStatus.Removed, after.Bookmark!.Status);
        Assert.Equal(2, after.Bookmark.LastSequence);
    }

    private static InMemoryRuntimeHistoryStore Reader(InMemoryRuntimeCheckpointStoreState state, IPersistenceAccessContextAccessor access) => new(state, access,
        new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "history-details-tests-signing-key-at-least-32-bytes" })));
    private static InMemoryRuntimeCheckpointStoreState Seed()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        foreach (var (scope, sequence) in new[] { ("scope-a", 1L), ("scope-b", 2L) })
        {
            var e = new RuntimeHistoryEvent("event", "run", sequence, RuntimeHistoryEventCategory.BookmarkLifecycle,
                sequence == 1 ? "BookmarkCreated" : "BookmarkRemoved", At, "activity", "node", "checkpoint")
                { Authority = RuntimeHistoryAuthority.CommittedTransition, BookmarkId = "bookmark", RecordedAt = At, PhysicalCommitId = "physical" };
            state.PublishHistoryEvent(scope, e, "seed");
            state.HistoryHeads[(scope, "run")] = new("run", sequence, sequence, "physical", At, Policy, sequence == 1, false, 0);
            var summary = new ActivityExecutionBookmarkSummary(new("bookmark", "run", "activity", "resume"), "task", "hash", At, null);
            state.PublishHistoryBookmark(scope, new(summary, "node", sequence == 1 ? RuntimeHistoryBookmarkStatus.Active : RuntimeHistoryBookmarkStatus.Removed,
                sequence == 1 ? "event" : null, sequence == 1 ? null : "event", "event", sequence, sequence, null, null, null, RuntimeHistoryCreationEvidence.CreationMetadata));
        }
        return state;
    }
    private sealed class Access(bool switchAfterFirst = false, PersistenceAccessContext? forced = null) : IPersistenceAccessContextAccessor
    {
        internal int Reads;
        public PersistenceAccessContext Current
        {
            get { Reads++; return forced ?? PersistenceAccessContext.Scoped(new(switchAfterFirst && Reads > 1 ? "scope-b" : "scope-a")); }
        }
    }
}

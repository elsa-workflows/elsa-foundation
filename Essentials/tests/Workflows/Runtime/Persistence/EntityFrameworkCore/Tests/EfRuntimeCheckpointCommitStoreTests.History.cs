using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    private static readonly RuntimeHistoryCapturePolicy HistoryPolicy = new("test-history", 1, true,
        ["WorkflowStarted", "ActivityStarted", "ActivityCompleted", "BookmarkCreated", "BookmarkConsumed", "BookmarkRemoved", "BookmarkSuperseded", "StimulusReceived"]);

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task History_is_atomic_with_marker_replay_restart_and_exact_context_pairing()
    {
        await using var db = await TestDatabase.CreateAsync();
        var access = new FixedAccessor("tenant-a");
        var commit = HistoryCommit("history-initial", true, [HistoryCandidate("checkpoint-history-initial", 0, "WorkflowStarted")]);
        await using (var context = db.Open("tenant-a"))
        {
            await SeedHistoryRunAsync(context);
            var writer = new EfRuntimeCheckpointCommitStore(context, access);
            var reader = HistoryStore(context, access);
            Assert.True(((IRuntimeCheckpointCommitStore)writer).SupportsHistory);
            Assert.True(((IRuntimeCheckpointCommitStore)writer).CanCommitHistory(reader));
            Assert.False(((IRuntimeCheckpointCommitStore)writer).CanCommitHistory(HistoryStore(context, new FixedAccessor("tenant-a"))));
            await using var other = db.Open("tenant-a");
            Assert.False(((IRuntimeCheckpointCommitStore)writer).CanCommitHistory(HistoryStore(other, access)));
            await writer.CommitAsync(commit, Decision());
            await writer.CommitAsync(commit, Decision());
        }
        await using var restarted = db.Open("tenant-a");
        var history = HistoryStore(restarted, access);
        var page = await history.ReadPageAsync(new("workflow-a"));
        var item = Assert.Single(page.Items);
        Assert.Equal(1, item.Sequence);
        Assert.Equal(0, item.CheckpointOrdinal);
        Assert.Equal(commit.CommitId, item.PhysicalCommitId);
        Assert.Equal(commit.CommitId, page.Head!.LastPhysicalCommitId);
        Assert.True(page.Head.CapturedFromStart);
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.NotNull(await history.FindEventAsync("workflow-a", item.HistoryEventId));
        Assert.Null(await HistoryStore(restarted, new FixedAccessor("tenant-b")).FindEventAsync("workflow-a", item.HistoryEventId));
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task History_marker_failure_rolls_back_head_events_bookmarks_counts_and_ancestry()
    {
        await using var db = await TestDatabase.CreateAsync();
        var failure = new FailMarkerInsertInterceptor();
        await using var context = db.Open("tenant-a", failure);
        await SeedHistoryRunAsync(context);
        failure.Arm();
        var candidate = HistoryCandidate("checkpoint-history-fail", 0, "BookmarkCreated", "activity-a", bookmark: "bookmark-a");
        var commit = HistoryCommit("history-fail", true, [candidate]);
        await Assert.ThrowsAsync<DbUpdateException>(() => new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(context.ChangeTracker.Entries());
        await using var restarted = db.Open("tenant-a");
        Assert.Empty(await restarted.RuntimeHistoryHeads.ToArrayAsync());
        Assert.Empty(await restarted.RuntimeHistoryEvents.ToArrayAsync());
        Assert.Empty(await restarted.RuntimeHistoryBookmarks.ToArrayAsync());
        Assert.Empty(await restarted.RuntimeHistoryBookmarkCounts.ToArrayAsync());
        Assert.Empty(await restarted.RuntimeHistoryActivities.ToArrayAsync());
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Required_retained_policy_cannot_be_bypassed_by_removing_capture_or_incoming_policy()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        var writer = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => writer.CommitAsync(Commit("missing-batch"), Decision()).AsTask());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
        var legacy = Execution("legacy", "tenant-a");
        context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(legacy, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", "legacy"), 1));
        await context.SaveChangesAsync();
        var injected = HistoryCommit("legacy-inject", false, [HistoryCandidate("checkpoint-legacy-inject", 0, "WorkflowStarted", run: "legacy")], "legacy");
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(injected, Decision()).AsTask());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Coalesced_logical_groups_keep_order_and_full_bookmark_candidate_fingerprints()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        var created = HistoryCandidate("logical-a", 0, "BookmarkCreated", "activity-a", bookmark: "bookmark-a");
        var consumed = HistoryCandidate("logical-b", 0, "BookmarkConsumed", "activity-a", bookmark: "bookmark-a");
        var commit = Commit("physical-fold") with { HistoryBatches = [
            new(HistoryPolicy, "logical-a", OccurredAt, true, [created]), new(HistoryPolicy, "logical-b", OccurredAt, false, [consumed])] };
        var writer = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
        await writer.CommitAsync(commit, Decision());
        // An identical logical group under another physical commit is deduplicated against its original receipt.
        await writer.CommitAsync(commit with { CommitId = "physical-retry" }, Decision());
        var history = HistoryStore(context, new FixedAccessor("tenant-a"));
        Assert.Equal([1L, 2L], (await history.ReadPageAsync(new("workflow-a"))).Items.Select(x => x.Sequence));
        Assert.Equal([0, 0], (await history.ReadPageAsync(new("workflow-a"))).Items.Select(x => x.CheckpointOrdinal));
        var bookmark = await history.FindBookmarkAsync("workflow-a", "bookmark-a");
        Assert.Equal(RuntimeHistoryBookmarkStatus.Consumed, bookmark!.Status);
        Assert.Equal(created.Event.HistoryEventId, bookmark.CreatedHistoryEventId);
        Assert.Equal(consumed.Event.HistoryEventId, bookmark.EndedHistoryEventId);
        var inventory = await history.ListBookmarksAsync(new("workflow-a", activityExecutionId: "activity-a"));
        Assert.Equal(1, inventory.TotalCount); Assert.Equal(0, inventory.ActiveCount);
        var altered = new RuntimeHistoryCandidate(created.Event, 0, created.BookmarkMutation! with { Reason = "different-safe-reason" });
        var conflict = commit with { CommitId = "physical-conflict", HistoryBatches = [new(HistoryPolicy, "logical-a", OccurredAt, false, [altered])] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(conflict, Decision()).AsTask());
        Assert.Equal(2, (await history.FindHeadAsync("workflow-a"))!.LastSequence);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Bounded_empty_search_windows_advance_to_tail_and_catch_up_after_append()
    {
        await using var db = await TestDatabase.CreateAsync();
        var access = new FixedAccessor("tenant-a");
        await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        var candidates = Enumerable.Range(0, 10).Select(i => HistoryCandidate("checkpoint-window", i, "ActivityCompleted", "a-" + i)).ToArray();
        var writer = new EfRuntimeCheckpointCommitStore(context, access);
        await writer.CommitAsync(HistoryCommit("window", true, candidates), Decision());
        var history = HistoryStore(context, access);
        var first = await history.ReadPageAsync(new("workflow-a", 1, search: "needle"));
        Assert.Empty(first.Items); Assert.Equal(4, first.ScannedCount); Assert.Equal(4, first.ScannedThroughSequence); Assert.NotNull(first.NextContinuationToken);
        var second = await history.ReadPageAsync(new("workflow-a", 1, first.NextContinuationToken, search: "needle"));
        Assert.Equal(8, second.ScannedThroughSequence);
        var third = await history.ReadPageAsync(new("workflow-a", 1, second.NextContinuationToken, search: "needle"));
        Assert.Equal(10, third.ScannedThroughSequence); Assert.Null(third.NextContinuationToken);
        await writer.CommitAsync(HistoryCommit("append", false, [HistoryCandidate("checkpoint-append", 0, "ActivityCompleted", "needle")]), Decision());
        var appended = await history.ReadPageAsync(new("workflow-a", 1, third.CatchUpToken, search: "needle"));
        Assert.Equal(11, Assert.Single(appended.Items).Sequence);
        // The old bounded traversal continues only through its original snapshot watermark.
        var old = await history.ReadPageAsync(new("workflow-a", 1, first.NextContinuationToken, search: "needle"));
        Assert.Equal(10, old.UpperSequence);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Continuations_are_authenticated_and_bound_to_scope_resource_limit_filters_access_and_purpose()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(HistoryCommit("token", true,
            [HistoryCandidate("checkpoint-token", 0, "ActivityCompleted", "a"), HistoryCandidate("checkpoint-token", 1, "ActivityCompleted", "b")]), Decision());
        var reader = HistoryStore(context, new FixedAccessor("tenant-a"));
        var first = await reader.ReadPageAsync(new("workflow-a", 1, accessBinding: "profile-a"));
        var token = first.NextContinuationToken!;
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadPageAsync(new("workflow-a", 2, token, accessBinding: "profile-a")).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadPageAsync(new("other-run", 1, token, accessBinding: "profile-a")).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadPageAsync(new("workflow-a", 1, token, accessBinding: "profile-b")).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadPageAsync(new("workflow-a", 1, token, search: "x", accessBinding: "profile-a")).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HistoryStore(context, new FixedAccessor("tenant-b")).ReadPageAsync(new("workflow-a", 1, token, accessBinding: "profile-a")).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ListBookmarksAsync(new("workflow-a", 1, token, accessBinding: "profile-a")).AsTask());
        var tampered = token[..^1] + (token[^1] == 'a' ? 'b' : 'a');
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadPageAsync(new("workflow-a", 1, tampered, accessBinding: "profile-a")).AsTask());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Descendant_scope_keeps_missing_ancestry_unknown_and_detects_cycles()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        var events = new[] { HistoryCandidate("checkpoint-scope", 0, "ActivityStarted", "root"),
            HistoryCandidate("checkpoint-scope", 1, "ActivityStarted", "child", parent: "root"),
            HistoryCandidate("checkpoint-scope", 2, "BookmarkCreated", "unknown", bookmark: "bookmark-unknown") };
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(HistoryCommit("scope", true, events), Decision());
        var reader = HistoryStore(context, new FixedAccessor("tenant-a"));
        var page = await reader.ReadPageAsync(new("workflow-a", activityExecutionId: "root", includeDescendants: true));
        Assert.Equal(["root", "child"], page.Items.Select(x => x.ActivityExecutionId));
        Assert.False(page.ScopeComplete); Assert.NotNull(page.ScopeLimitation);
        Assert.False((await reader.ListBookmarksAsync(new("workflow-a", activityExecutionId: "root", includeDescendants: true))).ScopeComplete);
        var cycle = HistoryCommit("cycle", false, [HistoryCandidate("checkpoint-cycle", 0, "ActivityStarted", "cycle-a", parent: "cycle-b"),
            HistoryCandidate("checkpoint-cycle", 1, "ActivityStarted", "cycle-b", parent: "cycle-a")]);
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(cycle, Decision());
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => reader.ReadPageAsync(new("workflow-a", activityExecutionId: "unrelated", includeDescendants: true)).AsTask());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Purge_keeps_allocator_policy_and_receipts_without_resurrecting_old_logical_events()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        var candidate = HistoryCandidate("checkpoint-purge", 0, "BookmarkCreated", "activity-a", bookmark: "bookmark-a");
        var commit = HistoryCommit("purge", true, [candidate]);
        var access = new FixedAccessor("tenant-a");
        var writer = new EfRuntimeCheckpointCommitStore(context, access);
        await writer.CommitAsync(commit, Decision());
        var history = HistoryStore(context, access);
        Assert.Equal(1, await history.PurgeThroughAsync("workflow-a", 100));
        Assert.Null(await history.FindEventAsync("workflow-a", candidate.Event.HistoryEventId));
        Assert.Empty((await history.ReadPageAsync(new("workflow-a"))).Items);
        Assert.True((await context.RuntimeHistoryEvents.SingleAsync()).IsPurged);
        Assert.Empty((await context.RuntimeHistoryEvents.SingleAsync()).ContentJson);
        var retainedBookmark = await history.FindBookmarkAsync("workflow-a", "bookmark-a");
        Assert.Equal(RuntimeHistoryBookmarkStatus.Active, retainedBookmark!.Status);
        Assert.Null(retainedBookmark.CreatedHistoryEventId);
        Assert.Equal(RuntimeHistoryPriorState.CreationMetadata, retainedBookmark.PriorState);
        Assert.Null((await history.ListBookmarksAsync(new("workflow-a"))).TotalCount);
        await writer.CommitAsync(commit with { CommitId = "late-physical-retry" }, Decision());
        Assert.Equal(1, (await history.FindHeadAsync("workflow-a"))!.LastSequence);
        var ended = HistoryCommit("purge-end", false, [HistoryCandidate("checkpoint-purge-end", 0, "BookmarkConsumed", "activity-a", bookmark: "bookmark-a")]);
        await writer.CommitAsync(ended, Decision());
        Assert.Equal(2, Assert.Single((await history.ReadPageAsync(new("workflow-a"))).Items).Sequence);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => writer.CommitAsync(Commit("still-required"), Decision()).AsTask());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Independent_observations_deduplicate_and_do_not_advance_runtime_capture_coverage()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        var access = new FixedAccessor("tenant-a");
        await new EfRuntimeCheckpointCommitStore(context, access).CommitAsync(HistoryCommit("observation-base", true,
            [HistoryCandidate("checkpoint-observation-base", 0, "WorkflowStarted")]), Decision());
        var history = HistoryStore(context, access);
        var original = await history.FindHeadAsync("workflow-a");
        var e = new RuntimeHistoryEvent("delivery-exact", "workflow-a", 0, RuntimeHistoryEventCategory.OperationalLifecycle, "StimulusReceived", OccurredAt.AddHours(1))
        { Authority = RuntimeHistoryAuthority.DeliveryObservation, Causation = new(DeliveryId: "delivery-1") };
        var first = await history.RecordAsync(new(HistoryPolicy, e));
        var replay = await history.RecordAsync(new(HistoryPolicy, e));
        Assert.True(first.Recorded); Assert.True(replay.Recorded); Assert.Equal(first.Event!.Sequence, replay.Event!.Sequence);
        var head = await history.FindHeadAsync("workflow-a");
        Assert.Equal(2, head!.LastSequence); Assert.Equal(original!.LastPhysicalCommitId, head.LastPhysicalCommitId); Assert.Equal(original.LastCommittedAt, head.LastCommittedAt);
        var absent = new RuntimeHistoryEvent("delivery-absent", "absent", 0, RuntimeHistoryEventCategory.OperationalLifecycle, "StimulusReceived", OccurredAt) { Authority = RuntimeHistoryAuthority.DeliveryObservation };
        Assert.False((await history.RecordAsync(new(HistoryPolicy, absent))).Recorded);
        Assert.Equal(2, (await history.FindHeadAsync("workflow-a"))!.LastSequence);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Missing_or_corrupt_event_positions_fail_closed_instead_of_claiming_complete_coverage()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(HistoryCommit("gap", true,
            [HistoryCandidate("checkpoint-gap", 0, "ActivityStarted", "a"), HistoryCandidate("checkpoint-gap", 1, "ActivityCompleted", "a")]), Decision());
        await context.RuntimeHistoryEvents.Where(x => x.Sequence == 1).ExecuteDeleteAsync();
        var reader = HistoryStore(context, new FixedAccessor("tenant-a"));
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => reader.ReadPageAsync(new("workflow-a")).AsTask());
        await context.RuntimeHistoryEvents.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ScopeKey, EfRelationalIdentity.Encode("different")));
        var eventId = HistoryCandidate("checkpoint-gap", 1, "ActivityCompleted", "a").Event.HistoryEventId;
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => reader.FindEventAsync("workflow-a", eventId).AsTask());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Ten_thousand_event_search_materializes_only_a_bounded_indexed_window()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var context = db.Open("tenant-a"))
        {
            await SeedHistoryRunAsync(context);
            var writer = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
            for (var group = 0; group < 4; group++)
            {
                var id = "load-" + group; var checkpoint = "checkpoint-" + id;
                var candidates = Enumerable.Range(0, 2500).Select(i => HistoryCandidate(checkpoint, i, group == 3 && i == 2499 ? "ActivityStarted" : "ActivityCompleted", "activity-" + (group * 2500 + i))).ToArray();
                await writer.CommitAsync(HistoryCommit(id, group == 0, candidates), Decision());
            }
        }
        var capture = new CommandCaptureInterceptor();
        await using var read = db.Open("tenant-a", capture);
        var reader = HistoryStore(read, new FixedAccessor("tenant-a"));
        var first = await reader.ReadPageAsync(new("workflow-a", 250, search: "no-such-event"));
        Assert.Empty(first.Items); Assert.Equal(1000, first.ScannedCount); Assert.Equal(1000, first.ScannedThroughSequence); Assert.Equal(10000, first.UpperSequence);
        Assert.NotNull(first.NextContinuationToken);
        Assert.DoesNotContain(capture.Commands, command => command.Contains("COUNT(", StringComparison.OrdinalIgnoreCase) || command.Contains("LIKE", StringComparison.OrdinalIgnoreCase));
        var eventRead = Assert.Single(capture.Commands, command => command.Contains("elsa_runtime_history_event", StringComparison.Ordinal));
        Assert.Contains("LIMIT", eventRead); Assert.Contains("Sequence", eventRead); Assert.Contains("ScopeKeyHash", eventRead);
        var exact = await reader.ReadPageAsync(new("workflow-a", activityExecutionId: "activity-9999"));
        Assert.Equal(10000, Assert.Single(exact.Items).Sequence);
        Assert.Equal(1, exact.ScannedCount);
        var lateKind = await reader.ReadPageAsync(new("workflow-a", name: "ActivityStarted"));
        Assert.Equal(10000, Assert.Single(lateKind.Items).Sequence);
        Assert.Equal(1, lateKind.ScannedCount);
        Assert.Contains(capture.Commands, command => command.Contains("NameHash", StringComparison.Ordinal) && command.Contains("LIMIT", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Inspection_refuses_uncommitted_caller_state_without_discarding_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        var reader = HistoryStore(context, new FixedAccessor("tenant-a"));
        context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.FindHeadAsync("workflow-a").AsTask());
        Assert.True(context.ChangeTracker.HasChanges());
        Assert.Single(context.ChangeTracker.Entries());
        context.ChangeTracker.Clear();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadPageAsync(new("workflow-a")).AsTask());
            await transaction.RollbackAsync();
        }
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.FindBookmarkAsync("workflow-a", "b").AsTask());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Future_event_common_envelopes_are_readable_and_malformed_content_reports_integrity()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        var candidate = HistoryCandidate("checkpoint-future", 0, "WorkflowStarted");
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(HistoryCommit("future", true, [candidate]), Decision());
        context.ChangeTracker.Clear();
        var row = await context.RuntimeHistoryEvents.SingleAsync();
        using (var stored = System.Text.Json.JsonDocument.Parse(row.ContentJson))
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Number, stored.RootElement.GetProperty("category").ValueKind);
            Assert.Equal(System.Text.Json.JsonValueKind.Number, stored.RootElement.GetProperty("authority").ValueKind);
        }
        var future = new RuntimeHistoryEvent(candidate.Event.HistoryEventId, "workflow-a", 1, (RuntimeHistoryEventCategory)987, "FutureKind", OccurredAt,
            checkpointId: candidate.Event.CheckpointId)
        { SchemaVersion = 42, Authority = (RuntimeHistoryAuthority)987, PhysicalCommitId = "future", RecordedAt = OccurredAt, CheckpointOrdinal = 0 };
        row.ContentJson = RuntimeArtifactJson.Serialize(future);
        row.Name = EfRelationalIdentity.Encode(future.Name); row.NameHash = EfRelationalIdentity.Hash(future.Name); row.Category = (int)future.Category;
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var reader = HistoryStore(context, new FixedAccessor("tenant-a"));
        var actual = Assert.Single((await reader.ReadPageAsync(new("workflow-a"))).Items);
        Assert.Equal("FutureKind", actual.Name); Assert.Equal(42, actual.SchemaVersion); Assert.Equal((RuntimeHistoryEventCategory)987, actual.Category);
        await context.RuntimeHistoryEvents.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ContentJson, "[malformed"));
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => reader.ReadPageAsync(new("workflow-a")).AsTask());
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => reader.FindEventAsync("workflow-a", candidate.Event.HistoryEventId).AsTask());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Owned_terminal_deletion_removes_history_and_refuses_late_observation_recreation()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var context = db.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        var access = new FixedAccessor("tenant-a");
        await new EfRuntimeCheckpointCommitStore(context, access).CommitAsync(HistoryCommit("delete-history", true,
            [HistoryCandidate("checkpoint-delete-history", 0, "BookmarkCreated", "a", bookmark: "b")]), Decision());
        context.ChangeTracker.Clear();
        var native = await context.WorkflowExecutionStates.SingleAsync();
        var completed = EfWorkflowExecutionStateStore.ReadChecked(native, "tenant-a", "workflow-a") with { Status = WorkflowExecutionStatus.Completed, CompletedAt = OccurredAt.AddHours(1) };
        EfWorkflowExecutionStateStore.CopyToEntity(native, completed, "tenant-a", native.Id, native.Revision + 1);
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "run-history-tests-exact-signing-key-32" }));
        var executions = new EfWorkflowExecutionStateStore(context, access, codec);
        var candidate = await executions.ReadDeletionCandidateAsync("workflow-a");
        Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await executions.TryDeleteTerminalAsync(candidate!));
        Assert.Empty(await context.RuntimeHistoryHeads.ToArrayAsync()); Assert.Empty(await context.RuntimeHistoryEvents.ToArrayAsync());
        Assert.Empty(await context.RuntimeHistoryBookmarks.ToArrayAsync()); Assert.Empty(await context.RuntimeHistoryBookmarkCounts.ToArrayAsync()); Assert.Empty(await context.RuntimeHistoryActivities.ToArrayAsync());
        var e = new RuntimeHistoryEvent("late-after-delete", "workflow-a", 0, RuntimeHistoryEventCategory.OperationalLifecycle, "StimulusReceived", OccurredAt) { Authority = RuntimeHistoryAuthority.DeliveryObservation };
        Assert.False((await HistoryStore(context, access).RecordAsync(new(HistoryPolicy, e))).Recorded);
        Assert.Empty(await context.RuntimeHistoryHeads.ToArrayAsync());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Head_allocation_conflict_retries_are_narrow_bounded_and_create_one_physical_commit()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var seed = db.Open("tenant-a"))
        {
            await SeedHistoryRunAsync(seed);
            await new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor("tenant-a")).CommitAsync(HistoryCommit("cas-initial", true,
                [HistoryCandidate("checkpoint-cas-initial", 0, "WorkflowStarted")]), Decision());
        }
        var guard = new HistoryHeadCasInterceptor(1);
        var committed = new HistoryCommitCounter();
        await using (var retry = db.Open("tenant-a", guard, committed))
            await new EfRuntimeCheckpointCommitStore(retry, new FixedAccessor("tenant-a")).CommitAsync(HistoryCommit("cas-retry", false,
                [HistoryCandidate("checkpoint-cas-retry", 0, "ActivityCompleted", "a")]), Decision());
        Assert.Equal(2, guard.Attempts); Assert.Equal(1, committed.PhysicalCommits);
        var exhausted = new HistoryHeadCasInterceptor(10);
        await using (var retry = db.Open("tenant-a", exhausted, committed))
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
                new EfRuntimeCheckpointCommitStore(retry, new FixedAccessor("tenant-a")).CommitAsync(HistoryCommit("cas-exhausted", false,
                    [HistoryCandidate("checkpoint-cas-exhausted", 0, "ActivityCompleted", "b")]), Decision()).AsTask());
        Assert.Equal(3, exhausted.Attempts); Assert.Equal(1, committed.PhysicalCommits);
        await using var verify = db.Open("tenant-a");
        Assert.Equal(2, (await HistoryStore(verify, new FixedAccessor("tenant-a")).FindHeadAsync("workflow-a"))!.LastSequence);
        Assert.Equal(2, await verify.RuntimeCheckpointCommits.CountAsync());
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Ambiguous_history_commit_acknowledgement_reconciles_one_marker_and_original_positions()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var seed = db.Open("tenant-a")) await SeedHistoryRunAsync(seed);
        var loss = new CommitAcknowledgementLossInterceptor();
        var commit = HistoryCommit("history-ambiguous", true, [HistoryCandidate("checkpoint-history-ambiguous", 0, "WorkflowStarted")]);
        await using (var ambiguous = db.Open("tenant-a", loss))
            await new EfRuntimeCheckpointCommitStore(ambiguous, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision());
        await using var restarted = db.Open("tenant-a");
        await new EfRuntimeCheckpointCommitStore(restarted, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision());
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Equal(1, Assert.Single((await HistoryStore(restarted, new FixedAccessor("tenant-a")).ReadPageAsync(new("workflow-a"))).Items).Sequence);
    }

    private sealed class HistoryHeadCasInterceptor(int misses) : DbCommandInterceptor
    {
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("elsa_runtime_history_head", StringComparison.Ordinal))
            {
                Attempts++;
                if (Attempts <= misses) return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
            }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class HistoryCommitCounter : DbTransactionInterceptor
    {
        public int PhysicalCommits { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { PhysicalCommits++; return Task.CompletedTask; }
    }

    private static EfRuntimeHistoryStore HistoryStore(RuntimeDbContext context, IPersistenceAccessContextAccessor access) => new(context, access,
        new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "run-history-tests-exact-signing-key-32" })), new FixedTimeProvider(OccurredAt.AddSeconds(1)));

    private static async Task SeedHistoryRunAsync(RuntimeDbContext context)
    {
        var state = Execution("workflow-a", "tenant-a") with { HistoryCapturePolicy = HistoryPolicy, HistoryCaptureEnabledAtAdmission = true };
        context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", "workflow-a"), 1));
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
    }
    private static RuntimeCheckpointCommit HistoryCommit(string id, bool fromStart, IReadOnlyList<RuntimeHistoryCandidate> candidates, string run = "workflow-a")
    {
        var commit = Commit(id);
        return commit with { Checkpoint = commit.Checkpoint with { WorkflowExecutionId = run }, HistoryBatches = [new(HistoryPolicy, "checkpoint-" + id, OccurredAt, fromStart, candidates)] };
    }
    private static RuntimeHistoryCandidate HistoryCandidate(string checkpoint, int ordinal, string name, string? activity = null, string? parent = null, string? bookmark = null, string run = "workflow-a")
    {
        var e = new RuntimeHistoryEvent(RuntimeHistoryIdentity.CreateId(checkpoint, name, (activity ?? "run") + ":" + ordinal), run, 0,
            bookmark is not null ? RuntimeHistoryEventCategory.BookmarkLifecycle : activity is not null ? RuntimeHistoryEventCategory.ActivityLifecycle : RuntimeHistoryEventCategory.WorkflowLifecycle,
            name, OccurredAt, activity, activity is null ? null : "node-" + activity, checkpoint)
        { Authority = RuntimeHistoryAuthority.CommittedTransition, ActivityAncestryKnown = bookmark is null && activity is not null ? true : null, ParentActivityExecutionId = parent, BookmarkId = bookmark };
        return new(e, ordinal, bookmark is null ? null : new(new(new(bookmark, run, activity!, "resume"), "safe-type", "safe-hash", OccurredAt, null),
            name == "BookmarkCreated" ? RuntimeHistoryBookmarkStatus.Active : RuntimeHistoryBookmarkStatus.Consumed));
    }
}

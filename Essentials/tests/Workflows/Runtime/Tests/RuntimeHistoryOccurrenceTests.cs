using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

// Provider projection controls. Publication is seeded directly here; these do not establish native
// capture, EF/restart, retention mutation policy, authorized HTTP reads or composed Studio support.
public sealed class RuntimeHistoryOccurrenceTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("occurrence-tests", 1, true,
        ["ActivityStarted", "ActivityCompleted", "ActivityContinuationStarted", "ActivitySuspended"]);

    [Fact]
    public async Task Exact_occurrences_are_unique_and_ordered_by_first_commit_including_intrinsics_and_retries()
    {
        var f = new Fixture();
        f.Append("root");
        f.Append("loop-1", parent: "root", at: At.AddHours(1));
        f.Append("loop-1", name: "ActivitySuspended", parent: "root");
        f.Append("loop-1", name: "ActivityContinuationStarted", parent: "root");
        f.Append("loop-2", parent: "root", at: At.AddHours(-1));
        f.Append("retry-2", parent: "root");
        f.Append("intrinsic", name: "ActivityCompleted", parent: "root");
        var page = await f.Reader.ReadOccurrencesAsync(new("run"));
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(new[] { "root", "loop-1", "loop-2", "retry-2", "intrinsic" }, Ids(page));
        Assert.Equal(new long[] { 1, 2, 5, 6, 7 }, page.Items.Select(item => item.Observation.Sequence));
        Assert.Equal("ActivityCompleted", page.Items[^1].Observation.Name);
        Assert.Null(page.Items[0].PreviousActivityExecutionId);
        Assert.Equal("loop-1", page.Items[0].NextActivityExecutionId);
        Assert.Equal("retry-2", page.Items[^1].PreviousActivityExecutionId);
        Assert.Null(page.Items[^1].NextActivityExecutionId);
        Assert.Equal(Enumerable.Range(0, 5).Select(value => (long)value), page.Items.Select(item => item.Position));
    }

    [Fact]
    public async Task Frozen_membership_excludes_late_occurrences_and_refresh_preserves_exact_anchor()
    {
        var f = new Fixture(); f.Append("a"); f.Append("b");
        var initial = await f.Reader.ReadOccurrencesAsync(new("run", limit: 1));
        f.Append("late", at: At.AddDays(-1));
        var frozen = await f.Reader.ReadOccurrencesAsync(new("run", 1, initial.SequenceToken, position: 1));
        Assert.Equal(2, frozen.TotalCount); Assert.Equal("b", Assert.Single(Ids(frozen)));
        Assert.True(frozen.HasNewerHistory); Assert.Equal(initial.SequenceToken, frozen.SequenceToken);
        var absent = await f.Reader.ReadOccurrencesAsync(new("run", 1, initial.SequenceToken, anchorActivityExecutionId: "late"));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.TargetUnavailable, absent.Availability);
        Assert.Empty(absent.Items);
        var refreshed = await f.Reader.ReadOccurrencesAsync(new("run", 1, anchorActivityExecutionId: "b"));
        Assert.Equal(3, refreshed.TotalCount); Assert.Equal("b", Assert.Single(Ids(refreshed)));
        Assert.NotEqual(initial.SequenceToken, refreshed.SequenceToken);
    }

    [Fact]
    public async Task Distant_ordinal_and_anchor_reads_do_not_traverse_lifecycle_pages()
    {
        var f = new Fixture();
        for (var i = 0; i < 200; i++)
        {
            f.Append($"occurrence-{i}");
            for (var continuation = 0; continuation < 30; continuation++)
                f.Append($"occurrence-{i}", name: "ActivityContinuationStarted");
        }
        // Remove browse indexes, retaining the actual first-event lookup and derived occurrence
        // receipts. A reader that walks ordinary event pages cannot satisfy these seeks.
        f.State.HistoryEvents.Clear(); f.State.HistoryCategoryEvents.Clear(); f.State.HistoryNameEvents.Clear();
        string? window = null;
        foreach (var position in new[] { 0, 100, 199, 23, 198, 1 })
        {
            var page = await f.Reader.ReadOccurrencesAsync(new("run", 3, window, position));
            window ??= page.SequenceToken;
            Assert.Equal(200, page.TotalCount); Assert.Equal(position, page.SelectedPosition);
            Assert.InRange(page.Items.Count, 1, 3);
            Assert.Equal($"occurrence-{position}", page.Items.Single(item => item.Position == position).Observation.ActivityExecutionId);
        }
        var exact = await f.Reader.ReadOccurrencesAsync(new("run", 3, window, anchorActivityExecutionId: "occurrence-123"));
        Assert.Equal(123, exact.SelectedPosition);
    }

    [Fact]
    public async Task Nested_membership_is_fixed_and_unknown_ancestry_never_becomes_complete_looking()
    {
        var f = new Fixture(); f.Append("root"); f.Append("left", parent: "root");
        f.Append("child", parent: "left"); f.Append("right", parent: "root");
        var known = await f.Reader.ReadOccurrencesAsync(new("run", activityExecutionId: "left", includeDescendants: true));
        Assert.Equal(new[] { "left", "child" }, Ids(known));
        // Current parent enrichment must not move a previously committed occurrence to a new scope.
        f.Append("right", parent: "left", name: "ActivityContinuationStarted");
        var frozen = await f.Reader.ReadOccurrencesAsync(new("run", sequenceToken: known.SequenceToken,
            activityExecutionId: "left", includeDescendants: true));
        Assert.Equal(new[] { "left", "child" }, Ids(frozen));
        f.Append("unknown", known: false);
        f.Append("unknown", name: "ActivityContinuationStarted", parent: "left");
        var stillFrozen = await f.Reader.ReadOccurrencesAsync(new("run", sequenceToken: known.SequenceToken,
            activityExecutionId: "left", includeDescendants: true));
        Assert.Equal(new[] { "left", "child" }, Ids(stillFrozen));
        var incomplete = await f.Reader.ReadOccurrencesAsync(new("run", activityExecutionId: "left", includeDescendants: true));
        Assert.Equal("scope-ancestry-unknown", incomplete.Reason); Assert.Null(incomplete.TotalCount);
        Assert.Empty(incomplete.Items); Assert.Null(incomplete.SequenceToken);
        Assert.Equal(5, (await f.Reader.ReadOccurrencesAsync(new("run"))).TotalCount);
    }

    [Fact]
    public async Task Prefix_retention_invalidates_old_window_and_never_reintroduces_a_later_continuation()
    {
        var f = new Fixture(); f.Append("old"); f.Append("retained");
        var first = await f.Reader.ReadOccurrencesAsync(new("run"));
        f.PurgePrefix(1); f.Append("old", name: "ActivityContinuationStarted");
        var invalidated = await f.Reader.ReadOccurrencesAsync(new("run", sequenceToken: first.SequenceToken));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Invalidated, invalidated.Availability);
        Assert.Empty(invalidated.Items);
        var next = await f.Reader.ReadOccurrencesAsync(new("run"));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Partial, next.Availability);
        Assert.Equal("retained-prefix-purged", next.Reason);
        Assert.Equal("retained", Assert.Single(Ids(next))); Assert.Equal(1, next.TotalCount);
        Assert.Equal(0, next.SelectedPosition);
    }

    [Fact]
    public async Task Missing_first_observation_is_integrity_failure_not_an_empty_or_rebased_occurrence()
    {
        var f = new Fixture(); var first = f.Append("one"); f.Append("one", name: "ActivityContinuationStarted");
        f.State.HistoryEventsById.Remove((f.Access.Scope, "run", first.HistoryEventId));
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => f.Reader.ReadOccurrencesAsync(new("run")).AsTask());
    }

    [Fact]
    public async Task Gap_and_missing_recording_are_unavailable_while_empty_recorded_segment_is_distinct()
    {
        var f = new Fixture();
        var missing = await f.Reader.ReadOccurrencesAsync(new("run"));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Unavailable, missing.Availability); Assert.Null(missing.TotalCount);
        f.Append("a"); var key = (f.Access.Scope, "run");
        f.State.HistoryHeads[key] = f.State.HistoryHeads[key] with { HasGaps = true };
        var gap = await f.Reader.ReadOccurrencesAsync(new("run"));
        Assert.Equal("history-gap-unlocated", gap.Reason); Assert.Null(gap.SequenceToken); Assert.Empty(gap.Items);
        f.State.HistoryHeads[key] = f.State.HistoryHeads[key] with { HasGaps = false, PurgedThroughSequence = 1, FirstSequence = 2 };
        var empty = await f.Reader.ReadOccurrencesAsync(new("run"));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Partial, empty.Availability); Assert.Equal(0, empty.TotalCount);
        Assert.NotNull(empty.SequenceToken); Assert.Null(empty.SelectedPosition);
    }

    [Fact]
    public async Task Tokens_reject_changed_scope_authorization_limit_purpose_and_tampering()
    {
        var f = new Fixture(); f.Append("a");
        var first = await f.Reader.ReadOccurrencesAsync(new("run", accessBinding: "operator-a"));
        foreach (var query in new[]
        {
            new RuntimeHistoryOccurrenceQuery("run", sequenceToken: first.SequenceToken, accessBinding: "operator-b"),
            new RuntimeHistoryOccurrenceQuery("run", 2, first.SequenceToken, accessBinding: "operator-a"),
            new RuntimeHistoryOccurrenceQuery("other", sequenceToken: first.SequenceToken, accessBinding: "operator-a"),
            new RuntimeHistoryOccurrenceQuery("run", sequenceToken: first.SequenceToken, activityExecutionId: "a", accessBinding: "operator-a"),
            new RuntimeHistoryOccurrenceQuery("run", sequenceToken: first.SequenceToken + "tamper", accessBinding: "operator-a")
        }) await Assert.ThrowsAsync<ArgumentException>(() => f.Reader.ReadOccurrencesAsync(query).AsTask());
        var events = await f.Reader.ReadPageAsync(new("run"));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Reader.ReadOccurrencesAsync(new("run", sequenceToken: events.CatchUpToken)).AsTask());
        f.Access.Scope = "scope-b";
        await Assert.ThrowsAsync<ArgumentException>(() => f.Reader.ReadOccurrencesAsync(new("run", sequenceToken: first.SequenceToken, accessBinding: "operator-a")).AsTask());
    }

    [Fact]
    public async Task Unknown_lifecycle_observations_do_not_silently_reduce_a_complete_looking_occurrence_sequence()
    {
        var f = new Fixture(); f.Append("known");
        var old = await f.Reader.ReadOccurrencesAsync(new("run"));
        f.Append("future", name: "FutureActivityKind");
        var frozen = await f.Reader.ReadOccurrencesAsync(new("run", sequenceToken: old.SequenceToken));
        Assert.Equal("known", Assert.Single(Ids(frozen)));
        var unavailable = await f.Reader.ReadOccurrencesAsync(new("run"));
        Assert.Equal("occurrence-observation-unknown", unavailable.Reason);
        Assert.Null(unavailable.TotalCount); Assert.Empty(unavailable.Items);
        f.PurgePrefix(2); f.Append("future", name: "ActivityContinuationStarted");
        Assert.Equal("occurrence-observation-unknown", (await f.Reader.ReadOccurrencesAsync(new("run"))).Reason);
    }

    [Fact]
    public async Task Run_deletion_removes_occurrence_receipts_and_retires_existing_windows()
    {
        var f = new Fixture(); f.Append("a");
        var first = await f.Reader.ReadOccurrencesAsync(new("run"));
        f.State.DeleteHistoryForRun(f.Access.Scope, "run");
        var retired = await f.Reader.ReadOccurrencesAsync(new("run", sequenceToken: first.SequenceToken));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Invalidated, retired.Availability);
        Assert.Empty(f.State.HistoryOccurrences); Assert.Empty(f.State.HistoryOccurrenceIds);
        Assert.Empty(f.State.HistoryOccurrenceScopes); Assert.Empty(f.State.HistoryOccurrenceUnknownAncestry);
    }

    [Theory]
    [InlineData("FutureActivityKind", RuntimeHistoryAuthority.CommittedTransition, 1, false)]
    [InlineData("ActivityStarted", RuntimeHistoryAuthority.DeliveryObservation, 1, false)]
    [InlineData("ActivityStarted", RuntimeHistoryAuthority.CommittedTransition, 2, false)]
    [InlineData("ActivityCompleted", RuntimeHistoryAuthority.CommittedTransition, 1, true)]
    public void Eligibility_retains_known_committed_occurrences_without_guessing_future_schema(string kind,
        RuntimeHistoryAuthority authority, int version, bool eligible) =>
        Assert.Equal(eligible, RuntimeHistoryOccurrence.IsEligible(new("event", "run", 1,
            RuntimeHistoryEventCategory.ActivityLifecycle, kind, At, "activity") { Authority = authority, SchemaVersion = version }));

    [Fact]
    public async Task Cancellation_is_preserved_and_query_rejects_ambiguous_or_unbounded_targets()
    {
        var f = new Fixture(); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Reader.ReadOccurrencesAsync(new("run"), canceled.Token).AsTask());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeHistoryOccurrenceQuery("run", 101));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeHistoryOccurrenceQuery("run", position: -1));
        Assert.Throws<ArgumentException>(() => new RuntimeHistoryOccurrenceQuery("run", includeDescendants: true));
        Assert.Throws<ArgumentException>(() => new RuntimeHistoryOccurrenceQuery("run", position: 0, anchorActivityExecutionId: "a"));
    }

    private static string[] Ids(RuntimeHistoryOccurrencePage page) => page.Items.Select(item => item.Observation.ActivityExecutionId!).ToArray();

    private sealed class Fixture
    {
        public InMemoryRuntimeCheckpointStoreState State { get; } = new();
        public Accessor Access { get; } = new();
        public InMemoryRuntimeHistoryStore Reader { get; }
        private long sequence;
        public Fixture() => Reader = new(State, Access, new HmacRuntimeRecoveryContinuationCodec(Options.Create(
            new RuntimeRecoveryContinuationOptions { SigningKey = "occurrence-projection-test-key-at-least-32-bytes" })));
        public RuntimeHistoryEvent Append(string activity, string name = "ActivityStarted", string? parent = null,
            DateTimeOffset? at = null, bool known = true)
        {
            var item = new RuntimeHistoryEvent($"event-{++sequence}", "run", sequence, RuntimeHistoryEventCategory.ActivityLifecycle,
                name, at ?? At, activity, executableNodeId: "same-authored-node")
            { Authority = RuntimeHistoryAuthority.CommittedTransition, PhysicalCommitId = $"physical-{sequence}", RecordedAt = At,
                ParentActivityExecutionId = parent, ActivityAncestryKnown = known, IterationId = activity, AttemptId = activity };
            State.PublishHistoryEvent(Access.Scope, item, "seed:" + sequence);
            var key = (Access.Scope, "run");
            State.HistoryHeads[key] = State.HistoryHeads.TryGetValue(key, out var previous)
                ? previous with { LastSequence = sequence } : new("run", 1, sequence, item.PhysicalCommitId!, At, Policy, true, false, 0);
            return item;
        }
        // A read-side simulation of an owner's acknowledged purge, not a retention implementation.
        public void PurgePrefix(long through)
        {
            var key = (Access.Scope, "run");
            State.HistoryHeads[key] = State.HistoryHeads[key] with { PurgedThroughSequence = through, FirstSequence = through + 1 };
            foreach (var receipt in State.HistoryOccurrences[key].Where(item => item.Sequence <= through))
                State.HistoryEventsById.Remove((Access.Scope, "run", receipt.EventId));
        }
    }
    private sealed class Accessor : IPersistenceAccessContextAccessor
    {
        public string Scope { get; set; } = "scope-a";
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(Scope));
    }
}

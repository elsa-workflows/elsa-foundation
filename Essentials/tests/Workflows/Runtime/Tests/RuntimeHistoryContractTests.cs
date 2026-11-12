using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeHistoryContractTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly RuntimeHistoryCapturePolicy Policy = new("test-history", 1, true, ["ActivityStarted"]);

    [Fact]
    public void Legacy_envelope_keeps_its_constructor_payload_and_unspecified_authority()
    {
        Assert.NotNull(typeof(RuntimeHistoryEvent).GetConstructor([
            typeof(string), typeof(string), typeof(long), typeof(RuntimeHistoryEventCategory), typeof(string),
            typeof(DateTimeOffset), typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(JsonElement?), typeof(IReadOnlyDictionary<string, string>)]));
        using var document = JsonDocument.Parse("{\"legacy\":true}");
        var legacy = new RuntimeHistoryEvent("event", "run", 3, RuntimeHistoryEventCategory.ActivityLifecycle,
            "ActivityStarted", At, payload: document.RootElement);
        document.Dispose();

        Assert.True(legacy.Payload!.Value.GetProperty("legacy").GetBoolean());
        Assert.Equal(RuntimeHistoryAuthority.Unspecified, legacy.Authority);
        Assert.Null(legacy.PhysicalCommitId);
        Assert.Null(legacy.CheckpointOrdinal);
        Assert.Equal(1, legacy.SchemaVersion);
        // New categories are appended so persisted numeric categories retain their meaning.
        Assert.Equal(6, (int)RuntimeHistoryEventCategory.OperationalLifecycle);
        Assert.Equal(7, (int)RuntimeHistoryEventCategory.DeliveryObservation);
    }

    [Fact]
    public void Policy_and_batch_snapshot_the_callers_collections()
    {
        var kinds = new List<string> { "ActivityStarted", "activityStarted", "ActivityStarted" };
        var policy = new RuntimeHistoryCapturePolicy("test-history", 1, true, kinds);
        var candidates = new List<RuntimeHistoryCandidate> { new(Event(), 0) };
        var batch = new RuntimeHistoryBatch(policy, "checkpoint", At, true, candidates);
        kinds.Clear();
        candidates.Clear();

        Assert.Equal(new[] { "ActivityStarted", "activityStarted" }, policy.SupportedKinds);
        Assert.Single(batch.Candidates);
        Assert.True(policy.IsEquivalentTo(new("test-history", 1, true, ["activityStarted", "ActivityStarted"])));
        Assert.False(policy.IsEquivalentTo(new("test-history", 1, false, ["ActivityStarted", "activityStarted"])));
        Assert.False(policy.IsEquivalentTo(new("test-history", 2, true, ["ActivityStarted", "activityStarted"])));
    }

    [Theory]
    [InlineData("sequence")]
    [InlineData("physical-commit")]
    [InlineData("recorded-time")]
    [InlineData("payload")]
    public void Candidates_refuse_provider_positions_and_copied_payloads(string invalid)
    {
        var value = new RuntimeHistoryEvent("event", "run", invalid == "sequence" ? 1 : 0,
            RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", At,
            payload: invalid == "payload" ? JsonSerializer.SerializeToElement(new { secret = "private" }) : null)
        {
            PhysicalCommitId = invalid == "physical-commit" ? "physical" : null,
            RecordedAt = invalid == "recorded-time" ? At : null
        };

        Assert.Throws<ArgumentException>(() => new RuntimeHistoryCandidate(value, 0));
    }

    [Theory]
    [InlineData("different-run")]
    [InlineData("different-checkpoint")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-ordinal")]
    [InlineData("skipped-ordinal")]
    public void Batch_refuses_ambiguous_or_cross_owner_order(string invalid)
    {
        var second = Event(id: invalid == "duplicate-id" ? "event" : "other",
            run: invalid == "different-run" ? "other-run" : "run",
            checkpoint: invalid == "different-checkpoint" ? "other-checkpoint" : "checkpoint");
        var ordinal = invalid == "duplicate-ordinal" ? 0 : invalid == "skipped-ordinal" ? 2 : 1;

        Assert.Throws<ArgumentException>(() => new RuntimeHistoryBatch(Policy, "checkpoint", At, false,
            [new(Event(), 0), new(second, ordinal)]));
    }

    [Fact]
    public void Batch_refuses_unadmitted_kind_and_arbitrary_metadata_but_accepts_safe_metadata()
    {
        Assert.Throws<InvalidOperationException>(() => Batch(Event(name: "Unadmitted")));
        Assert.Throws<InvalidOperationException>(() => Batch(Event(metadata: new Dictionary<string, string>
        {
            ["authorization"] = "private-token"
        })));
        var metadata = new Dictionary<string, string> { ["history.status"] = "Running" };
        var batch = Batch(Event(metadata: metadata));
        metadata["history.status"] = "Changed";

        Assert.Equal("Running", batch.Candidates[0].Event.Metadata["history.status"]);
        Assert.Throws<InvalidOperationException>(() => Batch(Event(metadata: new Dictionary<string, string>
        {
            ["history.status"] = new('a', 2049)
        })));
    }

    [Fact]
    public void Delivery_observation_cannot_impersonate_a_committed_transition()
    {
        var observation = new RuntimeHistoryEvent("event", "run", 0, RuntimeHistoryEventCategory.DeliveryObservation,
            "ActivityStarted", At, checkpointId: "checkpoint") { Authority = RuntimeHistoryAuthority.DeliveryObservation };

        Assert.Throws<InvalidOperationException>(() => Batch(observation));
    }

    [Fact]
    public void Bookmark_mutation_must_belong_to_the_exact_event_run_execution_and_handle()
    {
        var summary = new ActivityExecutionBookmarkSummary(new("bookmark", "run", "activity", "resume"), "Signal", "hash", At, null);
        var mutation = new RuntimeHistoryBookmarkMutation(summary, RuntimeHistoryBookmarkStatus.Consumed);
        var correct = new RuntimeHistoryEvent("event", "run", 0, RuntimeHistoryEventCategory.BookmarkLifecycle,
            "BookmarkConsumed", At, activityExecutionId: "activity") { BookmarkId = "bookmark" };
        Assert.Same(mutation, new RuntimeHistoryCandidate(correct, 0, mutation).BookmarkMutation);

        foreach (var identity in new[]
        {
            summary.Identity with { WorkflowExecutionId = "other-run" },
            summary.Identity with { ActivityExecutionId = "other-execution" },
            summary.Identity with { BookmarkId = "other-handle" }
        })
            Assert.Throws<ArgumentException>(() => new RuntimeHistoryCandidate(correct, 0, mutation with { Bookmark = summary with { Identity = identity } }));
    }

    [Fact]
    public void Replay_identity_ignores_provider_position_but_retains_logical_ordinal_and_mutation()
    {
        var original = Event();
        var positioned = RuntimeHistoryIdentity.Position(original, 7, "physical", At.AddMinutes(1));
        Assert.Equal(RuntimeHistoryIdentity.Compute(original), RuntimeHistoryIdentity.Compute(positioned));
        Assert.NotEqual(RuntimeHistoryIdentity.Compute(original, 0), RuntimeHistoryIdentity.Compute(original, 1));

        var summary = new ActivityExecutionBookmarkSummary(new("bookmark", "run", "activity", "resume"), "Signal", "hash", At, null);
        var @event = new RuntimeHistoryEvent("event", "run", 0, RuntimeHistoryEventCategory.BookmarkLifecycle,
            "BookmarkConsumed", At, activityExecutionId: "activity") { BookmarkId = "bookmark" };
        var consumed = new RuntimeHistoryCandidate(@event, 0, new(summary, RuntimeHistoryBookmarkStatus.Consumed, "selected"));
        var removed = new RuntimeHistoryCandidate(@event, 0, new(summary, RuntimeHistoryBookmarkStatus.Removed, "cancelled"));
        Assert.NotEqual(RuntimeHistoryIdentity.Compute(consumed), RuntimeHistoryIdentity.Compute(removed));
    }

    [Fact]
    public void Opaque_identity_and_access_fields_do_not_alias_at_nulls_or_invalid_utf16()
    {
        Assert.NotEqual(RuntimeHistoryIdentity.CreateId("a\0b", "c", "d"), RuntimeHistoryIdentity.CreateId("a", "b\0c", "d"));
        Assert.NotEqual(RuntimeHistoryIdentity.CreateId("a", "b", "\ud800"), RuntimeHistoryIdentity.CreateId("a", "b", "\ud801"));
        Assert.NotEqual(RuntimeHistoryIdentity.CreateBinding([null]), RuntimeHistoryIdentity.CreateBinding([""]));
        Assert.NotEqual(RuntimeHistoryIdentity.CreateBinding(["a", "b"]), RuntimeHistoryIdentity.CreateBinding(["b", "a"]));
        Assert.NotEqual(RuntimeHistoryIdentity.CreateBinding(["\ud800"]), RuntimeHistoryIdentity.CreateBinding(["\ud801"]));
    }

    [Fact]
    public void Position_preserves_causation_and_exact_occurrence_context()
    {
        var source = new RuntimeHistoryEvent("event", "run", 0, RuntimeHistoryEventCategory.ActivityLifecycle,
            "ActivityStarted", At, "activity", "node", "checkpoint", "incident")
        {
            Authority = RuntimeHistoryAuthority.CommittedTransition, CheckpointOrdinal = 2,
            AttemptId = "attempt", BookmarkId = "bookmark", ParentActivityExecutionId = "parent",
            ActivityAncestryKnown = true, ExecutionScopeId = "scope", IterationId = "iteration",
            Causation = new("cause", "related-run", "delivery", "command", "work")
        };
        var positioned = RuntimeHistoryIdentity.Position(source, 4, "physical", At.AddSeconds(1));

        Assert.Equal(RuntimeHistoryIdentity.Compute(source), RuntimeHistoryIdentity.Compute(positioned));
        Assert.Equal(4, positioned.Sequence);
        Assert.Equal("physical", positioned.PhysicalCommitId);
        Assert.Equal(At.AddSeconds(1), positioned.RecordedAt);
        Assert.Equal(source.Causation, positioned.Causation);
        Assert.True(positioned.ActivityAncestryKnown);
        Assert.Equal(source.AttemptId, positioned.AttemptId);
        Assert.Equal(source.IterationId, positioned.IterationId);
    }

    [Fact]
    public void Batch_roundtrip_keeps_safe_semantic_identity_and_source_proof()
    {
        var source = Batch(Event()) with { SourceFingerprint = new('a', 64) };
        var copy = JsonSerializer.Deserialize<RuntimeHistoryBatch>(JsonSerializer.Serialize(source))!;

        Assert.True(source.Policy.IsEquivalentTo(copy.Policy));
        Assert.Equal(source.SourceFingerprint, copy.SourceFingerprint);
        Assert.Equal(RuntimeHistoryIdentity.Compute(source.Candidates[0]), RuntimeHistoryIdentity.Compute(copy.Candidates[0]));
        RuntimeHistorySafety.Validate(copy, "run");
        Assert.Throws<InvalidOperationException>(() => RuntimeHistorySafety.Validate(copy, "other-run"));
        Assert.Throws<InvalidOperationException>(() => RuntimeHistorySafety.Validate(copy with { SourceFingerprint = "unbounded-raw-source" }, "run"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void History_and_bookmark_queries_share_bounded_page_limits(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeHistoryQuery("run", limit));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeHistoryBookmarkQuery("run", limit));
    }

    [Fact]
    public void Descendants_require_an_exact_execution_and_search_has_a_finite_bound()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeHistoryQuery("run", includeDescendants: true));
        Assert.Throws<ArgumentException>(() => new RuntimeHistoryBookmarkQuery("run", includeDescendants: true));
        Assert.Throws<ArgumentException>(() => new RuntimeHistoryQuery("run", search: new('a', 257)));
        Assert.Throws<ArgumentException>(() => new RuntimeHistoryQuery("run", continuationToken: " "));
        var exact = new RuntimeHistoryQuery("run", activityExecutionId: "execution", includeDescendants: true, search: new('a', 256));
        Assert.Equal("execution", exact.ActivityExecutionId);
        Assert.Equal(100, exact.Limit);
    }

    [Fact]
    public void Empty_search_window_can_continue_without_claiming_no_matches_in_the_whole_history()
    {
        // History deliberately does not use RuntimeStorePage's empty-items/no-continuation invariant.
        var page = new RuntimeHistoryPage(null, [], 1000, 100, 100, "next-window", "catch-up");
        Assert.Empty(page.Items);
        Assert.Equal(100, page.ScannedThroughSequence);
        Assert.Equal("next-window", page.NextContinuationToken);
    }

    private static RuntimeHistoryBatch Batch(RuntimeHistoryEvent @event) => new(Policy, "checkpoint", At, true, [new(@event, 0)]);

    private static RuntimeHistoryEvent Event(string id = "event", string run = "run", string checkpoint = "checkpoint",
        string name = "ActivityStarted", IReadOnlyDictionary<string, string>? metadata = null) =>
        new(id, run, 0, RuntimeHistoryEventCategory.ActivityLifecycle, name, At, checkpointId: checkpoint, metadata: metadata)
        {
            Authority = RuntimeHistoryAuthority.CommittedTransition
        };
}

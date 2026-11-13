using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeHistoryOwnerContractTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true, ["ActivityContinuationStarted"]);

    [Fact]
    public void Optional_checkpoint_causation_is_omitted_for_legacy_wire_and_survives_an_owned_roundtrip()
    {
        var commit = Legacy();
        Assert.False(JsonSerializer.SerializeToElement(commit.Checkpoint).TryGetProperty("HistoryCausation", out _));
        var cause = new RuntimeHistoryCausation("event", "source-run", "delivery", "command", "work");
        var captured = commit with { Checkpoint = commit.Checkpoint with { HistoryCausation = cause } };
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(commit), RuntimeCheckpointCommitFingerprint.Compute(captured));
        var restored = JsonSerializer.Deserialize<RuntimeCheckpoint>(JsonSerializer.Serialize(captured.Checkpoint))!;
        Assert.Equal(cause, restored.HistoryCausation);
    }

    [Fact]
    public void Checkpoint_source_causation_identity_preserves_opaque_code_units()
    {
        var commit = Legacy();
        var first = commit with { Checkpoint = commit.Checkpoint with { HistoryCausation = new(DeliveryId: "\ud800") } };
        var second = commit with { Checkpoint = commit.Checkpoint with { HistoryCausation = new(DeliveryId: "\ud801") } };
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(first), RuntimeCheckpointCommitFingerprint.Compute(second));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Absent_older_and_unsupported_profiles_do_not_activate_an_owner_factory(int version)
    {
        var calls = 0;
        var policy = version == 0 ? null : new RuntimeHistoryCapturePolicy("runtime-history-v3", version, true, ["ActivityContinuationStarted"]);
        var result = RuntimeHistorySourceFacts.ForPolicy(policy, () => { calls++; return [SourceFact("source")]; });

        Assert.Null(result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Source_inputs_preserve_a_wide_native_group_order_and_snapshot()
    {
        var supplied = Enumerable.Range(0, 4097).Select(index => SourceFact("source-" + index)).ToList();
        var captured = RuntimeHistorySourceFacts.ForPolicy(Policy, supplied)!;
        supplied.Clear();

        Assert.Equal(4097, captured.Count);
        Assert.Equal("source-0", captured[0].SubjectId);
        Assert.Equal("source-4096", captured[^1].SubjectId);
        Assert.Null(RuntimeHistorySourceFacts.ForPolicy(Policy, Array.Empty<RuntimeHistorySourceFact>()));
    }

    [Fact]
    public void Source_facts_snapshot_outcomes_and_refuse_invalid_or_unbounded_identifiers()
    {
        var outcomes = new List<string> { "Done" };
        var fact = new RuntimeHistorySourceFact(RuntimeHistorySourceFactKind.ActivityContinuationCompleted, "source", At, outcomeNames: outcomes);
        outcomes.Clear();
        Assert.Equal(new[] { "Done" }, fact.OutcomeNames);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeHistorySourceFact((RuntimeHistorySourceFactKind)999, "source", At));
        Assert.Throws<ArgumentException>(() => SourceFact(new('a', 1025)));
        Assert.Throws<ArgumentException>(() => new RuntimeHistorySourceFact(RuntimeHistorySourceFactKind.ControlApplied, "source", At, reasonCode: new('a', 129)));
        Assert.Throws<ArgumentException>(() => new RuntimeHistorySourceFact(RuntimeHistorySourceFactKind.ActivityContinuationCompleted, "source", At, outcomeNames: [" "]));
        Assert.Equal("IncidentRecorded", RuntimeHistorySourceFacts.EventName(RuntimeHistorySourceFactKind.IncidentCreated));
    }

    [Fact]
    public void Value_delta_keeps_exact_checkpoint_work_and_evidence_without_aliasing()
    {
        var ids = new List<string> { "evidence-1", "evidence-2" };
        var delta = new RuntimeCheckpointValueCapture("checkpoint", "work", ids);
        ids.Clear();
        var copy = JsonSerializer.Deserialize<RuntimeCheckpointValueCapture>(JsonSerializer.Serialize(delta))!;

        Assert.Equal("checkpoint", copy.CheckpointId);
        Assert.Equal("work", copy.SchedulerWorkItemId);
        Assert.Equal(new[] { "evidence-1", "evidence-2" }, copy.EvidenceIds);
        var empty = new RuntimeCheckpointValueCapture("reprojection", null, []);
        Assert.Empty(empty.EvidenceIds);
        Assert.Null(empty.SchedulerWorkItemId);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("blank")]
    [InlineData("unbounded")]
    public void Value_delta_refuses_ambiguous_or_invalid_evidence(string invalid)
    {
        string[] ids = invalid switch
        {
            "duplicate" => ["evidence", "evidence"],
            "blank" => [" "],
            _ => [new('a', 1025)]
        };
        Assert.Throws<ArgumentException>(() => new RuntimeCheckpointValueCapture("checkpoint", "work", ids));
    }

    [Theory]
    [InlineData(WorkflowExecutionCommandKind.InvokeActivity, ActivityValueCapturePhase.Invoke)]
    [InlineData(WorkflowExecutionCommandKind.ResumeBookmark, ActivityValueCapturePhase.Resume)]
    [InlineData(WorkflowExecutionCommandKind.CompleteActivity, ActivityValueCapturePhase.Structural)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint, ActivityValueCapturePhase.Unknown)]
    public void Source_phase_uses_actual_work_and_keeps_unknown_kinds_explicit(WorkflowExecutionCommandKind kind, ActivityValueCapturePhase phase)
    {
        var work = new RuntimeSchedulerWorkItem("work", "run", "command", kind, "envelope", "key", At, At);
        var source = ActivityValueCaptureOrigin.FromWorkItem(work);
        Assert.Equal("run", source.WorkflowExecutionId);
        Assert.Equal("work", source.SchedulerWorkItemId);
        Assert.Equal(phase, source.Phase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Absent_and_empty_sources_retain_the_existing_golden_fingerprint(bool explicitEmpty)
    {
        var legacy = Legacy() with { HistorySourceFacts = explicitEmpty ? [] : null };
        Assert.Equal("af976cb76bbef3f226bc15cbbdae5d8b330ec98ae9f287ff18e15aadc2df1f1a", RuntimeCheckpointCommitFingerprint.Compute(legacy));
        if (!explicitEmpty)
            Assert.False(JsonSerializer.SerializeToElement(legacy).TryGetProperty(nameof(RuntimeCheckpointCommit.HistorySourceFacts), out _));
    }

    [Fact]
    public void Source_sidecar_is_snapshotted_and_opaque_semantic_inputs_participate_in_identity()
    {
        var supplied = new List<RuntimeHistorySourceFact> { SourceFact("source") };
        var source = Legacy() with { HistorySourceFacts = supplied };
        supplied.Clear();
        Assert.Single(source.HistorySourceFacts!);
        Assert.Same(source.HistorySourceFacts, (source with { StateChanges = source.StateChanges.WithPostCommitOutbox([]) }).HistorySourceFacts);
        var copy = JsonSerializer.Deserialize<RuntimeHistorySourceFact>(JsonSerializer.Serialize(source.HistorySourceFacts[0]))!;
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(source), RuntimeCheckpointCommitFingerprint.Compute(source with { HistorySourceFacts = [copy] }));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(source), RuntimeCheckpointCommitFingerprint.Compute(Legacy()));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(Legacy() with { HistorySourceFacts = [SourceFact("\ud800")] }),
            RuntimeCheckpointCommitFingerprint.Compute(Legacy() with { HistorySourceFacts = [SourceFact("\ud801")] }));
    }

    [Fact]
    public async Task Legacy_accumulator_does_not_claim_support_or_invoke_the_owner_factory()
    {
        IRuntimeActivityExecutionInspectionAccumulator accumulator = new LegacyAccumulator();
        var calls = 0;
        var state = State();
        var projection = await accumulator.BuildProjectionWithSourceAsync(state, "checkpoint", At,
            () => { calls++; return new("run", "work", ActivityValueCapturePhase.Invoke); });

        Assert.False(accumulator.SupportsCheckpointValueCapture);
        Assert.Equal(0, calls);
        Assert.Equal("activity", projection.ActivityExecutionId);
        Assert.Null(projection.CheckpointValueCapture);
        Assert.False(JsonSerializer.SerializeToElement(projection).TryGetProperty(nameof(ActivityExecutionInspectionProjection.CheckpointValueCapture), out _));
    }

    [Theory]
    [InlineData("checkpoint-A", true)]
    [InlineData("checkpoint-B", false)]
    public void Inspection_merge_never_reattributes_an_earlier_raw_delta(string checkpointId, bool sameCheckpoint)
    {
        var state = State();
        var delta = new RuntimeCheckpointValueCapture("checkpoint-A", "work-A", ["evidence-A"]);
        var earlier = ActivityExecutionInspectionProjection.FromState(state, "checkpoint-A", At) with { CheckpointValueCapture = delta };
        var merged = earlier.Merge(state, checkpointId, At.AddSeconds(1));

        Assert.Equal(checkpointId, merged.LastCheckpointId);
        if (sameCheckpoint) Assert.Same(delta, merged.CheckpointValueCapture);
        else Assert.Null(merged.CheckpointValueCapture);
        Assert.Same(delta, earlier.CheckpointValueCapture);
    }

    private static RuntimeHistorySourceFact SourceFact(string subject) => new(RuntimeHistorySourceFactKind.ActivityContinuationStarted, subject, At);

    private static RuntimeCheckpointCommit Legacy() => new("commit", new("checkpoint", "name", "waiter", At, [], new Dictionary<string, string>()),
        new(null, null, [], [], [], [], []), [], new Dictionary<string, string>());

    private static ActivityExecutionState State() => new(new("activity", "run", "node", "authored", "test", "1"),
        ActivityExecutionStatus.Running, null, 1, At, At, null, null, null, null, null,
        ActivitySchedulingProvenance.From("run", null, null, null, null, null, null, "test"), 0, [], [], 0, 0, new Dictionary<string, string>());

    private sealed class LegacyAccumulator : IRuntimeActivityExecutionInspectionAccumulator
    {
        public ValueTask<ActivityExecutionInspectionProjection> BuildProjectionAsync(ActivityExecutionState state,
            string checkpointId, DateTimeOffset committedAt, IReadOnlyCollection<string>? outcomeNames = null,
            IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks = null,
            IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents = null,
            IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ActivityExecutionInspectionProjection.FromState(state, checkpointId, committedAt,
                outcomeNames, bookmarks, incidents, valueSnapshots, metadata));
    }
}

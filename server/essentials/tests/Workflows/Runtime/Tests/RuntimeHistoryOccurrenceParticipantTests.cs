using System.Collections;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.History;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeHistoryMemoryParticipantTests
{
    [Fact]
    public async Task Occurrence_projection_shares_atomic_commit_replay_and_validation_failure_boundaries()
    {
        var policy = new RuntimeHistoryCapturePolicy("occurrence-participant", 1, true, ["ActivityStarted", "ActivityCompleted", "ActivityContinuationStarted"]);
        var f = await Fixture.Create(policy);
        RuntimeHistoryBatch Group(string logical, string eventId, string activity, string kind, bool starts = false) =>
            new(policy, logical, At, starts,
                [new(new RuntimeHistoryEvent(eventId, "run", 0, RuntimeHistoryEventCategory.ActivityLifecycle, kind, At,
                    activity, checkpointId: logical) { Authority = RuntimeHistoryAuthority.CommittedTransition, ActivityAncestryKnown = true }, 0)]);
        var first = Group("first", "first-event", "a", "ActivityStarted", true);
        var intrinsic = Group("intrinsic", "intrinsic-event", "b", "ActivityCompleted");
        var continuation = Group("continuation", "continuation-event", "a", "ActivityContinuationStarted");
        var commit = Commit("occurrence-physical", [first, intrinsic, continuation]);
        await f.Store.CommitAsync(commit, Immediate);
        await f.Store.CommitAsync(commit, Immediate);
        var page = await f.Reader.ReadOccurrencesAsync(new("run"));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { "a", "b" }, page.Items.Select(item => item.Observation.ActivityExecutionId));
        Assert.Equal(new long[] { 1, 2 }, page.Items.Select(item => item.Observation.Sequence));
        Assert.All(page.Items, item => Assert.Equal("occurrence-physical", item.Observation.PhysicalCommitId));
        Assert.Single(f.Store.ListCommits());

        var newOccurrence = Group("new", "new-event", "c", "ActivityStarted");
        var conflict = Group("first", "first-event", "a", "ActivityCompleted");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await f.Store.CommitAsync(Commit("rejected-physical", [newOccurrence, conflict]), Immediate));
        var unchanged = await f.Reader.ReadOccurrencesAsync(new("run", sequenceToken: page.SequenceToken));
        Assert.Equal(2, unchanged.TotalCount); Assert.Equal(page.UpperSequence, unchanged.UpperSequence);
        Assert.DoesNotContain(unchanged.Items, item => item.Observation.ActivityExecutionId == "c");
        Assert.Single(f.Store.ListCommits());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(31, 1)]
    [InlineData(1550031, 50001)]
    [InlineData(3100000, 100000)]
    public void Occurrence_bound_lookup_uses_logarithmic_probes_independent_of_prior_lifecycle_volume(long sequence, int expected)
    {
        var source = new CountedOccurrenceIndex(100_000);
        Assert.Equal(expected, InMemoryRuntimeHistoryStore.FindOccurrenceAfter(source, sequence));
        Assert.InRange(source.Probes, 1, 18);
    }

    private sealed class CountedOccurrenceIndex(int count) : IReadOnlyList<InMemoryRuntimeCheckpointStoreState.OccurrenceReceipt>
    {
        public int Probes { get; private set; }
        public int Count => count;
        public InMemoryRuntimeCheckpointStoreState.OccurrenceReceipt this[int index]
        {
            get { Probes++; return new("activity-" + index, "event-" + index, 31L * (index + 1), true, []); }
        }
        public IEnumerator<InMemoryRuntimeCheckpointStoreState.OccurrenceReceipt> GetEnumerator() =>
            throw new InvalidOperationException("Bounded lookup cannot enumerate the occurrence index.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

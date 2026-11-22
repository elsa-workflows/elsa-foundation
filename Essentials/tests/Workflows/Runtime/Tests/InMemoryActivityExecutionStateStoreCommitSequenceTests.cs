using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Spec 1229 FR-016: the in-memory store answers the inspection reads the same way as the EF store: rows whose range
/// reaches a commit, by first commit sequence and keyset; rows without a range; the lost-update sum.
/// </summary>
public sealed class InMemoryActivityExecutionStateStoreCommitSequenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Rows_page_by_commit_sequence_range_with_a_keyset_and_lost_updates_sum_with_a_fallback()
    {
        IActivityExecutionStateStore store = new InMemoryActivityExecutionStateStore();
        await store.SaveAsync(Ranged("wf", "a", 1, 3) with { LostUpdateWarningCount = 1 });
        await store.SaveAsync(Ranged("wf", "b", 2, 9));
        await store.SaveAsync(Ranged("wf", "c", 2, 6));
        await store.SaveAsync(Ranged("wf", "d", 5, 6) with { VariableWrites = [LostUpdate()] });
        await store.SaveAsync(State("wf", "legacy"));
        await store.SaveAsync(Ranged("other", "a", 1, 9) with { LostUpdateWarningCount = 4 });

        var reaching4 = await store.ListByCommitSequenceAsync(new ActivityExecutionStateCommitSequenceQuery("wf", 4, 10));
        var first = await store.ListByCommitSequenceAsync(new ActivityExecutionStateCommitSequenceQuery("wf", 0, 2));
        var second = await store.ListByCommitSequenceAsync(new ActivityExecutionStateCommitSequenceQuery("wf", 0, 2,
            new ActivityExecutionStateCommitSequenceKey(first[^1].FirstCommitSequence!.Value, first[^1].Execution.ActivityExecutionId)));
        var unnumbered = await store.ListWithoutCommitSequenceAsync("wf");

        Assert.Equal(["b", "c", "d"], reaching4.Select(row => row.Execution.ActivityExecutionId));
        Assert.Equal(["a", "b"], first.Select(row => row.Execution.ActivityExecutionId));
        // Rows tied on their first commit continue by id.
        Assert.Equal(["c", "d"], second.Select(row => row.Execution.ActivityExecutionId));
        Assert.Equal(["legacy"], unnumbered.Select(row => row.Execution.ActivityExecutionId));
        // A row saved without the count is counted from its records.
        Assert.Equal(2, await store.SumLostUpdateWarningCountAsync("wf"));
    }

    private static VariableWriteRecord LostUpdate() =>
        new("scope", "count", new VariableWriter(VariableWriteSource.Capture, "node", "d"), VariableCommitOutcome.LostUpdateWarning, 0, 1, 2, 2, Now)
        {
            CommitSequence = 6
        };

    private static ActivityExecutionState Ranged(string workflow, string id, long first, long last) =>
        State(workflow, id) with
        {
            Transitions = [new ActivityLifecycleStamp(ActivityLifecycleTransitionKind.Scheduled, first), new ActivityLifecycleStamp(ActivityLifecycleTransitionKind.Completed, last)],
            FirstCommitSequence = first,
            LastCommitSequence = last
        };

    private static ActivityExecutionState State(string workflow, string id) =>
        new(new ActivityExecution(id, workflow, "node", "authored-node", "Test", "1.0.0"),
            ActivityExecutionStatus.Completed, null, Now, Now, Now, null, null, null, null, null, [], [], 0, 0, new Dictionary<string, string>());
}

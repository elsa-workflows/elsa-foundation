using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Xunit;
using static Elsa.Workflows.Runtime.Api.Tests.VariableLifecycle.VariableLifecycleRecords;

namespace Elsa.Workflows.Runtime.Api.Tests.VariableLifecycle;

/// <summary>
/// Spec 1229 T009, T012, T015, T018: the read model over committed record sets. Order and current value, the
/// concurrent-overwrite rule (research R-3) for every shape the spec names, accepted overrides, alterations, legacy
/// records, cursor paging, the journal and run-time paths.
/// </summary>
public sealed class VariableLifecycleInspectionTests
{
    private static VariableLifecycleInspection Build(params ActivityExecutionState[] rows) => VariableLifecycleInspection.Build(rows, []);

    private static VariableWriteEntry Entry(VariableLifecycleInspection model, string rowId, int index = 0) =>
        model.Entries.Single(entry => entry.Row?.Execution.ActivityExecutionId == rowId && entry.EntryId == VariableWriteEntryId.ForActivity(rowId, index));

    [Fact]
    public void Writes_in_commits_4_7_9_are_listed_in_that_order_and_the_last_is_the_current_value()
    {
        var model = Build(
            Row("c", "node-c", stamps: [Scheduled(8), Completed(9)], writes: [Write("c", "node-c", 9, 2, 3)]),
            Row("a", "node-a", stamps: [Scheduled(3), Completed(4)], writes: [Write("a", "node-a", 4, 0, 1)]),
            Row("b", "node-b", stamps: [Scheduled(6), Completed(7)], writes: [Write("b", "node-b", 7, 1, 2)]));

        Assert.Equal([4L, 7L, 9L], model.Entries.Select(entry => entry.CommitSequence!.Value));
        Assert.Equal(["a", "b", "c"], model.Entries.Select(entry => entry.Row!.Execution.ActivityExecutionId));
        Assert.Single(model.Entries, entry => entry.IsCurrentValue);
        Assert.True(Entry(model, "c").IsCurrentValue);
    }

    [Fact]
    public void Zero_one_zero_in_one_segment_keeps_both_writes_with_their_own_commit_sequence()
    {
        var model = Build(Row("a", "node-a", stamps: [Scheduled(1), Completed(3)], writes:
        [
            Write("a", "node-a", 2, 0, 1, frameRevision: 1),
            Write("a", "node-a", 3, 1, 2, frameRevision: 2)
        ]));

        Assert.Equal([2L, 3L], model.Entries.Select(entry => entry.CommitSequence!.Value));
        Assert.All(model.Entries, entry => Assert.True(entry.IsApplied));
        Assert.True(model.Entries[1].IsCurrentValue);
        // One invocation never overwrites itself.
        Assert.All(model.Entries, entry => Assert.Null(entry.Overwrote));
    }

    [Fact]
    public void A_write_that_changed_nothing_is_not_applied()
    {
        var model = Build(Row("a", "node-a", stamps: [Scheduled(1)], writes: [Write("a", "node-a", 2, 1, 1, VariableCommitOutcome.LostUpdateWarning, baseVersion: 0)]));

        var entry = Assert.Single(model.Entries);
        Assert.False(entry.IsApplied);
        Assert.False(entry.IsCurrentValue);
    }

    [Fact]
    public void A_write_that_changed_nothing_and_was_no_lost_update_is_not_listed()
    {
        var model = Build(Row("a", "node-a", stamps: [Scheduled(1)], writes:
        [
            Write("a", "node-a", 2, 1, 1, VariableCommitOutcome.ReturnedValue, baseVersion: 0),
            Write("a", "node-a", 3, 1, 2)
        ]));

        var entry = Assert.Single(model.Entries);
        Assert.Equal(2, entry.Record.NewVersion);
    }

    [Fact]
    public void Loop_iterations_are_separate_variable_instances()
    {
        var model = Build(
            Row("loop", "node-loop", stamps: [Scheduled(1), Completed(9)]),
            Row("i1", "node-body", parent: "loop", stamps: [Scheduled(2), Completed(3)], writes: [Write("i1", "node-body", 3, 0, 1, key: "x", frame: "frame-i1")]),
            Row("i2", "node-body", parent: "loop", causedBy: ["i1"], stamps: [Scheduled(4), Completed(5)], writes: [Write("i2", "node-body", 5, 0, 1, key: "x", frame: "frame-i2")]));

        Assert.Equal(2, model.Entries.Select(entry => entry.InstanceKey).Distinct().Count());
        Assert.All(model.Entries, entry => Assert.True(entry.IsCurrentValue));
        Assert.All(model.Entries, entry => Assert.Null(entry.Overwrote));
    }

    [Fact]
    public void Two_parallel_branches_with_a_stale_base_show_one_lost_update_naming_both_writers_and_branches()
    {
        var model = Build(
            Row("p", "node-parallel", stamps: [Scheduled(1), Started(1)], status: ActivityExecutionStatus.Running),
            Row("l", "node-left", parent: "p", branchId: "left", stamps: [Scheduled(2), Completed(3)], writes: [Write("l", "node-left", 3, 0, 1, VariableCommitOutcome.Current, baseVersion: 0)]),
            Row("r", "node-right", parent: "p", branchId: "right", stamps: [Scheduled(2), Completed(4)], writes: [Write("r", "node-right", 4, 1, 2, VariableCommitOutcome.LostUpdateWarning, baseVersion: 0)]));

        var lost = Entry(model, "r");
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, lost.Record.Outcome);
        Assert.Same(Entry(model, "l"), lost.Overwrote);
        Assert.Equal("left", lost.Overwrote!.Row!.BranchId);
        Assert.Equal("right", lost.Row!.BranchId);
        Assert.True(lost.IsCurrentValue);
    }

    [Fact]
    public void A_blind_set_on_a_third_branch_is_a_concurrent_overwrite_not_a_lost_update()
    {
        var model = Build(
            Row("p", "node-parallel", stamps: [Scheduled(1)], status: ActivityExecutionStatus.Running),
            Row("l", "node-left", parent: "p", stamps: [Scheduled(2), Completed(3)], writes: [Write("l", "node-left", 3, 0, 1, VariableCommitOutcome.Current, baseVersion: 0)]),
            Row("s", "node-set", parent: "p", stamps: [Scheduled(2), Completed(5)], writes: [Write("s", "node-set", 5, 1, 2, VariableCommitOutcome.Blind, source: VariableWriteSource.Intrinsic)]));

        var set = Entry(model, "s");
        Assert.Same(Entry(model, "l"), set.Overwrote);
        Assert.False(set.Record.IsLostUpdate);
    }

    [Fact]
    public void Sequential_writes_are_ordered_through_the_completed_child_that_scheduled_the_next()
    {
        var model = Build(
            Row("seq", "node-seq", stamps: [Scheduled(1), Completed(6)]),
            Row("a", "node-a", parent: "seq", stamps: [Scheduled(2), Completed(3)], writes: [Write("a", "node-a", 3, 0, 1)]),
            Row("b", "node-b", parent: "seq", causedBy: ["a"], stamps: [Scheduled(4), Completed(5)], writes: [Write("b", "node-b", 5, 1, 2)]));

        Assert.Null(Entry(model, "b").Overwrote);
    }

    [Fact]
    public void A_write_after_a_parallel_completes_is_ordered_after_every_branch()
    {
        var model = Build(
            Row("seq", "node-seq", stamps: [Scheduled(1), Completed(9)]),
            Row("p", "node-parallel", parent: "seq", stamps: [Scheduled(2), Completed(6)]),
            Row("l", "node-left", parent: "p", stamps: [Scheduled(3), Completed(4)], writes: [Write("l", "node-left", 4, 0, 1)]),
            Row("r", "node-right", parent: "p", stamps: [Scheduled(3), Completed(5)], writes: [Write("r", "node-right", 5, 1, 2)]),
            Row("after", "node-after", parent: "seq", causedBy: ["p"], stamps: [Scheduled(7), Completed(8)], writes: [Write("after", "node-after", 8, 2, 3)]));

        Assert.Null(Entry(model, "after").Overwrote);
        // The branches themselves are unordered.
        Assert.Same(Entry(model, "l"), Entry(model, "r").Overwrote);
    }

    [Fact]
    public void Flowchart_join_arrivals_order_the_joined_write_after_every_arrival()
    {
        var model = Build(
            Row("fc", "node-flowchart", stamps: [Scheduled(1)], status: ActivityExecutionStatus.Running),
            Row("x", "node-x", parent: "fc", stamps: [Scheduled(2), Completed(3)], writes: [Write("x", "node-x", 3, 0, 1)]),
            Row("y", "node-y", parent: "fc", stamps: [Scheduled(2), Completed(4)], writes: [Write("y", "node-y", 4, 1, 2)]),
            Row("join", "node-join", parent: "fc", scheduler: "y", causedBy: ["y", "x"], stamps: [Scheduled(5), Completed(6)], writes: [Write("join", "node-join", 6, 2, 3)]),
            Row("late", "node-late", parent: "fc", scheduler: "y", stamps: [Scheduled(5), Completed(7)], writes: [Write("late", "node-late", 7, 3, 4)]));

        Assert.Null(Entry(model, "join").Overwrote);
        // Scheduled by y alone, the late node is not ordered after the join's write.
        Assert.Same(Entry(model, "join"), Entry(model, "late").Overwrote);
    }

    [Fact]
    public void Loop_iterations_are_ordered_by_the_iteration_that_caused_the_next_and_without_the_cause_they_are_not()
    {
        ActivityExecutionState[] Rows(bool withCause) =>
        [
            Row("while", "node-while", stamps: [Scheduled(1)], status: ActivityExecutionStatus.Running),
            Row("i1", "node-body", parent: "while", stamps: [Scheduled(2), Completed(3)], writes: [Write("i1", "node-body", 3, 0, 1)]),
            Row("i2", "node-body", parent: "while", causedBy: withCause ? ["i1"] : null, stamps: [Scheduled(4), Completed(5)], writes: [Write("i2", "node-body", 5, 1, 2)])
        ];

        Assert.Null(Entry(Build(Rows(withCause: true)), "i2").Overwrote);
        Assert.NotNull(Entry(Build(Rows(withCause: false)), "i2").Overwrote);
    }

    [Fact]
    public void A_child_scheduled_before_its_container_wrote_overwrites_that_write_concurrently()
    {
        var model = Build(
            Row("c", "node-container", stamps: [Scheduled(1), Started(2)], status: ActivityExecutionStatus.Running, writes: [Write("c", "node-container", 5, 0, 1, source: VariableWriteSource.Intrinsic)]),
            Row("child", "node-child", parent: "c", stamps: [Scheduled(3), Started(6), Completed(6)], writes: [Write("child", "node-child", 6, 1, 2)]));

        Assert.Same(Entry(model, "c"), Entry(model, "child").Overwrote);
    }

    [Fact]
    public void A_container_write_after_its_child_ended_is_ordered_after_the_child_write()
    {
        var model = Build(
            Row("c", "node-container", stamps: [Scheduled(1), Started(2), Completed(8)], writes: [Write("c", "node-container", 8, 1, 2)]),
            Row("d", "node-child", parent: "c", stamps: [Scheduled(3), Completed(5)], writes: [Write("d", "node-child", 5, 0, 1)]),
            Row("e", "node-late", parent: "c", stamps: [Scheduled(3), Completed(9)], writes: [Write("e", "node-late", 9, 2, 3)]));

        Assert.Null(Entry(model, "c").Overwrote);
        // A child still running when the container wrote is not ordered after that write.
        Assert.Same(Entry(model, "c"), Entry(model, "e").Overwrote);
    }

    [Fact]
    public void A_write_on_suspension_and_again_on_completion_does_not_overwrite_itself()
    {
        var model = Build(Row("a", "node-a", stamps: [Scheduled(1), Suspended(2), Resumed(4), Completed(5)], writes:
        [
            Write("a", "node-a", 2, 0, 1),
            Write("a", "node-a", 5, 1, 2)
        ]));

        Assert.All(model.Entries, entry => Assert.Null(entry.Overwrote));
    }

    [Fact]
    public void Retry_attempts_of_one_logical_invocation_never_overwrite_each_other()
    {
        var lineage = new ActivityExecutionAttemptLineage(2, "a1", "a1");
        var model = Build(
            Row("p", "node-p", stamps: [Scheduled(1)], status: ActivityExecutionStatus.Running),
            Row("a1", "node-a", parent: "p", stamps: [Scheduled(2), Faulted(3)], writes: [Write("a1", "node-a", 3, 0, 1)], status: ActivityExecutionStatus.Faulted),
            Row("a2", "node-a", parent: "p", stamps: [Scheduled(4), Completed(5)], attempt: lineage, writes: [Write("a2", "node-a", 5, 1, 2)]));

        Assert.Null(Entry(model, "a2").Overwrote);
    }

    [Fact]
    public void An_automatic_retry_is_ordered_after_what_its_first_attempt_was_ordered_after()
    {
        var model = Build(
            Row("seq", "node-seq", stamps: [Scheduled(1)], status: ActivityExecutionStatus.Running),
            Row("a", "node-a", parent: "seq", stamps: [Scheduled(2), Completed(3)], writes: [Write("a", "node-a", 3, 0, 1)]),
            Row("b1", "node-b", parent: "seq", causedBy: ["a"], stamps: [Scheduled(4), Faulted(5)], status: ActivityExecutionStatus.Faulted),
            Row("b2", "node-b", parent: "seq", attempt: new ActivityExecutionAttemptLineage(2, "b1", "b1"), stamps: [Scheduled(6), Completed(7)],
                writes: [Write("b2", "node-b", 7, 1, 2)]));

        Assert.Null(Entry(model, "b2").Overwrote);
    }

    [Fact]
    public void Re_execution_after_ModifyVariable_is_ordered_after_the_alteration()
    {
        var alteration = Write("alteration:job-1", null!, 4, 1, 2, source: VariableWriteSource.Alteration, frameRevision: 2);
        var rows = new[]
        {
            Row("p", "node-p", stamps: [Scheduled(1)], status: ActivityExecutionStatus.Running),
            Row("a1", "node-a", parent: "p", stamps: [Scheduled(2), Faulted(3)], writes: [Write("a1", "node-a", 3, 0, 1)], status: ActivityExecutionStatus.Faulted),
            Row("a2", "node-a", parent: "p", metadata: RetryOf("a1"), stamps: [Scheduled(5), Completed(6)], writes: [Write("a2", "node-a", 6, 2, 3)])
        };

        var model = VariableLifecycleInspection.Build(rows, [AlterationJob("job-1", alteration)]);

        Assert.Equal(["a1", null, "a2"], model.Entries.Select(entry => entry.Row?.Execution.ActivityExecutionId));
        Assert.Equal(VariableWriteSource.Alteration, model.Entries[1].Record.Writer.Source);
        Assert.Equal("job-1", model.Entries[1].AlterationJobId);
        Assert.Null(Entry(model, "a2").Overwrote);
    }

    [Fact]
    public void A_write_not_scheduled_after_an_alteration_overwrites_it_concurrently()
    {
        var alteration = Write("alteration:job-1", null!, 4, 1, 2, source: VariableWriteSource.Alteration, frameRevision: 2);
        var rows = new[]
        {
            Row("p", "node-p", stamps: [Scheduled(1)], status: ActivityExecutionStatus.Running),
            Row("b", "node-b", parent: "p", stamps: [Scheduled(2), Completed(5)], writes: [Write("b", "node-b", 5, 2, 3)])
        };

        var model = VariableLifecycleInspection.Build(rows, [AlterationJob("job-1", alteration)]);

        Assert.Same(model.Entries[0], Entry(model, "b").Overwrote);
        // The alteration itself is listed in its position, never traced as an overwrite.
        Assert.Null(model.Entries[0].Overwrote);
    }

    [Fact]
    public void A_Fault_lost_update_is_listed_not_applied_and_an_accepted_completion_resolves_it()
    {
        var model = Build(
            Row("p", "node-p", stamps: [Scheduled(1)], status: ActivityExecutionStatus.Running),
            Row("l", "node-left", parent: "p", stamps: [Scheduled(2), Completed(3)], writes: [Write("l", "node-left", 3, 0, 1)]),
            Row("r", "node-right", parent: "p", stamps: [Scheduled(2), Faulted(4)], status: ActivityExecutionStatus.Faulted,
                writes: [Write("r", "node-right", 4, 1, 1, VariableCommitOutcome.LostUpdateFault, baseVersion: 0)]),
            Row("r2", "node-right", parent: "p", metadata: RetryOf("r"), stamps: [Scheduled(5), Completed(5)],
                writes: [Write("r2", "node-right", 5, 1, 2, VariableCommitOutcome.AcceptedOverride, source: VariableWriteSource.AcceptedCompletion)]));

        var refused = Entry(model, "r");
        Assert.False(refused.IsApplied);
        Assert.False(refused.IsCurrentValue);
        Assert.Null(refused.Overwrote);
        var accepted = Entry(model, "r2");
        Assert.True(accepted.IsApplied);
        Assert.True(accepted.IsCurrentValue);
        Assert.Same(refused, accepted.ResolvesEntry);
        Assert.Contains(refused, model.Entries);
    }

    [Fact]
    public void The_current_value_follows_the_frame_version_when_it_is_known()
    {
        var rows = new[]
        {
            Row("a", "node-a", stamps: [Scheduled(1), Completed(2)], writes: [Write("a", "node-a", 2, 0, 1)]),
            Row("b", "node-b", stamps: [Scheduled(3), Completed(4)], writes: [Write("b", "node-b", 4, 1, 2)])
        };
        var instance = VariableWriteEntry.InstanceKeyOf(RootFrame, RootScope, "count");

        var model = VariableLifecycleInspection.Build(rows, [], new Dictionary<string, long> { [instance] = 1 });

        Assert.True(Entry(model, "a").IsCurrentValue);
        Assert.False(Entry(model, "b").IsCurrentValue);
    }

    [Fact]
    public void Records_without_a_commit_sequence_come_first_and_are_approximate()
    {
        var model = Build(
            Row("new", "node-new", stamps: [Scheduled(5), Completed(6)], writes: [Write("new", "node-new", 6, 1, 2)]),
            Row("old", "node-old", writes: [Write("old", "node-old", null, 0, 1)]));

        Assert.Equal(["old", "new"], model.Entries.Select(entry => entry.Row!.Execution.ActivityExecutionId));
        Assert.True(model.Entries[0].OrderApproximate);
        Assert.False(model.Entries[1].OrderApproximate);
        // Order unknown: nothing is claimed as an overwrite.
        Assert.Null(model.Entries[1].Overwrote);
        Assert.True(model.HistoryRecorded);
    }

    [Fact]
    public void An_instance_without_write_history_reads_as_no_history_recorded()
    {
        var model = Build(Row("legacy", "node-legacy"));

        Assert.Empty(model.Entries);
        Assert.False(model.HistoryRecorded);
    }

    [Fact]
    public void A_page_ending_inside_a_commit_continues_exactly_there()
    {
        var model = Build(
            Row("a", "node-a", stamps: [Scheduled(1)], writes:
            [
                Write("a", "node-a", 2, 0, 1, key: "x"),
                Write("a", "node-a", 2, 0, 1, key: "y"),
                Write("a", "node-a", 2, 0, 1, key: "z")
            ]),
            Row("b", "node-b", stamps: [Scheduled(3)], writes: [Write("b", "node-b", 4, 1, 2, key: "x")]));

        var cursor = VariableLifecyclePageCursor.Of(model.Entries[1].CommitSequence, model.Entries[1].PositionInCommit);
        var decoded = VariableLifecyclePageCursor.Decode(cursor.Encode())!.Value;
        var rest = model.Entries.Where(entry => decoded.IsBefore(entry.CommitSequence, entry.PositionInCommit)).ToArray();

        Assert.Equal(model.Entries.Skip(2), rest);
        Assert.Equal([0, 1, 2, 0], model.Entries.Select(entry => entry.PositionInCommit));
    }

    [Fact]
    public void An_invalid_cursor_or_entry_id_is_refused()
    {
        Assert.Throws<ArgumentException>(() => VariableLifecyclePageCursor.Decode("not-a-cursor"));
        Assert.Null(VariableWriteEntryId.Decode("not-an-entry"));
        Assert.Equal(("activityExecution", "row/1", 0, 3), VariableWriteEntryId.Decode(VariableWriteEntryId.ForActivity("row/1", 3)));
        Assert.Equal(("alteration", "job-1", 2, 1), VariableWriteEntryId.Decode(VariableWriteEntryId.ForAlteration("job-1", 2, 1)));
    }

    [Fact]
    public void The_journal_lists_every_commit_once_in_order_with_its_transitions_and_writes()
    {
        var model = Build(
            Row("p", "node-p", stamps: [Scheduled(1), Started(1), Completed(6)], executionSequence: 1),
            Row("a", "node-a", parent: "p", stamps: [Scheduled(2), Suspended(3), Resumed(5), Completed(5)], writes: [Write("a", "node-a", 5, 0, 1)], executionSequence: 2),
            Row("b", "node-b", parent: "p", stamps: [Scheduled(2), Completed(4)], writes: [Write("b", "node-b", 4, 1, 2)], executionSequence: 3));

        var journal = model.BuildJournal();

        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L], journal.Select(commit => commit.CommitSequence!.Value));
        Assert.Equal(2, journal[1].Transitions.Count);
        Assert.Contains(journal[4].Transitions, transition => transition.Stamp.Kind == ActivityLifecycleTransitionKind.Resumed);
        Assert.Same(Entry(model, "b"), Assert.Single(journal[3].Writes));
    }

    [Fact]
    public void The_journal_keeps_faulted_cancelled_and_superseded_transitions_in_their_commits()
    {
        var model = Build(
            Row("f", "node-f", stamps: [Scheduled(1), Faulted(2)], status: ActivityExecutionStatus.Faulted),
            Row("c", "node-c", stamps: [Scheduled(1), (ActivityLifecycleTransitionKind.Cancelled, 3)], status: ActivityExecutionStatus.Cancelled),
            Row("s", "node-s", stamps: [Scheduled(1), Faulted(2), (ActivityLifecycleTransitionKind.Superseded, 4)], status: ActivityExecutionStatus.Faulted));

        var journal = model.BuildJournal();

        Assert.Equal([1L, 2L, 3L, 4L], journal.Select(commit => commit.CommitSequence!.Value));
        Assert.Equal(3, journal[0].Transitions.Count);
        Assert.Equal([ActivityLifecycleTransitionKind.Faulted, ActivityLifecycleTransitionKind.Faulted], journal[1].Transitions.Select(t => t.Stamp.Kind));
        Assert.Equal(ActivityLifecycleTransitionKind.Cancelled, Assert.Single(journal[2].Transitions).Stamp.Kind);
        Assert.Equal(ActivityLifecycleTransitionKind.Superseded, Assert.Single(journal[3].Transitions).Stamp.Kind);
    }

    [Fact]
    public void A_background_input_refresh_appears_in_the_commit_that_ran_on_the_new_evaluation()
    {
        var superseded = new ActivityInputSnapshot("bg", "contract", "binding", new Dictionary<string, ValueEnvelope>(), Now);
        var row = Row("bg", "node-bg", stamps: [Scheduled(1)], status: ActivityExecutionStatus.Running) with
        {
            Transitions = [new ActivityLifecycleStamp(ActivityLifecycleTransitionKind.Scheduled, 1), new ActivityLifecycleStamp(ActivityLifecycleTransitionKind.Started, 3, evaluation: 2)],
            InputRefreshes = [new ActivityInputRefresh(superseded, 2, [new VariableInputChange(RootScope, "count", 0, 1)], Now)]
        };

        var journal = Build(row).BuildJournal();

        var refresh = Assert.Single(journal.Single(commit => commit.CommitSequence == 3).InputRefreshes);
        Assert.Equal(2, refresh.Refresh.ReplacedByEvaluation);
        Assert.Equal(1, refresh.Refresh.Superseded.EvaluationNumber);
    }

    [Fact]
    public void Run_time_paths_number_loop_iterations_by_occurrence_whatever_the_row_ids()
    {
        VariableLifecycleInspection Instance(string prefix) => Build(
            Row($"{prefix}-loop", "node-loop", stamps: [Scheduled(1)]),
            Row($"{prefix}-i2", "node-body", parent: $"{prefix}-loop", stamps: [Scheduled(4)]),
            Row($"{prefix}-i1", "node-body", parent: $"{prefix}-loop", stamps: [Scheduled(2)]));

        var left = Instance("x");
        var right = Instance("y");

        Assert.Equal("node-loop[0]/node-body[1]", left.RunTimePathOf(left.FindRow("x-i2")!));
        Assert.Equal(left.RunTimePathOf(left.FindRow("x-i1")!), right.RunTimePathOf(right.FindRow("y-i1")!));
        Assert.Equal(left.SignatureOf(left.BuildJournal()[1]), right.SignatureOf(right.BuildJournal()[1]));
    }
}

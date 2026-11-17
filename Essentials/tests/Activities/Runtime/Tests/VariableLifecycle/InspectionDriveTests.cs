using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Spec 1229 T004, T009, T012: real runs. The runtime records the scheduling causes and each row's lost-update count,
/// and the read model built from what the run committed marks the lost update of a parallel stale-base run and nothing
/// for a sequential one.
/// </summary>
public sealed class InspectionDriveTests
{
    private static readonly string[] Ids = ["actexec-root", "actexec-a", "actexec-b", "actexec-c", "actexec-d", "actexec-e", "actexec-f"];

    [Fact]
    public async Task A_parallel_stale_base_run_shows_one_lost_update_overwriting_the_other_branch_and_counts_it_on_the_row()
    {
        await using var harness = VariableLifecycleTestGraph.NewStaleBaseHarness("actexec-parallel", "actexec-left", "actexec-right");

        // Left writes 0 + 1; right, on the stale base 0, writes 0 + 2 over it: an applied lost update.
        var run = await harness.RunAsync(VariableLifecycleTestGraph.ParallelIncrements(typeof(DoublingIncrementActivity)));
        var model = VariableLifecycleInspection.Build(run.ActivityStates.ToArray(), []);

        var lost = Assert.Single(model.Entries, entry => entry.Record.Outcome == VariableCommitOutcome.LostUpdateWarning);
        Assert.Equal("node-right", lost.Row!.Execution.ExecutableNodeId);
        Assert.Equal("node-left", lost.Overwrote?.Row?.Execution.ExecutableNodeId);
        Assert.Equal(1, run.State("node-right").LostUpdateWarningCount);
        Assert.Null(run.State("node-left").LostUpdateWarningCount);
        Assert.Equal(1, model.Entries.Count(entry => entry.Overwrote is not null));
        Assert.True(lost.IsCurrentValue);
    }

    [Fact]
    public async Task A_stale_base_write_of_the_value_already_there_is_a_lost_update_that_applies_nothing()
    {
        await using var harness = VariableLifecycleTestGraph.NewStaleBaseHarness("actexec-parallel", "actexec-left", "actexec-right");

        var run = await harness.RunAsync(VariableLifecycleTestGraph.ParallelIncrements(typeof(IncrementActivity)));
        var model = VariableLifecycleInspection.Build(run.ActivityStates.ToArray(), []);

        var lost = Assert.Single(model.Entries, entry => entry.Record.Outcome == VariableCommitOutcome.LostUpdateWarning);
        Assert.False(lost.IsApplied);
        Assert.Null(lost.Overwrote);
        Assert.Equal(1, run.State("node-right").LostUpdateWarningCount);
    }

    [Fact]
    public async Task A_sequence_records_the_completed_child_as_the_cause_of_the_next_and_shows_no_overwrite()
    {
        await using var harness = NewSequenceHarness();
        var executable = WorkflowExecutionHarness.NewExecutable(
            SequenceOf(
                VariableLifecycleTestGraph.Increment("node-a", typeof(IncrementActivity)),
                VariableLifecycleTestGraph.Increment("node-b", typeof(IncrementActivity))),
            VariableLifecycleTestGraph.CountDeclaration());

        var run = await harness.RunAsync(executable);
        var model = VariableLifecycleInspection.Build(run.ActivityStates.ToArray(), []);

        run.AssertWorkflowCompleted();
        Assert.Equal(2, VariableLifecycleTestGraph.ReadCount(run));
        var first = run.State("node-a");
        Assert.Contains(first.Execution.ActivityExecutionId, run.State("node-b").Provenance.CausedBy ?? []);
        Assert.Equal(2, model.Entries.Count(entry => entry.IsApplied));
        Assert.All(model.Entries, entry => Assert.Null(entry.Overwrote));
        Assert.Single(model.Entries, entry => entry.IsCurrentValue && entry.Row!.Execution.ExecutableNodeId == "node-b");
    }

    [Fact]
    public async Task A_write_after_a_parallel_completes_is_ordered_after_both_branches()
    {
        await using var harness = NewSequenceHarness(staleBase: true);
        var executable = WorkflowExecutionHarness.NewExecutable(
            SequenceOf(
                VariableLifecycleTestGraph.Parallel(
                    VariableLifecycleTestGraph.Increment("node-left", typeof(IncrementActivity)),
                    VariableLifecycleTestGraph.Increment("node-right", typeof(IncrementActivity))),
                VariableLifecycleTestGraph.Increment("node-after", typeof(IncrementActivity))),
            VariableLifecycleTestGraph.CountDeclaration());

        var run = await harness.RunAsync(executable);
        var model = VariableLifecycleInspection.Build(run.ActivityStates.ToArray(), []);

        run.AssertWorkflowCompleted();
        Assert.Contains(run.State("node-parallel").Execution.ActivityExecutionId, run.State("node-after").Provenance.CausedBy ?? []);
        var after = Assert.Single(model.Entries, entry => entry.Row!.Execution.ExecutableNodeId == "node-after");
        Assert.Null(after.Overwrote);
        Assert.True(after.IsCurrentValue);
        // The journal lists every commit once, in order.
        var journal = model.BuildJournal();
        Assert.Equal(journal.Select(commit => commit.CommitSequence), journal.Select(commit => commit.CommitSequence).Order());
        Assert.Equal(journal.Count, journal.Select(commit => commit.CommitSequence).Distinct().Count());
    }

    private static WorkflowExecutionHarness NewSequenceHarness(bool staleBase = false)
    {
        var builder = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.ControlFlow.ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services));
        if (staleBase)
            builder = builder.ConfigureServices(services => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(
                services, new RuntimeReplaySafeFusionOptions { Enabled = false }));
        return builder.Build(Ids);
    }

    private static ExecutableNode SequenceOf(params ExecutableNode[] children) =>
        new(
            executableNodeId: "node-sequence",
            authoredActivityId: "authored-node-sequence",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = children.Select(child => child.ExecutableNodeId).ToArray() })));
}

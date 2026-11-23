using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Spec 1229 T004: real runs commit what the inspection read model needs. The runtime records the scheduling causes and
/// each row's lost-update count; the read model over such records is tested in Elsa.Workflows.Runtime.Api.Tests.
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

        var right = run.State("node-right");
        var lost = Assert.Single(right.VariableWrites!);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, lost.Outcome);
        Assert.NotEqual(lost.CurrentVersion, lost.NewVersion);
        Assert.Equal("node-left", lost.OverwrittenWriter!.ExecutableNodeId);
        Assert.Equal(1, right.LostUpdateWarningCount);
        Assert.Null(run.State("node-left").LostUpdateWarningCount);
        // Both branches were scheduled by the Parallel, so nothing orders one branch after the other.
        Assert.Equal(run.State("node-parallel").Execution.ActivityExecutionId, right.Provenance.SchedulingActivityExecutionId);
        Assert.DoesNotContain(run.State("node-left").Execution.ActivityExecutionId, right.Provenance.CausedBy ?? []);
    }

    [Fact]
    public async Task A_stale_base_write_of_the_value_already_there_is_a_lost_update_that_applies_nothing()
    {
        await using var harness = VariableLifecycleTestGraph.NewStaleBaseHarness("actexec-parallel", "actexec-left", "actexec-right");

        var run = await harness.RunAsync(VariableLifecycleTestGraph.ParallelIncrements(typeof(IncrementActivity)));

        var lost = Assert.Single(run.State("node-right").VariableWrites!);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, lost.Outcome);
        Assert.Equal(lost.CurrentVersion, lost.NewVersion);
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

        run.AssertWorkflowCompleted();
        Assert.Equal(2, VariableLifecycleTestGraph.ReadCount(run));
        var first = run.State("node-a");
        var second = run.State("node-b");
        Assert.Contains(first.Execution.ActivityExecutionId, second.Provenance.CausedBy ?? []);
        // The cause completed before the next was scheduled: its write happens before the next one's.
        Assert.True(first.Transitions!.Last(stamp => stamp.Kind == ActivityLifecycleTransitionKind.Completed).CommitSequence
                    <= second.Transitions!.First().CommitSequence);
        Assert.Null(run.State("node-sequence").Provenance.CausedBy);
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

        run.AssertWorkflowCompleted();
        var parallel = run.State("node-parallel");
        Assert.Contains(parallel.Execution.ActivityExecutionId, run.State("node-after").Provenance.CausedBy ?? []);
        // The Parallel completed after both branches, so the later write is ordered after both of them.
        var parallelEnd = parallel.Transitions!.Last(stamp => stamp.Kind == ActivityLifecycleTransitionKind.Completed).CommitSequence;
        foreach (var branch in new[] { "node-left", "node-right" })
            Assert.True(run.State(branch).Transitions!.Last().CommitSequence <= parallelEnd);
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

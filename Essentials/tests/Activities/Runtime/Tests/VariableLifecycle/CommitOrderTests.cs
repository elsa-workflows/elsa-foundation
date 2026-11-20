using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Spec 1221 FR-024 (T036): every write record carries the instance commit sequence of the checkpoint that committed
/// it, and every lifecycle transition of an invocation is stamped with one, so writes and transitions share one order
/// across all frames of the instance.
/// </summary>
public sealed class CommitOrderTests
{
    private static readonly string[] Ids = ["actexec-parallel", "actexec-left", "actexec-right"];

    [Fact]
    public async Task Writes_and_transitions_share_one_instance_order()
    {
        await using var harness = VariableLifecycleTestGraph.NewStaleBaseHarness(Ids);

        var run = await harness.RunAsync(VariableLifecycleTestGraph.ParallelIncrements(typeof(IncrementActivity)));

        run.AssertWorkflowCompleted();
        var first = Assert.Single(run.State("node-left").VariableWrites!);
        var second = Assert.Single(run.State("node-right").VariableWrites!);
        Assert.NotNull(first.CommitSequence);
        Assert.NotNull(second.CommitSequence);
        // The lost update committed after the write it overwrote.
        Assert.True(second.CommitSequence > first.CommitSequence);
        // Records name their frame and the digest of the value they leave.
        Assert.Equal(run.WorkflowState!.RootVariableFrame!.FrameId, second.FrameId);
        Assert.NotNull(second.Digest);

        foreach (var nodeId in new[] { "node-parallel", "node-left", "node-right" })
        {
            var transitions = run.State(nodeId).Transitions!;
            Assert.Equal(ActivityLifecycleTransitionKind.Scheduled, transitions[0].Kind);
            Assert.Contains(transitions, stamp => stamp.Kind == ActivityLifecycleTransitionKind.Started);
            Assert.Equal(transitions[0].CommitSequence, run.State(nodeId).FirstCommitSequence);
            Assert.Equal(transitions[^1].CommitSequence, run.State(nodeId).LastCommitSequence);
            Assert.Equal(ActivityLifecycleTransitionKind.Completed, transitions[^1].Kind);
            Assert.True(transitions.Zip(transitions.Skip(1)).All(pair => pair.First.CommitSequence < pair.Second.CommitSequence));
        }

        // A leaf's write is committed with its completion; both leaves start before either completes (fusion off).
        var left = run.State("node-left").Transitions!;
        var right = run.State("node-right").Transitions!;
        Assert.Equal(left[^1].CommitSequence, first.CommitSequence);
        Assert.Equal(right[^1].CommitSequence, second.CommitSequence);
        Assert.True(right.Single(stamp => stamp.Kind == ActivityLifecycleTransitionKind.Started).CommitSequence < left[^1].CommitSequence);
        Assert.True(run.State("node-parallel").Transitions![^1].CommitSequence > second.CommitSequence);
    }
}

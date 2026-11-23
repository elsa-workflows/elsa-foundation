using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// US1 / US3 reproduction drives (spec 1221 T010, T010a, T011, T024): a parallel read-modify-write is never silent.
/// Inline it totals correctly (no gap); where both branches read the same base before either commits, the second commit
/// applies with a lost-update warning naming both writers by default, and faults through the incident path under
/// <c>Fault</c>. Every applied write is in the write history.
/// </summary>
public sealed class ParallelLostUpdateTests
{
    private static readonly string[] Ids = ["actexec-parallel", "actexec-left", "actexec-right"];

    [Fact]
    public async Task Inline_parallel_increments_total_two_with_current_bases_and_no_lost_update()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness(Ids);

        var run = await harness.RunAsync(VariableLifecycleTestGraph.ParallelIncrements(typeof(IncrementActivity)));

        Assert.Equal(2, VariableLifecycleTestGraph.ReadCount(run));
        var left = Assert.Single(run.State("node-left").VariableWrites!);
        var right = Assert.Single(run.State("node-right").VariableWrites!);
        Assert.Equal(VariableCommitOutcome.Current, left.Outcome);
        Assert.Equal(VariableCommitOutcome.Current, right.Outcome);
        Assert.Equal((0, 1), (left.BaseVersion!.Value, left.NewVersion));
        Assert.Equal((1, 2), (right.BaseVersion!.Value, right.NewVersion));
        Assert.True(right.CommitOrder > left.CommitOrder);
        Assert.Equal(2, run.WorkflowState!.RootVariableFrame!.VersionOf(VariableLifecycleTestGraph.CountKey).Version);
    }

    [Fact]
    public async Task Default_mode_stale_base_applies_with_a_lost_update_warning_naming_both_writers()
    {
        await using var harness = VariableLifecycleTestGraph.NewStaleBaseHarness(Ids);

        var run = await harness.RunAsync(VariableLifecycleTestGraph.ParallelIncrements(typeof(IncrementActivity)));

        // Both read 0 and wrote 1: the second is a lost update, raised as a warning, not hidden as a no-op.
        run.AssertWorkflowCompleted();
        Assert.Equal(1, VariableLifecycleTestGraph.ReadCount(run));
        var first = Assert.Single(run.State("node-left").VariableWrites!);
        var second = Assert.Single(run.State("node-right").VariableWrites!);
        Assert.Equal(VariableCommitOutcome.Current, first.Outcome);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, second.Outcome);
        Assert.True(second.IsLostUpdate);
        Assert.Equal(0, second.BaseVersion);
        Assert.Equal(1, second.CurrentVersion);
        Assert.Equal("node-left", second.OverwrittenWriter!.ExecutableNodeId);
        Assert.Equal("actexec-left", second.OverwrittenWriter.InvocationId);
        Assert.Equal("node-right", second.Writer.ExecutableNodeId);
        Assert.Equal(VariableWriteSource.Capture, second.Writer.Source);
    }

    [Fact]
    public async Task Default_mode_stale_base_that_changes_the_value_keeps_the_overwritten_value()
    {
        await using var harness = VariableLifecycleTestGraph.NewStaleBaseHarness(Ids);
        var executable = VariableLifecycleTestGraph.ParallelIncrements(typeof(DoublingIncrementActivity));

        var run = await harness.RunAsync(executable);

        // Left wrote 0 + 1 = 1; right, on the stale base 0, wrote 0 + 2 = 2 over it: last write wins, with a warning
        // that keeps the overwritten 1 so it can be restored.
        Assert.Equal(2, VariableLifecycleTestGraph.ReadCount(run));
        var second = Assert.Single(run.State("node-right").VariableWrites!);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, second.Outcome);
        Assert.Equal(1, second.OverwrittenValue!.InlineValue!.Value.GetInt32());
        Assert.Equal(2, second.NewVersion);
    }

    [Fact]
    public async Task A_blind_write_is_never_a_lost_update()
    {
        await using var harness = VariableLifecycleTestGraph.NewStaleBaseHarness("actexec-blind");
        var node = VariableLifecycleTestGraph.Increment("node-blind", typeof(IncrementActivity), LostUpdateMode.Fault, readsCount: false);

        var run = await harness.RunAsync(Elsa.Activities.Testing.WorkflowExecutionHarness.NewExecutable(node, VariableLifecycleTestGraph.CountDeclaration()));

        Assert.Equal(42, VariableLifecycleTestGraph.ReadCount(run));
        var record = Assert.Single(run.State("node-blind").VariableWrites!);
        Assert.Equal(VariableCommitOutcome.Blind, record.Outcome);
        Assert.Null(record.BaseVersion);
    }

    [Fact]
    public async Task Fault_mode_real_lost_update_faults_the_step_through_the_incident_path_and_keeps_the_rejected_write()
    {
        await using var harness = VariableLifecycleTestGraph.NewStaleBaseHarness(Ids);

        var run = await harness.RunAsync(
            VariableLifecycleTestGraph.ParallelIncrements(typeof(IncrementActivity), LostUpdateMode.Fault),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        // The second step's write is refused: the value stays at the first step's 1.
        Assert.Equal(1, VariableLifecycleTestGraph.ReadCount(run));
        var right = run.State("node-right");
        Assert.Equal(ActivityExecutionStatus.Faulted, right.Status);
        Assert.Equal("VariableLostUpdate", right.SubStatus);
        var refused = Assert.Single(right.VariableWrites!);
        Assert.Equal(VariableCommitOutcome.LostUpdateFault, refused.Outcome);
        Assert.Equal(1, refused.RejectedValue!.InlineValue!.Value.GetInt32());
        Assert.Equal("node-left", refused.OverwrittenWriter!.ExecutableNodeId);

        var incident = Assert.Single(
            await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(harness.ExecutionId),
            item => item.ExecutableNodeId == "node-right");
        Assert.Contains("Lost update on variable 'var-count'", incident.Message, StringComparison.Ordinal);
        Assert.Contains("node-left", incident.Message, StringComparison.Ordinal);
    }

    // A refused completion applies none of its captures, so the history holds only the refusal: not the blind capture
    // the batch evaluated before it, which never applied.
    [Fact]
    public async Task Fault_mode_refusal_records_only_the_refused_write_and_applies_no_capture()
    {
        await using var harness = VariableLifecycleTestGraph.NewStaleBaseHarness(Ids);
        var root = VariableLifecycleTestGraph.Parallel(
            VariableLifecycleTestGraph.Increment("node-left", typeof(IncrementActivity)),
            WithCapture(VariableLifecycleTestGraph.Increment("node-right", typeof(PairIncrementActivity), LostUpdateMode.Fault), nameof(IncrementedPair.Other), OtherKey));
        var executable = Elsa.Activities.Testing.WorkflowExecutionHarness.NewExecutable(
            root,
            VariableLifecycleTestGraph.CountDeclaration(),
            new RuntimeVariableDeclaration(OtherKey, "other", VariableLifecycleTestGraph.Int32Type, ValueProtectionPolicy.InstanceInline));

        var run = await harness.RunAsync(executable, allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        var right = run.State("node-right");
        Assert.Equal(ActivityExecutionStatus.Faulted, right.Status);
        var refused = Assert.Single(right.VariableWrites!);
        Assert.Equal(VariableLifecycleTestGraph.CountKey, refused.VariableKey);
        Assert.Equal(VariableCommitOutcome.LostUpdateFault, refused.Outcome);
        Assert.Equal(0, run.WorkflowState!.RootVariableFrame!.VersionOf(OtherKey).Version);
    }

    private const string OtherKey = "var-a-other";

    // Adds a capture of the given result projection into another workflow variable.
    private static ExecutableNode WithCapture(ExecutableNode node, string projection, string variableKey)
    {
        var template = node.OutputCaptures.Values.First();
        var captures = node.OutputCaptures.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        captures[projection] = new RuntimeOutputCapture(
            projection,
            $"{Elsa.Workflows.Runtime.Services.Values.RuntimeWorkflowStateSeed.VariableValueIdPrefix}other",
            template.Type,
            template.Lifecycle,
            template.Storage,
            captureOnSuccessfulCompletion: true,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RuntimeMetadataKeys.TargetVariableReferenceKey] = variableKey,
                [RuntimeMetadataKeys.VariableName] = "other"
            });
        return new ExecutableNode(
            node.ExecutableNodeId, node.AuthoredActivityId, node.ActivityType, node.ActivityTypeVersion, node.DescriptorType,
            node.DescriptorPayload, node.InputBindings, node.Metadata, activityContract: node.ActivityContract, outputCaptures: captures);
    }
}

/// <summary>Two captured projections: the increment and a second value.</summary>
public sealed record IncrementedPair([property: Output] int Value, [property: Output] int Other);

/// <summary>An External leaf returning <c>Count + 1</c> and a second value.</summary>
public sealed class PairIncrementActivity : Activity<IncrementedPair>
{
    [ActivityInput]
    public int Count { get; set; }

    protected override ValueTask<ActivityTransition<IncrementedPair>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new IncrementedPair(Count + 1, 7), ActivityOutcomes.Done));
}

/// <summary>An External leaf whose right-hand instance adds 2, so a stale-base write changes the value.</summary>
public sealed class DoublingIncrementActivity : Activity<Incremented>
{
    [ActivityInput]
    public int Count { get; set; }

    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(
        ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(
            new Incremented(Count + (context.ExecutableNodeId == "node-right" ? 2 : 1)),
            ActivityOutcomes.Done));
}

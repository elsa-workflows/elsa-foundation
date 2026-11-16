using System.Text.Json;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Activities.Flowchart.Tests.FlowchartConformanceCorpus;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// Spec 1208 US4 end to end in the engine: a node that selects several outcomes is an inclusive fork. Each selected
/// outcome's path runs; an unselected outcome's path is dead (ADR 0064), so a join behind all of them runs once after
/// the live arrivals. An empty selection ends that path without stopping a parallel branch.
/// </summary>
public sealed class FlowchartInclusiveForkTests
{
    [Theory]
    [InlineData(FlowchartPolicyKinds.DeadPathJoin)]
    [InlineData(FlowchartPolicyKinds.ReachabilityJoin)]
    public async Task Two_of_three_selected_outcomes_run_their_paths_and_the_join_runs_once_after_both(string joinPolicy)
    {
        var run = await RunAsync(joinPolicy, fixture => fixture.NewExecutable(
            children:
            [
                Leaf("node-fork", typeof(SelectsAAndB)),
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-join")
            ],
            connections:
            [
                fixture.NewConnection("node-fork", "node-a", "A"),
                fixture.NewConnection("node-fork", "node-b", "B"),
                fixture.NewConnection("node-fork", "node-c", "C"),
                fixture.NewConnection("node-a", "node-join"),
                fixture.NewConnection("node-b", "node-join"),
                fixture.NewConnection("node-c", "node-join")
            ],
            startNodeId: "node-fork"));

        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
        Assert.Contains("node-a", run.Order);
        Assert.Contains("node-b", run.Order);
        Assert.DoesNotContain("node-c", run.Order);
        Assert.Single(run.Order, node => node == "node-join");
        var join = run.Order.ToList().IndexOf("node-join");
        Assert.True(join > run.Order.ToList().IndexOf("node-a") && join > run.Order.ToList().IndexOf("node-b"),
            $"The join ran before both selected paths: {string.Join(", ", run.Order)}.");
    }

    [Fact]
    public async Task The_unselected_outcome_is_recorded_dead_under_the_dead_path_policy()
    {
        var run = await RunAsync(FlowchartPolicyKinds.DeadPathJoin, fixture => fixture.NewExecutable(
            children:
            [
                Leaf("node-fork", typeof(SelectsAAndB)),
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-join")
            ],
            connections:
            [
                fixture.NewConnection("node-fork", "node-a", "A"),
                fixture.NewConnection("node-fork", "node-b", "B"),
                fixture.NewConnection("node-fork", "node-c", "C"),
                fixture.NewConnection("node-a", "node-join"),
                fixture.NewConnection("node-b", "node-join"),
                fixture.NewConnection("node-c", "node-join")
            ],
            startNodeId: "node-fork"));

        Assert.NotNull(run.FlowchartState);
        var dead = run.FlowchartState.Diagnostics.Where(diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.DeadPath).Select(diagnostic => diagnostic.Message).ToArray();
        Assert.Contains(dead, message => message.Contains("node-fork:C->node-c", StringComparison.Ordinal));
        Assert.DoesNotContain(dead, message => message.Contains("node-fork:A->node-a", StringComparison.Ordinal));
        Assert.DoesNotContain(dead, message => message.Contains("node-fork:B->node-b", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_selected_outcomes_connected_to_one_node_run_it_once()
    {
        var run = await RunAsync(FlowchartPolicyKinds.DeadPathJoin, fixture => fixture.NewExecutable(
            children: [Leaf("node-fork", typeof(SelectsAAndB)), fixture.NewProbeNode("node-target")],
            connections:
            [
                fixture.NewConnection("node-fork", "node-target", "A"),
                fixture.NewConnection("node-fork", "node-target", "B")
            ],
            startNodeId: "node-fork"));

        Assert.True(run.Status == WorkflowExecutionStatus.Completed, $"Status {run.Status}; order: {string.Join(", ", run.Order)}.");
        Assert.Single(run.Order, node => node == "node-target");
        Assert.NotNull(run.FlowchartState);
        Assert.Contains(run.FlowchartState.Diagnostics, diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.Joined &&
            diagnostic.Message.Contains("fired after 2 active arrival(s)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FlowchartPolicyKinds.DeadPathJoin)]
    [InlineData(FlowchartPolicyKinds.ReachabilityJoin)]
    public async Task An_empty_selection_ends_its_path_while_a_parallel_branch_keeps_running(string joinPolicy)
    {
        var run = await RunAsync(joinPolicy, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-start"),
                Leaf("node-quiet", typeof(SelectsNothing)),
                fixture.NewProbeNode("node-after-quiet"),
                fixture.NewProbeNode("node-parallel"),
                fixture.NewProbeNode("node-after-parallel")
            ],
            connections:
            [
                fixture.NewConnection("node-start", "node-quiet"),
                fixture.NewConnection("node-start", "node-parallel"),
                fixture.NewConnection("node-quiet", "node-after-quiet", "A"),
                fixture.NewConnection("node-quiet", "node-after-quiet"),
                fixture.NewConnection("node-parallel", "node-after-parallel")
            ],
            startNodeId: "node-start"));

        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
        Assert.Contains("node-quiet", run.Order);
        Assert.DoesNotContain("node-after-quiet", run.Order);
        Assert.Contains("node-parallel", run.Order);
        Assert.Contains("node-after-parallel", run.Order);
    }

    [Fact]
    public async Task The_selection_is_recorded_on_the_completion()
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .Build(Enumerable.Range(0, 16).Select(index => $"actexec-{index:D2}"));
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(Enumerable.Range(0, 16).Select(index => $"actexec-{index:D2}"));

        var executable = fixture.NewExecutable(
            children: [Leaf("node-fork", typeof(SelectsAAndB)), fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b")],
            connections: [fixture.NewConnection("node-fork", "node-a", "A"), fixture.NewConnection("node-fork", "node-b", "B")],
            startNodeId: "node-fork");
        var run = await harness.RunAsync(executable);

        run.AssertOutcomes("node-fork", "A", "B");
        run.AssertRan("node-a", "node-b");
        run.AssertWorkflowCompleted();
    }

    // Spec 1208 (2026-11-13): there is no multi-outcome declaration; any activity may select several of its declared
    // outcomes. This activity carries nothing but its outcome declarations.
    [Theory]
    [InlineData(FlowchartPolicyKinds.DeadPathJoin)]
    [InlineData(FlowchartPolicyKinds.ReachabilityJoin)]
    public async Task An_activity_without_any_declaration_selects_two_outcomes_and_the_join_runs_once_after_both(string joinPolicy)
    {
        var run = await RunAsync(joinPolicy, fixture => fixture.NewExecutable(
            children:
            [
                Leaf("node-fork", typeof(PlainSelectsAAndB)),
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-join"),
                fixture.NewProbeNode("node-after-c")
            ],
            connections:
            [
                fixture.NewConnection("node-fork", "node-a", "A"),
                fixture.NewConnection("node-fork", "node-b", "B"),
                fixture.NewConnection("node-fork", "node-c", "C"),
                fixture.NewConnection("node-a", "node-join"),
                fixture.NewConnection("node-b", "node-join"),
                fixture.NewConnection("node-c", "node-after-c")
            ],
            startNodeId: "node-fork"));

        Assert.True(run.Status == WorkflowExecutionStatus.Completed, $"Status {run.Status}; order: {string.Join(", ", run.Order)}.");
        Assert.Contains("node-a", run.Order);
        Assert.Contains("node-b", run.Order);
        Assert.DoesNotContain("node-c", run.Order);
        Assert.DoesNotContain("node-after-c", run.Order);
        Assert.Single(run.Order, node => node == "node-join");
        var order = run.Order.ToList();
        Assert.True(order.IndexOf("node-join") > order.IndexOf("node-a") && order.IndexOf("node-join") > order.IndexOf("node-b"),
            $"The join ran before both selected paths: {string.Join(", ", run.Order)}.");
    }

    [Fact]
    public async Task An_activity_without_any_declaration_records_both_selected_outcomes()
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .Build(Enumerable.Range(0, 16).Select(index => $"actexec-{index:D2}"));
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(Enumerable.Range(0, 16).Select(index => $"actexec-{index:D2}"));

        var executable = fixture.NewExecutable(
            children: [Leaf("node-fork", typeof(PlainSelectsAAndB)), fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b"), fixture.NewProbeNode("node-c")],
            connections:
            [
                fixture.NewConnection("node-fork", "node-a", "A"),
                fixture.NewConnection("node-fork", "node-b", "B"),
                fixture.NewConnection("node-fork", "node-c", "C")
            ],
            startNodeId: "node-fork");
        var run = await harness.RunAsync(executable);

        run.AssertOutcomes("node-fork", "A", "B");
        run.AssertRan("node-a", "node-b");
        run.AssertDidNotRun("node-c");
        run.AssertWorkflowCompleted();
    }

    private static ExecutableNode Leaf(string nodeId, Type activityType) => new(
        executableNodeId: nodeId,
        authoredActivityId: $"authored-{nodeId}",
        activityType: activityType.FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: new Dictionary<string, RuntimeInputBinding>(),
        metadata: new Dictionary<string, string>());

    /// <summary>Declares A, B and C, guarantees a selection (so it needs no Done), and selects A and B.</summary>
    [ActivityOutcome("A")]
    [ActivityOutcome("B")]
    [ActivityOutcome("C")]
    [SelectsOutcomeOnEveryCompletion]
    public sealed class SelectsAAndB : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, OutcomeSelection.Named("A", "B")));
    }

    /// <summary>Declares A, B and C and nothing else, and selects A and B.</summary>
    [ActivityOutcome("A")]
    [ActivityOutcome("B")]
    [ActivityOutcome("C")]
    public sealed class PlainSelectsAAndB : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, new[] { "A", "B" }));
    }

    /// <summary>Declares A and Done and completes with an explicitly empty selection.</summary>
    [ActivityOutcome("A")]
    [ActivityOutcome("Done")]
    public sealed class SelectsNothing : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, OutcomeSelection.Empty));
    }
}

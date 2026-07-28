using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// Regression pins for backward-edge classification when a parallel fork/implicit-join lives inside a loop
/// body (surfaced during spec 122, BPMN cyclic flows). A correct classifier marks only the single loop-closing
/// edge of a cycle as backward — mirroring the standard compiler back-edge (BpmnGraph.ComputeBackwardFlowIds).
/// The historical naive rule (<c>CanReach(target, source)</c>) marks EVERY edge of a cycle as backward: because
/// every node in a cycle reaches every other, <c>fork → branch</c> and <c>branch → join</c> all classify as
/// backward. Each backward-classified edge mints a fresh loop-iteration scope + key on traversal
/// (<see cref="FlowchartScopeResolver.ResolveTargetScope"/>), so the two branches of a fork inside a cycle end
/// up in different iteration scopes/keys and their arrivals at the join — paired on
/// <c>(CurrentNodeId, ExecutionScopeId, IterationKey)</c> — never reconverge. The join then misfires per-arrival
/// instead of pairing.
/// </summary>
public sealed class FlowchartCyclicJoinTests
{
    // Loop body with a parallel fork/join, closed by a genuine loop-back edge:
    //   fork ─┬─▶ a ─┐
    //         └─▶ b ─┴─▶ join ─▶ gate ─(again)─▶ fork   (the ONLY loop-closing edge)
    //                                 └────(done)─▶ end
    private static readonly FlowchartConnectionSpec[] CyclicForkJoinConnections =
    [
        new("node-fork", "node-a", null),
        new("node-fork", "node-b", null),
        new("node-a", "node-join", null),
        new("node-b", "node-join", null),
        new("node-join", "node-gate", null),
        new("node-gate", "node-fork", "again"),
        new("node-gate", "node-end", "done")
    ];

    [Fact]
    public async Task IsBackwardEdge_ParallelForkJoinInsideCycle_MarksOnlyLoopClosingEdge()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(["actexec-flowchart"]);
        var graph = FlowchartGraph.From(BuildCyclicForkJoinExecutable(fixture).RootActivity);
        var analyzer = new FlowchartReachabilityAnalyzer();

        // The loop-closing edge is a backward edge — exactly one per cycle.
        Assert.True(analyzer.IsBackwardEdge(graph, "node-gate", "node-fork"));

        // Every OTHER edge of the cycle is a forward edge, not a backward edge. Under the naive
        // reachability rule these all (wrongly) classify as backward because each node reaches the loop head.
        Assert.False(analyzer.IsBackwardEdge(graph, "node-fork", "node-a"));
        Assert.False(analyzer.IsBackwardEdge(graph, "node-fork", "node-b"));
        Assert.False(analyzer.IsBackwardEdge(graph, "node-a", "node-join"));
        Assert.False(analyzer.IsBackwardEdge(graph, "node-b", "node-join"));
        Assert.False(analyzer.IsBackwardEdge(graph, "node-join", "node-gate"));

        // The clean exit edge leaves the cycle and is never backward.
        Assert.False(analyzer.IsBackwardEdge(graph, "node-gate", "node-end"));
    }

    [Fact]
    public async Task ParallelForkJoinInsideLoop_JoinPairsOncePerIteration_OverTwoIterations()
    {
        var gate = new LoopOnceThenExitPolicy();
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(
            Enumerable.Range(0, 40).Select(index => $"actexec-{index}"),
            services => services.AddSingleton<IFlowchartPolicy>(gate));
        var executable = BuildCyclicForkJoinExecutable(fixture, gatePolicy: true);

        await fixture.ExecuteAsync(executable);

        var activityStates = await fixture.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("wfexec-1");
        int RunCount(string nodeId) => activityStates.Count(state => state.Execution.ExecutableNodeId == nodeId);

        // Two iterations, each running the fork body exactly once. The implicit join reconverges the two
        // parallel branches ONCE per iteration — so the join node fires exactly twice across the whole run,
        // never per-arrival. Under the naive backward-edge rule the two branches carry divergent
        // iteration scopes/keys, the join never pairs, and node-join over-fires (or the run explodes).
        Assert.Equal(2, RunCount("node-fork"));
        Assert.Equal(2, RunCount("node-a"));
        Assert.Equal(2, RunCount("node-b"));
        Assert.Equal(2, RunCount("node-join"));
        Assert.Equal(2, RunCount("node-gate"));
        Assert.Equal(1, RunCount("node-end"));

        var workflowState = await fixture.Provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        Assert.Equal(Elsa.Workflows.Runtime.Core.Models.WorkflowExecutionStatus.Completed, workflowState?.Status);

        // Per-iteration pairing is durably evidenced: exactly one loopback iteration was created for the loop
        // head, and no arrival is left stranded waiting at the join.
        var state = await fixture.GetFlowchartStateAsync();
        Assert.Equal(1, state.LoopIterationCounters["node-fork"]);
        Assert.DoesNotContain(state.ExecutionPaths, path => path.Status == ExecutionPathStatus.Waiting);
    }

    private static Elsa.Workflows.Runtime.Core.Models.WorkflowExecutable BuildCyclicForkJoinExecutable(FlowchartRuntimeFixture fixture, bool gatePolicy = false) =>
        fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-fork"),
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-join"),
                fixture.NewProbeNode("node-gate"),
                fixture.NewProbeNode("node-end")
            ],
            connections: CyclicForkJoinConnections
                .Select(spec => fixture.NewConnection(spec.Source, spec.Target, spec.Port))
                .ToArray(),
            startNodeId: "node-fork",
            nodeMetadata: gatePolicy
                ? new Dictionary<string, FlowchartNodeMetadata> { ["node-gate"] = new(LoopOnceThenExitPolicy.Kind) }
                : null);

    private readonly record struct FlowchartConnectionSpec(string Source, string Target, string? Port);

    // Loops back to the fork head once, then exits — driving exactly two iterations of the fork/join body.
    private sealed class LoopOnceThenExitPolicy : IFlowchartPolicy
    {
        public const string Kind = "test/gate-loop-once-then-exit";
        private int _calls;

        public string PolicyKind => Kind;
        public string DisplayName => "Gate Loop Once Then Exit";

        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context)
        {
            var target = _calls++ == 0 ? "node-fork" : "node-end";
            return new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, nodeId: target)]);
        }
    }
}

using Kernel.Graph.Contracts;
using Kernel.Graph.Internal;
using Kernel.Graph.Models;
using Xunit;
using static Kernel.Graph.Tests.GraphHarness;

namespace Kernel.Graph.Tests;

/// <summary>
/// Main's implicit-join, cyclic-join, fault-join and loop-iteration tests (<c>FlowchartImplicitJoinTests</c>,
/// <c>FlowchartCyclicJoinTests</c>, <c>FlowchartFaultJoinTests</c>, <c>FlowchartLoopIterationTests</c>), under the
/// default join semantics (dead-path join), as main runs them.
/// </summary>
public sealed class FlowchartJoinTests
{
    // ---- FlowchartImplicitJoinTests ------------------------------------------------------------------------------------

    [Fact]
    public void DiamondFlowchart_RunsReconvergedActivityOnceAfterBothActiveBranchesArrive()
    {
        var run = Run(null,
            [new("node-a"), new("node-b"), new("node-c"), new("node-d")],
            [Connection("node-a", "node-b"), Connection("node-a", "node-c"), Connection("node-b", "node-d"), Connection("node-c", "node-d")],
            startNodeId: "node-a",
            executionIds: ["actexec-flowchart", "actexec-a", "actexec-b", "actexec-c", "actexec-d"]);    // main's pool: no extra run fits

        Assert.Equal(["node-a", "node-b", "node-c", "node-d"], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }

    [Fact]
    public void UntakenDecisionBranch_DoesNotBlockImplicitJoin()
    {
        var run = Run(null,
            [new("node-a", ["Left"]), new("node-b"), new("node-c"), new("node-d")],
            [Connection("node-a", "node-b", "Left"), Connection("node-a", "node-c", "Right"), Connection("node-b", "node-d"), Connection("node-c", "node-d")],
            startNodeId: "node-a",
            executionIds: ["actexec-flowchart", "actexec-a", "actexec-b", "actexec-d"]);

        Assert.Equal(["node-a", "node-b", "node-d"], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }

    [Fact]
    public void WaitingJoin_ReleasesWhenRemainingActiveBranchTakesTerminalOutcome()
    {
        var run = Run(null,
            [new("node-a"), new("node-b"), new("node-c", ["Skip"]), new("node-d"), new("node-e")],
            [
                Connection("node-a", "node-b"),
                Connection("node-a", "node-c"),
                Connection("node-b", "node-d"),
                Connection("node-c", "node-d", "Join"),
                Connection("node-c", "node-e", "Skip")
            ],
            startNodeId: "node-a",
            executionIds: ["actexec-flowchart", "actexec-a", "actexec-b", "actexec-c", "actexec-e", "actexec-d"]);

        Assert.Equal(["node-a", "node-b", "node-c", "node-e", "node-d"], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }

    // ---- FlowchartCyclicJoinTests --------------------------------------------------------------------------------------

    //   fork ─┬─▶ a ─┐
    //         └─▶ b ─┴─▶ join ─▶ gate ─(again)─▶ fork   (the only loop-closing edge)
    //                                 └────(done)─▶ end
    private static readonly FlowchartConnection[] CyclicForkJoinConnections =
    [
        Connection("node-fork", "node-a"),
        Connection("node-fork", "node-b"),
        Connection("node-a", "node-join"),
        Connection("node-b", "node-join"),
        Connection("node-join", "node-gate"),
        Connection("node-gate", "node-fork", "again"),
        Connection("node-gate", "node-end", "done")
    ];

    private static readonly Probe[] CyclicForkJoinChildren =
        [new("node-fork"), new("node-a"), new("node-b"), new("node-join"), new("node-gate"), new("node-end")];

    private static FlowchartGraph CyclicForkJoinGraph() =>
        FlowchartGraph.From([.. CyclicForkJoinChildren.Select(child => new FlowchartNode(child.NodeId))], new FlowchartStructure(CyclicForkJoinConnections, "node-fork"));

    [Fact]
    public void IsBackwardEdge_ParallelForkJoinInsideCycle_MarksOnlyLoopClosingEdge()
    {
        var graph = CyclicForkJoinGraph();
        var analyzer = new FlowchartReachabilityAnalyzer();

        Assert.True(analyzer.IsBackwardEdge(graph, "node-gate", "node-fork"));
        Assert.False(analyzer.IsBackwardEdge(graph, "node-fork", "node-a"));
        Assert.False(analyzer.IsBackwardEdge(graph, "node-fork", "node-b"));
        Assert.False(analyzer.IsBackwardEdge(graph, "node-a", "node-join"));
        Assert.False(analyzer.IsBackwardEdge(graph, "node-b", "node-join"));
        Assert.False(analyzer.IsBackwardEdge(graph, "node-join", "node-gate"));
        Assert.False(analyzer.IsBackwardEdge(graph, "node-gate", "node-end"));
    }

    [Fact]
    public void CanReachForward_ExcludesBackwardEdges_WhereTheCyclicWalkSaysEverythingReachesEverything()
    {
        var graph = CyclicForkJoinGraph();
        var analyzer = new FlowchartReachabilityAnalyzer();

        Assert.True(analyzer.CanReach(graph, "node-fork", "node-join"));
        Assert.True(analyzer.CanReachForward(graph, "node-fork", "node-join"));
        Assert.True(analyzer.CanReach(graph, "node-join", "node-a"));
        Assert.False(analyzer.CanReachForward(graph, "node-join", "node-a"));
        Assert.True(analyzer.CanReach(graph, "node-a", "node-b"));
        Assert.False(analyzer.CanReachForward(graph, "node-a", "node-b"));
        Assert.False(analyzer.CanReach(graph, "node-end", "node-fork"));
        Assert.False(analyzer.CanReachForward(graph, "node-end", "node-fork"));
    }

    [Fact]
    public void ParallelForkJoinInsideLoop_JoinPairsOncePerIteration_OverTwoIterations()
    {
        var gate = new ScriptedPolicy("test/gate-loop-once-then-exit", "node-fork", "node-end");
        var run = Run(null, CyclicForkJoinChildren, CyclicForkJoinConnections, startNodeId: "node-fork",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-gate"] = new(gate.PolicyKind) },
            policies: [gate]);

        Assert.Equal(2, run.RunCount("node-fork"));
        Assert.Equal(2, run.RunCount("node-a"));
        Assert.Equal(2, run.RunCount("node-b"));
        Assert.Equal(2, run.RunCount("node-join"));
        Assert.Equal(2, run.RunCount("node-gate"));
        Assert.Equal(1, run.RunCount("node-end"));
        Assert.Equal(GraphRunStatus.Completed, run.Status);
        Assert.NotNull(run.FlowchartState);
        Assert.Equal(1, run.FlowchartState.LoopIterationCounters["node-fork"]);
        Assert.DoesNotContain(run.FlowchartState.ExecutionPaths, path => path.Status == ExecutionPathStatus.Waiting);
    }

    // ---- FlowchartFaultJoinTests ---------------------------------------------------------------------------------------

    /// <summary>A diamond whose inbound branch faults faults the Flowchart (#308) instead of leaving the join waiting.</summary>
    [Fact]
    public void ParallelJoin_WithFaultingInboundBranch_FaultsFlowchart_InsteadOfHanging()
    {
        var run = Run(null,
            [new("node-a"), new("node-b", Faults: true), new("node-c"), new("node-d")],
            [Connection("node-a", "node-b"), Connection("node-a", "node-c"), Connection("node-b", "node-d"), Connection("node-c", "node-d")],
            executionIds: ["actexec-flowchart", "actexec-a", "actexec-b", "actexec-c"]);

        // The sibling branch still ran (a faulted container's scheduled children run on, as in main's harness); the
        // join never fired.
        Assert.Equal(["node-a", "node-b", "node-c"], run.Order);
        Assert.Equal(0, run.RunCount("node-d"));
        Assert.Equal(GraphRunStatus.Faulted, run.Status);
        Assert.NotNull(run.FlowchartState);
        Assert.Contains(run.FlowchartState.Diagnostics, diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.Faulted);
    }

    // ---- FlowchartLoopIterationTests -----------------------------------------------------------------------------------

    private static GraphRun RunLoopOnce() =>
        Run(null,
            [new("node-a"), new("node-b"), new("node-c")],
            [Connection("node-a", "node-b"), Connection("node-b", "node-a", "again"), Connection("node-b", "node-c", "done")],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-b"] = new("test/loop-once") },
            executionIds: ["actexec-flowchart", "actexec-a1", "actexec-b1", "actexec-a2", "actexec-b2", "actexec-c"],
            policies: [new ScriptedPolicy("test/loop-once", "node-a", "node-c")]);

    [Fact]
    public void BackwardPolicyRoute_CreatesLoopIterationScope()
    {
        var run = RunLoopOnce();

        Assert.NotNull(run.FlowchartState);
        Assert.Equal(1, run.FlowchartState.LoopIterationCounters["node-a"]);
        Assert.Equal(["node-a", "node-b", "node-a", "node-b", "node-c"], run.Order);
    }

    [Fact]
    public void LoopIterationKey_PreventsStaleJoinArrivalsFromSatisfyingLaterIteration()
    {
        var run = RunLoopOnce();

        Assert.NotNull(run.FlowchartState);
        Assert.Equal(1, run.FlowchartState.LoopIterationCounters["node-a"]);
        Assert.DoesNotContain(run.FlowchartState.ExecutionPaths, path => path.Status == ExecutionPathStatus.Waiting);
    }

    [Fact]
    public void AmbiguousLoopbackValidation_RejectsLoopbackIntoMultiInboundTarget()
    {
        var run = Run(null,
            [new("node-a"), new("node-b"), new("node-c")],
            [Connection("node-a", "node-b"), Connection("node-b", "node-c"), Connection("node-c", "node-b")],
            startNodeId: "node-a",
            executionIds: ["actexec-flowchart"]);

        Assert.Empty(run.Order);
        Assert.Equal(GraphRunStatus.Faulted, run.Status);
        Assert.Contains("ambiguous", run.FaultMessage, StringComparison.Ordinal);
    }

    // ---- the seam ----------------------------------------------------------------------------------------------------

    /// <summary>An empty Flowchart completes at once with Done, as main's <c>RuntimeStructuralContinuation.Complete()</c>.</summary>
    [Fact]
    public void EmptyGraph_CompletesAtOnceWithDone()
    {
        var step = FlowchartEngines.Create().Start(FlowchartGraph.From([], null), new FlowchartRun("actexec-flowchart"));

        Assert.Equal(FlowchartStepKind.Complete, step.Kind);
        Assert.Equal(FlowchartOutcomes.Done, step.Outcome);
        Assert.Empty(step.Scheduled);
    }

    /// <summary>
    /// A policy that schedules one node and then fails: main's runtime refuses a terminal decision that also schedules
    /// children, so the child is never run; here the engine refuses it in the same words.
    /// </summary>
    [Fact]
    public void TerminalDecisionThatAlsoSchedules_IsRefused_AndNothingRuns()
    {
        var run = Run(null,
            [new("node-a"), new("node-b"), new("node-c")],
            [Connection("node-a", "node-b"), Connection("node-a", "node-c")],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-a"] = new(HalfBadPolicy.Kind) },
            policies: [new HalfBadPolicy()]);

        Assert.Equal(["node-a"], run.Order);
        Assert.Equal(GraphRunStatus.Faulted, run.Status);
        Assert.Contains("terminal structural decision", run.FaultMessage, StringComparison.Ordinal);
    }

    /// <summary>Schedules a real node, then one that does not exist: the decision fails after it scheduled.</summary>
    private sealed class HalfBadPolicy : IFlowchartPolicy
    {
        public const string Kind = "test/half-bad";

        public string PolicyKind => Kind;
        public string DisplayName => "Half Bad";

        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) =>
            new([
                new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, nodeId: "node-b"),
                new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, nodeId: "node-missing")
            ]);
    }
}

using Kernel.Graph.Exceptions;
using Kernel.Graph.Internal;
using Kernel.Graph.Internal.Policies;
using Kernel.Graph.Models;
using Xunit;
using static Kernel.Graph.Tests.GraphHarness;

namespace Kernel.Graph.Tests;

/// <summary>
/// Main's join conformance corpus (<c>FlowchartConformanceCorpusTests</c>), scenario for scenario, against the
/// extracted engine. Every scenario asserts the order children ran in and the terminal status, under both join
/// semantics: one expectation for both rows records that they agree. The scenarios, their graphs and their expected
/// answers are main's; only the host differs (<see cref="GraphHarness"/> instead of the workflow execution harness).
/// </summary>
public sealed class FlowchartConformanceCorpusTests
{
    public static TheoryData<string> JoinPolicies =>
    [
        FlowchartPolicyKinds.ReachabilityJoin,
        FlowchartPolicyKinds.DeadPathJoin
    ];

    /// <summary>C1: diamond with one dead branch. The reconvergence fires once, on the single live inbound.</summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public void C1_DiamondWithOneDeadBranch_JoinFiresOnceOnTheLiveInbound(string joinPolicyKind)
    {
        var run = Run(joinPolicyKind,
            [new("node-decide", ["Left"]), new("node-left"), new("node-right"), new("node-join"), new("node-end")],
            [
                Connection("node-decide", "node-left", "Left"),
                Connection("node-decide", "node-right", "Right"),
                Connection("node-left", "node-join"),
                Connection("node-right", "node-join"),
                Connection("node-join", "node-end")
            ],
            startNodeId: "node-decide");

        Assert.Equal(["node-decide", "node-left", "node-join", "node-end"], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }

    /// <summary>C2: loop containing a join. The join fires exactly once per iteration, never once per arrival.</summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public void C2_LoopContainingAJoin_JoinFiresOncePerIteration(string joinPolicyKind)
    {
        var gate = new ScriptedPolicy("corpus/gate", "node-head", "node-head", "node-end");
        var run = Run(joinPolicyKind,
            [new("node-head"), new("node-a"), new("node-b"), new("node-join"), new("node-gate"), new("node-end")],
            [
                Connection("node-head", "node-a"),
                Connection("node-head", "node-b"),
                Connection("node-a", "node-join"),
                Connection("node-b", "node-join"),
                Connection("node-join", "node-gate"),
                Connection("node-gate", "node-head", "again"),
                Connection("node-gate", "node-end", "done")
            ],
            startNodeId: "node-head",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-gate"] = new(gate.PolicyKind) },
            policies: [gate]);

        Assert.Equal(
        [
            "node-head", "node-a", "node-b", "node-join", "node-gate",
            "node-head", "node-a", "node-b", "node-join", "node-gate",
            "node-head", "node-a", "node-b", "node-join", "node-gate",
            "node-end"
        ], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }

    /// <summary>C3: nested loops are rejected structurally, before any join semantics apply (an inner loop head has two inbound).</summary>
    [Fact]
    public void C3_NestedLoops_AreRejectedStructurallyBeforeAnyJoinSemanticsApply()
    {
        var graph = FlowchartGraph.From(
            [new("node-outer-head"), new("node-inner-head"), new("node-body"), new("node-inner-gate"), new("node-outer-gate"), new("node-end")],
            new FlowchartStructure(
            [
                Connection("node-outer-head", "node-inner-head"),
                Connection("node-inner-head", "node-body"),
                Connection("node-body", "node-inner-gate"),
                Connection("node-inner-gate", "node-inner-head", "again"),
                Connection("node-inner-gate", "node-outer-gate", "done"),
                Connection("node-outer-gate", "node-outer-head", "again"),
                Connection("node-outer-gate", "node-end", "done")
            ], "node-outer-head"));

        var exception = Assert.Throws<FlowchartExecutionException>(() => new FlowchartReachabilityAnalyzer().ValidateNoAmbiguousLoopbacks(graph));

        Assert.Contains("node-inner-head", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ambiguous", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// C4: a join inside a loop whose live inbound alternates per iteration (left, right, left). A join satisfied by the
    /// other branch's leftover from the previous iteration would fire early and change the order.
    /// </summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public void C4_JoinInsideLoop_WithAlternatingLiveBranch_FiresOncePerIterationAfterItsOwnBranch(string joinPolicyKind)
    {
        var pick = new ScriptedPolicy("corpus/pick", "node-left", "node-right", "node-left");
        var gate = new ScriptedPolicy("corpus/gate", "node-head", "node-head", "node-end");
        var run = Run(joinPolicyKind,
            [new("node-head"), new("node-pick"), new("node-left"), new("node-right"), new("node-join"), new("node-gate"), new("node-end")],
            [
                Connection("node-head", "node-pick"),
                Connection("node-pick", "node-left", "left"),
                Connection("node-pick", "node-right", "right"),
                Connection("node-left", "node-join"),
                Connection("node-right", "node-join"),
                Connection("node-join", "node-gate"),
                Connection("node-gate", "node-head", "again"),
                Connection("node-gate", "node-end", "done")
            ],
            startNodeId: "node-head",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-pick"] = new(pick.PolicyKind),
                ["node-gate"] = new(gate.PolicyKind)
            },
            policies: [pick, gate]);

        Assert.Equal(
        [
            "node-head", "node-pick", "node-left", "node-join", "node-gate",
            "node-head", "node-pick", "node-right", "node-join", "node-gate",
            "node-head", "node-pick", "node-left", "node-join", "node-gate",
            "node-end"
        ], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }

    /// <summary>C5: first-wins race with a losing branch mid-flight: the loser still runs, its completion is absorbed, the join fires once.</summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public void C5_FirstWinsRace_LosingBranchStillExecutesButDoesNotRoute(string joinPolicyKind)
    {
        var run = Run(joinPolicyKind,
            [new("node-fork"), new("node-b"), new("node-c"), new("node-join"), new("node-end")],
            [
                Connection("node-fork", "node-b"),
                Connection("node-fork", "node-c"),
                Connection("node-b", "node-join"),
                Connection("node-c", "node-join"),
                Connection("node-join", "node-end")
            ],
            startNodeId: "node-fork",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-fork"] = new(FlowchartPolicyKinds.FirstWins) });

        Assert.Equal(["node-fork", "node-b", "node-c", "node-join", "node-end"], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
        Assert.Equal(1, run.RunCount("node-join"));
    }

    /// <summary>C6: Break inside a parallel fork ends the Flowchart and cancels the waiting join (#304).</summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public void C6_BreakInsideAParallelFork_EndsTheFlowchartAndCancelsTheWaitingJoin(string joinPolicyKind)
    {
        var run = Run(joinPolicyKind,
            [new("node-fork"), new("node-slow"), new("node-break", [FlowchartOutcomes.Break]), new("node-join"), new("node-end")],
            [
                Connection("node-fork", "node-slow"),
                Connection("node-fork", "node-break"),
                Connection("node-slow", "node-join"),
                Connection("node-break", "node-join", FlowchartOutcomes.Break),
                Connection("node-join", "node-end")
            ],
            startNodeId: "node-fork");

        Assert.Equal(["node-fork", "node-slow", "node-break"], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }

    /// <summary>C7: the dead side reaches the join through an intermediate node; deadness travels the whole chain.</summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public void C7_JoinWithOneDeadAndOneLiveInbound_FiresWithoutWaitingOnTheDeadChain(string joinPolicyKind)
    {
        var run = Run(joinPolicyKind,
            [new("node-fork"), new("node-live"), new("node-decide", ["Skip"]), new("node-mid"), new("node-other"), new("node-join"), new("node-end")],
            [
                Connection("node-fork", "node-live"),
                Connection("node-fork", "node-decide"),
                Connection("node-live", "node-join"),
                Connection("node-decide", "node-mid", "Take"),
                Connection("node-decide", "node-other", "Skip"),
                Connection("node-mid", "node-join"),
                Connection("node-join", "node-end")
            ],
            startNodeId: "node-fork");

        Assert.Equal(["node-fork", "node-live", "node-decide", "node-other", "node-join", "node-end"], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }

    /// <summary>C8: a join whose every inbound is dead schedules nothing and carries the deadness on; the node after it still runs once.</summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public void C8_JoinWithAllInboundsDead_PropagatesInsteadOfStalling(string joinPolicyKind)
    {
        var run = Run(joinPolicyKind,
            [new("node-decide", ["Live"]), new("node-live"), new("node-r1"), new("node-r2"), new("node-join"), new("node-after")],
            [
                Connection("node-decide", "node-live", "Live"),
                Connection("node-decide", "node-r1", "DeadOne"),
                Connection("node-decide", "node-r2", "DeadTwo"),
                Connection("node-r1", "node-join"),
                Connection("node-r2", "node-join"),
                Connection("node-join", "node-after"),
                Connection("node-live", "node-after")
            ],
            startNodeId: "node-decide");

        Assert.Equal(["node-decide", "node-live", "node-after"], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }

    /// <summary>C9: a merge with one dead and one live inbound must not carry deadness past the live branch still in flight.</summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public void C9_MergeNodeWithOneDeadAndOneLiveInbound_DoesNotCarryDeadnessPastTheLiveBranch(string joinPolicyKind)
    {
        var run = Run(joinPolicyKind,
            [new("node-fork"), new("node-decide", ["Skip"]), new("node-live"), new("node-other"), new("node-skipped"), new("node-merge"), new("node-after"), new("node-join")],
            [
                Connection("node-fork", "node-decide"),
                Connection("node-fork", "node-live"),
                Connection("node-fork", "node-other"),
                Connection("node-decide", "node-merge", "Take"),
                Connection("node-decide", "node-skipped", "Skip"),
                Connection("node-live", "node-merge"),
                Connection("node-merge", "node-after"),
                Connection("node-after", "node-join"),
                Connection("node-other", "node-join")
            ],
            startNodeId: "node-fork",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-merge"] = new(FlowchartPolicyKinds.Merge) });

        Assert.Equal(
        [
            "node-fork", "node-decide", "node-live", "node-other", "node-skipped",
            "node-merge", "node-after", "node-join"
        ], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }
}

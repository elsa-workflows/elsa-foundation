using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Activities.Flowchart.Tests.FlowchartConformanceCorpus;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// The ADR 0064 conformance corpus: the routing scenarios whose answers define what "the same semantics"
/// means for a Flowchart, run against the real engine.
/// <para>
/// It exists to be the <b>instrument</b>, so it lands before the semantics change it measures. Every scenario
/// asserts the <b>order</b> children executed in and the terminal workflow status — not merely that the run
/// finished — because the failure modes this corpus is aimed at (a join firing an iteration early, a join
/// firing per-arrival instead of once) show up as a reordered or repeated node, and a completion-only
/// assertion is blind to both.
/// </para>
/// <para>
/// The scenarios are the ones ADR 0064 names, plus the loop shape that stresses the part the ADR calls
/// subtle: a join inside a loop body whose live inbound branch <em>alternates</em> between iterations, so a
/// dead path from iteration <i>n</i> that leaked into iteration <i>n+1</i> would fire that join early and
/// change this corpus's answer.
/// </para>
/// </summary>
public sealed class FlowchartConformanceCorpusTests
{
    /// <summary>
    /// Both join semantics, run over the same graphs. A scenario asserting one expectation for both rows is
    /// the corpus recording that they agree; a scenario that has to branch on this argument is recording a
    /// deliberate divergence, and there is nowhere else to hide one.
    /// </summary>
    public static TheoryData<string> JoinPolicies =>
    [
        FlowchartPolicyKinds.ReachabilityJoin,
        FlowchartPolicyKinds.DeadPathJoin
    ];

    /// <summary>
    /// C1 — diamond with one dead branch. A decision takes <c>Left</c>, so the <c>Right</c> branch never runs
    /// and the reconvergence must still fire, exactly once, on the single live inbound.
    /// </summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public async Task C1_DiamondWithOneDeadBranch_JoinFiresOnceOnTheLiveInbound(string joinPolicyKind)
    {
        var run = await RunAsync(joinPolicyKind, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-decide", ["Left"]),
                fixture.NewProbeNode("node-left"),
                fixture.NewProbeNode("node-right"),
                fixture.NewProbeNode("node-join"),
                fixture.NewProbeNode("node-end")
            ],
            connections:
            [
                fixture.NewConnection("node-decide", "node-left", "Left"),
                fixture.NewConnection("node-decide", "node-right", "Right"),
                fixture.NewConnection("node-left", "node-join"),
                fixture.NewConnection("node-right", "node-join"),
                fixture.NewConnection("node-join", "node-end")
            ],
            startNodeId: "node-decide"));

        Assert.Equal(["node-decide", "node-left", "node-join", "node-end"], run.Order);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
    }

    /// <summary>
    /// C2 — loop containing a join. Two parallel branches reconverge inside a loop body driven three times
    /// round. The join must fire exactly once per iteration: once per <em>pair</em> of arrivals, never once
    /// per arrival, and never carrying an arrival over into the next iteration.
    /// </summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public async Task C2_LoopContainingAJoin_JoinFiresOncePerIteration(string joinPolicyKind)
    {
        var gate = new ScriptedPolicy("corpus/gate", "node-head", "node-head", "node-end");
        var run = await RunAsync(joinPolicyKind, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-head"),
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-join"),
                fixture.NewProbeNode("node-gate"),
                fixture.NewProbeNode("node-end")
            ],
            connections:
            [
                fixture.NewConnection("node-head", "node-a"),
                fixture.NewConnection("node-head", "node-b"),
                fixture.NewConnection("node-a", "node-join"),
                fixture.NewConnection("node-b", "node-join"),
                fixture.NewConnection("node-join", "node-gate"),
                fixture.NewConnection("node-gate", "node-head", "again"),
                fixture.NewConnection("node-gate", "node-end", "done")
            ],
            startNodeId: "node-head",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-gate"] = new(gate.PolicyKind) }),
            gate);

        Assert.Equal(
        [
            "node-head", "node-a", "node-b", "node-join", "node-gate",
            "node-head", "node-a", "node-b", "node-join", "node-gate",
            "node-head", "node-a", "node-b", "node-join", "node-gate",
            "node-end"
        ], run.Order);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
    }

    /// <summary>
    /// C3 — nested loops. The corpus records the engine's <em>actual</em> answer, which is that it has none:
    /// a Flowchart loop head must have exactly one inbound connection, so an inner loop head — which needs one
    /// inbound from the outer body plus its own loop-closing edge — is rejected at start by
    /// <see cref="FlowchartReachabilityAnalyzer.ValidateNoAmbiguousLoopbacks"/>. Two consequences worth pinning:
    /// nested iteration is <c>ForEach</c>'s job, not the graph's (ADR 0064's "collection iteration stays
    /// ForEach"), and no join semantics can be blamed for it, because the rejection happens before any token
    /// moves. That is why this row is structural rather than executed — it must read the same under either
    /// join policy, and it does so by construction.
    /// </summary>
    [Fact]
    public async Task C3_NestedLoops_AreRejectedStructurallyBeforeAnyJoinSemanticsApply()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(["actexec-flowchart"]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-outer-head"),
                fixture.NewProbeNode("node-inner-head"),
                fixture.NewProbeNode("node-body"),
                fixture.NewProbeNode("node-inner-gate"),
                fixture.NewProbeNode("node-outer-gate"),
                fixture.NewProbeNode("node-end")
            ],
            connections:
            [
                fixture.NewConnection("node-outer-head", "node-inner-head"),
                fixture.NewConnection("node-inner-head", "node-body"),
                fixture.NewConnection("node-body", "node-inner-gate"),
                fixture.NewConnection("node-inner-gate", "node-inner-head", "again"),
                fixture.NewConnection("node-inner-gate", "node-outer-gate", "done"),
                fixture.NewConnection("node-outer-gate", "node-outer-head", "again"),
                fixture.NewConnection("node-outer-gate", "node-end", "done")
            ],
            startNodeId: "node-outer-head");

        var graph = FlowchartGraph.From(executable.RootActivity);
        var exception = Assert.Throws<FlowchartExecutionException>(
            () => new FlowchartReachabilityAnalyzer().ValidateNoAmbiguousLoopbacks(graph));

        Assert.Contains("node-inner-head", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ambiguous", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// C4 — a join inside a loop whose live inbound branch alternates per iteration. This is the corpus row
    /// ADR 0064 says to test hardest: in every iteration exactly <em>one</em> of the join's two inbound
    /// branches runs, so a join that could be satisfied by the other branch's leftover from the previous
    /// iteration would fire early and produce a different order here. Three iterations, alternating
    /// left/right/left, so a one-iteration leak in either direction is visible.
    /// </summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public async Task C4_JoinInsideLoop_WithAlternatingLiveBranch_FiresOncePerIterationAfterItsOwnBranch(string joinPolicyKind)
    {
        var pick = new ScriptedPolicy("corpus/pick", "node-left", "node-right", "node-left");
        var gate = new ScriptedPolicy("corpus/gate", "node-head", "node-head", "node-end");
        var run = await RunAsync(joinPolicyKind, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-head"),
                fixture.NewProbeNode("node-pick"),
                fixture.NewProbeNode("node-left"),
                fixture.NewProbeNode("node-right"),
                fixture.NewProbeNode("node-join"),
                fixture.NewProbeNode("node-gate"),
                fixture.NewProbeNode("node-end")
            ],
            connections:
            [
                fixture.NewConnection("node-head", "node-pick"),
                fixture.NewConnection("node-pick", "node-left", "left"),
                fixture.NewConnection("node-pick", "node-right", "right"),
                fixture.NewConnection("node-left", "node-join"),
                fixture.NewConnection("node-right", "node-join"),
                fixture.NewConnection("node-join", "node-gate"),
                fixture.NewConnection("node-gate", "node-head", "again"),
                fixture.NewConnection("node-gate", "node-end", "done")
            ],
            startNodeId: "node-head",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-pick"] = new(pick.PolicyKind),
                ["node-gate"] = new(gate.PolicyKind)
            }),
            pick,
            gate);

        Assert.Equal(
        [
            "node-head", "node-pick", "node-left", "node-join", "node-gate",
            "node-head", "node-pick", "node-right", "node-join", "node-gate",
            "node-head", "node-pick", "node-left", "node-join", "node-gate",
            "node-end"
        ], run.Order);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
    }

    /// <summary>
    /// C5 — first-wins race with a losing branch mid-flight. Both branches are already scheduled when the race
    /// resolves, so the loser still executes; its completion must be absorbed rather than routed, and the
    /// reconvergence must fire once, on the winner.
    /// </summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public async Task C5_FirstWinsRace_LosingBranchStillExecutesButDoesNotRoute(string joinPolicyKind)
    {
        var run = await RunAsync(joinPolicyKind, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-fork"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-join"),
                fixture.NewProbeNode("node-end")
            ],
            connections:
            [
                fixture.NewConnection("node-fork", "node-b"),
                fixture.NewConnection("node-fork", "node-c"),
                fixture.NewConnection("node-b", "node-join"),
                fixture.NewConnection("node-c", "node-join"),
                fixture.NewConnection("node-join", "node-end")
            ],
            startNodeId: "node-fork",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-fork"] = new(FlowchartPolicyKinds.FirstWins) }));

        Assert.Equal(["node-fork", "node-b", "node-c", "node-join", "node-end"], run.Order);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
        // The reconvergence ran once, on the winner — the loser's late completion was absorbed, not routed.
        Assert.Single(run.Order.Where(nodeId => StringComparer.Ordinal.Equals(nodeId, "node-join")));
    }

    /// <summary>
    /// C6 — <c>Break</c> inside a parallel fork. One branch of a fork breaks while the other is waiting at the
    /// reconvergence: the whole Flowchart ends with the Break outcome and the waiting branch is canceled, so
    /// the node behind the join never runs (#304).
    /// </summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public async Task C6_BreakInsideAParallelFork_EndsTheFlowchartAndCancelsTheWaitingJoin(string joinPolicyKind)
    {
        var run = await RunAsync(joinPolicyKind, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-fork"),
                fixture.NewProbeNode("node-slow"),
                fixture.NewProbeNode("node-break", [ActivityOutcomes.Break]),
                fixture.NewProbeNode("node-join"),
                fixture.NewProbeNode("node-end")
            ],
            connections:
            [
                fixture.NewConnection("node-fork", "node-slow"),
                fixture.NewConnection("node-fork", "node-break"),
                fixture.NewConnection("node-slow", "node-join"),
                fixture.NewConnection("node-break", "node-join", ActivityOutcomes.Break),
                fixture.NewConnection("node-join", "node-end")
            ],
            startNodeId: "node-fork"));

        Assert.Equal(["node-fork", "node-slow", "node-break"], run.Order);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
    }

    /// <summary>
    /// C7 — a join with one dead and one live inbound, where the dead side reaches the join through an
    /// intermediate node rather than directly. Deadness has to travel the whole chain: the untaken branch's
    /// first node never runs, so neither does the node behind it, and the join must still fire on the live
    /// inbound alone rather than wait for a chain that will never move.
    /// </summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public async Task C7_JoinWithOneDeadAndOneLiveInbound_FiresWithoutWaitingOnTheDeadChain(string joinPolicyKind)
    {
        var run = await RunAsync(joinPolicyKind, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-fork"),
                fixture.NewProbeNode("node-live"),
                fixture.NewProbeNode("node-decide", ["Skip"]),
                fixture.NewProbeNode("node-mid"),
                fixture.NewProbeNode("node-other"),
                fixture.NewProbeNode("node-join"),
                fixture.NewProbeNode("node-end")
            ],
            connections:
            [
                fixture.NewConnection("node-fork", "node-live"),
                fixture.NewConnection("node-fork", "node-decide"),
                fixture.NewConnection("node-live", "node-join"),
                fixture.NewConnection("node-decide", "node-mid", "Take"),
                fixture.NewConnection("node-decide", "node-other", "Skip"),
                fixture.NewConnection("node-mid", "node-join"),
                fixture.NewConnection("node-join", "node-end")
            ],
            startNodeId: "node-fork"));

        Assert.Equal(["node-fork", "node-live", "node-decide", "node-other", "node-join", "node-end"], run.Order);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
    }

    /// <summary>
    /// C8 — a join whose every inbound is dead. Nothing may be scheduled behind it, and the deadness must keep
    /// travelling: the node <em>after</em> the all-dead join has a second, live inbound, and it must still run
    /// exactly once. A join that stalled here instead of propagating would leave the Flowchart parked forever.
    /// </summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public async Task C8_JoinWithAllInboundsDead_PropagatesInsteadOfStalling(string joinPolicyKind)
    {
        var run = await RunAsync(joinPolicyKind, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-decide", ["Live"]),
                fixture.NewProbeNode("node-live"),
                fixture.NewProbeNode("node-r1"),
                fixture.NewProbeNode("node-r2"),
                fixture.NewProbeNode("node-join"),
                fixture.NewProbeNode("node-after")
            ],
            connections:
            [
                fixture.NewConnection("node-decide", "node-live", "Live"),
                fixture.NewConnection("node-decide", "node-r1", "DeadOne"),
                fixture.NewConnection("node-decide", "node-r2", "DeadTwo"),
                fixture.NewConnection("node-r1", "node-join"),
                fixture.NewConnection("node-r2", "node-join"),
                fixture.NewConnection("node-join", "node-after"),
                fixture.NewConnection("node-live", "node-after")
            ],
            startNodeId: "node-decide"));

        Assert.Equal(["node-decide", "node-live", "node-after"], run.Order);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
    }

    /// <summary>
    /// C9 — a <c>merge</c> node with one dead and one live inbound. Merge is the one node kind that bypasses
    /// join accounting entirely: it never waits, by design. That exemption means "must not block", not
    /// "nothing more is coming", and the two are easy to conflate — reading the first as the second lets a
    /// dead arrival carry deadness past a live branch still in flight, which fires the join behind it early
    /// and then again when the real token lands. So the row asserts the node behind the merge runs exactly
    /// once, in its place in the order, under both semantics.
    /// </summary>
    [Theory]
    [MemberData(nameof(JoinPolicies))]
    public async Task C9_MergeNodeWithOneDeadAndOneLiveInbound_DoesNotCarryDeadnessPastTheLiveBranch(string joinPolicyKind)
    {
        var run = await RunAsync(joinPolicyKind, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-fork"),
                fixture.NewProbeNode("node-decide", ["Skip"]),
                fixture.NewProbeNode("node-live"),
                fixture.NewProbeNode("node-other"),
                fixture.NewProbeNode("node-skipped"),
                fixture.NewProbeNode("node-merge"),
                fixture.NewProbeNode("node-after"),
                fixture.NewProbeNode("node-join")
            ],
            connections:
            [
                fixture.NewConnection("node-fork", "node-decide"),
                fixture.NewConnection("node-fork", "node-live"),
                fixture.NewConnection("node-fork", "node-other"),
                fixture.NewConnection("node-decide", "node-merge", "Take"),
                fixture.NewConnection("node-decide", "node-skipped", "Skip"),
                fixture.NewConnection("node-live", "node-merge"),
                fixture.NewConnection("node-merge", "node-after"),
                fixture.NewConnection("node-after", "node-join"),
                fixture.NewConnection("node-other", "node-join")
            ],
            startNodeId: "node-fork",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-merge"] = new(FlowchartPolicyKinds.Merge) }));

        Assert.Equal(
        [
            "node-fork", "node-decide", "node-live", "node-other", "node-skipped",
            "node-merge", "node-after", "node-join"
        ], run.Order);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
    }
}

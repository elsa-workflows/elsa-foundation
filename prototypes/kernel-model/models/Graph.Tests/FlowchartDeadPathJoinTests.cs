using Kernel.Graph.Contracts;
using Kernel.Graph.Exceptions;
using Kernel.Graph.Internal;
using Kernel.Graph.Internal.Policies;
using Kernel.Graph.Models;
using Xunit;
using static Kernel.Graph.Tests.GraphHarness;

namespace Kernel.Graph.Tests;

/// <summary>
/// Main's <c>FlowchartDeadPathJoinTests</c>: the mechanism behind the dead-path join, beside the behaviour the corpus
/// pins. The dead-path policy agrees with the reachability one for the stated reason (it represents deadness), and the
/// loop-boundary absorption rules hold.
/// </summary>
public sealed class FlowchartDeadPathJoinTests
{
    /// <summary>The untaken connection is recorded dead and the deadness reaches the join; the reachability policy records nothing.</summary>
    [Fact]
    public void UntakenConnection_IsRecordedDead_AndCarriesForwardToTheJoin()
    {
        var deadPath = RunDiamond(FlowchartPolicyKinds.DeadPathJoin);
        var reachability = RunDiamond(FlowchartPolicyKinds.ReachabilityJoin);

        var deadMessages = deadPath.DiagnosticMessages(FlowchartDiagnosticKind.DeadPath);
        Assert.Contains(deadMessages, message => message.Contains("node-decide:Right->node-right", StringComparison.Ordinal) && message.Contains("dead", StringComparison.Ordinal));
        Assert.Contains(deadMessages, message => message.Contains("did not schedule node 'node-right'", StringComparison.Ordinal));
        Assert.Contains(deadMessages, message => message.Contains("node-right:Done->node-join", StringComparison.Ordinal));

        Assert.Empty(reachability.DiagnosticMessages(FlowchartDiagnosticKind.DeadPath));
    }

    /// <summary>The join fires on one live and one dead arrival and consumes both.</summary>
    [Fact]
    public void JoinFiresOnOneLiveAndOneDeadArrival_AndConsumesBoth()
    {
        var run = RunDiamond(FlowchartPolicyKinds.DeadPathJoin);

        Assert.Contains(run.DiagnosticMessages(FlowchartDiagnosticKind.Joined),
            message => message.Contains("fired after 2 arrival(s), 1 of them dead", StringComparison.Ordinal));
        Assert.NotNull(run.FlowchartState);
        Assert.DoesNotContain(run.FlowchartState.Arrivals, arrival => arrival.Status == FlowchartArrivalStatus.Dead);
    }

    /// <summary>A dead path over the loop-closing edge is absorbed at the iteration boundary, and the absorption is recorded.</summary>
    [Fact]
    public void DeadPathAcrossALoopClosingEdge_IsAbsorbedAtTheIterationBoundary()
    {
        var gate = new ScriptedPolicy("dead/gate", "node-head", "node-end");
        var run = Run(FlowchartPolicyKinds.DeadPathJoin,
            [new("node-head"), new("node-gate"), new("node-end")],
            [
                Connection("node-head", "node-gate"),
                Connection("node-gate", "node-head", "again"),
                Connection("node-gate", "node-end", "done")
            ],
            startNodeId: "node-head",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-gate"] = new(gate.PolicyKind) },
            policies: [gate]);

        Assert.Equal(["node-head", "node-gate", "node-head", "node-gate", "node-end"], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
        Assert.Contains(run.DiagnosticMessages(FlowchartDiagnosticKind.DeadPath),
            message => message.Contains("absorbed the dead path over loop-closing connection", StringComparison.Ordinal));
    }

    /// <summary>Six iterations with the live branch alternating: the join runs once per iteration, after its own branch, and no dead arrival outlives its iteration.</summary>
    [Fact]
    public void JoinInsideALoop_NeverFiresAnIterationEarly_OnALeftoverDeadArrival()
    {
        const int iterations = 6;
        var picks = Enumerable.Range(0, iterations).Select(index => index % 2 == 0 ? "node-left" : "node-right").ToArray();
        var gates = Enumerable.Range(0, iterations).Select(index => index == iterations - 1 ? "node-end" : "node-head").ToArray();
        var pick = new ScriptedPolicy("dead/pick", picks);
        var gate = new ScriptedPolicy("dead/gate", gates);

        var run = Run(FlowchartPolicyKinds.DeadPathJoin,
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

        var expected = picks.SelectMany(picked => new[] { "node-head", "node-pick", picked, "node-join", "node-gate" }).Append("node-end");
        Assert.Equal(expected, run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
        Assert.NotNull(run.FlowchartState);
        Assert.DoesNotContain(run.FlowchartState.Arrivals, arrival => arrival.Status == FlowchartArrivalStatus.Dead);
    }

    /// <summary>A join owed an arrival by a cancelled race branch is released when the Flowchart goes quiet, and says so.</summary>
    [Fact]
    public void JoinOwedAnArrivalFromACanceledRaceBranch_IsReleasedWhenTheFlowchartGoesQuiet()
    {
        var run = Run(FlowchartPolicyKinds.DeadPathJoin,
            [new("node-fork"), new("node-b"), new("node-c"), new("node-join")],
            [
                Connection("node-fork", "node-b"),
                Connection("node-fork", "node-c"),
                Connection("node-b", "node-join"),
                Connection("node-c", "node-join")
            ],
            startNodeId: "node-fork",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-fork"] = new(FlowchartPolicyKinds.FirstWins) });

        Assert.Equal(["node-fork", "node-b", "node-c", "node-join"], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
        Assert.Contains(run.DiagnosticMessages(FlowchartDiagnosticKind.DeadPath),
            message => message.Contains("no work remains that could produce the outstanding inbound branch(es): node-c", StringComparison.Ordinal));
    }

    /// <summary>A policy that parks its path has not declined its outbound connections: nothing behind it is declared dead.</summary>
    [Fact]
    public void PolicyThatParksWithoutScheduling_DoesNotDeclareItsOutboundBranchesDead()
    {
        var run = Run(FlowchartPolicyKinds.DeadPathJoin,
            [new("node-park"), new("node-next")],
            [Connection("node-park", "node-next")],
            startNodeId: "node-park",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-park"] = new(ParkPolicy.Kind) },
            policies: [new ParkPolicy()]);

        Assert.Equal(["node-park"], run.Order);
        Assert.NotNull(run.FlowchartState);
        Assert.Empty(run.DiagnosticMessages(FlowchartDiagnosticKind.DeadPath));
        Assert.Contains(run.FlowchartState.ExecutionPaths, path => path.Status == ExecutionPathStatus.Waiting);
    }

    [Fact]
    public void JoinPolicyKind_MustNameARegisteredJoinPolicy()
    {
        var registry = new FlowchartPolicyRegistry([new DeadPathJoinFlowchartPolicy(), new MergeFlowchartPolicy()]);

        var missing = Assert.Throws<FlowchartExecutionException>(
            () => new FlowchartJoinCoordinator(registry, new FlowchartOptions { JoinPolicyKind = "nope" }).JoinPolicy);
        Assert.Contains("nope", missing.Message, StringComparison.Ordinal);

        var wrongKind = Assert.Throws<FlowchartExecutionException>(
            () => new FlowchartJoinCoordinator(registry, new FlowchartOptions { JoinPolicyKind = FlowchartPolicyKinds.Merge }).JoinPolicy);
        Assert.Contains(nameof(IFlowchartJoinPolicy), wrongKind.Message, StringComparison.Ordinal);
    }

    private static GraphRun RunDiamond(string joinPolicyKind) =>
        Run(joinPolicyKind,
            [new("node-decide", ["Left"]), new("node-left"), new("node-right"), new("node-join")],
            [
                Connection("node-decide", "node-left", "Left"),
                Connection("node-decide", "node-right", "Right"),
                Connection("node-left", "node-join"),
                Connection("node-right", "node-join")
            ],
            startNodeId: "node-decide");

    private sealed class ParkPolicy : IFlowchartPolicy
    {
        public const string Kind = "dead/park";

        public string PolicyKind => Kind;
        public string DisplayName => "Park";

        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) =>
            new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.WaitExecutionPath)]);
    }
}

using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Activities.Flowchart.Tests.FlowchartConformanceCorpus;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// The mechanism behind ADR 0064's join semantics, as opposed to the observable behaviour the conformance
/// corpus pins. The corpus proves the two policies agree; these prove the dead-path policy agrees for the
/// stated reason — that it really represents deadness rather than quietly falling back on the search — and
/// that the absorption rules the ADR calls the subtle part actually hold.
/// </summary>
public sealed class FlowchartDeadPathJoinTests
{
    /// <summary>
    /// A decision's untaken outbound connection is recorded dead, and the deadness travels to the join rather
    /// than being re-derived there. Under the reachability policy the same graph records nothing at all —
    /// which is the point: agreement in the corpus is not the two policies sharing one code path.
    /// </summary>
    [Fact]
    public async Task UntakenConnection_IsRecordedDead_AndCarriesForwardToTheJoin()
    {
        var deadPath = await RunDiamondAsync(FlowchartPolicyKinds.DeadPathJoin);
        var reachability = await RunDiamondAsync(FlowchartPolicyKinds.ReachabilityJoin);

        var deadMessages = DiagnosticMessages(deadPath, FlowchartDiagnosticKind.DeadPath);
        // The edge the decision did not take is marked dead where it was not taken...
        Assert.Contains(deadMessages, message => message.Contains("node-decide:Right->node-right", StringComparison.Ordinal) && message.Contains("dead", StringComparison.Ordinal));
        // ...the node behind it is not scheduled, because every arrival it holds is dead...
        Assert.Contains(deadMessages, message => message.Contains("did not schedule node 'node-right'", StringComparison.Ordinal));
        // ...and the deadness reaches the join, so the join answers from arrivals rather than from a search.
        Assert.Contains(deadMessages, message => message.Contains("node-right:Done->node-join", StringComparison.Ordinal));

        Assert.Empty(DiagnosticMessages(reachability, FlowchartDiagnosticKind.DeadPath));
    }

    /// <summary>
    /// The join fires with one live and one dead arrival, and consumes <b>both</b>. Leaving the dead one behind
    /// is exactly how a path from one iteration would end up satisfying a join in the next.
    /// </summary>
    [Fact]
    public async Task JoinFiresOnOneLiveAndOneDeadArrival_AndConsumesBoth()
    {
        var run = await RunDiamondAsync(FlowchartPolicyKinds.DeadPathJoin);

        Assert.Contains(DiagnosticMessages(run, FlowchartDiagnosticKind.Joined),
            message => message.Contains("fired after 2 arrival(s), 1 of them dead", StringComparison.Ordinal));
        Assert.NotNull(run.FlowchartState);
        Assert.DoesNotContain(run.FlowchartState.Arrivals, arrival => arrival.Status == FlowchartArrivalStatus.Dead);
    }

    /// <summary>
    /// Absorption at the loop boundary. The gate's untaken outbound in the first two iterations is the exit
    /// edge and in the last one the loop-closing edge; only the latter is absorbed, and the absorption is
    /// recorded rather than silent. Nothing dead may cross into an iteration that has not happened.
    /// </summary>
    [Fact]
    public async Task DeadPathAcrossALoopClosingEdge_IsAbsorbedAtTheIterationBoundary()
    {
        var gate = new ScriptedPolicy("dead/gate", "node-head", "node-end");
        var run = await RunAsync(FlowchartPolicyKinds.DeadPathJoin, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-head"),
                fixture.NewProbeNode("node-gate"),
                fixture.NewProbeNode("node-end")
            ],
            connections:
            [
                fixture.NewConnection("node-head", "node-gate"),
                fixture.NewConnection("node-gate", "node-head", "again"),
                fixture.NewConnection("node-gate", "node-end", "done")
            ],
            startNodeId: "node-head",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-gate"] = new(gate.PolicyKind) }),
            gate);

        Assert.Equal(["node-head", "node-gate", "node-head", "node-gate", "node-end"], run.Order);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
        Assert.Contains(DiagnosticMessages(run, FlowchartDiagnosticKind.DeadPath),
            message => message.Contains("absorbed the dead path over loop-closing connection", StringComparison.Ordinal));
    }

    /// <summary>
    /// The failure mode ADR 0064 says to test hardest, driven long enough to see it. A join inside a loop whose
    /// live branch alternates gets a dead arrival from the other branch every iteration; if one of those ever
    /// survived into the next iteration, the join would fire before its own live branch had run. Six iterations
    /// alternating left/right, asserting the join ran exactly once per iteration, always after its own branch,
    /// and that no dead arrival is ever left holding a key from a previous iteration.
    /// </summary>
    [Fact]
    public async Task JoinInsideALoop_NeverFiresAnIterationEarly_OnALeftoverDeadArrival()
    {
        const int iterations = 6;
        var picks = Enumerable.Range(0, iterations).Select(index => index % 2 == 0 ? "node-left" : "node-right").ToArray();
        var gates = Enumerable.Range(0, iterations).Select(index => index == iterations - 1 ? "node-end" : "node-head").ToArray();
        var pick = new ScriptedPolicy("dead/pick", picks);
        var gate = new ScriptedPolicy("dead/gate", gates);

        var run = await RunAsync(FlowchartPolicyKinds.DeadPathJoin, fixture => fixture.NewExecutable(
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

        var expected = picks
            .SelectMany(picked => new[] { "node-head", "node-pick", picked, "node-join", "node-gate" })
            .Append("node-end");
        Assert.Equal(expected, run.Order);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);

        // No dead arrival outlived the iteration that produced it: every one was consumed by the join it was
        // owed to, in the iteration it belonged to.
        Assert.NotNull(run.FlowchartState);
        Assert.DoesNotContain(run.FlowchartState.Arrivals, arrival => arrival.Status == FlowchartArrivalStatus.Dead);
    }

    /// <summary>
    /// A first-wins race cancels its losing branch, so the join behind it is owed an arrival nobody will ever
    /// produce — the one shape a purely local join cannot see, because nothing completed to record it. The
    /// Flowchart going quiet is what forces the decision, and it says so rather than parking.
    /// </summary>
    [Fact]
    public async Task JoinOwedAnArrivalFromACanceledRaceBranch_IsReleasedWhenTheFlowchartGoesQuiet()
    {
        var run = await RunAsync(FlowchartPolicyKinds.DeadPathJoin, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-fork"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-join")
            ],
            connections:
            [
                fixture.NewConnection("node-fork", "node-b"),
                fixture.NewConnection("node-fork", "node-c"),
                fixture.NewConnection("node-b", "node-join"),
                fixture.NewConnection("node-c", "node-join")
            ],
            startNodeId: "node-fork",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-fork"] = new(FlowchartPolicyKinds.FirstWins) }));

        Assert.Equal(["node-fork", "node-b", "node-c", "node-join"], run.Order);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Status);
        Assert.Contains(DiagnosticMessages(run, FlowchartDiagnosticKind.DeadPath),
            message => message.Contains("no work remains that could produce the outstanding inbound branch(es): node-c", StringComparison.Ordinal));
    }

    /// <summary>
    /// A policy that parks its path instead of routing has not declined its outbound connections — it has not
    /// answered yet. Reading that silence as "took none of them" would declare every branch behind the node
    /// dead on the strength of a decision the policy never made, which is unrecoverable: the park is meant to
    /// be resumed.
    /// </summary>
    [Fact]
    public async Task PolicyThatParksWithoutScheduling_DoesNotDeclareItsOutboundBranchesDead()
    {
        var run = await RunAsync(FlowchartPolicyKinds.DeadPathJoin, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-park"),
                fixture.NewProbeNode("node-next")
            ],
            connections: [fixture.NewConnection("node-park", "node-next")],
            startNodeId: "node-park",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["node-park"] = new(ParkPolicy.Kind) }),
            new ParkPolicy());

        // The parked node ran; nothing behind it was scheduled, and nothing behind it was killed either.
        Assert.Equal(["node-park"], run.Order);
        Assert.NotNull(run.FlowchartState);
        Assert.Empty(DiagnosticMessages(run, FlowchartDiagnosticKind.DeadPath));
        Assert.Contains(run.FlowchartState.ExecutionPaths, path => path.Status == ExecutionPathStatus.Waiting);
    }

    [Fact]
    public void JoinPolicyKind_MustNameARegisteredJoinPolicy()
    {
        var registry = new FlowchartPolicyRegistry([new DeadPathJoinFlowchartPolicy(), new MergeFlowchartPolicy()]);

        var missing = Assert.Throws<FlowchartExecutionException>(
            () => new FlowchartJoinCoordinator(registry, new FlowchartOptions { JoinPolicyKind = "nope" }).JoinPolicy);
        Assert.Contains("nope", missing.Message, StringComparison.Ordinal);

        // Registered, but not a join policy — a configuration mistake that must not silently pick something else.
        var wrongKind = Assert.Throws<FlowchartExecutionException>(
            () => new FlowchartJoinCoordinator(registry, new FlowchartOptions { JoinPolicyKind = FlowchartPolicyKinds.Merge }).JoinPolicy);
        Assert.Contains(nameof(IFlowchartJoinPolicy), wrongKind.Message, StringComparison.Ordinal);
    }

    private static Task<CorpusRun> RunDiamondAsync(string joinPolicyKind) =>
        RunAsync(joinPolicyKind, fixture => fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-decide", ["Left"]),
                fixture.NewProbeNode("node-left"),
                fixture.NewProbeNode("node-right"),
                fixture.NewProbeNode("node-join")
            ],
            connections:
            [
                fixture.NewConnection("node-decide", "node-left", "Left"),
                fixture.NewConnection("node-decide", "node-right", "Right"),
                fixture.NewConnection("node-left", "node-join"),
                fixture.NewConnection("node-right", "node-join")
            ],
            startNodeId: "node-decide"));

    /// <summary>Parks its path rather than routing — the "I have not decided yet" decision.</summary>
    private sealed class ParkPolicy : IFlowchartPolicy
    {
        public const string Kind = "dead/park";

        public string PolicyKind => Kind;
        public string DisplayName => "Park";

        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) =>
            new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.WaitExecutionPath)]);
    }

    private static string[] DiagnosticMessages(CorpusRun run, FlowchartDiagnosticKind kind)
    {
        Assert.NotNull(run.FlowchartState);
        return run.FlowchartState.Diagnostics.Where(diagnostic => diagnostic.Kind == kind).Select(diagnostic => diagnostic.Message).ToArray();
    }
}

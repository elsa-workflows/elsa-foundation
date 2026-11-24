using Kernel.Graph.Internal.Policies;
using Kernel.Graph.Models;
using Xunit;
using static Kernel.Graph.Tests.GraphHarness;

namespace Kernel.Graph.Tests;

public sealed class FlowchartSameTargetDecisionTests
{
    public static IEnumerable<object[]> Variants()
    {
        foreach (var policy in new[] { FlowchartPolicyKinds.DeadPathJoin, FlowchartPolicyKinds.ReachabilityJoin })
        foreach (var outcome in new[] { "True", "False" })
        foreach (var reverseFork in new[] { false, true })
        foreach (var reverseDecision in new[] { false, true })
        foreach (var taggedDecision in new[] { false, true })
        foreach (var explicitJoin in new[] { false, true })
            yield return [policy, outcome, reverseFork, reverseDecision, taggedDecision, explicitJoin];
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void SharedTarget_AndDownstreamJoin_RunOnce(
        string policy, string outcome, bool reverseFork, bool reverseDecision, bool taggedDecision, bool explicitJoin)
    {
        var fork = new[] { Connection("start", "decide"), Connection("start", "other") };
        var decision = new[] { Connection("decide", "mid", "True"), Connection("decide", "mid", "False") };
        var connections = (reverseFork ? fork.Reverse() : fork)
            .Concat(reverseDecision ? decision.Reverse() : decision)
            .Concat([Connection("mid", "join"), Connection("other", "join"), Connection("join", "end")])
            // Explicit IDs must match arrivals just as derived IDs do. Exercise both with both outcome/edge orders.
            .Select((c, i) => taggedDecision ? new FlowchartConnection(c.Source, c.Target, $"edge-{i}") : c)
            .ToArray();
        var metadata = new Dictionary<string, FlowchartNodeMetadata>();
        if (taggedDecision) metadata["decide"] = new(FlowchartPolicyKinds.Decision);
        if (explicitJoin) metadata["join"] = new(FlowchartPolicyKinds.ParallelJoin);

        var run = Run(policy,
            [new("start"), new("decide", [outcome]), new("other"), new("mid"), new("join"), new("end")],
            connections, startNodeId: "start", nodeMetadata: metadata);

        Assert.Equal(GraphRunStatus.Completed, run.Status);
        Assert.Equal(1, run.RunCount("mid"));
        Assert.Equal(1, run.RunCount("join"));
        Assert.Equal(1, run.RunCount("end"));
        Assert.True(Array.IndexOf(run.Order.ToArray(), "mid") < Array.IndexOf(run.Order.ToArray(), "join"));
        Assert.True(Array.IndexOf(run.Order.ToArray(), "other") < Array.IndexOf(run.Order.ToArray(), "join"));
        Assert.DoesNotContain(run.DiagnosticMessages(FlowchartDiagnosticKind.DeadPath),
            message => message.Contains("did not schedule node 'mid'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FlowchartPolicyKinds.DeadPathJoin, false, false)]
    [InlineData(FlowchartPolicyKinds.DeadPathJoin, false, true)]
    [InlineData(FlowchartPolicyKinds.DeadPathJoin, true, false)]
    [InlineData(FlowchartPolicyKinds.DeadPathJoin, true, true)]
    [InlineData(FlowchartPolicyKinds.ReachabilityJoin, false, false)]
    [InlineData(FlowchartPolicyKinds.ReachabilityJoin, false, true)]
    [InlineData(FlowchartPolicyKinds.ReachabilityJoin, true, false)]
    [InlineData(FlowchartPolicyKinds.ReachabilityJoin, true, true)]
    public void TwoTakenEdgesToTheSameTarget_RemainInvalid(string policy, bool taggedDecision, bool identicalEdges)
    {
        // Choosing both outcomes is invalid, as is declaring the same taken edge twice.
        var run = Run(policy,
            [new("decide", identicalEdges ? ["True"] : ["True", "False"]), new("mid"), new("join")],
            [Connection("decide", "mid", "True"), Connection("decide", "mid", identicalEdges ? "True" : "False"), Connection("mid", "join")],
            startNodeId: "decide", nodeMetadata: taggedDecision
                ? new Dictionary<string, FlowchartNodeMetadata> { ["decide"] = new(FlowchartPolicyKinds.Decision) }
                : null);

        Assert.Equal(GraphRunStatus.Faulted, run.Status);
        Assert.Equal(0, run.RunCount("join"));
    }

    [Theory]
    [InlineData(FlowchartPolicyKinds.DeadPathJoin)]
    [InlineData(FlowchartPolicyKinds.ReachabilityJoin)]
    public void SharedTarget_WithAllItsOutcomeEdgesDead_CarriesDeadnessWithoutExecutingIt(string policy)
    {
        var run = Run(policy,
            [new("start"), new("decide", ["Skip"]), new("other"), new("mid"), new("join"), new("end")],
            [Connection("start", "decide"), Connection("start", "other"),
             Connection("decide", "mid", "True"), Connection("decide", "mid", "False"),
             Connection("mid", "join"), Connection("other", "join"), Connection("join", "end")],
            startNodeId: "start");

        Assert.Equal(["start", "decide", "other", "join", "end"], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }

    [Theory]
    [InlineData(FlowchartPolicyKinds.DeadPathJoin, "True")]
    [InlineData(FlowchartPolicyKinds.DeadPathJoin, "False")]
    [InlineData(FlowchartPolicyKinds.ReachabilityJoin, "True")]
    [InlineData(FlowchartPolicyKinds.ReachabilityJoin, "False")]
    public void SharedTarget_InsideALoop_JoinsOnceInEachIteration(string policy, string outcome)
    {
        var gate = new ScriptedPolicy("same-target/gate", "start", "start", "end");
        var run = Run(policy,
            [new("start"), new("decide", [outcome]), new("other"), new("mid"), new("join"), new("gate"), new("end")],
            [Connection("start", "decide"), Connection("start", "other"),
             Connection("decide", "mid", "True"), Connection("decide", "mid", "False"),
             Connection("mid", "join"), Connection("other", "join"), Connection("join", "gate"),
             Connection("gate", "start", "Again"), Connection("gate", "end", "Done")],
            startNodeId: "start", nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["gate"] = new(gate.PolicyKind) },
            policies: [gate]);

        Assert.Equal(Enumerable.Range(0, 3).SelectMany(_ => new[] { "start", "decide", "other", "mid", "join", "gate" }).Append("end"), run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }
}

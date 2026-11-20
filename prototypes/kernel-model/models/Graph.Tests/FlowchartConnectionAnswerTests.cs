using Kernel.Graph.Internal.Policies;
using Kernel.Graph.Models;
using Xunit;
using static Kernel.Graph.Tests.GraphHarness;

namespace Kernel.Graph.Tests;

public sealed class FlowchartConnectionAnswerTests
{
    public static IEnumerable<object[]> Variants()
    {
        foreach (var policy in new[] { FlowchartPolicyKinds.DeadPathJoin, FlowchartPolicyKinds.ReachabilityJoin })
        foreach (var ids in new[] { "derived-collision", "authored-derived-overlap", "reused-authored", "distinct-authored" })
        foreach (var reverseFork in new[] { false, true })
            yield return [policy, ids, reverseFork];
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void LiveFork_WaitsForBothStructuralSources_EvenWhenTheirWireIdsCollide(string policy, string ids, bool reverseFork)
    {
        var (left, right) = Inbounds(ids);
        AssertWireIds(left, right, ids);
        var fork = new[] { Connection("start", left.Source.NodeId), Connection("start", right.Source.NodeId) };
        var run = Run(policy,
            [new("start"), new(left.Source.NodeId, [left.Source.Port]), new(right.Source.NodeId), new("join"), new("end")],
            [.. reverseFork ? fork.Reverse() : fork, left, right, Connection("join", "end")], startNodeId: "start");

        Assert.Equal(GraphRunStatus.Completed, run.Status);
        Assert.Equal(1, run.RunCount("join"));
        Assert.Equal(1, run.RunCount("end"));
        var order = run.Order.ToArray();
        Assert.True(Array.IndexOf(order, "join") > Array.IndexOf(order, left.Source.NodeId));
        Assert.True(Array.IndexOf(order, "join") > Array.IndexOf(order, right.Source.NodeId));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Merge_DeadAnswerDoesNotReplaceLiveSource_AtTheDownstreamBarrier(string policy, string ids, bool reverseFork)
    {
        var (left, right) = Inbounds(ids);
        AssertWireIds(left, right, ids);
        var fork = new[] { Connection("start", left.Source.NodeId), Connection("start", right.Source.NodeId) };
        var run = Run(policy,
            [new("start"), new(left.Source.NodeId, ["Decline"]), new(right.Source.NodeId), new("join"), new("third"), new("barrier"), new("end")],
            [.. reverseFork ? fork.Reverse() : fork, Connection("start", "third"), left, right,
             Connection("join", "barrier"), Connection("third", "barrier"), Connection("barrier", "end")],
            startNodeId: "start", nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["join"] = new(FlowchartPolicyKinds.Merge) });

        Assert.Equal(GraphRunStatus.Completed, run.Status);
        Assert.Equal(1, run.RunCount("join"));
        Assert.Equal(1, run.RunCount("barrier"));
        Assert.Equal(1, run.RunCount("end"));
        var order = run.Order.ToArray();
        Assert.True(Array.IndexOf(order, "barrier") > Array.IndexOf(order, "join"));
        Assert.True(Array.IndexOf(order, "barrier") > Array.IndexOf(order, "third"));
        Assert.DoesNotContain(run.DiagnosticMessages(FlowchartDiagnosticKind.DeadPath),
            message => message.Contains("did not schedule node 'join'", StringComparison.Ordinal));
    }

    private static (FlowchartConnection Left, FlowchartConnection Right) Inbounds(string ids)
    {
        var port = ids == "authored-derived-overlap" ? "Free" : "route:Done";
        var leftId = ids switch
        {
            "authored-derived-overlap" => "worker:route:Done->join",
            "reused-authored" => "common",
            "distinct-authored" => "left-edge",
            _ => null
        };
        var rightId = ids switch { "reused-authored" => "common", "distinct-authored" => "right-edge", _ => null };
        return (new(new("worker", port), new("join"), leftId), new(new("worker:route"), new("join"), rightId));
    }

    private static void AssertWireIds(FlowchartConnection left, FlowchartConnection right, string ids)
    {
        // Accepted punctuation and omitted default port remain intact; the collision is deliberate.
        Assert.Equal("worker", left.Source.NodeId);
        Assert.Equal("worker:route", right.Source.NodeId);
        Assert.Equal(FlowchartOutcomes.Done, right.Source.Port);
        if (ids == "distinct-authored") Assert.NotEqual(left.GetId(), right.GetId());
        else Assert.Equal(left.GetId(), right.GetId());
    }
}

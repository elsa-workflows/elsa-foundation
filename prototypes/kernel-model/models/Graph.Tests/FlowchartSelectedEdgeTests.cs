using Kernel.Graph.Contracts;
using Kernel.Graph.Internal;
using Kernel.Graph.Internal.Policies;
using Kernel.Graph.Models;
using Xunit;
using static Kernel.Graph.Tests.GraphHarness;

namespace Kernel.Graph.Tests;

public sealed class FlowchartSelectedEdgeTests
{
    public static IEnumerable<object[]> Variants()
    {
        foreach (var ids in new[] { "distinct", "shared", "authored-derived" })
        foreach (var outcome in new[] { "True", "False" })
        foreach (var tagged in new[] { false, true })
        foreach (var reverseEdges in new[] { false, true })
        foreach (var otherFirst in new[] { false, true })
            yield return [ids, outcome, tagged, reverseEdges, otherFirst];
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void SelectedOutcome_SchedulesMidImmediately_AndJoinsAfterBothBranches(
        string ids, string outcome, bool tagged, bool reverseEdges, bool otherFirst) =>
        RunCase(ids, outcome, tagged ? FlowchartPolicyKinds.Decision : null, reverseEdges, otherFirst, outcome);

    [Theory]
    [InlineData(false, "True", false)]
    [InlineData(false, "True", true)]
    [InlineData(false, "False", false)]
    [InlineData(false, "False", true)]
    [InlineData(true, "True", false)]
    [InlineData(true, "True", true)]
    [InlineData(true, "False", false)]
    [InlineData(true, "False", true)]
    public void LegacyIdOrNodeOnlyPolicy_KeepsFirstEdgeResolution_AndEmitsTheOtherEdgeDead(bool idOnly, string outcome, bool reverseEdges)
    {
        var policy = new LegacyPolicy(idOnly);
        RunCase("shared", outcome, policy.PolicyKind, reverseEdges, false, reverseEdges ? "False" : "True", policy);
    }

    [Theory]
    [InlineData(false, "True")]
    [InlineData(false, "False")]
    [InlineData(true, "True")]
    [InlineData(true, "False")]
    public void LegacySingleTargetCommand_WithIdenticalDeclarations_DoesNotInventADeadDuplicate(bool idOnly, string outcome)
    {
        var policy = new LegacyPolicy(idOnly);
        RunCase("identical", outcome, policy.PolicyKind, false, false, "True", policy);
    }

    private static void RunCase(string ids, string outcome, string? policyKind, bool reverseEdges, bool otherFirst, string expectedLivePort, params IFlowchartPolicy[] policies)
    {
        var yesId = ids switch { "distinct" => "yes", "shared" or "identical" => "shared", _ => "decide:False->mid" };
        var noId = ids switch { "distinct" => "no", "shared" or "identical" => "shared", _ => null };
        var decision = new[]
        {
            new FlowchartConnection(new("decide", "True"), new("mid"), yesId),
            new FlowchartConnection(new("decide", ids == "identical" ? "True" : "False"), new("mid"), noId)
        };
        var graph = FlowchartGraph.From([.. new[] { "start", "decide", "other", "mid", "join", "end" }.Select(n => new FlowchartNode(n))],
            new FlowchartStructure([Connection("start", "decide"), Connection("start", "other"),
                .. reverseEdges ? decision.Reverse() : decision, Connection("mid", "join"), Connection("other", "join"), Connection("join", "end")],
                "start", policyKind is null ? null : new Dictionary<string, FlowchartNodeMetadata> { ["decide"] = new(policyKind) }));
        var engine = FlowchartEngines.Create(new FlowchartOptions(), policies);
        var step = engine.Start(graph, new("container"));
        var pending = step.Scheduled.ToList();
        var order = new List<string>();
        void Complete(string node)
        {
            var next = pending.First(n => n.NodeId == node);
            pending.Remove(next);
            order.Add(node);
            step = engine.OnChildCompleted(graph, new("container"), step.State,
                new(node, $"e{order.Count}", [node == "decide" ? outcome : FlowchartOutcomes.Done], next.ExecutionPathId, next.ExecutionScopeId));
            pending.AddRange(step.Scheduled);
            Assert.NotEqual(FlowchartStepKind.Fault, step.Kind);
        }

        Complete("start");
        if (otherFirst) Complete("other");
        Complete("decide");
        Assert.Single(step.Scheduled, n => n.NodeId == "mid");
        Assert.DoesNotContain(step.Scheduled, n => n.NodeId == "join");
        if (!otherFirst) Assert.Contains(step.State.ActiveChildren, n => n.NodeId == "other");
        // Inspect real emitted facts: tagged routing must not silently bind the opposite port.
        var arrivals = step.Live.Arrivals.Where(a => a.TargetNodeId == "mid").ToArray();
        Assert.Equal(ids == "identical" ? 1 : 2, arrivals.Length);
        var deadPaths = step.Live.Diagnostics.Where(d => d.Kind == FlowchartDiagnosticKind.DeadPath).Select(d => d.ExecutionPathId).ToHashSet();
        Assert.DoesNotContain(arrivals.Single(a => a.SourcePort == expectedLivePort).ExecutionPathId, deadPaths);
        if (ids != "identical") Assert.Contains(arrivals.Single(a => a.SourcePort != expectedLivePort).ExecutionPathId, deadPaths);

        while (step.Kind == FlowchartStepKind.Defer && pending.Count > 0 && order.Count < 12) Complete(pending[0].NodeId);
        Assert.Equal(FlowchartStepKind.Complete, step.Kind);
        Assert.Empty(pending);
        foreach (var node in new[] { "mid", "join", "end" }) Assert.Equal(1, order.Count(n => n == node));
        Assert.True(order.IndexOf("join") > order.IndexOf("mid"));
        Assert.True(order.IndexOf("join") > order.IndexOf("other"));
    }

    [Theory]
    [InlineData(FlowchartPolicyKinds.ParallelFork)]
    [InlineData(FlowchartPolicyKinds.InclusiveFork)]
    [InlineData(FlowchartPolicyKinds.Decision)]
    public void BuiltInSelection_RetainsBothExactTargets_WhenTheirIdsCollide(string policyKind)
    {
        var run = Run(FlowchartPolicyKinds.DeadPathJoin,
            [new("fork"), new("a"), new("b"), new("join"), new("end")],
            [new(new("fork"), new("a"), "shared"), new(new("fork"), new("b"), "shared"),
             Connection("a", "join"), Connection("b", "join"), Connection("join", "end")],
            startNodeId: "fork", nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["fork"] = new(policyKind) });
        Assert.Equal(["fork", "a", "b", "join", "end"], run.Order);
        Assert.Equal(GraphRunStatus.Completed, run.Status);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("id")]
    public void UnusableHint_ResolvesOnlyAValidCurrentPublicCommand(string mismatch)
    {
        var policy = new HintPolicy(mismatch);
        var run = Run(FlowchartPolicyKinds.DeadPathJoin,
            [new("decide"), new("other"), new("mid"), new("end")],
            [Connection("decide", "mid"), Connection("other", "mid"), Connection("mid", "end")],
            startNodeId: "decide", nodeMetadata: new Dictionary<string, FlowchartNodeMetadata> { ["decide"] = new(policy.PolicyKind) }, policies: [policy]);
        // The foreign object has a valid public id in this graph. Discard the object and resolve that id.
        Assert.Equal(mismatch == "foreign" ? GraphRunStatus.Completed : GraphRunStatus.Faulted, run.Status);
        Assert.Equal(mismatch == "foreign" ? 1 : 0, run.RunCount("mid"));
    }

    [Fact]
    public void SelectedHint_DoesNotChangeTheSerializedCommand()
    {
        var edge = Connection("decide", "mid", "False");
        var command = new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, "mid", edge.GetId());
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(command),
            System.Text.Json.JsonSerializer.Serialize(command with { SelectedConnection = edge }));
    }

    private sealed class HintPolicy(string mismatch) : IFlowchartPolicy
    {
        public string PolicyKind => "selected-edge/hint";
        public string DisplayName => "Invalid selected-edge hint";
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context)
        {
            var edge = context.Connections.Single(c => c.Source.NodeId == "decide");
            var hint = mismatch switch
            {
                "foreign" => new FlowchartConnection(edge.Source, edge.Target, edge.Id),
                "source" => context.Connections.Single(c => c.Source.NodeId == "other"),
                "target" => context.Connections.Single(c => c.Source.NodeId == "mid"),
                _ => edge
            };
            return new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, "mid", mismatch == "id" ? "wrong" : hint.GetId()) { SelectedConnection = hint }]);
        }
    }

    private sealed class LegacyPolicy(bool idOnly) : IFlowchartPolicy
    {
        public string PolicyKind => "selected-edge/legacy";
        public string DisplayName => "Legacy first-edge command";
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) =>
            new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, nodeId: "mid", connectionId: idOnly ? "shared" : null)]);
    }
}

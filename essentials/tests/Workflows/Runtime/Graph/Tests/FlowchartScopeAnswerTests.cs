using Kernel.Graph.Contracts;
using Kernel.Graph.Internal;
using Kernel.Graph.Internal.Policies;
using Kernel.Graph.Models;
using Xunit;

namespace Kernel.Graph.Tests;

public sealed class FlowchartScopeAnswerTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in new[] { "target", "execution", "unchanged", "ordinary", "race-winner" })
        foreach (var ids in new[] { "distinct", "shared", "authored-derived" })
        foreach (var outcome in new[] { "yes", "no" })
        foreach (var join in new[] { FlowchartPolicyKinds.DeadPathJoin, FlowchartPolicyKinds.ReachabilityJoin })
            yield return [mode, ids, outcome, join];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AlternativesMeetTheirSelectedTargetInItsActualPartition(string mode, string ids, string outcome, string join)
    {
        var policy = new ScopePolicy(mode);
        var metadata = new Dictionary<string, FlowchartNodeMetadata>();
        var race = mode == "race-winner";
        if (race) metadata["race"] = new(FlowchartPolicyKinds.FirstWins);
        else if (mode != "ordinary") metadata["choice"] = new(policy.PolicyKind);
        FlowchartConnection Edge(string from, string to, string? port = null, string? id = null) => new(new(from, port), new(to), id);
        var edges = new List<FlowchartConnection> { Edge("head", "again", "next"), Edge("again", "head"),
            Edge("head", "anchor", "hold"), Edge("head", "held", "enter"), Edge("head", race ? "race" : "choice", "enter"),
            Edge("choice", "again", "next"), Edge("choice", "unused", "skip"), Edge("unused", "unusedEnd"), Edge("mid", "end") };
        if (race) edges.AddRange([Edge("race", "choice"), Edge("race", "loser")]);
        foreach (var target in new[] { "mid", "local" })
        {
            var derived = $"choice:yes->{target}";
            edges.Add(Edge("choice", target, "yes", ids == "authored-derived" ? null : "first-" + target));
            edges.Add(Edge("choice", target, "no", ids switch { "shared" => "first-" + target, "authored-derived" => derived, _ => "second-" + target }));
        }
        var nodes = edges.SelectMany(e => new[] { e.Source.NodeId, e.Target.NodeId }).Distinct().Select(n => new FlowchartNode(n)).ToArray();
        var graph = FlowchartGraph.From(nodes, new FlowchartStructure(edges, "head", metadata));
        var engine = FlowchartEngines.Create(new FlowchartOptions { JoinPolicyKind = join }, policy);
        var step = engine.Start(graph, new("owner"));
        var scheduled = new List<string>();
        var callbacks = 0;
        void Complete(string node, params string[] outcomes)
        {
            var child = Assert.Single(step.State.ActiveChildren, c => c.NodeId == node);
            step = engine.OnChildCompleted(graph, new("owner"), step.State, new(node, $"e{++callbacks}", outcomes, child.ExecutionPathId, child.ExecutionScopeId));
            Assert.Null(step.Fault);
            scheduled.AddRange(step.Scheduled.Select(s => s.NodeId));
        }

        Complete("head", "next"); Complete("again");
        Complete("head", "next", "hold"); Complete("again"); // anchor keeps the first iteration available to the policy
        Complete("head", "enter");
        if (race) Complete("race");
        var current = Assert.Single(step.State.Scopes, s => s.LoopIterationKey == "head:2").ExecutionScopeId;
        var previous = Assert.Single(step.State.Scopes, s => s.LoopIterationKey == "head:1").ExecutionScopeId;
        var origin = Assert.Single(step.State.ActiveChildren, c => c.NodeId == "choice").ExecutionScopeId;
        Assert.Equal(race, origin != current);
        if (race)
        {
            var raceScope = Assert.Single(step.State.Scopes, s => s.ExecutionScopeId == origin);
            Assert.Equal(ExecutionScopeKind.Race, raceScope.Kind);
            Assert.Equal(current, raceScope.ParentExecutionScopeId);
        }
        Complete("choice", outcome);
        Assert.Equal(new[] { "local", "mid" }, step.Scheduled.Select(c => c.NodeId).Order());
        Assert.Contains(step.State.ActiveChildren, c => c.NodeId == "held");
        var moved = mode is "target" or "execution";
        var deadPaths = step.Live.Diagnostics.Where(d => d.Kind == FlowchartDiagnosticKind.DeadPath && d.ExecutionPathId != null)
            .Select(d => d.ExecutionPathId).ToHashSet();
        foreach (var target in new[] { "mid", "local", "unused", "unusedEnd" })
        {
            var selected = target is "mid" or "local";
            var expected = moved && selected ? (target == "mid" ? (step.State.RootExecutionScopeId, (string?)null) : (previous, "head:1")) : (current, "head:2");
            var answers = step.Live.Arrivals.Where(a => a.TargetNodeId == target && a.ProducingActivityExecutionId == $"e{callbacks}").ToArray();
            Assert.Equal((selected ? 1 : 0) + (join == FlowchartPolicyKinds.DeadPathJoin ? 1 : 0), answers.Length);
            foreach (var a in answers)
            {
                Assert.Equal(expected, (a.ExecutionScopeId, a.IterationKey));
                Assert.Equal(FlowchartArrivalStatus.Consumed, a.Status);
                Assert.Equal(!selected || a.SourcePort != outcome, deadPaths.Contains(a.ExecutionPathId));
            }
            if (selected)
            {
                Assert.Contains(answers, a => a.SourcePort == outcome);
                if (join == FlowchartPolicyKinds.DeadPathJoin) Assert.Contains(answers, a => a.SourcePort == (outcome == "yes" ? "no" : "yes"));
                var child = Assert.Single(step.Scheduled, c => c.NodeId == target);
                Assert.Equal(expected, (child.ExecutionScopeId, child.IterationKey));
            }
        }
        Complete("mid"); Assert.Equal("end", Assert.Single(step.Scheduled).NodeId);
        Complete("end"); Complete("local"); Complete("held"); Complete("anchor");
        Assert.Equal(FlowchartStepKind.Complete, step.Kind);
        Assert.Empty(step.State.ActiveChildren);
        Assert.Equal(new[] { "mid", "local", "end" }, scheduled.Where(n => n is "mid" or "local" or "end"));
        Assert.Equal(2, step.State.LoopIterationCounters["head"]);
    }

    private sealed class ScopePolicy(string mode) : IFlowchartPolicy
    {
        public string PolicyKind => "test/scope";
        public string DisplayName => PolicyKind;
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context)
        {
            var commands = new DecisionFlowchartPolicy().Execute(context).Commands;
            if (mode == "unchanged") return new(commands);
            return new(commands.Select(c =>
            {
                var root = c.NodeId == "mid";
                var scope = root ? context.State.RootExecutionScopeId : context.State.Scopes.Single(s => s.LoopIterationKey == "head:1").ExecutionScopeId;
                return (mode == "target") == root ? c with { TargetExecutionScopeId = scope } : c with { ExecutionScopeId = scope };
            }).ToArray());
        }
    }
}

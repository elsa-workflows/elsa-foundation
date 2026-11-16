using Kernel.Graph.Contracts;
using Kernel.Graph.Internal;
using Kernel.Graph.Internal.Policies;
using Kernel.Graph.Models;
using Xunit;

namespace Kernel.Graph.Tests;

public sealed class FlowchartRoutingBoundaryTests
{
    [Theory]
    [InlineData(FlowchartPolicyKinds.DeadPathJoin)]
    [InlineData(FlowchartPolicyKinds.ReachabilityJoin)]
    public void PublicCommandComposition_UsesTheActiveGraphAndPublicSelectors(string joinKind)
    {
        foreach (var edit in new[] { "redirect", "node-only", "id-only", "copied-context", "no-op", "decorate" })
        {
            var policy = new DelegatePolicy(edit);
            var ids = edit == "decorate" || edit == "no-op" || edit == "copied-context" ? ("shared", "shared") : ("first", "second");
            var secondTarget = edit is "redirect" or "node-only" ? "alternate" : "target";
            var graph = FlowchartGraph.From([new("choice"), new("target"), new("alternate")],
                new FlowchartStructure([new(new("choice", "first"), new("target"), ids.Item1),
                        new(new("choice", "second"), new(secondTarget), ids.Item2)], "choice",
                    new Dictionary<string, FlowchartNodeMetadata> { ["choice"] = new(policy.PolicyKind) }));
            var engine = FlowchartEngines.Create(new FlowchartOptions { JoinPolicyKind = joinKind }, [policy]);
            var run = new FlowchartRun("owner");
            var start = engine.Start(graph, run);
            var child = Assert.Single(start.Scheduled);
            var outcome = edit is "decorate" or "no-op" or "copied-context" ? "second" : "first";
            var step = engine.OnChildCompleted(graph, new("owner"), start.State,
                new("choice", "e1", [outcome], child.ExecutionPathId, child.ExecutionScopeId));

            Assert.Equal(secondTarget, Assert.Single(step.Scheduled).NodeId);
            var dead = step.Live.Diagnostics.Where(d => d.Kind == FlowchartDiagnosticKind.DeadPath).Select(d => d.ExecutionPathId).ToHashSet();
            var live = Assert.Single(step.Live.Arrivals, a => !dead.Contains(a.ExecutionPathId));
            Assert.Equal(edit == "copied-context" ? "first" : "second", live.SourcePort);
        }
    }

    private sealed class DelegatePolicy(string edit) : IFlowchartPolicy
    {
        public string PolicyKind => "test/delegate";
        public string DisplayName => PolicyKind;
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context)
        {
            if (edit == "copied-context")
                context = new FlowchartPolicyContext(context.Trigger, context.State,
                    context.Connections.Select(c => new FlowchartConnection(c.Source, c.Target, c.Id)).ToArray(),
                    context.CurrentNodeId, context.OutcomeNames);
            var command = new DecisionFlowchartPolicy().Execute(context).Commands.Single();
            return new([edit switch
            {
                "redirect" => command with { NodeId = "alternate", ConnectionId = "second" },
                "node-only" => command with { NodeId = "alternate", ConnectionId = null },
                "id-only" => command with { ConnectionId = "second" },
                "decorate" => command with { Message = "retained", TargetExecutionScopeId = context.State.RootExecutionScopeId },
                _ => command with { NodeId = command.NodeId, ConnectionId = command.ConnectionId }
            }]);
        }
    }

    [Fact]
    public void SelectionEvidence_IsAbsentFromPublicRecordValue()
    {
        var edge = new FlowchartConnection(new("source"), new("target"));
        var plain = new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, "target", edge.GetId());
        var hinted = plain with { SelectedConnection = edge };
        Assert.Equal(plain, hinted);
        Assert.Equal(plain.GetHashCode(), hinted.GetHashCode());
        Assert.Equal(plain.ToString(), hinted.ToString());
        Assert.NotEqual(plain, hinted with { Message = "changed" });
    }

    [Theory]
    [InlineData(null)]
    [InlineData(FlowchartPolicyKinds.Merge)]
    public void DeadDiamond_EmitsAndConsumesEachStructuralAnswerOnce(string? sinkPolicy)
    {
        var first = new FlowchartConnection(new("choice", "no"), new("a"), "shared");
        var second = new FlowchartConnection(new("a"), new("sink"), "forward");
        var graph = FlowchartGraph.From([new("choice"), new("live"), new("a"), new("b"), new("sink"), new("end")],
            new FlowchartStructure([new(new("choice", "yes"), new("live"), "shared"), first, first,
                new(new("choice", "no"), new("b")), second, second, new(new("a"), new("sink"), "different"),
                new(new("a", "other"), new("sink"), "forward"), new(new("b"), new("sink")), new(new("sink"), new("end"))],
                "choice", sinkPolicy is null ? null : new Dictionary<string, FlowchartNodeMetadata> { ["sink"] = new(sinkPolicy) }));
        var engine = FlowchartEngines.Create();
        var run = new FlowchartRun("owner");
        var start = engine.Start(graph, run);
        var child = Assert.Single(start.Scheduled);
        var step = engine.OnChildCompleted(graph, new("owner"), start.State, new("choice", "e1", ["yes"], child.ExecutionPathId, child.ExecutionScopeId));
        Assert.Equal("live", Assert.Single(step.Scheduled).NodeId);
        foreach (var (target, count) in new[] { ("a", 1), ("b", 1), ("sink", 4), ("end", 1) })
        {
            var answers = step.Live.Arrivals.Where(a => a.TargetNodeId == target).ToArray();
            Assert.Equal(count, answers.Length);
            Assert.All(answers, a => Assert.Equal(FlowchartArrivalStatus.Consumed, a.Status));
            Assert.Equal(count, answers.Select(a => a.GetAnswerKey()).Distinct().Count());
        }
    }

    [Fact]
    public void RepeatedMergeExecutions_EachEmitTheirOwnDeadAnswerInTheSameScope()
    {
        FlowchartConnection Edge(string source, string target, string? port = null) => new(new(source, port), new(target));
        var graph = FlowchartGraph.From([.. new[] { "start", "a", "b", "choice", "live", "unused" }.Select(n => new FlowchartNode(n))],
            new FlowchartStructure([Edge("start", "a"), Edge("start", "b"), Edge("a", "choice"), Edge("b", "choice"),
                Edge("choice", "live", "yes"), Edge("choice", "unused", "no"), Edge("choice", "unused", "no")], "start",
                new Dictionary<string, FlowchartNodeMetadata> { ["choice"] = new(FlowchartPolicyKinds.Merge) }));
        var engine = FlowchartEngines.Create();
        var step = engine.Start(graph, new("owner"));
        var queue = new Queue<FlowchartScheduledNode>(step.Scheduled);
        var waves = 0;
        for (var i = 0; i < 12 && queue.TryDequeue(out var next); i++)
        {
            var execution = $"e{i}";
            step = engine.OnChildCompleted(graph, new("owner"), step.State, new(next.NodeId, execution,
                [next.NodeId == "choice" ? "yes" : FlowchartOutcomes.Done], next.ExecutionPathId, next.ExecutionScopeId));
            foreach (var child in step.Scheduled) queue.Enqueue(child);
            if (next.NodeId != "choice") continue;
            waves++;
            var answer = Assert.Single(step.Live.Arrivals, a => a.TargetNodeId == "unused" && a.ProducingActivityExecutionId == execution);
            Assert.Equal(step.State.RootExecutionScopeId, answer.ExecutionScopeId);
            Assert.Null(answer.IterationKey);
            Assert.Equal("live", Assert.Single(step.Scheduled).NodeId);
        }
        Assert.Equal(2, waves);
        Assert.Equal(FlowchartStepKind.Complete, step.Kind);
        Assert.Empty(queue);
    }

    [Fact]
    public void PublicCoordinator_UsesTheCallersWireIdSet()
    {
        var coordinator = new FlowchartJoinCoordinator(new FlowchartPolicyRegistry(FlowchartEngines.BuiltInPolicies()), new FlowchartOptions());
        var graph = FlowchartGraph.From([new("choice"), new("a"), new("b"), new("c")],
            new FlowchartStructure([new(new("choice"), new("a"), "shared"), new(new("choice"), new("b"), "shared"),
                new(new("choice"), new("c"), "third")], "choice"));
        var run = new FlowchartRun("owner");
        var state = FlowchartEngines.Create().Start(graph, run).State;
        var result = coordinator.RouteUntakenOutbound(run, state, graph, "choice", state.RootExecutionScopeId, null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SHARED" }, "e1");
        Assert.Equal("c", Assert.Single(result.Arrivals).TargetNodeId);
    }
}

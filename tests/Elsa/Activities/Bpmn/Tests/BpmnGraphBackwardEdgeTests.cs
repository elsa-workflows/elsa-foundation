using System.Text.Json;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// spec 122: backward (loop-back) edge classification on <see cref="BpmnGraph"/>. A backward edge is the
/// standard DFS back edge (an edge to a node still on the DFS stack), so only the loop-closing flow is
/// classified — never the forward edges of the cycle — and the set is deterministic and start-order stable.
/// Also covers that the pre-existing structural rules still constrain where a loop-back may land.
/// </summary>
public sealed class BpmnGraphBackwardEdgeTests
{
    [Fact]
    public void SelfLoopOnATask_ClassifiesOnlyTheLoopBackEdge()
    {
        // start → a → b → a (loop) / b → end. Only b→a closes the loop.
        var graph = Graph(
            elements: [Start(), Task("a"), Task("b"), End()],
            flows:
            [
                Flow("f1", "start", "a"),
                Flow("f2", "a", "b"),
                Flow("f3", "b", "a"),
                Flow("f4", "b", "end")
            ]);

        Assert.Equal(["f3"], graph.BackwardFlowIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void DoWhileShape_ClassifiesTheGatewayLoopBackEdge()
    {
        // start → body → gw ; gw --again--> body (loop) ; gw --done--> end.
        var graph = Graph(
            elements: [Start(), Task("body", childNodeId: "node-body"), Gateway("gw", childNodeId: "node-gw"), End()],
            flows:
            [
                Flow("f1", "start", "body"),
                Flow("f2", "body", "gw"),
                Flow("again", "gw", "body"),
                Flow("done", "gw", "end")
            ],
            children: ["node-body", "node-gw"]);

        Assert.Equal(["again"], graph.BackwardFlowIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void NestedLoops_ClassifyBothLoopBackEdges_AndNoForwardEdge()
    {
        // Outer loop a→b→c→…→a ; inner loop b→…→b.
        // start → a → b → c ; c → b (inner back) ; c → d ; d → a (outer back) ; d → end.
        var graph = Graph(
            elements: [Start(), Task("a"), Task("b"), Task("c"), Task("d"), End()],
            flows:
            [
                Flow("f1", "start", "a"),
                Flow("f2", "a", "b"),
                Flow("f3", "b", "c"),
                Flow("inner", "c", "b"),
                Flow("f4", "c", "d"),
                Flow("outer", "d", "a"),
                Flow("f5", "d", "end")
            ]);

        Assert.Equal(["inner", "outer"], graph.BackwardFlowIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void MultipleStartEvents_ClassificationIsStableRegardlessOfStartOrder()
    {
        // Two start events converge on a loop: start-1/start-2 → merge → body → gw ; gw→merge (back) ; gw→end.
        // "merge" is a plain task (implicit XOR), so it may take multiple inbound.
        BpmnGraph Build(IReadOnlyCollection<BpmnElement> elements) => Graph(
            elements: elements,
            flows:
            [
                Flow("f1", "start-1", "merge"),
                Flow("f2", "start-2", "merge"),
                Flow("f3", "merge", "body"),
                Flow("f4", "body", "gw"),
                Flow("back", "gw", "merge"),
                Flow("done", "gw", "end")
            ],
            children: ["node-body", "node-gw"]);

        var forward = Build([Start("start-1"), Start("start-2"), Task("merge"), Task("body", childNodeId: "node-body"), Gateway("gw", childNodeId: "node-gw"), End()]);
        var reordered = Build([Start("start-2"), Start("start-1"), Gateway("gw", childNodeId: "node-gw"), Task("body", childNodeId: "node-body"), Task("merge"), End()]);

        Assert.Equal(["back"], forward.BackwardFlowIds.OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(forward.BackwardFlowIds.OrderBy(id => id, StringComparer.Ordinal), reordered.BackwardFlowIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void ParallelForkJoinInsideLoop_ClassifiesOnlyTheSingleLoopBackEdge()
    {
        // start → fork ; fork→a, fork→b ; a→join, b→join ; join→gw ; gw→fork (back) ; gw→end.
        // The fork's forward edges MUST NOT be classified backward (that would break iteration grouping).
        var graph = Graph(
            elements:
            [
                Start(),
                new BpmnElement("fork", BpmnElementTypes.ParallelGateway),
                Task("a", childNodeId: "node-a"),
                Task("b", childNodeId: "node-b"),
                new BpmnElement("join", BpmnElementTypes.ParallelGateway),
                Gateway("gw", childNodeId: "node-gw"),
                End()
            ],
            flows:
            [
                Flow("f1", "start", "fork"),
                Flow("f2", "fork", "a"),
                Flow("f3", "fork", "b"),
                Flow("f4", "a", "join"),
                Flow("f5", "b", "join"),
                Flow("f6", "join", "gw"),
                Flow("back", "gw", "fork"),
                Flow("done", "gw", "end")
            ],
            children: ["node-a", "node-b", "node-gw"]);

        Assert.Equal(["back"], graph.BackwardFlowIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void LoopBackIntoStartEvent_IsRejectedByTheStartEventRule()
    {
        var exception = Assert.Throws<BpmnExecutionException>(() => Graph(
            elements: [Start(), Task("a"), End()],
            flows: [Flow("f1", "start", "a"), Flow("loop", "a", "start"), Flow("f2", "a", "end")]));
        Assert.Contains("cannot have inbound sequence flows", exception.Message);
    }

    [Fact]
    public void LoopBackIntoBoundaryEvent_IsRejectedByTheBoundaryRule()
    {
        var exception = Assert.Throws<BpmnExecutionException>(() => Graph(
            elements:
            [
                Start(),
                Task("host", childNodeId: "node-host"),
                BpmnRuntimeFixture.BoundaryEvent("boundary", "host", BpmnEventDefinitionTypes.Timer, childNodeId: "node-listener"),
                Task("after"),
                End()
            ],
            flows:
            [
                Flow("f1", "start", "host"),
                Flow("f2", "host", "after"),
                Flow("f3", "boundary", "after"),
                Flow("loop", "after", "boundary"),
                Flow("f4", "after", "end")
            ],
            children: ["node-host", "node-listener"]));
        Assert.Contains("cannot have inbound sequence flows", exception.Message);
    }

    [Fact]
    public void LoopBackIntoEventGatewayArmedCatch_IsRejectedByTheExactlyOneInboundRule()
    {
        var exception = Assert.Throws<BpmnExecutionException>(() => Graph(
            elements:
            [
                Start(),
                new BpmnElement("gw", BpmnElementTypes.EventBasedGateway),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-a", BpmnEventDefinitionTypes.Message, childNodeId: "node-a"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-b", BpmnEventDefinitionTypes.Signal, childNodeId: "node-b"),
                End()
            ],
            flows:
            [
                Flow("f1", "start", "gw"),
                Flow("f2", "gw", "catch-a"),
                Flow("f3", "gw", "catch-b"),
                // A loop-back into catch-a gives it a second inbound flow, so it is no longer a pure race member.
                Flow("loop", "catch-b", "catch-a"),
                Flow("f4", "catch-a", "end")
            ],
            children: ["node-a", "node-b"]));
        Assert.Contains("exactly one inbound flow", exception.Message);
    }

    private static BpmnElement Start(string elementId = "start") => BpmnRuntimeFixture.StartEvent(elementId);
    private static BpmnElement End(string elementId = "end") => BpmnRuntimeFixture.EndEvent(elementId);
    private static BpmnElement Task(string elementId, string? childNodeId = null) => BpmnRuntimeFixture.Task(elementId, childNodeId: childNodeId);
    private static BpmnElement Gateway(string elementId, string? childNodeId = null) => BpmnRuntimeFixture.ExclusiveGateway(elementId, childNodeId: childNodeId);
    private static BpmnSequenceFlow Flow(string flowId, string source, string target) => BpmnRuntimeFixture.Flow(flowId, source, target);

    private static BpmnGraph Graph(
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> flows,
        IReadOnlyCollection<string>? children = null)
    {
        var childNodes = (children ?? []).Select(id => Elsa.Activities.Testing.WorkflowExecutionHarness.NewProbeNode(id)).ToArray();
        var node = new ExecutableNode(
            executableNodeId: "node-bpmn",
            authoredActivityId: "authored-bpmn",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "BpmnDescriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, childNodes)],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnStructure(elements, flows))));
        return BpmnGraph.From(node);
    }
}

using System.Text.Json;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnGraphValidationTests
{
    [Fact]
    public void CyclicGraph_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("task-a"),
                BpmnRuntimeFixture.Task("task-b"),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "task-a"),
                BpmnRuntimeFixture.Flow("flow-2", "task-a", "task-b"),
                BpmnRuntimeFixture.Flow("flow-3", "task-b", "task-a"),
                BpmnRuntimeFixture.Flow("flow-4", "task-b", "end")
            ]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingStartEvent_IsRejected()
    {
        var node = NewExecutableNode(
            elements: [BpmnRuntimeFixture.Task("task-a"), BpmnRuntimeFixture.EndEvent()],
            flows: [BpmnRuntimeFixture.Flow("flow-1", "task-a", "end")]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("start event", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownFlowTarget_IsRejected()
    {
        var node = NewExecutableNode(
            elements: [BpmnRuntimeFixture.StartEvent(), BpmnRuntimeFixture.EndEvent()],
            flows: [BpmnRuntimeFixture.Flow("flow-1", "start", "missing")]);

        Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
    }

    [Fact]
    public void SubprocessWithoutChild_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                new BpmnElement("sub-1", BpmnElementTypes.SubProcess),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "sub-1"),
                BpmnRuntimeFixture.Flow("flow-2", "sub-1", "end")
            ]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("requires a bound child", exception.Message);
    }

    [Fact]
    public void GatewayBindingRules_ParallelGatewayCannotBindChild()
    {
        var node = NewExecutableNode(
            children: [WorkflowChild("node-a")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                new BpmnElement("fork", BpmnElementTypes.ParallelGateway, childNodeId: "node-a"),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "fork"),
                BpmnRuntimeFixture.Flow("flow-2", "fork", "end")
            ]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("cannot bind a child activity", exception.Message);
    }

    [Fact]
    public void UnboundChildActivity_IsRejected()
    {
        var node = NewExecutableNode(
            children: [WorkflowChild("node-orphan")],
            elements: [BpmnRuntimeFixture.StartEvent(), BpmnRuntimeFixture.EndEvent()],
            flows: [BpmnRuntimeFixture.Flow("flow-1", "start", "end")]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("not bound to any element", exception.Message);
    }

    [Fact]
    public void UnsupportedElementType_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                new BpmnElement("gw", "complexGateway"),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "gw"),
                BpmnRuntimeFixture.Flow("flow-2", "gw", "end")
            ]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("does not support", exception.Message);
    }

    [Fact]
    public void CatchEventWithoutChild_IsRejected()
    {
        var node = NewCatchEventNode(BpmnRuntimeFixture.IntermediateCatchEvent("catch-1", BpmnEventDefinitionTypes.Message));

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("requires a bound suspending child", exception.Message);
    }

    [Fact]
    public void CatchEventWithoutEventDefinitions_IsRejected()
    {
        var node = NewCatchEventNode(new BpmnElement("catch-1", BpmnElementTypes.IntermediateCatchEvent, childNodeId: "node-a"));

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("exactly one event definition", exception.Message);
    }

    [Fact]
    public void CatchEventWithMultipleEventDefinitions_IsRejected()
    {
        var node = NewCatchEventNode(new BpmnElement(
            "catch-1",
            BpmnElementTypes.IntermediateCatchEvent,
            childNodeId: "node-a",
            eventDefinitions:
            [
                new BpmnEventDefinition(BpmnEventDefinitionTypes.Message),
                new BpmnEventDefinition(BpmnEventDefinitionTypes.Signal)
            ]));

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("exactly one event definition", exception.Message);
    }

    [Fact]
    public void CatchEventWithUnsupportedEventDefinition_IsRejected()
    {
        var node = NewCatchEventNode(BpmnRuntimeFixture.IntermediateCatchEvent("catch-1", BpmnEventDefinitionTypes.Error, childNodeId: "node-a"));

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("only timer, message, and signal catch events", exception.Message);
    }

    [Fact]
    public void EventBasedGateway_WithSingleOutboundFlow_IsRejected()
    {
        var node = NewExecutableNode(
            children: [WorkflowChild("node-a")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.EventBasedGateway("gw"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-a", BpmnEventDefinitionTypes.Message, childNodeId: "node-a"),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "gw"),
                BpmnRuntimeFixture.Flow("flow-2", "gw", "catch-a"),
                BpmnRuntimeFixture.Flow("flow-3", "catch-a", "end")
            ]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("at least two outbound", exception.Message);
    }

    [Fact]
    public void EventBasedGateway_TargetingNonCatchEvent_IsRejected()
    {
        var node = NewExecutableNode(
            children: [WorkflowChild("node-a")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.EventBasedGateway("gw"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-a", BpmnEventDefinitionTypes.Message, childNodeId: "node-a"),
                BpmnRuntimeFixture.Task("task-b"),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "gw"),
                BpmnRuntimeFixture.Flow("flow-2", "gw", "catch-a"),
                BpmnRuntimeFixture.Flow("flow-3", "gw", "task-b"),
                BpmnRuntimeFixture.Flow("flow-4", "catch-a", "end"),
                BpmnRuntimeFixture.Flow("flow-5", "task-b", "end")
            ]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("must target an intermediate catch event", exception.Message);
    }

    [Fact]
    public void EventBasedGateway_TargetCatchWithExtraInboundFlow_IsRejected()
    {
        var node = NewExecutableNode(
            children: [WorkflowChild("node-a"), WorkflowChild("node-b")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.EventBasedGateway("gw"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-a", BpmnEventDefinitionTypes.Message, childNodeId: "node-a"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-b", BpmnEventDefinitionTypes.Signal, childNodeId: "node-b"),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "gw"),
                BpmnRuntimeFixture.Flow("flow-2", "gw", "catch-a"),
                BpmnRuntimeFixture.Flow("flow-3", "gw", "catch-b"),
                // catch-a also receives a second inbound flow, so it is not a pure race member.
                BpmnRuntimeFixture.Flow("flow-4", "catch-b", "catch-a"),
                BpmnRuntimeFixture.Flow("flow-5", "catch-a", "end")
            ]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("exactly one inbound flow", exception.Message);
    }

    [Fact]
    public void EventBasedGateway_WithConditionalOutboundFlow_IsRejected()
    {
        var node = NewExecutableNode(
            children: [WorkflowChild("node-a"), WorkflowChild("node-b")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.EventBasedGateway("gw"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-a", BpmnEventDefinitionTypes.Message, childNodeId: "node-a"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-b", BpmnEventDefinitionTypes.Signal, childNodeId: "node-b"),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "gw"),
                BpmnRuntimeFixture.Flow("flow-2", "gw", "catch-a", conditionOutcome: "Done"),
                BpmnRuntimeFixture.Flow("flow-3", "gw", "catch-b"),
                BpmnRuntimeFixture.Flow("flow-4", "catch-a", "end"),
                BpmnRuntimeFixture.Flow("flow-5", "catch-b", "end")
            ]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("cannot carry a condition", exception.Message);
    }

    [Fact]
    public void EventBasedGateway_BindingChild_IsRejected()
    {
        var node = NewExecutableNode(
            children: [WorkflowChild("node-gw"), WorkflowChild("node-a"), WorkflowChild("node-b")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                new BpmnElement("gw", BpmnElementTypes.EventBasedGateway, childNodeId: "node-gw"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-a", BpmnEventDefinitionTypes.Message, childNodeId: "node-a"),
                BpmnRuntimeFixture.IntermediateCatchEvent("catch-b", BpmnEventDefinitionTypes.Signal, childNodeId: "node-b"),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "gw"),
                BpmnRuntimeFixture.Flow("flow-2", "gw", "catch-a"),
                BpmnRuntimeFixture.Flow("flow-3", "gw", "catch-b"),
                BpmnRuntimeFixture.Flow("flow-4", "catch-a", "end"),
                BpmnRuntimeFixture.Flow("flow-5", "catch-b", "end")
            ]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("cannot bind a child activity", exception.Message);
    }

    private static ExecutableNode NewCatchEventNode(BpmnElement catchElement) =>
        NewExecutableNode(
            elements: [BpmnRuntimeFixture.StartEvent(), catchElement, BpmnRuntimeFixture.EndEvent()],
            flows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "catch-1"),
                BpmnRuntimeFixture.Flow("flow-2", "catch-1", "end")
            ],
            children: catchElement.ChildNodeId is null ? [] : [WorkflowChild(catchElement.ChildNodeId)]);

    private static ExecutableNode WorkflowChild(string nodeId) =>
        Elsa.Activities.Testing.WorkflowExecutionHarness.NewProbeNode(nodeId);

    private static ExecutableNode NewExecutableNode(
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> flows,
        IReadOnlyCollection<ExecutableNode>? children = null) =>
        new(
            executableNodeId: "node-bpmn",
            authoredActivityId: "authored-bpmn",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "BpmnDescriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, children ?? [])],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnStructure(elements, flows))));
}

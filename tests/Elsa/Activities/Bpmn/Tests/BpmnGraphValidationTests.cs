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

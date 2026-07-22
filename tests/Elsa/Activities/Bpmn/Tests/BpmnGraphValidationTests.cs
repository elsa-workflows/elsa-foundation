using System.Text.Json;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
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

    [Fact]
    public void BoundaryEvent_AttachedToMissingHost_IsRejected()
    {
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(NewBoundaryNode(
            BpmnRuntimeFixture.BoundaryEvent("boundary", "ghost", BpmnEventDefinitionTypes.Timer, childNodeId: "node-listener"),
            hostChildNodeId: "node-host")));
        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public void BoundaryEvent_AttachedToNonHostFamily_IsRejected()
    {
        // Attach to the start event, which is not a task-family/subprocess host.
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(NewBoundaryNode(
            BpmnRuntimeFixture.BoundaryEvent("boundary", "start", BpmnEventDefinitionTypes.Timer, childNodeId: "node-listener"),
            hostChildNodeId: "node-host")));
        Assert.Contains("not a task-family or subprocess host", exception.Message);
    }

    [Fact]
    public void BoundaryEvent_AttachedToChildlessHost_IsRejected()
    {
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(NewBoundaryNode(
            BpmnRuntimeFixture.BoundaryEvent("boundary", "host", BpmnEventDefinitionTypes.Timer, childNodeId: "node-listener"),
            hostChildNodeId: null)));
        Assert.Contains("no bound child activity", exception.Message);
    }

    [Fact]
    public void BoundaryEvent_WithInboundFlow_IsRejected()
    {
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(NewBoundaryNode(
            BpmnRuntimeFixture.BoundaryEvent("boundary", "host", BpmnEventDefinitionTypes.Timer, childNodeId: "node-listener"),
            hostChildNodeId: "node-host",
            extraFlows: [BpmnRuntimeFixture.Flow("flow-bad", "host", "boundary")])));
        Assert.Contains("cannot have inbound sequence flows", exception.Message);
    }

    [Fact]
    public void BoundaryEvent_WithoutOutboundFlow_IsRejected()
    {
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(NewBoundaryNode(
            BpmnRuntimeFixture.BoundaryEvent("boundary", "host", BpmnEventDefinitionTypes.Timer, childNodeId: "node-listener"),
            hostChildNodeId: "node-host",
            boundaryOutbound: false)));
        Assert.Contains("at least one outbound sequence flow", exception.Message);
    }

    [Fact]
    public void CatchBoundaryEvent_WithoutChild_IsRejected()
    {
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(NewBoundaryNode(
            BpmnRuntimeFixture.BoundaryEvent("boundary", "host", BpmnEventDefinitionTypes.Message),
            hostChildNodeId: "node-host")));
        Assert.Contains("requires a bound suspending listener child", exception.Message);
    }

    [Fact]
    public void ErrorBoundaryEvent_WithChild_IsRejected()
    {
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(NewBoundaryNode(
            BpmnRuntimeFixture.BoundaryEvent("boundary", "host", BpmnEventDefinitionTypes.Error, childNodeId: "node-listener"),
            hostChildNodeId: "node-host")));
        Assert.Contains("cannot bind a child activity", exception.Message);
    }

    [Fact]
    public void NonInterruptingErrorBoundaryEvent_IsRejected()
    {
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(NewBoundaryNode(
            BpmnRuntimeFixture.BoundaryEvent("boundary", "host", BpmnEventDefinitionTypes.Error, cancelActivity: false),
            hostChildNodeId: "node-host")));
        Assert.Contains("must be interrupting", exception.Message);
    }

    [Fact]
    public void MultipleErrorBoundaryEvents_OnOneHost_AreRejected()
    {
        var node = NewExecutableNode(
            children: [WorkflowChild("node-host")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                new BpmnElement("host", BpmnElementTypes.Task, childNodeId: "node-host"),
                BpmnRuntimeFixture.BoundaryEvent("err-1", "host", BpmnEventDefinitionTypes.Error),
                BpmnRuntimeFixture.BoundaryEvent("err-2", "host", BpmnEventDefinitionTypes.Error),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "host"),
                BpmnRuntimeFixture.Flow("flow-2", "host", "end"),
                BpmnRuntimeFixture.Flow("flow-3", "err-1", "end"),
                BpmnRuntimeFixture.Flow("flow-4", "err-2", "end")
            ]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("more than one error boundary", exception.Message);
    }

    [Fact]
    public void BoundaryEvent_WithDefaultFlow_IsRejected()
    {
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(NewBoundaryNode(
            new BpmnElement("boundary", BpmnElementTypes.BoundaryEvent, childNodeId: "node-listener", defaultFlowId: "flow-boundary",
                eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Timer)], attachedToRef: "host"),
            hostChildNodeId: "node-host")));
        Assert.Contains("cannot declare a default sequence flow", exception.Message);
    }

    [Fact]
    public void ValidTimerBoundaryEvent_IsAccepted()
    {
        var graph = BpmnGraph.From(NewBoundaryNode(
            BpmnRuntimeFixture.BoundaryEvent("boundary", "host", BpmnEventDefinitionTypes.Timer, childNodeId: "node-listener"),
            hostChildNodeId: "node-host"));
        Assert.Single(graph.AttachedCatchBoundaries("host"));
        Assert.Null(graph.AttachedErrorBoundary("host"));
    }

    /// <summary>Builds a start → host(task) → end graph with one attached boundary and its outbound flow, for boundary validation tests.</summary>
    private static ExecutableNode NewBoundaryNode(
        BpmnElement boundary,
        string? hostChildNodeId,
        bool boundaryOutbound = true,
        IReadOnlyCollection<BpmnSequenceFlow>? extraFlows = null)
    {
        var children = new List<ExecutableNode>();
        if (hostChildNodeId is not null) children.Add(WorkflowChild(hostChildNodeId));
        if (boundary.ChildNodeId is not null) children.Add(WorkflowChild(boundary.ChildNodeId));

        var flows = new List<BpmnSequenceFlow>
        {
            BpmnRuntimeFixture.Flow("flow-1", "start", "host"),
            BpmnRuntimeFixture.Flow("flow-2", "host", "end")
        };
        if (boundaryOutbound) flows.Add(BpmnRuntimeFixture.Flow("flow-boundary", "boundary", "end"));
        if (extraFlows is not null) flows.AddRange(extraFlows);

        return NewExecutableNode(
            children: children,
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                new BpmnElement("host", BpmnElementTypes.Task, childNodeId: hostChildNodeId),
                boundary,
                BpmnRuntimeFixture.EndEvent()
            ],
            flows: flows);
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

    // ---- spec 121: multi-instance loop-characteristics validation ----

    [Fact]
    public void MultiInstance_OnChildlessHost_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                new BpmnElement("mi", BpmnElementTypes.Task, loopCharacteristics: new BpmnLoopCharacteristics(cardinality: 3)),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows: [BpmnRuntimeFixture.Flow("flow-1", "start", "mi"), BpmnRuntimeFixture.Flow("flow-2", "mi", "end")]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("not a task-family or subprocess host with a bound child", exception.Message);
    }

    [Fact]
    public void MultiInstance_OnGateway_IsRejected()
    {
        var node = NewExecutableNode(
            children: [WorkflowChild("node-a")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                new BpmnElement("fork", BpmnElementTypes.ParallelGateway, loopCharacteristics: new BpmnLoopCharacteristics(cardinality: 2)),
                BpmnRuntimeFixture.Task("t", childNodeId: "node-a"),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows: [BpmnRuntimeFixture.Flow("flow-1", "start", "fork"), BpmnRuntimeFixture.Flow("flow-2", "fork", "t"), BpmnRuntimeFixture.Flow("flow-3", "t", "end")]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("not a task-family or subprocess host", exception.Message);
    }

    [Fact]
    public void MultiInstance_BothCardinalityAndCollection_IsRejected()
    {
        var node = NewMultiInstanceNode(new BpmnLoopCharacteristics(cardinality: 3, collectionVariable: "items"));

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("exactly one of a positive cardinality or a collection variable", exception.Message);
    }

    [Fact]
    public void MultiInstance_NeitherCardinalityNorCollection_IsRejected()
    {
        var node = NewMultiInstanceNode(new BpmnLoopCharacteristics(isSequential: true));

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("exactly one of a positive cardinality or a collection variable", exception.Message);
    }

    [Fact]
    public void MultiInstance_NonPositiveCardinality_IsRejected()
    {
        var node = NewMultiInstanceNode(new BpmnLoopCharacteristics(cardinality: 0));

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("must be a positive integer", exception.Message);
    }

    [Fact]
    public void MultiInstance_CollectionMode_UndeclaredVariable_IsRejected()
    {
        var node = NewMultiInstanceNode(new BpmnLoopCharacteristics(collectionVariable: "missing"));

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("not a declared container-scoped variable", exception.Message);
    }

    [Fact]
    public void MultiInstance_CollectionMode_DeclaredVariable_IsRejectedAsStatedCut()
    {
        var node = NewMultiInstanceNode(
            new BpmnLoopCharacteristics(collectionVariable: "items"),
            variables: [new VariableDefinition("items", "items", new TypeReference("String", CollectionKind.List), null, null)]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("collection mode, which is not executable in this engine slice", exception.Message);
    }

    [Fact]
    public void MultiInstance_CardinalityHost_IsAccepted()
    {
        var node = NewMultiInstanceNode(new BpmnLoopCharacteristics(isSequential: false, cardinality: 3));

        var graph = BpmnGraph.From(node);
        Assert.NotNull(graph.GetRequiredElement("mi").LoopCharacteristics);
    }

    private static ExecutableNode NewMultiInstanceNode(BpmnLoopCharacteristics loop, IReadOnlyCollection<VariableDefinition>? variables = null) =>
        new(
            executableNodeId: "node-bpmn",
            authoredActivityId: "authored-bpmn",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "BpmnDescriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, [WorkflowChild("node-a")])],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnStructure(
                    elements:
                    [
                        BpmnRuntimeFixture.StartEvent(),
                        new BpmnElement("mi", BpmnElementTypes.Task, childNodeId: "node-a", loopCharacteristics: loop),
                        BpmnRuntimeFixture.EndEvent()
                    ],
                    sequenceFlows: [BpmnRuntimeFixture.Flow("flow-1", "start", "mi"), BpmnRuntimeFixture.Flow("flow-2", "mi", "end")],
                    variables: variables))));

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

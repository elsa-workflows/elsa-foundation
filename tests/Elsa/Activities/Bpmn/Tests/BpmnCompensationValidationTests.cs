using System.Text.Json;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// Spec 124 D1 validation: a valid compensation graph (boundary + handler + throw) builds cleanly, and every
/// compensation rule rejects deterministically with the offending element named — compensation-boundary shape
/// (existing handler, no child, zero outbound, non-multi-instance host), handler shape (task-family/subprocess,
/// binds a child, zero flows, no loop characteristics, hosts no boundary, referenced by exactly one boundary,
/// no orphans), throw shape (single compensate definition, no child), and activityRef resolution.
/// </summary>
public sealed class BpmnCompensationValidationTests
{
    private static BpmnElement CompHandler(string id, string childNodeId = "node-h") =>
        BpmnRuntimeFixture.CompensationHandler(id, childNodeId);

    private static readonly BpmnSequenceFlow[] MinimalFlows =
    [
        BpmnRuntimeFixture.Flow("flow-1", "start", "host"),
        BpmnRuntimeFixture.Flow("flow-2", "host", "throw"),
        BpmnRuntimeFixture.Flow("flow-3", "throw", "end")
    ];

    private static BpmnElement[] MinimalElements(
        BpmnElement? boundary = null, BpmnElement? handler = null, BpmnElement? throwEvent = null, BpmnElement? host = null) =>
    [
        BpmnRuntimeFixture.StartEvent(),
        host ?? BpmnRuntimeFixture.Task("host", childNodeId: "node-host"),
        boundary ?? BpmnRuntimeFixture.CompensationBoundary("cb", "host", "handler"),
        handler ?? CompHandler("handler"),
        throwEvent ?? BpmnRuntimeFixture.CompensateThrow("throw"),
        BpmnRuntimeFixture.EndEvent()
    ];

    [Fact]
    public void ValidCompensationGraph_BuildsCleanly()
    {
        var node = NewNode(MinimalElements(), MinimalFlows, ["node-host", "node-h"]);
        var graph = BpmnGraph.From(node);

        Assert.NotNull(graph.AttachedCompensationBoundary("host"));
        Assert.Empty(graph.AttachedCatchBoundaries("host"));
    }

    [Fact]
    public void CompensationBoundary_ReferencingMissingHandler_IsRejected() =>
        AssertRejects(
            MinimalElements(boundary: BpmnRuntimeFixture.CompensationBoundary("cb", "host", "ghost"), handler: CompHandler("handler")),
            "does not exist");

    [Fact]
    public void CompensationBoundary_ReferencingNonHandler_IsRejected()
    {
        // The reference points at an ordinary task, not an isForCompensation element.
        var elements = new List<BpmnElement>(MinimalElements(boundary: BpmnRuntimeFixture.CompensationBoundary("cb", "host", "plain")))
        {
            BpmnRuntimeFixture.Task("plain", childNodeId: "node-plain")
        };
        AssertRejects(elements.ToArray(), "not a compensation handler", ["node-host", "node-h", "node-plain"]);
    }

    [Fact]
    public void CompensationBoundary_WithOutboundFlow_IsRejected()
    {
        var flows = MinimalFlows.Append(BpmnRuntimeFixture.Flow("flow-x", "cb", "end")).ToArray();
        AssertRejects(MinimalElements(), "cannot have outbound sequence flows", flows: flows);
    }

    [Fact]
    public void CompensationBoundary_BindingChild_IsRejected() =>
        AssertRejects(
            MinimalElements(boundary: new BpmnElement("cb", BpmnElementTypes.BoundaryEvent, childNodeId: "node-x",
                eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Compensation)], attachedToRef: "host", compensationHandlerElementId: "handler")),
            "cannot bind a child",
            children: ["node-host", "node-h", "node-x"]);

    [Fact]
    public void CompensationBoundary_OnMultiInstanceHost_IsRejected() =>
        AssertRejects(
            MinimalElements(host: BpmnRuntimeFixture.MultiInstanceTask("host", "node-host", cardinality: 2, isSequential: true)),
            "multi-instance host");

    [Fact]
    public void Handler_NotTaskFamilyOrSubprocess_IsRejected()
    {
        // A gateway marked isForCompensation is not a valid handler.
        var handler = new BpmnElement("handler", BpmnElementTypes.ExclusiveGateway, childNodeId: "node-h", isForCompensation: true);
        AssertRejects(MinimalElements(handler: handler), "must be a task-family or subprocess");
    }

    [Fact]
    public void Handler_WithoutChild_IsRejected() =>
        AssertRejects(
            MinimalElements(handler: new BpmnElement("handler", BpmnElementTypes.Task, isForCompensation: true)),
            "must bind a child",
            children: ["node-host"]);

    [Fact]
    public void Handler_WithSequenceFlow_IsRejected()
    {
        var flows = MinimalFlows.Append(BpmnRuntimeFixture.Flow("flow-x", "handler", "end")).ToArray();
        AssertRejects(MinimalElements(), "cannot participate in sequence flows", flows: flows);
    }

    [Fact]
    public void OrphanHandler_ReferencedByNoBoundary_IsRejected()
    {
        // A second isForCompensation element that no boundary references.
        var elements = new List<BpmnElement>(MinimalElements())
        {
            CompHandler("orphan", childNodeId: "node-orphan")
        };
        AssertRejects(elements.ToArray(), "referenced by no compensation boundary", ["node-host", "node-h", "node-orphan"]);
    }

    [Fact]
    public void Handler_HostingBoundary_IsRejected()
    {
        // An (otherwise structurally valid) error boundary attached to the handler element.
        var elements = new List<BpmnElement>(MinimalElements())
        {
            BpmnRuntimeFixture.BoundaryEvent("b-on-handler", "handler", BpmnEventDefinitionTypes.Error)
        };
        var flows = MinimalFlows.Append(BpmnRuntimeFixture.Flow("flow-b", "b-on-handler", "end")).ToArray();
        AssertRejects(elements.ToArray(), "cannot host attached boundary events", ["node-host", "node-h"], flows);
    }

    [Fact]
    public void ThrowEvent_WithNonCompensationDefinition_IsRejected() =>
        AssertRejects(
            MinimalElements(throwEvent: new BpmnElement("throw", BpmnElementTypes.IntermediateThrowEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Signal)])),
            "only compensate throw events are supported");

    [Fact]
    public void ThrowEvent_BindingChild_IsRejected() =>
        AssertRejects(
            MinimalElements(throwEvent: new BpmnElement("throw", BpmnElementTypes.IntermediateThrowEvent, childNodeId: "node-throw",
                eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Compensation)])),
            "cannot bind a child",
            children: ["node-host", "node-h", "node-throw"]);

    [Fact]
    public void ActivityRef_NamingUnknownElement_IsRejected() =>
        AssertRejects(
            MinimalElements(throwEvent: BpmnRuntimeFixture.CompensateThrow("throw", activityRef: "ghost")),
            "does not exist in this process");

    [Fact]
    public void ActivityRef_NamingHostWithoutCompensationBoundary_IsRejected() =>
        AssertRejects(
            MinimalElements(throwEvent: BpmnRuntimeFixture.CompensateThrow("throw", activityRef: "start")),
            "has no attached compensation boundary");

    [Fact]
    public void ActivityRef_NamingCompensationHost_ValidatesCleanly()
    {
        var node = NewNode(MinimalElements(throwEvent: BpmnRuntimeFixture.CompensateThrow("throw", activityRef: "host")), MinimalFlows, ["node-host", "node-h"]);
        Assert.NotNull(BpmnGraph.From(node));
    }

    private static void AssertRejects(IReadOnlyCollection<BpmnElement> elements, string expectedMessage, IReadOnlyCollection<string>? children = null, IReadOnlyCollection<BpmnSequenceFlow>? flows = null)
    {
        var node = NewNode(elements, flows ?? MinimalFlows, children ?? ["node-host", "node-h"]);
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    private static ExecutableNode NewNode(IReadOnlyCollection<BpmnElement> elements, IReadOnlyCollection<BpmnSequenceFlow> flows, IReadOnlyCollection<string> childNodeIds) =>
        new(
            executableNodeId: "node-bpmn",
            authoredActivityId: "authored-bpmn",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "BpmnDescriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, childNodeIds.Select(WorkflowChild).ToArray())],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnStructure(elements, flows))));

    private static ExecutableNode WorkflowChild(string nodeId) =>
        Elsa.Activities.Testing.WorkflowExecutionHarness.NewProbeNode(nodeId);
}

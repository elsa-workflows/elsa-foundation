using System.Text.Json;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// Escalation validation: a valid escalation graph (subprocess host + escalation boundary + escalation throw)
/// builds cleanly, and every escalation rule rejects deterministically with the offending element named — throw/end
/// code-required + no-child, escalation-boundary host-must-be-a-subprocess + no-child, and per-host distinct codes
/// with at most one code-less catch-all.
/// </summary>
public sealed class BpmnEscalationValidationTests
{
    private static BpmnElement[] MinimalElements(
        BpmnElement? boundary = null, BpmnElement? thrower = null, BpmnElement? host = null, params BpmnElement[] extra) =>
    [
        BpmnTestNodes.StartEvent(),
        host ?? BpmnTestNodes.SubProcess("sub", childNodeId: "node-sub"),
        boundary ?? BpmnTestNodes.EscalationBoundary("b-esc", "sub", "E1"),
        thrower ?? BpmnTestNodes.EscalationThrow("throw", "E1"),
        BpmnTestNodes.EndEvent("end"),
        BpmnTestNodes.EndEvent("bnd-end"),
        .. extra
    ];

    private static readonly BpmnSequenceFlow[] MinimalFlows =
    [
        BpmnTestNodes.Flow("flow-1", "start", "sub"),
        BpmnTestNodes.Flow("flow-2", "sub", "throw"),
        BpmnTestNodes.Flow("flow-3", "throw", "end"),
        BpmnTestNodes.Flow("flow-b", "b-esc", "bnd-end")
    ];

    [Fact]
    public void ValidEscalationGraph_BuildsCleanly()
    {
        var graph = BpmnGraphAdapter.Build(NewNode(MinimalElements(), MinimalFlows, ["node-sub"]));

        var boundary = Assert.Single(graph.AttachedEscalationBoundaries("sub"));
        Assert.Equal("b-esc", boundary.ElementId);
        Assert.Empty(graph.AttachedCatchBoundaries("sub"));
    }

    [Fact]
    public void EscalationThrow_WithoutCode_IsRejected() =>
        AssertRejects(
            MinimalElements(thrower: new BpmnElement("throw", BpmnElementTypes.IntermediateThrowEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation)])),
            "must declare a non-empty escalation code");

    [Fact]
    public void EscalationEnd_WithoutCode_IsRejected() =>
        // A code-less escalation end (no outbound, as an end event requires).
        AssertRejects(
            [
                BpmnTestNodes.StartEvent(),
                BpmnTestNodes.SubProcess("sub", childNodeId: "node-sub"),
                new BpmnElement("esc-end", BpmnElementTypes.EndEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation)])
            ],
            "must declare a non-empty escalation code",
            flows:
            [
                BpmnTestNodes.Flow("flow-1", "start", "sub"),
                BpmnTestNodes.Flow("flow-2", "sub", "esc-end")
            ]);

    [Fact]
    public void EscalationThrow_BindingChild_IsRejected() =>
        AssertRejects(
            MinimalElements(thrower: new BpmnElement("throw", BpmnElementTypes.IntermediateThrowEvent, childNodeId: "node-throw",
                eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = "E1" })])),
            "cannot bind work",
            children: ["node-sub", "node-throw"]);

    [Fact]
    public void EscalationBoundary_OnTaskHost_IsRejected() =>
        // A task host is dead by construction: its bound child is a leaf that can never escalate.
        AssertRejects(
            [
                BpmnTestNodes.StartEvent(),
                BpmnTestNodes.Task("task-a", childNodeId: "node-sub"),
                BpmnTestNodes.EscalationBoundary("b-esc", "task-a", "E1"),
                BpmnTestNodes.EndEvent("end"),
                BpmnTestNodes.EndEvent("bnd-end")
            ],
            "only attach to a subprocess or call activity host",
            flows:
            [
                BpmnTestNodes.Flow("flow-1", "start", "task-a"),
                BpmnTestNodes.Flow("flow-2", "task-a", "end"),
                BpmnTestNodes.Flow("flow-b", "b-esc", "bnd-end")
            ]);

    [Fact]
    public void EscalationBoundary_BindingChild_IsRejected() =>
        AssertRejects(
            MinimalElements(boundary: new BpmnElement("b-esc", BpmnElementTypes.BoundaryEvent, childNodeId: "node-listener",
                eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = "E1" })],
                attachedToRef: "sub")),
            "cannot bind work",
            children: ["node-sub", "node-listener"]);

    [Fact]
    public void EscalationBoundary_WithNoOutbound_IsRejected() =>
        AssertRejects(
            MinimalElements(),
            "must have at least one outbound",
            flows:
            [
                BpmnTestNodes.Flow("flow-1", "start", "sub"),
                BpmnTestNodes.Flow("flow-2", "sub", "throw"),
                BpmnTestNodes.Flow("flow-3", "throw", "end")
            ]);

    [Fact]
    public void EscalationBoundary_DuplicateCode_OnOneHost_IsRejected() =>
        AssertRejects(
            MinimalElements(extra: BpmnTestNodes.EscalationBoundary("b-esc-2", "sub", "E1")),
            "escalation boundary codes on one host must be distinct",
            flows: [.. MinimalFlows, BpmnTestNodes.Flow("flow-b2", "b-esc-2", "end")]);

    [Fact]
    public void EscalationBoundary_TwoCatchAlls_OnOneHost_IsRejected() =>
        AssertRejects(
            [
                BpmnTestNodes.StartEvent(),
                BpmnTestNodes.SubProcess("sub", childNodeId: "node-sub"),
                BpmnTestNodes.EscalationBoundary("b-all-1", "sub"),
                BpmnTestNodes.EscalationBoundary("b-all-2", "sub"),
                BpmnTestNodes.EndEvent("end"),
                BpmnTestNodes.EndEvent("bnd-1"),
                BpmnTestNodes.EndEvent("bnd-2")
            ],
            "at most one",
            flows:
            [
                BpmnTestNodes.Flow("flow-1", "start", "sub"),
                BpmnTestNodes.Flow("flow-2", "sub", "end"),
                BpmnTestNodes.Flow("flow-a1", "b-all-1", "bnd-1"),
                BpmnTestNodes.Flow("flow-a2", "b-all-2", "bnd-2")
            ]);

    [Fact]
    public void EscalationBoundary_CodedPlusCatchAll_OnOneHost_BuildsCleanly()
    {
        var graph = BpmnGraphAdapter.Build(NewNode(
            [
                BpmnTestNodes.StartEvent(),
                BpmnTestNodes.SubProcess("sub", childNodeId: "node-sub"),
                BpmnTestNodes.EscalationBoundary("b-coded", "sub", "E1"),
                BpmnTestNodes.EscalationBoundary("b-all", "sub"),
                BpmnTestNodes.EndEvent("end"),
                BpmnTestNodes.EndEvent("bnd-1"),
                BpmnTestNodes.EndEvent("bnd-2")
            ],
            [
                BpmnTestNodes.Flow("flow-1", "start", "sub"),
                BpmnTestNodes.Flow("flow-2", "sub", "end"),
                BpmnTestNodes.Flow("flow-c", "b-coded", "bnd-1"),
                BpmnTestNodes.Flow("flow-a", "b-all", "bnd-2")
            ],
            ["node-sub"]));

        Assert.Equal(2, graph.AttachedEscalationBoundaries("sub").Count);
    }

    private static void AssertRejects(IReadOnlyCollection<BpmnElement> elements, string expectedMessage, IReadOnlyCollection<string>? children = null, IReadOnlyCollection<BpmnSequenceFlow>? flows = null)
    {
        var node = NewNode(elements, flows ?? MinimalFlows, children ?? ["node-sub"]);
        var exception = Assert.Throws<BpmnHostException>(() => BpmnGraphAdapter.Build(node));
        Assert.True(exception.Message.Contains(expectedMessage, StringComparison.Ordinal), exception.ToString());
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
        BpmnTestNodes.Probe(nodeId);
}

using System.Text.Json;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// Spec 128 D1 validation: a valid event-subprocess graph (a flow-less <c>triggeredByEvent</c> subprocess whose
/// body declares exactly one escalation/error start) builds cleanly and indexes the catcher, and every rule rejects
/// deterministically — element-level shape (no flows, no loop, no boundary host, not a compensation handler, not a
/// transaction), body shape (exactly one start with one supported trigger, no nested event subprocess), the
/// error-must-be-interrupting rule, and per-scope distinct codes / single catch-all / single error.
/// </summary>
public sealed class BpmnEventSubprocessValidationTests
{
    /// <summary>A nested event-subprocess body node: one event-start (escalation/error) → probe → end.</summary>
    private static ExecutableNode Body(string nodeId, BpmnElement start) =>
        BpmnRuntimeFixture.NestedProcessNode(nodeId,
            elements: [start, BpmnRuntimeFixture.Task("es-task", childNodeId: $"{nodeId}-probe"), BpmnRuntimeFixture.EndEvent("es-end")],
            flows: [BpmnRuntimeFixture.Flow("es-f1", start.ElementId, "es-task"), BpmnRuntimeFixture.Flow("es-f2", "es-task", "es-end")],
            innerChildren: [WorkflowChild($"{nodeId}-probe")]);

    [Fact]
    public void ValidEscalationEventSubprocess_BuildsCleanly_AndIndexesCatcher()
    {
        var graph = BpmnGraph.From(Node(
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("work", childNodeId: "node-work"),
                BpmnRuntimeFixture.EndEvent("end"),
                BpmnRuntimeFixture.EventSubprocess("es", "node-body")
            ],
            [BpmnRuntimeFixture.Flow("f1", "start", "work"), BpmnRuntimeFixture.Flow("f2", "work", "end")],
            children: [WorkflowChild("node-work"), Body("node-body", BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start", "E1", interrupting: false))]));

        var catcher = Assert.Single(graph.EventSubprocesses);
        Assert.Equal("es", catcher.ElementId);
        Assert.Equal(BpmnEventSubprocessTriggerKind.Escalation, catcher.TriggerKind);
        Assert.Equal("E1", catcher.Code);
        Assert.False(catcher.Interrupting);
        Assert.Equal("es-start", catcher.BodyStartElementId);
        Assert.Same(catcher, graph.EscalationEventSubprocess("E1"));
    }

    [Fact]
    public void ErrorEventSubprocess_IsStatedCut_AndIsRejected() =>
        // spec 128 stated cut: an error-triggered event subprocess is not executable in this slice (it needs a runtime
        // deferred seam-B fault absorption). A follow-up unit removes this restriction.
        AssertRejects(
            DefaultElements(),
            DefaultFlows(),
            [WorkflowChild("node-work"), Body("node-body", BpmnRuntimeFixture.EventSubprocessErrorStart("es-start"))],
            "not executable in this slice");

    [Fact]
    public void EventSubprocess_WithSequenceFlow_IsRejected() =>
        AssertRejects(
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("work", childNodeId: "node-work"),
                BpmnRuntimeFixture.EndEvent("end"),
                BpmnRuntimeFixture.EventSubprocess("es", "node-body")
            ],
            [BpmnRuntimeFixture.Flow("f1", "start", "work"), BpmnRuntimeFixture.Flow("f2", "work", "end"), BpmnRuntimeFixture.Flow("f3", "work", "es")],
            [WorkflowChild("node-work"), Body("node-body", BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start", "E1"))],
            "cannot participate in sequence flows");

    [Fact]
    public void EventSubprocess_HostingBoundary_IsRejected() =>
        AssertRejects(
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("work", childNodeId: "node-work"),
                BpmnRuntimeFixture.EndEvent("end"),
                BpmnRuntimeFixture.EventSubprocess("es", "node-body"),
                BpmnRuntimeFixture.BoundaryEvent("b", "es", BpmnEventDefinitionTypes.Error),
                BpmnRuntimeFixture.EndEvent("b-end")
            ],
            [BpmnRuntimeFixture.Flow("f1", "start", "work"), BpmnRuntimeFixture.Flow("f2", "work", "end"), BpmnRuntimeFixture.Flow("f3", "b", "b-end")],
            [WorkflowChild("node-work"), Body("node-body", BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start", "E1"))],
            "cannot host attached boundary events");

    [Fact]
    public void EventSubprocess_BodyWithNoneStart_IsRejected() =>
        AssertRejects(
            DefaultElements(),
            DefaultFlows(),
            [WorkflowChild("node-work"), Body("node-body", BpmnRuntimeFixture.StartEvent("es-start"))],
            "must declare exactly one event definition of a supported trigger");

    [Fact]
    public void EventSubprocess_BodyWithTwoStarts_IsRejected()
    {
        var body = BpmnRuntimeFixture.NestedProcessNode("node-body",
            elements:
            [
                BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start-1", "E1"),
                BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start-2", "E2"),
                BpmnRuntimeFixture.Task("es-task", childNodeId: "node-body-probe"),
                BpmnRuntimeFixture.EndEvent("es-end")
            ],
            flows: [BpmnRuntimeFixture.Flow("es-f1", "es-start-1", "es-task"), BpmnRuntimeFixture.Flow("es-f2", "es-start-2", "es-task"), BpmnRuntimeFixture.Flow("es-f3", "es-task", "es-end")],
            innerChildren: [WorkflowChild("node-body-probe")]);

        AssertRejects(DefaultElements(), DefaultFlows(), [WorkflowChild("node-work"), body], "must declare exactly one start event");
    }

    [Fact]
    public void EventSubprocess_BodyWithUnsupportedTrigger_IsRejected() =>
        AssertRejects(
            DefaultElements(),
            DefaultFlows(),
            [WorkflowChild("node-work"), Body("node-body", BpmnRuntimeFixture.EventStart("es-start", BpmnEventDefinitionTypes.Message))],
            "unsupported trigger definition");

    [Fact]
    public void NonInterruptingErrorEventSubprocess_IsRejected() =>
        // Error is a stated cut this slice, so a non-interrupting error start is rejected by the stated-cut rule too.
        AssertRejects(
            DefaultElements(),
            DefaultFlows(),
            [WorkflowChild("node-work"), Body("node-body", BpmnRuntimeFixture.EventSubprocessErrorStart("es-start", interrupting: false))],
            "not executable in this slice");

    [Fact]
    public void EventSubprocess_NestedInsideBody_IsRejected()
    {
        var inner = Body("node-inner", BpmnRuntimeFixture.EventSubprocessEscalationStart("inner-start", "E-inner"));
        var body = BpmnRuntimeFixture.NestedProcessNode("node-body",
            elements:
            [
                BpmnRuntimeFixture.EventSubprocessEscalationStart("es-start", "E1"),
                BpmnRuntimeFixture.Task("es-task", childNodeId: "node-body-probe"),
                BpmnRuntimeFixture.EndEvent("es-end"),
                BpmnRuntimeFixture.EventSubprocess("nested-es", "node-inner")
            ],
            flows: [BpmnRuntimeFixture.Flow("es-f1", "es-start", "es-task"), BpmnRuntimeFixture.Flow("es-f2", "es-task", "es-end")],
            innerChildren: [WorkflowChild("node-body-probe"), inner]);

        AssertRejects(DefaultElements(), DefaultFlows(), [WorkflowChild("node-work"), body], "nested event subprocess");
    }

    [Fact]
    public void TwoEscalationEventSubprocesses_SameCode_IsRejected() =>
        AssertRejects(
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("work", childNodeId: "node-work"),
                BpmnRuntimeFixture.EndEvent("end"),
                BpmnRuntimeFixture.EventSubprocess("es-1", "node-body-1"),
                BpmnRuntimeFixture.EventSubprocess("es-2", "node-body-2")
            ],
            DefaultFlows(),
            [
                WorkflowChild("node-work"),
                Body("node-body-1", BpmnRuntimeFixture.EventSubprocessEscalationStart("es1-start", "E1")),
                Body("node-body-2", BpmnRuntimeFixture.EventSubprocessEscalationStart("es2-start", "E1"))
            ],
            "codes in one scope must be distinct");

    [Fact]
    public void ErrorEventSubprocess_EvenNonInterruptingWithEscalationSibling_IsRejected() =>
        // Even alongside a valid escalation event subprocess, an error trigger is rejected as the stated cut.
        AssertRejects(
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("work", childNodeId: "node-work"),
                BpmnRuntimeFixture.EndEvent("end"),
                BpmnRuntimeFixture.EventSubprocess("es-1", "node-body-1"),
                BpmnRuntimeFixture.EventSubprocess("es-2", "node-body-2")
            ],
            DefaultFlows(),
            [
                WorkflowChild("node-work"),
                Body("node-body-1", BpmnRuntimeFixture.EventSubprocessEscalationStart("es1-start", "E1")),
                Body("node-body-2", BpmnRuntimeFixture.EventSubprocessErrorStart("es2-start"))
            ],
            "not executable in this slice");

    private static BpmnElement[] DefaultElements() =>
    [
        BpmnRuntimeFixture.StartEvent(),
        BpmnRuntimeFixture.Task("work", childNodeId: "node-work"),
        BpmnRuntimeFixture.EndEvent("end"),
        BpmnRuntimeFixture.EventSubprocess("es", "node-body")
    ];

    private static BpmnSequenceFlow[] DefaultFlows() =>
    [
        BpmnRuntimeFixture.Flow("f1", "start", "work"),
        BpmnRuntimeFixture.Flow("f2", "work", "end")
    ];

    private static void AssertRejects(
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> flows,
        IReadOnlyCollection<ExecutableNode> children,
        string expectedMessage)
    {
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(Node(elements, flows, children)));
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    private static ExecutableNode Node(IReadOnlyCollection<BpmnElement> elements, IReadOnlyCollection<BpmnSequenceFlow> flows, IReadOnlyCollection<ExecutableNode> children) =>
        new(
            executableNodeId: BpmnRuntimeFixture.ProcessNodeId,
            authoredActivityId: "authored-bpmn",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "BpmnDescriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnStructure(elements, flows))));

    private static ExecutableNode WorkflowChild(string nodeId) =>
        Elsa.Activities.Testing.WorkflowExecutionHarness.NewProbeNode(nodeId);
}

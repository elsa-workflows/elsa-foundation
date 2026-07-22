using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

/// <summary>
/// Spec 120 D6 — interchange for boundary events: a timer/message/error boundary attached to a bound host
/// (an embedded subprocess) imports with a synthesized listener child (or, for error, none) and round-trips
/// through export→import with attachedToRef/cancelActivity fidelity. A boundary attached to a childless host
/// is dropped (validate-representable: the importer never emits a boundary the graph validator rejects).
/// </summary>
public sealed class BpmnBoundaryEventInterchangeTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BpmnDocumentImporter _importer = new();
    private readonly BpmnDocumentExporter _exporter = new();

    private const string HostedBoundaryDocument = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
          <message id="msg-ping" name="ping" />
          <process id="p" isExecutable="true">
            <startEvent id="start" />
            <subProcess id="host">
              <startEvent id="s2" />
              <task id="t2" />
              <endEvent id="e2" />
              <sequenceFlow id="sf1" sourceRef="s2" targetRef="t2" />
              <sequenceFlow id="sf2" sourceRef="t2" targetRef="e2" />
            </subProcess>
            <boundaryEvent id="b-timer" attachedToRef="host">
              <timerEventDefinition><timeDuration>PT5M</timeDuration></timerEventDefinition>
            </boundaryEvent>
            <boundaryEvent id="b-error" attachedToRef="host">
              <errorEventDefinition />
            </boundaryEvent>
            <boundaryEvent id="b-msg" attachedToRef="host" cancelActivity="false">
              <messageEventDefinition messageRef="msg-ping" />
            </boundaryEvent>
            <endEvent id="end" />
            <endEvent id="end-t" />
            <endEvent id="end-e" />
            <endEvent id="end-m" />
            <sequenceFlow id="f1" sourceRef="start" targetRef="host" />
            <sequenceFlow id="f2" sourceRef="host" targetRef="end" />
            <sequenceFlow id="f3" sourceRef="b-timer" targetRef="end-t" />
            <sequenceFlow id="f4" sourceRef="b-error" targetRef="end-e" />
            <sequenceFlow id="f5" sourceRef="b-msg" targetRef="end-m" />
          </process>
        </definitions>
        """;

    [Fact]
    public void Import_ResolvesBoundaryKinds_WithListenerChildren_AndValidates()
    {
        var structure = _importer.Import(HostedBoundaryDocument).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        AssertTimerBoundary(structure);
        var error = structure.Elements.Single(element => element.ElementId == "b-error");
        Assert.Equal(BpmnElementTypes.BoundaryEvent, error.ElementType);
        Assert.Null(error.ChildNodeId);
        Assert.True(error.CancelActivity);
        Assert.Equal(BpmnEventDefinitionTypes.Error, error.EventDefinitions.Single().Type);

        var message = structure.Elements.Single(element => element.ElementId == "b-msg");
        Assert.False(message.CancelActivity);
        Assert.Equal("node-b-msg", message.ChildNodeId);
        Assert.Equal("ping", message.EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Name]);
        Assert.Contains(structure.Activities, child => child.NodeId == "node-b-msg" && child.ActivityVersionId == "Elsa.Event");

        // Validate-representable: the imported structure satisfies the frozen graph rules.
        var graph = BpmnGraph.From(BridgeToExecutableNode(structure));
        Assert.Equal(2, graph.AttachedCatchBoundaries("host").Count);
        Assert.NotNull(graph.AttachedErrorBoundary("host"));
    }

    [Fact]
    public void BoundaryEvents_RoundTripThroughExportImport_WithFidelity()
    {
        var imported = _importer.Import(HostedBoundaryDocument).ProcessNode;

        var xml = _exporter.Export(imported);
        Assert.Contains("<boundaryEvent", xml, StringComparison.Ordinal);
        Assert.Contains("attachedToRef=\"host\"", xml, StringComparison.Ordinal);
        Assert.Contains("cancelActivity=\"false\"", xml, StringComparison.Ordinal);
        Assert.Contains("<errorEventDefinition", xml, StringComparison.Ordinal);

        var reimported = _importer.Import(xml).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        AssertTimerBoundary(reimported);

        var error = reimported.Elements.Single(element => element.ElementId == "b-error");
        Assert.Equal(BpmnEventDefinitionTypes.Error, error.EventDefinitions.Single().Type);
        Assert.True(error.CancelActivity);

        var message = reimported.Elements.Single(element => element.ElementId == "b-msg");
        Assert.False(message.CancelActivity);
        Assert.Equal("host", message.AttachedToRef);

        // The round-tripped structure still validates.
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(reimported)));
    }

    [Fact]
    public void BoundaryEvent_AttachedToChildlessHost_IsDropped()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <task id="task-a" />
                <boundaryEvent id="b-timer" attachedToRef="task-a">
                  <timerEventDefinition><timeDuration>PT5M</timeDuration></timerEventDefinition>
                </boundaryEvent>
                <endEvent id="end" />
                <endEvent id="end-t" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="task-a" />
                <sequenceFlow id="f2" sourceRef="task-a" targetRef="end" />
                <sequenceFlow id="f3" sourceRef="b-timer" targetRef="end-t" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "b-timer");
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "b-timer" && issue.Message.Contains("no bound child", StringComparison.Ordinal));
    }

    private static void AssertTimerBoundary(BpmnAuthoredStructure structure)
    {
        var timer = structure.Elements.Single(element => element.ElementId == "b-timer");
        Assert.Equal(BpmnElementTypes.BoundaryEvent, timer.ElementType);
        Assert.Equal("host", timer.AttachedToRef);
        Assert.True(timer.CancelActivity);
        Assert.Equal("node-b-timer", timer.ChildNodeId);
        Assert.Equal(BpmnEventDefinitionTypes.Timer, timer.EventDefinitions.Single().Type);
        Assert.Equal("PT5M", timer.EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Interval]);
        Assert.Contains(structure.Activities, child => child.NodeId == "node-b-timer" && child.ActivityVersionId == "Elsa.Delay");
    }

    private static ExecutableNode BridgeToExecutableNode(BpmnAuthoredStructure structure)
    {
        var children = structure.Activities
            .Select(activity => new ExecutableNode(
                executableNodeId: activity.NodeId,
                authoredActivityId: $"authored-{activity.NodeId}",
                activityType: activity.ActivityVersionId,
                activityTypeVersion: "1.0.0",
                descriptorType: "test",
                descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: new Dictionary<string, string>()))
            .ToArray();

        return new ExecutableNode(
            executableNodeId: "node-p",
            authoredActivityId: "authored-bpmn",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnStructure(structure.Elements, structure.SequenceFlows))));
    }
}

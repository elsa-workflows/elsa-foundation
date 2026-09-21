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
/// Spec 124 D4 — interchange for compensation. A compensation boundary + isForCompensation handler resolved via
/// an association, a compensate throw (± activityRef), and a compensate end round-trip import→export→import; a
/// boundary with no association, an orphan isForCompensation activity, and an unresolvable activityRef degrade or
/// drop with specific findings; and the importer never emits a graph the validator rejects.
/// </summary>
public sealed class BpmnCompensationInterchangeTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BpmnDocumentImporter _importer = new();
    private readonly BpmnDocumentExporter _exporter = new();

    private const string CompensationDocument = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
          <process id="p" isExecutable="true">
            <startEvent id="start" />
            <subProcess id="host">
              <startEvent id="hs" /><endEvent id="he" />
              <sequenceFlow id="hf" sourceRef="hs" targetRef="he" />
            </subProcess>
            <subProcess id="handler" isForCompensation="true">
              <startEvent id="cs" /><endEvent id="ce" />
              <sequenceFlow id="cf" sourceRef="cs" targetRef="ce" />
            </subProcess>
            <boundaryEvent id="cb" attachedToRef="host">
              <compensateEventDefinition />
            </boundaryEvent>
            <association id="a1" associationDirection="One" sourceRef="cb" targetRef="handler" />
            <intermediateThrowEvent id="throw">
              <compensateEventDefinition activityRef="host" />
            </intermediateThrowEvent>
            <endEvent id="end" />
            <sequenceFlow id="f1" sourceRef="start" targetRef="host" />
            <sequenceFlow id="f2" sourceRef="host" targetRef="throw" />
            <sequenceFlow id="f3" sourceRef="throw" targetRef="end" />
          </process>
        </definitions>
        """;

    [Fact]
    public void Import_ResolvesBoundaryHandlerAndThrow_AndValidates()
    {
        var structure = _importer.Import(CompensationDocument).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        var boundary = structure.Elements.Single(element => element.ElementId == "cb");
        Assert.Equal(BpmnEventDefinitionTypes.Compensation, boundary.EventDefinitions.Single().Type);
        Assert.Equal("handler", boundary.CompensationHandlerElementId);
        Assert.Equal("host", boundary.AttachedToRef);

        var handler = structure.Elements.Single(element => element.ElementId == "handler");
        Assert.True(handler.IsForCompensation);
        Assert.NotNull(handler.ChildNodeId);

        var throwEvent = structure.Elements.Single(element => element.ElementId == "throw");
        Assert.Equal(BpmnElementTypes.IntermediateThrowEvent, throwEvent.ElementType);
        Assert.Equal("host", throwEvent.EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.ActivityRef]);

        // Validate-representable: the imported structure satisfies the frozen graph rules.
        var graph = BpmnGraph.From(BridgeToExecutableNode(structure));
        Assert.NotNull(graph.AttachedCompensationBoundary("host"));
    }

    [Fact]
    public void Compensation_RoundTripsThroughExportImport_WithFidelity()
    {
        var imported = _importer.Import(CompensationDocument).ProcessNode;

        var xml = _exporter.Export(imported);
        Assert.Contains("<compensateEventDefinition", xml, StringComparison.Ordinal);
        Assert.Contains("isForCompensation=\"true\"", xml, StringComparison.Ordinal);
        Assert.Contains("<association", xml, StringComparison.Ordinal);
        Assert.Contains("<intermediateThrowEvent", xml, StringComparison.Ordinal);
        Assert.Contains("activityRef=\"host\"", xml, StringComparison.Ordinal);

        var reimported = _importer.Import(xml).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        Assert.Equal("handler", reimported.Elements.Single(element => element.ElementId == "cb").CompensationHandlerElementId);
        Assert.True(reimported.Elements.Single(element => element.ElementId == "handler").IsForCompensation);
        Assert.Equal("host", reimported.Elements.Single(element => element.ElementId == "throw").EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.ActivityRef]);

        // The round-tripped structure still validates.
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(reimported)));
    }

    [Fact]
    public void CompensateEnd_RoundTrips()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <subProcess id="host">
                  <startEvent id="hs" /><endEvent id="he" />
                  <sequenceFlow id="hf" sourceRef="hs" targetRef="he" />
                </subProcess>
                <subProcess id="handler" isForCompensation="true">
                  <startEvent id="cs" /><endEvent id="ce" />
                  <sequenceFlow id="cf" sourceRef="cs" targetRef="ce" />
                </subProcess>
                <boundaryEvent id="cb" attachedToRef="host"><compensateEventDefinition /></boundaryEvent>
                <association id="a1" sourceRef="handler" targetRef="cb" />
                <endEvent id="end"><compensateEventDefinition /></endEvent>
                <sequenceFlow id="f1" sourceRef="start" targetRef="host" />
                <sequenceFlow id="f2" sourceRef="host" targetRef="end" />
              </process>
            </definitions>
            """;

        var imported = _importer.Import(document).ProcessNode;
        var reimported = _importer.Import(_exporter.Export(imported)).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        var end = reimported.Elements.Single(element => element.ElementId == "end");
        Assert.Equal(BpmnEventDefinitionTypes.Compensation, end.EventDefinitions.Single().Type);
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(reimported)));
    }

    [Fact]
    public void CompensationBoundary_WithoutAssociation_IsDropped()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <subProcess id="host">
                  <startEvent id="hs" /><endEvent id="he" />
                  <sequenceFlow id="hf" sourceRef="hs" targetRef="he" />
                </subProcess>
                <boundaryEvent id="cb" attachedToRef="host"><compensateEventDefinition /></boundaryEvent>
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="host" />
                <sequenceFlow id="f2" sourceRef="host" targetRef="end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "cb");
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "cb" && issue.Message.Contains("no association", StringComparison.Ordinal));
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    [Fact]
    public void OrphanIsForCompensationActivity_IsDropped()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <subProcess id="orphan" isForCompensation="true">
                  <startEvent id="os" /><endEvent id="oe" />
                  <sequenceFlow id="of" sourceRef="os" targetRef="oe" />
                </subProcess>
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "orphan");
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "orphan" && issue.Message.Contains("referenced by no compensation boundary", StringComparison.Ordinal));
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    [Fact]
    public void AssociationTarget_ParticipatingInSequenceFlows_IsNotAHandler_BoundaryDrops()
    {
        // A handler is flow-less by rule; an association target that rides sequence flows must stay an ordinary
        // flow element (never marked isForCompensation), and the boundary drops — the importer never emits a
        // graph the validator rejects.
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <subProcess id="host">
                  <startEvent id="hs" /><endEvent id="he" />
                  <sequenceFlow id="hf" sourceRef="hs" targetRef="he" />
                </subProcess>
                <subProcess id="flowed">
                  <startEvent id="cs" /><endEvent id="ce" />
                  <sequenceFlow id="cf" sourceRef="cs" targetRef="ce" />
                </subProcess>
                <boundaryEvent id="cb" attachedToRef="host">
                  <compensateEventDefinition />
                </boundaryEvent>
                <association id="assoc" sourceRef="cb" targetRef="flowed" />
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="host" />
                <sequenceFlow id="f2" sourceRef="host" targetRef="flowed" />
                <sequenceFlow id="f3" sourceRef="flowed" targetRef="end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "cb");
        var flowed = Assert.Single(structure.Elements, element => element.ElementId == "flowed");
        Assert.False(flowed.IsForCompensation); // stays an ordinary flow element.
        Assert.Contains(structure.SequenceFlows, flow => flow.FlowId == "f2");
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "cb" && issue.Message.Contains("flow-less", StringComparison.Ordinal));
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    [Fact]
    public void CompensateThrow_WithUnresolvableActivityRef_IsDropped_WithFlowCascade()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <intermediateThrowEvent id="throw"><compensateEventDefinition activityRef="ghost" /></intermediateThrowEvent>
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="throw" />
                <sequenceFlow id="f2" sourceRef="throw" targetRef="end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "throw");
        Assert.DoesNotContain(structure.SequenceFlows, flow => flow.FlowId == "f2"); // cascade-dropped with the throw.
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "throw");
    }

    [Fact]
    public void CompensateEnd_WithUnresolvableActivityRef_DegradesToNoneEnd()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <endEvent id="end"><compensateEventDefinition activityRef="ghost" /></endEvent>
                <sequenceFlow id="f1" sourceRef="start" targetRef="end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        var end = structure.Elements.Single(element => element.ElementId == "end");
        Assert.Empty(end.EventDefinitions); // degraded to a none end event.
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Degraded && issue.ElementId == "end");
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
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

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
/// Spec 127 D4 — interchange for escalation: a nested escalation throw + escalation end (resolving an
/// <c>escalationRef</c> to a root <c>&lt;escalation&gt;</c> declaration's code) and parent escalation boundaries
/// (interrupting + non-interrupting + code-less catch-all) attached to a subprocess host import cleanly and
/// round-trip through export→import with code/cancelActivity fidelity; ref-less throws Drop, ref-less ends degrade
/// to none ends, task-host boundaries and code collisions Drop (validate-representable).
/// </summary>
public sealed class BpmnEscalationInterchangeTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BpmnDocumentImporter _importer = new();
    private readonly BpmnDocumentExporter _exporter = new();

    private const string EscalationDocument = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
          <escalation id="esc-decl-1" name="Overload" escalationCode="E1" />
          <escalation id="esc-decl-2" escalationCode="E2" />
          <process id="p" isExecutable="true">
            <startEvent id="start" />
            <subProcess id="host">
              <startEvent id="s2" />
              <intermediateThrowEvent id="throw-esc">
                <escalationEventDefinition escalationRef="esc-decl-1" />
              </intermediateThrowEvent>
              <endEvent id="e2">
                <escalationEventDefinition escalationRef="esc-decl-1" />
              </endEvent>
              <sequenceFlow id="sf1" sourceRef="s2" targetRef="throw-esc" />
              <sequenceFlow id="sf2" sourceRef="throw-esc" targetRef="e2" />
            </subProcess>
            <boundaryEvent id="b-esc" attachedToRef="host">
              <escalationEventDefinition escalationRef="esc-decl-1" />
            </boundaryEvent>
            <boundaryEvent id="b-ni" attachedToRef="host" cancelActivity="false">
              <escalationEventDefinition escalationRef="esc-decl-2" />
            </boundaryEvent>
            <boundaryEvent id="b-all" attachedToRef="host">
              <escalationEventDefinition />
            </boundaryEvent>
            <endEvent id="end" />
            <endEvent id="end-b1" />
            <endEvent id="end-b2" />
            <endEvent id="end-b3" />
            <sequenceFlow id="f1" sourceRef="start" targetRef="host" />
            <sequenceFlow id="f2" sourceRef="host" targetRef="end" />
            <sequenceFlow id="f3" sourceRef="b-esc" targetRef="end-b1" />
            <sequenceFlow id="f4" sourceRef="b-ni" targetRef="end-b2" />
            <sequenceFlow id="f5" sourceRef="b-all" targetRef="end-b3" />
          </process>
        </definitions>
        """;

    [Fact]
    public void Import_ResolvesEscalationThrowEndAndBoundaries_AndValidates()
    {
        var structure = _importer.Import(EscalationDocument).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        AssertBoundaries(structure);

        // The nested subprocess's escalation throw + end carry the resolved code (and the declaration name on the throw).
        var nested = ReadNested(structure, "node-host");
        var throwEsc = nested.Elements.Single(element => element.ElementId == "throw-esc");
        Assert.Equal(BpmnEventDefinitionTypes.Escalation, throwEsc.EventDefinitions.Single().Type);
        Assert.Equal("E1", throwEsc.EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Code]);
        Assert.Equal("Overload", throwEsc.EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Name]);
        var end = nested.Elements.Single(element => element.ElementId == "e2");
        Assert.Equal("E1", end.EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Code]);

        // Validate-representable: the imported parent graph satisfies the frozen escalation rules.
        var graph = BpmnGraph.From(BridgeToExecutableNode(structure));
        Assert.Equal(3, graph.AttachedEscalationBoundaries("host").Count);
    }

    [Fact]
    public void EscalationEvents_RoundTripThroughExportImport_WithFidelity()
    {
        var imported = _importer.Import(EscalationDocument).ProcessNode;

        var xml = _exporter.Export(imported);
        Assert.Contains("<escalation ", xml, StringComparison.Ordinal);
        Assert.Contains("escalationCode=\"E1\"", xml, StringComparison.Ordinal);
        Assert.Contains("escalationRef=\"escalation-E1\"", xml, StringComparison.Ordinal);
        Assert.Contains("cancelActivity=\"false\"", xml, StringComparison.Ordinal);

        var reimported = _importer.Import(xml).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        AssertBoundaries(reimported);
        var nested = ReadNested(reimported, "node-host");
        Assert.Equal("E1", nested.Elements.Single(element => element.ElementId == "throw-esc").EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Code]);
        Assert.Equal("E1", nested.Elements.Single(element => element.ElementId == "e2").EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Code]);

        // The round-tripped structure still validates.
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(reimported)));
    }

    [Fact]
    public void RefLessThrow_IsDropped()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <intermediateThrowEvent id="t"><escalationEventDefinition /></intermediateThrowEvent>
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="t" />
                <sequenceFlow id="f2" sourceRef="t" targetRef="end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "t");
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "t" && issue.Message.Contains("no escalationRef", StringComparison.Ordinal));
    }

    [Fact]
    public void RefLessEnd_DegradesToNoneEnd()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <endEvent id="e"><escalationEventDefinition /></endEvent>
                <sequenceFlow id="f1" sourceRef="start" targetRef="e" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        var end = structure.Elements.Single(element => element.ElementId == "e");
        Assert.Empty(end.EventDefinitions); // degraded to a none end
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Degraded && issue.ElementId == "e");
    }

    [Fact]
    public void TaskHostBoundary_IsDropped()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <escalation id="esc-decl-1" escalationCode="E1" />
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <subProcess id="host"><startEvent id="s2" /><endEvent id="e2" /><sequenceFlow id="sf1" sourceRef="s2" targetRef="e2" /></subProcess>
                <boundaryEvent id="b-task" attachedToRef="host2"><escalationEventDefinition escalationRef="esc-decl-1" /></boundaryEvent>
                <userTask id="host2" />
                <endEvent id="end" />
                <endEvent id="end-b" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="host" />
                <sequenceFlow id="f2" sourceRef="host" targetRef="host2" />
                <sequenceFlow id="f3" sourceRef="host2" targetRef="end" />
                <sequenceFlow id="f4" sourceRef="b-task" targetRef="end-b" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        // A task host imported from XML is always childless (an Elsa activity binding is re-established later), so an
        // escalation boundary on it drops at the childless-host guard — the importer stays validate-representable.
        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "b-task");
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "b-task" && issue.Message.Contains("no bound child", StringComparison.Ordinal));
    }

    [Fact]
    public void CodeCollisionAndDuplicateCatchAll_AreDropped()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <escalation id="esc-decl-1" escalationCode="E1" />
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <subProcess id="host"><startEvent id="s2" /><endEvent id="e2" /><sequenceFlow id="sf1" sourceRef="s2" targetRef="e2" /></subProcess>
                <boundaryEvent id="b1" attachedToRef="host"><escalationEventDefinition escalationRef="esc-decl-1" /></boundaryEvent>
                <boundaryEvent id="b2" attachedToRef="host"><escalationEventDefinition escalationRef="esc-decl-1" /></boundaryEvent>
                <boundaryEvent id="b-all-1" attachedToRef="host"><escalationEventDefinition /></boundaryEvent>
                <boundaryEvent id="b-all-2" attachedToRef="host"><escalationEventDefinition /></boundaryEvent>
                <endEvent id="end" />
                <endEvent id="e-b1" /><endEvent id="e-b2" /><endEvent id="e-a1" /><endEvent id="e-a2" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="host" />
                <sequenceFlow id="f2" sourceRef="host" targetRef="end" />
                <sequenceFlow id="fb1" sourceRef="b1" targetRef="e-b1" />
                <sequenceFlow id="fb2" sourceRef="b2" targetRef="e-b2" />
                <sequenceFlow id="fa1" sourceRef="b-all-1" targetRef="e-a1" />
                <sequenceFlow id="fa2" sourceRef="b-all-2" targetRef="e-a2" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        // The first coded boundary and the first catch-all survive; the collisions drop.
        Assert.Contains(structure.Elements, element => element.ElementId == "b1");
        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "b2");
        Assert.Contains(structure.Elements, element => element.ElementId == "b-all-1");
        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "b-all-2");
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "b2");
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "b-all-2");

        // Validate-representable: the surviving graph builds.
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    private static void AssertBoundaries(BpmnAuthoredStructure structure)
    {
        var interrupting = structure.Elements.Single(element => element.ElementId == "b-esc");
        Assert.True(interrupting.CancelActivity);
        Assert.Equal("E1", interrupting.EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Code]);

        var nonInterrupting = structure.Elements.Single(element => element.ElementId == "b-ni");
        Assert.False(nonInterrupting.CancelActivity);
        Assert.Equal("E2", nonInterrupting.EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Code]);

        var catchAll = structure.Elements.Single(element => element.ElementId == "b-all");
        Assert.False(catchAll.EventDefinitions.Single().Properties.ContainsKey(BpmnEventDefinitionProperties.Code));
    }

    private static BpmnAuthoredStructure ReadNested(BpmnAuthoredStructure structure, string nodeId)
    {
        var nested = structure.Activities.Single(activity => activity.NodeId == nodeId);
        return nested.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
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

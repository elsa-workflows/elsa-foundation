using System.Text.Json;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

/// <summary>
/// Spec 119 D6 — interchange for the event-based gateway: the element round-trips through export→import
/// (the exporter's local-name fallback emits <c>&lt;eventBasedGateway&gt;</c> and the importer maps it back),
/// and the graph validation rules surface on the analyze/commit path when an imported gateway targets a
/// non-catch element.
/// </summary>
public sealed class BpmnEventBasedGatewayInterchangeTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BpmnDocumentImporter _importer = new();
    private readonly BpmnDocumentExporter _exporter = new();

    [Fact]
    public void EventBasedGateway_RoundTripsThroughExportImport()
    {
        var authored = new BpmnAuthoredStructure(
            activities:
            [
                new ActivityNode("node-a", "Elsa.Event", [new ArgumentState("EventName", new ArgumentValue("evt-a", "Literal"), null, null, null, null)], []),
                new ActivityNode("node-b", "Elsa.Event", [new ArgumentState("EventName", new ArgumentValue("evt-b", "Literal"), null, null, null, null)], [])
            ],
            elements:
            [
                new BpmnElement("start", BpmnElementTypes.StartEvent),
                new BpmnElement("gw", BpmnElementTypes.EventBasedGateway),
                new BpmnElement("catch-a", BpmnElementTypes.IntermediateCatchEvent, childNodeId: "node-a",
                    eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Message, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = "evt-a" })]),
                new BpmnElement("catch-b", BpmnElementTypes.IntermediateCatchEvent, childNodeId: "node-b",
                    eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Signal, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = "evt-b" })]),
                new BpmnElement("end", BpmnElementTypes.EndEvent)
            ],
            sequenceFlows:
            [
                new BpmnSequenceFlow("flow-1", "start", "gw"),
                new BpmnSequenceFlow("flow-2", "gw", "catch-a"),
                new BpmnSequenceFlow("flow-3", "gw", "catch-b"),
                new BpmnSequenceFlow("flow-4", "catch-a", "end"),
                new BpmnSequenceFlow("flow-5", "catch-b", "end")
            ]);

        var xml = _exporter.Export(ProcessNode(authored));
        Assert.Contains("<eventBasedGateway", xml, StringComparison.Ordinal);

        var reimported = _importer.Import(xml).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        var gateway = reimported.Elements.Single(element => element.ElementId == "gw");
        Assert.Equal(BpmnElementTypes.EventBasedGateway, gateway.ElementType);
        Assert.Equal(2, reimported.SequenceFlows.Count(flow => flow.SourceRef == "gw"));

        // The round-tripped structure still satisfies the frozen graph rules.
        var graph = BpmnGraph.From(BridgeToExecutableNode(reimported));
        Assert.Equal(BpmnElementFamilies.EventBasedGateway, BpmnElementFamilies.Resolve(gateway));
        Assert.NotNull(graph);
    }

    [Fact]
    public void ImportedEventBasedGateway_TargetingNonCatch_RejectsOnGraphValidation()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <eventBasedGateway id="gw" />
                <task id="task-a" />
                <task id="task-b" />
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="gw" />
                <sequenceFlow id="f2" sourceRef="gw" targetRef="task-a" />
                <sequenceFlow id="f3" sourceRef="gw" targetRef="task-b" />
                <sequenceFlow id="f4" sourceRef="task-a" targetRef="end" />
                <sequenceFlow id="f5" sourceRef="task-b" targetRef="end" />
              </process>
            </definitions>
            """;

        var structure = _importer.Import(document).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        var gateway = structure.Elements.Single(element => element.ElementId == "gw");
        Assert.Equal(BpmnElementTypes.EventBasedGateway, gateway.ElementType);

        // Graph validation runs on the analyze/commit path via BpmnGraph.From; the non-catch targets are rejected deterministically.
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(BridgeToExecutableNode(structure)));
        Assert.Contains("must target an intermediate catch event", exception.Message);
    }

    private static ActivityNode ProcessNode(BpmnAuthoredStructure structure) =>
        new(
            "node-bpmn",
            "Elsa.BpmnProcess",
            [],
            [],
            new ActivityNodeStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions)));

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

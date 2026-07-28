using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

public sealed class BpmnRoundTripTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BpmnDocumentImporter _importer = new();
    private readonly BpmnDocumentExporter _exporter = new();

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

    [Fact]
    public void ExportThenImport_RoundTripsElementsFlowsAndConditions()
    {
        var authored = new BpmnAuthoredStructure(
            elements:
            [
                new BpmnElement("start", BpmnElementTypes.StartEvent),
                new BpmnElement("gw", BpmnElementTypes.ExclusiveGateway, defaultFlowId: "flow-no"),
                new BpmnElement("task-yes", BpmnElementTypes.Task, name: "Approve"),
                new BpmnElement("task-no", BpmnElementTypes.Task),
                new BpmnElement("end", BpmnElementTypes.EndEvent),
                new BpmnElement("end-term", BpmnElementTypes.EndEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Terminate)])
            ],
            sequenceFlows:
            [
                new BpmnSequenceFlow("flow-1", "start", "gw"),
                new BpmnSequenceFlow("flow-yes", "gw", "task-yes", conditionOutcome: "Approved"),
                new BpmnSequenceFlow("flow-no", "gw", "task-no"),
                new BpmnSequenceFlow("flow-2", "task-yes", "end"),
                new BpmnSequenceFlow("flow-3", "task-no", "end-term")
            ]);

        var xml = _exporter.Export(ProcessNode(authored));
        var result = _importer.Import(xml);
        var reimported = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.Equal(
            authored.Elements.Select(element => (element.ElementId, element.ElementType, element.DefaultFlowId)),
            reimported.Elements.Select(element => (element.ElementId, element.ElementType, element.DefaultFlowId)));
        Assert.Equal(
            authored.SequenceFlows.Select(flow => (flow.FlowId, flow.SourceRef, flow.TargetRef, flow.ConditionOutcome)),
            reimported.SequenceFlows.Select(flow => (flow.FlowId, flow.SourceRef, flow.TargetRef, flow.ConditionOutcome)));

        var terminate = reimported.Elements.Single(element => element.ElementId == "end-term");
        Assert.Contains(terminate.EventDefinitions, definition => definition.Type == BpmnEventDefinitionTypes.Terminate);

        // The elsa:conditionOutcome attribute round-trips cleanly — the paired human-readable
        // conditionExpression must not double-report as a degraded import.
        Assert.DoesNotContain(result.Analysis.Issues, issue => issue.ElementId == "flow-yes");
    }

    [Fact]
    public void CyclicProcess_ImportsClean_AndRoundTrips()
    {
        // spec 122: a loop-back sequence flow (gw --again--> body) is executable, so a cyclic document
        // imports with NO cycle degradation finding and round-trips through export → import unchanged.
        var authored = new BpmnAuthoredStructure(
            elements:
            [
                new BpmnElement("start", BpmnElementTypes.StartEvent),
                new BpmnElement("body", BpmnElementTypes.Task),
                new BpmnElement("gw", BpmnElementTypes.ExclusiveGateway, defaultFlowId: "exit"),
                new BpmnElement("end", BpmnElementTypes.EndEvent)
            ],
            sequenceFlows:
            [
                new BpmnSequenceFlow("flow-1", "start", "body"),
                new BpmnSequenceFlow("flow-2", "body", "gw"),
                new BpmnSequenceFlow("again", "gw", "body", conditionOutcome: "Retry"),
                new BpmnSequenceFlow("exit", "gw", "end")
            ]);

        var xml = _exporter.Export(ProcessNode(authored));
        var result = _importer.Import(xml);
        var reimported = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.DoesNotContain(result.Analysis.Issues, issue =>
            issue.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            authored.SequenceFlows.Select(flow => (flow.FlowId, flow.SourceRef, flow.TargetRef)),
            reimported.SequenceFlows.Select(flow => (flow.FlowId, flow.SourceRef, flow.TargetRef)));
    }

    [Fact]
    public void Export_SynthesizesDiagramWhenAbsent_AndPreservesAuthoredShapes()
    {
        var diagram = JsonSerializer.SerializeToElement(new
        {
            shapes = new Dictionary<string, object> { ["start"] = new { x = 42, y = 24, width = 36, height = 36 } },
            edges = new Dictionary<string, object>()
        }, SerializerOptions);

        var authored = new BpmnAuthoredStructure(
            elements: [new BpmnElement("start", BpmnElementTypes.StartEvent), new BpmnElement("end", BpmnElementTypes.EndEvent)],
            sequenceFlows: [new BpmnSequenceFlow("flow-1", "start", "end")],
            diagram: diagram);

        var xml = _exporter.Export(ProcessNode(authored));
        var reimported = _importer.Import(xml).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        var shapes = reimported.Diagram!.Value.GetProperty("shapes");
        Assert.Equal(42, shapes.GetProperty("start").GetProperty("x").GetDouble());
        // The shape without an authored position got a synthesized one, so other modelers can render it.
        Assert.True(shapes.GetProperty("end").GetProperty("x").GetDouble() > 0);
    }

    [Fact]
    public void ExportThenImport_RoundTripsNestedSubprocess()
    {
        var inner = new BpmnAuthoredStructure(
            elements:
            [
                new BpmnElement("inner-start", BpmnElementTypes.StartEvent),
                new BpmnElement("inner-end", BpmnElementTypes.EndEvent)
            ],
            sequenceFlows: [new BpmnSequenceFlow("inner-flow", "inner-start", "inner-end")]);

        var innerNode = new ActivityNode(
            "node-inner",
            "Elsa.BpmnProcess",
            [],
            [],
            new ActivityNodeStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(inner, SerializerOptions)));

        var outer = new BpmnAuthoredStructure(
            activities: [innerNode],
            elements:
            [
                new BpmnElement("start", BpmnElementTypes.StartEvent),
                new BpmnElement("sub", BpmnElementTypes.SubProcess, childNodeId: "node-inner"),
                new BpmnElement("end", BpmnElementTypes.EndEvent)
            ],
            sequenceFlows:
            [
                new BpmnSequenceFlow("flow-1", "start", "sub"),
                new BpmnSequenceFlow("flow-2", "sub", "end")
            ]);

        var xml = _exporter.Export(ProcessNode(outer));
        var reimported = _importer.Import(xml).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        var subprocess = reimported.Elements.Single(element => element.ElementId == "sub");
        Assert.Equal("node-sub", subprocess.ChildNodeId);
        var child = Assert.Single(reimported.Activities);
        var childStructure = child.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        Assert.Equal(["inner-start", "inner-end"], childStructure.Elements.Select(element => element.ElementId));
    }

    [Theory]
    [InlineData(BpmnEventDefinitionTypes.Timer, BpmnEventDefinitionProperties.Cron, "0 * * * *")]
    [InlineData(BpmnEventDefinitionTypes.Timer, BpmnEventDefinitionProperties.Interval, "PT5M")]
    [InlineData(BpmnEventDefinitionTypes.Message, BpmnEventDefinitionProperties.Name, "order-placed")]
    [InlineData(BpmnEventDefinitionTypes.Signal, BpmnEventDefinitionProperties.Name, "cancel-requested")]
    public void EventDefinedStart_RoundTripsDefinitionAndProperties(string type, string propertyKey, string propertyValue)
    {
        var authored = new BpmnAuthoredStructure(
            elements:
            [
                new BpmnElement("start-evt", BpmnElementTypes.StartEvent, eventDefinitions:
                    [new BpmnEventDefinition(type, new Dictionary<string, string> { [propertyKey] = propertyValue })]),
                new BpmnElement("end", BpmnElementTypes.EndEvent)
            ],
            sequenceFlows: [new BpmnSequenceFlow("flow-1", "start-evt", "end")]);

        var reimported = ExportImport(authored);

        var start = reimported.Elements.Single(element => element.ElementId == "start-evt");
        Assert.Null(start.ChildNodeId); // pure element (spec 117)
        var definition = Assert.Single(start.EventDefinitions);
        Assert.Equal(type, definition.Type);
        Assert.Equal(propertyValue, definition.Properties[propertyKey]);
    }

    [Fact]
    public void TimerCatchEvent_RoundTripsDurationAndRegeneratesDelayChild()
    {
        var authored = CatchStructure(
            new BpmnEventDefinition(BpmnEventDefinitionTypes.Timer, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Interval] = "PT5M" }),
            new ActivityNode("node-catch", "Elsa.Delay",
                [new ArgumentState("Duration", new ArgumentValue("PT5M", "Literal"), null, null, null, null)], []));

        var reimported = ExportImport(authored);

        var catchEvent = reimported.Elements.Single(element => element.ElementId == "catch");
        Assert.Equal(BpmnElementTypes.IntermediateCatchEvent, catchEvent.ElementType);
        Assert.Equal("node-catch", catchEvent.ChildNodeId);
        Assert.Equal("PT5M", Assert.Single(catchEvent.EventDefinitions).Properties[BpmnEventDefinitionProperties.Interval]);
        var child = Assert.Single(reimported.Activities);
        Assert.Equal("Elsa.Delay", child.ActivityVersionId);
        Assert.Equal("PT5M", LiteralString(child, "Duration"));
    }

    [Theory]
    [InlineData(BpmnEventDefinitionTypes.Message)]
    [InlineData(BpmnEventDefinitionTypes.Signal)]
    public void MessageOrSignalCatchEvent_RoundTripsNameAndRegeneratesEventChild(string type)
    {
        var authored = CatchStructure(
            new BpmnEventDefinition(type, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = "order-shipped" }),
            new ActivityNode("node-catch", "Elsa.Event",
                [new ArgumentState("EventName", new ArgumentValue("order-shipped", "Literal"), null, null, null, null)], []));

        var reimported = ExportImport(authored);

        var catchEvent = reimported.Elements.Single(element => element.ElementId == "catch");
        Assert.Equal(type, Assert.Single(catchEvent.EventDefinitions).Type);
        Assert.Equal("order-shipped", Assert.Single(catchEvent.EventDefinitions).Properties[BpmnEventDefinitionProperties.Name]);
        var child = Assert.Single(reimported.Activities);
        Assert.Equal("Elsa.Event", child.ActivityVersionId);
        Assert.Equal("order-shipped", LiteralString(child, "EventName"));
    }

    // The structure payload is JSON, so a deserialized literal surfaces as a JsonElement string.
    private static string? LiteralString(ActivityNode node, string key)
    {
        var value = Assert.Single(node.Inputs, argument => argument.ReferenceKey == key).Value.Value;
        return value is JsonElement element ? element.GetString() : value?.ToString();
    }

    private static BpmnAuthoredStructure CatchStructure(BpmnEventDefinition definition, ActivityNode child) =>
        new(
            activities: [child],
            elements:
            [
                new BpmnElement("start", BpmnElementTypes.StartEvent),
                new BpmnElement("catch", BpmnElementTypes.IntermediateCatchEvent, childNodeId: child.NodeId, eventDefinitions: [definition]),
                new BpmnElement("end", BpmnElementTypes.EndEvent)
            ],
            sequenceFlows:
            [
                new BpmnSequenceFlow("flow-1", "start", "catch"),
                new BpmnSequenceFlow("flow-2", "catch", "end")
            ]);

    private BpmnAuthoredStructure ExportImport(BpmnAuthoredStructure authored)
    {
        var xml = _exporter.Export(ProcessNode(authored));
        return _importer.Import(xml).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
    }
}

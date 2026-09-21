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
/// Spec 133 D5 — interchange for call activities. A <c>&lt;callActivity&gt;</c> carrying the Elsa extension
/// attribute <c>elsa:workflowDefinitionId</c> imports bound (a synthesized <c>DispatchWorkflow</c> child, honoring
/// <c>elsa:waitForCompletion="false"</c>); a plain <c>calledElement</c> imports unbound with an Info finding and a
/// <c>bpmn.calledElement</c> passthrough. Both round-trip through export→import, and the importer never emits a
/// graph the validator rejects.
/// </summary>
public sealed class BpmnCallActivityInterchangeTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BpmnDocumentImporter _importer = new();
    private readonly BpmnDocumentExporter _exporter = new();

    private const string BoundDocument = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:elsa="https://elsa-workflows.io/schemas/bpmn" id="defs" targetNamespace="https://example.org">
          <process id="p" isExecutable="true">
            <startEvent id="start" />
            <callActivity id="call" name="Invoke child" calledElement="ForeignProcess" elsa:workflowDefinitionId="child-def-1" />
            <endEvent id="end" />
            <sequenceFlow id="f1" sourceRef="start" targetRef="call" />
            <sequenceFlow id="f2" sourceRef="call" targetRef="end" />
          </process>
        </definitions>
        """;

    private const string UnboundDocument = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
          <process id="p" isExecutable="true">
            <startEvent id="start" />
            <callActivity id="call" calledElement="ForeignProcess" />
            <endEvent id="end" />
            <sequenceFlow id="f1" sourceRef="start" targetRef="call" />
            <sequenceFlow id="f2" sourceRef="call" targetRef="end" />
          </process>
        </definitions>
        """;

    [Fact]
    public void Import_Bound_SynthesizesDispatchWorkflowChild_RecordsPassthrough_AndValidates()
    {
        var result = _importer.Import(BoundDocument);
        var structure = Deserialize(result);

        var call = structure.Elements.Single(element => element.ElementId == "call");
        Assert.Equal(BpmnElementTypes.CallActivity, call.ElementType);
        Assert.NotNull(call.ChildNodeId);
        Assert.Equal("ForeignProcess", call.Properties["bpmn.calledElement"]);

        var child = structure.Activities.Single(activity => activity.NodeId == call.ChildNodeId);
        Assert.Equal(BpmnDocumentImporter.DefaultDispatchWorkflowActivityVersionId, child.ActivityVersionId);
        Assert.Equal("child-def-1", Literal(child, "WorkflowDefinitionId"));
        Assert.Equal(true, Literal(child, "WaitForCompletion"));

        // No unbound finding, and the imported graph builds.
        Assert.DoesNotContain(result.Analysis.Issues, issue => issue.ElementId == "call");
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    [Fact]
    public void Import_Unbound_RecordsPassthrough_AddsInfoFinding_AndValidates()
    {
        var result = _importer.Import(UnboundDocument);
        var structure = Deserialize(result);

        var call = structure.Elements.Single(element => element.ElementId == "call");
        Assert.Equal(BpmnElementTypes.CallActivity, call.ElementType);
        Assert.Null(call.ChildNodeId);
        Assert.Equal("ForeignProcess", call.Properties["bpmn.calledElement"]);
        Assert.Empty(structure.Activities);

        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Info && issue.ElementId == "call" && issue.Message.Contains("imported unbound", StringComparison.Ordinal));
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    [Fact]
    public void Import_HonorsWaitForCompletionFalse()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:elsa="https://elsa-workflows.io/schemas/bpmn" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <callActivity id="call" elsa:workflowDefinitionId="child-def-1" elsa:waitForCompletion="false" />
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="call" />
                <sequenceFlow id="f2" sourceRef="call" targetRef="end" />
              </process>
            </definitions>
            """;

        var structure = Deserialize(_importer.Import(document));
        var call = structure.Elements.Single(element => element.ElementId == "call");
        var child = structure.Activities.Single(activity => activity.NodeId == call.ChildNodeId);
        Assert.Equal(false, Literal(child, "WaitForCompletion"));
    }

    [Fact]
    public void Bound_RoundTripsThroughExportImport_WithFidelity()
    {
        var imported = _importer.Import(BoundDocument).ProcessNode;
        var xml = _exporter.Export(imported);

        Assert.Contains("<callActivity", xml, StringComparison.Ordinal);
        Assert.Contains("calledElement=\"ForeignProcess\"", xml, StringComparison.Ordinal);
        Assert.Contains("workflowDefinitionId=\"child-def-1\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("waitForCompletion", xml, StringComparison.Ordinal); // waited-by-default omits it

        var reimported = Deserialize(_importer.Import(xml));
        var call = reimported.Elements.Single(element => element.ElementId == "call");
        Assert.Equal("ForeignProcess", call.Properties["bpmn.calledElement"]);
        var child = reimported.Activities.Single(activity => activity.NodeId == call.ChildNodeId);
        Assert.Equal("child-def-1", Literal(child, "WorkflowDefinitionId"));
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(reimported)));
    }

    [Fact]
    public void FireAndForget_RoundTripsWaitForCompletionFalse()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:elsa="https://elsa-workflows.io/schemas/bpmn" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <callActivity id="call" elsa:workflowDefinitionId="child-def-1" elsa:waitForCompletion="false" />
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="call" />
                <sequenceFlow id="f2" sourceRef="call" targetRef="end" />
              </process>
            </definitions>
            """;

        var xml = _exporter.Export(_importer.Import(document).ProcessNode);
        Assert.Contains("waitForCompletion=\"false\"", xml, StringComparison.Ordinal);

        var reimported = Deserialize(_importer.Import(xml));
        var call = reimported.Elements.Single(element => element.ElementId == "call");
        var child = reimported.Activities.Single(activity => activity.NodeId == call.ChildNodeId);
        Assert.Equal(false, Literal(child, "WaitForCompletion"));
    }

    [Fact]
    public void Unbound_RoundTripsCalledElementPassthrough_StillDegrades()
    {
        var imported = _importer.Import(UnboundDocument).ProcessNode;
        var xml = _exporter.Export(imported);

        Assert.Contains("calledElement=\"ForeignProcess\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("workflowDefinitionId", xml, StringComparison.Ordinal);

        var result = _importer.Import(xml);
        var reimported = Deserialize(result);
        var call = reimported.Elements.Single(element => element.ElementId == "call");
        Assert.Null(call.ChildNodeId);
        Assert.Equal("ForeignProcess", call.Properties["bpmn.calledElement"]);
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Info && issue.ElementId == "call");
    }

    [Fact]
    public void Export_Bound_WithoutPassthrough_FallsBackToWorkflowDefinitionId()
    {
        // A Studio-authored bound call activity (no bpmn.calledElement property) exports calledElement = the child's
        // authored WorkflowDefinitionId, so re-import binds and round-trips.
        var childNode = new ActivityNode("node-call", BpmnDocumentImporter.DefaultDispatchWorkflowActivityVersionId,
            [new ArgumentState("WorkflowDefinitionId", new Expressions.Core.Models.ArgumentValue("child-def-9", "Literal"), null, null, null, null),
             new ArgumentState("WaitForCompletion", new Expressions.Core.Models.ArgumentValue(true, "Literal"), null, null, null, null)],
            []);
        var authored = new BpmnAuthoredStructure(
            activities: [childNode],
            elements:
            [
                BpmnRuntimeElement("start", BpmnElementTypes.StartEvent),
                new BpmnElement("call", BpmnElementTypes.CallActivity, childNodeId: "node-call"),
                BpmnRuntimeElement("end", BpmnElementTypes.EndEvent)
            ],
            sequenceFlows:
            [
                new BpmnSequenceFlow("f1", "start", "call"),
                new BpmnSequenceFlow("f2", "call", "end")
            ]);

        var xml = _exporter.Export(ProcessNode(authored));
        Assert.Contains("calledElement=\"child-def-9\"", xml, StringComparison.Ordinal);
        Assert.Contains("workflowDefinitionId=\"child-def-9\"", xml, StringComparison.Ordinal);

        var reimported = Deserialize(_importer.Import(xml));
        var call = reimported.Elements.Single(element => element.ElementId == "call");
        Assert.Equal("child-def-9", Literal(reimported.Activities.Single(activity => activity.NodeId == call.ChildNodeId), "WorkflowDefinitionId"));
    }

    private static BpmnElement BpmnRuntimeElement(string id, string type) => new(id, type);

    private static ActivityNode ProcessNode(BpmnAuthoredStructure structure) =>
        new("node-p", BpmnProcessActivity.StructureKind, [], [],
            new ActivityNodeStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions)));

    private static BpmnAuthoredStructure Deserialize(BpmnImportResult result) =>
        result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

    // The structure payload is JSON, so a deserialized literal surfaces as a JsonElement; unwrap it.
    private static object? Literal(ActivityNode node, string key)
    {
        var value = Assert.Single(node.Inputs, argument => argument.ReferenceKey == key).Value.Value;
        return value is JsonElement element
            ? element.ValueKind switch
            {
                JsonValueKind.False => false,
                JsonValueKind.True => true,
                JsonValueKind.String => element.GetString(),
                _ => element.ToString()
            }
            : value;
    }

    private static ExecutableNode BridgeToExecutableNode(BpmnAuthoredStructure structure)
    {
        var children = structure.Activities
            .Select(activity => new ExecutableNode(
                executableNodeId: activity.NodeId,
                authoredActivityId: $"authored-{activity.NodeId}",
                activityType: StringComparer.Ordinal.Equals(activity.Structure?.Kind, BpmnProcessActivity.StructureKind) ? typeof(BpmnProcessActivity).FullName! : "test",
                activityTypeVersion: "1.0.0",
                descriptorType: "test",
                descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: new Dictionary<string, string>(),
                childSlots: [],
                structure: activity.Structure is { } childStructure
                    ? new ExecutableActivityStructure(childStructure.Kind, childStructure.SchemaVersion, childStructure.Payload)
                    : null))
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

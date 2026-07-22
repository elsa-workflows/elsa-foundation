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
/// Spec 128 D7 — interchange for event subprocesses: a flow-less <c>subProcess triggeredByEvent="true"</c> whose body
/// declares an escalation/error start (with isInterrupting) imports as a <c>TriggeredByEvent</c> element and round-trips
/// through export→import with trigger/interrupting fidelity; unsupported/malformed shapes (no/multiple start, unsupported
/// trigger, non-interrupting error, per-scope code collisions / duplicate error) Drop with specific findings, so the
/// importer never emits a graph the validator rejects.
/// </summary>
public sealed class BpmnEventSubprocessInterchangeTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BpmnDocumentImporter _importer = new();
    private readonly BpmnDocumentExporter _exporter = new();

    private const string Document = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
          <escalation id="esc-decl-1" name="Overload" escalationCode="E1" />
          <process id="p" isExecutable="true">
            <startEvent id="start" />
            <task id="work" />
            <endEvent id="end" />
            <subProcess id="es-esc" triggeredByEvent="true">
              <startEvent id="es-esc-start" isInterrupting="false">
                <escalationEventDefinition escalationRef="esc-decl-1" />
              </startEvent>
              <task id="es-esc-task" />
              <endEvent id="es-esc-end" />
              <sequenceFlow id="ee1" sourceRef="es-esc-start" targetRef="es-esc-task" />
              <sequenceFlow id="ee2" sourceRef="es-esc-task" targetRef="es-esc-end" />
            </subProcess>
            <subProcess id="es-err" triggeredByEvent="true">
              <startEvent id="es-err-start">
                <errorEventDefinition />
              </startEvent>
              <task id="es-err-task" />
              <endEvent id="es-err-end" />
              <sequenceFlow id="er1" sourceRef="es-err-start" targetRef="es-err-task" />
              <sequenceFlow id="er2" sourceRef="es-err-task" targetRef="es-err-end" />
            </subProcess>
            <sequenceFlow id="f1" sourceRef="start" targetRef="work" />
            <sequenceFlow id="f2" sourceRef="work" targetRef="end" />
          </process>
        </definitions>
        """;

    [Fact]
    public void Import_ResolvesEventSubprocesses_AndValidates()
    {
        var structure = Import(Document);

        var esc = structure.Elements.Single(element => element.ElementId == "es-esc");
        Assert.True(esc.TriggeredByEvent);
        Assert.Null(esc.EventDefinitions.SingleOrDefault()); // the trigger lives on the body start, not the element
        var err = structure.Elements.Single(element => element.ElementId == "es-err");
        Assert.True(err.TriggeredByEvent);

        // The escalation body start carries the resolved code + non-interrupting flag; the error body start is interrupting.
        var escStart = ReadNested(structure, esc.ChildNodeId!).Elements.Single(element => element.ElementId == "es-esc-start");
        Assert.Equal(BpmnEventDefinitionTypes.Escalation, escStart.EventDefinitions.Single().Type);
        Assert.Equal("E1", escStart.EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Code]);
        Assert.False(escStart.CancelActivity); // isInterrupting="false"
        var errStart = ReadNested(structure, err.ChildNodeId!).Elements.Single(element => element.ElementId == "es-err-start");
        Assert.Equal(BpmnEventDefinitionTypes.Error, errStart.EventDefinitions.Single().Type);
        Assert.True(errStart.CancelActivity); // interrupting by default

        // Validate-representable: the imported graph builds and indexes both catchers.
        var graph = BpmnGraph.From(BridgeToExecutableNode(structure));
        Assert.Equal(2, graph.EventSubprocesses.Count);
        Assert.NotNull(graph.EscalationEventSubprocess("E1"));
        Assert.NotNull(graph.ErrorEventSubprocess());
    }

    [Fact]
    public void EventSubprocesses_RoundTripThroughExportImport_WithFidelity()
    {
        var imported = _importer.Import(Document).ProcessNode;
        var xml = _exporter.Export(imported);

        Assert.Contains("triggeredByEvent=\"true\"", xml, StringComparison.Ordinal);
        Assert.Contains("<escalationEventDefinition", xml, StringComparison.Ordinal);
        Assert.Contains("<errorEventDefinition", xml, StringComparison.Ordinal);
        Assert.Contains("isInterrupting=\"false\"", xml, StringComparison.Ordinal);

        var reimported = Import(xml);
        Assert.True(reimported.Elements.Single(element => element.ElementId == "es-esc").TriggeredByEvent);
        Assert.True(reimported.Elements.Single(element => element.ElementId == "es-err").TriggeredByEvent);
        var escStart = ReadNested(reimported, reimported.Elements.Single(element => element.ElementId == "es-esc").ChildNodeId!).Elements.Single(element => element.ElementId == "es-esc-start");
        Assert.Equal("E1", escStart.EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Code]);
        Assert.False(escStart.CancelActivity);

        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(reimported)));
    }

    [Fact]
    public void BodyWithNoStart_Drops() =>
        AssertDrops(EventSubprocessDoc("es", body: "<task id=\"t\" />"), "es", "exactly one start event");

    [Fact]
    public void UnsupportedTrigger_Drops() =>
        // A timer start imports WITH a (timer) definition, so it reaches the unsupported-trigger check (tier 2).
        AssertDrops(
            EventSubprocessDoc("es", body: "<startEvent id=\"s\"><timerEventDefinition><timeCycle>PT1H</timeCycle></timerEventDefinition></startEvent>"),
            "es", "unsupported trigger definition");

    [Fact]
    public void NonInterruptingError_Drops() =>
        AssertDrops(
            EventSubprocessDoc("es", body: "<startEvent id=\"s\" isInterrupting=\"false\"><errorEventDefinition /></startEvent>"),
            "es", "non-interrupting error event subprocess");

    [Fact]
    public void DuplicateEscalationCode_Drops()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <escalation id="d1" escalationCode="E1" />
              <process id="p" isExecutable="true">
                <startEvent id="start" /><endEvent id="end" />
                <subProcess id="es-1" triggeredByEvent="true"><startEvent id="s1"><escalationEventDefinition escalationRef="d1" /></startEvent></subProcess>
                <subProcess id="es-2" triggeredByEvent="true"><startEvent id="s2"><escalationEventDefinition escalationRef="d1" /></startEvent></subProcess>
                <sequenceFlow id="f1" sourceRef="start" targetRef="end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.Contains(structure.Elements, element => element.ElementId == "es-1");
        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "es-2");
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "es-2");
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    private static string EventSubprocessDoc(string id, string body) => $"""
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
          <process id="p" isExecutable="true">
            <startEvent id="start" /><endEvent id="end" />
            <subProcess id="{id}" triggeredByEvent="true">{body}</subProcess>
            <sequenceFlow id="f1" sourceRef="start" targetRef="end" />
          </process>
        </definitions>
        """;

    private void AssertDrops(string document, string elementId, string expectedFinding)
    {
        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.DoesNotContain(structure.Elements, element => element.ElementId == elementId);
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == elementId && issue.Message.Contains(expectedFinding, StringComparison.Ordinal));

        // The surviving graph is validate-representable.
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    private BpmnAuthoredStructure Import(string document) =>
        _importer.Import(document).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

    private static BpmnAuthoredStructure ReadNested(BpmnAuthoredStructure structure, string nodeId) =>
        structure.Activities.Single(activity => activity.NodeId == nodeId).Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

    private static ExecutableNode BridgeToExecutableNode(BpmnAuthoredStructure structure)
    {
        // Preserve each child's BPMN structure so event-subprocess body-shape validation can read the body start event.
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

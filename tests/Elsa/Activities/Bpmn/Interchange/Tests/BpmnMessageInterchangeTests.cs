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
/// Spec 135 — interchange for the message send surface: a message throw/end event and a sendTask/receiveTask
/// (resolving a <c>messageRef</c> to a root <c>&lt;message&gt;</c> declaration's name) import bound — synthesizing a
/// <c>PublishEvent</c> send child (throw/end/sendTask) or an <c>Event</c> catch child (receiveTask) — and round-trip
/// through export→import; a name-less message throw Drops, a name-less message end degrades to a none end, a name-less
/// send/receive task imports as a plain unbound task, and a signal throw stays Dropped (stated cut). The importer never
/// emits a validator-rejected graph.
/// </summary>
public sealed class BpmnMessageInterchangeTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BpmnDocumentImporter _importer = new();
    private readonly BpmnDocumentExporter _exporter = new();

    private const string MessageDocument = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
          <message id="msg-shipped" name="order-shipped" />
          <message id="msg-approved" name="order-approved" />
          <message id="msg-done" name="process-done" />
          <process id="p" isExecutable="true">
            <startEvent id="start" />
            <intermediateThrowEvent id="throw-msg">
              <messageEventDefinition messageRef="msg-shipped" />
            </intermediateThrowEvent>
            <sendTask id="send"><extensionElements /></sendTask>
            <receiveTask id="receive" />
            <endEvent id="end-msg">
              <messageEventDefinition messageRef="msg-done" />
            </endEvent>
            <sequenceFlow id="f1" sourceRef="start" targetRef="throw-msg" />
            <sequenceFlow id="f2" sourceRef="throw-msg" targetRef="send" />
            <sequenceFlow id="f3" sourceRef="send" targetRef="receive" />
            <sequenceFlow id="f4" sourceRef="receive" targetRef="end-msg" />
          </process>
        </definitions>
        """;

    private static string WithMessageRefs(string document) => document
        .Replace("<sendTask id=\"send\"><extensionElements /></sendTask>", "<sendTask id=\"send\" messageRef=\"msg-shipped\" />", StringComparison.Ordinal)
        .Replace("<receiveTask id=\"receive\" />", "<receiveTask id=\"receive\" messageRef=\"msg-approved\" />", StringComparison.Ordinal);

    [Fact]
    public void Import_ResolvesMessageThrowEndAndSendReceiveTasks_AndValidates()
    {
        var result = _importer.Import(WithMessageRefs(MessageDocument));
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        var throwMsg = structure.Elements.Single(element => element.ElementId == "throw-msg");
        Assert.Equal(BpmnElementTypes.IntermediateThrowEvent, throwMsg.ElementType);
        Assert.NotNull(throwMsg.ChildNodeId);
        Assert.Equal("order-shipped", throwMsg.EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Name]);
        AssertPublishSendChild(structure, throwMsg.ChildNodeId!, "order-shipped");

        var endMsg = structure.Elements.Single(element => element.ElementId == "end-msg");
        Assert.Equal(BpmnElementTypes.EndEvent, endMsg.ElementType);
        AssertPublishSendChild(structure, endMsg.ChildNodeId!, "process-done");

        var send = structure.Elements.Single(element => element.ElementId == "send");
        Assert.Equal(BpmnElementTypes.SendTask, send.ElementType);
        AssertPublishSendChild(structure, send.ChildNodeId!, "order-shipped");

        var receive = structure.Elements.Single(element => element.ElementId == "receive");
        Assert.Equal(BpmnElementTypes.ReceiveTask, receive.ElementType);
        var receiveChild = structure.Activities.Single(activity => activity.NodeId == receive.ChildNodeId);
        Assert.Equal(BpmnDocumentImporter.DefaultEventActivityVersionId, receiveChild.ActivityVersionId);

        // Validate-representable: the imported graph satisfies the frozen + new message rules.
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    [Fact]
    public void MessageEvents_RoundTripThroughExportImport_WithFidelity()
    {
        var imported = _importer.Import(WithMessageRefs(MessageDocument)).ProcessNode;

        var xml = _exporter.Export(imported);
        Assert.Contains("<message ", xml, StringComparison.Ordinal);
        Assert.Contains("name=\"order-shipped\"", xml, StringComparison.Ordinal);
        Assert.Contains("<messageEventDefinition messageRef=\"message-order-shipped\"", xml, StringComparison.Ordinal);
        Assert.Contains("<sendTask", xml, StringComparison.Ordinal);
        Assert.Contains("messageRef=\"message-order-shipped\"", xml, StringComparison.Ordinal);
        Assert.Contains("<receiveTask", xml, StringComparison.Ordinal);
        Assert.Contains("messageRef=\"message-order-approved\"", xml, StringComparison.Ordinal);
        // Synthesized send/catch children never export.
        Assert.DoesNotContain("PublishEvent", xml, StringComparison.Ordinal);

        var reimported = _importer.Import(xml).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        Assert.Equal("order-shipped", reimported.Elements.Single(element => element.ElementId == "throw-msg").EventDefinitions.Single().Properties[BpmnEventDefinitionProperties.Name]);
        AssertPublishSendChild(reimported, reimported.Elements.Single(element => element.ElementId == "send").ChildNodeId!, "order-shipped");
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(reimported)));
    }

    [Fact]
    public void NameLessMessageThrow_IsDropped_AndFlowsCascade()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <intermediateThrowEvent id="t"><messageEventDefinition /></intermediateThrowEvent>
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
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "t" && issue.Message.Contains("no resolvable name", StringComparison.Ordinal));
    }

    [Fact]
    public void NameLessMessageEnd_DegradesToNoneEnd()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <endEvent id="e"><messageEventDefinition /></endEvent>
                <sequenceFlow id="f1" sourceRef="start" targetRef="e" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        var end = structure.Elements.Single(element => element.ElementId == "e");
        Assert.Empty(end.EventDefinitions); // degraded to a none end
        Assert.Null(end.ChildNodeId);
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Degraded && issue.ElementId == "e");
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    [Fact]
    public void NameLessSendReceiveTasks_ImportUnbound_WithInfoFinding()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <sendTask id="send" />
                <receiveTask id="receive" />
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="send" />
                <sequenceFlow id="f2" sourceRef="send" targetRef="receive" />
                <sequenceFlow id="f3" sourceRef="receive" targetRef="end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.Null(structure.Elements.Single(element => element.ElementId == "send").ChildNodeId);
        Assert.Null(structure.Elements.Single(element => element.ElementId == "receive").ChildNodeId);
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Info && issue.ElementId == "send");
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Info && issue.ElementId == "receive");
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    [Fact]
    public void SignalThrow_StaysDropped()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <signal id="sig" name="alarm" />
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <intermediateThrowEvent id="t"><signalEventDefinition signalRef="sig" /></intermediateThrowEvent>
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="t" />
                <sequenceFlow id="f2" sourceRef="t" targetRef="end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "t");
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "t");
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    [Fact]
    public void Import_IsDeterministic()
    {
        var first = _exporter.Export(_importer.Import(WithMessageRefs(MessageDocument)).ProcessNode);
        var second = _exporter.Export(_importer.Import(WithMessageRefs(MessageDocument)).ProcessNode);
        Assert.Equal(first, second);
    }

    private static void AssertPublishSendChild(BpmnAuthoredStructure structure, string childNodeId, string expectedEventName)
    {
        var child = structure.Activities.Single(activity => activity.NodeId == childNodeId);
        Assert.Equal(BpmnDocumentImporter.DefaultPublishEventActivityVersionId, child.ActivityVersionId);
        var eventName = child.Inputs.Single(input => input.ReferenceKey == "EventName").Value.Value;
        Assert.Equal(expectedEventName, eventName switch
        {
            string text => text,
            JsonElement json => json.GetString(),
            _ => eventName?.ToString()
        });
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

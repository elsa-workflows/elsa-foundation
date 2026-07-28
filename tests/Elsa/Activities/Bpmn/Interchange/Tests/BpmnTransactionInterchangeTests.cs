using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

/// <summary>
/// Spec 125 D4 — interchange for transactions. A &lt;transaction&gt; with a cancel end, a cancel boundary, and an
/// inner spec-124 compensation construct round-trips import→export→import; a cancel end outside a transaction
/// degrades to a none end, a cancel boundary on a non-transaction host drops (flow cascade), and a duplicate
/// cancel boundary drops — each with a specific finding. The importer never emits a graph the validator rejects.
/// </summary>
public sealed class BpmnTransactionInterchangeTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BpmnDocumentImporter _importer = new();
    private readonly BpmnDocumentExporter _exporter = new();

    private const string TransactionDocument = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
          <process id="p" isExecutable="true">
            <startEvent id="start" />
            <transaction id="txn">
              <startEvent id="ts" />
              <subProcess id="thost">
                <startEvent id="hs" /><endEvent id="he" />
                <sequenceFlow id="hf" sourceRef="hs" targetRef="he" />
              </subProcess>
              <subProcess id="thandler" isForCompensation="true">
                <startEvent id="chs" /><endEvent id="che" />
                <sequenceFlow id="chf" sourceRef="chs" targetRef="che" />
              </subProcess>
              <boundaryEvent id="tcb" attachedToRef="thost"><compensateEventDefinition /></boundaryEvent>
              <association id="ta" sourceRef="tcb" targetRef="thandler" />
              <endEvent id="tcancel"><cancelEventDefinition /></endEvent>
              <sequenceFlow id="tf1" sourceRef="ts" targetRef="thost" />
              <sequenceFlow id="tf2" sourceRef="thost" targetRef="tcancel" />
            </transaction>
            <boundaryEvent id="cb" attachedToRef="txn"><cancelEventDefinition /></boundaryEvent>
            <endEvent id="normal-end" />
            <endEvent id="cancelled-end" />
            <sequenceFlow id="f1" sourceRef="start" targetRef="txn" />
            <sequenceFlow id="f2" sourceRef="txn" targetRef="normal-end" />
            <sequenceFlow id="f3" sourceRef="cb" targetRef="cancelled-end" />
          </process>
        </definitions>
        """;

    [Fact]
    public void Import_ResolvesTransactionCancelEndAndCancelBoundary_AndValidates()
    {
        var node = _importer.Import(TransactionDocument).ProcessNode;
        var structure = node.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        var txn = structure.Elements.Single(element => element.ElementId == "txn");
        Assert.Equal(BpmnElementTypes.SubProcess, txn.ElementType);
        Assert.True(txn.IsTransaction);

        var cancelBoundary = structure.Elements.Single(element => element.ElementId == "cb");
        Assert.Equal(BpmnEventDefinitionTypes.Cancel, cancelBoundary.EventDefinitions.Single().Type);
        Assert.Equal("txn", cancelBoundary.AttachedToRef);

        // The nested transaction process carries IsTransaction and the cancel end.
        var nested = structure.Activities.Single(activity => activity.NodeId == "node-txn").Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        Assert.True(nested.IsTransaction);
        var cancelEnd = nested.Elements.Single(element => element.ElementId == "tcancel");
        Assert.Equal(BpmnEventDefinitionTypes.Cancel, cancelEnd.EventDefinitions.Single().Type);
        // The inner spec-124 compensation boundary/handler still resolved inside the transaction.
        Assert.Equal("thandler", nested.Elements.Single(element => element.ElementId == "tcb").CompensationHandlerElementId);
        Assert.True(nested.Elements.Single(element => element.ElementId == "thandler").IsForCompensation);

        // The imported outer graph is validate-representable (transaction element + cancel boundary).
        var graph = BpmnGraph.From(BridgeToExecutableNode(structure));
        Assert.NotNull(graph.AttachedCancelBoundary("txn"));
    }

    [Fact]
    public void Transaction_RoundTripsThroughExportImport_WithFidelity()
    {
        var imported = _importer.Import(TransactionDocument).ProcessNode;

        var xml = _exporter.Export(imported);
        Assert.Contains("<transaction", xml, StringComparison.Ordinal);
        Assert.Contains("<cancelEventDefinition", xml, StringComparison.Ordinal);
        Assert.Contains("<compensateEventDefinition", xml, StringComparison.Ordinal);

        var reimported = _importer.Import(xml).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        Assert.True(reimported.Elements.Single(element => element.ElementId == "txn").IsTransaction);
        Assert.Equal(BpmnEventDefinitionTypes.Cancel, reimported.Elements.Single(element => element.ElementId == "cb").EventDefinitions.Single().Type);

        var reNested = reimported.Activities.Single(activity => activity.NodeId == "node-txn").Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        Assert.True(reNested.IsTransaction);
        Assert.Equal(BpmnEventDefinitionTypes.Cancel, reNested.Elements.Single(element => element.ElementId == "tcancel").EventDefinitions.Single().Type);

        // The round-tripped structure still validates.
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(reimported)));
    }

    [Fact]
    public void CancelEnd_OutsideTransaction_DegradesToNoneEnd()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <endEvent id="e"><cancelEventDefinition /></endEvent>
                <sequenceFlow id="f1" sourceRef="start" targetRef="e" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        // Degraded to a none end event (no cancel definition).
        Assert.Empty(structure.Elements.Single(element => element.ElementId == "e").EventDefinitions);
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Degraded && issue.ElementId == "e" && issue.Message.Contains("not inside a transaction", StringComparison.Ordinal));
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    [Fact]
    public void CancelBoundary_OnNonTransactionHost_IsDropped_WithFlowCascade()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <subProcess id="sub">
                  <startEvent id="ss" /><endEvent id="se" />
                  <sequenceFlow id="sf" sourceRef="ss" targetRef="se" />
                </subProcess>
                <boundaryEvent id="cb" attachedToRef="sub"><cancelEventDefinition /></boundaryEvent>
                <endEvent id="end" />
                <endEvent id="cancelled-end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="sub" />
                <sequenceFlow id="f2" sourceRef="sub" targetRef="end" />
                <sequenceFlow id="f3" sourceRef="cb" targetRef="cancelled-end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "cb");
        Assert.DoesNotContain(structure.SequenceFlows, flow => flow.FlowId == "f3"); // flow cascade-dropped
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "cb" && issue.Message.Contains("not a transaction", StringComparison.Ordinal));
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(structure)));
    }

    [Fact]
    public void SecondCancelBoundary_OnTransactionHost_IsDropped()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <transaction id="txn">
                  <startEvent id="ts" /><endEvent id="te" />
                  <sequenceFlow id="tf" sourceRef="ts" targetRef="te" />
                </transaction>
                <boundaryEvent id="cb1" attachedToRef="txn"><cancelEventDefinition /></boundaryEvent>
                <boundaryEvent id="cb2" attachedToRef="txn"><cancelEventDefinition /></boundaryEvent>
                <endEvent id="end" />
                <endEvent id="c1" />
                <endEvent id="c2" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="txn" />
                <sequenceFlow id="f2" sourceRef="txn" targetRef="end" />
                <sequenceFlow id="f3" sourceRef="cb1" targetRef="c1" />
                <sequenceFlow id="f4" sourceRef="cb2" targetRef="c2" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        // Exactly one cancel boundary survives; the second dropped with its flow cascading.
        Assert.Single(structure.Elements, element => element.ElementId is "cb1" or "cb2");
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "cb2" && issue.Message.Contains("at most one", StringComparison.Ordinal));
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
                JsonSerializer.SerializeToElement(new BpmnStructure(structure.Elements, structure.SequenceFlows, isTransaction: structure.IsTransaction))));
    }
}

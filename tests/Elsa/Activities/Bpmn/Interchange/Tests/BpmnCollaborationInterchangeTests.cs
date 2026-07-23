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
/// Spec 136 — collaboration/pool/message-flow import. A multi-pool document imports every <c>&lt;process&gt;</c>
/// into <c>ProcessNodes</c> (the primary stays <c>ProcessNode</c>), populates <c>BpmnPool</c> from
/// <c>&lt;participant&gt;</c> (lanes gain their pool id), and records <c>&lt;messageFlow&gt;</c> wiring on the
/// involved structures with matched/mismatch/black-box/unresolvable findings. Interchange/model-layer only — the
/// imported graph still validates and the populated pools/message flows are metadata the validator ignores.
/// </summary>
public sealed class BpmnCollaborationInterchangeTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BpmnDocumentImporter _importer = new();
    private readonly BpmnDocumentExporter _exporter = new();

    // A two-pool executable collaboration. Buyer throws "order-placed" to the Seller's message start (throw→start);
    // the Seller's sendTask "order-confirmed" reaches the Buyer's message catch (sendTask→catch).
    private const string TwoPoolDocument = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
          <message id="msg-order" name="order-placed" />
          <message id="msg-confirm" name="order-confirmed" />
          <collaboration id="collab">
            <participant id="pool-buyer" name="Buyer" processRef="buyer" />
            <participant id="pool-seller" name="Seller" processRef="seller" />
            <messageFlow id="mf-order" name="Order" sourceRef="buyer-throw" targetRef="seller-start" messageRef="msg-order" />
            <messageFlow id="mf-confirm" name="Confirm" sourceRef="seller-send" targetRef="buyer-catch" messageRef="msg-confirm" />
          </collaboration>
          <process id="buyer" isExecutable="true">
            <laneSet id="buyer-lanes">
              <lane id="lane-buyer" name="Buyer lane">
                <flowNodeRef>buyer-throw</flowNodeRef>
              </lane>
            </laneSet>
            <startEvent id="buyer-start-ev" />
            <intermediateThrowEvent id="buyer-throw">
              <messageEventDefinition messageRef="msg-order" />
            </intermediateThrowEvent>
            <intermediateCatchEvent id="buyer-catch">
              <messageEventDefinition messageRef="msg-confirm" />
            </intermediateCatchEvent>
            <endEvent id="buyer-end" />
            <sequenceFlow id="bf1" sourceRef="buyer-start-ev" targetRef="buyer-throw" />
            <sequenceFlow id="bf2" sourceRef="buyer-throw" targetRef="buyer-catch" />
            <sequenceFlow id="bf3" sourceRef="buyer-catch" targetRef="buyer-end" />
          </process>
          <process id="seller" isExecutable="true">
            <startEvent id="seller-start">
              <messageEventDefinition messageRef="msg-order" />
            </startEvent>
            <sendTask id="seller-send" messageRef="msg-confirm" />
            <endEvent id="seller-end" />
            <sequenceFlow id="sf1" sourceRef="seller-start" targetRef="seller-send" />
            <sequenceFlow id="sf2" sourceRef="seller-send" targetRef="seller-end" />
          </process>
        </definitions>
        """;

    private static BpmnAuthoredStructure StructureOf(ActivityNode node) =>
        node.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

    [Fact]
    public void TwoPoolCollaboration_ImportsBothProcesses_WithPoolsLanesAndWiring()
    {
        var result = _importer.Import(TwoPoolDocument);

        // FR-1: both pools imported; the primary (first executable) is Buyer and equals ProcessNodes[0].Node.
        Assert.Equal(["buyer", "seller"], result.ProcessNodes.Select(process => process.ProcessId));
        Assert.Same(result.ProcessNode, result.ProcessNodes[0].Node);

        var buyer = result.ProcessNodes[0];
        var seller = result.ProcessNodes[1];
        Assert.Equal(("pool-buyer", "Buyer"), (buyer.ParticipantId, buyer.ParticipantName));
        Assert.Equal(("pool-seller", "Seller"), (seller.ParticipantId, seller.ParticipantName));

        var buyerStructure = StructureOf(buyer.Node);
        var sellerStructure = StructureOf(seller.Node);

        // FR-2: pools populated from participants; lanes stamped with the (unambiguous) pool id.
        var buyerPool = Assert.Single(buyerStructure.Pools);
        Assert.Equal(("pool-buyer", "Buyer", "buyer"), (buyerPool.PoolId, buyerPool.Name, buyerPool.ProcessRef));
        Assert.Equal("pool-buyer", Assert.Single(buyerStructure.Lanes).PoolId);
        Assert.Equal("pool-seller", Assert.Single(sellerStructure.Pools).PoolId);

        // FR-3: both flows matched and recorded on BOTH involved structures (throw→start and sendTask→catch).
        var order = Assert.Single(buyerStructure.MessageFlows, flow => flow.FlowId == "mf-order");
        Assert.Equal(("buyer-throw", "pool-buyer", "seller-start", "pool-seller", "order-placed"),
            (order.SourceElementId, order.SourcePoolId, order.TargetElementId, order.TargetPoolId, order.MessageName));
        Assert.Contains(sellerStructure.MessageFlows, flow => flow.FlowId == "mf-order");

        var confirm = Assert.Single(sellerStructure.MessageFlows, flow => flow.FlowId == "mf-confirm");
        Assert.Equal(("seller-send", "pool-seller", "buyer-catch", "pool-buyer", "order-confirmed"),
            (confirm.SourceElementId, confirm.SourcePoolId, confirm.TargetElementId, confirm.TargetPoolId, confirm.MessageName));
        Assert.Contains(buyerStructure.MessageFlows, flow => flow.FlowId == "mf-confirm");

        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Info && issue.ElementId == "mf-order" && issue.Message.Contains("wires", StringComparison.Ordinal));
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Info && issue.ElementId == "mf-confirm");

        // Tripwire 1: the graph still validates with pools/message flows populated (they are metadata the validator ignores).
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(buyerStructure)));
        Assert.NotNull(BpmnGraph.From(BridgeToExecutableNode(sellerStructure)));
    }

    [Fact]
    public void ProcessIdOption_SelectsPrimary_WithoutChangingProcessNodes()
    {
        var result = _importer.Import(TwoPoolDocument, new BpmnImportOptions { ProcessId = "seller" });

        Assert.Same(result.ProcessNode, result.ProcessNodes[1].Node);
        Assert.Equal(["buyer", "seller"], result.ProcessNodes.Select(process => process.ProcessId));
    }

    [Fact]
    public void NameMismatchMessageFlow_DegradesLoudly()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <message id="msg-a" name="ping" />
              <message id="msg-b" name="pong" />
              <collaboration id="c">
                <participant id="pool-a" processRef="a" />
                <participant id="pool-b" processRef="b" />
                <messageFlow id="mf" sourceRef="a-throw" targetRef="b-catch" />
              </collaboration>
              <process id="a" isExecutable="true">
                <startEvent id="a-start" />
                <intermediateThrowEvent id="a-throw"><messageEventDefinition messageRef="msg-a" /></intermediateThrowEvent>
                <endEvent id="a-end" />
                <sequenceFlow id="af1" sourceRef="a-start" targetRef="a-throw" />
                <sequenceFlow id="af2" sourceRef="a-throw" targetRef="a-end" />
              </process>
              <process id="b" isExecutable="true">
                <startEvent id="b-start" />
                <intermediateCatchEvent id="b-catch"><messageEventDefinition messageRef="msg-b" /></intermediateCatchEvent>
                <endEvent id="b-end" />
                <sequenceFlow id="bf1" sourceRef="b-start" targetRef="b-catch" />
                <sequenceFlow id="bf2" sourceRef="b-catch" targetRef="b-end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);

        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Degraded && issue.ElementId == "mf" &&
            issue.Message.Contains("'ping'", StringComparison.Ordinal) && issue.Message.Contains("'pong'", StringComparison.Ordinal) &&
            issue.Message.Contains("differ", StringComparison.Ordinal));
        // A mismatched flow is still recorded (both endpoints are real elements) — the wiring facts are known.
        Assert.Contains(StructureOf(result.ProcessNodes[0].Node).MessageFlows, flow => flow.FlowId == "mf");
    }

    [Fact]
    public void BlackBoxPool_AndBlackBoxFlow_AreInfo()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <message id="msg" name="ping" />
              <collaboration id="c">
                <participant id="pool-a" processRef="a" />
                <participant id="pool-ext" name="External" />
                <messageFlow id="mf" sourceRef="a-send" targetRef="pool-ext" messageRef="msg" />
              </collaboration>
              <process id="a" isExecutable="true">
                <startEvent id="a-start" />
                <sendTask id="a-send" messageRef="msg" />
                <endEvent id="a-end" />
                <sequenceFlow id="af1" sourceRef="a-start" targetRef="a-send" />
                <sequenceFlow id="af2" sourceRef="a-send" targetRef="a-end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);

        Assert.Single(result.ProcessNodes); // the black-box pool imports no process
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Info && issue.ElementId == "pool-ext" && issue.Message.Contains("black-box", StringComparison.Ordinal));
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Info && issue.ElementId == "mf" && issue.Message.Contains("black-box", StringComparison.Ordinal));

        var flow = Assert.Single(StructureOf(result.ProcessNode).MessageFlows);
        Assert.Equal(("a-send", "pool-a"), (flow.SourceElementId, flow.SourcePoolId));
        Assert.Null(flow.TargetElementId);
        Assert.Equal("pool-ext", flow.TargetPoolId);
    }

    [Fact]
    public void UnresolvableParticipantRef_AndFlowRef_Degrade()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <collaboration id="c">
                <participant id="pool-a" processRef="a" />
                <participant id="pool-ghost" processRef="ghost" />
                <messageFlow id="mf-bad" sourceRef="a-send" targetRef="nonexistent" />
              </collaboration>
              <process id="a" isExecutable="true">
                <startEvent id="a-start" />
                <sendTask id="a-send" />
                <endEvent id="a-end" />
                <sequenceFlow id="af1" sourceRef="a-start" targetRef="a-send" />
                <sequenceFlow id="af2" sourceRef="a-send" targetRef="a-end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);

        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Degraded && issue.ElementId == "pool-ghost" && issue.Message.Contains("not a process", StringComparison.Ordinal));
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Degraded && issue.ElementId == "mf-bad" && issue.Message.Contains("recorded nowhere", StringComparison.Ordinal));
        Assert.Empty(StructureOf(result.ProcessNode).MessageFlows); // the unresolvable flow is recorded nowhere
        Assert.Equal("pool-a", Assert.Single(StructureOf(result.ProcessNode).Pools).PoolId); // the resolvable participant still stamps its pool
    }

    [Fact]
    public void NonExecutableProcess_ImportsWithInfoFinding()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="doc-pool" isExecutable="false">
                <startEvent id="s" />
                <endEvent id="e" />
                <sequenceFlow id="f" sourceRef="s" targetRef="e" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);

        Assert.Single(result.ProcessNodes);
        Assert.Contains(result.Analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Info && issue.ElementId == "doc-pool" && issue.Message.Contains("not executable", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleProcessDocument_IsByteIdenticalPrimary_PlusOneEntryList()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <userTask id="task" name="Do" />
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="task" />
                <sequenceFlow id="f2" sourceRef="task" targetRef="end" />
              </process>
            </definitions>
            """;

        var result = _importer.Import(document);

        var only = Assert.Single(result.ProcessNodes);
        Assert.Equal("p", only.ProcessId);
        Assert.Null(only.ParticipantId);
        Assert.Same(result.ProcessNode, only.Node); // no collaboration → no re-materialization

        var structure = StructureOf(result.ProcessNode);
        Assert.Empty(structure.Pools);
        Assert.Empty(structure.MessageFlows);
        // No collaboration findings for a single-process document (byte-identical finding set).
        Assert.DoesNotContain(result.Analysis.Issues, issue => issue.Message.Contains("pool", StringComparison.OrdinalIgnoreCase) || issue.Message.Contains("participant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SinglePoolWithLane_RoundTripsParticipantWrapper()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <collaboration id="c">
                <participant id="pool-a" name="Team A" processRef="a" />
              </collaboration>
              <process id="a" isExecutable="true">
                <laneSet id="ls">
                  <lane id="lane-1" name="Lane 1"><flowNodeRef>task</flowNodeRef></lane>
                </laneSet>
                <startEvent id="start" />
                <userTask id="task" name="Do" />
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="task" />
                <sequenceFlow id="f2" sourceRef="task" targetRef="end" />
              </process>
            </definitions>
            """;

        var node = _importer.Import(document).ProcessNode;
        var xml = _exporter.Export(node);

        Assert.Contains("<collaboration", xml, StringComparison.Ordinal);
        Assert.Contains("<participant", xml, StringComparison.Ordinal);
        Assert.Contains("id=\"pool-a\"", xml, StringComparison.Ordinal);
        Assert.Contains("name=\"Team A\"", xml, StringComparison.Ordinal);

        var reimported = StructureOf(_importer.Import(xml).ProcessNode);
        var pool = Assert.Single(reimported.Pools);
        Assert.Equal("pool-a", pool.PoolId);
        // Lanes themselves are not exported by this slice (a pre-existing exporter limitation), so lane pool ids
        // do not survive the round-trip; the participant/pool wrapper does.
    }

    [Fact]
    public void MessageFlows_ReEmitOnExport()
    {
        var buyerNode = _importer.Import(TwoPoolDocument).ProcessNodes[0].Node;
        var xml = _exporter.Export(buyerNode);

        Assert.Contains("<messageFlow", xml, StringComparison.Ordinal);
        Assert.Contains("id=\"mf-order\"", xml, StringComparison.Ordinal);
        Assert.Contains("sourceRef=\"buyer-throw\"", xml, StringComparison.Ordinal);
        Assert.Contains("targetRef=\"seller-start\"", xml, StringComparison.Ordinal);
        Assert.Contains("messageRef=\"message-order-placed\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_And_Export_AreDeterministic()
    {
        var first = _importer.Import(TwoPoolDocument);
        var second = _importer.Import(TwoPoolDocument);

        Assert.Equal(
            first.Analysis.Issues.Select(issue => (issue.Severity, issue.Message, issue.ElementId)),
            second.Analysis.Issues.Select(issue => (issue.Severity, issue.Message, issue.ElementId)));
        Assert.Equal(
            _exporter.Export(first.ProcessNodes[0].Node),
            _exporter.Export(second.ProcessNodes[0].Node));
        Assert.Equal(
            _exporter.Export(first.ProcessNodes[1].Node),
            _exporter.Export(second.ProcessNodes[1].Node));
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

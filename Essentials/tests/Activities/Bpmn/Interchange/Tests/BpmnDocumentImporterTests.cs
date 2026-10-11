using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

public sealed class BpmnDocumentImporterTests
{
    private const string Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private const string SampleDocument = $$"""
        <definitions xmlns="{{Bpmn}}"
                     xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                     xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                     xmlns:elsa="https://elsa-workflows.io/schemas/bpmn"
                     id="defs-1" targetNamespace="https://example.org">
          <process id="order-process" isExecutable="true">
            <laneSet id="lanes">
              <lane id="lane-sales" name="Sales">
                <flowNodeRef>task-review</flowNodeRef>
              </lane>
            </laneSet>
            <startEvent id="start" name="Order received" />
            <userTask id="task-review" name="Review order" />
            <exclusiveGateway id="gw-decision" default="flow-reject" />
            <task id="task-approve" name="Approve" />
            <task id="task-reject" name="Reject" />
            <subProcess id="sub-fulfil" name="Fulfil">
              <startEvent id="sub-start" />
              <task id="sub-task" />
              <endEvent id="sub-end" />
              <sequenceFlow id="sub-flow-1" sourceRef="sub-start" targetRef="sub-task" />
              <sequenceFlow id="sub-flow-2" sourceRef="sub-task" targetRef="sub-end" />
            </subProcess>
            <boundaryEvent id="boundary-1" attachedToRef="task-review" />
            <endEvent id="end-done" />
            <endEvent id="end-terminated"><terminateEventDefinition /></endEvent>
            <sequenceFlow id="flow-1" sourceRef="start" targetRef="task-review" />
            <sequenceFlow id="flow-2" sourceRef="task-review" targetRef="gw-decision" />
            <sequenceFlow id="flow-approve" sourceRef="gw-decision" targetRef="task-approve" elsa:conditionOutcome="Approved" />
            <sequenceFlow id="flow-reject" sourceRef="gw-decision" targetRef="task-reject">
              <conditionExpression>amount &gt; 1000</conditionExpression>
            </sequenceFlow>
            <sequenceFlow id="flow-3" sourceRef="task-approve" targetRef="sub-fulfil" />
            <sequenceFlow id="flow-4" sourceRef="sub-fulfil" targetRef="end-done" />
            <sequenceFlow id="flow-5" sourceRef="task-reject" targetRef="end-terminated" />
          </process>
          <bpmndi:BPMNDiagram id="diagram">
            <bpmndi:BPMNPlane id="plane" bpmnElement="order-process">
              <bpmndi:BPMNShape id="start-shape" bpmnElement="start">
                <dc:Bounds x="120" y="200" width="36" height="36" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </definitions>
        """;

    private readonly BpmnDocumentImporter _importer = new();

    private BpmnAuthoredStructure ImportStructure(out BpmnImportResult result)
    {
        result = _importer.Import(SampleDocument);
        Assert.Equal(BpmnProcessActivity.StructureKind, result.ProcessNode.Structure?.Kind);
        return result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
    }

    [Fact]
    public void Import_MapsSupportedElementsAndFlows()
    {
        var structure = ImportStructure(out _);

        Assert.Equal(
            ["start", "task-review", "gw-decision", "task-approve", "task-reject", "sub-fulfil", "end-done", "end-terminated"],
            structure.Elements.Select(element => element.ElementId));

        var gateway = structure.Elements.Single(element => element.ElementId == "gw-decision");
        Assert.Equal(BpmnElementTypes.ExclusiveGateway, gateway.ElementType);
        Assert.Equal("flow-reject", gateway.DefaultFlowId);

        var terminate = structure.Elements.Single(element => element.ElementId == "end-terminated");
        Assert.Contains(terminate.EventDefinitions, definition => definition.Type == BpmnEventDefinitionTypes.Terminate);

        var approveFlow = structure.SequenceFlows.Single(flow => flow.FlowId == "flow-approve");
        Assert.Equal("Approved", approveFlow.ConditionOutcome);

        var laneTask = structure.Elements.Single(element => element.ElementId == "task-review");
        Assert.Equal("lane-sales", laneTask.LaneId);
        Assert.Single(structure.Lanes);
    }

    [Fact]
    public void Import_NestsSubprocessAsBoundBpmnProcessChild()
    {
        var structure = ImportStructure(out _);

        var subprocess = structure.Elements.Single(element => element.ElementId == "sub-fulfil");
        Assert.Equal(BpmnElementTypes.SubProcess, subprocess.ElementType);
        Assert.Equal("node-sub-fulfil", subprocess.ChildNodeId);

        var child = Assert.Single(structure.Activities);
        Assert.Equal("node-sub-fulfil", child.NodeId);
        var childStructure = child.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        Assert.Equal(["sub-start", "sub-task", "sub-end"], childStructure.Elements.Select(element => element.ElementId));
        Assert.Equal(2, childStructure.SequenceFlows.Count);
    }

    [Fact]
    public void Import_ReportsDegradedAndDroppedElements()
    {
        ImportStructure(out var result);

        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "boundary-1");
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Degraded && issue.ElementId == "flow-reject");
    }

    [Fact]
    public void Import_PreservesDiagramShapes()
    {
        var structure = ImportStructure(out _);

        Assert.NotNull(structure.Diagram);
        var shape = structure.Diagram!.Value.GetProperty("shapes").GetProperty("start");
        Assert.Equal(120, shape.GetProperty("x").GetDouble());
        Assert.Equal(200, shape.GetProperty("y").GetDouble());
    }

    [Fact]
    public void Analyze_InventoriesProcessesAndElements()
    {
        var analysis = _importer.Analyze(SampleDocument);

        Assert.Equal(["order-process"], analysis.ProcessIds);
        // Inventory spans nested subprocess content too: two top-level end events plus sub-end.
        Assert.Equal(3, analysis.ElementCounts["endEvent"]);
        Assert.True(analysis.ElementCounts["sequenceFlow"] >= 7);
    }

    [Fact]
    public void Import_RejectsNonBpmnDocuments()
    {
        Assert.Throws<BpmnInterchangeException>(() => _importer.Import("<not-bpmn />"));
        Assert.Throws<BpmnInterchangeException>(() => _importer.Import("not xml at all"));
    }
}

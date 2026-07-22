using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

/// <summary>
/// Spec 121 D4 — interchange for multi-instance loop characteristics: a cardinality
/// <c>multiInstanceLoopCharacteristics</c> on a bound subprocess host imports with
/// <c>isSequential</c>/<c>loopCardinality</c> fidelity and round-trips through export→import. Collection mode,
/// a non-integer cardinality, or a host that binds no child on import (a plain task) degrade to a host WITHOUT
/// loop characteristics with a finding (validate-representable: the importer never emits a loop the validator
/// rejects).
/// </summary>
public sealed class BpmnMultiInstanceInterchangeTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BpmnDocumentImporter _importer = new();
    private readonly BpmnDocumentExporter _exporter = new();

    private const string SequentialLoopDocument = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:elsa="https://elsa-workflows.io/schemas/bpmn" id="defs" targetNamespace="https://example.org">
          <process id="p" isExecutable="true">
            <startEvent id="start" />
            <subProcess id="mi-seq">
              <multiInstanceLoopCharacteristics isSequential="true">
                <loopCardinality>3</loopCardinality>
              </multiInstanceLoopCharacteristics>
              <startEvent id="s2" />
              <task id="t2" />
              <endEvent id="e2" />
              <sequenceFlow id="sf1" sourceRef="s2" targetRef="t2" />
              <sequenceFlow id="sf2" sourceRef="t2" targetRef="e2" />
            </subProcess>
            <endEvent id="end" />
            <sequenceFlow id="f1" sourceRef="start" targetRef="mi-seq" />
            <sequenceFlow id="f2" sourceRef="mi-seq" targetRef="end" />
          </process>
        </definitions>
        """;

    [Fact]
    public void Import_ResolvesCardinalityLoop_OnSubprocessHost()
    {
        var structure = Import(SequentialLoopDocument);

        var loop = structure.Elements.Single(element => element.ElementId == "mi-seq").LoopCharacteristics;
        Assert.NotNull(loop);
        Assert.True(loop!.IsSequential);
        Assert.Equal(3, loop.Cardinality);
        Assert.Null(loop.CollectionVariable);
    }

    [Fact]
    public void Import_ParallelLoop_DefaultsIsSequentialFalse()
    {
        var document = SequentialLoopDocument.Replace("isSequential=\"true\"", "");
        var structure = Import(document);

        var loop = structure.Elements.Single(element => element.ElementId == "mi-seq").LoopCharacteristics;
        Assert.NotNull(loop);
        Assert.False(loop!.IsSequential);
        Assert.Equal(3, loop.Cardinality);
    }

    [Fact]
    public void Import_MultiInstanceTask_DegradesToNoLoop_WithFinding()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <task id="mi-task">
                  <multiInstanceLoopCharacteristics><loopCardinality>2</loopCardinality></multiInstanceLoopCharacteristics>
                </task>
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="mi-task" />
                <sequenceFlow id="f2" sourceRef="mi-task" targetRef="end" />
              </process>
            </definitions>
            """;

        var analysis = _importer.Analyze(document);
        Assert.Contains(analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Degraded && issue.ElementId == "mi-task");

        var structure = Import(document);
        Assert.Null(structure.Elements.Single(element => element.ElementId == "mi-task").LoopCharacteristics);
    }

    [Fact]
    public void Import_CollectionMode_DegradesToNoLoop_WithFinding()
    {
        const string document = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:elsa="https://elsa-workflows.io/schemas/bpmn" id="defs" targetNamespace="https://example.org">
              <process id="p" isExecutable="true">
                <startEvent id="start" />
                <subProcess id="mi-seq">
                  <multiInstanceLoopCharacteristics isSequential="false" elsa:collection="items" elsa:itemVariable="item" />
                  <startEvent id="s2" />
                  <task id="t2" />
                  <endEvent id="e2" />
                  <sequenceFlow id="sf1" sourceRef="s2" targetRef="t2" />
                  <sequenceFlow id="sf2" sourceRef="t2" targetRef="e2" />
                </subProcess>
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="mi-seq" />
                <sequenceFlow id="f2" sourceRef="mi-seq" targetRef="end" />
              </process>
            </definitions>
            """;

        var analysis = _importer.Analyze(document);
        Assert.Contains(analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Degraded && issue.ElementId == "mi-seq" && issue.Message.Contains("collection", StringComparison.OrdinalIgnoreCase));

        var structure = Import(document);
        Assert.Null(structure.Elements.Single(element => element.ElementId == "mi-seq").LoopCharacteristics);
    }

    [Fact]
    public void Import_NonIntegerCardinality_DegradesToNoLoop_WithFinding()
    {
        var document = SequentialLoopDocument.Replace("<loopCardinality>3</loopCardinality>", "<loopCardinality>${n}</loopCardinality>");

        var analysis = _importer.Analyze(document);
        Assert.Contains(analysis.Issues, issue => issue.Severity == BpmnImportIssueSeverity.Degraded && issue.ElementId == "mi-seq");

        var structure = Import(document);
        Assert.Null(structure.Elements.Single(element => element.ElementId == "mi-seq").LoopCharacteristics);
    }

    [Fact]
    public void CardinalityLoop_RoundTripsThroughExportImport_WithFidelity()
    {
        var imported = _importer.Import(SequentialLoopDocument).ProcessNode;

        var xml = _exporter.Export(imported);
        Assert.Contains("<multiInstanceLoopCharacteristics", xml, StringComparison.Ordinal);
        Assert.Contains("isSequential=\"true\"", xml, StringComparison.Ordinal);
        Assert.Contains("<loopCardinality>3</loopCardinality>", xml, StringComparison.Ordinal);

        var reimported = _importer.Import(xml).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        var loop = reimported.Elements.Single(element => element.ElementId == "mi-seq").LoopCharacteristics;
        Assert.NotNull(loop);
        Assert.True(loop!.IsSequential);
        Assert.Equal(3, loop.Cardinality);
    }

    private BpmnAuthoredStructure Import(string document) =>
        _importer.Import(document).ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
}

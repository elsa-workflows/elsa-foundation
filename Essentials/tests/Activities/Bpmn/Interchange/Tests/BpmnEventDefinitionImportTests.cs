using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

/// <summary>
/// Spec 118: importing timer/message/signal event definitions on event-defined start events (spec 117)
/// and intermediate catch events (spec 116). Start events import as pure elements carrying a populated
/// <see cref="BpmnEventDefinition"/>; catch events also synthesize a bound suspending child.
/// </summary>
public sealed class BpmnEventDefinitionImportTests
{
    private const string Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BpmnDocumentImporter _importer = new();

    private static string Document(string declarations, string processBody) => $$"""
        <definitions xmlns="{{Bpmn}}" id="defs" targetNamespace="https://example.org">
          {{declarations}}
          <process id="p" isExecutable="true">
            {{processBody}}
          </process>
        </definitions>
        """;

    private BpmnAuthoredStructure Import(string declarations, string processBody, out BpmnImportResult result)
    {
        result = _importer.Import(Document(declarations, processBody));
        return result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
    }

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

    [Fact]
    public void Import_MessageStart_ResolvesNameFromRootDeclaration_AsPureElement()
    {
        var structure = Import(
            declarations: """<message id="msg-order" name="order-placed" />""",
            processBody: """
                <startEvent id="start-msg"><messageEventDefinition messageRef="msg-order" /></startEvent>
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start-msg" targetRef="end" />
                """,
            out var result);

        var start = structure.Elements.Single(element => element.ElementId == "start-msg");
        Assert.Equal(BpmnElementTypes.StartEvent, start.ElementType);
        Assert.Null(start.ChildNodeId); // event-defined start is a pure element (spec 117)
        var definition = Assert.Single(start.EventDefinitions);
        Assert.Equal(BpmnEventDefinitionTypes.Message, definition.Type);
        Assert.Equal("order-placed", definition.Properties[BpmnEventDefinitionProperties.Name]);
        Assert.Empty(structure.Activities);
        Assert.DoesNotContain(result.Analysis.Issues, issue => issue.ElementId == "start-msg");
    }

    [Fact]
    public void Import_SignalStart_ResolvesName()
    {
        var structure = Import(
            declarations: """<signal id="sig-cancel" name="cancel-requested" />""",
            processBody: """
                <startEvent id="start-sig"><signalEventDefinition signalRef="sig-cancel" /></startEvent>
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start-sig" targetRef="end" />
                """,
            out _);

        var definition = Assert.Single(structure.Elements.Single(element => element.ElementId == "start-sig").EventDefinitions);
        Assert.Equal(BpmnEventDefinitionTypes.Signal, definition.Type);
        Assert.Equal("cancel-requested", definition.Properties[BpmnEventDefinitionProperties.Name]);
    }

    [Theory]
    [InlineData("PT5M", BpmnEventDefinitionProperties.Interval, "PT5M")] // ISO-8601 duration
    [InlineData("R5/PT1H", BpmnEventDefinitionProperties.Interval, "PT1H")] // repetition prefix stripped
    [InlineData("0 * * * *", BpmnEventDefinitionProperties.Cron, "0 * * * *")] // cron expression
    public void Import_TimerStart_MapsTimeCycleToIntervalOrCron(string cycle, string expectedKey, string expectedValue)
    {
        var structure = Import(
            declarations: "",
            processBody: $"""
                <startEvent id="start-timer"><timerEventDefinition><timeCycle>{cycle}</timeCycle></timerEventDefinition></startEvent>
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start-timer" targetRef="end" />
                """,
            out _);

        var definition = Assert.Single(structure.Elements.Single(element => element.ElementId == "start-timer").EventDefinitions);
        Assert.Equal(BpmnEventDefinitionTypes.Timer, definition.Type);
        Assert.Equal(expectedValue, definition.Properties[expectedKey]);
    }

    [Theory]
    [InlineData("""<startEvent id="bad"><messageEventDefinition messageRef="missing" /></startEvent>""")] // unresolvable ref
    [InlineData("""<startEvent id="bad"><timerEventDefinition><timeDuration>PT5M</timeDuration></timerEventDefinition></startEvent>""")] // non-recurring
    [InlineData("""<startEvent id="bad"><messageEventDefinition messageRef="m" /><signalEventDefinition signalRef="s" /></startEvent>""")] // multiple
    public void Import_UnresolvableStart_DegradesToNoneStart(string startXml)
    {
        var structure = Import(
            declarations: """<message id="m" name="x" /><signal id="s" name="y" />""",
            processBody: $"""
                {startXml}
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="bad" targetRef="end" />
                """,
            out var result);

        var start = structure.Elements.Single(element => element.ElementId == "bad");
        Assert.Equal(BpmnElementTypes.StartEvent, start.ElementType);
        Assert.Empty(start.EventDefinitions); // degraded to a plain none start
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Degraded && issue.ElementId == "bad");
        // Degraded (not dropped): the element and its flow survive.
        Assert.Single(structure.SequenceFlows);
    }

    [Fact]
    public void Import_TimerCatch_SynthesizesBoundDelayChild()
    {
        var structure = Import(
            declarations: "",
            processBody: """
                <startEvent id="start" />
                <intermediateCatchEvent id="catch-timer"><timerEventDefinition><timeDuration>PT30S</timeDuration></timerEventDefinition></intermediateCatchEvent>
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="catch-timer" />
                <sequenceFlow id="f2" sourceRef="catch-timer" targetRef="end" />
                """,
            out _);

        var catchEvent = structure.Elements.Single(element => element.ElementId == "catch-timer");
        Assert.Equal(BpmnElementTypes.IntermediateCatchEvent, catchEvent.ElementType);
        Assert.Equal("node-catch-timer", catchEvent.ChildNodeId);
        var definition = Assert.Single(catchEvent.EventDefinitions);
        Assert.Equal(BpmnEventDefinitionTypes.Timer, definition.Type);
        Assert.Equal("PT30S", definition.Properties[BpmnEventDefinitionProperties.Interval]);

        var child = Assert.Single(structure.Activities);
        Assert.Equal("node-catch-timer", child.NodeId);
        Assert.Equal(BpmnDocumentImporter.DefaultDelayActivityVersionId, child.ActivityVersionId);
        Assert.Equal("PT30S", Literal(child, "Duration"));
        Assert.Equal(2, structure.SequenceFlows.Count); // both flows retained
    }

    [Theory]
    [InlineData("message", "messageEventDefinition", "messageRef")]
    [InlineData("signal", "signalEventDefinition", "signalRef")]
    public void Import_MessageOrSignalCatch_SynthesizesBoundEventChild(string declarationName, string definitionName, string refAttribute)
    {
        var structure = Import(
            declarations: $"""<{declarationName} id="decl" name="order-shipped" />""",
            processBody: $"""
                <startEvent id="start" />
                <intermediateCatchEvent id="catch-evt"><{definitionName} {refAttribute}="decl" /></intermediateCatchEvent>
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="catch-evt" />
                <sequenceFlow id="f2" sourceRef="catch-evt" targetRef="end" />
                """,
            out _);

        var catchEvent = structure.Elements.Single(element => element.ElementId == "catch-evt");
        Assert.Equal(BpmnElementTypes.IntermediateCatchEvent, catchEvent.ElementType);
        Assert.Equal("node-catch-evt", catchEvent.ChildNodeId);
        Assert.Equal("order-shipped", Assert.Single(catchEvent.EventDefinitions).Properties[BpmnEventDefinitionProperties.Name]);

        var child = Assert.Single(structure.Activities);
        Assert.Equal(BpmnDocumentImporter.DefaultEventActivityVersionId, child.ActivityVersionId);
        Assert.Equal("order-shipped", Literal(child, "EventName"));
        Assert.Equal(false, Literal(child, "CanStartWorkflow")); // a mid-flow catch never starts
    }

    [Theory]
    [InlineData("""<intermediateCatchEvent id="bad"><timerEventDefinition><timeCycle>0 * * * *</timeCycle></timerEventDefinition></intermediateCatchEvent>""")] // recurring on a catch
    [InlineData("""<intermediateCatchEvent id="bad"><messageEventDefinition messageRef="missing" /></intermediateCatchEvent>""")] // unresolvable ref
    [InlineData("""<intermediateCatchEvent id="bad"><linkEventDefinition /></intermediateCatchEvent>""")] // unsupported type
    [InlineData("""<intermediateCatchEvent id="bad" />""")] // no definition
    public void Import_UnbindableCatch_DropsAndCascadesFlows(string catchXml)
    {
        var structure = Import(
            declarations: "",
            processBody: $"""
                <startEvent id="start" />
                {catchXml}
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="bad" />
                <sequenceFlow id="f2" sourceRef="bad" targetRef="end" />
                """,
            out var result);

        Assert.DoesNotContain(structure.Elements, element => element.ElementId == "bad");
        Assert.Empty(structure.Activities);
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId == "bad");
        // Both flows referenced the dropped element and cascade-dropped with it.
        Assert.Empty(structure.SequenceFlows);
        Assert.Contains(result.Analysis.Issues, issue =>
            issue.Severity == BpmnImportIssueSeverity.Dropped && issue.ElementId is "f1" or "f2");
    }

    [Fact]
    public void Analyze_CountsCatchEvents_RegardlessOfDrop()
    {
        var analysis = _importer.Analyze(Document(
            declarations: "",
            processBody: """
                <startEvent id="start" />
                <intermediateCatchEvent id="bad" />
                <endEvent id="end" />
                <sequenceFlow id="f1" sourceRef="start" targetRef="end" />
                """));

        Assert.Equal(1, analysis.ElementCounts["intermediateCatchEvent"]);
    }
}

using System.Text.Json;
using System.Xml.Linq;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Models;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>
/// Emits BPMN 2.0 XML + BPMNDI from a <c>BpmnProcess</c> node's authored structure. Outcome-matched
/// flow conditions export as <c>elsa:conditionOutcome</c> attributes (there is no standard BPMN
/// representation), which the importer reads back for lossless round-trips; the flow also carries a
/// human-readable <c>conditionExpression</c> so other modelers show the condition. Nested
/// <c>BpmnProcess</c> children inline as <c>&lt;subProcess&gt;</c> content. Layout comes from the
/// authored <c>diagram</c> payload; a simple left-to-right grid is synthesized when absent.
/// </summary>
public sealed class BpmnDocumentExporter : IBpmnDocumentExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Export(ActivityNode processNode, BpmnExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(processNode);
        var structure = ReadStructure(processNode);
        var processId = options?.ProcessId ?? SanitizeId(processNode.NodeId);

        var process = new XElement(BpmnXmlNames.Model + "process",
            new XAttribute("id", processId),
            new XAttribute("isExecutable", "true"));
        AppendContainerContent(process, structure);

        var definitions = new XElement(BpmnXmlNames.Model + "definitions",
            new XAttribute("id", $"{processId}-definitions"),
            new XAttribute(XNamespace.Xmlns + "bpmndi", BpmnXmlNames.Di.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dc", BpmnXmlNames.Dc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "di", BpmnXmlNames.Dd.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "elsa", BpmnXmlNames.Elsa.NamespaceName),
            new XAttribute("targetNamespace", BpmnXmlNames.Elsa.NamespaceName),
            process,
            BuildDiagram(processId, structure));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), definitions).ToString();
    }

    private static void AppendContainerContent(XElement container, BpmnAuthoredStructure structure)
    {
        var childrenByNodeId = structure.Activities.ToDictionary(activity => activity.NodeId, StringComparer.Ordinal);

        foreach (var element in structure.Elements)
        {
            var xmlElement = element.ElementType switch
            {
                BpmnElementTypes.StartEvent => new XElement(BpmnXmlNames.Model + "startEvent"),
                BpmnElementTypes.EndEvent => BuildEndEvent(element),
                BpmnElementTypes.SubProcess => BuildSubProcess(element, childrenByNodeId),
                _ => new XElement(BpmnXmlNames.Model + element.ElementType)
            };

            xmlElement.SetAttributeValue("id", element.ElementId);
            if (element.Name is not null) xmlElement.SetAttributeValue("name", element.Name);
            if (element.DefaultFlowId is not null) xmlElement.SetAttributeValue("default", element.DefaultFlowId);
            container.Add(xmlElement);
        }

        foreach (var flow in structure.SequenceFlows)
        {
            var flowElement = new XElement(BpmnXmlNames.Model + "sequenceFlow",
                new XAttribute("id", flow.FlowId),
                new XAttribute("sourceRef", flow.SourceRef),
                new XAttribute("targetRef", flow.TargetRef));
            if (flow.Name is not null) flowElement.SetAttributeValue("name", flow.Name);
            if (flow.ConditionOutcome is not null)
            {
                flowElement.SetAttributeValue(BpmnXmlNames.Elsa + "conditionOutcome", flow.ConditionOutcome);
                flowElement.Add(new XElement(BpmnXmlNames.Model + "conditionExpression", $"outcome == '{flow.ConditionOutcome}'"));
            }

            container.Add(flowElement);
        }
    }

    private static XElement BuildEndEvent(BpmnElement element)
    {
        var endEvent = new XElement(BpmnXmlNames.Model + "endEvent");
        if (element.EventDefinitions.Any(definition => StringComparer.Ordinal.Equals(definition.Type, BpmnEventDefinitionTypes.Terminate)))
            endEvent.Add(new XElement(BpmnXmlNames.Model + "terminateEventDefinition"));
        return endEvent;
    }

    private static XElement BuildSubProcess(BpmnElement element, IReadOnlyDictionary<string, ActivityNode> childrenByNodeId)
    {
        var subProcess = new XElement(BpmnXmlNames.Model + "subProcess");

        // A nested BpmnProcess child inlines as subprocess content; any other bound activity has no
        // BPMN representation for its internals, so the subprocess exports empty (the binding is an
        // Elsa concern the importer re-establishes).
        if (element.ChildNodeId is { } childNodeId
            && childrenByNodeId.TryGetValue(childNodeId, out var child)
            && StringComparer.Ordinal.Equals(child.Structure?.Kind, BpmnProcessActivity.StructureKind))
        {
            AppendContainerContent(subProcess, ReadStructure(child));
        }

        return subProcess;
    }

    private static XElement BuildDiagram(string processId, BpmnAuthoredStructure structure)
    {
        var plane = new XElement(BpmnXmlNames.Di + "BPMNPlane",
            new XAttribute("id", $"{processId}-plane"),
            new XAttribute("bpmnElement", processId));

        var shapes = ReadDiagramShapes(structure);
        var index = 0;
        foreach (var element in structure.Elements)
        {
            var bounds = shapes.TryGetValue(element.ElementId, out var shape)
                ? shape
                : SynthesizeBounds(element, index);
            index++;

            plane.Add(new XElement(BpmnXmlNames.Di + "BPMNShape",
                new XAttribute("id", $"{element.ElementId}-shape"),
                new XAttribute("bpmnElement", element.ElementId),
                new XElement(BpmnXmlNames.Dc + "Bounds",
                    new XAttribute("x", bounds.X),
                    new XAttribute("y", bounds.Y),
                    new XAttribute("width", bounds.Width),
                    new XAttribute("height", bounds.Height))));
        }

        return new XElement(BpmnXmlNames.Di + "BPMNDiagram",
            new XAttribute("id", $"{processId}-diagram"),
            plane);
    }

    private readonly record struct ShapeBounds(double X, double Y, double Width, double Height);

    private static Dictionary<string, ShapeBounds> ReadDiagramShapes(BpmnAuthoredStructure structure)
    {
        var result = new Dictionary<string, ShapeBounds>(StringComparer.Ordinal);
        if (structure.Diagram is not { } diagram || diagram.ValueKind != JsonValueKind.Object) return result;
        if (!diagram.TryGetProperty("shapes", out var shapes) || shapes.ValueKind != JsonValueKind.Object) return result;

        foreach (var shape in shapes.EnumerateObject())
        {
            if (shape.Value.ValueKind != JsonValueKind.Object) continue;
            result[shape.Name] = new ShapeBounds(
                ReadNumber(shape.Value, "x"),
                ReadNumber(shape.Value, "y"),
                ReadNumber(shape.Value, "width", 100),
                ReadNumber(shape.Value, "height", 80));
        }

        return result;
    }

    private static double ReadNumber(JsonElement element, string property, double fallback = 0) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : fallback;

    private static ShapeBounds SynthesizeBounds(BpmnElement element, int index)
    {
        var isEvent = StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.StartEvent)
                      || StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.EndEvent);
        var isGateway = element.ElementType.EndsWith("Gateway", StringComparison.Ordinal);
        var (width, height) = isEvent ? (36d, 36d) : isGateway ? (50d, 50d) : (100d, 80d);
        return new ShapeBounds(100 + index * 180, 100, width, height);
    }

    private static BpmnAuthoredStructure ReadStructure(ActivityNode processNode)
    {
        if (!StringComparer.Ordinal.Equals(processNode.Structure?.Kind, BpmnProcessActivity.StructureKind))
            throw new BpmnInterchangeException($"Activity node '{processNode.NodeId}' does not carry an '{BpmnProcessActivity.StructureKind}' structure.");

        return processNode.Structure.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)
               ?? throw new BpmnInterchangeException($"Activity node '{processNode.NodeId}' structure resolved to null.");
    }

    private static string SanitizeId(string value)
    {
        var sanitized = new string(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "process" : sanitized;
    }
}

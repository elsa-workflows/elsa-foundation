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
/// <c>BpmnProcess</c> children inline as <c>&lt;subProcess&gt;</c> content. Event-defined start events and
/// intermediate catch events emit their timer/message/signal event definitions from the populated
/// <c>BpmnEventDefinition</c> properties (spec 118); message/signal names emit a deduped root
/// <c>&lt;message&gt;</c>/<c>&lt;signal&gt;</c> declaration each. Layout comes from the authored
/// <c>diagram</c> payload; a simple left-to-right grid is synthesized when absent.
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
            new XAttribute("targetNamespace", BpmnXmlNames.Elsa.NamespaceName));

        // Root-level message/signal declarations (deduped by name) precede the process, matching the
        // importer's global id→name index the messageRef/signalRef resolve through.
        var declarations = new Dictionary<string, (string Type, string Name)>(StringComparer.Ordinal);
        CollectMessageSignalDeclarations(structure, declarations);
        foreach (var declaration in declarations.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            definitions.Add(new XElement(
                BpmnXmlNames.Model + (declaration.Value.Type == BpmnEventDefinitionTypes.Message ? "message" : "signal"),
                new XAttribute("id", declaration.Key),
                new XAttribute("name", declaration.Value.Name)));

        definitions.Add(process);
        definitions.Add(BuildDiagram(processId, structure));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), definitions).ToString();
    }

    private static void AppendContainerContent(XElement container, BpmnAuthoredStructure structure)
    {
        var childrenByNodeId = structure.Activities.ToDictionary(activity => activity.NodeId, StringComparer.Ordinal);

        foreach (var element in structure.Elements)
        {
            var xmlElement = element.ElementType switch
            {
                BpmnElementTypes.StartEvent => BuildEventElement("startEvent", element, isCatch: false),
                BpmnElementTypes.IntermediateCatchEvent => BuildEventElement("intermediateCatchEvent", element, isCatch: true),
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

    /// <summary>
    /// Builds an event-defined start or intermediate catch event, emitting its timer/message/signal event
    /// definition from the populated <see cref="BpmnEventDefinition"/> properties (spec 118 D4). A catch
    /// event's bound child (<c>Delay</c>/<c>Event</c>) is engine detail and is not exported.
    /// </summary>
    private static XElement BuildEventElement(string localName, BpmnElement element, bool isCatch)
    {
        var xmlElement = new XElement(BpmnXmlNames.Model + localName);
        if (element.EventDefinitions.FirstOrDefault() is { } definition)
            AppendEventDefinition(xmlElement, definition, isCatch);
        return xmlElement;
    }

    private static void AppendEventDefinition(XElement host, BpmnEventDefinition definition, bool isCatch)
    {
        switch (definition.Type)
        {
            case BpmnEventDefinitionTypes.Message:
            case BpmnEventDefinitionTypes.Signal:
            {
                if (!definition.Properties.TryGetValue(BpmnEventDefinitionProperties.Name, out var name) || string.IsNullOrWhiteSpace(name))
                    return;
                var isMessage = definition.Type == BpmnEventDefinitionTypes.Message;
                host.Add(new XElement(
                    BpmnXmlNames.Model + (isMessage ? "messageEventDefinition" : "signalEventDefinition"),
                    new XAttribute(isMessage ? "messageRef" : "signalRef", MessageSignalDeclarationId(definition.Type, name))));
                break;
            }
            case BpmnEventDefinitionTypes.Timer:
            {
                var timer = new XElement(BpmnXmlNames.Model + "timerEventDefinition");
                if (isCatch)
                {
                    // A catch timer is a one-shot relative delay → <timeDuration>.
                    if (!definition.Properties.TryGetValue(BpmnEventDefinitionProperties.Interval, out var interval) || string.IsNullOrWhiteSpace(interval))
                        return;
                    timer.Add(new XElement(BpmnXmlNames.Model + "timeDuration", interval.Trim()));
                }
                else if (definition.Properties.TryGetValue(BpmnEventDefinitionProperties.Cron, out var cron) && !string.IsNullOrWhiteSpace(cron))
                {
                    // A recurring start schedule → <timeCycle>; a cron expression round-trips as-is (not P/R-prefixed).
                    timer.Add(new XElement(BpmnXmlNames.Model + "timeCycle", cron.Trim()));
                }
                else if (definition.Properties.TryGetValue(BpmnEventDefinitionProperties.Interval, out var interval) && !string.IsNullOrWhiteSpace(interval))
                {
                    // A recurring start interval → <timeCycle> as an ISO-8601 duration (round-trips via the P/R discriminator).
                    timer.Add(new XElement(BpmnXmlNames.Model + "timeCycle", interval.Trim()));
                }
                else
                {
                    return;
                }

                host.Add(timer);
                break;
            }
        }
    }

    /// <summary>Collects the distinct message/signal event names referenced by start/catch elements across the whole structure tree, keyed by their deterministic root-declaration id.</summary>
    private static void CollectMessageSignalDeclarations(BpmnAuthoredStructure structure, Dictionary<string, (string Type, string Name)> declarationsById)
    {
        foreach (var definition in structure.Elements.SelectMany(element => element.EventDefinitions))
        {
            if (definition.Type is not (BpmnEventDefinitionTypes.Message or BpmnEventDefinitionTypes.Signal))
                continue;
            if (!definition.Properties.TryGetValue(BpmnEventDefinitionProperties.Name, out var name) || string.IsNullOrWhiteSpace(name))
                continue;
            declarationsById[MessageSignalDeclarationId(definition.Type, name)] = (definition.Type, name.Trim());
        }

        foreach (var child in structure.Activities.Where(child => StringComparer.Ordinal.Equals(child.Structure?.Kind, BpmnProcessActivity.StructureKind)))
            CollectMessageSignalDeclarations(ReadStructure(child), declarationsById);
    }

    /// <summary>The deterministic root-declaration id for a message/signal name (the prefix keeps message and signal namespaces disjoint).</summary>
    private static string MessageSignalDeclarationId(string type, string name) =>
        $"{(type == BpmnEventDefinitionTypes.Message ? "message" : "signal")}-{SanitizeId(name.Trim())}";

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
                      || StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.EndEvent)
                      || StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.IntermediateCatchEvent);
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

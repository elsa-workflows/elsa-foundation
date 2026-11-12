using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>Closed direct-profile source admission before any XML field is reduced to the released direct DTO.</summary>
internal static class BpmnCompleteSourceDirectProjection
{
    private static readonly XNamespace Model = BpmnDocumentStructureHandler.ModelNamespace;
    private static readonly XNamespace Diagram = "http://www.omg.org/spec/BPMN/20100524/DI";
    private static readonly XNamespace Coordinates = "http://www.omg.org/spec/DD/20100524/DI";
    private static readonly XNamespace Bounds = "http://www.omg.org/spec/DD/20100524/DC";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly HashSet<string> Nodes = new(StringComparer.Ordinal)
        { "startEvent", "endEvent", "userTask", "serviceTask", "parallelGateway", "boundaryEvent" };

    internal static BpmnAuthoredStructure Read(ActivityNode owner)
    {
        BpmnAuthoredDocument source;
        XDocument document;
        try
        {
            source = BpmnDocumentStructureHandler.Read(owner);
            document = BpmnDocumentStructureHandler.ParseXml(source.DocumentXml);
        }
        catch (InvalidOperationException exception)
        { throw new BpmnModelValidationException(exception.Message, exception); }
        return ReadCore(source, document);
    }

    private static BpmnAuthoredStructure ReadCore(BpmnAuthoredDocument source, XDocument document)
    {
        var definitions = document.Root!;
        var processes = definitions.Elements(Model + "process").ToArray();
        if (processes.Length != 1 || (string?)processes[0].Attribute("id") != source.ProcessId ||
            (string?)processes[0].Attribute("isExecutable") is not ("true" or "1"))
            throw new BpmnModelValidationException("The complete-source direct profile requires exactly its selected executable process.");
        var process = processes[0];
        try { BpmnDirectBindingMetadata.ValidateSourceAttributes(document); }
        catch (BpmnInterchangeException exception)
        { throw new BpmnModelValidationException(exception.Message, exception); }
        foreach (var instruction in document.DescendantNodes().OfType<XProcessingInstruction>())
            throw new BpmnModelValidationException($"Complete-source direct publication cannot interpret processing instruction '{instruction.Target}'.");

        // Assess every element, attribute and nonblank text node, including all presentation descendants.
        // Neither a warning-free typed reader nor the current runtime whitelist may hide source syntax.
        foreach (var element in definitions.DescendantsAndSelf()) Assess(element);
        if (source.Activities.Count != 0 || source.Bindings.Count != 0 || source.ScopeHeaders.Count != 0 || source.Variables.Count != 0 ||
            source.AdditionalProperties is { Count: > 0 })
            throw new BpmnModelValidationException("The complete-source direct profile requires a closed process without native children, bindings, scope headers, variables or unqualified payload metadata.");
        var index = definitions.DescendantsAndSelf().Where(element => element.Attribute("id") is not null)
            .ToDictionary(element => element.Attribute("id")!.Value, StringComparer.Ordinal);
        var flowNodes = process.Elements().Where(element => element.Name.Namespace == Model && Nodes.Contains(element.Name.LocalName)).ToArray();
        var flows = process.Elements(Model + "sequenceFlow").ToArray();
        var laneByNode = new Dictionary<string, string>(StringComparer.Ordinal);
        var lanes = process.Elements(Model + "laneSet").Elements(Model + "lane").Select(lane =>
        {
            foreach (var reference in lane.Elements(Model + "flowNodeRef"))
            {
                var id = Resolve(reference, reference.Value);
                if (!flowNodes.Any(node => Id(node) == id) || !laneByNode.TryAdd(id, Id(lane)))
                    Refuse(reference, "lane membership must identify one actual flow node exactly once");
            }
            return new BpmnLane(Id(lane), name: Name(lane));
        }).ToArray();
        var usedErrors = new HashSet<string>(StringComparer.Ordinal);
        var elements = flowNodes.Select(element =>
        {
            var id = Id(element);
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            var metadata = element.Attributes().SingleOrDefault(DirectAttribute);
            if (metadata is not null) properties[BpmnDirectBindingMetadata.PropertyKey] = metadata.Value;
            var events = element.Elements(Model + "errorEventDefinition").ToArray();
            if (element.Name.LocalName == "boundaryEvent" && events.Length != 1 || element.Name.LocalName != "boundaryEvent" && events.Length != 0)
                Refuse(element, "only an existing coded service error boundary is represented");
            var definitionsForEvent = events.Select(error =>
            {
                var reference = Resolve(error, Required(error, "errorRef"));
                if (!index.TryGetValue(reference, out var declaration) || declaration.Name != Model + "error" || declaration.Parent != definitions)
                    Refuse(error, "errorRef must resolve to an actual definitions-level error declaration");
                usedErrors.Add(reference);
                return new BpmnEventDefinition(BpmnEventDefinitionTypes.Error,
                    new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = Required(declaration!, "errorCode") });
            }).ToArray();
            var attached = element.Attribute("attachedToRef") is { } host ? Resolve(element, host.Value) : null;
            if (attached is not null && !flowNodes.Any(node => Id(node) == attached && node.Name == Model + "serviceTask"))
                Refuse(element, "the coded boundary must attach to this process's direct service task");
            var defaultFlow = element.Attribute("default") is { } fallback ? Resolve(element, fallback.Value) : null;
            if (defaultFlow is not null && !flows.Any(flow => Id(flow) == defaultFlow && Resolve(flow, Required(flow, "sourceRef")) == id))
                Refuse(element, "the default must identify this node's actual outgoing sequence flow");
            foreach (var reference in element.Elements().Where(child => child.Name == Model + "incoming" || child.Name == Model + "outgoing"))
                if (!flows.Any(flow => Id(flow) == Resolve(reference, reference.Value) && Resolve(flow,
                    Required(flow, reference.Name.LocalName == "incoming" ? "targetRef" : "sourceRef")) == id))
                    Refuse(reference, "flow-node references must identify actual incoming/outgoing sequence flows");
            if (element.Elements().Where(child => child.Name == Model + "incoming" || child.Name == Model + "outgoing")
                .GroupBy(child => (child.Name, Value: Resolve(child, child.Value))).Any(group => group.Count() != 1))
                Refuse(element, "duplicate incoming/outgoing references are not represented");
            return new BpmnElement(id, element.Name.LocalName, Name(element), laneId: laneByNode.GetValueOrDefault(id),
                defaultFlowId: defaultFlow, eventDefinitions: definitionsForEvent, properties: properties,
                attachedToRef: attached, cancelActivity: Boolean(element, "cancelActivity", true));
        }).ToArray();
        foreach (var error in definitions.Elements(Model + "error"))
            if (!usedErrors.Contains(Id(error))) Refuse(error, "an unused executable root declaration cannot be discarded during direct projection");
        var sequenceFlows = flows.Select(flow =>
        {
            var from = Resolve(flow, Required(flow, "sourceRef")); var to = Resolve(flow, Required(flow, "targetRef"));
            if (!elements.Any(element => element.ElementId == from && element.ElementType != "endEvent") ||
                !elements.Any(element => element.ElementId == to && element.ElementType != "startEvent"))
                Refuse(flow, "sequence flow endpoints must resolve to flow nodes in this exact process");
            var outcome = flow.Attribute(BpmnVendorNamespace.Elsa + "conditionOutcome")?.Value;
            if (outcome is not null && (string.IsNullOrWhiteSpace(outcome) || outcome != outcome.Trim())) Refuse(flow, "outcome names must be exact nonblank names");
            var isDefault = elements.Any(element => element.DefaultFlowId == Id(flow));
            if (isDefault && outcome is not null) Refuse(flow, "a default flow cannot declare a condition outcome");
            return new BpmnSequenceFlow(Id(flow), from, to, Name(flow), outcome, isDefault);
        }).ToArray();
        return new(elements: elements, sequenceFlows: sequenceFlows, lanes: lanes, diagram: ReadDiagram());

        string Resolve(XElement context, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim()) Refuse(context, "references must be exact nonblank identities");
            var parts = value.Split(':');
            if (parts.Length == 1) return value;
            var targetNamespace = (string?)definitions.Attribute("targetNamespace");
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0 &&
                !string.IsNullOrEmpty(targetNamespace) && context.GetNamespaceOfPrefix(parts[0])?.NamespaceName == targetNamespace) return parts[1];
            Refuse(context, "reference QName does not identify this document's target namespace"); return null!;
        }
        void Assess(XElement element)
        {
            var name = element.Name.LocalName; var parent = element.Parent;
            string[] allowed;
            if (element.Name.Namespace == Model)
            {
                var valid = name switch
                {
                    "definitions" => parent is null,
                    "process" => parent == definitions,
                    "error" => parent == definitions,
                    "sequenceFlow" or "laneSet" => parent == process,
                    "lane" => parent?.Name == Model + "laneSet",
                    "flowNodeRef" => parent?.Name == Model + "lane",
                    "incoming" or "outgoing" => parent?.Name.Namespace == Model && Nodes.Contains(parent.Name.LocalName),
                    "errorEventDefinition" => parent?.Name == Model + "boundaryEvent",
                    "documentation" => parent?.Name.Namespace == Model && parent.Name.LocalName is "definitions" or "process" or "startEvent" or "endEvent" or "userTask" or "serviceTask" or "parallelGateway" or "boundaryEvent" or "sequenceFlow" or "lane" or "laneSet" or "error",
                    _ => Nodes.Contains(name) && parent == process
                };
                if (!valid) Refuse(element, $"'{name}' has no qualified direct source/containment mapping");
                allowed = name switch
                {
                    "definitions" => ["id", "name", "targetNamespace", "exporter", "exporterVersion"],
                    "process" => ["id", "name", "isExecutable"],
                    "sequenceFlow" => ["id", "name", "sourceRef", "targetRef"],
                    "userTask" or "serviceTask" => ["id", "name", "default"],
                    "boundaryEvent" => ["id", "name", "attachedToRef", "cancelActivity"],
                    "errorEventDefinition" => ["id", "errorRef"],
                    "error" => ["id", "name", "errorCode"],
                    "documentation" => ["id", "textFormat"],
                    "incoming" or "outgoing" or "flowNodeRef" => [],
                    _ => ["id", "name"]
                };
                if (name != "definitions" && name is not ("documentation" or "incoming" or "outgoing" or "flowNodeRef" or "errorEventDefinition")) _ = Id(element);
                _ = Name(element);
            }
            else if (element.Name.Namespace == Diagram)
            {
                var valid = name switch
                {
                    "BPMNDiagram" => parent == definitions,
                    "BPMNPlane" or "BPMNLabelStyle" => parent?.Name == Diagram + "BPMNDiagram",
                    "BPMNShape" or "BPMNEdge" => parent?.Name == Diagram + "BPMNPlane",
                    "BPMNLabel" => parent?.Name == Diagram + "BPMNShape" || parent?.Name == Diagram + "BPMNEdge",
                    _ => false
                };
                if (!valid) Refuse(element, "presentation must have an exact non-executable DI owner");
                allowed = name switch
                {
                    "BPMNDiagram" => ["id", "name", "documentation", "resolution"],
                    "BPMNPlane" => ["id", "bpmnElement"],
                    "BPMNShape" => ["id", "bpmnElement", "isHorizontal", "isExpanded", "isMarkerVisible"],
                    "BPMNEdge" => ["id", "bpmnElement", "sourceElement", "targetElement"],
                    "BPMNLabel" => ["id", "labelStyle"],
                    _ => ["id"]
                };
            }
            else if (element.Name.Namespace == Bounds && name == "Bounds" && (parent?.Name == Diagram + "BPMNShape" || parent?.Name == Diagram + "BPMNLabel")) allowed = ["x", "y", "width", "height"];
            else if (element.Name.Namespace == Bounds && name == "Font" && parent?.Name == Diagram + "BPMNLabelStyle") allowed = ["name", "size", "isBold", "isItalic", "isUnderline", "isStrikeThrough"];
            else if (element.Name.Namespace == Coordinates && name == "waypoint" && parent?.Name == Diagram + "BPMNEdge") allowed = ["x", "y"];
            else { Refuse(element, $"'{element.Name}' has no qualified direct source mapping"); return; }
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || attribute.Name.NamespaceName.Length == 0 && allowed.Contains(attribute.Name.LocalName, StringComparer.Ordinal)) continue;
                if (element == definitions && attribute.Name == Xsi + "schemaLocation") continue;
                if (name is "userTask" or "serviceTask" && DirectAttribute(attribute)) continue;
                if (element.Name == Model + "sequenceFlow" && attribute.Name == BpmnVendorNamespace.Elsa + "conditionOutcome") continue;
                if (element.Name == Coordinates + "waypoint" && attribute.Name == Xsi + "type" && attribute.Value.Split(':') is [var prefix, "Point"] && prefix.Length > 0 && element.GetNamespaceOfPrefix(prefix) == Bounds) continue;
                Refuse(element, $"attribute '{attribute.Name}' has no qualified direct mapping");
            }
            if (element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)) &&
                !(element.Name.Namespace == Model && name is "incoming" or "outgoing" or "flowNodeRef" or "documentation"))
                Refuse(element, "nonblank source text has no qualified direct mapping");
        }
        JsonElement? ReadDiagram()
        {
            var diagrams = definitions.Elements(Diagram + "BPMNDiagram").ToArray();
            if (diagrams.Length == 0) return null;
            if (diagrams.Length != 1 || diagrams[0].Elements(Diagram + "BPMNPlane").ToArray() is not [var plane] || Resolve(plane, Required(plane, "bpmnElement")) != source.ProcessId)
                throw new BpmnModelValidationException("The direct profile requires one selected process DI plane.");
            var diagram = diagrams[0];
            if (diagram.Attribute("resolution") is not null && Number(diagram, "resolution") <= 0) Refuse(diagram, "DI resolution must be positive");
            foreach (var element in diagram.DescendantsAndSelf())
            {
                if (element.Name.Namespace == Diagram && element.Name.LocalName != "BPMNLabel") _ = Id(element);
                if (element.Name == Bounds + "Bounds")
                {
                    _ = Number(element, "x"); _ = Number(element, "y");
                    if (Number(element, "width") < 0 || Number(element, "height") < 0) Refuse(element, "DI bounds cannot have negative dimensions");
                }
                if (element.Name == Bounds + "Font")
                {
                    _ = Required(element, "name");
                    if (Number(element, "size") <= 0) Refuse(element, "font size must be positive");
                    foreach (var flag in new[] { "isBold", "isItalic", "isUnderline", "isStrikeThrough" }) _ = Boolean(element, flag, false);
                }
                if (element.Name == Diagram + "BPMNLabel")
                {
                    if (element.Elements(Bounds + "Bounds").Count() != 1) Refuse(element, "a label requires one exact bounds record");
                    if (element.Attribute("labelStyle") is { } style && (!index.TryGetValue(Resolve(element, style.Value), out var target) ||
                        target.Name != Diagram + "BPMNLabelStyle" || target.Parent != diagram))
                        Refuse(element, "labelStyle must resolve to this diagram's actual label style");
                }
                if (element.Name == Diagram + "BPMNLabelStyle" && element.Elements(Bounds + "Font").Count() != 1)
                    Refuse(element, "a label style requires one exact font record");
                if (element.Name == Diagram + "BPMNShape")
                    foreach (var flag in new[] { "isHorizontal", "isExpanded", "isMarkerVisible" }) _ = Boolean(element, flag, false);
                if ((element.Name == Diagram + "BPMNShape" || element.Name == Diagram + "BPMNEdge") && element.Elements(Diagram + "BPMNLabel").Count() > 1)
                    Refuse(element, "an element cannot project ambiguous labels");
            }
            var shapes = new Dictionary<string, object>(StringComparer.Ordinal);
            var edges = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var shape in plane.Elements(Diagram + "BPMNShape"))
            {
                var target = Resolve(shape, Required(shape, "bpmnElement"));
                if (!flowNodes.Any(node => Id(node) == target) && !lanes.Any(lane => lane.LaneId == target) || shape.Elements(Bounds + "Bounds").ToArray() is not [var box] ||
                    !shapes.TryAdd(target, new { x = Number(box, "x"), y = Number(box, "y"), width = Number(box, "width"), height = Number(box, "height") }))
                    Refuse(shape, "a DI shape must identify one actual semantic element and exact bounds");
            }
            foreach (var edge in plane.Elements(Diagram + "BPMNEdge"))
            {
                var target = Resolve(edge, Required(edge, "bpmnElement"));
                var points = edge.Elements(Coordinates + "waypoint").Select(point => new { x = Number(point, "x"), y = Number(point, "y") }).ToArray();
                if (!flows.Any(flow => Id(flow) == target) || points.Length < 2 || !edges.TryAdd(target, new { waypoints = points }))
                    Refuse(edge, "a DI edge must identify one actual sequence flow and at least two waypoints");
                var flow = flows.Single(flow => Id(flow) == target);
                foreach (var (attribute, endpoint) in new[] { ("sourceElement", "sourceRef"), ("targetElement", "targetRef") })
                    if (edge.Attribute(attribute) is { } reference && (!index.TryGetValue(Resolve(edge, reference.Value), out var shape) ||
                        shape.Name != Diagram + "BPMNShape" || shape.Parent != plane || Resolve(shape, Required(shape, "bpmnElement")) != Resolve(flow, Required(flow, endpoint))))
                        Refuse(edge, $"{attribute} must identify this sequence flow's actual endpoint DI shape");
            }
            // This is derived runtime layout provenance, never another authored source field. Retain
            // every admitted label/style/flag plus QName context that the legacy view map cannot render.
            var namespaceBindings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var ancestor in diagram.AncestorsAndSelf().Reverse())
                foreach (var attribute in ancestor.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
                    namespaceBindings[attribute.Name.LocalName == "xmlns" ? "" : attribute.Name.LocalName] = attribute.Value;
            var sourceDiagram = new { targetNamespace = (string?)definitions.Attribute("targetNamespace"), namespaceBindings, xml = diagram.ToString(SaveOptions.DisableFormatting) };
            return JsonSerializer.SerializeToElement(new { shapes, edges, sourceDiagram });
        }
    }

    private static bool DirectAttribute(XAttribute attribute) => attribute.Name.LocalName == "directBinding" &&
        (attribute.Name.NamespaceName == BpmnVendorNamespace.NamespaceName || attribute.Name.NamespaceName == global::Bpmn.Interchange.BpmnXmlNames.VendorNamespaceName);
    private static string Id(XElement element) => Required(element, "id");
    private static string Required(XElement element, string name) => element.Attribute(name) is { Value: { Length: > 0 } value } && value == value.Trim()
        ? value : throw new BpmnModelValidationException($"Complete-source element '{(string?)element.Attribute("id") ?? element.Name.LocalName}' requires exact '{name}'.");
    private static string? Name(XElement element)
    {
        var name = (string?)element.Attribute("name");
        if (name is not null && (string.IsNullOrWhiteSpace(name) || name != name.Trim())) Refuse(element, "names cannot require trimming during direct projection");
        return name;
    }
    private static bool Boolean(XElement element, string name, bool fallback) => element.Attribute(name)?.Value switch
    { null => fallback, "true" or "1" => true, "false" or "0" => false, _ => throw new BpmnModelValidationException($"Element '{Id(element)}' has invalid '{name}'.") };
    private static double Number(XElement element, string name) => double.TryParse(Required(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
        ? number : throw new BpmnModelValidationException($"Direct DI '{element.Name.LocalName}' requires finite '{name}'.");
    private static void Refuse(XElement element, string reason) => throw new BpmnModelValidationException($"Complete-source direct element '{(string?)element.Attribute("id") ?? element.Name.LocalName}': {reason}.");
}

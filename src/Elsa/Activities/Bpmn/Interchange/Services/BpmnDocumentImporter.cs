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
/// Maps BPMN 2.0 XML onto the native <c>elsa.bpmn.structure</c> authored payload. Supported elements
/// (Phase 1 subset) import cleanly; event-defined start events degrade to none start events; expression
/// flow conditions degrade to unconditional flows (reported); unsupported flow nodes are dropped with
/// an issue. An embedded <c>subProcess</c> becomes a nested <c>BpmnProcess</c> activity node bound by
/// the subprocess element, mirroring the runtime module's composition model. BPMNDI shapes/edges are
/// preserved on the authored <c>diagram</c> payload for lossless layout round-trips.
/// </summary>
public sealed class BpmnDocumentImporter : IBpmnDocumentImporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Placeholder activity version id for nested BPMN process nodes; API hosts override via options later.</summary>
    public const string DefaultBpmnProcessActivityVersionId = "Elsa.BpmnProcess";

    public BpmnImportAnalysis Analyze(string xml, BpmnImportOptions? options = null)
    {
        var context = new ImportContext();
        ImportCore(xml, options, context);
        return context.ToAnalysis();
    }

    public BpmnImportResult Import(string xml, BpmnImportOptions? options = null)
    {
        var context = new ImportContext();
        var node = ImportCore(xml, options, context);
        return new BpmnImportResult(node, context.ToAnalysis());
    }

    private static ActivityNode ImportCore(string xml, BpmnImportOptions? options, ImportContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new BpmnInterchangeException("The document is not well-formed XML.", exception);
        }

        var definitions = document.Root;
        if (definitions is null || definitions.Name != BpmnXmlNames.Model + "definitions")
            throw new BpmnInterchangeException("The document root is not a BPMN 2.0 <definitions> element.");

        var processes = definitions.Elements(BpmnXmlNames.Model + "process").ToArray();
        foreach (var candidate in processes)
            context.ProcessIds.Add(IdOf(candidate) ?? "(no id)");
        if (processes.Length == 0)
            throw new BpmnInterchangeException("The document contains no <process> element.");

        var process = options?.ProcessId is { } processId
            ? processes.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(IdOf(candidate), processId))
              ?? throw new BpmnInterchangeException($"The document contains no process with id '{processId}'.")
            : processes.FirstOrDefault(candidate => string.Equals((string?)candidate.Attribute("isExecutable"), "true", StringComparison.OrdinalIgnoreCase))
              ?? processes[0];

        var diagram = ReadDiagram(definitions);
        var processIdValue = IdOf(process) ?? "process";
        var nodeId = $"{options?.NodeIdPrefix ?? "node"}-{processIdValue}";
        return BuildProcessNode(process, nodeId, diagram, context);
    }

    private static ActivityNode BuildProcessNode(XElement container, string nodeId, JsonElement? diagram, ImportContext context)
    {
        var elements = new List<BpmnElement>();
        var flows = new List<BpmnSequenceFlow>();
        var lanes = new List<BpmnLane>();
        var childActivities = new List<ActivityNode>();

        foreach (var child in container.Elements().Where(child => child.Name.Namespace == BpmnXmlNames.Model))
        {
            var localName = child.Name.LocalName;
            var id = IdOf(child);
            context.CountElement(localName);

            switch (localName)
            {
                case "startEvent":
                {
                    if (id is null) break;
                    if (child.Elements().Any(IsEventDefinition))
                        context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Start event '{id}' declares event definitions; imported as a none start event (event-defined starts arrive in the events tier).", id));
                    elements.Add(new BpmnElement(id, BpmnElementTypes.StartEvent, name: NameOf(child)));
                    break;
                }
                case "endEvent":
                {
                    if (id is null) break;
                    var isTerminate = child.Elements(BpmnXmlNames.Model + "terminateEventDefinition").Any();
                    var otherDefinitions = child.Elements().Where(IsEventDefinition).Any(definition => definition.Name.LocalName != "terminateEventDefinition");
                    if (otherDefinitions)
                        context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"End event '{id}' declares unsupported event definitions; imported as a {(isTerminate ? "terminate" : "none")} end event.", id));
                    elements.Add(new BpmnElement(
                        id,
                        BpmnElementTypes.EndEvent,
                        name: NameOf(child),
                        eventDefinitions: isTerminate ? [new BpmnEventDefinition(BpmnEventDefinitionTypes.Terminate)] : null));
                    break;
                }
                case "subProcess":
                {
                    if (id is null) break;
                    var nestedNodeId = $"node-{id}";
                    childActivities.Add(BuildProcessNode(child, nestedNodeId, diagram: null, context));
                    elements.Add(new BpmnElement(id, BpmnElementTypes.SubProcess, name: NameOf(child), childNodeId: nestedNodeId, defaultFlowId: DefaultOf(child)));
                    break;
                }
                case "sequenceFlow":
                {
                    if (id is null) break;
                    var sourceRef = (string?)child.Attribute("sourceRef");
                    var targetRef = (string?)child.Attribute("targetRef");
                    if (sourceRef is null || targetRef is null)
                    {
                        context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Sequence flow '{id}' is missing sourceRef/targetRef and was dropped.", id));
                        break;
                    }

                    var conditionOutcome = (string?)child.Attribute(BpmnXmlNames.Elsa + "conditionOutcome");
                    var conditionExpression = child.Element(BpmnXmlNames.Model + "conditionExpression");
                    if (conditionOutcome is null && conditionExpression is not null)
                        context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Sequence flow '{id}' carries an expression condition ('{conditionExpression.Value.Trim()}'); expression conditions are not executable in this slice, so the flow imported as unconditional.", id));

                    flows.Add(new BpmnSequenceFlow(id, sourceRef, targetRef, name: NameOf(child), conditionOutcome: conditionOutcome));
                    break;
                }
                case "laneSet":
                {
                    foreach (var lane in child.Elements(BpmnXmlNames.Model + "lane"))
                    {
                        var laneId = IdOf(lane);
                        if (laneId is null) continue;
                        lanes.Add(new BpmnLane(laneId, name: NameOf(lane)));
                        foreach (var flowNodeRef in lane.Elements(BpmnXmlNames.Model + "flowNodeRef"))
                            context.LaneByElementId[flowNodeRef.Value.Trim()] = laneId;
                    }
                    break;
                }
                default:
                {
                    if (BpmnXmlNames.TaskLocalNamesToElementTypes.TryGetValue(localName, out var taskType))
                    {
                        if (id is null) break;
                        elements.Add(new BpmnElement(id, taskType, name: NameOf(child), defaultFlowId: DefaultOf(child)));
                        if (taskType != BpmnElementTypes.Task)
                            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Info, $"{Capitalize(localName)} '{id}' imported unbound; bind an Elsa activity to execute it.", id));
                        break;
                    }

                    if (BpmnXmlNames.GatewayLocalNamesToElementTypes.TryGetValue(localName, out var gatewayType))
                    {
                        if (id is null) break;
                        elements.Add(new BpmnElement(id, gatewayType, name: NameOf(child), defaultFlowId: DefaultOf(child)));
                        break;
                    }

                    if (localName is "documentation" or "extensionElements" or "incoming" or "outgoing")
                        break;

                    context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"BPMN element <{localName}>{(id is null ? "" : $" '{id}'")} is not supported by this slice and was dropped.", id));
                    break;
                }
            }
        }

        var elementsWithLanes = elements
            .Select(element => context.LaneByElementId.TryGetValue(element.ElementId, out var laneId)
                ? new BpmnElement(element.ElementId, element.ElementType, element.Name, element.ChildNodeId, laneId, element.DefaultFlowId, element.EventDefinitions, element.Properties)
                : element)
            .ToArray();

        var elementIds = elementsWithLanes.Select(element => element.ElementId).ToHashSet(StringComparer.Ordinal);
        var connectedFlows = flows.Where(flow =>
        {
            var connected = elementIds.Contains(flow.SourceRef) && elementIds.Contains(flow.TargetRef);
            if (!connected)
                context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Sequence flow '{flow.FlowId}' references a dropped element and was dropped with it.", flow.FlowId));
            return connected;
        }).ToArray();

        if (HasCycle(elementIds, connectedFlows))
            context.Issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, "The process graph contains a cycle; it will import but is not executable in this slice (loops arrive in the events tier)."));

        var structure = new BpmnAuthoredStructure(
            activities: childActivities,
            elements: elementsWithLanes,
            sequenceFlows: connectedFlows,
            lanes: lanes,
            diagram: diagram);

        return new ActivityNode(
            nodeId,
            DefaultBpmnProcessActivityVersionId,
            [],
            [],
            new ActivityNodeStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions)));
    }

    private static JsonElement? ReadDiagram(XElement definitions)
    {
        var plane = definitions
            .Elements(BpmnXmlNames.Di + "BPMNDiagram")
            .Elements(BpmnXmlNames.Di + "BPMNPlane")
            .FirstOrDefault();
        if (plane is null) return null;

        var shapes = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var shape in plane.Elements(BpmnXmlNames.Di + "BPMNShape"))
        {
            var elementId = (string?)shape.Attribute("bpmnElement");
            var bounds = shape.Element(BpmnXmlNames.Dc + "Bounds");
            if (elementId is null || bounds is null) continue;
            shapes[elementId] = new
            {
                x = (double?)bounds.Attribute("x") ?? 0,
                y = (double?)bounds.Attribute("y") ?? 0,
                width = (double?)bounds.Attribute("width") ?? 0,
                height = (double?)bounds.Attribute("height") ?? 0
            };
        }

        var edges = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var edge in plane.Elements(BpmnXmlNames.Di + "BPMNEdge"))
        {
            var elementId = (string?)edge.Attribute("bpmnElement");
            if (elementId is null) continue;
            edges[elementId] = new
            {
                waypoints = edge.Elements(BpmnXmlNames.Dd + "waypoint")
                    .Select(waypoint => new { x = (double?)waypoint.Attribute("x") ?? 0, y = (double?)waypoint.Attribute("y") ?? 0 })
                    .ToArray()
            };
        }

        return JsonSerializer.SerializeToElement(new { shapes, edges }, SerializerOptions);
    }

    private static bool HasCycle(IReadOnlyCollection<string> elementIds, IReadOnlyCollection<BpmnSequenceFlow> flows)
    {
        var outbound = flows.ToLookup(flow => flow.SourceRef, StringComparer.Ordinal);
        var states = new Dictionary<string, int>(StringComparer.Ordinal);

        bool Visit(string elementId)
        {
            if (states.TryGetValue(elementId, out var known)) return known == 1;
            states[elementId] = 1;
            if (outbound[elementId].Any(flow => Visit(flow.TargetRef))) return true;
            states[elementId] = 2;
            return false;
        }

        return elementIds.Any(Visit);
    }

    private static bool IsEventDefinition(XElement element) =>
        element.Name.Namespace == BpmnXmlNames.Model && element.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal);

    private static string? IdOf(XElement element) => (string?)element.Attribute("id");
    private static string? NameOf(XElement element) => (string?)element.Attribute("name");
    private static string? DefaultOf(XElement element) => (string?)element.Attribute("default");

    private static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private sealed class ImportContext
    {
        public List<string> ProcessIds { get; } = [];
        public List<BpmnImportIssue> Issues { get; } = [];
        public Dictionary<string, string> LaneByElementId { get; } = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _elementCounts = new(StringComparer.Ordinal);

        public void CountElement(string localName)
        {
            _elementCounts[localName] = (_elementCounts.TryGetValue(localName, out var count) ? count : 0) + 1;
        }

        public BpmnImportAnalysis ToAnalysis() => new(ProcessIds, _elementCounts, Issues);
    }
}

using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Models;
using LibInterchange = global::Bpmn.Interchange;
using LibModel = global::Bpmn.Model;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>
/// Renders a <c>BpmnProcess</c> activity node's authored <c>elsa.bpmn.structure</c> payload as BPMN 2.0 XML
/// with BPMN DI.
/// <para>
/// Writing the XML is <c>Bpmn.Interchange</c>'s job. This class unwraps the authored payload into the neutral
/// <see cref="LibModel.BpmnDefinitions"/> the writer takes, plus the work bindings it needs to see through an
/// element: a <see cref="LibInterchange.BpmnWorkBinding.NestedProcess"/> so a subprocess inlines its body, and a
/// <see cref="LibInterchange.BpmnWorkBinding.CallProcess"/> so a call activity emits what it calls and whether it
/// waits. The synthesized <c>Delay</c>/<c>Event</c>/<c>PublishEvent</c>/<c>DispatchWorkflow</c> children are Elsa
/// detail the importer re-establishes, so their nodes are never inlined.
/// </para>
/// </summary>
public sealed class BpmnDocumentExporter : IBpmnDocumentExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Export(ActivityNode processNode, BpmnExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(processNode);

        var structure = ReadStructure(processNode);
        var processId = options?.ProcessId ?? SanitizeId(processNode.NodeId);
        var bindings = new List<LibInterchange.BpmnWorkBinding>();

        var definitions = new LibModel.BpmnDefinitions(
            Processes: [BuildProcess(structure, processId, bindings)],
            Collaboration: BuildCollaboration(structure, processId),
            Diagrams: BuildDiagrams(structure, processId));

        var xml = new LibInterchange.BpmnXmlWriter().Write(definitions, bindings, new LibInterchange.BpmnExportOptions
        {
            TargetNamespace = BpmnVendorNamespace.NamespaceName
        });

        return BpmnVendorNamespace.ToElsa(xml);
    }

    private static LibModel.BpmnProcessDefinition BuildProcess(BpmnAuthoredStructure structure, string processId, List<LibInterchange.BpmnWorkBinding> bindings)
    {
        var childrenByNodeId = structure.Activities.ToDictionary(activity => activity.NodeId, StringComparer.Ordinal);
        var elements = new List<LibModel.BpmnElement>();

        foreach (var element in structure.Elements)
        {
            var child = element.ChildNodeId is { } childNodeId && childrenByNodeId.TryGetValue(childNodeId, out var bound) ? bound : null;
            var extensions = LibModel.BpmnExtensions.Empty;

            if (child is not null && StringComparer.Ordinal.Equals(child.Structure?.Kind, BpmnProcessActivity.StructureKind))
            {
                bindings.Add(new LibInterchange.BpmnWorkBinding.NestedProcess(
                    processId, element.ElementId, child.NodeId, LibInterchange.BpmnBindingSlot.Primary,
                    BuildProcess(ReadStructure(child), element.ElementId, bindings)));
            }
            else if (StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.CallActivity) && child is not null)
            {
                // The bound DispatchWorkflow is what says which Elsa workflow this call runs; BPMN has no standard
                // attribute for that, so it also rides the vendor namespace for a lossless round trip.
                var workflowDefinitionId = ReadLiteralString(child, "WorkflowDefinitionId");
                bindings.Add(new LibInterchange.BpmnWorkBinding.CallProcess(
                    processId, element.ElementId, child.NodeId, LibInterchange.BpmnBindingSlot.Primary,
                    workflowDefinitionId, ReadLiteralBool(child, "WaitForCompletion") ?? true));
                if (!string.IsNullOrWhiteSpace(workflowDefinitionId))
                    extensions = new LibModel.BpmnExtensions(ForeignAttributes:
                    [
                        new LibModel.BpmnForeignAttribute(
                            new LibModel.BpmnQName(LibInterchange.BpmnXmlNames.VendorNamespaceName, "workflowDefinitionId"),
                            workflowDefinitionId!)
                    ]);
            }

            elements.Add(new LibModel.BpmnElement(
                element.ElementId,
                element.ElementType,
                element.Name,
                element.ChildNodeId,
                element.LaneId,
                element.DefaultFlowId,
                element.EventDefinitions.Select(definition => new LibModel.BpmnEventDefinition(definition.Type, definition.Properties)).ToArray(),
                element.Properties.Count == 0 ? null : element.Properties,
                element.AttachedToRef,
                element.CancelActivity,
                element.LoopCharacteristics is { } loop ? new LibModel.BpmnLoopCharacteristics(loop.IsSequential, loop.Cardinality, loop.CollectionVariable, loop.ItemVariable) : null,
                element.IsForCompensation,
                element.CompensationHandlerElementId,
                element.IsTransaction,
                element.TriggeredByEvent,
                element.ListenerNodeId,
                extensions));
        }

        return new LibModel.BpmnProcessDefinition(
            processId,
            IsExecutable: true,
            IsTransaction: structure.IsTransaction,
            Elements: elements,
            SequenceFlows: structure.SequenceFlows
                .Select(flow => new LibModel.BpmnSequenceFlow(flow.FlowId, flow.SourceRef, flow.TargetRef, flow.Name, flow.ConditionOutcome, flow.IsDefault))
                .ToArray(),
            Lanes: structure.Lanes.Select(lane => new LibModel.BpmnLane(lane.LaneId, lane.PoolId, lane.Name)).ToArray(),
            Variables: structure.Variables.Select(variable => new LibModel.BpmnVariableDeclaration(variable.Name)).ToArray());
    }

    /// <summary>
    /// The <c>&lt;collaboration&gt;</c> wrapper for a structure carrying pools and/or message flows. This is a
    /// per-definition export, so every participant references the one exported process; a full N-pool document
    /// assembled from N separate structures remains a documented cut.
    /// </summary>
    private static LibModel.BpmnCollaboration? BuildCollaboration(BpmnAuthoredStructure structure, string processId)
    {
        if (structure.Pools.Count == 0 && structure.MessageFlows.Count == 0)
            return null;

        return new LibModel.BpmnCollaboration(
            $"{processId}-collaboration",
            structure.Pools
                .DistinctBy(pool => pool.PoolId, StringComparer.Ordinal)
                .Select(pool => new LibModel.BpmnPool(pool.PoolId, pool.Name, processId, pool.IsExecutable))
                .ToArray(),
            structure.MessageFlows
                .DistinctBy(flow => flow.FlowId, StringComparer.Ordinal)
                .Select(flow => new LibModel.BpmnMessageFlow(flow.FlowId, flow.Name, flow.SourceElementId, flow.SourcePoolId, flow.TargetElementId, flow.TargetPoolId, flow.MessageName))
                .ToArray());
    }

    /// <summary>
    /// Rehydrates the opaque <c>{shapes, edges}</c> diagram payload into the writer's typed layout. Anything the
    /// payload never positioned is left out here and laid out by the writer, so every element still gets a shape.
    /// </summary>
    private static IReadOnlyList<LibModel.BpmnDiagram> BuildDiagrams(BpmnAuthoredStructure structure, string processId)
    {
        if (structure.Diagram is not { } diagram || diagram.ValueKind != JsonValueKind.Object)
            return [];

        var shapes = new List<LibModel.BpmnShape>();
        if (diagram.TryGetProperty("shapes", out var shapeMap) && shapeMap.ValueKind == JsonValueKind.Object)
            foreach (var shape in shapeMap.EnumerateObject().Where(entry => entry.Value.ValueKind == JsonValueKind.Object))
                shapes.Add(new LibModel.BpmnShape(null, shape.Name, new LibModel.BpmnBounds(
                    ReadNumber(shape.Value, "x"),
                    ReadNumber(shape.Value, "y"),
                    ReadNumber(shape.Value, "width", LibModel.BpmnLayoutDefaults.TaskWidth),
                    ReadNumber(shape.Value, "height", LibModel.BpmnLayoutDefaults.TaskHeight))));

        var edges = new List<LibModel.BpmnEdge>();
        if (diagram.TryGetProperty("edges", out var edgeMap) && edgeMap.ValueKind == JsonValueKind.Object)
            foreach (var edge in edgeMap.EnumerateObject().Where(entry => entry.Value.ValueKind == JsonValueKind.Object))
            {
                if (!edge.Value.TryGetProperty("waypoints", out var waypoints) || waypoints.ValueKind != JsonValueKind.Array)
                    continue;
                edges.Add(new LibModel.BpmnEdge(null, edge.Name, waypoints
                    .EnumerateArray()
                    .Where(waypoint => waypoint.ValueKind == JsonValueKind.Object)
                    .Select(waypoint => new LibModel.BpmnPoint(ReadNumber(waypoint, "x"), ReadNumber(waypoint, "y")))
                    .ToArray()));
            }

        if (shapes.Count == 0 && edges.Count == 0)
            return [];

        return [new LibModel.BpmnDiagram($"{processId}-diagram", null, new LibModel.BpmnPlane($"{processId}-plane", processId, shapes, edges))];
    }

    private static double ReadNumber(JsonElement element, string property, double fallback = 0) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : fallback;

    /// <summary>Reads a synthesized child's literal string input, tolerating both the in-memory raw value and the round-tripped <see cref="JsonElement"/> form.</summary>
    private static string? ReadLiteralString(ActivityNode node, string referenceKey) =>
        node.Inputs.FirstOrDefault(input => StringComparer.Ordinal.Equals(input.ReferenceKey, referenceKey))?.Value.Value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            { } other => other.ToString()
        };

    /// <summary>Reads a synthesized child's literal bool input, tolerating both the in-memory raw value and the round-tripped <see cref="JsonElement"/> form.</summary>
    private static bool? ReadLiteralBool(ActivityNode node, string referenceKey) =>
        node.Inputs.FirstOrDefault(input => StringComparer.Ordinal.Equals(input.ReferenceKey, referenceKey))?.Value.Value switch
        {
            null => null,
            bool value => value,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            JsonElement { ValueKind: JsonValueKind.String } json => bool.TryParse(json.GetString(), out var parsed) ? parsed : null,
            string text => bool.TryParse(text, out var parsed) ? parsed : null,
            _ => null
        };

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

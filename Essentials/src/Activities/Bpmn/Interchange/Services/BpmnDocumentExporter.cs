using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using LibInterchange = global::Bpmn.Interchange;
using LibModel = global::Bpmn.Model;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Core.BpmnActivityIdentities;

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
/// <para>
/// A task's own bound work (its exact activity version and literal inputs) travels in <c>elsa:taskBinding</c> only from
/// <see cref="ExportAsync"/>, and only when the visible catalog resolves the version and declares every bound input as a
/// portable literal: never a sensitive, credential, masked or connection input, an expression, or a binding with storage,
/// conversion or output settings. Every binding left out is reported for its element. <see cref="Export"/> has no catalog,
/// so it carries none.
/// </para>
/// <para>
/// Complete-source schema2 export returns its canonical XML directly. Disjoint native bindings and scope/process
/// configuration are not added to that XML; <see cref="ExportAsync"/> reports those omissions explicitly.
/// </para>
/// </summary>
public sealed class BpmnDocumentExporter(IActivityDefinitionLookup? catalog = null) : IBpmnDocumentExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Export(ActivityNode processNode, BpmnExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(processNode);
        if (IsCompleteSource(processNode)) return CompleteSourceExport(processNode, options).Xml;
        return Write(ReadStructure(processNode), processNode, options, new Dictionary<string, LibModel.BpmnForeignAttribute>(StringComparer.Ordinal));
    }

    public async Task<BpmnExportResult> ExportAsync(ActivityNode processNode, BpmnExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processNode);
        if (IsCompleteSource(processNode)) return CompleteSourceExport(processNode, options);
        var structure = ReadStructure(processNode);
        var carried = new Dictionary<string, LibModel.BpmnForeignAttribute>(StringComparer.Ordinal);
        var omissions = new List<BpmnExportOmission>();
        var candidates = new List<(BpmnElement Element, ActivityNode Child)>();
        var elementIds = new List<string>();
        CollectTaskBindings(structure, candidates, elementIds);
        // A binding is keyed by its element id in the document, so a task whose id any other element shares, in any
        // scope, carries none: the attribute would land on every element with that id.
        var shared = elementIds.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var ambiguous = candidates.Select(candidate => candidate.Element.ElementId).Where(shared.Contains).ToHashSet(StringComparer.Ordinal);
        foreach (var elementId in ambiguous.Order(StringComparer.Ordinal))
            omissions.Add(new(elementId, "ambiguous-element", "Another element in the document uses this element id; no binding travels for it."));
        candidates.RemoveAll(candidate => ambiguous.Contains(candidate.Element.ElementId));
        if (candidates.Count > 0)
        {
            // ListDefinitions is the visible picker inventory: a version of a removed or hidden definition never travels.
            var visible = catalog is null
                ? null
                : (await catalog.ListDefinitions(cancellationToken: cancellationToken)).Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var (element, child) in candidates)
            {
                var omission = visible is null
                    ? new BpmnExportOmission(element.ElementId, "catalog-unavailable", "No activity catalog is available to resolve this task's binding.")
                    : await PlanAsync(element, child, visible, cancellationToken);
                var attribute = omission is null
                    ? BpmnTaskBindingMetadata.ToAttribute(child.ActivityVersionId, child.Inputs.Where(input => input.IsBound()).ToArray())
                    : null;
                // Never write what import refuses to read: the same size, depth and id limits decide here.
                if (attribute is not null && !BpmnTaskBindingMetadata.TryRead(attribute.Value, out _, out _, out var problem))
                    omission = new(element.ElementId, "oversized-binding", problem!);
                if (omission is null)
                    carried[element.ElementId] = attribute!;
                else
                    omissions.Add(omission);
            }
        }

        return new(Write(structure, processNode, options, carried), omissions);
    }

    private static bool IsCompleteSource(ActivityNode node) => node.Structure is
        { Kind: BpmnProcessActivity.StructureKind, SchemaVersion: BpmnAuthoredDocument.SchemaVersion };

    private static BpmnExportResult CompleteSourceExport(ActivityNode owner, BpmnExportOptions? options)
    {
        var source = BpmnDocumentStructureHandler.Read(owner);
        if (options?.ProcessId is { } requested && requested != source.ProcessId)
            throw new BpmnInterchangeException("Complete-source export preserves authored process IDs; renaming requires an explicit source/reference transform.");
        var omissions = source.Bindings.Select(binding => new BpmnExportOmission(binding.ElementId, "native-configuration-not-exported",
            "The complete BPMN XML is retained. Disjoint Elsa child/listener versions, inputs, outputs, native subtrees and scoped references are not embedded in shared XML.")).ToList();
        omissions.AddRange(source.ScopeHeaders.Select(header => new BpmnExportOmission(header.ScopeElementId, "scope-configuration-not-exported",
            "The standard XML body is retained; its Elsa invocation header and scoped variable configuration are not embedded in shared XML.")));
        omissions.Add(new(source.ProcessId, "process-configuration-not-exported", "The exact Elsa process activity version, arguments, scoped variables and invocation configuration are not embedded in shared XML."));
        return new(source.DocumentXml, omissions);
    }

    /// <summary>Every task (in this process and its nested subprocesses) bound to an author-chosen child, and every element id in all scopes.</summary>
    private static void CollectTaskBindings(BpmnAuthoredStructure structure, List<(BpmnElement Element, ActivityNode Child)> candidates, List<string> elementIds)
    {
        var childrenByNodeId = structure.Activities.ToDictionary(activity => activity.NodeId, StringComparer.Ordinal);
        foreach (var element in structure.Elements)
        {
            elementIds.Add(element.ElementId);
            if (element.ChildNodeId is not { } childNodeId || !childrenByNodeId.TryGetValue(childNodeId, out var child))
                continue;
            if (StringComparer.Ordinal.Equals(child.Structure?.Kind, BpmnProcessActivity.StructureKind))
                CollectTaskBindings(ReadStructure(child), candidates, elementIds);
            else if (BpmnTaskBindingMetadata.TaskTypes.Contains(element.ElementType))
                candidates.Add((element, child));
        }
    }

    /// <summary>Why this task's binding cannot travel, or null when it can. The first input that disqualifies it is named.</summary>
    private async Task<BpmnExportOmission?> PlanAsync(BpmnElement element, ActivityNode child, IReadOnlySet<string> visibleDefinitions, CancellationToken cancellationToken)
    {
        if (child.Structure is not null)
            return new(element.ElementId, "structured-child", "A bound child with its own authored structure cannot travel as a task binding.");
        if (child.Outputs.Any(output => output.IsBound()))
            return new(element.ElementId, "unsupported-output", "A task binding with output captures cannot travel; outputs name this host's variables.");
        var version = await catalog!.FindVersion(child.ActivityVersionId, cancellationToken);
        if (version is null || !visibleDefinitions.Contains(version.DefinitionId))
            return new(element.ElementId, "unresolvable-version", $"Activity version '{child.ActivityVersionId}' is not in the visible catalog.");
        var declarations = version.Inputs.ToDictionary(input => input.ReferenceKey, StringComparer.Ordinal);
        if (child.Inputs.Where(input => input.IsBound()).GroupBy(input => input.ReferenceKey, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1) is { } repeated)
            return new(element.ElementId, "duplicate-input", "The task binds the same input more than once.", repeated.Key);
        foreach (var input in child.Inputs.Where(input => input.IsBound()))
        {
            if (!declarations.TryGetValue(input.ReferenceKey, out var declaration))
                return new(element.ElementId, "unresolvable-input", "The bound input is not declared by the activity version.", input.ReferenceKey);
            if (!BpmnTaskBindingMetadata.IsPortable(declaration) || input.IsSensitive == true)
                return new(element.ElementId, "sensitive-input", "A sensitive, credential or connection input never travels in shared XML.", input.ReferenceKey);
            if (!BpmnTaskBindingMetadata.IsLiteral(input.Value))
                return new(element.ElementId, "non-literal-input", "Only literal input values travel; an expression refers to this host's process.", input.ReferenceKey);
            if (!string.IsNullOrWhiteSpace(input.StorageDriverType) || !string.IsNullOrWhiteSpace(input.EvaluatorType) || input.AutoEvaluate is not null || input.Conversion is not null)
                return new(element.ElementId, "unsupported-input-setting", "An input with storage, evaluation or conversion settings cannot travel.", input.ReferenceKey);
        }
        return null;
    }

    private static string Write(
        BpmnAuthoredStructure structure,
        ActivityNode processNode,
        BpmnExportOptions? options,
        IReadOnlyDictionary<string, LibModel.BpmnForeignAttribute> taskBindings)
    {
        var processId = options?.ProcessId ?? SanitizeId(processNode.NodeId);
        var bindings = new List<LibInterchange.BpmnWorkBinding>();

        var definitions = new LibModel.BpmnDefinitions(
            Processes: [BuildProcess(structure, processId, bindings, taskBindings)],
            Collaboration: BuildCollaboration(structure, processId),
            Diagrams: BuildDiagrams(structure, processId));

        var xml = new LibInterchange.BpmnXmlWriter().Write(definitions, bindings, new LibInterchange.BpmnExportOptions
        {
            TargetNamespace = BpmnVendorNamespace.NamespaceName
        });

        return BpmnVendorNamespace.ToElsa(xml);
    }

    private static LibModel.BpmnProcessDefinition BuildProcess(
        BpmnAuthoredStructure structure,
        string processId,
        List<LibInterchange.BpmnWorkBinding> bindings,
        IReadOnlyDictionary<string, LibModel.BpmnForeignAttribute> taskBindings)
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
                    BuildProcess(ReadStructure(child), element.ElementId, bindings, taskBindings)));
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

            if (taskBindings.TryGetValue(element.ElementId, out var taskBinding))
                extensions = extensions with { ForeignAttributes = extensions.ForeignAttributes.Append(taskBinding).ToArray() };

            if (element.Properties.TryGetValue(BpmnDirectBindingMetadata.PropertyKey, out var directBinding))
                extensions = extensions with
                {
                    ForeignAttributes = extensions.ForeignAttributes
                        .Append(BpmnDirectBindingMetadata.ToAttribute(directBinding, element.ElementId)).ToArray()
                };

            elements.Add(new LibModel.BpmnElement(
                element.ElementId,
                element.ElementType,
                element.Name,
                element.ChildNodeId,
                element.LaneId,
                element.DefaultFlowId,
                element.EventDefinitions.Select(definition => new LibModel.BpmnEventDefinition(definition.Type, definition.Properties)).ToArray(),
                element.Properties.Where(property => property.Key != BpmnDirectBindingMetadata.PropertyKey &&
                        !property.Key.StartsWith(BpmnRetainedAuthoring.ConditionExpressionPrefix, StringComparison.Ordinal))
                    .ToDictionary(property => property.Key, property => property.Value, StringComparer.Ordinal),
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
                .Select(flow => new LibModel.BpmnSequenceFlow(flow.FlowId, flow.SourceRef, flow.TargetRef, flow.Name, flow.ConditionOutcome, flow.IsDefault)
                    { ConditionExpression = ReadRetainedCondition(structure, flow) })
                .ToArray(),
            Lanes: structure.Lanes.Select(lane => new LibModel.BpmnLane(lane.LaneId, lane.PoolId, lane.Name)).ToArray(),
            Variables: structure.Variables.Select(variable => new LibModel.BpmnVariableDeclaration(variable.Name)).ToArray());
    }

    private static LibModel.BpmnConditionExpression? ReadRetainedCondition(BpmnAuthoredStructure structure, BpmnSequenceFlow flow)
    {
        // Studio reconnects an edge without changing its id. Retention follows that identity even
        // while its property bag remains on the original source; deletion ignores unused markers.
        var retained = structure.Elements.Select(element => element.Properties.GetValueOrDefault(BpmnRetainedAuthoring.ConditionExpressionPrefix + flow.FlowId))
            .Where(value => value is not null).Distinct(StringComparer.Ordinal).ToArray();
        if (retained.Length == 0)
            return null;
        if (retained.Length != 1)
            throw new BpmnInterchangeException($"Flow '{flow.FlowId}' has conflicting retained condition expressions.");
        var json = retained[0]!;
        try
        {
            return JsonSerializer.Deserialize<LibModel.BpmnConditionExpression>(json, SerializerOptions)
                ?? throw new BpmnInterchangeException($"Retained condition for flow '{flow.FlowId}' is null.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new BpmnInterchangeException($"Retained condition for flow '{flow.FlowId}' is invalid.", exception);
        }
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
        if (!StringComparer.Ordinal.Equals(processNode.Structure.SchemaVersion, BpmnProcessActivity.StructureSchemaVersion))
            throw new BpmnInterchangeException($"Activity node '{processNode.NodeId}' has unsupported authoring schema '{processNode.Structure.SchemaVersion}'; legacy BPMN export requires '{BpmnProcessActivity.StructureSchemaVersion}'.");

        return processNode.Structure.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)
               ?? throw new BpmnInterchangeException($"Activity node '{processNode.NodeId}' structure resolved to null.");
    }

    private static string SanitizeId(string value)
    {
        var sanitized = new string(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "process" : sanitized;
    }
}

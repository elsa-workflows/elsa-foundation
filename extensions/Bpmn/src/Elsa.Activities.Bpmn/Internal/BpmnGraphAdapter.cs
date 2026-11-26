using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Bpmn.Semantics;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Process = Elsa.Activities.Bpmn.Activities.BpmnProcess;
using LibraryGraph = Bpmn.Semantics.BpmnGraph;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Pure translation from the pinned Elsa authored/executable envelope to the library graph.</summary>
internal static class BpmnGraphAdapter
{
    public const BpmnHostCapabilities Capabilities = BpmnHostCapabilities.SubtreeCancellation |
        BpmnHostCapabilities.ScopeSignalling | BpmnHostCapabilities.IterationScopes | BpmnHostCapabilities.ScopeVariables;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static LibraryGraph Build(ExecutableNode node)
    {
        try
        {
            var definition = Definition(node);
            var children = Children(node).ToDictionary(child => child.ExecutableNodeId, StringComparer.Ordinal);
            // Supply the complete host inventory. Deriving only referenced bindings would hide orphan
            // authored children from the library validator, which must reject them rather than ignore work.
            var work = children.Values.Select(child => new BpmnBoundWork(child.ExecutableNodeId,
                child.Structure?.Kind == Process.StructureKind ? Definition(child) : null)).ToArray();
            return LibraryGraph.Build(definition, work, Capabilities);
        }
        catch (BpmnHostException)
        {
            throw;
        }
        catch (global::Bpmn.Semantics.BpmnExecutionException exception)
        {
            throw new BpmnHostException($"BPMN executable node '{node.ExecutableNodeId}' could not be bound to the library graph: {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new BpmnHostException($"BPMN executable node '{node.ExecutableNodeId}' could not be bound to the library graph.", exception);
        }
    }

    public static BpmnProcessDefinition Definition(ExecutableNode node)
    {
        if (node.Structure is not { } structure)
            return new BpmnProcessDefinition(node.ExecutableNodeId);
        if (structure.Kind != Process.StructureKind || structure.SchemaVersion != Process.StructureSchemaVersion)
            throw new BpmnHostException($"BPMN executable node '{node.ExecutableNodeId}' has an unsupported structure kind or schema version.");

        var root = JsonNode.Parse(structure.Payload.GetRawText())?.AsObject()
            ?? throw new JsonException("BPMN structure is null.");
        root["processId"] = node.ExecutableNodeId;
        foreach (var value in root["elements"]?.AsArray() ?? [])
        {
            var element = value!.AsObject();
            if (element["properties"] is JsonObject properties && properties.Any(property =>
                    property.Key.StartsWith(Models.BpmnRetainedAuthoring.ConditionExpressionPrefix, StringComparison.Ordinal) &&
                    root["sequenceFlows"]!.AsArray().Any(flow =>
                        flow!["flowId"]!.GetValue<string>() == property.Key[Models.BpmnRetainedAuthoring.ConditionExpressionPrefix.Length..])))
                throw new BpmnHostException($"BPMN element '{element["elementId"]}' retains a condition expression, but this Foundation host does not support condition evaluation.");
            Move(element, "childNodeId", "bindingRef");
            Move(element, "listenerNodeId", "listenerBindingRef");
        }
        return root.Deserialize<BpmnProcessDefinition>(Web) ?? throw new JsonException("BPMN definition is null.");
    }

    internal static Models.BpmnElement AuthoredElement(global::Bpmn.Model.BpmnElement source)
    {
        var value = JsonSerializer.SerializeToNode(source, Web)!.AsObject();
        Move(value, "bindingRef", "childNodeId");
        Move(value, "listenerBindingRef", "listenerNodeId");
        return value.Deserialize<Models.BpmnElement>(Web) ?? throw new BpmnHostException("BPMN authored element is null.");
    }

    private static IEnumerable<ExecutableNode> Children(ExecutableNode node) =>
        node.ChildSlots.FirstOrDefault(slot => slot.Name == Process.ActivitiesSlotName)?.Activities ?? [];

    private static void Move(JsonObject value, string from, string to)
    {
        if (!value.TryGetPropertyValue(from, out var child))
            return;
        if (value.ContainsKey(to))
            throw new JsonException($"BPMN structure contains conflicting '{from}' and '{to}' fields.");
        value.Remove(from);
        value[to] = child;
    }
}

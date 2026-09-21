using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>
/// Projects the run-view connectivity of a BPMN executable structure. Only activity-bearing elements
/// (tasks/subprocesses with a bound child) execute and therefore appear in the run layout; gateways and
/// events are engine-interpreted and never run. This projector collapses each sequence-flow path that
/// crosses pure structure elements into one connection between the bound child activities on either
/// side, so the run canvas draws honest edges between the nodes that actually executed. Unknown kinds,
/// schemas, and malformed entries are omitted, mirroring the Flowchart projector's allowlist stance.
/// </summary>
internal static class RuntimeBpmnLayoutConnectionProjector
{
    private const string StructureKind = "elsa.bpmn.structure";
    private const string StructureSchemaVersion = "1.0.0";

    public static IReadOnlyList<ActivityExecutionLayoutConnection> Project(
        WorkflowExecutable executable,
        IReadOnlyCollection<ExecutableActivityLayoutRecord> boundaryNodes)
    {
        var result = new List<ActivityExecutionLayoutConnection>();
        foreach (var record in boundaryNodes)
        {
            if (!executable.NodesById.TryGetValue(record.ExecutableNodeId, out var node) ||
                node.Structure is not { } structure ||
                !StringComparer.Ordinal.Equals(structure.Kind, StructureKind) ||
                !StringComparer.Ordinal.Equals(structure.SchemaVersion, StructureSchemaVersion))
                continue;

            ProjectContainer(structure.Payload, executable, result);
        }

        return result;
    }

    private static void ProjectContainer(JsonElement payload, WorkflowExecutable executable, ICollection<ActivityExecutionLayoutConnection> result)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return;

        // elementId -> bound child executable node id, for elements whose child actually exists.
        var boundByElementId = new Dictionary<string, string>(StringComparer.Ordinal);
        if (payload.TryGetProperty("elements", out var elements) && elements.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in elements.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !TryReadString(element, "elementId", out var elementId))
                    continue;

                if (TryReadString(element, "childNodeId", out var childNodeId) && executable.NodesById.ContainsKey(childNodeId))
                    boundByElementId[elementId] = childNodeId;
            }
        }

        if (boundByElementId.Count == 0)
            return;

        var outbound = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (payload.TryGetProperty("sequenceFlows", out var flows) && flows.ValueKind == JsonValueKind.Array)
        {
            foreach (var flow in flows.EnumerateArray())
            {
                if (flow.ValueKind != JsonValueKind.Object ||
                    !TryReadString(flow, "sourceRef", out var sourceRef) ||
                    !TryReadString(flow, "targetRef", out var targetRef))
                    continue;

                if (!outbound.TryGetValue(sourceRef, out var targets))
                    outbound[sourceRef] = targets = [];
                targets.Add(targetRef);
            }
        }

        // From each bound element, walk forward through pure elements until the next bound element(s);
        // each landing becomes one collapsed connection between the two bound children.
        var emitted = new HashSet<(string Source, string Target)>();
        foreach (var (sourceElementId, sourceNodeId) in boundByElementId)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { sourceElementId };
            var queue = new Queue<string>();
            foreach (var next in OutboundOf(sourceElementId))
                queue.Enqueue(next);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current))
                    continue;

                if (boundByElementId.TryGetValue(current, out var targetNodeId))
                {
                    if (emitted.Add((sourceNodeId, targetNodeId)))
                        result.Add(new(new(sourceNodeId, null), new(targetNodeId, null), null));
                    continue;
                }

                foreach (var next in OutboundOf(current))
                    queue.Enqueue(next);
            }
        }

        return;

        IEnumerable<string> OutboundOf(string elementId) =>
            outbound.TryGetValue(elementId, out var targets) ? targets : [];
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = "";
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        var candidate = property.GetString();
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        value = candidate;
        return true;
    }
}

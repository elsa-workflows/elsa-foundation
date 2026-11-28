using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>
/// Projects the small, allowlisted canvas subset of the Runtime-owned Flowchart executable structure.
/// Activity-owned structure payloads can contain arbitrary runtime data, so unknown kinds and schemas are
/// deliberately opaque and malformed connection entries are omitted instead of being reflected to callers.
/// </summary>
internal static class RuntimeFlowchartLayoutConnectionProjector
{
    private const string StructureKind = "elsa.flowchart.structure";
    private const string StructureSchemaVersion = "1.0.0";

    public static IReadOnlyList<ActivityExecutionLayoutConnection> Project(
        WorkflowExecutable executable,
        IReadOnlyCollection<ExecutableActivityLayoutRecord> boundaryNodes)
    {
        var nodeIdMap = BuildNodeIdMap(executable, boundaryNodes);
        if (nodeIdMap.Count == 0)
            return [];

        var result = new List<ActivityExecutionLayoutConnection>();
        foreach (var record in boundaryNodes)
        {
            if (!executable.NodesById.TryGetValue(record.ExecutableNodeId, out var node) ||
                node.Structure is not { } structure ||
                !StringComparer.Ordinal.Equals(structure.Kind, StructureKind) ||
                !StringComparer.Ordinal.Equals(structure.SchemaVersion, StructureSchemaVersion))
                continue;

            ProjectConnections(structure.Payload, nodeIdMap, result);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildNodeIdMap(
        WorkflowExecutable executable,
        IReadOnlyCollection<ExecutableActivityLayoutRecord> records)
    {
        var candidates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (!executable.NodesById.ContainsKey(record.ExecutableNodeId))
                continue;

            AddCandidate(record.TemplateNodeId, record.ExecutableNodeId);
            AddCandidate(record.AuthoredActivityId, record.ExecutableNodeId);
        }

        // A duplicated authored/template id is not safe to guess. Only unambiguous ids participate in joins.
        return candidates
            .Where(candidate => candidate.Value.Count == 1)
            .ToDictionary(candidate => candidate.Key, candidate => candidate.Value.Single(), StringComparer.Ordinal);

        void AddCandidate(string sourceNodeId, string executableNodeId)
        {
            if (string.IsNullOrWhiteSpace(sourceNodeId))
                return;
            if (!candidates.TryGetValue(sourceNodeId, out var values))
            {
                values = new HashSet<string>(StringComparer.Ordinal);
                candidates.Add(sourceNodeId, values);
            }
            values.Add(executableNodeId);
        }
    }

    private static void ProjectConnections(
        JsonElement payload,
        IReadOnlyDictionary<string, string> nodeIdMap,
        ICollection<ActivityExecutionLayoutConnection> result)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("connections", out var connections) ||
            connections.ValueKind != JsonValueKind.Array)
            return;

        foreach (var connection in connections.EnumerateArray())
        {
            if (connection.ValueKind != JsonValueKind.Object ||
                !TryProjectEndpoint(connection, "source", nodeIdMap, out var source) ||
                !TryProjectEndpoint(connection, "target", nodeIdMap, out var target))
                continue;

            result.Add(new(source, target, ProjectVertices(connection)));
        }
    }

    private static bool TryProjectEndpoint(
        JsonElement connection,
        string propertyName,
        IReadOnlyDictionary<string, string> nodeIdMap,
        out ActivityExecutionLayoutEndpoint endpoint)
    {
        endpoint = default!;
        if (!connection.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("nodeId", out var nodeIdElement) ||
            nodeIdElement.ValueKind != JsonValueKind.String)
            return false;

        var sourceNodeId = nodeIdElement.GetString();
        if (string.IsNullOrWhiteSpace(sourceNodeId) || !nodeIdMap.TryGetValue(sourceNodeId, out var executableNodeId))
            return false;

        string? port = null;
        if (value.TryGetProperty("port", out var portElement) && portElement.ValueKind == JsonValueKind.String)
        {
            var candidate = portElement.GetString();
            if (!string.IsNullOrWhiteSpace(candidate))
                port = candidate.Trim();
        }

        endpoint = new(executableNodeId, port);
        return true;
    }

    private static IReadOnlyList<ActivityExecutionLayoutVertex>? ProjectVertices(JsonElement connection)
    {
        if (!connection.TryGetProperty("vertices", out var vertices) || vertices.ValueKind != JsonValueKind.Array)
            return null;

        var result = new List<ActivityExecutionLayoutVertex>();
        foreach (var vertex in vertices.EnumerateArray())
        {
            if (vertex.ValueKind != JsonValueKind.Object ||
                !vertex.TryGetProperty("x", out var xElement) ||
                !vertex.TryGetProperty("y", out var yElement) ||
                xElement.ValueKind != JsonValueKind.Number ||
                yElement.ValueKind != JsonValueKind.Number ||
                !xElement.TryGetDouble(out var x) ||
                !yElement.TryGetDouble(out var y) ||
                !double.IsFinite(x) ||
                !double.IsFinite(y))
                continue;

            result.Add(new(x, y));
        }

        return result.Count == 0 ? null : result;
    }
}

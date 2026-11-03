using System.Text.Json;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Agent.Workflows.Services;

/// <summary>
/// The version tool's own structural reading of an exact version's authored state, and its own comparison of two exact
/// versions (spec 1193 R6). Children are projected only by the owning activity modules' structure handlers, through the
/// activity structure service; connections are read from a container's structure as the conventional <c>connections</c>
/// list of source and target node ids and ports (the shape the Runtime inspector also reads). Input values never leave this
/// class: only their names, and whether they differ.
/// </summary>
internal static class WorkflowVersionStructure
{
    /// <summary>At most this many activities are walked; a larger version is reported as truncated.</summary>
    internal const int WalkLimit = 2000;
    internal const int ListLimit = 100;
    private const int DepthLimit = 64;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal sealed record Activity(string NodeId, string ActivityVersionId, IReadOnlyList<string> ScopePath, string? Slot,
        IReadOnlyDictionary<string, string> Inputs, IReadOnlyDictionary<string, string> Outputs);

    internal sealed record Connection(string Container, string Source, string? SourcePort, string Target, string? TargetPort);

    internal sealed record Flattened(IReadOnlyList<Activity> Activities, IReadOnlyList<Connection> Connections, bool Truncated);

    internal static Flattened Flatten(ActivityNode? root, IActivityStructureService structure)
    {
        var activities = new List<Activity>();
        var connections = new List<Connection>();
        var truncated = false;
        void Visit(ActivityNode node, IReadOnlyList<string> scope, string? slot)
        {
            if (activities.Count >= WalkLimit || scope.Count > DepthLimit) { truncated = true; return; }
            activities.Add(new(node.NodeId, node.ActivityVersionId, scope, slot, Arguments(node.Inputs), Arguments(node.Outputs)));
            connections.AddRange(Connections(node));
            var inner = scope.Append(node.NodeId).ToArray();
            foreach (var projection in structure.ProjectChildren(node))
                foreach (var child in projection.Activities)
                    Visit(child, inner, projection.Name);
        }
        if (root is not null) Visit(root, [], null);
        return new(activities, connections, truncated);
    }

    /// <summary>The version section of the tool's result: bounded activities by scope path (input names only) and connections.</summary>
    internal static object Describe(Flattened flattened, int limit = ListLimit) => new
    {
        activityCount = flattened.Activities.Count,
        activities = flattened.Activities.Take(limit).Select(Row).ToArray(),
        activitiesTruncated = flattened.Truncated || flattened.Activities.Count > limit,
        connections = flattened.Connections.Take(limit).Select(ConnectionRow).ToArray(),
        connectionsTruncated = flattened.Truncated || flattened.Connections.Count > limit
    };

    /// <summary>
    /// The changes of <paramref name="version"/> relative to <paramref name="comparedTo"/>: activities only in the version
    /// (added) or only in the compared version (removed), activities in both whose catalog version, scope or authored inputs
    /// or outputs differ (changed, naming the differing input names), and connections added and removed. Each list is bounded.
    /// </summary>
    internal static object Compare(Flattened version, Flattened comparedTo, int limit = ListLimit)
    {
        var theirs = comparedTo.Activities.GroupBy(item => item.NodeId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var mine = version.Activities.Select(item => item.NodeId).ToHashSet(StringComparer.Ordinal);
        var added = version.Activities.Where(item => !theirs.ContainsKey(item.NodeId)).OrderBy(item => item.NodeId, StringComparer.Ordinal).ToArray();
        var removed = comparedTo.Activities.Where(item => !mine.Contains(item.NodeId)).OrderBy(item => item.NodeId, StringComparer.Ordinal).ToArray();
        var changed = new List<object>();
        foreach (var item in version.Activities.OrderBy(item => item.NodeId, StringComparer.Ordinal))
        {
            if (!theirs.TryGetValue(item.NodeId, out var other)) continue;
            var aspects = new List<string>();
            if (item.ActivityVersionId != other.ActivityVersionId) aspects.Add("activityVersion");
            var inputs = Differing(item.Inputs, other.Inputs);
            if (inputs.Length > 0) aspects.Add("inputs");
            if (Differing(item.Outputs, other.Outputs).Length > 0) aspects.Add("outputs");
            if (!item.ScopePath.SequenceEqual(other.ScopePath, StringComparer.Ordinal) || item.Slot != other.Slot) aspects.Add("scope");
            if (aspects.Count > 0) changed.Add(new { nodeId = item.NodeId, scopePath = item.ScopePath, aspects = aspects.Order(StringComparer.Ordinal).ToArray(), inputs });
        }
        var mineConnections = version.Connections.ToHashSet();
        var theirConnections = comparedTo.Connections.ToHashSet();
        var connectionsAdded = version.Connections.Where(item => !theirConnections.Contains(item)).Distinct().ToArray();
        var connectionsRemoved = comparedTo.Connections.Where(item => !mineConnections.Contains(item)).Distinct().ToArray();
        return new
        {
            computedBy = "workflow_read_version",
            note = "This is workflow_read_version's own structural comparison of the two exact versions the workflow design service returned, by node id and "
                + "scope path. It is not a semantic diff of activity behaviour, and input values are compared without being disclosed.",
            activitiesAdded = added.Take(limit).Select(Summary).ToArray(),
            activitiesRemoved = removed.Take(limit).Select(Summary).ToArray(),
            activitiesChanged = changed.Take(limit).ToArray(),
            connectionsAdded = connectionsAdded.Take(limit).Select(ConnectionRow).ToArray(),
            connectionsRemoved = connectionsRemoved.Take(limit).Select(ConnectionRow).ToArray(),
            truncated = version.Truncated || comparedTo.Truncated || new[] { added.Length, removed.Length, changed.Count, connectionsAdded.Length, connectionsRemoved.Length }
                .Any(count => count > limit)
        };
    }

    private static object Row(Activity item) => new
    {
        nodeId = item.NodeId, activityVersionId = item.ActivityVersionId, scopePath = item.ScopePath, slot = item.Slot,
        inputs = item.Inputs.Keys.Order(StringComparer.Ordinal).ToArray()
    };

    private static object Summary(Activity item) => new { nodeId = item.NodeId, activityVersionId = item.ActivityVersionId, scopePath = item.ScopePath, slot = item.Slot };

    private static object ConnectionRow(Connection item) => new
    {
        container = item.Container, source = item.Source, sourcePort = item.SourcePort, target = item.Target, targetPort = item.TargetPort
    };

    private static string[] Differing(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Keys.Union(right.Keys, StringComparer.Ordinal)
            .Where(key => !left.TryGetValue(key, out var mine) || !right.TryGetValue(key, out var theirs) || mine != theirs)
            .Order(StringComparer.Ordinal).ToArray();

    // Each argument's whole authored state by its reference key, compared as JSON text and never disclosed.
    private static IReadOnlyDictionary<string, string> Arguments(IEnumerable<ArgumentState>? arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var argument in arguments ?? [])
            if (!string.IsNullOrEmpty(argument.ReferenceKey)) result[argument.ReferenceKey] = JsonSerializer.Serialize(argument, JsonOptions);
        return result;
    }

    private static IEnumerable<Connection> Connections(ActivityNode node)
    {
        if (node.Structure?.Payload is not { ValueKind: JsonValueKind.Object } payload
            || !payload.TryGetProperty("connections", out var list) || list.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("source", out var source) || !item.TryGetProperty("target", out var target)) continue;
            if (Endpoint(source) is not { } from || Endpoint(target) is not { } to) continue;
            yield return new(node.NodeId, from.NodeId, from.Port, to.NodeId, to.Port);
        }
    }

    private static (string NodeId, string? Port)? Endpoint(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("nodeId", out var id) || id.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(id.GetString())) return null;
        var port = value.TryGetProperty("port", out var portValue) && portValue.ValueKind == JsonValueKind.String ? portValue.GetString() : null;
        return (id.GetString()!, port);
    }
}

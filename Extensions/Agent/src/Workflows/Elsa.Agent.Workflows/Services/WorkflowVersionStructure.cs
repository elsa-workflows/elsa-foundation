using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Agent.Workflows.Services;

/// <summary>
/// Weaver's own structural reading of an exact version's authored state, and its own comparison of two exact versions,
/// computed over two reads from the owning design services (spec 1193 R6). Children are projected only by the owning
/// activity modules' structure handlers, through the activity structure service; a container whose structure no installed
/// handler projects is reported as an incomplete reading, never as having no children. Connections are read from a
/// container's structure as the conventional <c>connections</c> list of source and target node ids and ports (the shape the
/// Runtime inspector also reads), each with the container's metadata for it (<c>connectionMetadata</c> by connection id), and
/// a child's metadata in its container (<c>nodeMetadata</c> by node id) belongs to that child. The rest of a container's
/// structure (its start node, cases and variables) is compared as one normalized signature, with the projected children,
/// the connections and that per-element metadata taken out. The order of a slot's children is compared only for an ordered
/// container: a container that wires its children with connections (a flowchart) runs them by its connections, so the order
/// of its list carries no meaning. Input and output values never leave this class: only their names, and whether they differ.
/// </summary>
internal static class WorkflowVersionStructure
{
    /// <summary>At most this many activities are walked; a larger version is reported as truncated.</summary>
    internal const int WalkLimit = 2000;
    internal const int ListLimit = 100;
    private const int DepthLimit = 64;
    private const int ReasonLimit = 10;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal sealed record Activity(string NodeId, string ActivityVersionId, IReadOnlyList<string> ScopePath, string? Slot,
        IReadOnlyDictionary<string, string> Inputs, IReadOnlyDictionary<string, string> Outputs, string? Intrinsic, string Placement,
        string? Structure, IReadOnlyDictionary<string, IReadOnlyList<string>> SlotOrder, string? ContainerMetadata);

    /// <summary>
    /// One connection, identified within its container as the flowchart identifies it: its <c>id</c>, or without one
    /// <c>{source}:{sourcePort}->{target}</c> (FlowchartGraph.GetConnectionId). Its container's metadata for that identity is
    /// compared with it, never part of its identity; so are its endpoints, which an identified connection can change.
    /// </summary>
    internal sealed record Connection(string Container, string Key, string Source, string? SourcePort, string Target, string? TargetPort)
    {
        public string? Metadata { get; init; }
        public bool Equals(Connection? other) => other is not null && Container == other.Container && Key == other.Key;
        public override int GetHashCode() => HashCode.Combine(Container, Key);
        public bool SameEndpoints(Connection other) => Source == other.Source && SourcePort == other.SourcePort && Target == other.Target && TargetPort == other.TargetPort;
    }

    /// <summary>The flowchart's identity of a connection: its id, or the key it gives an id-less connection.</summary>
    internal static string ConnectionKey(string? id, string source, string? sourcePort, string target) => id ?? $"{source}:{sourcePort}->{target}";

    /// <summary>
    /// One version's reading. <c>Truncated</c>: the walk stopped at its bound. <c>Incomplete</c>: some container's children
    /// could not be projected, so the reading (and any comparison over it) does not cover them; the reasons say which.
    /// </summary>
    internal sealed record Flattened(IReadOnlyList<Activity> Activities, IReadOnlyList<Connection> Connections, bool Truncated,
        IReadOnlyList<string> IncompleteReasons)
    {
        public bool Incomplete => IncompleteReasons.Count > 0;
    }

    internal static Flattened Flatten(ActivityNode? root, IActivityStructureService structure)
    {
        var activities = new List<Activity>();
        var connections = new List<Connection>();
        var reasons = new List<string>();
        var truncated = false;
        void Visit(ActivityNode node, IReadOnlyList<string> scope, string? slot, string? containerMetadata)
        {
            if (activities.Count >= WalkLimit || scope.Count > DepthLimit) { truncated = true; return; }
            IReadOnlyCollection<ActivityChildProjection> projections = [];
            if (node.Structure is { } authored)
            {
                if (!structure.HasHandler(authored))
                    reasons.Add($"No installed structure handler reads '{node.NodeId}' ({authored.Kind} {authored.SchemaVersion}); its children were not read.");
                else
                {
                    try { projections = structure.ProjectChildren(node); }
                    catch (Exception exception) when (exception is InvalidOperationException or JsonException or ArgumentException or NotSupportedException)
                    {
                        reasons.Add($"The structure of '{node.NodeId}' could not be projected; its children were not read.");
                    }
                }
            }
            var children = projections.SelectMany(item => item.Activities).Select(child => child.NodeId).ToHashSet(StringComparer.Ordinal);
            var wired = Connections(node).ToArray();
            var claimed = wired.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            var graph = node.Structure?.Payload is { ValueKind: JsonValueKind.Object } payload
                && payload.TryGetProperty("connections", out var list) && list.ValueKind == JsonValueKind.Array;
            activities.Add(new(node.NodeId, node.ActivityVersionId, scope, slot, Arguments(node.Inputs), Arguments(node.Outputs),
                node.Intrinsic is null ? null : JsonSerializer.Serialize(node.Intrinsic, JsonOptions), node.ExecutionPlacement.ToString(),
                StructureSignature(node.Structure, children, claimed),
                // Only an ordered container's child order is compared (see the summary).
                graph ? new Dictionary<string, IReadOnlyList<string>>()
                    : projections.GroupBy(item => item.Name, StringComparer.Ordinal).ToDictionary(group => group.Key,
                        group => (IReadOnlyList<string>)group.SelectMany(item => item.Activities).Select(child => child.NodeId).ToArray(), StringComparer.Ordinal),
                containerMetadata));
            connections.AddRange(wired);
            var inner = scope.Append(node.NodeId).ToArray();
            foreach (var projection in projections)
                foreach (var child in projection.Activities)
                    Visit(child, inner, projection.Name, Metadata(node.Structure, "nodeMetadata", child.NodeId));
        }
        if (root is not null) Visit(root, [], null, null);
        return new(activities, connections, truncated, reasons);
    }

    /// <summary>The version section of the tool's result: bounded activities by scope path (input names only) and connections.</summary>
    internal static object Describe(Flattened flattened, int limit = ListLimit) => new
    {
        activityCount = flattened.Activities.Count,
        activities = flattened.Activities.Take(limit).Select(Row).ToArray(),
        activitiesTruncated = flattened.Truncated || flattened.Activities.Count > limit,
        connections = flattened.Connections.Take(limit).Select(ConnectionRow).ToArray(),
        connectionsTruncated = flattened.Truncated || flattened.Connections.Count > limit,
        incomplete = flattened.Incomplete,
        incompleteReasons = flattened.IncompleteReasons.Take(ReasonLimit).ToArray()
    };

    /// <summary>
    /// The changes of <paramref name="version"/> relative to <paramref name="comparedTo"/>: activities only in the version
    /// (added) or only in the compared version (removed), and activities in both that changed, each with its aspects:
    /// <c>activityVersion</c>, <c>inputs</c> (naming the differing inputs), <c>outputs</c>, <c>scope</c> (another container or
    /// slot), <c>intrinsic</c>, <c>placement</c>, <c>containerMetadata</c> (its metadata in its container, such as a
    /// flowchart join policy), <c>structure</c> (the container's structure other than its children, connections and their
    /// metadata, such as its start node or cases) and <c>childOrder</c> (children common to both in another order in a slot
    /// of an ordered container, named in <c>reorderedSlots</c>); and connections added, removed and changed (the same
    /// endpoints with other metadata). Each list is bounded.
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
            if (item.Intrinsic != other.Intrinsic) aspects.Add("intrinsic");
            if (item.Placement != other.Placement) aspects.Add("placement");
            if (item.Structure != other.Structure) aspects.Add("structure");
            if (item.ContainerMetadata != other.ContainerMetadata) aspects.Add("containerMetadata");
            var reordered = item.SlotOrder
                .Where(slot => other.SlotOrder.TryGetValue(slot.Key, out var before)
                    && !slot.Value.Where(before.Contains).SequenceEqual(before.Where(slot.Value.Contains), StringComparer.Ordinal))
                .Select(slot => slot.Key).Order(StringComparer.Ordinal).ToArray();
            if (reordered.Length > 0) aspects.Add("childOrder");
            if (aspects.Count > 0)
                changed.Add(new { nodeId = item.NodeId, scopePath = item.ScopePath, aspects = aspects.Order(StringComparer.Ordinal).ToArray(), inputs, reorderedSlots = reordered });
        }
        // Connections by the flowchart's own identity (container and id or fallback key): one entry per identity.
        var mineConnections = version.Connections.GroupBy(item => item).ToDictionary(group => group.Key, group => group.First());
        var theirConnections = comparedTo.Connections.GroupBy(item => item).ToDictionary(group => group.Key, group => group.First());
        var connectionsAdded = mineConnections.Keys.Where(item => !theirConnections.ContainsKey(item)).ToArray();
        var connectionsRemoved = theirConnections.Keys.Where(item => !mineConnections.ContainsKey(item)).ToArray();
        // The same connection in both whose endpoints (rewired) or metadata (its label, default flag, policy or configuration) changed.
        var connectionsChanged = mineConnections.Values
            .Where(item => theirConnections.TryGetValue(item, out var before) && (before.Metadata != item.Metadata || !before.SameEndpoints(item)))
            .Select(item => (Now: item, Before: theirConnections[item])).ToArray();
        var reasons = version.IncompleteReasons.Concat(comparedTo.IncompleteReasons).Distinct().ToArray();
        return new
        {
            computedBy = "workflow_read_version",
            note = "This is Weaver's own structural comparison (workflow_read_version), computed over the two exact versions the workflow design service "
                + "returned, by node id and scope path. It is not a semantic diff of activity behaviour, and input and output values are compared without "
                + "being disclosed." + (reasons.Length > 0 ? " It is incomplete: some containers' children could not be read, so changes inside them are not covered." : ""),
            activitiesAdded = added.Take(limit).Select(Summary).ToArray(),
            activitiesRemoved = removed.Take(limit).Select(Summary).ToArray(),
            activitiesChanged = changed.Take(limit).ToArray(),
            connectionsAdded = connectionsAdded.Take(limit).Select(ConnectionRow).ToArray(),
            connectionsRemoved = connectionsRemoved.Take(limit).Select(ConnectionRow).ToArray(),
            connectionsChanged = connectionsChanged.Take(limit).Select(item => ChangedConnectionRow(item.Now, item.Before)).ToArray(),
            truncated = version.Truncated || comparedTo.Truncated
                || new[] { added.Length, removed.Length, changed.Count, connectionsAdded.Length, connectionsRemoved.Length, connectionsChanged.Length }
                .Any(count => count > limit),
            incomplete = reasons.Length > 0,
            incompleteReasons = reasons.Take(ReasonLimit).ToArray()
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
        container = item.Container, connectionId = item.Key, source = item.Source, sourcePort = item.SourcePort, target = item.Target, targetPort = item.TargetPort
    };

    private static object ChangedConnectionRow(Connection now, Connection before) => new
    {
        container = now.Container, connectionId = now.Key, source = now.Source, sourcePort = now.SourcePort, target = now.Target, targetPort = now.TargetPort,
        aspects = new[] { now.SameEndpoints(before) ? null : "endpoints", now.Metadata == before.Metadata ? null : "metadata" }.OfType<string>().ToArray()
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

    /// <summary>
    /// A container's structure without its projected children (a child node object anywhere in the payload) and without its
    /// connections, which are compared on their own; the kind and schema version are part of it. Object properties are
    /// ordered, so equal structures read equal.
    /// </summary>
    private static string? StructureSignature(ActivityNodeStructure? structure, IReadOnlySet<string> children, IReadOnlySet<string> connections)
    {
        if (structure is null) return null;
        var payload = JsonNode.Parse(structure.Payload.GetRawText());
        // The connections, and each metadata entry claimed by a connection or a projected child, are compared with what they
        // describe; an entry no connection or child claims stays part of the container's own structure.
        if (payload is JsonObject body)
        {
            body.Remove("connections");
            Unclaim(body, "connectionMetadata", connections);
            Unclaim(body, "nodeMetadata", children);
        }
        return JsonSerializer.Serialize(new { kind = structure.Kind, schemaVersion = structure.SchemaVersion, payload = Normalize(payload, children) }, JsonOptions);
    }

    private static void Unclaim(JsonObject body, string property, IReadOnlySet<string> claimed)
    {
        if (body[property] is not JsonObject map) return;
        foreach (var key in map.Select(entry => entry.Key).Where(claimed.Contains).ToArray()) map.Remove(key);
        if (map.Count == 0) body.Remove(property);
    }

    private static JsonNode? Normalize(JsonNode? node, IReadOnlySet<string> children) => node switch
    {
        JsonObject value when IsChild(value, children) => JsonValue.Create("child"),
        JsonObject value => new JsonObject(value.OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(property.Key, Normalize(property.Value, children)))),
        JsonArray items => new JsonArray(items.Where(item => item is not JsonObject child || !IsChild(child, children)).Select(item => Normalize(item, children)).ToArray()),
        null => null,
        _ => node.DeepClone()
    };

    // A projected child as the payload embeds it: an activity node object (node id and catalog version) the handler projected.
    private static bool IsChild(JsonObject value, IReadOnlySet<string> children) =>
        value.TryGetPropertyValue("nodeId", out var id) && id is JsonValue nodeId && nodeId.TryGetValue<string>(out var text) && children.Contains(text)
        && value.ContainsKey("activityVersionId");

    private static IEnumerable<Connection> Connections(ActivityNode node)
    {
        if (node.Structure?.Payload is not { ValueKind: JsonValueKind.Object } payload
            || !payload.TryGetProperty("connections", out var list) || list.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("source", out var source) || !item.TryGetProperty("target", out var target)) continue;
            if (Endpoint(source) is not { } from || Endpoint(target) is not { } to) continue;
            var id = item.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()) ? value.GetString() : null;
            var key = ConnectionKey(id, from.NodeId, from.Port, to.NodeId);
            yield return new(node.NodeId, key, from.NodeId, from.Port, to.NodeId, to.Port) { Metadata = Metadata(node.Structure, "connectionMetadata", key) };
        }
    }

    // A container's metadata entry for one element (a child or a connection) by its id, as normalized JSON text, or null.
    private static string? Metadata(ActivityNodeStructure? structure, string property, string key)
    {
        if (structure?.Payload is not { ValueKind: JsonValueKind.Object } payload || !payload.TryGetProperty(property, out var map)
            || map.ValueKind != JsonValueKind.Object || !map.TryGetProperty(key, out var entry)) return null;
        return Normalize(JsonNode.Parse(entry.GetRawText()), new HashSet<string>())?.ToJsonString();
    }

    private static (string NodeId, string? Port)? Endpoint(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("nodeId", out var id) || id.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(id.GetString())) return null;
        var port = value.TryGetProperty("port", out var portValue) && portValue.ValueKind == JsonValueKind.String ? portValue.GetString() : null;
        return (id.GetString()!, port);
    }
}

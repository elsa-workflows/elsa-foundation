using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// Flowchart-owned child-result analysis. A run starts at its selected entry: the start node or a direct child
/// without an incoming connection. Dominance therefore uses a virtual root above all those legal entries.
/// </summary>
internal sealed class FlowchartChildResultAnalysis(
    IReadOnlyCollection<string> entryNodeIds,
    IReadOnlyCollection<string> nodeIds,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> successors,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> predecessors)
{
    public ActivityChildResultFacts Assess(string producer, string consumer)
    {
        var sameChild = StringComparer.Ordinal.Equals(producer, consumer);
        var forward = IsReachable(producer, consumer);
        var backward = IsReachable(consumer, producer);
        var dominates = Dominates(producer, consumer);
        var exclusive = AreExclusive(producer, consumer);
        var cyclicBackEdge = sameChild
            ? IsReachable(producer, producer, requireEdge: true)
            : forward && backward && !dominates;
        var availability = sameChild
            ? ActivityChildResultAvailability.Unavailable
            : dominates
                ? ActivityChildResultAvailability.Definite
                : forward && entryNodeIds.Any(entry => IsReachable(entry, producer))
                    ? ActivityChildResultAvailability.Conditional
                    : ActivityChildResultAvailability.Unavailable;

        // Preserve the existing concurrent-write relation; branch write/merge policy stays with its validator.
        return new(availability, cyclicBackEdge, !forward && !backward && !exclusive, exclusive);
    }

    private bool IsReachable(string source, string target, bool requireEdge = false)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(source);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
                continue;

            foreach (var next in successors.GetValueOrDefault(current, []))
            {
                if (StringComparer.Ordinal.Equals(next, target) && (!StringComparer.Ordinal.Equals(source, target) || requireEdge))
                    return true;
                pending.Push(next);
            }
        }

        return !requireEdge && StringComparer.Ordinal.Equals(source, target);
    }

    /// <summary>Whether every path from every entry to the node passes the candidate.</summary>
    private bool Dominates(string candidate, string node)
    {
        var entries = entryNodeIds.ToHashSet(StringComparer.Ordinal);
        var reachable = nodeIds.Where(id => entries.Any(entry => IsReachable(entry, id))).ToHashSet(StringComparer.Ordinal);
        if (!reachable.Contains(candidate) || !reachable.Contains(node))
            return false;

        var dominators = reachable.ToDictionary(
            id => id,
            id => entries.Contains(id)
                ? new HashSet<string>([id], StringComparer.Ordinal)
                : new HashSet<string>(reachable, StringComparer.Ordinal),
            StringComparer.Ordinal);

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var current in reachable.Where(id => !entries.Contains(id)))
            {
                var incoming = predecessors.GetValueOrDefault(current, []).Where(reachable.Contains).ToArray();
                var next = incoming.Length == 0
                    ? []
                    : new HashSet<string>(dominators[incoming[0]], StringComparer.Ordinal);
                foreach (var predecessor in incoming.Skip(1))
                    next.IntersectWith(dominators[predecessor]);
                next.Add(current);
                if (dominators[current].SetEquals(next))
                    continue;
                dominators[current] = next;
                changed = true;
            }
        }

        return dominators[node].Contains(candidate);
    }

    /// <summary>Whether each node has a legal entry and no entry can reach both nodes.</summary>
    private bool AreExclusive(string left, string right)
    {
        var leftEntries = EntriesReaching(left);
        var rightEntries = EntriesReaching(right);
        return leftEntries.Count > 0 && rightEntries.Count > 0 && !leftEntries.Overlaps(rightEntries);
    }

    private HashSet<string> EntriesReaching(string node) =>
        entryNodeIds.Where(entry => IsReachable(entry, node)).ToHashSet(StringComparer.Ordinal);

    public static FlowchartChildResultAnalysis? TryCreate(JsonElement? payload, IReadOnlyCollection<string> childNodeIds)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value ||
            !TryGetArray(value, "connections", out var connections))
            return null;

        var startNodeId = TryGetString(value, "startNodeId");
        if (string.IsNullOrWhiteSpace(startNodeId))
            return null;

        var edges = new List<(string Source, string Target)>();
        foreach (var connection in connections.EnumerateArray())
        {
            if (!connection.TryGetProperty("source", out var source) ||
                !connection.TryGetProperty("target", out var target))
                continue;
            var sourceId = TryGetString(source, "nodeId");
            var targetId = TryGetString(target, "nodeId");
            if (!string.IsNullOrWhiteSpace(sourceId) && !string.IsNullOrWhiteSpace(targetId))
                edges.Add((sourceId, targetId));
        }

        var targetIds = edges.Select(edge => edge.Target).ToHashSet(StringComparer.Ordinal);
        var entryNodeIds = childNodeIds
            .Where(id => !targetIds.Contains(id))
            .Order(StringComparer.Ordinal)
            .Prepend(startNodeId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var nodeIds = edges.SelectMany(edge => new[] { edge.Source, edge.Target }).Concat(entryNodeIds).ToHashSet(StringComparer.Ordinal);
        var successors = nodeIds.ToDictionary(
            id => id,
            id => (IReadOnlyCollection<string>)edges.Where(edge => StringComparer.Ordinal.Equals(edge.Source, id)).Select(edge => edge.Target).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        var predecessors = nodeIds.ToDictionary(
            id => id,
            id => (IReadOnlyCollection<string>)edges.Where(edge => StringComparer.Ordinal.Equals(edge.Target, id)).Select(edge => edge.Source).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        return new FlowchartChildResultAnalysis(entryNodeIds, nodeIds, successors, predecessors);
    }

    private static bool TryGetArray(JsonElement value, string propertyName, out JsonElement array)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out array) && array.ValueKind == JsonValueKind.Array)
            return true;
        array = default;
        return false;
    }

    private static string? TryGetString(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kernel.Native;

internal enum StructuredMigrationFinding
{
    UnsupportedCheckpoint,
    VariableDeclarations,
    LiveNode,
    LiveParent,
    AuthoredNode,
    SequencePrefix,
    ChildLayout,
}

/// <summary>Checks the structured Native subset against a committed checkpoint without making a turn.</summary>
internal static class StructuredMigration
{
    // The caller verifies that the actor is running or suspended, its bound definition is source,
    // and the checkpoint is at a readable model and activity version.
    internal static StructuredMigrationFinding? Validate(Workflow source, Workflow target, NativeState state)
    {
        if (!state.Started || state.Mode is not (Modes.Balanced or Modes.Durable) ||
            (state.Frames.Count == 0 && state.Agenda.Count == 0) ||
            state.Waits.Values.Any(wait => wait.Kind == "job" || !state.Frames.ContainsKey(wait.Frame)) ||
            state.InterventionEnabled || state.CheckpointControl || state.Inboxes is { Count: > 0 })
            return StructuredMigrationFinding.UnsupportedCheckpoint;

        // This slice compares the declaration set. Adding defaults to an existing scope belongs to the apply step.
        if (!SameEntries(source.Variables, target.Variables, SameJson))
            return StructuredMigrationFinding.VariableDeclarations;

        var live = new HashSet<string>(state.Results.Keys, StringComparer.Ordinal);
        if (state.TriggerNode is { } trigger) live.Add(trigger);
        foreach (var frame in state.Frames.Values) live.Add(frame.Node);
        foreach (var work in state.Agenda)
            if (work.Node is { } node) live.Add(node);

        foreach (var node in live.ToArray())
            for (var parent = source.Parents.GetValueOrDefault(node); parent is not null; parent = source.Parents.GetValueOrDefault(parent.Id))
                live.Add(parent.Id);

        var comparisons = new List<(Node Before, Node After)>(live.Count);
        foreach (var id in live.Order(StringComparer.Ordinal))
        {
            if (!source.Nodes.TryGetValue(id, out var before) || !target.Nodes.TryGetValue(id, out var after) ||
                before.Type != after.Type)
                return StructuredMigrationFinding.LiveNode;
            if (source.Parents.GetValueOrDefault(id)?.Id != target.Parents.GetValueOrDefault(id)?.Id)
                return StructuredMigrationFinding.LiveParent;
            if (!SameAuthoredNode(before, after))
                return StructuredMigrationFinding.AuthoredNode;
            comparisons.Add((before, after));
        }

        var framesByNode = state.Frames.Values.ToLookup(frame => frame.Node, StringComparer.Ordinal);
        var starts = state.Agenda.Where(work => !work.Resume && !work.Gone && work.Node is not null)
            .Select(work => work.Node!).ToHashSet(StringComparer.Ordinal);
        var children = state.Frames.Values.Select(frame => (frame.Parent, Node: (string?)frame.Node))
            .Concat(state.Agenda.Where(work => work.Node is not null).Select(work => (work.Parent, work.Node)))
            .ToLookup(child => child.Parent, child => child.Node!, StringComparer.Ordinal);
        foreach (var (before, after) in comparisons)
        {
            if (!typeof(CompositeActivity).IsAssignableFrom(before.Type)) continue;
            // A scheduled start has no Sequence cursor or chosen If branch until its frame executes.
            // Another activation of the same node may already have a frame and must still be checked.
            if ((before.Type == typeof(Sequence) || before.Type == typeof(If)) &&
                !framesByNode.Contains(before.Id) && starts.Contains(before.Id)) continue;
            var selected = framesByNode[before.Id].Select(frame => children[frame.Id].ToArray());
            if (before.Type == typeof(Sequence))
            {
                if (!SameChildren(before.Children, after.Children) &&
                    !CanEditSequence(before, after, selected))
                    return StructuredMigrationFinding.SequencePrefix;
            }
            else if (before.Type == typeof(If))
            {
                if (!SameChildren(before.Children, after.Children) && !CanEditIf(before, after, selected))
                    return StructuredMigrationFinding.ChildLayout;
            }
            else if (before.Type == typeof(Parallel))
            {
                if (!SameChildren(before.Children.OrderBy(child => child.Id, StringComparer.Ordinal),
                        after.Children.OrderBy(child => child.Id, StringComparer.Ordinal)))
                    return StructuredMigrationFinding.ChildLayout;
            }
            else if (before.Type == typeof(ForEach) || before.Type == typeof(While))
            {
                if (before.Children.Count != 1 || !SameChildren(before.Children, after.Children))
                    return StructuredMigrationFinding.ChildLayout;
            }
            else
            {
                // A graph, statechart or custom composite may refer to additional nodes in private state.
                return StructuredMigrationFinding.UnsupportedCheckpoint;
            }
        }

        return null;
    }

    private static bool CanEditSequence(Node before, Node after, IEnumerable<string[]> selections)
    {
        var active = selections.ToArray();
        if (active.Length == 0) return false;
        foreach (var selected in active)
        {
            if (selected.Length != 1) return false;
            var index = before.Children.FindIndex(child => child.Id == selected[0]);
            if (index < 0 || after.Children.Count <= index ||
                !SameChildren(before.Children.Take(index + 1), after.Children.Take(index + 1)))
                return false;
        }
        return true;
    }

    private static bool CanEditIf(Node before, Node after, IEnumerable<string[]> selections)
    {
        var active = selections.ToArray();
        if (active.Length == 0) return false;
        foreach (var selected in active)
        {
            if (selected.Length != 1) return false;
            var branch = before.Children.Find(child => child.Id == selected[0]);
            if (branch is null || after.Children.FirstOrDefault(child => child.Slot == branch.Slot)?.Id != branch.Id)
                return false;
        }
        return true;
    }

    private static bool SameChildren(IEnumerable<Node> before, IEnumerable<Node> after) =>
        before.Select(child => (child.Id, child.Slot)).SequenceEqual(after.Select(child => (child.Id, child.Slot)));

    private static bool SameAuthoredNode(Node before, Node after) =>
        before.Slot == after.Slot && before.JobTimeout == after.JobTimeout &&
        SameEntries(before.Inputs, after.Inputs, SameBinding) &&
        SameEntries(before.Captures, after.Captures, StringComparer.Ordinal.Equals) &&
        before.Connections.SequenceEqual(after.Connections) &&
        SameEntries(before.Gateways, after.Gateways, (left, right) => left == right) &&
        JsonNode.DeepEquals(NativeRecoveryMaterial.DescribeCases(before), NativeRecoveryMaterial.DescribeCases(after)) &&
        JsonNode.DeepEquals(NativeRecoveryMaterial.DescribeGraph(before), NativeRecoveryMaterial.DescribeGraph(after));

    private static bool SameBinding(Binding before, Binding after) =>
        (before, after) switch
        {
            (LiteralBinding left, LiteralBinding right) => SameJson(left.Value, right.Value),
            (VariableBinding left, VariableBinding right) => left == right,
            (ResultBinding left, ResultBinding right) => left == right,
            (ExpressionBinding left, ExpressionBinding right) => left == right,
            _ => false,
        };

    // Native copies each root literal before hydration. An activity can inspect the options and typed
    // values of that copy, including options inherited by children and raw tokens in parsed JSON.
    private static bool SameJson(JsonNode? before, JsonNode? after) =>
        SameClonedNode(before?.DeepClone(), after?.DeepClone());

    private static bool SameClonedNode(JsonNode? before, JsonNode? after)
    {
        if (before?.Options?.PropertyNameCaseInsensitive != after?.Options?.PropertyNameCaseInsensitive)
            return false;
        return (before, after) switch
        {
            (null, null) => true,
            (JsonObject left, JsonObject right) =>
                left.Count == right.Count && left.Zip(right).All(pair =>
                    pair.First.Key == pair.Second.Key && SameClonedNode(pair.First.Value, pair.Second.Value)),
            (JsonArray left, JsonArray right) =>
                left.Count == right.Count && left.Zip(right).All(pair => SameClonedNode(pair.First, pair.Second)),
            (JsonValue left, JsonValue right) => SameScalar(left, right),
            _ => false,
        };
    }

    private static bool SameScalar(JsonValue before, JsonValue after)
    {
        if (before.GetType() != after.GetType() || before.ToJsonString() != after.ToJsonString()) return false;
        var left = before.GetValue<object>();
        var right = after.GetValue<object>();
        if (left is JsonElement element)
            return right is JsonElement other && element.GetRawText() == other.GetRawText();
        return left.GetType() == right.GetType() && left.Equals(right);
    }

    private static bool SameEntries<T>(IReadOnlyDictionary<string, T> before, IReadOnlyDictionary<string, T> after, Func<T, T, bool> equals)
    {
        if (before.Count != after.Count) return false;
        var left = before.OrderBy(entry => entry.Key, StringComparer.Ordinal);
        var right = after.OrderBy(entry => entry.Key, StringComparer.Ordinal);
        return left.Zip(right).All(pair => pair.First.Key == pair.Second.Key && equals(pair.First.Value, pair.Second.Value));
    }
}

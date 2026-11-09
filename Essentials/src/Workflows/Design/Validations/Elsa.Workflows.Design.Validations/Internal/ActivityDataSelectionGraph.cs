using System.Collections.Immutable;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Internal;

/// <summary>One request's structural placement; execution facts remain with each owning handler.</summary>
internal sealed class ActivityDataSelectionGraph(IActivityStructureService structures)
{
    private readonly List<Location> _nodes = [];
    private readonly Dictionary<string, Location> _byId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _duplicates = new(StringComparer.Ordinal);
    private readonly Dictionary<(Location Container, string Producer, string Consumer), ActivityChildResultFacts> _facts = [];

    public IReadOnlyList<Location> Nodes => _nodes;
    public bool IsAmbiguous(string nodeId) => _duplicates.Contains(nodeId);
    public Location? Find(string nodeId) => _byId.GetValueOrDefault(nodeId);

    public void Build(ActivityNode? root, int maxDepth, ICollection<ValidationError> findings, CancellationToken cancellationToken,
        ImmutableDictionary<string, ImmutableArray<ArgumentState>> compiledBoundaryInputs)
    {
        if (compiledBoundaryInputs is null || compiledBoundaryInputs.Count > 10000)
        {
            findings.Add(new("$workflow", "ActivityData/InvalidCompiledBoundaryInputs", "Compiled boundary input facts are missing or exceed the analysis limit."));
            return;
        }
        // Never inherit a caller-supplied dictionary comparer for canonical node identities.
        var boundaries = new Dictionary<string, ImmutableArray<ArgumentState>>(StringComparer.Ordinal);
        foreach (var (nodeId, inputs) in compiledBoundaryInputs)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || !boundaries.TryAdd(nodeId, inputs) || inputs.IsDefault ||
                inputs.Any(input => input is null || string.IsNullOrWhiteSpace(input.ReferenceKey)) ||
                inputs.GroupBy(input => input.ReferenceKey, StringComparer.Ordinal).Any(group => group.Count() > 1))
                findings.Add(new(nodeId, "ActivityData/InvalidCompiledBoundaryInputs", "Compiled boundary inputs must have an exact node identity and unique, nonempty input keys."));
        }
        if (findings.Count > 0) return;
        var pending = new Stack<(ActivityNode Node, Location? Parent, int Depth)>();
        if (root is not null) pending.Push((root, null, 0));
        while (pending.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_nodes.Count >= 10000 || item.Depth > maxDepth)
            {
                findings.Add(new("$workflow", "ActivityData/AnalysisLimit", "The canonical graph exceeds the source-analysis limit."));
                break;
            }
            var children = structures.ProjectChildren(item.Node);
            ImmutableArray<ArgumentState> inputs = default;
            var boundary = !string.IsNullOrWhiteSpace(item.Node.NodeId) && boundaries.TryGetValue(item.Node.NodeId, out inputs);
            if (boundary && (item.Node.Intrinsic is not null || children.SelectMany(slot => slot.Activities).Any()))
                findings.Add(new(item.Node.NodeId, "ActivityData/InvalidCompiledBoundaryInputs", "Compiled boundary facts cannot replace an intrinsic or a node with authored children."));
            var projection = boundary ? ActivityStructureInputProjection.Empty : structures.ProjectInputs(item.Node);
            var location = new Location(boundary ? item.Node with { Structure = null } : item.Node, item.Parent, projection,
                boundary ? inputs : projection.ApplyToInputs(item.Node.Inputs));
            _nodes.Add(location);
            if (string.IsNullOrWhiteSpace(item.Node.NodeId) || !_byId.TryAdd(item.Node.NodeId, location))
            {
                _duplicates.Add(item.Node.NodeId);
                findings.Add(new(item.Node.NodeId, "ActivityData/AmbiguousNode", "Activity node identities must be nonempty and unique."));
            }
            foreach (var child in children.SelectMany(slot => slot.Activities).Reverse())
                pending.Push((child, location, item.Depth + 1));
        }
        foreach (var nodeId in boundaries.Keys)
            if (!_byId.ContainsKey(nodeId) || _duplicates.Contains(nodeId))
                findings.Add(new(nodeId, "ActivityData/InvalidCompiledBoundaryInputs", "Compiled boundary facts must name one reachable, unambiguous activity node."));
    }

    public ActivityChildResultFacts Assess(Location producer, Location consumer)
    {
        if (ReferenceEquals(producer, consumer)) return new(ActivityChildResultAvailability.Unavailable);
        var common = Common(producer, consumer);
        if (common is null) return new(ActivityChildResultAvailability.Unavailable);
        var producerChild = ChildBelow(producer, common);
        var consumerChild = ChildBelow(consumer, common);
        if (ReferenceEquals(producerChild, consumerChild)) return new(ActivityChildResultAvailability.Unavailable);
        return Pair(common, producerChild.Node.NodeId, consumerChild.Node.NodeId);
    }

    public bool IsBackEdge(Location producer, Location consumer)
    {
        for (var common = Common(producer, consumer); common is not null; common = common.Parent)
        {
            var facts = Pair(common, ChildBelow(producer, common).Node.NodeId, ChildBelow(consumer, common).Node.NodeId);
            if (facts.IsCyclicBackEdge == true) return true;
        }
        return false;
    }

    public static bool CrossesOutOfScope(Location producer, Location consumer) => producer.Parent is not null && consumer.Parent is not null
        && !ReferenceEquals(producer.Parent, consumer.Parent) && !IsAncestorOrSelf(producer.Parent, consumer.Parent);

    public bool HasExplicitReturn(Location producer) => _nodes.Any(node => ReferenceEquals(node.Parent, producer)
        && (node.Node.Intrinsic?.Kind == AuthoredWorkflowIntrinsicKind.Return || node.Node.ActivityVersionId == "elsa.intrinsic.return@1"));

    public int Rank(Location producer, Location consumer)
    {
        var common = Common(producer, consumer);
        if (common is null) return int.MaxValue;
        var completed = structures.ChildrenCompletedBefore(common.Node, ChildBelow(consumer, common).Node.NodeId);
        var rank = completed?.ToList().IndexOf(ChildBelow(producer, common).Node.NodeId) ?? -1;
        return rank >= 0 ? rank : int.MaxValue;
    }

    private ActivityChildResultFacts Pair(Location container, string producer, string consumer)
    {
        var key = (container, producer, consumer);
        if (!_facts.TryGetValue(key, out var facts))
            _facts[key] = facts = structures.AssessChildResult(container.Node, producer, consumer);
        return facts;
    }

    private static Location? Common(Location left, Location right)
    {
        var ancestors = new HashSet<Location>();
        for (var current = left.Parent; current is not null; current = current.Parent) ancestors.Add(current);
        for (var current = right.Parent; current is not null; current = current.Parent)
            if (ancestors.Contains(current)) return current;
        return null;
    }

    private static Location ChildBelow(Location node, Location ancestor)
    {
        while (node.Parent is not null && !ReferenceEquals(node.Parent, ancestor)) node = node.Parent;
        return node;
    }

    private static bool IsAncestorOrSelf(Location ancestor, Location node)
    {
        for (var current = node; current is not null; current = current.Parent)
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    internal sealed record Location(ActivityNode Node, Location? Parent, ActivityStructureInputProjection Projection,
        IReadOnlyCollection<ArgumentState> Inputs);
}

using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Services;

/// <summary>The kind of a declared variable write (spec 266).</summary>
public enum VariableWriteKind
{
    Capture,
    Set,
    Merge,
    Reduce
}

/// <summary>
/// A write to a variable that a node declares: an output capture, or a Set, Merge or Reduce intrinsic. The target is
/// normalized: a blank, <c>workflow</c>, <c>$workflow</c> or legacy <c>activity-graph</c> scope is the root scope.
/// </summary>
public sealed record VariableWrite(string NodeId, VariableWriteKind Kind, string ScopeId, string ReferenceKey, string? OutputKey = null);

/// <summary>A structure that did not declare what the analysis needed, so its children were treated as unordered.</summary>
public sealed record UndeclaredStructure(string NodeId, string Kind);

/// <summary>
/// One variable whose writes the definition does not order: every writer, the writers that can run more than once at
/// the same time, and the structures that declared nothing.
/// </summary>
public sealed record UnorderedVariableWriteFinding(
    string ScopeId,
    string ReferenceKey,
    IReadOnlyList<string> WriterNodeIds,
    IReadOnlyList<string> SelfOverlappingNodeIds,
    IReadOnlyList<UndeclaredStructure> UndeclaredStructures)
{
    /// <summary>The author-facing text: writers, last-commit-wins, the fixes and the read-modify-write pointer.</summary>
    public string Message
    {
        get
        {
            var writers = string.Join(", ", WriterNodeIds.Select(id => $"'{id}'"));
            var text = WriterNodeIds.Count == 1
                ? $"Variable '{ReferenceKey}' in scope '{ScopeId}' is written by node {writers}, whose runs the definition does not order."
                : $"Variable '{ReferenceKey}' in scope '{ScopeId}' is written by {WriterNodeIds.Count} nodes whose order the definition does not fix: {writers}.";
            foreach (var node in SelfOverlappingNodeIds)
                text += $" Node '{node}' can run more than once at the same time.";
            foreach (var structure in UndeclaredStructures)
                text += $" Structure '{structure.Kind}' (node '{structure.NodeId}') does not declare how its children are ordered, so they are treated as unordered.";
            return text +
                   " The last successfully committed write wins, so the final value can differ between runs." +
                   " To make it deterministic, order the branches, capture into separate variables, or combine them with one write after the join." +
                   " A step that reads the variable and writes it back is a read-modify-write: opt it in to the Fault lost-update mode where an overwrite must stop the step (ADR 0092).";
        }
    }
}

/// <summary>Two or more outputs of one node capture into the same variable.</summary>
public sealed record DuplicateCaptureTargetFinding(string NodeId, string ScopeId, string ReferenceKey, IReadOnlyList<string> OutputKeys)
{
    public string Message =>
        $"Outputs {string.Join(", ", OutputKeys.Select(key => $"'{key}'"))} of node '{NodeId}' all capture into variable '{ReferenceKey}' in scope '{ScopeId}'." +
        " They commit in one completion in a fixed order, so the last of them wins; capture each output into its own variable to keep every value.";
}

/// <summary>The result of <see cref="UnorderedVariableWriteAnalysis.Analyze"/>.</summary>
public sealed record UnorderedVariableWriteAnalysisResult(
    IReadOnlyList<UnorderedVariableWriteFinding> UnorderedWrites,
    IReadOnlyList<DuplicateCaptureTargetFinding> DuplicateCaptureTargets);

/// <summary>
/// Finds declared writes to one variable whose order the definition does not fix (spec 266). All ordering knowledge
/// comes from the structures' own declarations (<see cref="IActivityStructureHandler.RelateChildren"/> and
/// <see cref="IActivityStructureHandler.CompletesAfterItsChildren"/>); the analysis knows no structure kind.
/// </summary>
public static class UnorderedVariableWriteAnalysis
{
    private const string VariableExpressionType = "Variable";
    private const string LegacySetActivityVersionId = "elsa.intrinsic.set@1";

    /// <summary>
    /// The writes a node declares: its output captures into variables and, when <paramref name="includeIntrinsics"/>,
    /// its Set, Merge or Reduce intrinsic (including the legacy <c>elsa.intrinsic.set@1</c> node).
    /// </summary>
    public static IEnumerable<VariableWrite> DeclaredWrites(ActivityNode node, bool includeIntrinsics = true)
    {
        ArgumentNullException.ThrowIfNull(node);
        foreach (var output in node.Outputs ?? [])
        {
            if (output is null || !output.IsBound() ||
                !StringComparer.OrdinalIgnoreCase.Equals(output.Value.ExpressionType, VariableExpressionType) ||
                !VariableReference.TryParse(output.Value.Value, out var target) || target is null)
                continue;
            yield return new(node.NodeId, VariableWriteKind.Capture, NormalizeScopeId(target.DeclaringScopeId), target.ReferenceKey, output.ReferenceKey);
        }

        if (!includeIntrinsics)
            yield break;

        if (node.Intrinsic is { Variable: { } variable } intrinsic &&
            intrinsic.Kind is AuthoredWorkflowIntrinsicKind.Set or AuthoredWorkflowIntrinsicKind.Merge or AuthoredWorkflowIntrinsicKind.Reduce)
        {
            var kind = intrinsic.Kind switch
            {
                AuthoredWorkflowIntrinsicKind.Merge => VariableWriteKind.Merge,
                AuthoredWorkflowIntrinsicKind.Reduce => VariableWriteKind.Reduce,
                _ => VariableWriteKind.Set
            };
            yield return new(node.NodeId, kind, NormalizeScopeId(variable.DeclaringScopeId), variable.ReferenceKey);
        }
        else if (StringComparer.Ordinal.Equals(node.ActivityVersionId, LegacySetActivityVersionId) &&
                 node.Inputs?.FirstOrDefault(input => StringComparer.Ordinal.Equals(input.ReferenceKey, "variable")) is { } legacy &&
                 VariableReference.TryParse(legacy.Value?.Value, out var legacyTarget) && legacyTarget is not null)
        {
            yield return new(node.NodeId, VariableWriteKind.Set, NormalizeScopeId(legacyTarget.DeclaringScopeId), legacyTarget.ReferenceKey);
        }
    }

    /// <summary>The root scope id for a blank, <c>workflow</c>, <c>$workflow</c> or legacy <c>activity-graph</c> scope.</summary>
    public static string NormalizeScopeId(string? scopeId) =>
        string.IsNullOrWhiteSpace(scopeId) ||
        StringComparer.Ordinal.Equals(scopeId, VariableReference.WorkflowScopeId) ||
        StringComparer.Ordinal.Equals(scopeId, "$workflow") ||
        StringComparer.Ordinal.Equals(scopeId, "activity-graph")
            ? VariableReference.WorkflowScopeId
            : scopeId;

    /// <summary>Analyses the tree under <paramref name="root"/>, reading each node's writes through <paramref name="writesOf"/>.</summary>
    public static UnorderedVariableWriteAnalysisResult Analyze(
        ActivityNode root,
        IActivityStructureService structures,
        Func<ActivityNode, IEnumerable<VariableWrite>> writesOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(writesOf);
        return new Analysis(structures, cancellationToken).Run(root, writesOf);
    }

    private sealed class Analysis(IActivityStructureService structures, CancellationToken cancellationToken)
    {
        private readonly List<Node> _nodes = [];
        private readonly Dictionary<(Node Parent, string First, string Second), ActivityChildTimeRelation> _relations = [];
        private readonly Dictionary<Node, bool?> _completes = [];

        public UnorderedVariableWriteAnalysisResult Run(ActivityNode root, Func<ActivityNode, IEnumerable<VariableWrite>> writesOf)
        {
            Build(root);
            var writes = new List<(Node Node, VariableWrite Write)>();
            var duplicates = new List<DuplicateCaptureTargetFinding>();
            foreach (var node in _nodes)
            {
                var nodeWrites = writesOf(node.Activity).ToArray();
                writes.AddRange(nodeWrites.Select(write => (node, write)));
                duplicates.AddRange(nodeWrites
                    .Where(write => write.Kind == VariableWriteKind.Capture)
                    .GroupBy(write => (write.ScopeId, write.ReferenceKey))
                    .Where(group => group.Count() > 1)
                    .Select(group => new DuplicateCaptureTargetFinding(
                        node.Activity.NodeId, group.Key.ScopeId, group.Key.ReferenceKey,
                        group.Select(write => write.OutputKey ?? string.Empty).ToArray())));
            }

            var findings = new List<UnorderedVariableWriteFinding>();
            foreach (var variable in writes.GroupBy(item => (item.Write.ScopeId, item.Write.ReferenceKey)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var writers = variable.Select(item => item.Node).Distinct().OrderBy(node => node.Order).ToArray();
                var reported = new HashSet<Node>();
                var selfOverlapping = new List<Node>();
                var undeclared = new List<UndeclaredStructure>();
                foreach (var writer in writers)
                {
                    if (!IsSelfOverlapping(writer))
                        continue;
                    reported.Add(writer);
                    selfOverlapping.Add(writer);
                }

                for (var left = 0; left < writers.Length; left++)
                for (var right = left + 1; right < writers.Length; right++)
                {
                    if (!AreUnordered(writers[left], writers[right], undeclared))
                        continue;
                    reported.Add(writers[left]);
                    reported.Add(writers[right]);
                }

                if (reported.Count == 0)
                    continue;
                findings.Add(new(
                    variable.Key.ScopeId,
                    variable.Key.ReferenceKey,
                    reported.OrderBy(node => node.Order).Select(node => node.Activity.NodeId).ToArray(),
                    selfOverlapping.Select(node => node.Activity.NodeId).ToArray(),
                    undeclared.Distinct().ToArray()));
            }

            return new(findings, duplicates);
        }

        private void Build(ActivityNode root)
        {
            var pending = new Stack<(ActivityNode Activity, Node? Parent)>();
            pending.Push((root, null));
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (activity, parent) = pending.Pop();
                var node = new Node(activity, parent, _nodes.Count);
                _nodes.Add(node);
                var children = structures.ProjectChildren(activity).SelectMany(slot => slot.Activities).ToArray();
                foreach (var child in children.Reverse())
                    pending.Push((child, node));
            }
        }

        // Two runs of the node can be in flight together when a structure above it says so for the child on its path.
        private bool IsSelfOverlapping(Node node) => AnyAncestorOverlaps(node);

        private bool AnyAncestorOverlaps(Node from)
        {
            for (var child = from; child.Parent is { } parent; child = parent)
            {
                if (Relate(parent, child, child) == ActivityChildTimeRelation.Unordered)
                    return true;
            }

            return false;
        }

        private bool AreUnordered(Node first, Node second, List<UndeclaredStructure> undeclared)
        {
            if (IsAncestor(first, second))
                return !FinishesBefore(second, first, undeclared) || AnyAncestorOverlaps(first);
            if (IsAncestor(second, first))
                return !FinishesBefore(first, second, undeclared) || AnyAncestorOverlaps(second);

            var common = NearestCommonAncestor(first, second);
            if (common is null)
                return false;
            if (AnyAncestorOverlaps(common))
                return true;

            var firstChild = ChildBelow(first, common);
            var secondChild = ChildBelow(second, common);
            switch (Relate(common, firstChild, secondChild))
            {
                case ActivityChildTimeRelation.MutuallyExclusive:
                    return false;
                case ActivityChildTimeRelation.Unordered:
                    return true;
                case ActivityChildTimeRelation.FirstBeforeSecond:
                    return !EarlierSideIsDone(common, first, firstChild, undeclared);
                case ActivityChildTimeRelation.SecondBeforeFirst:
                    return !EarlierSideIsDone(common, second, secondChild, undeclared);
                default:
                    AddUndeclared(common, undeclared);
                    return true;
            }
        }

        // The earlier child's writes end before the later child starts only if that child cannot overlap itself and
        // every structure between the write and that child completes only after its children.
        private bool EarlierSideIsDone(Node common, Node write, Node earlierChild, List<UndeclaredStructure> undeclared) =>
            Relate(common, earlierChild, earlierChild) != ActivityChildTimeRelation.Unordered &&
            FinishesBefore(write, earlierChild, undeclared);

        // Whether a write in the subtree of `ancestor` has ended when `ancestor` completes.
        private bool FinishesBefore(Node write, Node ancestor, List<UndeclaredStructure> undeclared)
        {
            if (ReferenceEquals(write, ancestor))
                return true;
            for (var structure = write.Parent; structure is not null; structure = structure.Parent)
            {
                var completes = Completes(structure);
                if (completes != true)
                {
                    if (completes is null)
                        AddUndeclared(structure, undeclared);
                    return false;
                }

                if (ReferenceEquals(structure, ancestor))
                    return true;
            }

            return false;
        }

        private ActivityChildTimeRelation Relate(Node parent, Node first, Node second)
        {
            var key = (parent, first.Activity.NodeId, second.Activity.NodeId);
            if (_relations.TryGetValue(key, out var cached))
                return cached;
            var relation = string.IsNullOrWhiteSpace(first.Activity.NodeId) || string.IsNullOrWhiteSpace(second.Activity.NodeId)
                ? ActivityChildTimeRelation.Unspecified
                : structures.RelateChildren(parent.Activity, first.Activity.NodeId, second.Activity.NodeId);
            _relations[key] = relation;
            return relation;
        }

        private bool? Completes(Node structure)
        {
            if (_completes.TryGetValue(structure, out var cached))
                return cached;
            var completes = structures.CompletesAfterItsChildren(structure.Activity);
            _completes[structure] = completes;
            return completes;
        }

        private static void AddUndeclared(Node structure, List<UndeclaredStructure> undeclared)
        {
            var entry = new UndeclaredStructure(structure.Activity.NodeId, structure.Activity.Structure?.Kind ?? "<none>");
            if (!undeclared.Contains(entry))
                undeclared.Add(entry);
        }

        private static bool IsAncestor(Node candidate, Node node)
        {
            for (var current = node.Parent; current is not null; current = current.Parent)
                if (ReferenceEquals(current, candidate))
                    return true;
            return false;
        }

        private static Node? NearestCommonAncestor(Node left, Node right)
        {
            var ancestors = new HashSet<Node>();
            for (var current = left.Parent; current is not null; current = current.Parent)
                ancestors.Add(current);
            for (var current = right.Parent; current is not null; current = current.Parent)
                if (ancestors.Contains(current))
                    return current;
            return null;
        }

        private static Node ChildBelow(Node node, Node ancestor)
        {
            var current = node;
            while (current.Parent is not null && !ReferenceEquals(current.Parent, ancestor))
                current = current.Parent;
            return current;
        }
    }

    private sealed class Node(ActivityNode activity, Node? parent, int order)
    {
        public ActivityNode Activity { get; } = activity;
        public Node? Parent { get; } = parent;
        public int Order { get; } = order;
    }
}

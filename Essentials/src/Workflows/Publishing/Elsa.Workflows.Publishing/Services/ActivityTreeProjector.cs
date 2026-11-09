using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Walks an authored activity tree exactly once, capturing every node in traversal order together with a
/// memoized child projection per node, and validates structural invariants. Extracted from
/// <see cref="WorkflowExecutableCompiler"/> (#418) to eliminate the previous double traversal: the
/// compiler used to project children once while flattening and again while compiling each node.
/// </summary>
public sealed class ActivityTreeProjector(IActivityStructureService activityStructureService)
{
    public const string NoRootActivityMessage = "Workflow version has no root activity to publish.";

    internal ActivityTemplateDataSelectionFacts DescribeDataSelectionFacts(
        Elsa.Workflows.Runtime.Core.Models.ExecutableNode root, WorkflowDefinitionState state) =>
        ActivityTemplatePlacer.DescribeDataSelectionFacts(root, state.Inputs.Select(input => input.Name),
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
            {
                [Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId] = state.Variables.Select(variable => variable.ReferenceKey).ToArray()
            }, state.RootActivity, activityStructureService, outerRequestIsOpen: !state.Inputs.Any());

    /// <summary>
    /// Traverses <paramref name="rootActivity"/> once, projecting each node's children through
    /// <see cref="IActivityStructureService.ProjectChildren"/> exactly once and reusing the result for both the
    /// flattened node list and downstream compilation. Relies on <c>ProjectChildren</c> being a pure projection
    /// (no caching or side effects) so a single call per node is equivalent to the former repeated calls — do
    /// not weaken that contract without revisiting this memoization.
    /// </summary>
    public ActivityTreeProjection Project(ActivityNode rootActivity)
    {
        var nodes = new List<ActivityNode>();
        var childProjectionsByNodeId = new Dictionary<string, IReadOnlyCollection<ActivityChildProjection>>(StringComparer.Ordinal);

        var stack = new Stack<ActivityNode>();
        stack.Push(rootActivity);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            nodes.Add(node);

            var childProjections = activityStructureService.ProjectChildren(node);
            // Keyed by node id (unique per Validate below), so memoization is safe even though ActivityNode is a
            // value-equality record and two structurally identical nodes would otherwise collide.
            childProjectionsByNodeId[node.NodeId] = childProjections;

            foreach (var child in childProjections.SelectMany(slot => slot.Activities))
                stack.Push(child);
        }

        return new ActivityTreeProjection(nodes, childProjectionsByNodeId);
    }

    /// <summary>
    /// Presents only the public defaults actually selected by boundary compilation to source analysis.
    /// This request-only view leaves the authored source and its behavioral identity unchanged. Child
    /// replacement remains owned by each structure handler, using the memoized slots and their metadata.
    /// </summary>
    internal ActivityNode WithInputDefaults(ActivityNode root, ActivityTreeProjection projection,
        IReadOnlyDictionary<string, IReadOnlyList<ArgumentState>> defaults)
    {
        if (defaults.Count == 0)
            return root;

        var replacements = new Dictionary<string, ActivityNode>(StringComparer.Ordinal);
        for (var index = projection.Nodes.Count - 1; index >= 0; index--)
        {
            var original = projection.Nodes[index];
            var current = original;
            var childChanged = false;
            var slots = projection.ChildProjections(original).Select(slot =>
            {
                var activities = slot.Activities.Select(child =>
                {
                    if (!replacements.TryGetValue(child.NodeId, out var replacement))
                        return child;
                    childChanged = true;
                    return replacement;
                }).ToArray();
                return slot with { Activities = activities };
            }).ToArray();
            if (childChanged)
                current = activityStructureService.ReplaceChildren(current, slots);
            if (defaults.TryGetValue(original.NodeId, out var inputs))
                current = current with { Inputs = [.. current.Inputs, .. inputs] };
            if (!ReferenceEquals(current, original))
                replacements.Add(original.NodeId, current);
        }

        return replacements.GetValueOrDefault(root.NodeId) ?? root;
    }

    public static void Validate(IReadOnlyCollection<ActivityNode> activities)
    {
        if (activities.Count == 0)
            throw new ArgumentException(NoRootActivityMessage);

        var duplicateNodeId = activities.GroupBy(activity => activity.NodeId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateNodeId is not null)
            throw new ArgumentException($"Workflow version contains duplicate activity node id '{duplicateNodeId}'.");
    }
}

/// <summary>
/// The single-walk result of <see cref="ActivityTreeProjector.Project"/>: every authored node in traversal
/// order plus its memoized child projection, keyed by node id.
/// </summary>
public sealed class ActivityTreeProjection(
    IReadOnlyList<ActivityNode> nodes,
    IReadOnlyDictionary<string, IReadOnlyCollection<ActivityChildProjection>> childProjectionsByNodeId)
{
    public IReadOnlyList<ActivityNode> Nodes { get; } = nodes;

    public IReadOnlyCollection<ActivityChildProjection> ChildProjections(ActivityNode activity) =>
        childProjectionsByNodeId[activity.NodeId];
}

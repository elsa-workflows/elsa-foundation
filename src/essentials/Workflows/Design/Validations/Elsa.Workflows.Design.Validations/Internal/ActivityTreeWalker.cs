using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Validations.Internal;

/// <summary>
/// Iterative depth-first walker over an <see cref="ActivityNode"/> tree (root activities +
/// their nested activity-specific child slots). Iterative, not recursive — the .NET call stack is
/// never the bottleneck; <paramref name="maxDepth"/> is a safety net against cyclic /
/// malformed Draft data.
/// </summary>
public sealed class ActivityTreeWalker(IActivityStructureService activityStructureService)
{
    /// <summary>
    /// Yields every activity reachable from <paramref name="root"/> up to
    /// <paramref name="maxDepth"/>. Root is at depth 0; composed child activities are at depth
    /// 1; etc. Nodes beyond <paramref name="maxDepth"/> are silently skipped — the
    /// caller decides whether to surface that as a validation error.
    /// </summary>
    public IEnumerable<ActivityNode> Walk(ActivityNode? root, int maxDepth)
    {
        if (root is null)
            yield break;

        var stack = new Stack<(ActivityNode Node, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            yield return node;

            if (depth >= maxDepth)
                continue;

            foreach (var child in activityStructureService.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push((child, depth + 1));
        }
    }

}

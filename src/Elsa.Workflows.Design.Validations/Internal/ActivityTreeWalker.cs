using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Validations.Internal;

/// <summary>
/// Iterative depth-first walker over an <see cref="ActivityNode"/> tree (root activities +
/// their nested <c>ChildActivities</c>). Iterative, not recursive — the .NET call stack is
/// never the bottleneck; <paramref name="maxDepth"/> is a safety net against cyclic /
/// malformed Draft data.
/// </summary>
internal static class ActivityTreeWalker
{
    /// <summary>
    /// Yields every activity reachable from <paramref name="roots"/> up to
    /// <paramref name="maxDepth"/>. Roots are at depth 0; their <c>ChildActivities</c> are at
    /// depth 1; etc. Nodes beyond <paramref name="maxDepth"/> are silently skipped — the
    /// caller decides whether to surface that as a validation error.
    /// </summary>
    public static IEnumerable<ActivityNode> Walk(IEnumerable<ActivityNode> roots, int maxDepth)
    {
        var stack = new Stack<(ActivityNode Node, int Depth)>();

        foreach (var root in roots)
            stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            yield return node;

            if (depth >= maxDepth)
                continue;

            foreach (var child in node.ChildActivities)
                stack.Push((child, depth + 1));
        }
    }
}

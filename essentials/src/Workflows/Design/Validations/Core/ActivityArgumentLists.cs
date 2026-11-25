using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core;

/// <summary>
/// The refusal of a node whose <c>inputs</c> or <c>outputs</c> list is missing or null. A node with no arguments carries
/// an empty list; the node reader turns a missing or null list into null despite the non-nullable type, so every path
/// that reads posted or stored draft state refuses such a node before it reads the list. The rule lives here once: the
/// draft validation gate, credential admission, data selection, the rename planner, the scoped-variable analysis and the
/// draft test runs all report this refusal.
/// </summary>
public static class ActivityArgumentLists
{
    /// <summary>The validation error type of the refusal.</summary>
    public const string MalformedType = "Graph/MalformedArgumentList";

    /// <summary>Whether <paramref name="node"/> itself lacks either list.</summary>
    public static bool AreMissing(ActivityNode node) => node.Inputs is null || node.Outputs is null;

    /// <summary>
    /// The refusals for every node under <paramref name="root"/>, at any depth: some readers walk the whole tree, so the
    /// check does too. Children are found through the node's structure, which never reads a node's argument lists.
    /// </summary>
    public static IReadOnlyList<ValidationError> Refusals(ActivityNode? root, IActivityStructureService structures)
    {
        ArgumentNullException.ThrowIfNull(structures);
        var refusals = new List<ValidationError>();
        if (root is null)
            return refusals;

        var stack = new Stack<ActivityNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            refusals.AddRange(Refusals(node));
            foreach (var child in structures.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push(child);
        }

        return refusals;
    }

    /// <summary>The refusals for <paramref name="node"/> alone: one per missing or null list.</summary>
    public static IEnumerable<ValidationError> Refusals(ActivityNode node)
    {
        if (node.Inputs is null)
            yield return Refusal(node, "inputs");
        if (node.Outputs is null)
            yield return Refusal(node, "outputs");
    }

    /// <summary>
    /// A refusal as a bad-request message: <c>{NodeId}/{list}: {message}</c>. Readers that report through
    /// <see cref="ArgumentException"/> (mapped to 400) use it, so the message names the node, the list and the type.
    /// </summary>
    public static string Describe(ValidationError refusal) => $"{refusal.Path}: {refusal.Message}";

    private static ValidationError Refusal(ActivityNode node, string list) => new(
        $"{node.NodeId}/{list}", MalformedType,
        $"{MalformedType}: activity node '{node.NodeId}' has no '{list}' list; a node with no arguments carries [].");
}

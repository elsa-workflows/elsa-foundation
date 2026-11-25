using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Internal;

/// <summary>
/// A node with no arguments carries an empty <c>inputs</c>/<c>outputs</c> list. The node reader turns a missing or
/// null list into null despite the non-nullable type, so it is refused at the node before anything reads it.
/// </summary>
internal static class ActivityArgumentLists
{
    public const string MalformedType = "Graph/MalformedArgumentList";

    public static bool AreMissing(ActivityNode node) => node.Inputs is null || node.Outputs is null;

    public static IEnumerable<ValidationError> Refusals(ActivityNode node)
    {
        if (node.Inputs is null)
            yield return Refusal(node, "inputs");
        if (node.Outputs is null)
            yield return Refusal(node, "outputs");
    }

    private static ValidationError Refusal(ActivityNode node, string list) => new(
        $"{node.NodeId}/{list}", MalformedType, $"An activity node's '{list}' must be a list of arguments ([] when it has none).");
}

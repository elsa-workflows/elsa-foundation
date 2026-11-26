using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Activities.Graph.Design.Services;

/// <summary>
/// Workflow Draft validator: a <c>Complete</c> node ends its nearest graph activity, so a workflow definition, which
/// is never a graph body, cannot contain one (spec 237 FR-016). Recording the error does not block saving the Draft;
/// the promotion gate refuses to publish a Draft that carries it. Graph bodies are activity drafts validated by
/// <see cref="GraphActivityProvider"/>, so this validator never sees them. At run time an unenclosed Complete node
/// faults with <c>runtime.ancestor-notification.target-not-found</c>.
/// </summary>
public sealed class CompleteOutsideGraphDraftValidator(
    IActivityStructureService activityStructureService,
    IActivityDefinitionLookup activityDefinitionLookup) : IDraftValidator
{
    public const string ErrorType = "Graph/CompleteOutsideGraph";

    public async ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();
        foreach (var node in WalkTree(draft.State.RootActivity))
        {
            if (string.IsNullOrWhiteSpace(node.ActivityVersionId))
                continue;

            var version = await activityDefinitionLookup.FindVersion(node.ActivityVersionId, cancellationToken);
            if (version is null || !StringComparer.Ordinal.Equals(version.DefinitionId, GraphActivityProvider.CompleteDefinitionId))
                continue;

            errors.Add(new ValidationError(
                Path: node.NodeId,
                Type: ErrorType,
                Message: $"Complete node '{node.NodeId}' ends the reusable activity it sits in, so it can only be placed inside a reusable activity's body."));
        }

        return errors;
    }

    // Iterative depth-first walk over the activity tree, as SwitchDuplicateCaseValidator walks it.
    private IEnumerable<ActivityNode> WalkTree(ActivityNode? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<ActivityNode>();
        stack.Push(root);
        var visited = new HashSet<ActivityNode>(ReferenceEqualityComparer.Instance);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node))
                continue;

            yield return node;

            foreach (var child in activityStructureService.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }
}

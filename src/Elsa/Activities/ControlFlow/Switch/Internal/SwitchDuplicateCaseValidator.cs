using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Internal;

/// <summary>
/// Activity-owned Draft validator (FR-034). Surfaces duplicate <c>Switch</c> case match values as a
/// design-time <see cref="ValidationError"/> so authors see them in the designer. Recording the error
/// does not block saving the Draft; the promotion gate (<c>PromoteDraftToVersion</c>) refuses to publish
/// a Draft that carries any validation error, so a duplicate-case <c>Switch</c> can be authored and saved
/// but never compiled into an executable artefact. The compile-time guard in
/// <see cref="SwitchStructureHandler"/> and the runtime guard in <see cref="SwitchNavigator"/> remain as
/// backstops for paths that bypass Draft validation.
/// </summary>
public sealed class SwitchDuplicateCaseValidator(IActivityStructureService activityStructureService) : IDraftValidator
{
    public const string ErrorType = "Switch/DuplicateCase";

    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();

        foreach (var node in WalkTree(draft.State.RootActivity))
        {
            if (!StringComparer.Ordinal.Equals(node.Structure?.Kind, SwitchActivity.StructureKind))
                continue;

            var cases = SwitchAuthoredStructureReader.Read(node).Cases;

            foreach (var duplicate in SwitchCaseRules.DuplicateMatches(cases.Select(@case => @case.Match)))
            {
                errors.Add(new ValidationError(
                    Path: node.NodeId,
                    Type: ErrorType,
                    Message: $"Switch activity '{node.NodeId}' declares more than one case with match value '{duplicate}'. Each Switch case must have a unique match value."
                ));
            }
        }

        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }

    // Iterative depth-first walk over the activity tree (root + activity-specific child slots). A
    // reference-identity visited set guards against malformed/cyclic Draft data without an arbitrary
    // depth cap.
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

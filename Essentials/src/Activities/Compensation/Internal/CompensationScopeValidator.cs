using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using CompensationScopeActivity = Elsa.Activities.Compensation.Activities.CompensationScope;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// Activity-owned Draft validator for compensation scopes: every step needs a unique name and a forward activity.
/// Errors do not block saving the Draft; the promotion gate refuses to publish it. The compile-time guard in
/// <see cref="CompensationScopeStructureHandler"/> and the runtime navigator are backstops.
/// </summary>
internal sealed class CompensationScopeValidator(IActivityStructureService activityStructureService) : IDraftValidator
{
    public const string BlankNameType = "CompensationScope/BlankStepName";
    public const string DuplicateNameType = "CompensationScope/DuplicateStepName";
    public const string MissingActivityType = "CompensationScope/MissingStepActivity";

    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();

        foreach (var node in WalkTree(draft.State.RootActivity))
        {
            if (!StringComparer.Ordinal.Equals(node.Structure?.Kind, CompensationScopeActivity.StructureKind))
                continue;

            var steps = CompensationScopeAuthoredStructureReader.Read(node).Steps.ToArray();
            for (var index = 0; index < steps.Length; index++)
            {
                if (CompensationStepRules.Normalize(steps[index].Name).Length == 0)
                    errors.Add(new ValidationError(StepPath(node, index), BlankNameType, $"Step {index + 1} needs a name."));
            }

            foreach (var duplicate in CompensationStepRules.DuplicateNames(steps.Select(step => step.Name)))
            {
                var index = Array.FindIndex(steps, step => StringComparer.Ordinal.Equals(CompensationStepRules.Normalize(step.Name), duplicate));
                errors.Add(new ValidationError(StepPath(node, index), DuplicateNameType, $"Step \"{duplicate}\" is defined more than once. Step names must be unique."));
            }

            for (var index = 0; index < steps.Length; index++)
            {
                if (steps[index].Activity is null)
                    errors.Add(new ValidationError(StepPath(node, index), MissingActivityType, $"Step \"{CompensationStepRules.Normalize(steps[index].Name)}\" has no activity."));
            }
        }

        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }

    // The path starts with the scope's node id, so the designer's "Go to" lands on the scope; the step index lets
    // the scope designer highlight the row.
    private static string StepPath(ActivityNode node, int index) => $"{node.NodeId}/steps/{index}";

    // Iterative depth-first walk over the activity tree. A reference-identity visited set guards against malformed
    // or cyclic Draft data without an arbitrary depth cap.
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

using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Internal;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Baseline validator (Unit C FR-033, extended per-scope for #972). Detects duplicate variable names
/// within one declaring scope: the workflow-scope bag (<c>WorkflowDefinitionState.Variables</c>) and
/// each container's structure-declared bag independently. Comparison is case-insensitive
/// (<c>MyVar</c> and <c>myvar</c> collide). One error per offending name per scope. Names remain free
/// to repeat ACROSS scopes — ADR 0027 allows nested shadowing (advisory warnings only).
/// </summary>
public sealed class VariableUniquenessValidator(
    IOptions<WorkflowDesignValidatorOptions> options,
    ActivityTreeWalker activityTreeWalker,
    IActivityStructureService activityStructureService
) : IDraftValidator
{
    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();

        AddDuplicateNameErrors(
            errors,
            draft.State.Variables,
            $"{ValidationPaths.Workflow}/variables",
            "the workflow");

        foreach (var node in activityTreeWalker.Walk(draft.State.RootActivity, options.Value.MaxRecursionDepth))
        {
            var declared = activityStructureService.ProjectScopedVariables(node);
            if (declared.Count > 0)
                AddDuplicateNameErrors(errors, declared, $"{node.NodeId}/variables", $"container '{node.NodeId}'");
        }

        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }

    private static void AddDuplicateNameErrors(
        ICollection<ValidationError> errors,
        IEnumerable<VariableDefinition> variables,
        string pathPrefix,
        string scopeDescription)
    {
        var duplicates = variables
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicates)
        {
            errors.Add(new ValidationError(
                Path: $"{pathPrefix}/{group.Key}",
                Type: "Variables/Uniqueness",
                Message: $"Variable name '{group.Key}' is declared {group.Count()} times (case-insensitive) in {scopeDescription}. Variable names must be unique within one declaring scope."
            ));
        }
    }
}

using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Internal;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Baseline validator (Unit C FR-033). For every activity in the Draft (root + nested), scans
/// every <see cref="ArgumentState"/> whose <see cref="ArgumentValue.ExpressionType"/> equals
/// the exact string <c>"Variable"</c> (per research item R9). Asserts that
/// <see cref="ArgumentValue.Value"/> (read as the variable's <c>ReferenceKey</c>) names a
/// variable that exists in <c>WorkflowDefinitionState.Variables</c>.
/// </summary>
/// <remarks>
/// Variable lookup is by <see cref="Elsa.Expressions.Core.Models.VariableDefinition.ReferenceKey"/>,
/// not <see cref="Elsa.Expressions.Core.Models.VariableDefinition.Name"/> — the id is stable
/// across renames; the name is mutable. Recurses through activity-owned composition via
/// <see cref="ActivityTreeWalker"/> up to
/// <see cref="WorkflowDesignValidatorOptions.MaxRecursionDepth"/>.
/// </remarks>
public sealed class VariableExpressionResolverValidator(
    IOptions<WorkflowDesignValidatorOptions> options,
    ActivityTreeWalker activityTreeWalker
) : IDraftValidator
{
    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var state = draft.State;
        var knownReferenceKeys = state.Variables
            .Select(v => v.ReferenceKey)
            .ToHashSet(StringComparer.Ordinal);

        var maxDepth = options.Value.MaxRecursionDepth;
        var errors = new List<ValidationError>();

        foreach (var node in activityTreeWalker.Walk(state.RootActivity, maxDepth))
        {
            foreach (var argument in node.Inputs)
                CheckArgument(errors, node.NodeId, "inputs", argument, knownReferenceKeys);

            foreach (var argument in node.Outputs)
                CheckArgument(errors, node.NodeId, "outputs", argument, knownReferenceKeys);
        }

        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }

    private static void CheckArgument(
        ICollection<ValidationError> errors,
        string nodeId,
        string argumentBag,
        ArgumentState argument,
        HashSet<string> knownReferenceKeys
    )
    {
        if (argument.Value is null)
            return;

        if (!string.Equals(argument.Value.ExpressionType, WellKnownExpressionDescriptorTypes.Variable, StringComparison.Ordinal))
            return;

        var hasVariableReference = VariableReference.TryParse(argument.Value.Value, out var variableReference);
        var variableReferenceKey = variableReference?.ReferenceKey;

        if (hasVariableReference && variableReference!.IsWorkflowScope && knownReferenceKeys.Contains(variableReferenceKey!))
            return;

        errors.Add(new ValidationError(
            Path: $"{nodeId}/{argumentBag}/{argument.ReferenceKey}",
            Type: "Expressions/UnresolvedVariable",
            Message: !hasVariableReference
                ? $"Variable expression on '{nodeId}/{argumentBag}/{argument.ReferenceKey}' has no variable reference."
                : $"Variable expression on '{nodeId}/{argumentBag}/{argument.ReferenceKey}' references unknown variable '{variableReferenceKey}'."
        ));
    }
}

using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Internal;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Baseline validator (Unit C FR-033). For every activity in the Draft (root + nested), scans
/// every <see cref="ArgumentState"/> whose <see cref="ArgumentValue.ExpressionType"/> equals
/// the exact string <c>"Variable"</c> (per research item R9). Asserts that
/// <see cref="ArgumentValue.Value"/> (a structured <see cref="VariableReference"/>) resolves to a
/// variable that is visible from the referencing activity — either a workflow-scoped variable in
/// <c>WorkflowDefinitionState.Variables</c> or a container-scoped variable declared by a visible
/// ancestor container (ADR 0027).
/// </summary>
/// <remarks>
/// Variable lookup is by <see cref="Elsa.Expressions.Core.Models.VariableDefinition.ReferenceKey"/>,
/// not <see cref="Elsa.Expressions.Core.Models.VariableDefinition.Name"/> — the id is stable
/// across renames; the name is mutable. Visibility (including nearest-scope shadowing across nested
/// containers) is computed by <see cref="ScopedVariableResolver"/>. References whose declaring
/// scope is not a visible ancestor are reported as out-of-scope rather than retargeted by name.
/// </remarks>
public sealed class VariableExpressionResolverValidator(
    IOptions<WorkflowDesignValidatorOptions> options,
    ActivityTreeWalker activityTreeWalker,
    ScopedVariableResolver scopedVariableResolver
) : IDraftValidator
{
    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var state = draft.State;
        var maxDepth = options.Value.MaxRecursionDepth;
        var visibility = scopedVariableResolver.Resolve(state.Variables, state.RootActivity, maxDepth);
        var errors = new List<ValidationError>();

        foreach (var node in activityTreeWalker.Walk(state.RootActivity, maxDepth))
        {
            foreach (var argument in node.Inputs)
                CheckArgument(errors, node.NodeId, "inputs", argument, visibility);

            foreach (var argument in node.Outputs)
                CheckArgument(errors, node.NodeId, "outputs", argument, visibility);
        }

        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }

    private static void CheckArgument(
        ICollection<ValidationError> errors,
        string nodeId,
        string argumentBag,
        ArgumentState argument,
        ScopedVariableVisibility visibility
    )
    {
        if (argument.Value is null)
            return;

        if (!string.Equals(argument.Value.ExpressionType, WellKnownExpressionDescriptorTypes.Variable, StringComparison.Ordinal))
            return;

        var hasVariableReference = VariableReference.TryParse(argument.Value.Value, out var variableReference);

        if (hasVariableReference && visibility.IsReferenceVisible(nodeId, variableReference!))
            return;

        errors.Add(new ValidationError(
            Path: $"{nodeId}/{argumentBag}/{argument.ReferenceKey}",
            Type: "Expressions/UnresolvedVariable",
            Message: !hasVariableReference
                ? $"Variable expression on '{nodeId}/{argumentBag}/{argument.ReferenceKey}' has no variable reference."
                : $"Variable expression on '{nodeId}/{argumentBag}/{argument.ReferenceKey}' references variable '{variableReference!.ReferenceKey}' that is not visible from this activity's scope."
        ));
    }
}

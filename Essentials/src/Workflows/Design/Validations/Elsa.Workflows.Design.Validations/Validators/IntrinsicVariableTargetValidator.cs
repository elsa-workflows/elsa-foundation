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
/// Baseline validator (#972): every variable-writing intrinsic (<c>Set</c>/<c>Merge</c>/<c>Reduce</c>)
/// must target a variable that is visible from the intrinsic's scope — a workflow-scope variable in
/// <c>WorkflowDefinitionState.Variables</c> or a container-scoped variable declared by a visible
/// ancestor container (ADR 0027). Without this guard an out-of-scope target publishes cleanly and then
/// poisons at runtime ("Set intrinsic ... targets undeclared variable"); the author sees the error here
/// instead, and promotion is blocked (FR-024). The publish-time backstop in
/// <c>ExecutableNodeCompiler.ValidateIntrinsicVariableTargets</c> mirrors this check (PR #971 two-guard
/// pattern) so a draft that bypasses validation still fails closed at publication.
/// </summary>
public sealed class IntrinsicVariableTargetValidator(
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
            if (node.Intrinsic?.Variable is not { } reference)
                continue;

            if (visibility.IsReferenceVisible(node.NodeId, reference))
                continue;

            var scopeId = reference.IsWorkflowScope ? VariableReference.WorkflowScopeId : reference.DeclaringScopeId;
            errors.Add(new ValidationError(
                Path: $"{node.NodeId}/intrinsic/variable",
                Type: "Intrinsics/UnresolvedVariableTarget",
                Message: $"{node.Intrinsic.Kind} intrinsic '{node.NodeId}' targets variable '{reference.ReferenceKey}' in scope '{scopeId}', which is not visible from this activity's scope."
            ));
        }

        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }
}

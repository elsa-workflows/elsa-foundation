using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Internal;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Baseline validator (FR-033, 2026-07-05 amendment). For every activity in the Draft (root +
/// nested), resolves the node's <c>ActivityVersionId</c> via <see cref="CatalogVersionResolver"/>
/// and emits a <see cref="ValidationError"/> when it does not resolve. Referencing a nonexistent
/// activity version is a compile error the author must see in the designer: without this
/// validator the catalog's throwing Get contract faults the whole validation gate with an
/// opaque, node-anonymous exception (fail-closed, but undiagnosable at the offending node).
/// </summary>
/// <remarks>
/// A draft referencing an activity from an uninstalled package carries this error (and cannot
/// promote) until the package is reinstalled — intended semantics, ratified with the FR-033
/// amendment. Other catalog-consulting validators (<see cref="RequiredInputOutputValidator"/>)
/// skip the unresolvable node — this validator owns the report. Recurses via the iterative
/// <see cref="ActivityTreeWalker"/>; max depth is
/// <see cref="WorkflowDesignValidatorOptions.MaxRecursionDepth"/>.
/// </remarks>
public sealed class UnknownActivityVersionValidator(
    CatalogVersionResolver catalogResolver,
    IOptions<WorkflowDesignValidatorOptions> options,
    ActivityTreeWalker activityTreeWalker
) : IDraftValidator
{
    public async ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var maxDepth = options.Value.MaxRecursionDepth;
        var errors = new List<ValidationError>();

        foreach (var node in activityTreeWalker.Walk(draft.State.RootActivity, maxDepth))
        {
            if (await catalogResolver.Find(node.ActivityVersionId, cancellationToken) is not null)
                continue;

            errors.Add(new ValidationError(
                Path: node.NodeId,
                Type: ValidationCategories.UnknownActivityVersion,
                Message: $"Activity '{node.NodeId}' references activity version '{node.ActivityVersionId}', which does not exist in the activity catalog."
            ));
        }

        return errors;
    }
}

using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Internal;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Baseline validator. For every activity in the Draft (root + nested), flags a node whose
/// authored structure kind is declared by its activity's catalog design facets but has no
/// registered <see cref="IActivityStructureService"/> handler in this shell. Spec 071 FR-008
/// keeps a structure of UNKNOWN kind opaque at publish, so a declared-but-unhandled kind would
/// otherwise sail through authoring and publication with its children never lowered, then fault
/// confusingly deep in runtime execution (e.g. a BPMN process authored while the ActivitiesBpmn
/// feature is disabled in the shell). The author must see the composition error at the node.
/// </summary>
/// <remarks>
/// A structure kind the activity does NOT declare stays opaque and valid (FR-008); an
/// unresolvable <c>ActivityVersionId</c> is owned by <see cref="UnknownActivityVersionValidator"/>
/// and skipped here. Note the tree walker cannot descend into the unhandled node's children (no
/// handler means no child projection), so nested nodes go unvalidated — acceptable because this
/// error already blocks promotion (FR-024). Recurses via the iterative
/// <see cref="ActivityTreeWalker"/>; max depth is
/// <see cref="WorkflowDesignValidatorOptions.MaxRecursionDepth"/>.
/// </remarks>
public sealed class UnhandledActivityStructureValidator(
    CatalogVersionResolver catalogResolver,
    IActivityStructureService activityStructureService,
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
            var structure = node.Structure;
            if (structure is null || activityStructureService.HasHandler(structure))
                continue;

            var version = await catalogResolver.Find(node.ActivityVersionId, cancellationToken);
            if (version is null || !version.DesignFacets.Any(facet => StringComparer.Ordinal.Equals(facet.Kind, structure.Kind)))
                continue;

            errors.Add(new ValidationError(
                Path: node.NodeId,
                Type: "Graph/UnhandledActivityStructure",
                Message: $"Activity '{node.NodeId}' carries structure '{structure.Kind}@{structure.SchemaVersion}', which its activity type declares as an owned structure contract, but no structure handler for that kind is registered — the feature that provides this activity is disabled or missing from the shell."
            ));
        }

        return errors;
    }
}

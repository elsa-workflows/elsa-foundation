using Elsa.Activities.Design.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Internal;
using Microsoft.Extensions.Options;
using InputDefinition = Elsa.Activities.Design.Core.Models.InputDefinition;
using OutputDefinition = Elsa.Activities.Design.Core.Models.OutputDefinition;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Baseline validator (Unit C FR-033). For every activity in the Draft (root + nested), looks
/// up the catalog version via <see cref="IActivityDefinitionLookup"/> and reads
/// <c>IsRequired</c> from each <see cref="InputDefinition"/> / <see cref="OutputDefinition"/>
/// (per FR-036). Emits a <see cref="ValidationError"/> per required input or output where the
/// corresponding <see cref="ArgumentState"/> on the activity is absent or carries an empty
/// value.
/// </summary>
/// <remarks>
/// <para>
/// Recurses <c>ChildActivities</c> via the iterative <see cref="ActivityTreeWalker"/> — every
/// activity at any depth contributes its own required-arg check. Max depth is
/// <see cref="WorkflowDesignValidatorOptions.MaxRecursionDepth"/>.
/// </para>
/// <para>
/// Workflow-level <c>State.Inputs</c> / <c>State.Outputs</c> are declarations: the design-time
/// data model carries no default value or internal binding to check against. The workflow-level
/// branch of the FR-033 wording therefore has no actionable design-time semantic in Unit C's
/// data shape; it is deliberately skipped here and flagged as a follow-up for a unit that adds
/// the workflow-level binding surface (Unit D / E). Activity-level coverage is complete.
/// </para>
/// </remarks>
public sealed class RequiredInputOutputValidator(
    IActivityDefinitionLookup catalog,
    IOptions<WorkflowDesignValidatorOptions> options
) : IDomainEventHandler<OnDraftValidating>
{
    public async ValueTask Handle(OnDraftValidating domainEvent, CancellationToken cancellationToken)
    {
        var maxDepth = options.Value.MaxRecursionDepth;

        foreach (var node in ActivityTreeWalker.Walk(domainEvent.Draft.State.Activities, maxDepth))
        {
            var version = await catalog.GetVersion(node.ActivityVersionId, cancellationToken);
            if (version is null)
                continue;

            var providedInputKeys = node.Inputs
                .Where(IsBound)
                .Select(a => a.ReferenceKey)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var definition in version.Inputs.Where(d => d.IsRequired))
            {
                if (providedInputKeys.Contains(definition.ReferenceKey))
                    continue;

                domainEvent.AddValidationError(new ValidationError(
                    Path: $"{node.NodeId}/inputs/{definition.ReferenceKey}",
                    Type: "InputOutput/MissingRequired",
                    Message: $"Required input '{definition.Name}' on activity '{node.NodeId}' is missing or empty."
                ));
            }

            var providedOutputKeys = node.Outputs
                .Where(IsBound)
                .Select(a => a.ReferenceKey)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var definition in version.Outputs.Where(d => d.IsRequired))
            {
                if (providedOutputKeys.Contains(definition.ReferenceKey))
                    continue;

                domainEvent.AddValidationError(new ValidationError(
                    Path: $"{node.NodeId}/outputs/{definition.ReferenceKey}",
                    Type: "InputOutput/MissingRequired",
                    Message: $"Required output '{definition.Name}' on activity '{node.NodeId}' is missing or empty."
                ));
            }
        }
    }

    private static bool IsBound(ArgumentState state)
    {
        if (state.Value is null)
            return false;

        return !string.IsNullOrEmpty(state.Value.Value);
    }
}

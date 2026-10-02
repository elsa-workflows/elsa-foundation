using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Internal;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// The credential-literal rule (spec 188, FR-008, FR-009). For every catalog-backed activity in the state (root and
/// nested), reads each input the catalog version declares a credential (<c>InputDefinition.IsCredential</c>) and refuses
/// its binding unless <see cref="CredentialInputBinding.IsAccepted"/> accepts it. Each finding is
/// <see cref="CredentialLiteralFinding"/>'s, keyed by <c>{NodeId}/inputs/{ReferenceKey}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Engine intrinsics have no catalog version, and a node whose version the catalog does not hold cannot be judged: both
/// are skipped, as <see cref="RequiredInputOutputValidator"/> skips them. <see cref="UnknownActivityVersionValidator"/>
/// reports the unresolvable node, and publication refuses it because it cannot compile it. Recurses through the iterative
/// <see cref="ActivityTreeWalker"/>; max depth is <see cref="WorkflowDesignValidatorOptions.MaxRecursionDepth"/>.
/// </para>
/// <para>
/// Registered as <see cref="ICredentialLiteralValidator"/>, which the admitting writers of workflow state take, and as an
/// <see cref="IDraftValidator"/>, so the draft's validation panel reports the same findings. The second registration only
/// reports: no entry point relies on it.
/// </para>
/// </remarks>
public sealed class CredentialLiteralValidator(
    CatalogVersionResolver catalogResolver,
    IOptions<WorkflowDesignValidatorOptions> options,
    ActivityTreeWalker activityTreeWalker
) : ICredentialLiteralValidator, IDraftValidator
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ValidationError>> Validate(WorkflowDefinitionState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var findings = new List<ValidationError>();
        var catalogNodes = activityTreeWalker
            .Walk(state.RootActivity, options.Value.MaxRecursionDepth)
            .Where(node => node.Intrinsic is null);

        foreach (var node in catalogNodes)
            findings.AddRange(Refusals(node, await catalogResolver.Find(node.ActivityVersionId, cancellationToken)));

        return findings;
    }

    async ValueTask<IEnumerable<ValidationError>> IDraftValidator.Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken) =>
        await Validate(draft.State, cancellationToken);

    private static IEnumerable<ValidationError> Refusals(ActivityNode node, IActivityDefinitionVersion? version) =>
        (version?.Inputs ?? [])
            .Where(input => input.IsCredential == true)
            .Where(input => node.Inputs.Any(state =>
                StringComparer.Ordinal.Equals(state.ReferenceKey, input.ReferenceKey) &&
                !CredentialInputBinding.IsAccepted(isCredential: true, state)))
            .Select(input => CredentialLiteralFinding.For(node.NodeId, input.ReferenceKey, input.Name));
}

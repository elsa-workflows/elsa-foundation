using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http.Exceptions;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// Publish-time HTTP endpoint <c>(template, method)</c> uniqueness (spec 089 follow-up, issue #592 item 2),
/// contributed on the trigger indexer's pre-write <see cref="IWorkflowTriggerIndexValidator"/> seam. For each
/// HTTP-endpoint binding about to be written it looks up the existing claimants of the same stimulus identity
/// (<c>(StimulusType, StimulusHash)</c> = <c>(template, method)</c>) and throws
/// <see cref="EndpointRoutingConflictException"/> when a DIFFERENT workflow definition already owns it — failing
/// the second, conflicting publish with the durable index untouched.
/// </summary>
/// <remarks>
/// <para>
/// Two exemptions keep legitimate publishes flowing: bindings owned by the artifact being republished are
/// ignored (delete-and-resave is about to supersede them), and same-<c>DefinitionId</c> claimants are allowed
/// (another version/artifact of the same definition, or a duplicate node — not a cross-definition conflict).
/// </para>
/// <para>
/// The constraint is deliberately HTTP-specific and lives in this module: for most stimulus types (e.g. two
/// definitions on one Timer cron) a shared stimulus identity is legitimate fan-out, so uniqueness must not be
/// enforced generically at the indexer. The middleware's request-time 409 ambiguity guard remains the serving
/// backstop for conflicts that enter the store out-of-band.
/// </para>
/// </remarks>
public sealed class HttpEndpointRoutingUniquenessValidator(IWorkflowTriggerBindingStore bindingStore) : IWorkflowTriggerIndexValidator
{
    public async ValueTask ValidateAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (var binding in snapshot.Bindings)
        {
            if (!StringComparer.Ordinal.Equals(binding.StimulusType, HttpEndpointRouting.StimulusType))
                continue;

            var claimants = await bindingStore.ListByStimulusAsync(binding.StimulusType, binding.StimulusHash, cancellationToken);
            var conflicting = claimants.Any(existing =>
                !StringComparer.Ordinal.Equals(existing.ArtifactId, snapshot.ArtifactId) &&
                !StringComparer.Ordinal.Equals(existing.DefinitionId, binding.DefinitionId));

            if (conflicting)
                throw EndpointRoutingConflictException.ForBinding(binding);
        }
    }
}

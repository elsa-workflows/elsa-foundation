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
/// <see cref="EndpointRoutingConflictException"/> when another authoritative slot owns it — failing the
/// conflicting publication with the durable index untouched.
/// </summary>
/// <remarks>
/// <para>
/// Publication-scoped validation excludes only the publication being replaced in the candidate's own slot.
/// Sharing an artifact or definition is not an exemption across explicit slots. The legacy artifact-scoped
/// indexing path retains its artifact/same-definition compatibility exemptions.
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
            var conflicting = claimants.FirstOrDefault(existing => IsConflict(binding, snapshot.ArtifactId, existing));

            if (conflicting is not null)
                throw EndpointRoutingConflictException.ForBindings(binding, conflicting);
        }
    }

    private static bool IsConflict(
        WorkflowTriggerBinding candidate,
        string candidateArtifactId,
        WorkflowTriggerBinding existing)
    {
        // Publication-scoped replacement excludes only the authority in the candidate's own slot. Sharing an
        // artifact or definition across two explicit slots is not an Exclusive-claim exemption (ADR 0043).
        if (candidate.PublicationId is not null)
        {
            if (StringComparer.Ordinal.Equals(existing.PublicationId, candidate.PublicationId))
                return false;
            return !StringComparer.Ordinal.Equals(existing.SlotId, candidate.SlotId);
        }

        // Compatibility path for the legacy artifact-scoped indexer.
        return !StringComparer.Ordinal.Equals(existing.ArtifactId, candidateArtifactId) &&
               !StringComparer.Ordinal.Equals(existing.DefinitionId, candidate.DefinitionId);
    }
}

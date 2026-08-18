using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

public sealed record WorkflowPublicationPreflightPlan(
    ResolvedPublicationAction ResolvedAction,
    WorkflowActivationSlot? Slot,
    PublicationPreflightResult Result,
    IReadOnlyCollection<PublicationTriggerClaim> CandidateClaims);

/// <summary>Builds the same policy-resolved, publication-scoped trigger plan used by preview and activation.</summary>
/// <remarks>
/// Deliberately reads <b>no</b> <c>IPublicationRecordStore</c>. Contention is a property of what a slot is
/// actually serving, and the activation ledger plus the trigger-binding projection answer that on their own. An
/// activation minted by another source — artifact reconciliation — has no <c>PublicationRecord</c> at all
/// (FR-B-006, the 2026-08-16 publish/activation split), so a preflight that resolved every active activation
/// through publishing's journal could not evaluate a mixed engine.
/// </remarks>
public sealed class WorkflowPublicationPreflightReader(
    IPublicationPolicyStore policyStore,
    IPublicationPolicyResolver policyResolver,
    IWorkflowActivationAuthority activationAuthority,
    IPublicationPreflightService preflightService,
    IWorkflowTriggerBindingExtractor triggerExtractor,
    IWorkflowTriggerBindingStore triggerBindingStore,
    TimeProvider timeProvider)
{
    public async ValueTask<WorkflowPublicationPreflightPlan> EvaluateAsync(
        WorkflowExecutable executable,
        PublicationRequestIntent? requestIntent,
        string? expectedPublicationId,
        string candidatePublicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePublicationId);
        var identity = executable.Identity;
        var workflowPolicy = await policyStore.FindAsync(identity.DefinitionId, cancellationToken);
        var hostPolicy = await policyStore.FindAsync(workflowDefinitionId: null, cancellationToken)
            ?? new PublicationPolicy(null, PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 0, timeProvider.GetUtcNow());
        var resolved = policyResolver.Resolve(
            identity.DefinitionId,
            identity.DefinitionVersionId,
            requestIntent,
            workflowPolicy,
            hostPolicy);
        var slot = await activationAuthority.FindAsync(identity.DefinitionId, resolved.SlotName, cancellationToken);
        ValidateExpectedPublication(expectedPublicationId, slot);

        var candidateClaims = triggerExtractor.Evaluate(executable).Bindings
            .Select(binding => Claim(candidatePublicationId, binding))
            .ToArray();
        var authoritativeSets = new List<PublicationAuthoritativeClaimSet>();
        foreach (var authoritativeSlot in await activationAuthority.ListByDefinitionAsync(identity.DefinitionId, cancellationToken))
        {
            // Every live activation contends, whoever activated it. The claim set is identified by the activation
            // id and named by the SLOT's own name — which is exactly what the publication record used to be read
            // for, and which the slot already carries (PublicationActivator.ValidateCandidate makes the two equal
            // by construction). Skipping an unjournalled slot instead would drop real exclusive-trigger contention
            // against an imported artifact and let a publish claim a stimulus that is already being served.
            if (authoritativeSlot.ActiveActivationId is not { } activeActivationId)
                continue;
            var activeBindings = await triggerBindingStore.ListAllByActivationAsync(activeActivationId, cancellationToken);
            authoritativeSets.Add(new PublicationAuthoritativeClaimSet(
                activeActivationId,
                authoritativeSlot.SlotName,
                StringComparer.Ordinal.Equals(activeActivationId, slot?.ActiveActivationId),
                activeBindings.Select(binding => Claim(activeActivationId, binding)).ToArray()));
        }

        return new WorkflowPublicationPreflightPlan(
            resolved,
            slot,
            preflightService.Evaluate(candidateClaims, authoritativeSets),
            candidateClaims);
    }

    private static PublicationTriggerClaim Claim(string publicationId, WorkflowTriggerBinding binding) =>
        new(
            WorkflowTriggerBinding.BuildId(publicationId, binding.ArtifactId, binding.ExecutableNodeId, binding.StimulusHash),
            publicationId,
            binding.ArtifactId,
            binding.ExecutableNodeId,
            binding.StimulusType,
            binding.StimulusHash,
            binding.Cardinality == TriggerCardinality.Exclusive
                ? PublicationTriggerCardinality.Exclusive
                : PublicationTriggerCardinality.FanOut,
            binding.Metadata);

    private static void ValidateExpectedPublication(string? expectedPublicationId, WorkflowActivationSlot? slot)
    {
        if (expectedPublicationId is not null &&
            !StringComparer.Ordinal.Equals(expectedPublicationId, slot?.ActiveActivationId))
            throw new PublicationPolicyResolutionException("expected_publication_mismatch", "The publication slot authority changed.");
    }
}

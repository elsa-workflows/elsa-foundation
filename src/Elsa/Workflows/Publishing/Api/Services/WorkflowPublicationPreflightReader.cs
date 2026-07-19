using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed record WorkflowPublicationPreflightPlan(
    ResolvedPublicationAction ResolvedAction,
    PublicationSlot? Slot,
    PublicationPreflightResult Result,
    IReadOnlyCollection<PublicationTriggerClaim> CandidateClaims);

/// <summary>Builds the same policy-resolved, publication-scoped trigger plan used by preview and activation.</summary>
public sealed class WorkflowPublicationPreflightReader(
    IPublicationPolicyStore policyStore,
    IPublicationPolicyResolver policyResolver,
    IPublicationSlotStore slotStore,
    IPublicationRecordStore publicationStore,
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
        var slot = await slotStore.FindAsync(identity.DefinitionId, resolved.SlotName, cancellationToken);
        ValidateExpectedPublication(expectedPublicationId, slot);

        var candidateClaims = triggerExtractor.Evaluate(executable).Bindings
            .Select(binding => Claim(candidatePublicationId, binding))
            .ToArray();
        var authoritativeSets = new List<PublicationAuthoritativeClaimSet>();
        foreach (var authoritativeSlot in await slotStore.ListByDefinitionAsync(identity.DefinitionId, cancellationToken))
        {
            if (authoritativeSlot.ActivePublicationId is not { } activePublicationId)
                continue;
            var active = await publicationStore.FindAsync(activePublicationId, cancellationToken)
                ?? throw new InvalidOperationException($"Active publication '{activePublicationId}' does not exist.");
            var activeBindings = await triggerBindingStore.ListAllByPublicationAsync(activePublicationId, cancellationToken);
            authoritativeSets.Add(new PublicationAuthoritativeClaimSet(
                activePublicationId,
                active.SlotName,
                StringComparer.Ordinal.Equals(activePublicationId, slot?.ActivePublicationId),
                activeBindings.Select(binding => Claim(activePublicationId, binding)).ToArray()));
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

    private static void ValidateExpectedPublication(string? expectedPublicationId, PublicationSlot? slot)
    {
        if (expectedPublicationId is not null &&
            !StringComparer.Ordinal.Equals(expectedPublicationId, slot?.ActivePublicationId))
            throw new PublicationPolicyResolutionException("expected_publication_mismatch", "The publication slot authority changed.");
    }
}

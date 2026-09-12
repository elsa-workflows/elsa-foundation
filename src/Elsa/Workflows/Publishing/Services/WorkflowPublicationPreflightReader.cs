using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

public sealed record WorkflowPublicationPreflightPlan(
    ResolvedPublicationAction ResolvedAction,
    WorkflowActivationSlot? Slot,
    PublicationPreflightResult Result,
    IReadOnlyCollection<PublicationTriggerClaim> CandidateClaims)
{
    /// <summary>
    /// The activation source that owns the target slot when it is not the publish pipeline, or <c>null</c> when the
    /// slot is empty, deactivated (deactivation clears the source), or already owned by publishing.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="Slot"/> rather than stored, so a plan cannot carry a slot and an owner that disagree.
    /// The predicate mirrors the activation authorities' <c>ForeignSource</c> refusal for a publishing request, which
    /// is where ownership is enforced; this only lets preflight and publish see that refusal before any write.
    /// </remarks>
    public WorkflowActivationSource? TargetSlotOwner =>
        Slot is { ActiveActivationId: not null, Source: { } source } && !source.IsSameOwnerAs(PublicationActivator.Source)
            ? source
            : null;

    /// <summary>
    /// True only when neither a trigger conflict nor a foreign target-slot owner stands in the way. Every consumer
    /// gates on this rather than on <see cref="PublicationPreflightResult.CanActivate"/>, which covers triggers only.
    /// </summary>
    public bool CanActivate => Result.CanActivate && TargetSlotOwner is null;
}

/// <summary>Builds the same policy-resolved, publication-scoped trigger plan used by preview and activation.</summary>
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

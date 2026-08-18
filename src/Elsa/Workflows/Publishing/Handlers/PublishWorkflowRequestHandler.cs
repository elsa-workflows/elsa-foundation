using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Handlers;

/// <summary>Compiles an immutable artifact and activates it through one policy-resolved publication slot.</summary>
public sealed class PublishWorkflowRequestHandler(
    IWorkflowExecutableCompiler compiler,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowTriggerBindingExtractor triggerExtractor,
    IWorkflowTriggerBindingStore triggerBindingStore,
    IWorkflowDefinitionVersionLayoutStore layoutStore,
    // The root-write lease is taken by IWorkflowActivationCoordinator, not here.
    IWorkflowActivationAuthority activationAuthority,
    IPublicationPolicyStore policyStore,
    IPublicationPolicyResolver policyResolver,
    IPublicationRecordStore publicationStore,
    IPublicationPreflightService preflightService,
    IPublicationActivator activator,
    TimeProvider timeProvider,
    WorkflowPublicationPreflightReader? publicationPreflightReader = null,
    IWorkflowDefinitionVersionStore? workflowVersionStore = null,
    PublicationSnapshotReviewService? snapshotReviews = null,
    WorkflowExecutablePlacementSidecarContext? placementSidecars = null,
    WorkflowExecutableAuthoredInputsSidecar? authoredInputsSidecar = null,
    ILogger<PublishWorkflowRequestHandler>? logger = null,
    IExpressionDraftSemanticValidator? expressionValidator = null)
    : IRequestHandler<PublishWorkflow, PublishedWorkflowView>
{
    private const string PublishedArtifactPrefix = "artifact-";
    private readonly WorkflowPublicationPreflightReader _publicationPreflightReader = publicationPreflightReader ?? new(
        policyStore,
        policyResolver,
        activationAuthority,
        preflightService,
        triggerExtractor,
        triggerBindingStore,
        timeProvider);

    public async Task<PublishedWorkflowView> Handle(PublishWorkflow request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (expressionValidator is null || workflowVersionStore is null)
            throw new ExpressionPublicationValidationException(new(
                ExpressionDraftValidationState.Unavailable,
                [],
                "expression-validation-unavailable"));
        Elsa.Workflows.Design.Persistence.Core.Entities.WorkflowDefinitionVersion version;
        try
        {
            version = await workflowVersionStore.GetWithDefinitionAsync(request.VersionId, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new WorkflowExecutableCompilationException(null, request.VersionId, exception.Message, exception);
        }
        var expressionValidation = await ExpressionDraftSemanticValidation.ValidateSafelyAsync(
            expressionValidator,
            version.State,
            request.VersionId,
            cancellationToken);
        if (expressionValidation.State != ExpressionDraftValidationState.Valid)
            throw new ExpressionPublicationValidationException(expressionValidation);
        var now = timeProvider.GetUtcNow();
        var executable = await CompileAsync(request.VersionId, request.TenantId, now, cancellationToken);
        var identity = executable.Identity;
        var resolvedReview = request.PreflightToken is { } preflightToken
            ? await (snapshotReviews ?? throw new InvalidOperationException("Publication snapshot review services are not configured."))
                .GetAsync(preflightToken, request.TenantId, cancellationToken)
            : null;
        if (resolvedReview is not null)
            snapshotReviews!.ValidateRequestedIntent(resolvedReview, request.Action, request.SlotName, request.ExpectedPublicationId);
        var requestIntent = resolvedReview is not null
            ? RequestIntent(resolvedReview.RequestedAction, resolvedReview.RequestedSlotName)
            : RequestIntent(request.Action, request.SlotName);
        var publicationId = $"publication-{ShortIdentityGenerator.Generate(now)}";
        var plan = await _publicationPreflightReader.EvaluateAsync(
            executable,
            requestIntent,
            resolvedReview?.ActivePublicationId ?? request.ExpectedPublicationId,
            publicationId,
            cancellationToken);
        if (resolvedReview is not null)
        {
            var reviewedVersion = await workflowVersionStore.GetWithDefinitionAsync(request.VersionId, cancellationToken);
            var layout = await layoutStore.FindByVersionIdAsync(request.VersionId, cancellationToken);
            var candidateHash = snapshotReviews!.ComputeCandidateHash(reviewedVersion.State, layout?.Records ?? []);
            await snapshotReviews.ValidateAndConsumeAsync(
                request.PreflightToken!, candidateHash, plan, request.TenantId, cancellationToken);
        }
        if (!plan.Result.CanActivate)
            throw new PublicationPreflightConflictException(plan.Result.Conflicts);

        // A supplied snapshot token is fully revalidated and consumed before this first write. Stale candidates,
        // intents, policies, and slot authorities therefore fail without persisting an executable or publication.
        await executableStore.SaveAsync(executable, cancellationToken);

        var resolved = plan.ResolvedAction;
        var slot = plan.Slot;
        // Publishing keeps its own four-condition same-artifact guard and calls the coordinator only once it has
        // decided to activate. The coordinator's own no-op cannot see trigger-change retention — a publication-plan
        // concept — and delegating wholesale would therefore lose a condition publishing owns.
        //
        // The guard only speaks for slots publishing owns. Ownership is read from the slot's explicit
        // WorkflowActivationSource (P3: never inferred from the activation id), and it is checked BEFORE the
        // journal is consulted. A slot activated by artifact reconciliation legitimately holds an
        // ActiveActivationId with no PublicationRecord behind it; that absence means "not published by me", not
        // "your data is missing" (FR-B-006, the 2026-08-16 publish/activation split). Such a request falls through
        // to the coordinator, which answers it properly: the same artifact is the idempotent no-op, and a
        // different one is refused by the authority with a diagnostic naming the owning source.
        if (slot is { ActiveActivationId: { } activePublicationId, Source: { } activeSource } &&
            activeSource.IsSameOwnerAs(PublicationActivator.Source) &&
            await publicationStore.FindAsync(activePublicationId, cancellationToken) is { } current)
        {
            if (StringComparer.Ordinal.Equals(current.ArtifactId, identity.ArtifactId) &&
                plan.Result.Changes.All(change => change.Change == PublicationTriggerChangeKind.Retained))
            {
                var currentReference = current.SourceReferenceId is { } sourceReferenceId
                    ? await sourceReferenceStore.FindAsync(sourceReferenceId, cancellationToken)
                    : null;
                if (currentReference is not null &&
                    currentReference.DeletedAt is null &&
                    StringComparer.Ordinal.Equals(currentReference.TenantId, request.TenantId))
                    return PublishedWorkflowView.From(executable, currentReference, current, wasCreated: false);
            }
        }

        var reference = await BuildSourceReferenceAsync(executable, publicationId, resolved.SlotName, request.TenantId, now, cancellationToken);
        var candidate = new PublicationRecord(
            publicationId,
            WorkflowActivationSlotIdentity.Create(identity.DefinitionId, resolved.SlotName),
            identity.DefinitionId,
            identity.DefinitionVersionId,
            identity.ArtifactId,
            reference.SourceReferenceId,
            slot?.Revision ?? 0,
            PublicationStatus.Candidate,
            now,
            ActivatedAt: null,
            RetiredAt: null,
            Failure: null,
            resolved.SlotName);

        // The root-write lease, the reference mint/save, the failure retire and the predecessor retire all moved
        // into IWorkflowActivationCoordinator, which the activator now calls. Publishing requests activation; it
        // does not implement it (FR-B-006).
        var activation = await activator.ActivateAsync(
            new PublicationActivationRequest(candidate, executable, reference),
            cancellationToken);
        if (!activation.Succeeded)
        {
            logger?.LogWarning(
                "Publish: activation of publication {PublicationId} for workflow definition {DefinitionId} failed with '{FailureCode}'",
                publicationId,
                identity.DefinitionId,
                activation.Failure?.Code);
            throw new PublicationActivationException(activation.Failure);
        }

        return PublishedWorkflowView.From(executable, reference, activation.Publication);
    }

    private ValueTask<WorkflowExecutable> CompileAsync(
        string versionId,
        string? tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        compiler.CompileAsync(
            new WorkflowExecutableCompileRequest(
                versionId,
                WorkflowExecutableReferenceScope.Published,
                now,
                now,
                ExpiresAt: null,
                PublishedArtifactPrefix,
                new Dictionary<string, string> { ["slice"] = "workflow-execution-vertical-slice" },
                tenantId),
            cancellationToken);

    private static PublicationRequestIntent? RequestIntent(PublicationAction? action, string? slotName) =>
        action is { } requestedAction
            ? new PublicationRequestIntent(requestedAction, slotName)
            : slotName is not null
                ? new PublicationRequestIntent(PublicationAction.Replace, slotName)
                : null;

    private async ValueTask<WorkflowExecutableSourceReference> BuildSourceReferenceAsync(
        WorkflowExecutable executable,
        string publicationId,
        string slotName,
        string? tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var identity = executable.Identity;
        var layout = await layoutStore.FindByVersionIdAsync(identity.DefinitionVersionId, cancellationToken);
        var authoredInputs = workflowVersionStore is not null && authoredInputsSidecar is not null
            ? authoredInputsSidecar.CopyFrom((await workflowVersionStore.GetWithDefinitionAsync(identity.DefinitionVersionId, cancellationToken)).State)
            : [];
        // The coordinator owns activation↔reference identity, so the id it will stamp is minted here too: the
        // publication record has to carry the id the reference is actually stored under.
        return new WorkflowExecutableSourceReference(
            SourceReferenceId: WorkflowActivationReferenceIdentity.Create(publicationId),
            ArtifactId: identity.ArtifactId,
            SourceKind: WorkflowExecutableSourceKinds.WorkflowDefinitionVersion,
            SourceId: identity.DefinitionVersionId,
            SourceVersion: identity.ArtifactVersion,
            DefinitionId: identity.DefinitionId,
            DefinitionVersionId: identity.DefinitionVersionId,
            ArtifactVersion: identity.ArtifactVersion,
            CreatedAt: now,
            PublishedAt: now,
            Scope: WorkflowExecutableReferenceScope.Published,
            Layout: WorkflowExecutableLayoutSidecar.CopyFrom(layout),
            ActivationId: publicationId,
            SlotId: WorkflowActivationSlotIdentity.Create(identity.DefinitionId, slotName),
            LayoutSidecar: placementSidecars?.Get(identity.DefinitionVersionId),
            AuthoredInputs: authoredInputs,
            TenantId: tenantId,
            ActivityPresentation:
                WorkflowExecutableActivityPresentationSidecar.CopyFrom(
                    layout?.ActivityPresentation,
                    executable));
    }

}

public sealed class PublicationActivationException(PublicationFailure? failure)
    : InvalidOperationException(failure?.Message ?? "Publication activation failed.")
{
    public string Code { get; } = failure?.Code ?? "publication_activation_failed";
}

public sealed class PublicationPreflightConflictException(IReadOnlyCollection<PublicationTriggerConflict> conflicts)
    : InvalidOperationException("Publication trigger preflight found one or more authoritative conflicts.")
{
    public IReadOnlyCollection<PublicationTriggerConflict> Conflicts { get; } = conflicts;
}

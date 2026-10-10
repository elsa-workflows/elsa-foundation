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
        // Both dependencies are optional, and either one missing blocks publication the same way. The reported
        // code stays stable for callers; the log names which one is absent, so a composition gap is diagnosable
        // without sending every reader to expression validation.
        if (expressionValidator is null || workflowVersionStore is null)
        {
            var missing = new List<string>();
            if (expressionValidator is null)
                missing.Add(nameof(IExpressionDraftSemanticValidator));
            if (workflowVersionStore is null)
                missing.Add(nameof(IWorkflowDefinitionVersionStore));
            logger?.LogError(
                "Publish: publication of workflow definition version {VersionId} is blocked because publishing could not resolve {MissingDependencies}; compose the feature that registers each",
                request.VersionId,
                string.Join(" and ", missing));
            throw new ExpressionPublicationValidationException(new(
                ExpressionDraftValidationState.Unavailable,
                [],
                ExpressionDraftSemanticValidation.UnavailableCode));
        }
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
            cancellationToken,
            logger);
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
        if (!plan.CanActivate)
        {
            // A slot another activation source owns is not publishing's to take: ownership transfer is an operator
            // action. Activation enforces that only after the writes below, and not at all for the artifact the owner
            // already serves, so refuse here with its slot_owner_conflict. It outranks trigger conflicts, because
            // resolving those would still leave the slot to its owner.
            if (plan.TargetSlotOwner is { } slotOwner)
                throw ActivationFailed(publicationId, identity.DefinitionId,
                    SlotOwnerConflict(identity.DefinitionId, plan.ResolvedAction.SlotName, slotOwner));
            throw new PublicationPreflightConflictException(plan.Result.Conflicts);
        }

        // A supplied snapshot token is fully revalidated and consumed before this first write. Stale candidates,
        // intents, policies, slot authorities, and foreign slot owners therefore fail without persisting an
        // executable, source reference, or publication.
        await executableStore.SaveAsync(executable, cancellationToken);

        var resolved = plan.ResolvedAction;
        var slot = plan.Slot;
        if (slot is { ActiveActivationId: { } activePublicationId, Source: { } activeSource } &&
            activeSource.IsSameOwnerAs(PublicationActivator.Source))
        {
            var current = await publicationStore.FindAsync(activePublicationId, cancellationToken);
            if (current is not null &&
                StringComparer.Ordinal.Equals(current.ArtifactId, identity.ArtifactId) &&
                plan.Result.Changes.All(change => change.Change == PublicationTriggerChangeKind.Retained))
            {
                if (await AlreadyPublishedAsync(executable, current, resolved.SlotName, request.TenantId, cancellationToken) is { } answer)
                    return answer;
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

        var activation = await activator.ActivateAsync(
            new PublicationActivationRequest(candidate, executable, reference),
            cancellationToken);
        if (!activation.Succeeded)
            throw ActivationFailed(publicationId, identity.DefinitionId, activation.Failure);

        // The slot already served this artifact through another publication, which this candidate never replaced: the
        // same answer the early return above gives a same-version republish, and not a record of a publication that was
        // never minted.
        if (!StringComparer.Ordinal.Equals(activation.Publication.PublicationId, publicationId))
        {
            var served = activation.Publication;
            return await AlreadyPublishedAsync(executable, served, resolved.SlotName, request.TenantId, cancellationToken)
                ?? throw ActivationFailed(served.PublicationId, served.WorkflowDefinitionId, new PublicationFailure(
                    PublicationFailureCodes.PublicationActivationFailed,
                    $"Publication '{served.PublicationId}' already serves definition '{served.WorkflowDefinitionId}' slot '{resolved.SlotName}', " +
                    "but its source reference is gone. It is not reported as published."));
        }

        return PublishedWorkflowView.From(executable, reference, activation.Publication);
    }

    /// <summary>
    /// The answer for a publication that already serves this artifact: its live source reference for the requesting
    /// tenant, with the publication completed first when a stopped process left it a candidate. Null when the reference
    /// is gone or belongs to another tenant, so the caller decides whether to mint a new publication or refuse.
    /// </summary>
    private async ValueTask<PublishedWorkflowView?> AlreadyPublishedAsync(
        WorkflowExecutable executable,
        PublicationRecord publication,
        string slotName,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var reference = publication.SourceReferenceId is { } sourceReferenceId
            ? await sourceReferenceStore.FindAsync(sourceReferenceId, cancellationToken)
            : null;
        return reference is { DeletedAt: null } && StringComparer.Ordinal.Equals(reference.TenantId, tenantId)
            ? PublishedWorkflowView.From(
                executable,
                reference,
                await CompletedAsync(publication, slotName, cancellationToken),
                wasCreated: false)
            : null;
    }

    /// <summary>
    /// The publication the slot already names, once its journal record says it serves. A process that stopped after the
    /// runtime's switch leaves that record a candidate (#2223), so the publication is completed first, and a republish
    /// answers once it is active. One that cannot be completed is refused as a failed activation, never reported as
    /// published.
    /// </summary>
    private async ValueTask<PublicationRecord> CompletedAsync(PublicationRecord current, string slotName, CancellationToken cancellationToken)
    {
        if (current.Status == PublicationStatus.Active)
            return current;

        var completion = await activator.CompleteAsync(current.WorkflowDefinitionId, slotName, cancellationToken);
        if (completion.Publication is { Status: PublicationStatus.Active } completed &&
            StringComparer.Ordinal.Equals(completed.PublicationId, current.PublicationId))
            return completed;

        throw ActivationFailed(current.PublicationId, current.WorkflowDefinitionId, completion.Failure ?? new PublicationFailure(
            PublicationFailureCodes.PublicationActivationFailed,
            $"Publication '{current.PublicationId}' held definition '{current.WorkflowDefinitionId}' slot '{slotName}' with a " +
            $"'{current.Status}' record, which could not be brought into line with the slot: the slot moved on, or the " +
            "publication does not serve. It is not reported as published."));
    }

    /// <summary>
    /// Logs and builds the activation failure. The early owner refusal shares this log line with a refused activation,
    /// so moving that refusal ahead of the writes does not remove the warning operators already see for it.
    /// </summary>
    private PublicationActivationException ActivationFailed(string publicationId, string definitionId, PublicationFailure? failure)
    {
        logger?.LogWarning(
            "Publish: activation of publication {PublicationId} for workflow definition {DefinitionId} failed with '{FailureCode}'",
            publicationId,
            definitionId,
            failure?.Code);
        return new PublicationActivationException(failure);
    }

    private static PublicationFailure SlotOwnerConflict(string definitionId, string slotName, WorkflowActivationSource owner) => new(
        PublicationFailureCodes.SlotOwnerConflict,
        $"Definition '{definitionId}' slot '{slotName}' is owned by activation source '{owner.Describe()}'; " +
        $"'{PublicationActivator.Source.Describe()}' cannot publish to it. Ownership transfer is an explicit operator action.");

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
    public string Code { get; } = failure?.Code ?? PublicationFailureCodes.PublicationActivationFailed;
}

public sealed class PublicationPreflightConflictException(IReadOnlyCollection<PublicationTriggerConflict> conflicts)
    : InvalidOperationException("Publication trigger preflight found one or more authoritative conflicts.")
{
    public IReadOnlyCollection<PublicationTriggerConflict> Conflicts { get; } = conflicts;
    public string Code => PublicationFailureCodes.TriggerConflict;
}

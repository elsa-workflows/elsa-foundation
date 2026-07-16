using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Handlers;

/// <summary>Compiles an immutable artifact and activates it through one policy-resolved publication slot.</summary>
public sealed class PublishWorkflowRequestHandler(
    IWorkflowExecutableCompiler compiler,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowTriggerBindingExtractor triggerExtractor,
    IWorkflowTriggerBindingStore triggerBindingStore,
    IWorkflowDefinitionVersionLayoutStore layoutStore,
    IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager,
    IPublicationPolicyStore policyStore,
    IPublicationPolicyResolver policyResolver,
    IPublicationSlotStore slotStore,
    IPublicationRecordStore publicationStore,
    IPublicationPreflightService preflightService,
    IPublicationActivator activator,
    TimeProvider timeProvider,
    WorkflowPublicationPreflightReader? publicationPreflightReader = null,
    IWorkflowDefinitionVersionStore? workflowVersionStore = null,
    PublicationSnapshotReviewService? snapshotReviews = null,
    WorkflowExecutablePlacementSidecarContext? placementSidecars = null,
    WorkflowExecutableAuthoredInputsSidecar? authoredInputsSidecar = null)
    : IRequestHandler<PublishWorkflow, PublishedWorkflowView>
{
    private const string PublishedArtifactPrefix = "artifact-";
    private readonly WorkflowPublicationPreflightReader _publicationPreflightReader = publicationPreflightReader ?? new(
        policyStore,
        policyResolver,
        slotStore,
        publicationStore,
        preflightService,
        triggerExtractor,
        triggerBindingStore,
        timeProvider);

    public async Task<PublishedWorkflowView> Handle(PublishWorkflow request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var executable = await CompileAsync(request.VersionId, now, cancellationToken);
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
            var version = await (workflowVersionStore
                ?? throw new InvalidOperationException("Workflow definition version services are not configured."))
                .GetWithDefinitionAsync(request.VersionId, cancellationToken);
            var layout = await layoutStore.FindByVersionIdAsync(request.VersionId, cancellationToken);
            var candidateHash = snapshotReviews!.ComputeCandidateHash(version.State, layout?.Records ?? []);
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
        if (slot?.ActivePublicationId is { } activePublicationId)
        {
            var current = await publicationStore.FindAsync(activePublicationId, cancellationToken)
                ?? throw new InvalidOperationException($"Active publication '{activePublicationId}' does not exist.");
            if (StringComparer.Ordinal.Equals(current.ArtifactId, identity.ArtifactId) &&
                plan.Result.Changes.All(change => change.Change == PublicationTriggerChangeKind.Retained))
            {
                var currentReference = current.SourceReferenceId is { } sourceReferenceId
                    ? await sourceReferenceStore.FindAsync(sourceReferenceId, cancellationToken)
                    : null;
                if (currentReference is not null && currentReference.DeletedAt is null)
                    return PublishedWorkflowView.From(executable, currentReference, current, wasCreated: false);
            }
        }

        var reference = await BuildSourceReferenceAsync(executable, publicationId, resolved.SlotName, now, cancellationToken);
        var candidate = new PublicationRecord(
            publicationId,
            PublicationSlotIdentity.Create(identity.DefinitionId, resolved.SlotName),
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

        PublicationActivationResult? activation = null;
        await rootWriteLeaseManager.ExecuteAsync(
            identity.ArtifactId,
            $"publish:{publicationId}",
            async ct =>
            {
                await sourceReferenceStore.SaveAsync(reference, ct);
                try
                {
                    activation = await activator.ActivateAsync(new PublicationActivationRequest(candidate), ct);
                    if (!activation.Succeeded)
                        throw new PublicationActivationException(activation.Failure);
                }
                catch
                {
                    await sourceReferenceStore.RetireAsync(
                        reference.SourceReferenceId,
                        timeProvider.GetUtcNow(),
                        "publication-activation-failed",
                        CancellationToken.None);
                    throw;
                }
            },
            cancellationToken);

        if (activation!.ReplacedPublicationId is { } replacedPublicationId &&
            !StringComparer.Ordinal.Equals(replacedPublicationId, publicationId))
            await RetireReferenceAsync(replacedPublicationId, now, cancellationToken);

        return PublishedWorkflowView.From(executable, reference, activation.Publication);
    }

    private ValueTask<WorkflowExecutable> CompileAsync(string versionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        compiler.CompileAsync(
            new WorkflowExecutableCompileRequest(
                versionId,
                WorkflowExecutableReferenceScope.Published,
                now,
                now,
                ExpiresAt: null,
                PublishedArtifactPrefix,
                new Dictionary<string, string> { ["slice"] = "workflow-execution-vertical-slice" }),
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
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var identity = executable.Identity;
        var layout = await layoutStore.FindByVersionIdAsync(identity.DefinitionVersionId, cancellationToken);
        var authoredInputs = workflowVersionStore is not null && authoredInputsSidecar is not null
            ? authoredInputsSidecar.CopyFrom((await workflowVersionStore.GetWithDefinitionAsync(identity.DefinitionVersionId, cancellationToken)).State)
            : [];
        return new WorkflowExecutableSourceReference(
            SourceReferenceId: ShortIdentityGenerator.Generate(now),
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
            PublicationId: publicationId,
            SlotId: PublicationSlotIdentity.Create(identity.DefinitionId, slotName),
            LayoutSidecar: placementSidecars?.Get(identity.DefinitionVersionId),
            AuthoredInputs: authoredInputs);
    }

    private async ValueTask RetireReferenceAsync(string publicationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var publication = await publicationStore.FindAsync(publicationId, cancellationToken);
        if (publication?.SourceReferenceId is not { } sourceReferenceId)
            return;
        await sourceReferenceStore.RetireAsync(sourceReferenceId, now, "publication-replaced", cancellationToken);
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

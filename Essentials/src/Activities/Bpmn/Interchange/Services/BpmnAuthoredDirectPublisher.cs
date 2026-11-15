using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Process = Elsa.Activities.Bpmn.Core.BpmnActivityIdentities;

namespace Elsa.Activities.Bpmn.Interchange.Services;

internal sealed class BpmnAuthoredDirectPublisher(IWorkflowDefinitionVersionStore versions,
    IWorkflowDefinitionStore definitions, IWorkflowDefinitionVersionLayoutStore layouts, IActivityDefinitionLookup catalog,
    NormalizedPrincipalValidator validator, IPermissionAuthorizationService authorization, IBpmnModelRuntime runtime,
    IWorkflowExecutableSourceReferenceReader sources, TimeProvider clock,
    IPublicationActivator? activator = null, IPublicationRecordStore? publications = null,
    IWorkflowActivationAuthority? slots = null, IPublicationPolicyStore? policies = null,
    IPublicationPolicyResolver? policyResolver = null, IPersistenceAccessContextAccessor? access = null)
{
    internal const string PublishPermission = "bpmn:publish";
    // Existing workflow publication permission; the domain bridge does not depend on its API assembly.
    internal const string WorkflowPublishPermission = "workflows/publishing:manage";
    internal const string WorkflowReadPermission = "workflows/runtime:read";

    public async Task<BpmnPublishedModel> PublishAsync(string versionId, string requestId, ClaimsPrincipal user, CancellationToken cancellationToken,
        PublicationRequestIntent? intent = null, long? expectedSlotRevision = null, string? expectedPublicationId = null)
        => await PublishCoreAsync(versionId, requestId, user, cancellationToken, intent, expectedSlotRevision, expectedPublicationId, 2);

    private async Task<BpmnPublishedModel> PublishCoreAsync(string versionId, string requestId, ClaimsPrincipal user,
        CancellationToken cancellationToken, PublicationRequestIntent? intent, long? expectedSlotRevision,
        string? expectedPublicationId, int createReconciliations)
    {
        if (!validator.TryGetNormalizedPrincipal(user, out var principal)) throw new BpmnModelResourceMissingException();
        var subjects = principal.Claims.Where(claim => claim.Type is ClaimTypes.NameIdentifier or "sub")
            .Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
        var tenants = principal.FindAll(IdentityClaimTypes.TenantId).ToArray();
        if (subjects is not [{ Length: > 0 }] || tenants.Length > 1 || tenants.Any(claim => string.IsNullOrWhiteSpace(claim.Value)))
            throw new BpmnModelResourceMissingException();
        var caller = new BpmnModelCaller(subjects[0], tenants.SingleOrDefault()?.Value);
        var version = await versions.FindByIdAsync(versionId, cancellationToken);
        if (version is null || version.Id != versionId || version.TenantId != caller.TenantId) throw new BpmnModelResourceMissingException();
        var definition = await definitions.FindByIdAsync(version.DefinitionId, cancellationToken);
        if (definition is null || definition.Id != version.DefinitionId || definition.TenantId != caller.TenantId || definition.DeletedAt is not null)
            throw new BpmnModelResourceMissingException();
        foreach (var permission in new[] { PublishPermission, WorkflowPublishPermission })
            if (!(await authorization.AuthorizeAsync(new(principal, permission, caller.TenantId, version), cancellationToken)).Succeeded)
                throw new BpmnModelResourceMissingException();
        if (definition.IsSourceOwned)
            throw new BpmnModelValidationException("This source-owned definition cannot be directly published through the catalog authoring action.");
        var root = BpmnAuthoredDirectProjection.ReadState(version)?.RootActivity;
        var activity = root is null ? null : await catalog.FindVersion(root.ActivityVersionId, cancellationToken);
        if (activity is null ||
            !SemVer.TryParse(activity.Version, out var activityVersion) || activityVersion.Major != 1 || activityVersion.Minor != 0 ||
            activityVersion.Patch != 0 || activityVersion.Prerelease is not null ||
            !(await catalog.ListDefinitions(id: activity.DefinitionId, cancellationToken: cancellationToken))
                .Any(candidate => candidate.Id == activity.DefinitionId && candidate.ActivityTypeKey == Process.ProcessType))
            throw new BpmnModelValidationException("The immutable version root does not name a visible compatible BpmnProcess activity.");
        var layout = await layouts.FindByVersionIdAsync(version.Id, cancellationToken);
        if (layout is not null && (layout.WorkflowDefinitionVersionId != version.Id || layout.TenantId != caller.TenantId))
            throw new BpmnModelResourceMissingException();
        var projected = BpmnAuthoredDirectProjection.Project(version, layout);
        var inputContract = BpmnAuthoredDirectProjection.InputContract(version);
        var inputCapable = inputContract.Inputs.Count != 0 || projected.Services.Any(item => item.InputMapping is not null);
        foreach (var service in projected.Services)
        {
            var source = await sources.FindAsync(service.SourceReferenceId, cancellationToken);
            var pin = service.Binding.Executable;
            if (source is null || source.SourceReferenceId != service.SourceReferenceId || source.TenantId != caller.TenantId ||
                source.Scope != WorkflowExecutableReferenceScope.Published || !source.IsLive(clock.GetUtcNow()) ||
                source.ArtifactId != pin.ArtifactId || !await WorkerSourceMatchesAsync(source, pin, inputCapable, caller, cancellationToken) ||
                !(await authorization.AuthorizeAsync(new(principal, WorkflowReadPermission, caller.TenantId, source), cancellationToken)).Succeeded)
                throw new BpmnModelResourceMissingException();
        }
        if (activator is null || publications is null || slots is null || policies is null || policyResolver is null)
            throw new NotSupportedException("Authored BPMN publication requires the shared publication journal, policy and activation slot coordinator.");
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var material = await runtime.PreparePublicationAsync(projected.Definition, inputContract,
            projected.Services.Where(item => item.InputMapping is not null).Select(item => item.InputMapping!).ToArray(), caller, cancellationToken);
        var publicationId = "bpmn-publication:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new[] { caller.TenantId ?? "", caller.ActorId, requestId }))).ToLowerInvariant();
        var sourceId = WorkflowActivationReferenceIdentity.Create(publicationId);
        var now = clock.GetUtcNow();
        var prior = await publications.FindAsync(publicationId, cancellationToken);
        // Bind supplied intent, including omission, rather than today's resolved policy/slot authority.
        var requestFingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1, definitionId = definition.Id, versionId = version.Id, caller.ActorId, caller.TenantId, requestId,
            intentProvided = intent is not null, action = intent is null ? (int?)null : (int)intent.Action,
            slotName = intent?.SlotName, expectedSlotRevision, expectedPublicationId
        }))).ToLowerInvariant();
        if (prior is not null && prior.RequestFingerprint != requestFingerprint)
            throw new RuntimeCheckpointReplayConflictException("publish:" + requestId);
        var hostPolicy = await policies.FindAsync(null, cancellationToken)
            ?? new PublicationPolicy(null, PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 0, now);
        // An already accepted operation retains its resolved slot when the default policy later changes.
        var resolved = prior is not null
            ? new ResolvedPublicationAction(definition.Id, version.Id, intent?.Action ?? PublicationAction.Replace, prior.SlotName, PublicationPolicySource.Request, null)
            : policyResolver.Resolve(definition.Id, version.Id, intent, await policies.FindAsync(definition.Id, cancellationToken), hostPolicy);
        var slot = await slots.FindAsync(definition.Id, resolved.SlotName, cancellationToken);
        var slotId = WorkflowActivationSlotIdentity.Create(definition.Id, resolved.SlotName);
        if (prior is not null && (prior.WorkflowDefinitionId != definition.Id || prior.WorkflowDefinitionVersionId != version.Id ||
            prior.ArtifactId != material.Identity.ArtifactId || prior.SlotId != slotId ||
            expectedSlotRevision.HasValue && prior.ExpectedSlotRevision != expectedSlotRevision.Value))
            throw new RuntimeCheckpointReplayConflictException("publish:" + requestId);
        if (prior is { Status: PublicationStatus.Active or PublicationStatus.Retired })
            return Published(prior, slot?.Revision ?? 0);
        if (prior is { Status: PublicationStatus.Failed })
        {
            if (prior.Failure?.Code == PublicationFailureCodes.ArtifactAlreadyServing && slot?.ActiveActivationId is { } winner &&
                await publications.FindAsync(winner, cancellationToken) is { Status: PublicationStatus.Active } serving && serving.ArtifactId == prior.ArtifactId)
                return Published(serving, slot.Revision);
            throw new BpmnModelValidationException(prior.Failure?.Message ?? "This publication request previously failed; use a new request id.");
        }
        if (prior is null && expectedPublicationId is not null && slot?.ActiveActivationId != expectedPublicationId)
            throw new PublicationPolicyResolutionException(PublicationFailureCodes.ExpectedPublicationMismatch, "The serving publication changed.");
        var candidate = prior ?? new PublicationRecord(publicationId, slotId, definition.Id, version.Id, material.Identity.ArtifactId,
            sourceId, expectedSlotRevision ?? slot?.Revision ?? 0, PublicationStatus.Candidate, now, null, null, null, resolved.SlotName)
        { RequestFingerprint = requestFingerprint };
        var reference = new WorkflowExecutableSourceReference(sourceId, material.Identity.ArtifactId, "BpmnModel", version.Id, version.Version,
            definition.Id, version.Id, version.Version, candidate.CreatedAt, candidate.CreatedAt, WorkflowExecutableReferenceScope.Published,
            Layout: projected.Source.Layout, TenantId: caller.TenantId, ActivityPresentation: projected.Source.ActivityPresentation,
            ActivationId: publicationId, SlotId: slotId);
        PublicationActivationResult activation;
        try { activation = await activator.ActivateMaterialAsync(new(candidate, material, reference), cancellationToken); }
        catch (PublicationRecordAlreadyExistsException exception) when (exception.PublicationId == publicationId && createReconciliations > 0)
        {
            // Only a proved create-only collision permits reconciliation. The next read verifies the full
            // intent and reuses the winner's timestamps/revision, or returns its terminal result. One more
            // reconciliation covers a candidate becoming terminal between that read and the repeated save.
            if (await publications.FindAsync(publicationId, cancellationToken) is null) throw;
            return await PublishCoreAsync(versionId, requestId, user, cancellationToken, intent,
                expectedSlotRevision, expectedPublicationId, createReconciliations - 1);
        }
        if (!activation.Succeeded) throw new BpmnModelValidationException(activation.Failure?.Message ?? "The model publication could not activate.");
        return Published(activation.Publication, activation.Slot.Revision);

        BpmnPublishedModel Published(PublicationRecord publication, long revision) => new(material.Identity, material.IncarnationId,
            publication.SourceReferenceId ?? throw new BpmnModelValidationException("The publication lacks its exact source reference."), material.Model!.Profile)
        { Publication = new(publication.PublicationId, publication.WorkflowDefinitionId, publication.WorkflowDefinitionVersionId,
            publication.SlotName, revision, publication.Status.ToString()) };
    }
    private async ValueTask<bool> WorkerSourceMatchesAsync(WorkflowExecutableSourceReference source, WorkflowExecutableIdentity worker,
        bool inputCapable, BpmnModelCaller caller, CancellationToken cancellationToken)
    {
        // Ordinary producer-bound sources retain their legacy contract. Only the checked material-neutral
        // writer can assert PublicationScope; its logical identity is proved by the actual current slot.
        if (source.PublicationScope is null) return inputCapable || source.DefinitionId == worker.DefinitionId &&
            source.DefinitionVersionId == worker.DefinitionVersionId && source.ArtifactVersion == worker.ArtifactVersion;
        var slot = slots is null ? null : (await slots.ListByDefinitionAsync(source.DefinitionId, cancellationToken))
            .SingleOrDefault(item => item.SlotId == source.SlotId);
        return slot is not null && access is not null && WorkflowExecutableSourceReferenceComparer.MatchesPublication(source, slot,
            access.Current.RequireScope(admitPrivileged: true), caller.TenantId, clock.GetUtcNow());
    }

}

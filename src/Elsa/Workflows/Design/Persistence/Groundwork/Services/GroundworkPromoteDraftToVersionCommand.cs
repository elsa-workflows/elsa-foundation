using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkPromoteDraftToVersionCommand(
    IDistributedLockProvider lockProvider,
    IDocumentStore store,
    IDesignAtomicWriter atomicWrite,
    IPayloadSerializer payloadSerializer,
    IInlineEventPublisher inlineEventPublisher,
    IWorkflowDefinitionVersionStore versionStore,
    IIdentityGenerator identityGenerator,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IPromoteDraftToVersionCommand
{
    private const string OperationKind = "workflow.draft.promote.v1";

    public async Task<string> Execute(
        DesignOperationKey operationKey,
        string draftId,
        CancellationToken cancellationToken = default)
        => await Execute(operationKey, draftId, requestedVersion: null, cancellationToken);

    public async Task<string> Execute(
        DesignOperationKey operationKey,
        string draftId,
        string? requestedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        var normalizedRequestedVersion = requestedVersion?.Trim();

        var documents = new GroundworkWorkflowDefinitionDraftDocumentStore(
            store,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor);
        var draftLockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);
        IDistributedSynchronizationHandle? draftLock = null;
        IDistributedSynchronizationHandle? definitionLock = null;
        GroundworkDesignAtomicCommandResult<PromoteDraftResult> outcome;

        try
        {
            outcome = await GroundworkDesignAtomicCommand.ExecuteAsync(
                atomicWrite,
                operationKey,
                OperationKind,
                new PromoteDraftRequestMaterial(
                    draftId,
                    normalizedRequestedVersion is null ? "automatic" : "exact",
                    normalizedRequestedVersion),
                [
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind
                ],
                async (context, token) =>
                {
                    var document = await documents.FindByIdAsync(draftId, token)
                                   ?? throw EntityNotFoundException.ForEntity(
                                       typeof(WorkflowDefinitionDraft),
                                       draftId);

                    // FR-024 promotion gate: derive errors against the loaded Draft (see DraftValidationGate).
                    // Runs inside the per-Draft lock, so the validated state is exactly the state promoted.
                    var errors = await inlineEventPublisher.DeriveValidationErrorsAsync(document.Entity, token);
                    if (errors.Count > 0)
                        throw new DraftHasValidationErrorsException(draftId, errors);

                    var draft = document.Entity;
                    var lastVersion = await versionStore.FindLatestVersionAsync(
                        draft.WorkflowDefinitionId,
                        token);
                    var initialAssessment = WorkflowVersionNumbering.AssessPromotion(
                        lastVersion?.Version,
                        normalizedRequestedVersion,
                        versionIdentityExists: false);
                    if (initialAssessment.ResolvedVersion is null)
                        ThrowIfRejected(initialAssessment, draft.WorkflowDefinitionId);

                    var versionIdentityExists = await versionStore.ExistsAsync(
                        draft.WorkflowDefinitionId,
                        Elsa.Primitives.Versioning.SemVer.ToSortKey(initialAssessment.ResolvedVersion!),
                        token);
                    var assessment = WorkflowVersionNumbering.AssessPromotion(
                        lastVersion?.Version,
                        normalizedRequestedVersion,
                        versionIdentityExists);
                    ThrowIfRejected(assessment, draft.WorkflowDefinitionId);
                    var versionId = identityGenerator.Generate();
                    var version = new WorkflowDefinitionVersion(
                        draft.WorkflowDefinitionId,
                        assessment.ResolvedVersion!)
                    {
                        Id = versionId,
                        TenantId = draft.TenantId,
                        State = draft.State,
                        SourceDraftId = draft.Id
                    };
                    var versionLayout = new WorkflowDefinitionVersionLayout
                    {
                        Id = identityGenerator.Generate(),
                        TenantId = draft.TenantId,
                        WorkflowDefinitionVersionId = versionId,
                        Records = document.Layout.ToList()
                    };
                    var now = clock.UtcNow;
                    GroundworkEntityTimestamps.StampAdded(version, now);
                    GroundworkEntityTimestamps.StampAdded(versionLayout, now);
                    var versionSave = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                        WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                        WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
                        WorkflowsDesignStorageManifest.SchemaVersion,
                        version,
                        GroundworkDesignDocumentSerialization.Create(payloadSerializer),
                        accessContextAccessor.Current,
                        persistenceDomain: DesignPersistenceDomain.Workflow) with
                    { ExpectedVersion = 0 };
                    var layoutSave = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                        WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
                        WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutCollection,
                        WorkflowsDesignStorageManifest.SchemaVersion,
                        versionLayout,
                        GroundworkDesignJson.Options,
                        accessContextAccessor.Current,
                        persistenceDomain: DesignPersistenceDomain.Workflow) with
                    { ExpectedVersion = 0 };
                    await context.SaveAsync(versionSave, token);
                    await context.SaveAsync(layoutSave, token);
                    return new PromoteDraftResult(
                        draft.Id,
                        draft.WorkflowDefinitionId,
                        version.Id,
                        version.Version);
                },
                cancellationToken: cancellationToken,
                beforeAttempt: async token =>
                {
                    // Marker replay must win before source reads and locks: a successfully promoted
                    // draft may later be discarded while its authoritative version remains valid.
                    draftLock = await lockProvider.AcquireLockAsync(draftLockKey, null, token);
                    var observed = await documents.FindByIdAsync(draftId, token)
                                   ?? throw EntityNotFoundException.ForEntity(
                                       typeof(WorkflowDefinitionDraft),
                                       draftId);
                    var definitionLockKey =
                        WorkflowDesignPersistenceLockKeys.DefinitionKey(observed.Entity.WorkflowDefinitionId);
                    definitionLock = await lockProvider.AcquireLockAsync(definitionLockKey, null, token);
                });
        }
        catch (GroundworkDesignOperationConflictException exception)
        {
            throw new WorkflowPromotionOperationConflictException(exception.Message, exception);
        }
        finally
        {
            try
            {
                if (definitionLock is not null)
                    await definitionLock.DisposeAsync();
            }
            finally
            {
                if (draftLock is not null)
                    await draftLock.DisposeAsync();
            }
        }

        return outcome.Value.VersionId;
    }

    private static void ThrowIfRejected(
        WorkflowPromotionVersionAssessment assessment,
        string definitionId)
    {
        if (assessment.IsReady)
            return;

        var issue = assessment.Issues.Single();
        if (issue.Code == "version-conflict")
            throw new WorkflowDefinitionVersionConflictException(
                definitionId,
                assessment.RequestedVersion ?? assessment.ResolvedVersion ?? "automatic");

        throw new WorkflowVersionSelectionException(issue.Code, issue.Message);
    }

    private sealed record PromoteDraftRequestMaterial(
        string DraftId,
        string AssignmentMode,
        string? RequestedVersion);

    private sealed record PromoteDraftResult(
        string DraftId,
        string WorkflowDefinitionId,
        string VersionId,
        string Version);
}

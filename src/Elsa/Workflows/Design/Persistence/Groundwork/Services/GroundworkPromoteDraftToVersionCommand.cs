using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Design.Persistence.Core.Models;
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
using Groundwork.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkPromoteDraftToVersionCommand(
    IDistributedLockProvider lockProvider,
    GroundworkDesignStorage storage,
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
    private readonly IWorkflowDefinitionVersionStore versionStore = versionStore;
    private readonly GroundworkWorkflowDefinitionVersionStore? transactionVersionStore =
        versionStore as GroundworkWorkflowDefinitionVersionStore;

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
            storage,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor);
        var draftLockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);
        IDistributedSynchronizationHandle? draftLock = null;
        IDistributedSynchronizationHandle? definitionLock = null;
        PromotionVersionReadState? replacementVersionState = null;
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
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind
                ],
                async (context, token) =>
                {
                    var transactionDocuments = documents.ForStorage(context.Storage);
                    var document = await transactionDocuments.FindByIdAsync(draftId, token)
                                   ?? throw EntityNotFoundException.ForEntity(
                                       typeof(WorkflowDefinitionDraft),
                                       draftId);

                    // FR-024 promotion gate: derive errors against the loaded Draft (see DraftValidationGate).
                    // Runs inside the per-Draft lock, so the validated state is exactly the state promoted.
                    var errors = await inlineEventPublisher.DeriveValidationErrorsAsync(document.Entity, token);
                    if (errors.Count > 0)
                        throw new DraftHasValidationErrorsException(draftId, errors);

                    var draft = document.Entity;
                    var versionState = transactionVersionStore is not null
                        ? await ReadPromotionVersionStateAsync(
                            transactionVersionStore.ForStorage(context.Storage),
                            draft.WorkflowDefinitionId,
                            normalizedRequestedVersion,
                            token)
                        : replacementVersionState
                          ?? throw new InvalidOperationException(
                              "The replacement workflow version store was not read under the definition lock.");
                    var assessment = WorkflowVersionNumbering.AssessPromotion(
                        versionState.LatestVersion?.Version,
                        normalizedRequestedVersion,
                        versionState.IdentityExists);
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
                        Records = document.Layout.ToList(),
                        ActivityPresentation = document.ActivityPresentation.ToList()
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
                        GroundworkDesignDocumentSerialization.Create(payloadSerializer),
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
                    if (transactionVersionStore is null)
                    {
                        // A replacement/decorator cannot be rebound to the active Groundwork transaction.
                        // Preserve that public extension point, but read it only after both aggregate locks
                        // are held and before the transaction opens so SQLite cannot re-enter its provider gate.
                        replacementVersionState = await ReadPromotionVersionStateAsync(
                            versionStore,
                            observed.Entity.WorkflowDefinitionId,
                            normalizedRequestedVersion,
                            token);
                    }
                });
        }
        catch (GroundworkDesignOperationConflictException exception)
        {
            throw new WorkflowPromotionOperationConflictException(exception.Message, exception);
        }
        catch (GroundworkDesignOperationRejectedException)
        {
            // A final version identity CreateOnly race is an observable version conflict,
            // even when the public-v2 provider reports it as an unsuccessful batch outcome.
            throw new WorkflowDefinitionVersionConflictException(
                draftId,
                normalizedRequestedVersion ?? "automatic");
        }
        catch (DesignPersistenceException exception) when (exception.InnerException is BatchWriteException)
        {
            throw new WorkflowDefinitionVersionConflictException(
                draftId,
                normalizedRequestedVersion ?? "automatic");
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

    private static async Task<PromotionVersionReadState> ReadPromotionVersionStateAsync(
        IWorkflowDefinitionVersionStore store,
        string definitionId,
        string? requestedVersion,
        CancellationToken cancellationToken)
    {
        var latestVersion = await store.FindLatestVersionAsync(definitionId, cancellationToken);
        var initialAssessment = WorkflowVersionNumbering.AssessPromotion(
            latestVersion?.Version,
            requestedVersion,
            versionIdentityExists: false);
        var candidateIdentitySortKey = WorkflowVersionNumbering.GetCandidateIdentitySortKey(initialAssessment);
        var identityExists = candidateIdentitySortKey is not null &&
                             await store.ExistsAsync(definitionId, candidateIdentitySortKey, cancellationToken);
        return new(latestVersion, identityExists);
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

    private sealed record PromotionVersionReadState(
        WorkflowDefinitionVersion? LatestVersion,
        bool IdentityExists);

    private sealed record PromoteDraftResult(
        string DraftId,
        string WorkflowDefinitionId,
        string VersionId,
        string Version);
}

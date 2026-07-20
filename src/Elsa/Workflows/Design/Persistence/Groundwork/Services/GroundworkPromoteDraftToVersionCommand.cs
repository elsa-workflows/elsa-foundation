using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
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
    GroundworkDesignAtomicWrite atomicWrite,
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
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);

        var documents = new GroundworkWorkflowDefinitionDraftDocumentStore(
            store,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor);
        var observed = await documents.FindByIdAsync(draftId, cancellationToken)
                       ?? throw new InvalidOperationException($"Workflow definition draft '{draftId}' not found");
        var definitionLockKey = WorkflowDesignPersistenceLockKeys.DefinitionKey(observed.Entity.WorkflowDefinitionId);
        var draftLockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);

        await using var definitionLock = await lockProvider.AcquireLockAsync(definitionLockKey, null, cancellationToken);
        await using var draftLock = await lockProvider.AcquireLockAsync(draftLockKey, null, cancellationToken);

        var outcome = await GroundworkDesignAtomicCommand.ExecuteAsync(
            atomicWrite,
            operationKey,
            OperationKind,
            new PromoteDraftRequestMaterial(draftId),
            [
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind
            ],
            async (context, token) =>
            {
                var document = await documents.FindByIdAsync(draftId, token)
                               ?? throw new InvalidOperationException(
                                   $"Workflow definition draft '{draftId}' not found");

                // FR-024 promotion gate: derive errors against the loaded Draft (see DraftValidationGate).
                // Runs inside the per-Draft lock, so the validated state is exactly the state promoted.
                var errors = await inlineEventPublisher.DeriveValidationErrorsAsync(document.Entity, token);
                if (errors.Count > 0)
                    throw new DraftHasValidationErrorsException(draftId, errors.Count);

                var draft = document.Entity;
                var lastVersion = await versionStore.FindLatestVersionAsync(
                    draft.WorkflowDefinitionId,
                    token);
                var versionId = identityGenerator.Generate();
                var version = new WorkflowDefinitionVersion(
                    draft.WorkflowDefinitionId,
                    WorkflowVersionNumbering.NextMajor(lastVersion?.Version))
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
            cancellationToken: cancellationToken);

        return outcome.Value.VersionId;
    }

    private sealed record PromoteDraftRequestMaterial(string DraftId);

    private sealed record PromoteDraftResult(
        string DraftId,
        string WorkflowDefinitionId,
        string VersionId,
        string Version);
}

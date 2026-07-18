using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
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
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkPromoteDraftToVersionCommand(
    IDistributedLockProvider lockProvider,
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    IInlineEventPublisher inlineEventPublisher,
    IWorkflowDefinitionVersionStore versionStore,
    IIdentityGenerator identityGenerator,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IPromoteDraftToVersionCommand
{
    public async Task<string> Execute(string draftId, CancellationToken cancellationToken = default)
    {
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

        var document = await documents.FindByIdAsync(draftId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition draft '{draftId}' not found");

        // FR-024 promotion gate: derive errors against the loaded Draft (see DraftValidationGate).
        // Runs inside the per-Draft lock, so the validated state is exactly the state promoted.
        var errors = await inlineEventPublisher.DeriveValidationErrorsAsync(document.Entity, cancellationToken);

        if (errors.Count > 0)
            throw new DraftHasValidationErrorsException(draftId, errors.Count);

        var draft = document.Entity;
        var lastVersion = await versionStore.FindLatestVersionAsync(draft.WorkflowDefinitionId, cancellationToken);
        var versionId = identityGenerator.Generate();
        var version = new WorkflowDefinitionVersion(draft.WorkflowDefinitionId, WorkflowVersionNumbering.NextMajor(lastVersion?.Version))
        {
            Id = versionId,
            State = draft.State,
            SourceDraftId = draft.Id
        };

        var versionLayout = new WorkflowDefinitionVersionLayout
        {
            Id = identityGenerator.Generate(),
            WorkflowDefinitionVersionId = versionId,
            Records = document.Layout.ToList()
        };

        var now = clock.UtcNow;
        GroundworkEntityTimestamps.StampAdded(version, now);
        GroundworkEntityTimestamps.StampAdded(versionLayout, now);

        await store.SaveAllAsync(
            DocumentCommitScope.Of(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind),
            [
                GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    version,
                    GroundworkDesignDocumentSerialization.Create(payloadSerializer),
                    accessContextAccessor.Current),
                GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    versionLayout,
                    GroundworkDesignJson.Options,
                    accessContextAccessor.Current)
            ],
            cancellationToken);

        return versionId;
    }
}

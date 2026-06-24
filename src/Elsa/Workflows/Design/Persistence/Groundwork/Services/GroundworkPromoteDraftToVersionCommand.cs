using Elsa.Locking.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkPromoteDraftToVersionCommand(
    IDistributedLockProvider lockProvider,
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    IWorkflowDefinitionVersionStore versionStore,
    IIdentityGenerator identityGenerator,
    ISystemClock clock)
    : IPromoteDraftToVersionCommand
{
    public async Task<string> Execute(string draftId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);

        var documents = new GroundworkWorkflowDefinitionDraftDocumentStore(store, GroundworkDesignDocumentSerialization.Create(payloadSerializer));
        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);

        await using var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken);

        var document = await documents.FindByIdAsync(draftId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition draft '{draftId}' not found");

        var errorCount = document.Errors.Count;
        if (errorCount > 0)
            throw new DraftHasValidationErrorsException(draftId, errorCount);

        var draft = document.Entity;
        var lastVersion = await versionStore.FindLatestVersionAsync(draft.WorkflowDefinitionId, cancellationToken);
        var versionId = identityGenerator.Generate();
        var version = new WorkflowDefinitionVersion(draft.WorkflowDefinitionId, NextVersion(lastVersion?.Version))
        {
            Id = versionId,
            State = draft.State
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
                GroundworkDocumentWriter.ToSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    version,
                    GroundworkDesignDocumentSerialization.Create(payloadSerializer)),
                GroundworkDocumentWriter.ToSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    versionLayout,
                    GroundworkDesignJson.Options)
            ],
            cancellationToken);

        return versionId;
    }

    private static string NextVersion(string? lastVersion) =>
        lastVersion is not null && SemVer.TryParse(lastVersion, out var semVer)
            ? $"{semVer.Major + 1}.0.0"
            : "1.0.0";
}

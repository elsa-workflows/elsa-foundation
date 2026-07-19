using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkSaveWorkflowDefinitionCommand(
    IDocumentStore store,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : ISaveWorkflowDefinitionCommand
{
    public async Task Execute(WorkflowDefinition definition, CancellationToken cancellationToken = default)
    {
        var access = accessContextAccessor.Current;
        access.EnsureTenantScope(definition.TenantId);
        var scopeKinds = new[]
        {
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind
        };

        try
        {
            await using var unit = await store.BeginAsync(DocumentCommitScope.Of(scopeKinds), cancellationToken);
            var existingEnvelope = await unit.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, definition.Id, cancellationToken);
            var existing = existingEnvelope is null ? null : ReadDefinition(existingEnvelope);
            if (existing is not null)
                access.EnsureTenantScope(existing.TenantId);
            GroundworkEntityTimestamps.StampSaved(definition, existing, clock.UtcNow);

            var folderIds = new[] { existing?.FolderId, definition.FolderId }
                .Where(folderId => folderId is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var folderFences = new List<(DocumentEnvelope Envelope, WorkflowFolder Folder)>();
            foreach (var folderId in folderIds)
                folderFences.Add(await LoadOwnedFolderAsync(unit, folderId, access, cancellationToken));

            var definitionResult = await unit.SaveAsync(GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                WorkflowsDesignStorageManifest.SchemaVersion,
                definition,
                GroundworkDesignJson.Options,
                access) with { ExpectedVersion = existingEnvelope?.Version ?? 0 }, cancellationToken);
            if (definitionResult.Status != DocumentStoreWriteStatus.Saved)
                throw new WorkflowFolderRestructureConflictException();

            foreach (var (envelope, folder) in folderFences)
            {
                var fence = await unit.SaveAsync(GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowFolderCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    folder,
                    GroundworkDesignJson.Options,
                    access) with { ExpectedVersion = envelope.Version }, cancellationToken);
                if (fence.Status != DocumentStoreWriteStatus.Saved)
                    throw new WorkflowFolderRestructureConflictException();
            }

            await unit.CommitAsync(cancellationToken);
        }
        catch (DocumentAtomicWriteException exception) when (exception.Status is
            DocumentStoreWriteStatus.ConcurrencyConflict or
            DocumentStoreWriteStatus.IdentityConflict or
            DocumentStoreWriteStatus.NotFound)
        {
            throw new WorkflowFolderRestructureConflictException(exception);
        }
    }

    private static WorkflowDefinition ReadDefinition(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<GroundworkDocument<WorkflowDefinition>>(envelope.ContentJson, GroundworkDesignJson.Options)?.Entity
        ?? throw new InvalidOperationException("The workflow-definition document is empty.");

    private static async Task<(DocumentEnvelope Envelope, WorkflowFolder Folder)> LoadOwnedFolderAsync(
        IDocumentUnitOfWork unit,
        string folderId,
        PersistenceAccessContext access,
        CancellationToken cancellationToken)
    {
        var envelope = await unit.LoadAsync(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind, folderId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folderId);
        var folder = JsonSerializer.Deserialize<GroundworkDocument<WorkflowFolder>>(envelope.ContentJson, GroundworkDesignJson.Options)?.Entity
            ?? throw new InvalidOperationException("The workflow-folder document is empty.");
        try { access.EnsureTenantScope(folder.TenantId); }
        catch (InvalidOperationException) { throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folderId); }
        return (envelope, folder);
    }
}

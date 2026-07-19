using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Elsa.Primitives.Exceptions;
using System.Text.Json;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IAddWorkflowDefinitionCommand"/>, the document-store
/// counterpart of the EF Core <c>AddWorkflowDefinition</c>. It stages the <c>workflowDefinition</c> and its first
/// embedded <c>workflowDefinitionDraft</c> into one Groundwork <see cref="IDocumentUnitOfWork"/> and commits them
/// together.
/// </summary>
public sealed class GroundworkAddWorkflowDefinitionCommand(
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IAddWorkflowDefinitionCommand
{
    public Task Execute(WorkflowDefinition workflowDefinition, WorkflowDefinitionDraft draft, CancellationToken cancellation) =>
        Execute(workflowDefinition, draft, [], cancellation);

    public async Task Execute(
        WorkflowDefinition workflowDefinition,
        WorkflowDefinitionDraft draft,
        IReadOnlyCollection<DesignMetadataRecord> layout,
        CancellationToken cancellation)
    {
        accessContextAccessor.Current.EnsureTenantScope(workflowDefinition.TenantId);
        accessContextAccessor.Current.EnsureTenantScope(draft.TenantId);

        var now = clock.UtcNow;
        GroundworkEntityTimestamps.StampAdded(workflowDefinition, now);
        GroundworkEntityTimestamps.StampAdded(draft, now);

        var definitionSave = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            workflowDefinition,
            GroundworkDesignJson.Options,
            accessContextAccessor.Current);

        var draftDocuments = new GroundworkWorkflowDefinitionDraftDocumentStore(
            store,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor);
        var draftSave = draftDocuments.ToSaveRequest(draft, layout.ToArray());

        var scopeKinds = workflowDefinition.FolderId is null
            ? new[]
            {
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind
            }
            : new[]
            {
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind
            };
        await using var unit = await store.BeginAsync(DocumentCommitScope.Of(scopeKinds), cancellation);
        if (workflowDefinition.FolderId is { } folderId)
        {
            var folderDocument = await unit.LoadAsync(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind, folderId, cancellation);
            if (folderDocument is null)
                throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folderId);
            var folder = JsonSerializer.Deserialize<GroundworkDocument<WorkflowFolder>>(
                folderDocument.ContentJson,
                GroundworkDesignJson.Options)?.Entity
                ?? throw new InvalidOperationException("The workflow-folder document is empty.");
            if (!string.Equals(folder.TenantId, workflowDefinition.TenantId, StringComparison.Ordinal))
                throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folderId);
        }

        var definitionResult = await unit.SaveAsync(definitionSave, cancellation);
        var draftResult = await unit.SaveAsync(draftSave, cancellation);
        if (definitionResult.Status != DocumentStoreWriteStatus.Saved || draftResult.Status != DocumentStoreWriteStatus.Saved)
            throw new InvalidOperationException("Workflow-definition creation could not be committed.");
        await unit.CommitAsync(cancellation);
    }
}

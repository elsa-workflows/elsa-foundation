using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Core;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkDeleteWorkflowDefinitionPermanentlyCommand(
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionDraftStore draftStore,
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionVersionLayoutStore layoutStore,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IEnumerable<IWorkflowDefinitionPermanentDeletionGuard>? deletionGuards = null,
    ILogger<GroundworkDeleteWorkflowDefinitionPermanentlyCommand>? logger = null)
    : IDeleteWorkflowDefinitionPermanentlyCommand
{
    public async Task Execute(string definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await definitionStore.FindByIdAsync(definitionId, cancellationToken)
            ?? throw new ArgumentException($"Workflow definition '{definitionId}' was not found.");
        accessContextAccessor.Current.EnsureTenantScope(definition.TenantId);
        if (definition.DeletedAt is null)
            throw new InvalidOperationException("A workflow definition must be soft-deleted before permanent deletion.");
        foreach (var guard in deletionGuards ?? [])
            await guard.EnsureCanDeleteAsync(definitionId, cancellationToken);

        var deletes = new List<DeleteDocumentRequest>();
        var drafts = await draftStore.ListByWorkflowDefinitionIdAsync(definitionId, cancellationToken);
        foreach (var draft in drafts)
            accessContextAccessor.Current.EnsureTenantScope(draft.TenantId);
        if (drafts.Count > 0)
        {
            var draftDocuments = new GroundworkWorkflowDefinitionDraftDocumentStore(
                store,
                GroundworkDesignDocumentSerialization.Create(payloadSerializer),
                accessContextAccessor);
            deletes.AddRange(drafts.Select(draft => draftDocuments.ToDeleteRequest(draft.Id)));
        }

        var versions = await versionStore.ListByDefinitionAsync(definitionId, cancellationToken);
        foreach (var version in versions)
        {
            accessContextAccessor.Current.EnsureTenantScope(version.TenantId);
            var layout = await layoutStore.FindByVersionIdAsync(version.Id, cancellationToken);
            if (layout is not null)
            {
                accessContextAccessor.Current.EnsureTenantScope(layout.TenantId);
                deletes.Add(GroundworkDocumentWriter.ToDeleteRequest(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
                    layout.Id));
            }

            deletes.Add(GroundworkDocumentWriter.ToDeleteRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                version.Id));
        }

        deletes.Add(GroundworkDocumentWriter.ToDeleteRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definitionId));

        logger?.LogInformation(
            "Permanently deleting workflow definition {DefinitionId} ({VersionCount} version(s), {DraftCount} draft(s)); soft-deleted at {DeletedAt} with reason {DeletedReason}",
            definitionId,
            versions.Count,
            drafts.Count,
            definition.DeletedAt,
            definition.DeletedReason);
        await store.DeleteAllAsync(
            DocumentCommitScope.Of(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind),
            deletes,
            cancellationToken);
    }
}

using Elsa.Persistence.Groundwork.Querying;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkDeleteWorkflowDefinitionPermanentlyCommand(
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionDraftStore draftStore,
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionVersionLayoutStore layoutStore)
    : IDeleteWorkflowDefinitionPermanentlyCommand
{
    public async Task Execute(string definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await definitionStore.FindByIdAsync(definitionId, cancellationToken)
            ?? throw new ArgumentException($"Workflow definition '{definitionId}' was not found.");
        if (definition.DeletedAt is null)
            throw new InvalidOperationException("A workflow definition must be soft-deleted before permanent deletion.");

        var deletes = new List<DeleteDocumentRequest>();
        var drafts = await draftStore.ListByWorkflowDefinitionIdAsync(definitionId, cancellationToken);
        if (drafts.Count > 0)
        {
            var draftDocuments = new GroundworkWorkflowDefinitionDraftDocumentStore(
                store,
                GroundworkDesignDocumentSerialization.Create(payloadSerializer));
            deletes.AddRange(drafts.Select(draft => draftDocuments.ToDeleteRequest(draft.Id)));
        }

        var versions = await versionStore.ListByDefinitionAsync(definitionId, cancellationToken);
        foreach (var version in versions)
        {
            var layout = await layoutStore.FindByVersionIdAsync(version.Id, cancellationToken);
            if (layout is not null)
            {
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

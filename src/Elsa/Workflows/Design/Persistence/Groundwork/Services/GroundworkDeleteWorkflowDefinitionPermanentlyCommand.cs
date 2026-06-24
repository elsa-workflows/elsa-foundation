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
    IWorkflowDefinitionDraftStore draftStore,
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionVersionLayoutStore layoutStore)
    : IDeleteWorkflowDefinitionPermanentlyCommand
{
    public async Task Execute(string definitionId, CancellationToken cancellationToken = default)
    {
        var deletes = new List<DeleteDocumentRequest>();
        var draft = await draftStore.FindByWorkflowDefinitionIdAsync(definitionId, cancellationToken);
        if (draft is not null)
        {
            var draftDocuments = new GroundworkWorkflowDefinitionDraftDocumentStore(
                store,
                GroundworkDesignDocumentSerialization.Create(payloadSerializer));
            deletes.Add(draftDocuments.ToDeleteRequest(draft.Id));
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

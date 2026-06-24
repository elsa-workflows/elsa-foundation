using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork (document) implementation of <see cref="IWorkflowDefinitionDraftStore"/>, the document-store
/// counterpart of <c>EFCoreWorkflowDefinitionDraftStore</c>. Like the version store, the draft is a rich
/// aggregate: its authored <c>WorkflowDefinitionState</c> is serialized via <see cref="IPayloadSerializer"/>
/// and the EF shadow / navigation members are excluded from the document.
/// </summary>
public sealed class GroundworkWorkflowDefinitionDraftStore : IWorkflowDefinitionDraftStore
{
    private readonly GroundworkReadStore<WorkflowDefinitionDraft> _reads;

    public GroundworkWorkflowDefinitionDraftStore(IDocumentStore store, IPayloadSerializer payloadSerializer)
    {
        _reads = new GroundworkReadStore<WorkflowDefinitionDraft>(
            store,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            WorkflowsDesignStorageManifest.ByCollectionIndex,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer));
    }

    public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
        => _reads.FirstOrDefaultAsync(
            Query<WorkflowDefinitionDraft>.Where(x => x.WorkflowDefinitionId, QueryOp.Equal, workflowDefinitionId),
            cancellationToken);
}

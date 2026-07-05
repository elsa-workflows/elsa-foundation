using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
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
    private readonly GroundworkWorkflowDefinitionDraftDocumentStore _documents;

    public GroundworkWorkflowDefinitionDraftStore(IDocumentStore store, IPayloadSerializer payloadSerializer)
    {
        _documents = new GroundworkWorkflowDefinitionDraftDocumentStore(store, GroundworkDesignDocumentSerialization.Create(payloadSerializer));
    }

    public async Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        var document = await _documents.FindByIdAsync(draftId, cancellationToken);
        return document?.Entity;
    }

    public async Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var document = await _documents.FindByWorkflowDefinitionIdAsync(workflowDefinitionId, cancellationToken);
        return document?.Entity;
    }

    public async Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var documents = await _documents.ListByWorkflowDefinitionIdAsync(workflowDefinitionId, cancellationToken);
        return documents.Select(x => x.Entity).ToArray();
    }

    public async Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        var document = await _documents.FindByIdAsync(draftId, cancellationToken);
        return document?.Layout.ToArray() ?? [];
    }

    public async Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        // One document load feeds both the draft entity and its layout — the document already carries
        // both, so this avoids re-loading and re-deserializing the same draft document a second time.
        var document = await _documents.FindByIdAsync(draftId, cancellationToken);
        return document is null ? null : new DraftWithLayout(document.Entity, document.Layout.ToArray());
    }
}

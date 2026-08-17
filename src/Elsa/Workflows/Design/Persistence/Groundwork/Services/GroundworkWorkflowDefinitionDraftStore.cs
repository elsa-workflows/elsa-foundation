using Elsa.Persistence.Core;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkWorkflowDefinitionDraftStore(
    GroundworkDesignStorage storage,
    IPayloadSerializer payloadSerializer,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowDefinitionDraftStore
{
    private readonly GroundworkWorkflowDefinitionDraftDocumentStore documents =
        new(storage, GroundworkDesignDocumentSerialization.Create(payloadSerializer), accessContextAccessor);

    public async Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
        (await documents.FindByIdAsync(draftId, cancellationToken))?.Entity;

    public async Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken = default) =>
        (await documents.FindByWorkflowDefinitionIdAsync(workflowDefinitionId, cancellationToken))?.Entity;

    public async Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken = default) =>
        (await documents.ListByWorkflowDefinitionIdAsync(workflowDefinitionId, cancellationToken))
        .Select(x => x.Entity)
        .ToArray();

    public async Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(
        string draftId,
        CancellationToken cancellationToken = default) =>
        (await documents.FindByIdAsync(draftId, cancellationToken))?.Layout.ToArray() ?? [];

    public async Task<IReadOnlyCollection<ActivityPresentationRecord>> FindActivityPresentationByDraftIdAsync(
        string draftId,
        CancellationToken cancellationToken = default) =>
        (await documents.FindByIdAsync(draftId, cancellationToken))?.ActivityPresentation.ToArray() ?? [];

    public async Task<DraftWithLayout?> FindWithLayoutByIdAsync(
        string draftId,
        CancellationToken cancellationToken = default)
    {
        var document = await documents.FindByIdAsync(draftId, cancellationToken);
        return document is null
            ? null
            : new DraftWithLayout(document.Entity, document.Layout.ToArray(), document.ActivityPresentation.ToArray());
    }
}

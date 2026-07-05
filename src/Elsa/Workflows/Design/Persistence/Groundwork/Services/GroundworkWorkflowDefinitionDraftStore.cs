using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
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
    private readonly IEventPublisher _eventPublisher;

    public GroundworkWorkflowDefinitionDraftStore(IDocumentStore store, IPayloadSerializer payloadSerializer, IEventPublisher eventPublisher)
    {
        _documents = new GroundworkWorkflowDefinitionDraftDocumentStore(store, GroundworkDesignDocumentSerialization.Create(payloadSerializer));
        _eventPublisher = eventPublisher;
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

    public async Task<IReadOnlyCollection<ValidationError>> FindValidationErrorsByDraftIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        // Validation errors are derived state, not persisted. Load the Draft and re-run the validators
        // via the OnDraftValidating gate; the ExecuteValidations handler aggregates every IDraftValidator's
        // errors onto the event, which we read back after the Sequential chain completes.
        var document = await _documents.FindByIdAsync(draftId, cancellationToken);
        if (document is null)
            return [];

        var validatingEvent = new OnDraftValidating(document.Entity);
        await _eventPublisher.Publish(validatingEvent, EventPublishingStrategy.Sequential, cancellationToken);

        return validatingEvent.Errors.ToArray();
    }
}

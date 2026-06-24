using System.Text.Json;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

internal sealed record GroundworkWorkflowDefinitionDraftDocument(
    string Collection,
    WorkflowDefinitionDraft Entity,
    IReadOnlyCollection<DesignMetadataRecord> Layout,
    IReadOnlyCollection<ValidationError> Errors);

internal sealed class GroundworkWorkflowDefinitionDraftDocumentStore(
    IDocumentStore store,
    JsonSerializerOptions jsonOptions)
{
    public async Task<GroundworkWorkflowDefinitionDraftDocument?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        var envelope = await store.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, draftId, cancellationToken);
        return envelope is null ? null : Deserialize(envelope);
    }

    public async Task<GroundworkWorkflowDefinitionDraftDocument?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
        => CurrentDraft(await ListByWorkflowDefinitionIdAsync(workflowDefinitionId, cancellationToken));

    public async Task<IReadOnlyList<GroundworkWorkflowDefinitionDraftDocument>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                WorkflowsDesignStorageManifest.ByCollectionIndex,
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection),
            cancellationToken);

        return envelopes
            .Select(Deserialize)
            .Where(document => document.Entity.WorkflowDefinitionId == workflowDefinitionId)
            .ToList();
    }

    public SaveDocumentRequest ToSaveRequest(
        WorkflowDefinitionDraft draft,
        IReadOnlyCollection<DesignMetadataRecord> layout,
        IReadOnlyCollection<ValidationError> errors) =>
        JsonDocumentStoreExtensions.ToSaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            draft.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            new GroundworkWorkflowDefinitionDraftDocument(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
                draft,
                layout,
                errors),
            jsonOptions);

    public DeleteDocumentRequest ToDeleteRequest(string draftId) =>
        GroundworkDocumentWriter.ToDeleteRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            draftId);

    private GroundworkWorkflowDefinitionDraftDocument Deserialize(DocumentEnvelope envelope)
    {
        var document = JsonSerializer.Deserialize<GroundworkWorkflowDefinitionDraftDocument>(envelope.ContentJson, jsonOptions);
        if (document?.Entity is not null)
            return document with
            {
                Layout = document.Layout ?? [],
                Errors = document.Errors ?? []
            };

        var legacyDocument = JsonSerializer.Deserialize<GroundworkDocument<WorkflowDefinitionDraft>>(envelope.ContentJson, jsonOptions);
        if (legacyDocument?.Entity is null)
            throw new InvalidOperationException($"Document '{envelope.Id}' of kind '{WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind}' could not be deserialized as {nameof(WorkflowDefinitionDraft)}.");

        return new GroundworkWorkflowDefinitionDraftDocument(legacyDocument.Collection, legacyDocument.Entity, [], []);
    }

    private static GroundworkWorkflowDefinitionDraftDocument? CurrentDraft(IReadOnlyCollection<GroundworkWorkflowDefinitionDraftDocument> drafts) =>
        drafts
            .OrderByDescending(x => x.Entity.LastModifiedAt)
            .ThenByDescending(x => x.Entity.CreatedAt)
            .ThenByDescending(x => x.Entity.Id, StringComparer.Ordinal)
            .FirstOrDefault();
}

using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

// Validation errors are deliberately absent: they are derived state, recomputed by the
// validation gate on every mutation and by the promotion gate on demand. Documents written
// before this change may still carry an "Errors" property; the serializer ignores it.
internal sealed record GroundworkWorkflowDefinitionDraftDocument(
    string Collection,
    WorkflowDefinitionDraft Entity,
    IReadOnlyCollection<DesignMetadataRecord> Layout);

internal sealed class GroundworkWorkflowDefinitionDraftDocumentStore(
    IDocumentStore store,
    JsonSerializerOptions jsonOptions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IBoundedDocumentStore? boundedStore = null)
{
    public async Task<GroundworkWorkflowDefinitionDraftDocument?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default)
    {
        var envelope = await store.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, draftId, cancellationToken);
        return envelope is null ? null : Deserialize(envelope);
    }

    public async Task<GroundworkWorkflowDefinitionDraftDocument?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var envelope = await BoundedStore().FirstOrDefaultAsync(
            new DocumentQuery(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                WorkflowsDesignStorageManifest.FindCurrentDraftByDefinitionQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    WorkflowsDesignStorageManifest.DraftDefinitionIdField,
                    workflowDefinitionId))],
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftOrder,
                resultOperation: BoundedQueryResultOperation.First),
            cancellationToken);
        return envelope is null ? null : Deserialize(envelope);
    }

    public async Task<IReadOnlyList<GroundworkWorkflowDefinitionDraftDocument>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
        => await ListByWorkflowDefinitionIdsAsync([workflowDefinitionId], cancellationToken);

    public async Task<IReadOnlyList<GroundworkWorkflowDefinitionDraftDocument>> ListByWorkflowDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        var definitionIds = workflowDefinitionIds.Distinct(StringComparer.Ordinal).ToArray();
        if (definitionIds.Length == 0)
            return [];

        var comparison = definitionIds.Length == 1
            ? DocumentQueryComparison.Equal(WorkflowsDesignStorageManifest.DraftDefinitionIdField, definitionIds[0])
            : DocumentQueryComparison.In(WorkflowsDesignStorageManifest.DraftDefinitionIdField, definitionIds);
        var documents = await BoundedDocumentQueryPager.QueryAllOffsetAsync(
            BoundedStore(),
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            WorkflowsDesignStorageManifest.ListDraftsByDefinitionQuery,
            [DocumentQueryClause.Of(comparison)],
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftOrder,
            cancellationToken);

        return documents
            .Select(Deserialize)
            .ToList();
    }

    public SaveDocumentRequest ToSaveRequest(
        WorkflowDefinitionDraft draft,
        IReadOnlyCollection<DesignMetadataRecord> layout)
    {
        accessContextAccessor.Current.EnsureTenantScope(draft.TenantId);
        return JsonDocumentStoreExtensions.ToSaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            draft.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            new GroundworkWorkflowDefinitionDraftDocument(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
                draft,
                layout),
            jsonOptions);
    }

    public DeleteDocumentRequest ToDeleteRequest(string draftId) =>
        GroundworkDocumentWriter.ToDeleteRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            draftId);

    private GroundworkWorkflowDefinitionDraftDocument Deserialize(DocumentEnvelope envelope)
    {
        var document = JsonSerializer.Deserialize<GroundworkWorkflowDefinitionDraftDocument>(envelope.ContentJson, jsonOptions);
        if (document?.Entity is not null)
        {
            accessContextAccessor.Current.EnsureTenantScope(document.Entity.TenantId);
            return document with
            {
                Layout = document.Layout ?? []
            };
        }

        var legacyDocument = JsonSerializer.Deserialize<GroundworkDocument<WorkflowDefinitionDraft>>(envelope.ContentJson, jsonOptions);
        if (legacyDocument?.Entity is null)
            throw new InvalidOperationException($"Document '{envelope.Id}' of kind '{WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind}' could not be deserialized as {nameof(WorkflowDefinitionDraft)}.");

        accessContextAccessor.Current.EnsureTenantScope(legacyDocument.Entity.TenantId);
        return new GroundworkWorkflowDefinitionDraftDocument(legacyDocument.Collection, legacyDocument.Entity, []);
    }

    private IBoundedDocumentStore BoundedStore() =>
        boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException(
            "Workflow-definition draft queries require an admitted bounded document-store runtime.");
}

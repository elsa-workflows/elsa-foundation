using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
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
        DocumentEnvelope? envelope;
        try
        {
            envelope = await store.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, draftId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw ProviderFailure(exception);
        }
        return envelope is null ? null : Deserialize(envelope);
    }

    public async Task<GroundworkWorkflowDefinitionDraftDocument?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var reader = BoundedStore();
        DocumentEnvelope? envelope;
        try
        {
            envelope = await reader.FirstOrDefaultAsync(
                new DocumentQuery(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                    WorkflowsDesignStorageManifest.FindCurrentDraftByDefinitionQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                        WorkflowsDesignStorageManifest.DraftDefinitionIdField,
                        workflowDefinitionId))],
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDraftOrder,
                    resultOperation: BoundedQueryResultOperation.First),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw ProviderFailure(exception);
        }
        return envelope is null ? null : Deserialize(envelope);
    }

    public async Task<IReadOnlyList<GroundworkWorkflowDefinitionDraftDocument>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
        => await ListByWorkflowDefinitionIdsAsync([workflowDefinitionId], cancellationToken);

    public async Task<IReadOnlyList<GroundworkWorkflowDefinitionDraftDocument>> ListByWorkflowDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        var batches = GroundworkMembershipBatches.Create(workflowDefinitionIds);
        if (batches.Count == 0)
            return [];

        var reader = BoundedStore();
        var drafts = new List<GroundworkWorkflowDefinitionDraftDocument>();
        foreach (var batch in batches)
        {
            var comparison = batch.Length == 1
                ? DocumentQueryComparison.Equal(WorkflowsDesignStorageManifest.DraftDefinitionIdField, batch[0])
                : DocumentQueryComparison.In(WorkflowsDesignStorageManifest.DraftDefinitionIdField, batch);
            IReadOnlyList<DocumentEnvelope> documents;
            try
            {
                documents = await BoundedDocumentQueryPager.QueryAllOffsetAsync(
                    reader,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                    WorkflowsDesignStorageManifest.ListDraftsByDefinitionQuery,
                    [DocumentQueryClause.Of(comparison)],
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDraftOrder,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw ProviderFailure(exception);
            }

            drafts.AddRange(documents.Select(Deserialize));
        }

        return drafts;
    }

    public SaveDocumentRequest ToSaveRequest(
        WorkflowDefinitionDraft draft,
        IReadOnlyCollection<DesignMetadataRecord> layout)
    {
        accessContextAccessor.Current.EnsureTenantScope(draft.TenantId);
        return GroundworkDesignSerialization.Execute(
            DesignPersistenceDomain.Workflow,
            "save",
            "workflow definition draft",
            () => JsonDocumentStoreExtensions.ToSaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                draft.Id,
                WorkflowsDesignStorageManifest.SchemaVersion,
                new GroundworkWorkflowDefinitionDraftDocument(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
                    draft,
                    layout),
                jsonOptions));
    }

    public DeleteDocumentRequest ToDeleteRequest(string draftId) =>
        GroundworkDocumentWriter.ToDeleteRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            draftId);

    private GroundworkWorkflowDefinitionDraftDocument Deserialize(DocumentEnvelope envelope)
    {
        try
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
                throw SerializationFailure(new InvalidDataException(
                    $"Document '{envelope.Id}' of kind '{WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind}' could not be deserialized as {nameof(WorkflowDefinitionDraft)}."));

            accessContextAccessor.Current.EnsureTenantScope(legacyDocument.Entity.TenantId);
            return new GroundworkWorkflowDefinitionDraftDocument(legacyDocument.Collection, legacyDocument.Entity, []);
        }
        catch (DesignPersistenceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw SerializationFailure(exception);
        }
    }

    private IBoundedDocumentStore BoundedStore() =>
        boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException(
            "Workflow-definition draft queries require an admitted bounded document-store runtime.");

    private static DesignPersistenceException ProviderFailure(Exception exception) =>
        new(
            DesignPersistenceDomain.Workflow,
            DesignPersistenceFailureKind.Provider,
            "read",
            "workflow definition draft",
            exception);

    private static DesignPersistenceException SerializationFailure(Exception exception) =>
        new(
            DesignPersistenceDomain.Workflow,
            DesignPersistenceFailureKind.Serialization,
            "read",
            "workflow definition draft",
            exception);
}

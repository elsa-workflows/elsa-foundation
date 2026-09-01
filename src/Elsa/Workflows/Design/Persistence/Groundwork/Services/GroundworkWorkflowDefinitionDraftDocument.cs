using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

// Validation errors are derived state and are intentionally not persisted. The JSON document retains
// the aggregate plus its design metadata while the public-v2 row keeps query projections as columns.
internal sealed record GroundworkWorkflowDefinitionDraftDocument(
    string Collection,
    WorkflowDefinitionDraft Entity,
    IReadOnlyCollection<DesignMetadataRecord> Layout,
    long? Version = null)
{
    public IReadOnlyCollection<ActivityPresentationRecord> ActivityPresentation { get; init; } = [];
}

internal sealed class GroundworkWorkflowDefinitionDraftDocumentStore(
    GroundworkDesignStorage storage,
    JsonSerializerOptions jsonOptions,
    IPersistenceAccessContextAccessor accessContextAccessor)
{
    private readonly string unit = WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind;

    public Task<GroundworkWorkflowDefinitionDraftDocument?> FindByIdAsync(
        string draftId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = storage.Read(unit, draftId);
        return Task.FromResult(entry is null ? null : Deserialize(entry));
    }

    public Task<GroundworkWorkflowDefinitionDraftDocument?> FindByWorkflowDefinitionIdAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = storage.Query(
            unit,
            storage.Equal(unit, WorkflowsDesignStorageManifest.DraftDefinitionIdField, workflowDefinitionId),
            [
                storage.Order(unit, WorkflowsDesignStorageManifest.DraftLastModifiedAtField, descending: true),
                storage.Order(unit, WorkflowsDesignStorageManifest.DraftCreatedAtField, descending: true),
                storage.Order(unit, WorkflowsDesignStorageManifest.DraftIdField, descending: true)
            ],
            WorkflowsDesignStorageManifest.DraftByDefinitionIndex,
            cancellationToken: cancellationToken);
        return Task.FromResult(rows.Select(Deserialize).FirstOrDefault());
    }

    public Task<IReadOnlyList<GroundworkWorkflowDefinitionDraftDocument>> ListByWorkflowDefinitionIdAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken = default) =>
        ListByWorkflowDefinitionIdsAsync([workflowDefinitionId], cancellationToken);

    public Task<IReadOnlyList<GroundworkWorkflowDefinitionDraftDocument>> ListByWorkflowDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (workflowDefinitionIds.Count == 0)
            return Task.FromResult<IReadOnlyList<GroundworkWorkflowDefinitionDraftDocument>>([]);

        var ids = workflowDefinitionIds.Distinct(StringComparer.Ordinal).ToArray();
        var rows = storage.Query(
            unit,
            storage.In(unit, WorkflowsDesignStorageManifest.DraftDefinitionIdField, ids.Cast<object?>()),
            [
                storage.Order(unit, WorkflowsDesignStorageManifest.DraftDefinitionIdField),
                storage.Order(unit, WorkflowsDesignStorageManifest.DraftLastModifiedAtField, descending: true),
                storage.Order(unit, WorkflowsDesignStorageManifest.DraftCreatedAtField, descending: true),
                storage.Order(unit, WorkflowsDesignStorageManifest.DraftIdField, descending: true)
            ],
            WorkflowsDesignStorageManifest.DraftByDefinitionIndex,
            cancellationToken: cancellationToken);
        return Task.FromResult<IReadOnlyList<GroundworkWorkflowDefinitionDraftDocument>>(
            rows.Select(Deserialize).ToArray());
    }

    public GroundworkDesignSaveRequest ToSaveRequest(
        WorkflowDefinitionDraft draft,
        IReadOnlyCollection<DesignMetadataRecord> layout,
        IReadOnlyCollection<ActivityPresentationRecord>? activityPresentation = null,
        long? expectedVersion = null)
    {
        accessContextAccessor.Current.EnsureTenantScope(draft.TenantId);
        var normalizedPresentation = ActivityPresentationRecord.NormalizeCollection(activityPresentation ?? []);
        var values = GroundworkDesignStorage.Values(
            unit,
            draft,
            jsonOptions,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
            layout,
            normalizedPresentation);
        return new GroundworkDesignSaveRequest(unit, values, expectedVersion);
    }

    public GroundworkDesignDeleteRequest ToDeleteRequest(string draftId, long? expectedVersion = null) =>
        new(unit, draftId, expectedVersion);

    private GroundworkWorkflowDefinitionDraftDocument Deserialize(GroundworkDesignEntry entry)
    {
        var document = GroundworkDesignStorage.DeserializeDocument<WorkflowDefinitionDraft>(entry.Entry, jsonOptions);
        accessContextAccessor.Current.EnsureTenantScope(document.Entity.TenantId);
        return new GroundworkWorkflowDefinitionDraftDocument(
            document.Collection,
            document.Entity,
            document.Layout ?? [],
            entry.Entry.Version)
        {
            ActivityPresentation = ActivityPresentationRecord.NormalizeCollection(document.ActivityPresentation ?? [])
        };
    }
}

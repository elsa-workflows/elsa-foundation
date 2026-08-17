using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Resolves list projections from bounded, indexed v2 reads of the draft and version units.
/// </summary>
public sealed class GroundworkWorkflowDefinitionListProjectionStore(
    IGroundworkStorageSessionSource sessions,
    IPayloadSerializer payloadSerializer,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName = null) : IWorkflowDefinitionListProjectionStore
{
    private readonly GroundworkDesignStorage storage = new(sessions, accessContextAccessor, targetName);
    private readonly System.Text.Json.JsonSerializerOptions json =
        GroundworkDesignDocumentSerialization.Create(payloadSerializer);

    public async Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        var ids = workflowDefinitionIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
            return [];

        var draftRows = new List<GroundworkDesignEntry>();
        var versionRows = new List<GroundworkDesignEntry>();
        foreach (var batch in ids.Chunk(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            draftRows.AddRange(storage.Query(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                storage.In(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                    WorkflowsDesignStorageManifest.DraftDefinitionIdField,
                    batch.Cast<object?>()),
                [
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, WorkflowsDesignStorageManifest.DraftDefinitionIdField),
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, WorkflowsDesignStorageManifest.DraftLastModifiedAtField, descending: true),
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, WorkflowsDesignStorageManifest.DraftCreatedAtField, descending: true),
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, WorkflowsDesignStorageManifest.DraftIdField, descending: true)
                ],
                WorkflowsDesignStorageManifest.DraftByDefinitionIndex,
                cancellationToken: cancellationToken));
            versionRows.AddRange(storage.Query(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                storage.In(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                    WorkflowsDesignStorageManifest.VersionDefinitionIdField,
                    batch.Cast<object?>()),
                [
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind, WorkflowsDesignStorageManifest.VersionDefinitionIdField),
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind, WorkflowsDesignStorageManifest.VersionSemVerSortKeyField),
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind, WorkflowsDesignStorageManifest.VersionIdField)
                ],
                WorkflowsDesignStorageManifest.VersionByDefinitionIndex,
                cancellationToken: cancellationToken));
        }

        var drafts = draftRows
            .Select(row => GroundworkDesignStorage.DeserializeDocument<WorkflowDefinitionDraft>(row.Entry, json))
            .GroupBy(document => document.Entity.WorkflowDefinitionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.Entity.LastModifiedAt)
                    .ThenByDescending(x => x.Entity.CreatedAt)
                    .ThenByDescending(x => x.Entity.Id, StringComparer.Ordinal)
                    .First().Entity,
                StringComparer.Ordinal);
        var versions = versionRows
            .Select(row => GroundworkDesignStorage.Deserialize<WorkflowDefinitionVersion>(row.Entry, json))
            .GroupBy(version => version.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.SemVerSortKey, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return ids.Select(definitionId =>
        {
            drafts.TryGetValue(definitionId, out var draft);
            versions.TryGetValue(definitionId, out var definitionVersions);
            var latest = definitionVersions?.FirstOrDefault();
            return new WorkflowDefinitionListProjection(
                definitionId,
                draft?.Id,
                latest?.Id,
                latest?.Version,
                definitionVersions?.Length ?? 0);
        }).ToArray();
    }
}

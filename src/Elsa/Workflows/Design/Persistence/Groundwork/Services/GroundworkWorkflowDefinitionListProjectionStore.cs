using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
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
    string? targetName = null,
    IGroundworkPrivilegedQueryAuditSink? auditSink = null) : IWorkflowDefinitionListProjectionStore
{
    private readonly GroundworkDesignStorage storage = new(sessions, accessContextAccessor, targetName, auditSink);
    private readonly System.Text.Json.JsonSerializerOptions json =
        GroundworkDesignDocumentSerialization.Create(payloadSerializer);

    public async Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        var ids = workflowDefinitionIds
            .GroupBy(WorkflowDefinitionIdentity.Fold, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (ids.Length == 0)
            return [];

        var draftRows = new List<GroundworkDesignEntry>();
        var versionRows = new List<GroundworkDesignEntry>();
        // Groundwork normalizes an IN predicate into an OR expression. Keep each
        // provider request within the query normalizer's 16-disjunct budget.
        foreach (var batch in ids.Chunk(16))
        {
            cancellationToken.ThrowIfCancellationRequested();
            draftRows.AddRange(storage.Query(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                storage.In(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
                    WorkflowsDesignStorageManifest.DraftDefinitionIdField,
                    batch.Cast<object?>()),
                [
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, WorkflowsDesignStorageManifest.DraftDefinitionIdLookupHashField),
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, WorkflowsDesignStorageManifest.DraftLastModifiedAtField, descending: true),
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, WorkflowsDesignStorageManifest.DraftCreatedAtField, descending: true),
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, WorkflowsDesignStorageManifest.DraftIdField, descending: true)
                ],
                WorkflowsDesignStorageManifest.DraftByDefinitionIndex,
                acrossScopes: accessContextAccessor.Current.AcrossScopes,
                cancellationToken: cancellationToken));
            versionRows.AddRange(storage.Query(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                storage.In(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
                    WorkflowsDesignStorageManifest.VersionDefinitionIdField,
                    batch.Cast<object?>()),
                [
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind, WorkflowsDesignStorageManifest.VersionDefinitionIdLookupHashField),
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind, WorkflowsDesignStorageManifest.VersionSemVerSortKeyField),
                    storage.Order(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind, WorkflowsDesignStorageManifest.VersionIdField)
                ],
                WorkflowsDesignStorageManifest.VersionByDefinitionIndex,
                acrossScopes: accessContextAccessor.Current.AcrossScopes,
                cancellationToken: cancellationToken));
        }

        var draftDocuments = draftRows
            .Select(row => (Row: row, Document: GroundworkDesignStorage.DeserializeDocument<WorkflowDefinitionDraft>(row.Entry, json)))
            .ToArray();
        foreach (var candidate in draftDocuments)
        {
            var document = candidate.Document;
            GroundworkDesignStorage.EnsureDefinitionIdentityInSet(
                ids,
                document.Entity.WorkflowDefinitionId,
                "workflow draft projection lookup");
            GroundworkDesignStorage.EnsureProjectedIdentity(
                candidate.Row,
                document.Entity,
                "workflow draft projection lookup",
                requireCrossScopeProvenance: accessContextAccessor.Current.AcrossScopes);
        }
        var drafts = draftDocuments
            .GroupBy(document => WorkflowDefinitionIdentity.Fold(document.Document.Entity.WorkflowDefinitionId), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.Document.Entity.LastModifiedAt)
                    .ThenByDescending(x => x.Document.Entity.CreatedAt)
                    .ThenByDescending(x => x.Document.Entity.Id, StringComparer.Ordinal)
                    .First().Document.Entity,
                StringComparer.Ordinal);
        var versionEntities = versionRows
            .Select(row => (Row: row, Entity: GroundworkDesignStorage.Deserialize<WorkflowDefinitionVersion>(row.Entry, json)))
            .ToArray();
        foreach (var candidate in versionEntities)
        {
            var version = candidate.Entity;
            GroundworkDesignStorage.EnsureDefinitionIdentityInSet(
                ids,
                version.DefinitionId,
                "workflow version projection lookup");
            GroundworkDesignStorage.EnsureProjectedIdentity(
                candidate.Row,
                version,
                "workflow version projection lookup",
                requireCrossScopeProvenance: accessContextAccessor.Current.AcrossScopes);
        }
        EnsureSingleTenantScopePerDefinition(
            draftDocuments
                .Select(candidate => (candidate.Document.Entity.WorkflowDefinitionId, candidate.Document.Entity.TenantId))
                .Concat(versionEntities.Select(candidate => (candidate.Entity.DefinitionId, candidate.Entity.TenantId))),
            "workflow definition list projection lookup");

        var versions = versionEntities
            .GroupBy(version => WorkflowDefinitionIdentity.Fold(version.Entity.DefinitionId), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.Entity.SemVerSortKey, StringComparer.Ordinal).Select(x => x.Entity).ToArray(),
                StringComparer.Ordinal);

        return ids.Select(definitionId =>
        {
            var key = WorkflowDefinitionIdentity.Fold(definitionId);
            drafts.TryGetValue(key, out var draft);
            versions.TryGetValue(key, out var definitionVersions);
            var latest = definitionVersions?.FirstOrDefault();
            return new WorkflowDefinitionListProjection(
                definitionId,
                draft?.Id,
                latest?.Id,
                latest?.Version,
                definitionVersions?.Length ?? 0);
        }).ToArray();
    }

    private static void EnsureSingleTenantScopePerDefinition(
        IEnumerable<(string DefinitionId, string? TenantId)> candidates,
        string operation)
    {
        foreach (var group in candidates.GroupBy(candidate => WorkflowDefinitionIdentity.Fold(candidate.DefinitionId), StringComparer.Ordinal))
        {
            var scopes = group
                .Select(candidate => candidate.TenantId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (scopes.Length > 1)
                throw new GroundworkQueryReadinessException(
                    $"The {operation} returned definition identity '{group.Key}' from multiple authoritative tenant scopes.");
        }
    }
}

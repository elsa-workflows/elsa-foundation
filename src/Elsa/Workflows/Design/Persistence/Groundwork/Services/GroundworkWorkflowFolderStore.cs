using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork folder store. Creation validates ancestry and stages the opaque folder document in a
/// unit of work; the manifest's non-null parent-key/normalized-name unique index rejects sibling races.
/// </summary>
public sealed class GroundworkWorkflowFolderStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor,
    ISystemClock clock,
    IBoundedDocumentStore? boundedStore = null) : IWorkflowFolderStore
{
    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    public bool IsAvailable => _boundedStore is not null;

    public async Task<WorkflowFolderPage> ListDirectChildrenAsync(WorkflowFolderPageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        var parentKey = WorkflowFolder.ToParentKey(
            string.IsNullOrWhiteSpace(request.ParentFolderId) ? null : request.ParentFolderId);
        var bounded = _boundedStore
            ?? throw new InvalidOperationException("Workflow-folder browsing requires an admitted bounded document-store runtime.");
        DocumentQueryResult result;
        try
        {
            result = await bounded.QueryAsync(new DocumentQuery(
                WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind,
                WorkflowsDesignStorageManifest.PageWorkflowFoldersQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(WorkflowsDesignStorageManifest.WorkflowFolderParentKeyField, parentKey))],
                [
                    new DocumentQueryOrder(WorkflowsDesignStorageManifest.WorkflowFolderNormalizedNameField, PhysicalSortDirection.Ascending),
                    new DocumentQueryOrder("entity.id", PhysicalSortDirection.Ascending)
                ],
                take: request.PageSize,
                continuation: request.ContinuationToken), cancellationToken);
        }
        catch (InvalidDocumentQueryContinuationException exception)
        {
            throw new ArgumentException("The workflow-folder continuation token is invalid or does not belong to this parent.", nameof(request.ContinuationToken), exception);
        }
        return new WorkflowFolderPage(result.Documents.Select(ReadFolder).ToArray(), result.NextContinuation);
    }

    public async Task<WorkflowFolderDetails?> FindWithAncestorsAsync(string folderId, CancellationToken cancellationToken = default)
    {
        var details = await FindManyWithAncestorsAsync([folderId], cancellationToken);
        return details.GetValueOrDefault(folderId);
    }

    public async Task<IReadOnlyDictionary<string, WorkflowFolderDetails>> FindManyWithAncestorsAsync(
        IReadOnlyCollection<string> folderIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folderIds);
        var requestedIds = folderIds.Distinct(StringComparer.Ordinal).ToArray();
        var foldersById = new Dictionary<string, WorkflowFolder>(StringComparer.Ordinal);
        var missingIds = new HashSet<string>(StringComparer.Ordinal);
        var pendingIds = new Queue<string>(requestedIds);
        var access = accessContextAccessor.Current;
        while (pendingIds.TryDequeue(out var folderId))
        {
            if (foldersById.ContainsKey(folderId) || missingIds.Contains(folderId))
                continue;
            var envelope = await store.LoadAsync(
                WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind,
                folderId,
                cancellationToken);
            if (envelope is null)
            {
                missingIds.Add(folderId);
                continue;
            }
            var folder = ReadFolder(envelope);
            try
            {
                access.EnsureTenantScope(folder.TenantId);
            }
            catch (InvalidOperationException)
            {
                missingIds.Add(folderId);
                continue;
            }
            foldersById.Add(folderId, folder);
            if (folder.ParentFolderId is not null)
                pendingIds.Enqueue(folder.ParentFolderId);
        }

        var result = new Dictionary<string, WorkflowFolderDetails>(StringComparer.Ordinal);
        foreach (var requestedId in requestedIds)
        {
            if (!foldersById.TryGetValue(requestedId, out var folder))
                continue;
            var ancestors = new List<WorkflowFolder>();
            var parentId = folder.ParentFolderId;
            while (parentId is not null)
            {
                if (!foldersById.TryGetValue(parentId, out var parent))
                    throw new InvalidOperationException("Workflow-folder ancestry is inconsistent.");
                ancestors.Add(parent);
                parentId = parent.ParentFolderId;
                if (ancestors.Count > WorkflowFolderNames.MaximumDepth)
                    throw new InvalidOperationException("Workflow-folder ancestry exceeds the supported depth.");
            }
            ancestors.Reverse();
            result.Add(requestedId, new WorkflowFolderDetails(folder, ancestors));
        }
        return result;
    }

    public async Task<WorkflowFolder> CreateAsync(WorkflowFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        WorkflowFolderNames.ValidateIdentifier(folder.Id, nameof(folder.Id));
        if (!string.IsNullOrWhiteSpace(folder.ParentFolderId))
            WorkflowFolderNames.ValidateIdentifier(folder.ParentFolderId, nameof(folder.ParentFolderId));
        var access = accessContextAccessor.Current;
        if (access.Scope is null)
            throw new InvalidOperationException("Workflow folders require a tenant-scoped persistence context.");
        access.EnsureTenantScope(folder.TenantId);
        folder.TenantId ??= access.Scope.Value;
        folder.ParentFolderId = string.IsNullOrWhiteSpace(folder.ParentFolderId) ? null : folder.ParentFolderId;
        folder.ParentKey = WorkflowFolder.ToParentKey(folder.ParentFolderId);
        try
        {
            await using var unit = await store.BeginAsync(
                DocumentCommitScope.Of(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind),
                cancellationToken);
            var parentFences = await ValidateParentAsync(unit, folder, cancellationToken);
            GroundworkEntityTimestamps.StampAdded(folder, clock.UtcNow);
            var save = GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind,
                WorkflowsDesignStorageManifest.WorkflowFolderCollection,
                WorkflowsDesignStorageManifest.SchemaVersion,
                folder,
                GroundworkDesignJson.Options,
                access) with
            {
                ExpectedVersion = 0
            };
            var result = await unit.SaveAsync(save, cancellationToken);
            if (result.Status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.IdentityConflict)
                throw new WorkflowFolderSiblingConflictException();
            if (result.Status != DocumentStoreWriteStatus.Saved)
                throw new InvalidOperationException($"Workflow-folder create failed with write status '{result.Status}'.");
            // A parent is a structural CAS anchor.  Touching it makes a concurrent empty-delete or
            // hierarchy rewrite retry instead of committing a child beneath a removed/stale node.
            foreach (var (envelope, parent) in parentFences)
            {
                var fence = await unit.SaveAsync(GroundworkDocumentWriter.ToTenantScopedSaveRequest(
                    WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowFolderCollection,
                    WorkflowsDesignStorageManifest.SchemaVersion,
                    parent,
                    GroundworkDesignJson.Options,
                    access) with
                {
                    ExpectedVersion = envelope.Version
                }, cancellationToken);
                if (fence.Status != DocumentStoreWriteStatus.Saved)
                    throw new WorkflowFolderSiblingConflictException();
            }
            await unit.CommitAsync(cancellationToken);
            return folder;
        }
        catch (DocumentAtomicWriteException exception) when (exception.Status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.IdentityConflict or DocumentStoreWriteStatus.NotFound)
        {
            throw new WorkflowFolderSiblingConflictException(exception);
        }
    }

    private static async Task<IReadOnlyList<(DocumentEnvelope Envelope, WorkflowFolder Folder)>> ValidateParentAsync(IDocumentUnitOfWork unit, WorkflowFolder folder, CancellationToken cancellationToken)
    {
        var parentId = folder.ParentFolderId;
        var depth = 1;
        var fences = new List<(DocumentEnvelope Envelope, WorkflowFolder Folder)>();
        while (parentId is not null)
        {
            var envelope = await unit.LoadAsync(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind, parentId, cancellationToken)
                ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), parentId);
            var parent = ReadFolder(envelope);
            if (!string.Equals(parent.TenantId, folder.TenantId, StringComparison.Ordinal))
                throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), parentId);
            fences.Add((envelope, parent));
            parentId = parent.ParentFolderId;
            if (++depth > WorkflowFolderNames.MaximumDepth)
                throw new ArgumentOutOfRangeException(nameof(folder.ParentFolderId), "Workflow-folder depth cannot exceed 16.");
        }
        return fences;
    }

    private static WorkflowFolder ReadFolder(DocumentEnvelope envelope)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<GroundworkDocument<WorkflowFolder>>(envelope.ContentJson, GroundworkDesignJson.Options)?.Entity
                ?? throw new System.Text.Json.JsonException("The workflow-folder document is empty.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new WorkflowFolderDocumentDeserializationException(envelope.Id, exception);
        }
    }
}

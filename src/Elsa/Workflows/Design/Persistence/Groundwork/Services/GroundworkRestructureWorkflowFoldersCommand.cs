using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Groundwork.Core.PhysicalStorage;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

/// <summary>
/// Folder structural mutations use optimistic saves of every node that makes the decision safe.  Those
/// otherwise unchanged documents are deliberate CAS fences: concurrent child creation, placement and
/// hierarchy changes cannot make a preflight result stale between validation and commit.
/// </summary>
public sealed class GroundworkRestructureWorkflowFoldersCommand(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor,
    ISystemClock clock,
    IBoundedDocumentStore? boundedStore = null) : IRestructureWorkflowFoldersCommand
{
    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    public async Task<WorkflowFolder> RenameAsync(string folderId, string name, CancellationToken cancellationToken = default)
    {
        WorkflowFolderNames.ValidateIdentifier(folderId, nameof(folderId));
        var (displayName, normalizedName) = WorkflowFolderNames.Normalize(name);
        var access = RequireScope();
        try
        {
            await using var unit = await store.BeginAsync(DocumentCommitScope.Of(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind), cancellationToken);
            var (envelope, folder) = await LoadOwnedAsync(unit, folderId, access, cancellationToken);
            folder.Name = displayName;
            folder.NormalizedName = normalizedName;
            folder.LastModifiedAt = clock.UtcNow;
            await SaveAsync(unit, envelope, folder, access, cancellationToken);
            await unit.CommitAsync(cancellationToken);
            return folder;
        }
        catch (DocumentAtomicWriteException exception) when (IsConflict(exception.Status))
        {
            throw new WorkflowFolderRestructureConflictException(exception);
        }
    }

    public async Task<WorkflowFolder> MoveAsync(string folderId, string? parentId, CancellationToken cancellationToken = default)
    {
        WorkflowFolderNames.ValidateIdentifier(folderId, nameof(folderId));
        if (parentId is not null)
            WorkflowFolderNames.ValidateIdentifier(parentId, nameof(parentId));
        if (StringComparer.Ordinal.Equals(folderId, parentId))
            throw new ArgumentException("A workflow folder cannot be its own parent.", nameof(parentId));
        var access = RequireScope();
        var snapshot = await SnapshotFoldersAsync(cancellationToken);
        var stabilized = await SnapshotFoldersAsync(cancellationToken);
        if (!SameSnapshot(snapshot, stabilized))
            throw new WorkflowFolderRestructureConflictException();
        snapshot = stabilized;
        try
        {
            await using var unit = await store.BeginAsync(DocumentCommitScope.Of(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind), cancellationToken);
            var fences = new Dictionary<string, (DocumentEnvelope Envelope, WorkflowFolder Folder)>(StringComparer.Ordinal);
            var moving = await LoadOwnedAsync(unit, folderId, access, cancellationToken);
            EnsureUnchanged(snapshot, moving);
            fences.Add(folderId, moving);
            var descendants = FindDescendants(folderId, snapshot);
            foreach (var descendant in descendants)
            {
                var loaded = await LoadOwnedAsync(unit, descendant.Id, access, cancellationToken);
                EnsureUnchanged(snapshot, loaded);
                fences.TryAdd(loaded.Folder.Id, loaded);
            }
            if (descendants.Any(descendant => StringComparer.Ordinal.Equals(descendant.Id, parentId)))
                throw new ArgumentException("A workflow folder cannot be moved into one of its descendants.", nameof(parentId));
            await AddAncestorsAsync(unit, moving.Folder.ParentFolderId, folderId, access, fences, snapshot, cancellationToken);
            var destinationDepth = await AddAncestorsAsync(unit, parentId, folderId, access, fences, snapshot, cancellationToken);
            var deepestDescendant = descendants.Count == 0 ? 0 : descendants.Max(descendant => RelativeDepth(descendant, folderId, descendants));
            if (destinationDepth + 1 + deepestDescendant > WorkflowFolderNames.MaximumDepth)
                throw new ArgumentOutOfRangeException(nameof(parentId), "Workflow-folder depth cannot exceed 16.");

            // The proposed parent chain is now fully read and fenced.  Seeing the moving id in it would
            // otherwise create a cycle; the bounded walk also rejects a depth greater than sixteen.
            moving.Folder.ParentFolderId = parentId;
            moving.Folder.ParentKey = parentId ?? WorkflowFolder.RootParentKey;
            moving.Folder.LastModifiedAt = clock.UtcNow;
            foreach (var (_, fenced) in fences)
                await SaveAsync(unit, fenced.Envelope, fenced.Folder, access, cancellationToken);
            await unit.CommitAsync(cancellationToken);
            return moving.Folder;
        }
        catch (DocumentAtomicWriteException exception) when (IsConflict(exception.Status))
        {
            throw new WorkflowFolderRestructureConflictException(exception);
        }
    }

    public async Task DeleteEmptyAsync(string folderId, CancellationToken cancellationToken = default)
    {
        WorkflowFolderNames.ValidateIdentifier(folderId, nameof(folderId));
        var access = RequireScope();
        var snapshotEnvelope = await store.LoadAsync(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind, folderId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folderId);
        var snapshotFolder = ReadFolder(snapshotEnvelope);
        try { access.EnsureTenantScope(snapshotFolder.TenantId); }
        catch (InvalidOperationException) { throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folderId); }
        var childExists = await HasDirectChildAsync(folderId, cancellationToken);
        var definitionExists = await HasDirectDefinitionAsync(folderId, cancellationToken);
        if (childExists || definitionExists)
            throw new WorkflowFolderRestructureConflictException();
        try
        {
            await using var unit = await store.BeginAsync(DocumentCommitScope.Of(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind), cancellationToken);
            // The bounded snapshot is taken before the UoW because native providers do not permit a
            // second physical reader while an atomic writer is open. Membership writers CAS-touch this
            // folder, so its expected-version delete rejects any intervening placement or child creation.
            var loaded = await LoadOwnedAsync(unit, folderId, access, cancellationToken);
            if (loaded.Envelope.Version != snapshotEnvelope.Version)
                throw new WorkflowFolderRestructureConflictException();
            var envelope = loaded.Envelope;
            var result = await unit.DeleteAsync(new DeleteDocumentRequest(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind, folderId, envelope.Version), cancellationToken);
            if (result.Status != DocumentStoreWriteStatus.Deleted)
                throw new WorkflowFolderRestructureConflictException();
            await unit.CommitAsync(cancellationToken);
        }
        catch (DocumentAtomicWriteException exception) when (IsConflict(exception.Status))
        {
            throw new WorkflowFolderRestructureConflictException(exception);
        }
    }

    private PersistenceAccessContext RequireScope()
    {
        var access = accessContextAccessor.Current;
        if (access.Scope is null)
            throw new InvalidOperationException("Workflow-folder restructuring requires a tenant-scoped persistence context.");
        return access;
    }

    private static async Task<int> AddAncestorsAsync(
        IDocumentUnitOfWork unit,
        string? folderId,
        string movingId,
        PersistenceAccessContext access,
        IDictionary<string, (DocumentEnvelope Envelope, WorkflowFolder Folder)> fences,
        IReadOnlyList<FolderSnapshot> snapshot,
        CancellationToken cancellationToken)
    {
        var depth = 0;
        while (folderId is not null)
        {
            if (StringComparer.Ordinal.Equals(folderId, movingId))
                throw new ArgumentException("A workflow folder cannot be moved into one of its descendants.", nameof(folderId));
            var current = await LoadOwnedAsync(unit, folderId, access, cancellationToken);
            EnsureUnchanged(snapshot, current);
            fences.TryAdd(folderId, current);
            folderId = current.Folder.ParentFolderId;
            if (++depth > WorkflowFolderNames.MaximumDepth)
                throw new ArgumentOutOfRangeException(nameof(folderId), "Workflow-folder depth cannot exceed 16.");
        }
        return depth;
    }

    private static IReadOnlyList<WorkflowFolder> FindDescendants(string folderId, IReadOnlyList<FolderSnapshot> snapshot)
    {
        var all = snapshot.Select(item => item.Entity).ToArray();
        var descendants = new List<WorkflowFolder>();
        var parents = new HashSet<string>(StringComparer.Ordinal) { folderId };
        while (true)
        {
            var next = all.Where(folder => folder.ParentFolderId is not null && parents.Contains(folder.ParentFolderId))
                .Where(folder => descendants.All(existing => !StringComparer.Ordinal.Equals(existing.Id, folder.Id)))
                .ToArray();
            if (next.Length == 0)
                return descendants;
            descendants.AddRange(next);
            foreach (var folder in next)
                parents.Add(folder.Id);
        }
    }

    private static int RelativeDepth(WorkflowFolder descendant, string rootId, IReadOnlyList<WorkflowFolder> descendants)
    {
        var byId = descendants.ToDictionary(folder => folder.Id, StringComparer.Ordinal);
        var depth = 1;
        var parentId = descendant.ParentFolderId;
        while (!StringComparer.Ordinal.Equals(parentId, rootId))
        {
            if (parentId is null || !byId.TryGetValue(parentId, out var parent))
                throw new InvalidOperationException("Workflow-folder descendants are inconsistent.");
            parentId = parent.ParentFolderId;
            depth++;
        }
        return depth;
    }

    private async Task<IReadOnlyList<FolderSnapshot>> SnapshotFoldersAsync(CancellationToken cancellationToken)
    {
        var documents = await BoundedStore.QueryAsync(new DocumentQuery(
            WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind,
            WorkflowsDesignStorageManifest.ListAllQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(WorkflowsDesignStorageManifest.CollectionField, WorkflowsDesignStorageManifest.WorkflowFolderCollection))]), cancellationToken);
        return documents.Documents.Select(document => new FolderSnapshot(document, ReadFolder(document))).ToArray();
    }

    private async Task<bool> HasDirectChildAsync(string folderId, CancellationToken cancellationToken)
    {
        var result = await BoundedStore.QueryAsync(new DocumentQuery(
            WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind,
            WorkflowsDesignStorageManifest.PageWorkflowFoldersQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(WorkflowsDesignStorageManifest.WorkflowFolderParentKeyField, folderId))],
            [
                new DocumentQueryOrder(WorkflowsDesignStorageManifest.WorkflowFolderNormalizedNameField, PhysicalSortDirection.Ascending),
                new DocumentQueryOrder("entity.id", PhysicalSortDirection.Ascending)
            ],
            take: 1), cancellationToken);
        return result.Documents.Count != 0;
    }

    private async Task<bool> HasDirectDefinitionAsync(string folderId, CancellationToken cancellationToken)
    {
        var result = await BoundedStore.QueryAsync(new DocumentQuery(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            WorkflowsDesignStorageManifest.PageAllWorkflowDefinitionsByFolderQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(WorkflowsDesignStorageManifest.WorkflowDefinitionFolderIdField, folderId))],
            [
                new DocumentQueryOrder(WorkflowsDesignStorageManifest.WorkflowDefinitionLastModifiedAtField, PhysicalSortDirection.Descending),
                new DocumentQueryOrder(WorkflowsDesignStorageManifest.WorkflowDefinitionIdField, PhysicalSortDirection.Ascending)
            ],
            take: 1), cancellationToken);
        return result.Documents.Count != 0;
    }

    private IBoundedDocumentStore BoundedStore => _boundedStore ?? throw new InvalidOperationException(
        "Workflow-folder restructuring requires an admitted bounded document-store runtime.");

    private static WorkflowFolder ReadFolder(DocumentEnvelope document) =>
        JsonSerializer.Deserialize<GroundworkDocument<WorkflowFolder>>(document.ContentJson, GroundworkDesignJson.Options)?.Entity
        ?? throw new InvalidOperationException("The workflow-folder document is empty.");

    private static void EnsureUnchanged(IReadOnlyList<FolderSnapshot> snapshot, (DocumentEnvelope Envelope, WorkflowFolder Folder) loaded)
    {
        var expected = snapshot.SingleOrDefault(item => StringComparer.Ordinal.Equals(item.Entity.Id, loaded.Folder.Id));
        if (expected is null || expected.Envelope.Version != loaded.Envelope.Version ||
            !StringComparer.Ordinal.Equals(expected.Entity.ParentFolderId, loaded.Folder.ParentFolderId))
            throw new WorkflowFolderRestructureConflictException();
    }

    private static bool SameSnapshot(IReadOnlyList<FolderSnapshot> first, IReadOnlyList<FolderSnapshot> second) =>
        first.Count == second.Count && first.All(item => second.Any(other =>
            StringComparer.Ordinal.Equals(item.Entity.Id, other.Entity.Id) &&
            StringComparer.Ordinal.Equals(item.Entity.ParentFolderId, other.Entity.ParentFolderId) &&
            item.Envelope.Version == other.Envelope.Version));

    private sealed record FolderSnapshot(DocumentEnvelope Envelope, WorkflowFolder Entity);

    private static async Task<(DocumentEnvelope Envelope, WorkflowFolder Folder)> LoadOwnedAsync(
        IDocumentUnitOfWork unit,
        string folderId,
        PersistenceAccessContext access,
        CancellationToken cancellationToken)
    {
        var envelope = await unit.LoadAsync(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind, folderId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folderId);
        var folder = JsonSerializer.Deserialize<GroundworkDocument<WorkflowFolder>>(envelope.ContentJson, GroundworkDesignJson.Options)?.Entity
            ?? throw new InvalidOperationException("The workflow-folder document is empty.");
        try
        {
            access.EnsureTenantScope(folder.TenantId);
        }
        catch (InvalidOperationException)
        {
            throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folderId);
        }
        return (envelope, folder);
    }

    private static async Task SaveAsync(
        IDocumentUnitOfWork unit,
        DocumentEnvelope envelope,
        WorkflowFolder folder,
        PersistenceAccessContext access,
        CancellationToken cancellationToken)
    {
        var result = await unit.SaveAsync(GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowFolderCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            folder,
            GroundworkDesignJson.Options,
            access) with { ExpectedVersion = envelope.Version }, cancellationToken);
        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw new WorkflowFolderRestructureConflictException();
    }

    private static bool IsConflict(DocumentStoreWriteStatus status) => status is
        DocumentStoreWriteStatus.ConcurrencyConflict or
        DocumentStoreWriteStatus.IdentityConflict or
        DocumentStoreWriteStatus.NotFound;
}

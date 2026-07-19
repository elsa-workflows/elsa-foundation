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
    private const int SnapshotPageSize = 100;
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
        // A concurrent move can shift a child across a continuation boundary without changing this root.
        // Stabilize two bounded subtree traversals before the UoW, then CAS-fence every observed node.
        var snapshot = await SnapshotSubtreeAsync(folderId, access, cancellationToken);
        SubtreeSnapshot stabilized;
        try
        {
            stabilized = await SnapshotSubtreeAsync(folderId, access, cancellationToken);
        }
        catch (EntityNotFoundException exception)
        {
            throw new WorkflowFolderRestructureConflictException(exception);
        }
        if (!SameSnapshot(snapshot, stabilized))
            throw new WorkflowFolderRestructureConflictException();
        snapshot = stabilized;
        if (parentId is not null && snapshot.Folders.ContainsKey(parentId))
            throw new ArgumentException("A workflow folder cannot be moved into one of its descendants.", nameof(parentId));
        try
        {
            await using var unit = await store.BeginAsync(DocumentCommitScope.Of(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind), cancellationToken);
            var fences = new Dictionary<string, (DocumentEnvelope Envelope, WorkflowFolder Folder)>(StringComparer.Ordinal);
            var moving = await LoadFencedAsync(unit, folderId, access, snapshot.Folders, cancellationToken);
            fences.Add(folderId, moving);
            foreach (var descendant in snapshot.Folders.Values.Where(item => !StringComparer.Ordinal.Equals(item.Entity.Id, folderId)))
            {
                var loaded = await LoadFencedAsync(unit, descendant.Entity.Id, access, snapshot.Folders, cancellationToken);
                fences.TryAdd(loaded.Folder.Id, loaded);
            }
            await AddAncestorsAsync(unit, moving.Folder.ParentFolderId, folderId, access, fences, cancellationToken);
            var destinationDepth = await AddAncestorsAsync(unit, parentId, folderId, access, fences, cancellationToken);
            if (destinationDepth + 1 + snapshot.DeepestRelativeDepth > WorkflowFolderNames.MaximumDepth)
                throw new ArgumentOutOfRangeException(nameof(parentId), "Workflow-folder depth cannot exceed 16.");

            // The proposed parent chain is now fully read and fenced.  Seeing the moving id in it would
            // otherwise create a cycle; the bounded walk also rejects a depth greater than sixteen.
            moving.Folder.ParentFolderId = parentId;
            moving.Folder.ParentKey = WorkflowFolder.ToParentKey(parentId);
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
        CancellationToken cancellationToken)
    {
        var depth = 0;
        while (folderId is not null)
        {
            if (StringComparer.Ordinal.Equals(folderId, movingId))
                throw new ArgumentException("A workflow folder cannot be moved into one of its descendants.", nameof(folderId));
            var current = await LoadOwnedAsync(unit, folderId, access, cancellationToken);
            fences.TryAdd(folderId, current);
            folderId = current.Folder.ParentFolderId;
            if (++depth > WorkflowFolderNames.MaximumDepth)
                throw new ArgumentOutOfRangeException(nameof(folderId), "Workflow-folder depth cannot exceed 16.");
        }
        return depth;
    }

    private async Task<SubtreeSnapshot> SnapshotSubtreeAsync(
        string folderId,
        PersistenceAccessContext access,
        CancellationToken cancellationToken)
    {
        var rootEnvelope = await store.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind,
            folderId,
            cancellationToken) ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folderId);
        var root = ReadFolder(rootEnvelope);
        EnsureOwned(root, folderId, access);
        var folders = new Dictionary<string, FolderSnapshot>(StringComparer.Ordinal)
        {
            [folderId] = new(rootEnvelope, root, 0)
        };
        var pendingParents = new Queue<FolderSnapshot>();
        pendingParents.Enqueue(folders[folderId]);
        var deepestRelativeDepth = 0;
        while (pendingParents.TryDequeue(out var parent))
        {
            string? continuation = null;
            do
            {
                var page = await BoundedStore.QueryAsync(
                    DirectChildrenQuery(parent.Entity.Id, SnapshotPageSize, continuation),
                    cancellationToken);
                foreach (var envelope in page.Documents)
                {
                    var child = ReadFolder(envelope);
                    EnsureOwned(child, child.Id, access);
                    if (!StringComparer.Ordinal.Equals(child.ParentFolderId, parent.Entity.Id))
                        throw new WorkflowFolderRestructureConflictException();
                    var relativeDepth = parent.RelativeDepth + 1;
                    var childSnapshot = new FolderSnapshot(envelope, child, relativeDepth);
                    if (!folders.TryAdd(child.Id, childSnapshot))
                        throw new WorkflowFolderRestructureConflictException();
                    pendingParents.Enqueue(childSnapshot);
                    deepestRelativeDepth = Math.Max(deepestRelativeDepth, relativeDepth);
                }
                continuation = page.NextContinuation;
            } while (continuation is not null);
        }
        return new SubtreeSnapshot(folders, deepestRelativeDepth);
    }

    private async Task<bool> HasDirectChildAsync(string folderId, CancellationToken cancellationToken)
    {
        var result = await BoundedStore.QueryAsync(DirectChildrenQuery(folderId, 1), cancellationToken);
        return result.Documents.Count != 0;
    }

    private static DocumentQuery DirectChildrenQuery(string folderId, int take, string? continuation = null) =>
        new(
            WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind,
            WorkflowsDesignStorageManifest.PageWorkflowFoldersQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                WorkflowsDesignStorageManifest.WorkflowFolderParentKeyField,
                WorkflowFolder.ToParentKey(folderId)))],
            [
                new DocumentQueryOrder(WorkflowsDesignStorageManifest.WorkflowFolderNormalizedNameField, PhysicalSortDirection.Ascending),
                new DocumentQueryOrder("entity.id", PhysicalSortDirection.Ascending)
            ],
            take: take,
            continuation: continuation);

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

    private static async Task<(DocumentEnvelope Envelope, WorkflowFolder Folder)> LoadFencedAsync(
        IDocumentUnitOfWork unit,
        string folderId,
        PersistenceAccessContext access,
        IReadOnlyDictionary<string, FolderSnapshot> snapshot,
        CancellationToken cancellationToken)
    {
        (DocumentEnvelope Envelope, WorkflowFolder Folder) loaded;
        try
        {
            loaded = await LoadOwnedAsync(unit, folderId, access, cancellationToken);
        }
        catch (EntityNotFoundException exception)
        {
            throw new WorkflowFolderRestructureConflictException(exception);
        }
        if (!snapshot.TryGetValue(loaded.Folder.Id, out var expected) ||
            expected.Envelope.Version != loaded.Envelope.Version ||
            !StringComparer.Ordinal.Equals(expected.Entity.ParentFolderId, loaded.Folder.ParentFolderId))
            throw new WorkflowFolderRestructureConflictException();
        return loaded;
    }

    private static bool SameSnapshot(SubtreeSnapshot first, SubtreeSnapshot second)
    {
        if (first.Folders.Count != second.Folders.Count)
            return false;
        foreach (var (id, item) in first.Folders)
        {
            if (!second.Folders.TryGetValue(id, out var other) ||
                !StringComparer.Ordinal.Equals(item.Entity.ParentFolderId, other.Entity.ParentFolderId) ||
                item.Envelope.Version != other.Envelope.Version)
                return false;
        }
        return true;
    }

    private static void EnsureOwned(WorkflowFolder folder, string folderId, PersistenceAccessContext access)
    {
        try
        {
            access.EnsureTenantScope(folder.TenantId);
        }
        catch (InvalidOperationException)
        {
            throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folderId);
        }
    }

    private sealed record FolderSnapshot(DocumentEnvelope Envelope, WorkflowFolder Entity, int RelativeDepth);

    private sealed record SubtreeSnapshot(
        IReadOnlyDictionary<string, FolderSnapshot> Folders,
        int DeepestRelativeDepth);

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
        EnsureOwned(folder, folderId, access);
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
            access) with
        { ExpectedVersion = envelope.Version }, cancellationToken);
        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw new WorkflowFolderRestructureConflictException();
    }

    private static bool IsConflict(DocumentStoreWriteStatus status) => status is
        DocumentStoreWriteStatus.ConcurrencyConflict or
        DocumentStoreWriteStatus.IdentityConflict or
        DocumentStoreWriteStatus.NotFound;
}

using System.Text;
using System.Text.Json;
using Elsa.Locking.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfWorkflowLibraryStore(WorkflowsDesignDbContext db,
    IPersistenceAccessContextAccessor access, IDesignAtomicWriter atomic,
    IDistributedLockProvider? locks = null) : IWorkflowLibraryStore
{
    private const int MaximumFolders = 10000;
    private const int MaximumTags = 10000;
    private const int MaximumAssignments = 64;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Tenant => access.Current.RequireScope().Value;
    private static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N");
    private static string Revision() => Guid.NewGuid().ToString("N");
    private static string DefinitionHash(string id) => EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(id));
    private IQueryable<T> Scoped<T>(IQueryable<T> query) where T : TenantEntity
    {
        // Organization identities and query correlations belong to exactly one catalog.
        // The legacy definition inventory remains the explicit across-scope surface.
        if (access.Current.AcrossScopes) throw new InvalidOperationException("Workflow library operations require a single persistence scope.");
        return EfDesignSupport.InScope(query, access, row => row.TenantId);
    }
    private IQueryable<WorkflowFolderEntity> FolderRows => Scoped(db.Folders);
    private IQueryable<WorkflowOrganizationEntity> OrganizationRows => Scoped(db.Organizations);
    private IQueryable<WorkflowTagEntity> TagRows => Scoped(db.Tags);
    private IQueryable<WorkflowTagAssignmentEntity> AssignmentRows => Scoped(db.TagAssignments);
    private IQueryable<WorkflowDefinition> DefinitionRows => Scoped(db.Definitions);
    private static T Missing<T>(string id) => throw EntityNotFoundException.ForEntity(typeof(T), id);
    private static void Conflict(string message) => throw new WorkflowLibraryConflictException(message);
    private static void CheckRevision(string? expected, string actual)
    {
        if (expected is not null && !StringComparer.Ordinal.Equals(expected, actual))
            Conflict("This item changed. Reload it before applying your changes.");
    }
    private static void ValidatePage(int size)
    {
        if (size is < 1 or > 100) throw new ArgumentException("Page size must be between 1 and 100.");
    }
    private static string Text(string value, string label, int maximum = 256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        if (trimmed.Length > maximum || trimmed.Any(char.IsControl)) throw new ArgumentException($"{label} must have 1–{maximum} characters without control characters.");
        return trimmed;
    }
    private static string Normalized(string value)
    {
        var result = value.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        if (result.Length > 512) throw new ArgumentException("The normalized organization name exceeds 512 characters.");
        return result;
    }
    private static void ValidateActor(string actor)
    {
        Text(actor, "Actor", 256);
        if (actor.Length > 256) throw new ArgumentException("Actor identifiers cannot exceed 256 characters.");
    }
    private static void ValidateStoredId(string id, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!id.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(id[prefix.Length..], "N", out _))
            throw new ArgumentException("The supplied organization identity is invalid.");
    }

    internal static async ValueTask<IDistributedSynchronizationHandle?> AcquireHierarchyAsync(IPersistenceAccessContextAccessor context,
        IDistributedLockProvider? provider, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var scope = context.Current.RequireScope();
        // The transaction's revision fence below rejects competing hierarchy observations.
        // A host-provided lease additionally coordinates contention; it is not required to
        // publish or organize workflows in an otherwise valid minimal EF composition.
        return provider is null ? null : await provider.AcquireLockAsync(
            "workflow-library:" + EfDesignSupport.ScopeKey(scope.Value), null, token);
    }

    internal static async Task TouchFenceAsync(WorkflowsDesignDbContext context, IPersistenceAccessContextAccessor accessContext, CancellationToken token)
    {
        var tenant = accessContext.Current.RequireScope().Value;
        var row = await EfDesignSupport.InScope(context.LibraryFences, accessContext, candidate => candidate.TenantId)
            .SingleOrDefaultAsync(candidate => candidate.Id == "hierarchy", token);
        if (row is null)
        {
            row = new WorkflowLibraryFenceEntity { Id = "hierarchy", TenantId = tenant, CreatedAt = DateTimeOffset.UtcNow };
            context.LibraryFences.Add(row);
        }
        row.Revision = Revision();
        row.LastModifiedAt = DateTimeOffset.UtcNow;
    }

    private async Task<T> WriteAsync<T>(string key, string operation, object material,
        Func<CancellationToken, Task<T>> stage, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 256) throw new ArgumentException("Operation keys cannot exceed 256 characters.");
        await using var hierarchy = await AcquireHierarchyAsync(access, locks, token);
        using var lease = hierarchy is null ? null : CancellationTokenSource.CreateLinkedTokenSource(token, hierarchy.HandleLostToken);
        try
        {
            var result = await atomic.ExecuteAsync(new DesignOperationKey(key), operation, material, ["workflow-library"], async (_, ct) =>
            {
                // Store clients may reuse a scope. Only these internal, already-committed
                // metadata rows are detached: ancestry decisions must use this transaction's
                // state, while unrelated domain tracking remains owned by its caller.
                foreach (var entry in db.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Unchanged &&
                    entry.Entity is WorkflowFolderEntity or WorkflowOrganizationEntity or WorkflowTagEntity or WorkflowTagAssignmentEntity or
                        WorkflowLibraryViewEntity or WorkflowFavoriteEntity or WorkflowLibraryFenceEntity).ToArray())
                    entry.State = EntityState.Detached;
                await TouchFenceAsync(db, access, ct);
                return DesignAtomicWriteStage<T>.Accepted(await stage(ct));
            }, cancellationToken: lease?.Token ?? token);
            if (result.Status is DesignAtomicWriteStatus.Conflict or DesignAtomicWriteStatus.Rejected)
                throw new WorkflowLibraryConflictException("This operation key was already used for a different organization change.");
            return result.Value!;
        }
        catch (DesignPersistenceException exception) when (exception.FailureKind == DesignPersistenceFailureKind.Concurrency)
        { throw new WorkflowLibraryConflictException("Workflow organization changed concurrently. Reload and try again."); }
        catch (DbUpdateConcurrencyException)
        { throw new WorkflowLibraryConflictException("Workflow organization changed concurrently. Reload and try again."); }
    }

    private void Audit(string target, string actor, string operation, string key, object? before, object? after)
    {
        ValidateActor(actor);
        var now = DateTimeOffset.UtcNow;
        db.LibraryAudit.Add(new WorkflowLibraryAuditEntity {
            Id = NewId("audit-"), TenantId = Tenant, TargetId = target, ActorId = actor, Operation = operation,
            OperationKey = key, BeforeJson = JsonSerializer.Serialize(before, Json), AfterJson = JsonSerializer.Serialize(after, Json),
            CreatedAt = now, LastModifiedAt = now });
    }

    private async Task<WorkflowDefinition> DefinitionAsync(string id, bool mutable, CancellationToken token)
    {
        var hash = DefinitionHash(id);
        var row = await DefinitionRows.SingleOrDefaultAsync(candidate => candidate.IdLookupHash == hash, token)
            ?? Missing<WorkflowDefinition>(id);
        await db.Entry(row).ReloadAsync(token);
        if (db.Entry(row).State == EntityState.Detached) return Missing<WorkflowDefinition>(id);
        EfDesignSupport.EnsurePhysicalScope(db, row, "workflow library definition lookup");
        EfDesignSupport.EnsureDefinitionIdentity(id, row.Id, "workflow library definition lookup");
        if (mutable && row.DeletedAt is not null) Conflict("Restore this workflow before changing its organization.");
        return row;
    }

    private async Task<WorkflowFolderEntity> FolderAsync(string id, CancellationToken token)
    {
        ValidateStoredId(id, "folder-");
        var row = await FolderRows.SingleOrDefaultAsync(row => row.Id == id, token) ?? Missing<WorkflowFolderEntity>(id);
        await db.Entry(row).ReloadAsync(token);
        return db.Entry(row).State == EntityState.Detached ? Missing<WorkflowFolderEntity>(id) : row;
    }

    private static WorkflowFolder Folder(WorkflowFolderEntity row) => new(row.Id, row.ParentId, row.Name,
        row.NormalizedName, row.CreatedAt, row.LastModifiedAt, row.Revision);

    private async Task<IReadOnlyList<WorkflowFolder>> BreadcrumbAsync(string? id, CancellationToken token)
    {
        if (id is null) return [];
        var folder = await FolderAsync(id, token);
        var ids = folder.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length is < 1 or > 16) throw new InvalidOperationException("The stored folder path is corrupt.");
        var rows = await FolderRows.Where(row => ids.Contains(row.Id)).ToArrayAsync(token);
        if (rows.Length != ids.Length) throw new InvalidOperationException("The stored folder hierarchy has an unavailable ancestor.");
        var index = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        return ids.Select(key => Folder(index[key])).ToArray();
    }

    public Task<WorkflowOrganization?> GetOrganizationAsync(string definitionId, CancellationToken cancellationToken = default) =>
        EfDesignSupport.ReadAsync("reading workflow organization", () => GetOrganizationCoreAsync(definitionId, cancellationToken));

    private async Task<WorkflowOrganization?> GetOrganizationCoreAsync(string definitionId, CancellationToken cancellationToken)
    {
        var hash = DefinitionHash(definitionId);
        var definition = await DefinitionRows.AsNoTracking().SingleOrDefaultAsync(row => row.IdLookupHash == hash, cancellationToken);
        if (definition is null) return null;
        EfDesignSupport.EnsureDefinitionIdentity(definitionId, definition.Id, "workflow organization lookup");
        return await OrganizationAsync(definition.Id, hash, cancellationToken);
    }

    private async Task<WorkflowOrganization> OrganizationAsync(string id, string hash, CancellationToken token)
    {
        var row = await OrganizationRows.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.DefinitionHash == hash, token);
        var assignments = await AssignmentRows.AsNoTracking().Where(candidate => candidate.DefinitionHash == hash)
            .OrderBy(candidate => candidate.TagId).ThenBy(candidate => candidate.ValueKey).Take(MaximumAssignments + 1).ToArrayAsync(token);
        if (assignments.Length > MaximumAssignments) throw new InvalidOperationException("The stored workflow tag set exceeds its limit.");
        return new(id, row?.FolderId, await BreadcrumbAsync(row?.FolderId, token),
            assignments.Select(candidate => new WorkflowTagAssignment(candidate.TagId, candidate.ValueKey.Length == 0 ? null : candidate.ValueKey)).ToArray(), row?.Revision ?? "0");
    }

    private WorkflowOrganizationEntity NewOrganization(WorkflowDefinition definition)
    {
        var row = new WorkflowOrganizationEntity { Id = definition.IdLookupHash, DefinitionHash = definition.IdLookupHash,
            DefinitionId = definition.Id, TenantId = Tenant, Revision = Revision(), CreatedAt = DateTimeOffset.UtcNow, LastModifiedAt = DateTimeOffset.UtcNow };
        db.Organizations.Add(row);
        return row;
    }

    private sealed record Cursor(string Binding, string Key, string Id, long Ticks = 0);
    private string Binding(string kind, object material, string? subject = null) =>
        EfDesignSupport.LookupHash(JsonSerializer.Serialize(new { kind, tenant = access.Current.Scope?.Value,
            global = access.Current.IsGlobal, subject, material }, Json));
    private static string Encode(Cursor cursor) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor, Json)))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static Cursor? Decode(string? token, string binding)
    {
        if (token is null) return null;
        if (token.Length > 8192) throw new ArgumentException("The continuation token is invalid.");
        try
        {
            var value = token.Replace('-', '+').Replace('_', '/');
            value = value.PadRight((value.Length + 3) / 4 * 4, '=');
            var cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(value), Json);
            if (cursor is null || cursor.Binding != binding || string.IsNullOrEmpty(cursor.Id) || cursor.Key is null ||
                cursor.Id.Length > 128 || cursor.Key.Length > 4096 || cursor.Ticks < 0)
                throw new ArgumentException("The continuation token does not belong to this query.");
            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        { throw new ArgumentException("The continuation token is invalid.", exception); }
    }
}

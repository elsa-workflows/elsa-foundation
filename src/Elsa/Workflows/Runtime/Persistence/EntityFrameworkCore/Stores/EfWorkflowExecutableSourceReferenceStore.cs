using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowExecutableSourceReferenceStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor access) : IWorkflowExecutableSourceReferenceStore
{
    public async ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
    {
        Validate(reference);
        var scope = ScopeForWrite(reference);
        var id = CreateId(scope, reference.SourceReferenceId);
        var existing = await context.WorkflowExecutableSourceReferences.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is not null)
        {
            _ = Read(existing, scope, reference.SourceReferenceId);
            throw new InvalidOperationException($"Workflow executable source reference '{reference.SourceReferenceId}' already exists; source references are create-only.");
        }
        context.WorkflowExecutableSourceReferences.Add(ToEntity(reference, scope, id));
        try
        { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        { context.ChangeTracker.Clear(); throw new InvalidOperationException("The workflow executable source reference already exists; source references are create-only.", exception); }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    public async ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);
        var scope = RequireScope();
        var id = CreateId(scope, sourceReferenceId);
        var row = await context.WorkflowExecutableSourceReferences.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.SourceReferenceId == sourceReferenceId, cancellationToken);
        return row is null ? null : Read(row, scope, sourceReferenceId);
    }

    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) => Page(query, Route.Artifact, query.ArtifactId, null, null, cancellationToken);
    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByDefinitionVersionPageAsync(WorkflowExecutableSourceReferenceDefinitionVersionPageQuery query, CancellationToken cancellationToken = default) => Page(query, Route.DefinitionVersion, null, query.DefinitionVersionId, null, cancellationToken);
    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default) => Page(query, Route.All, null, null, query, cancellationToken);

    private async ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> Page(RuntimeStorePageRequest request, Route route, string? artifact, string? definition, WorkflowExecutableSourceReferencePageQuery? all, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var binding = artifact ?? definition ?? (all?.Scope?.ToString() ?? "all");
        var cursor = Decode(request.ContinuationToken, route, scope, binding);
        var query = context.WorkflowExecutableSourceReferences.AsNoTracking().AsQueryable();
        query = query.Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope));
        if (artifact is not null)
            query = query.Where(x => x.ArtifactIdHash == EfRelationalIdentity.Hash(artifact) && x.ArtifactId == artifact);
        if (definition is not null)
            query = query.Where(x => x.DefinitionVersionIdHash == EfRelationalIdentity.Hash(definition) && x.DefinitionVersionId == definition);
        if (all?.Scope is { } sourceScope)
            query = query.Where(x => x.Scope == sourceScope.ToString());
        if (all?.LiveOnly == true)
            query = query.Where(x => !x.IsRetired && x.ExpiresAtUtcTicks > all.Now!.Value.UtcTicks);
        if (cursor is not null)
            query = query.Where(x => string.CompareOrdinal(x.SourceReferenceIdOrderKey, cursor.Key) > 0);
        var rows = await query.OrderBy(x => x.SourceReferenceIdOrderKey).Take(request.Limit + 1).ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > request.Limit;
        if (hasMore)
            rows = rows[..request.Limit];
        var items = rows.Select(x => Read(x, scope, x.SourceReferenceId)).ToArray();
        var next = hasMore ? Encode(route, scope, binding, rows[^1].SourceReferenceIdOrderKey) : null;
        return new RuntimeStorePage<WorkflowExecutableSourceReference>(request, items, next);
    }

    public async ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);
        var scope = RequireScope();
        var row = await context.WorkflowExecutableSourceReferences.SingleOrDefaultAsync(x => x.Id == CreateId(scope, sourceReferenceId), cancellationToken);
        if (row is null)
            return false;
        var current = Read(row, scope, sourceReferenceId);
        if (current.DeletedAt is not null)
            return true;
        Copy(row, current.Retire(deletedAt, reason), scope);
        try
        { await context.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
    }

    public ValueTask<bool> TryRetireAsync(WorkflowExecutableSourceReference expectedLiveReference, WorkflowExecutableSourceReference retiredReference, CancellationToken cancellationToken = default) => TryReplace(expectedLiveReference, retiredReference, false, cancellationToken);
    public ValueTask<bool> TryRestoreAsync(WorkflowExecutableSourceReference expectedRetiredReference, WorkflowExecutableSourceReference restoredReference, CancellationToken cancellationToken = default) => TryReplace(expectedRetiredReference, restoredReference, true, cancellationToken);

    private async ValueTask<bool> TryReplace(WorkflowExecutableSourceReference expected, WorkflowExecutableSourceReference replacement, bool restore, CancellationToken cancellationToken)
    {
        Validate(expected);
        Validate(replacement);
        var valid = WorkflowExecutableSourceReferenceComparer.SameIdentity(expected, replacement) && (restore ? expected.DeletedAt is not null && replacement.DeletedAt is null : expected.DeletedAt is null && replacement.DeletedAt is not null);
        if (!valid)
            return false;
        var scope = RequireScope();
        var row = await context.WorkflowExecutableSourceReferences.SingleOrDefaultAsync(x => x.Id == CreateId(scope, expected.SourceReferenceId), cancellationToken);
        if (row is null)
            return false;
        var current = Read(row, scope, expected.SourceReferenceId);
        if (!WorkflowExecutableSourceReferenceComparer.SameSnapshot(current, expected))
            return false;
        Copy(row, replacement, scope);
        try
        { await context.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
    }

    public async ValueTask<bool> DeleteAsync(string sourceReferenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);
        var scope = RequireScope();
        var row = await context.WorkflowExecutableSourceReferences.SingleOrDefaultAsync(x => x.Id == CreateId(scope, sourceReferenceId), cancellationToken);
        if (row is null)
            return false;
        _ = Read(row, scope, sourceReferenceId);
        context.Remove(row);
        try
        { await context.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
    }

    public async ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(WorkflowExecutableSourceReferenceCleanupBatch batch, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var scope = RequireScope();
        var query = context.WorkflowExecutableSourceReferences.Where(x => x.ExpiresAtUtcTicks <= now.UtcTicks || x.IsRetired);
        query = query.Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope));
        var rows = await query.OrderBy(x => x.ExpiresAtUtcTicks).ThenBy(x => x.SourceReferenceIdOrderKey).Take(batch.Limit).ToArrayAsync(cancellationToken);
        var deleted = new List<string>(rows.Length);
        foreach (var row in rows)
        { var current = Read(row, scope, row.SourceReferenceId); if (await DeleteAsync(current.SourceReferenceId, cancellationToken)) deleted.Add(current.SourceReferenceId); }
        return deleted;
    }

    public async ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var scope = RequireScope();
        var candidateHashes = candidates.ArtifactIds.Select(Hash).ToArray();
        var query = context.WorkflowExecutableSourceReferences.Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && !x.IsRetired && x.ExpiresAtUtcTicks > now.UtcTicks && candidateHashes.Contains(x.ArtifactIdHash));
        var rows = await query.Select(x => new { x.ArtifactId, x.ArtifactIdHash }).ToArrayAsync(cancellationToken);
        return candidates.ArtifactIds.Where(candidate => !rows.Any(row => row.ArtifactIdHash == EfRelationalIdentity.Hash(candidate) && row.ArtifactId == candidate)).ToArray();
    }

    private string RequireScope()
    {
        var current = access.Current;
        if (current.AccessPolicy != PersistenceAccessPolicy.Ordinary || current.Scope is null || current.AcrossScopes)
            throw new InvalidOperationException("EF workflow executable source-reference persistence requires one explicit persistence scope.");
        return current.Scope.Value;
    }

    private string ScopeForRead() => RequireScope();
    private string ScopeForWrite(WorkflowExecutableSourceReference reference)
    {
        var scope = RequireScope();
        if (reference.TenantId is not null)
            access.Current.EnsureTenantScope(reference.TenantId);
        return scope;
    }
    private static WorkflowExecutableSourceReferenceEntity ToEntity(WorkflowExecutableSourceReference value, string scope, string id) { var row = new WorkflowExecutableSourceReferenceEntity { Id = id }; Copy(row, value, scope); return row; }
    private static void Copy(WorkflowExecutableSourceReferenceEntity row, WorkflowExecutableSourceReference value, string scope)
    {
        row.SourceReferenceId = value.SourceReferenceId;
        row.SourceReferenceIdOrderKey = OrderKey(value.SourceReferenceId);
        row.ArtifactId = value.ArtifactId;
        row.ArtifactIdHash = Hash(value.ArtifactId);
        row.DefinitionVersionId = value.DefinitionVersionId;
        row.DefinitionVersionIdHash = Hash(value.DefinitionVersionId);
        row.ScopeKey = Encode(scope);
        row.ScopeKeyHash = Hash(scope);
        row.Scope = value.Scope.ToString();
        row.IsRetired = value.DeletedAt is not null;
        row.ExpiresAtUtcTicks = (value.ExpiresAt ?? DateTimeOffset.MaxValue).UtcTicks;
        row.ContentJson = RuntimeArtifactJson.Serialize(value);
        row.SchemaVersion = RuntimeArtifactEfModule.SchemaVersion;
        if (row.Revision <= 0)
            row.Revision = 1;
        else
            row.Revision++;
    }
    private static WorkflowExecutableSourceReference Read(WorkflowExecutableSourceReferenceEntity row, string expectedScope, string expectedId)
    {
        try
        {
            if (row.Id != CreateId(expectedScope, expectedId) || row.SourceReferenceId != expectedId || row.SchemaVersion != RuntimeArtifactEfModule.SchemaVersion || row.SourceReferenceIdOrderKey != OrderKey(expectedId) || row.ScopeKey != Encode(expectedScope) || row.ScopeKeyHash != Hash(expectedScope) || row.ArtifactIdHash != Hash(row.ArtifactId) || row.DefinitionVersionIdHash != Hash(row.DefinitionVersionId) || row.Revision <= 0)
                throw new InvalidDataException("The persisted workflow executable source reference row is corrupt.");
            var value = RuntimeArtifactJson.Deserialize<WorkflowExecutableSourceReference>(row.ContentJson);
            Validate(value);
            if ((value.TenantId is not null && value.TenantId != expectedScope) || EfRelationalIdentity.Decode(row.ScopeKey) != expectedScope || value.SourceReferenceId != expectedId || value.ArtifactId != row.ArtifactId || value.DefinitionVersionId != row.DefinitionVersionId || value.Scope.ToString() != row.Scope || row.ExpiresAtUtcTicks != (value.ExpiresAt ?? DateTimeOffset.MaxValue).UtcTicks || row.IsRetired != (value.DeletedAt is not null))
                throw new InvalidDataException("The persisted workflow executable source reference projection is corrupt.");
            return value;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or NotSupportedException or FormatException) { throw new InvalidDataException("The persisted workflow executable source reference payload is corrupt.", exception); }
    }
    private static void Validate(WorkflowExecutableSourceReference value) { ArgumentNullException.ThrowIfNull(value); ArgumentException.ThrowIfNullOrWhiteSpace(value.SourceReferenceId); ArgumentException.ThrowIfNullOrWhiteSpace(value.ArtifactId); ArgumentException.ThrowIfNullOrWhiteSpace(value.DefinitionVersionId); if (!Enum.IsDefined(value.Scope)) throw new ArgumentOutOfRangeException(nameof(value.Scope)); }
    private static string CreateId(string scope, string value) => Hash($"{scope.Length}:{scope}{value.Length}:{value}");
    private static string Hash(string value) => EfRelationalIdentity.Hash(value);
    private static string Encode(string value) => EfRelationalIdentity.Encode(value);
    private static string OrderKey(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeArtifactEfModule.IdentityMaximumLength));
    private static string Encode(Route route, string? scope, string binding, string key) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(new Cursor(1, route, scope is null ? null : EfRelationalIdentity.Hash(scope), binding, key))));
    private static Cursor? Decode(string? token, Route route, string? scope, string binding) { if (token is null) return null; try { var cursor = RuntimeArtifactJson.Deserialize<Cursor>(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token))); if (cursor.Version != 1 || cursor.Route != route || cursor.ScopeHash != (scope is null ? null : EfRelationalIdentity.Hash(scope)) || cursor.Binding != binding) throw new FormatException(); return cursor; } catch (Exception exception) when (exception is FormatException or ArgumentException or JsonException or InvalidOperationException) { throw new ArgumentException("The source-reference continuation token is invalid.", nameof(token), exception); } }
    private enum Route { All, Artifact, DefinitionVersion }
    private sealed record Cursor(int Version, Route Route, string? ScopeHash, string Binding, string Key);
}

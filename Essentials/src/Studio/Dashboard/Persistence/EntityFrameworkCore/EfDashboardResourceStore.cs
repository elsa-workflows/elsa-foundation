using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;

internal sealed class EfDashboardResourceStore(DashboardResourceDbContext context) : IDashboardResourceStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<DashboardResourceDocument?> FindAsync(string tenantId, string kind, string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = Hash(tenantId, kind, id);
            var row = await context.Resources.AsNoTracking().SingleOrDefaultAsync(item => item.Id == key && !item.Deleted, cancellationToken);
            return row is null ? null : Read(row, tenantId, kind, id);
        }
        catch (Exception error) when (EfRelationalExceptionClassifier.IsStoreBoundaryFailure(error)) { throw Unavailable(error); }
    }

    public async ValueTask<IReadOnlyList<DashboardResourceDocument>> ListAsync(string tenantId, string kind, CancellationToken cancellationToken = default)
    {
        try
        {
            var scope = Hash(tenantId);
            var rows = await context.Resources.AsNoTracking().Where(row => row.ScopeId == scope && row.Kind == kind && !row.Deleted)
                .OrderBy(row => row.Id).ToArrayAsync(cancellationToken);
            return rows.Select(row => Read(row, tenantId, kind, null)).ToArray();
        }
        catch (Exception error) when (EfRelationalExceptionClassifier.IsStoreBoundaryFailure(error)) { throw Unavailable(error); }
    }

    public async ValueTask<DashboardResourceDocument?> FindCreatedAsync(string tenantId, string actorId, string kind,
        string operationKey, string fingerprint, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = Hash(tenantId, actorId, kind, operationKey);
            var operation = await context.Operations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == key, cancellationToken);
            if (operation is null) return null;
            if (operation.Fingerprint != fingerprint) throw new DashboardResourceException("conflict", "This operation key was already used for a different request.");
            var row = await context.Resources.AsNoTracking().SingleOrDefaultAsync(item => item.Id == operation.ResourceKey, cancellationToken);
            if (row is null || row.Deleted) throw new DashboardResourceException("notFound", "The resource created by this operation is unavailable.");
            var document = Read(row, tenantId, kind, null);
            if (document.OwnerId != actorId) throw new DashboardResourceException("notFound", "This resource is unavailable.");
            return document;
        }
        catch (Exception error) when (EfRelationalExceptionClassifier.IsStoreBoundaryFailure(error)) { throw Unavailable(error); }
    }

    public async ValueTask<DashboardResourceWriteResult> WriteAsync(DashboardResourceWrite write, CancellationToken cancellationToken = default)
    {
        var document = write.Document;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (write.OperationKey is { } repeatedKey && await FindCreatedAsync(document.TenantId, document.OwnerId,
                    document.Kind, repeatedKey, write.OperationFingerprint!, cancellationToken) is { } repeated)
                    return new("replayed", repeated);
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var scopeKey = Hash(document.TenantId);
                var scope = await context.Scopes.SingleOrDefaultAsync(row => row.Id == scopeKey, cancellationToken);
                if (scope is null) context.Scopes.Add(scope = new() { Id = scopeKey, Revision = Guid.NewGuid().ToString("N") });
                else scope.Revision = Guid.NewGuid().ToString("N");
                var key = Hash(document.TenantId, document.Kind, document.Id);
                var row = await context.Resources.SingleOrDefaultAsync(item => item.Id == key, cancellationToken);
                if (write.ExpectedRevision is null)
                {
                    if (row is not null) return Status("conflict");
                    var ownerKey = Hash(document.TenantId, document.OwnerId);
                    var ownerMaximum = document.Kind == DashboardResourceKinds.Question ? 100 : 50;
                    var tenantMaximum = document.Kind == DashboardResourceKinds.Question ? 5_000 : 2_000;
                    if (await context.Resources.CountAsync(item => item.ScopeId == scopeKey && item.Kind == document.Kind && !item.Deleted, cancellationToken) >= tenantMaximum ||
                        await context.Resources.CountAsync(item => item.ScopeId == scopeKey && item.OwnerKey == ownerKey && item.Kind == document.Kind && !item.Deleted, cancellationToken) >= ownerMaximum)
                        throw new DashboardResourceException("validation", "The resource capacity has been reached. Remove an owned resource or contact the administrator.");
                    row = new() { Id = key, ScopeId = scopeKey, OwnerKey = ownerKey, Kind = document.Kind };
                    context.Resources.Add(row);
                }
                else
                {
                    if (row is null || row.Deleted) return Status("notFound");
                    var stored = Read(row, document.TenantId, document.Kind, document.Id);
                    if (row.Revision != write.ExpectedRevision || stored.OwnerId != document.OwnerId) return Status("conflict");
                }
                row.Revision = write.Delete ? $"deleted-{Guid.NewGuid():N}" : document.Revision;
                row.DocumentJson = JsonSerializer.Serialize(document, Json);
                row.Deleted = write.Delete;
                if (write.OperationKey is { } operationKey)
                    context.Operations.Add(new() { Id = Hash(document.TenantId, document.OwnerId, document.Kind, operationKey), ResourceKey = key,
                        Fingerprint = write.OperationFingerprint! });
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                context.ChangeTracker.Clear();
                return new("saved", document);
            }
            catch (Exception error) when (EfRelationalExceptionClassifier.IsWriteConflict(error, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient))
            {
                context.ChangeTracker.Clear();
                if (attempt == 2) return new("conflict");
            }
            catch (Exception error) when (EfRelationalExceptionClassifier.IsStoreBoundaryFailure(error))
            { context.ChangeTracker.Clear(); throw Unavailable(error); }
        }
        return new("conflict");
    }

    private static DashboardResourceDocument Read(DashboardResourceRow row, string tenantId, string kind, string? id)
    {
        if (!EfSchemaVersion.Readable(DashboardResourcesEfModule.Chain, row.SchemaVersion))
            throw new DashboardResourceException("unavailable", "The resource schema is unavailable.");
        var text = DashboardResourcesEfModule.Chain.Upcast<DashboardResourceRow>(row.SchemaVersion,
            (nameof(DashboardResourceRow.DocumentJson), row.DocumentJson))[nameof(DashboardResourceRow.DocumentJson)]!;
        var document = JsonSerializer.Deserialize<DashboardResourceDocument>(text, Json)
            ?? throw new DashboardResourceException("unavailable", "The stored resource is invalid.");
        if (document.TenantId != tenantId || document.Kind != kind || id is not null && document.Id != id ||
            row.Id != Hash(document.TenantId, document.Kind, document.Id) || row.ScopeId != Hash(tenantId) || row.OwnerKey != Hash(tenantId, document.OwnerId))
            throw new DashboardResourceException("unavailable", "The stored resource scope is invalid.");
        return document;
    }

    private static string Hash(params string[] parts) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(parts.Select(part => $"{part.Length}:{part}")))));
    private DashboardResourceWriteResult Status(string status) { context.ChangeTracker.Clear(); return new(status); }
    private static DashboardResourceException Unavailable(Exception inner) => new("unavailable", "Dashboard resource persistence is unavailable. Retry or contact the administrator.", inner);
}

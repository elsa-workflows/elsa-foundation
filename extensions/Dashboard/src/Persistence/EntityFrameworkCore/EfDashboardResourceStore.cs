using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Persistence.EntityFramework.SchemaFinalization;

namespace Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;

internal sealed class EfDashboardResourceStore<TContext>(TContext configuredContext, IServiceProvider services) : IDashboardResourceStore
    where TContext : DbContext
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<DashboardResourceDocument?> FindAsync(string tenantId, string kind, string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = CreateContext();
            var key = Hash(tenantId, kind, id);
            var row = await context.Set<DashboardResourceRow>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == key && !item.Deleted, cancellationToken);
            return row is null ? null : Read(row, tenantId, kind, id);
        }
        catch (Exception error) when (EfRelationalExceptionClassifier.IsStoreBoundaryFailure(error)) { throw Unavailable(error); }
    }

    public async ValueTask<IReadOnlyList<DashboardResourceDocument>> ReadPageAsync(string tenantId, string kind, string? afterId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = CreateContext();
            var scope = Hash(tenantId);
            if (afterId?.Length > 128) throw new DashboardResourceException("validation", "The store continuation is invalid.");
            var afterKey = afterId is null ? null : Hash(tenantId, kind, afterId);
            var rows = await context.Set<DashboardResourceRow>().AsNoTracking()
                .Where(row => row.ScopeId == scope && row.Kind == kind && !row.Deleted && (afterKey == null || row.Id.CompareTo(afterKey) > 0))
                .OrderBy(row => row.Id).Take(16).ToArrayAsync(cancellationToken);
            return rows.Select(row => Read(row, tenantId, kind, null, includeHistory: false)).ToArray();
        }
        catch (Exception error) when (EfRelationalExceptionClassifier.IsStoreBoundaryFailure(error)) { throw Unavailable(error); }
    }

    public async ValueTask<DashboardResourceDocument?> FindCreatedAsync(string tenantId, string actorId, string kind,
        string operationKey, string fingerprint, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = CreateContext();
            var key = Hash(tenantId, actorId, kind, operationKey);
            var operation = await context.Set<DashboardResourceOperationRow>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == key, cancellationToken);
            if (operation is null) return null;
            EnsureCurrentMetadata(operation.SchemaVersion);
            if (operation.Fingerprint != fingerprint) throw new DashboardResourceException("conflict", "This operation key was already used for a different request.");
            var row = await context.Set<DashboardResourceRow>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == operation.ResourceKey, cancellationToken);
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
                await using var context = CreateContext();
                if (write.OperationKey is { } repeatedKey && await FindCreatedAsync(document.TenantId, document.OwnerId,
                    document.Kind, repeatedKey, write.OperationFingerprint!, cancellationToken) is { } repeated)
                    return new("replayed", repeated);
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var scopeKey = Hash(document.TenantId);
                var scope = await context.Set<DashboardResourceScopeRow>().SingleOrDefaultAsync(row => row.Id == scopeKey, cancellationToken);
                if (scope is null) context.Set<DashboardResourceScopeRow>().Add(scope = new() { Id = scopeKey, Revision = Guid.NewGuid().ToString("N"),
                    SchemaVersion = DashboardResourcesEfModule.Chain.CurrentVersion });
                else
                {
                    EnsureCurrentMetadata(scope.SchemaVersion);
                    scope.Revision = Guid.NewGuid().ToString("N");
                }
                var key = Hash(document.TenantId, document.Kind, document.Id);
                var row = await context.Set<DashboardResourceRow>().SingleOrDefaultAsync(item => item.Id == key, cancellationToken);
                if (write.ExpectedRevision is null)
                {
                    if (row is not null) return Status("conflict");
                    var ownerKey = Hash(document.TenantId, document.OwnerId);
                    var ownerMaximum = document.Kind == DashboardResourceKinds.Question ? 100 : 50;
                    var tenantMaximum = document.Kind == DashboardResourceKinds.Question ? 5_000 : 2_000;
                    if (await context.Set<DashboardResourceRow>().CountAsync(item => item.ScopeId == scopeKey && item.Kind == document.Kind && !item.Deleted, cancellationToken) >= tenantMaximum ||
                        await context.Set<DashboardResourceRow>().CountAsync(item => item.ScopeId == scopeKey && item.OwnerKey == ownerKey && item.Kind == document.Kind && !item.Deleted, cancellationToken) >= ownerMaximum)
                        throw new DashboardResourceException("validation", "The resource capacity has been reached. Remove an owned resource or contact the administrator.");
                    row = new() { Id = key, ScopeId = scopeKey, OwnerKey = ownerKey, Kind = document.Kind };
                    context.Set<DashboardResourceRow>().Add(row);
                }
                else
                {
                    if (row is null || row.Deleted) return Status("notFound");
                    var stored = Read(row, document.TenantId, document.Kind, document.Id);
                    if (row.Revision != write.ExpectedRevision || stored.OwnerId != document.OwnerId) return Status("conflict");
                }
                row.Revision = write.Delete ? $"deleted-{Guid.NewGuid():N}" : document.Revision;
                row.DocumentJson = JsonSerializer.Serialize(document, Json);
                row.SchemaVersion = DashboardResourcesEfModule.Chain.CurrentVersion;
                row.Deleted = write.Delete;
                if (write.OperationKey is { } operationKey)
                    context.Set<DashboardResourceOperationRow>().Add(new() { Id = Hash(document.TenantId, document.OwnerId, document.Kind, operationKey), ResourceKey = key,
                        Fingerprint = write.OperationFingerprint!, SchemaVersion = DashboardResourcesEfModule.Chain.CurrentVersion });
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new("saved", document);
            }
            catch (Exception error) when (EfRelationalExceptionClassifier.IsWriteConflict(error, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient))
            {
                if (attempt == 2) return new("conflict");
            }
            catch (DashboardResourceException error)
            {
                if (error.Code == "validation" && write.OperationKey is { } operationKey &&
                    await FindCreatedAsync(document.TenantId, document.OwnerId, document.Kind, operationKey,
                        write.OperationFingerprint!, cancellationToken) is { } winner)
                    return new("replayed", winner);
                throw;
            }
            catch (OperationCanceledException)
            { throw; }
            catch (Exception error) when (EfRelationalExceptionClassifier.IsStoreBoundaryFailure(error))
            { throw Unavailable(error); }
        }
        return new("conflict");
    }


    private DbContext CreateContext()
    {
        try
        {
            var options = (DbContextOptions)configuredContext.GetService<IDbContextOptions>();
            if (System.Transactions.Transaction.Current is not null ||
                options.Extensions.OfType<RelationalOptionsExtension>().Any(extension => extension.Connection is not null))
                throw new InvalidOperationException("Dashboard resource operations require independently owned connection-string options outside an ambient transaction.");
            var provider = configuredContext.Database.ProviderName;
            if (provider is not (EfProviderNames.Sqlite or EfProviderNames.SqlServer or EfProviderNames.PostgreSql or EfProviderNames.MySql))
                throw new InvalidOperationException("Dashboard resources require a supported relational provider.");
            foreach (var entity in new[] { typeof(DashboardResourceRow), typeof(DashboardResourceScopeRow), typeof(DashboardResourceOperationRow) })
                if (configuredContext.Model.FindEntityType(entity) is null)
                    throw new InvalidOperationException("Install the Dashboard resource model in the consumer context before using its store.");
            var builder = new DbContextOptionsBuilder(options);
            EfSchemaWriteGateInterceptor.EnsureAdded(builder);
            // The same provider, model, connection and host dependencies, with an owned tracker and
            // transaction. A store operation never saves, clears or enlists the caller's unit of work.
            return (DbContext)ActivatorUtilities.CreateInstance(services, configuredContext.GetType(), builder.Options);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        { throw Unavailable(error); }
    }

    private static void EnsureCurrentMetadata(string? version)
    {
        try { EfSchemaVersion.EnsureCurrent(DashboardResourcesEfModule.Chain, version); }
        catch (EfSchemaVersionSkewException error)
        { throw new DashboardResourceException("schemaIncompatible", "Stored Dashboard operation or quota metadata uses a schema this backend cannot read. Run a compatible backend before retrying.", error); }
    }

    private static string WithoutHistory(string text)
    {
        using var payload = JsonDocument.Parse(text);
        if (payload.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid resource document.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in payload.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals("history", StringComparison.OrdinalIgnoreCase)) property.WriteTo(writer);
                else
                {
                    if (property.Value.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid resource history.");
                    writer.WriteStartArray(property.Name); writer.WriteEndArray();
                }
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static DashboardResourceDocument Read(DashboardResourceRow row, string tenantId, string kind, string? id, bool includeHistory = true)
    {
        try { EfSchemaVersion.EnsureReadable(DashboardResourcesEfModule.Chain, row.SchemaVersion); }
        catch (EfSchemaVersionSkewException error)
        {
            throw new DashboardResourceException("schemaIncompatible",
                "This resource uses a storage schema this backend cannot read. Run a compatible backend build before retrying.", error);
        }
        var text = DashboardResourcesEfModule.Chain.Upcast<DashboardResourceRow>(row.SchemaVersion,
            (nameof(DashboardResourceRow.DocumentJson), row.DocumentJson))[nameof(DashboardResourceRow.DocumentJson)]!;
        DashboardResourceDocument document;
        try
        {
            if (!includeHistory) text = WithoutHistory(text);
            document = JsonSerializer.Deserialize<DashboardResourceDocument>(text, Json)
                ?? throw new DashboardResourceException("unavailable", "The stored resource is invalid.");
        }
        catch (JsonException error) { throw new DashboardResourceException("unavailable", "The stored resource is invalid.", error); }
        if (document.Content is null || string.IsNullOrWhiteSpace(document.Content.Name) || document.Content.Tiles is null ||
            document.Audience is null || document.History is null ||
            document.TenantId != tenantId || document.Kind != kind || id is not null && document.Id != id ||
            row.Id != Hash(document.TenantId, document.Kind, document.Id) || row.ScopeId != Hash(tenantId) ||
            row.Kind != kind || row.Revision != document.Revision || row.OwnerKey != Hash(tenantId, document.OwnerId))
            throw new DashboardResourceException("unavailable", "The stored resource scope is invalid.");
        return document;
    }

    private static string Hash(params string[] parts) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(parts.Select(part => $"{part.Length}:{part}")))));
    private static DashboardResourceWriteResult Status(string status) => new(status);
    private static DashboardResourceException Unavailable(Exception inner) => new("unavailable", "Dashboard resource persistence is unavailable. Retry or contact the administrator.", inner);
}

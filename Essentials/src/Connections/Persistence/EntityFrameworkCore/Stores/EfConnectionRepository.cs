using System.Data.Common;
using System.Text.Json;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Connections.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfConnectionRepository(DbContext context) : IConnectionRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DbSet<ConnectionRecord> Connections => context.Set<ConnectionRecord>();
    private DbSet<ConnectionSecretReferenceRecord> SecretReferences => context.Set<ConnectionSecretReferenceRecord>();

    public ValueTask<ConnectionRepositoryCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = context.Database.ProviderName ?? "unconfigured";
        var durable = provider is EfProviderNames.SqlServer or EfProviderNames.PostgreSql or EfProviderNames.MySql;
        if (provider == EfProviderNames.Sqlite)
        {
            var connection = context.Database.GetDbConnection();
            var factory = DbProviderFactories.GetFactory(connection);
            if (factory is null) return ValueTask.FromResult(new ConnectionRepositoryCapabilities(provider, false));
            var builder = factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
            builder.ConnectionString = connection.ConnectionString;
            var source = new[] { "Data Source", "DataSource", "Filename" }.Select(key => builder.TryGetValue(key, out var value) ? value?.ToString() : null)
                .FirstOrDefault(value => value is not null) ?? connection.DataSource;
            var memory = builder.TryGetValue("Mode", out var mode) && string.Equals(mode?.ToString(), "Memory", StringComparison.OrdinalIgnoreCase);
            var path = source;
            if (source.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                // SQLite decodes percent escapes in URI keys/values and discards fragments.
                // An omitted path is a temporary database, even without mode=memory.
                var uri = source[5..].Split('#', 2)[0];
                var parts = uri.Split('?', 2);
                path = parts[0];
                if (path.StartsWith("//", StringComparison.Ordinal))
                {
                    var slash = path.IndexOf('/', 2);
                    path = slash < 0 ? "" : path[slash..];
                }
                path = Uri.UnescapeDataString(path);
                if (parts.Length == 2)
                    foreach (var parameter in parts[1].Split('&'))
                    {
                        var pair = parameter.Split('=', 2);
                        var key = Uri.UnescapeDataString(pair[0]);
                        var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "";
                        memory |= key.Equals("mode", StringComparison.OrdinalIgnoreCase) && value.Equals("memory", StringComparison.OrdinalIgnoreCase);
                        // Custom VFS persistence is not established by this provider profile.
                        memory |= key.Equals("vfs", StringComparison.OrdinalIgnoreCase) || key.Contains('\0') || value.Contains('\0');
                    }
            }
            memory |= path.Equals(":memory:", StringComparison.OrdinalIgnoreCase) || path.Contains('\0');
            durable = !string.IsNullOrWhiteSpace(path) && !memory;
        }
        return ValueTask.FromResult(new ConnectionRepositoryCapabilities(provider, durable));
    }

    public async ValueTask<ConnectionDocument?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ConnectionKeys.Tenant(tenantId);
        var key = ConnectionKeys.Name(name);
        try
        {
            var row = await Connections.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.NormalizedName == key, cancellationToken);
            return row is null ? null : Read(row);
        }
        catch (OperationCanceledException) { throw; }
        catch (ConnectionStoreException) { throw; }
        catch (Exception exception) { throw Failure(name, exception); }
    }

    public async ValueTask<IReadOnlyList<ConnectionDocument>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default)
    {
        ConnectionKeys.Tenant(tenantId);
        var rows = Connections.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (query?.TypeName is { Length: > 0 } type)
        {
            var key = ConnectionKeys.Fold(type);
            rows = rows.Where(x => x.TypeLookupKey == key);
        }
        if (query?.AuthScheme is { Length: > 0 } scheme)
        {
            var key = ConnectionKeys.Fold(scheme);
            rows = rows.Where(x => x.SchemeLookupKey == key);
        }
        if (query?.Search is { Length: > 0 } search)
        {
            var key = ConnectionKeys.Fold(search);
            rows = rows.Where(x => x.NameSearchKey.Contains(key) || x.DisplayNameSearchKey.Contains(key));
        }
        var take = query is { PageSize: > 0 } ? Math.Min(query.PageSize, 250) : 50;
        try { return (await rows.OrderBy(x => x.NormalizedName).Take(take).ToListAsync(cancellationToken)).Select(Read).ToArray(); }
        catch (OperationCanceledException) { throw; }
        catch (ConnectionStoreException) { throw; }
        catch (Exception exception) { throw Failure("connection", exception); }
    }

    public async ValueTask<bool> IsSecretReferencedAsync(string tenantId, string secretName, string? excludedConnectionName = null, CancellationToken cancellationToken = default)
    {
        ConnectionKeys.Tenant(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        var secret = ConnectionKeys.Secret(secretName);
        var excluded = excludedConnectionName is null ? null : ConnectionKeys.Name(excludedConnectionName);
        try
        {
            // An unreadable document cannot establish that deleting its referenced secret is safe.
            if (await Connections.AnyAsync(x => x.TenantId == tenantId && x.SchemaVersion != ConnectionsEfModule.SchemaVersion, cancellationToken))
                throw new ConnectionStoreException("connection", "Connection references cannot be inspected in an unsupported stored schema.", new InvalidDataException());
            return await SecretReferences.AnyAsync(x => x.TenantId == tenantId && x.SecretLookupHash == secret &&
                (excluded == null || x.NormalizedName != excluded), cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (ConnectionStoreException) { throw; }
        catch (Exception exception) { throw Failure("connection", exception); }
    }

    public async ValueTask<bool> TryAddAsync(ConnectionDocument document, CancellationToken cancellationToken = default)
    {
        var row = Prepare(document, increment: false);
        try
        {
            await using var transaction = await EfSharedTransaction.BeginAsync([context], cancellationToken);
            var owned = transaction.Context<DbContext>();
            owned.Set<ConnectionRecord>().Add(row);
            owned.Set<ConnectionSecretReferenceRecord>().AddRange(References(row));
            await owned.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey)) { return false; }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw Failure(document.Definition.Name, exception); }
    }

    public async ValueTask SaveAsync(ConnectionDocument document, CancellationToken cancellationToken = default)
    {
        var row = Prepare(document, increment: true);
        var expectedVersion = row.Version - 1;
        try
        {
            await using var transaction = await EfSharedTransaction.BeginAsync([context], cancellationToken);
            var owned = transaction.Context<DbContext>();
            var changed = await owned.Set<ConnectionRecord>().Where(x => x.TenantId == row.TenantId && x.NormalizedName == row.NormalizedName &&
                x.Version == expectedVersion && x.SchemaVersion == ConnectionsEfModule.SchemaVersion).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Version, row.Version).SetProperty(x => x.Payload, row.Payload)
                .SetProperty(x => x.NameSearchKey, row.NameSearchKey).SetProperty(x => x.DisplayNameSearchKey, row.DisplayNameSearchKey)
                .SetProperty(x => x.TypeLookupKey, row.TypeLookupKey).SetProperty(x => x.SchemeLookupKey, row.SchemeLookupKey), cancellationToken);
            if (changed != 1) throw new ConnectionConflictException(document.Definition.Name, "The connection changed before its metadata could be saved.");
            await owned.Set<ConnectionSecretReferenceRecord>().Where(x => x.TenantId == row.TenantId && x.NormalizedName == row.NormalizedName).ExecuteDeleteAsync(cancellationToken);
            owned.Set<ConnectionSecretReferenceRecord>().AddRange(References(row));
            await owned.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (ConnectionConflictException) { throw; }
        catch (Exception exception) { throw Failure(document.Definition.Name, exception); }
    }

    public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default) => DeleteCoreAsync(tenantId, name, null, cancellationToken);
    public ValueTask<bool> DeleteAsync(string tenantId, string name, int expectedVersion, CancellationToken cancellationToken = default) => DeleteCoreAsync(tenantId, name, expectedVersion, cancellationToken);

    private async ValueTask<bool> DeleteCoreAsync(string tenantId, string name, int? expectedVersion, CancellationToken cancellationToken)
    {
        ConnectionKeys.Tenant(tenantId);
        var key = ConnectionKeys.Name(name);
        try
        {
            await using var transaction = await EfSharedTransaction.BeginAsync([context], cancellationToken);
            var owned = transaction.Context<DbContext>();
            var changed = await owned.Set<ConnectionRecord>().Where(x => x.TenantId == tenantId && x.NormalizedName == key && x.SchemaVersion == ConnectionsEfModule.SchemaVersion &&
                (!expectedVersion.HasValue || x.Version == expectedVersion.Value)).ExecuteDeleteAsync(cancellationToken);
            // Keep reference cleanup atomic even when a SQLite host disabled FK enforcement.
            if (changed == 1)
                await owned.Set<ConnectionSecretReferenceRecord>().Where(x => x.TenantId == tenantId && x.NormalizedName == key).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return changed == 1;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw Failure(name, exception); }
    }

    private static ConnectionRecord Prepare(ConnectionDocument document, bool increment)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Definition);
        ArgumentNullException.ThrowIfNull(document.State);
        var snapshot = Clone(document);
        ConnectionKeys.Tenant(snapshot.Definition.TenantId);
        var key = ConnectionKeys.Name(snapshot.Definition.Name);
        if (increment) snapshot.State.Version = checked(snapshot.State.Version + 1);
        return new()
        {
            TenantId = snapshot.Definition.TenantId, NormalizedName = key, Version = snapshot.State.Version,
            SchemaVersion = ConnectionsEfModule.SchemaVersion, Payload = JsonSerializer.Serialize(snapshot, Json),
            NameSearchKey = ConnectionKeys.Fold(snapshot.Definition.Name), DisplayNameSearchKey = ConnectionKeys.Fold(snapshot.Definition.DisplayName),
            TypeLookupKey = ConnectionKeys.Fold(snapshot.Definition.TypeName), SchemeLookupKey = ConnectionKeys.Fold(snapshot.Definition.AuthScheme)
        };
    }

    private static ConnectionDocument Read(ConnectionRecord row)
    {
        try
        {
            EfSchemaVersion.EnsureReadable(ConnectionsEfModule.Chain, row.SchemaVersion);
            var document = JsonSerializer.Deserialize<ConnectionDocument>(row.Payload, Json) ?? throw new InvalidDataException();
            if (document.Definition.TenantId != row.TenantId || ConnectionKeys.Name(document.Definition.Name) != row.NormalizedName || document.State.Version != row.Version)
                throw new InvalidDataException("The connection payload does not match its persisted identity and version.");
            return Clone(document);
        }
        catch (Exception exception) { throw Failure(string.IsNullOrWhiteSpace(row.NormalizedName) ? "connection" : row.NormalizedName, exception); }
    }

    private static ConnectionDocument Clone(ConnectionDocument document)
    {
        var copy = JsonSerializer.Deserialize<ConnectionDocument>(JsonSerializer.Serialize(document, Json), Json)!;
        copy.Definition.Settings = new Dictionary<string, string>(copy.Definition.Settings, StringComparer.OrdinalIgnoreCase);
        copy.Definition.SecretFields = new Dictionary<string, ConnectionSecretField>(copy.Definition.SecretFields, StringComparer.Ordinal);
        return copy;
    }

    private static IEnumerable<ConnectionSecretReferenceRecord> References(ConnectionRecord row) =>
        JsonSerializer.Deserialize<ConnectionDocument>(row.Payload, Json)!.Definition.SecretFields.Values.Where(x => x.SecretName is not null).Select(x => ConnectionKeys.Secret(x.SecretName!)).Distinct(StringComparer.Ordinal)
            .Select(key => new ConnectionSecretReferenceRecord { TenantId = row.TenantId, NormalizedName = row.NormalizedName, SecretLookupHash = key });
    private static ConnectionStoreException Failure(string name, Exception cause) => new(name, "The connection metadata store could not complete the operation.", cause);
}

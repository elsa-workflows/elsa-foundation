using System.Collections.Concurrent;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Services;

internal sealed class InMemoryConnectionRepository : IConnectionRepository
{
    public ValueTask<ConnectionRepositoryCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ConnectionRepositoryCapabilities("in-memory", false));
    }

    private readonly ConcurrentDictionary<string, ConnectionDocument> _documents = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask<ConnectionDocument?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        Validate(tenantId, name);
        cancellationToken.ThrowIfCancellationRequested();
        _documents.TryGetValue(Key(tenantId, name), out var document);
        return new(Clone(document));
    }

    public ValueTask<bool> IsSecretReferencedAsync(
        string tenantId,
        string secretName,
        string? excludedConnectionName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        cancellationToken.ThrowIfCancellationRequested();

        var isReferenced = _documents.Values.Any(document =>
            string.Equals(document.Definition.TenantId, tenantId, StringComparison.Ordinal) &&
            !string.Equals(document.Definition.Name, excludedConnectionName, StringComparison.OrdinalIgnoreCase) &&
            document.Definition.SecretFields.Values.Any(field =>
                string.Equals(field.SecretName, secretName, StringComparison.OrdinalIgnoreCase)));

        return new(isReferenced);
    }

    public ValueTask<IReadOnlyList<ConnectionDocument>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<ConnectionDocument> items = _documents.Values
            .Where(document => string.Equals(document.Definition.TenantId, tenantId, StringComparison.Ordinal));

        if (query?.TypeName is { Length: > 0 } typeName)
            items = items.Where(document => string.Equals(document.Definition.TypeName, typeName, StringComparison.OrdinalIgnoreCase));
        if (query?.AuthScheme is { Length: > 0 } scheme)
            items = items.Where(document => string.Equals(document.Definition.AuthScheme, scheme, StringComparison.OrdinalIgnoreCase));
        if (query?.Search is { Length: > 0 } search)
        {
            items = items.Where(document =>
                document.Definition.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                document.Definition.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var pageSize = query is { PageSize: > 0 } ? Math.Min(query.PageSize, 250) : 50;
        var list = items
            .OrderBy(document => document.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .Take(pageSize)
            .Select(Clone)
            .Where(document => document is not null)
            .Cast<ConnectionDocument>()
            .ToArray();
        return new(list);
    }

    public ValueTask<bool> TryAddAsync(ConnectionDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document.Definition.TenantId, document.Definition.Name);
        return new(_documents.TryAdd(Key(document.Definition.TenantId, document.Definition.Name), Clone(document)!));
    }

    public ValueTask SaveAsync(ConnectionDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document.Definition.TenantId, document.Definition.Name);
        var key = Key(document.Definition.TenantId, document.Definition.Name);
        var next = Clone(document)!;
        next.State.Version = document.State.Version + 1;
        while (true)
        {
            if (!_documents.TryGetValue(key, out var current))
            {
                throw new ConnectionConflictException(
                    document.Definition.Name,
                    $"Connection '{document.Definition.Name}' was changed by another request.");
            }

            if (current.State.Version != document.State.Version)
            {
                throw new ConnectionConflictException(
                    document.Definition.Name,
                    $"Connection '{document.Definition.Name}' was changed by another request.");
            }

            if (_documents.TryUpdate(key, next, current))
                return ValueTask.CompletedTask;
        }
    }

    public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        Validate(tenantId, name);
        return new(_documents.TryRemove(Key(tenantId, name), out _));
    }

    public ValueTask<bool> DeleteAsync(string tenantId, string name, int expectedVersion, CancellationToken cancellationToken = default)
    {
        Validate(tenantId, name);
        var key = Key(tenantId, name);
        while (_documents.TryGetValue(key, out var current))
        {
            if (current.State.Version != expectedVersion)
                return new(false);

            // Removes the entry only while it is still the one read, so a concurrent save or re-add is never deleted.
            if (_documents.TryRemove(new KeyValuePair<string, ConnectionDocument>(key, current)))
                return new(true);
        }

        return new(false);
    }

    private static void Validate(string tenantId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
    }

    private static string Key(string tenantId, string name) => $"{tenantId}\u001f{name.Trim().ToLowerInvariant()}";

    private static ConnectionDocument? Clone(ConnectionDocument? document)
    {
        if (document is null)
            return null;

        return new ConnectionDocument
        {
            Definition = new ConnectionDefinition
            {
                TenantId = document.Definition.TenantId,
                Name = document.Definition.Name,
                DisplayName = document.Definition.DisplayName,
                TypeName = document.Definition.TypeName,
                AuthScheme = document.Definition.AuthScheme,
                Settings = new Dictionary<string, string>(document.Definition.Settings, StringComparer.OrdinalIgnoreCase),
                SecretFields = document.Definition.SecretFields.ToDictionary(
                    pair => pair.Key,
                    pair => new ConnectionSecretField
                    {
                        FieldName = pair.Value.FieldName,
                        SourceKind = pair.Value.SourceKind,
                        SecretName = pair.Value.SecretName,
                        IsOwnedSecret = pair.Value.IsOwnedSecret,
                        NamedCredentialName = pair.Value.NamedCredentialName
                    },
                    StringComparer.Ordinal),
                ManagedBy = document.Definition.ManagedBy,
                Owner = document.Definition.Owner,
                Tags = [..document.Definition.Tags]
            },
            State = new ConnectionState
            {
                Status = document.State.Status,
                GrantSecretName = document.State.GrantSecretName,
                GrantSecretVersion = document.State.GrantSecretVersion,
                LastTestedAt = document.State.LastTestedAt,
                CreatedAt = document.State.CreatedAt,
                UpdatedAt = document.State.UpdatedAt,
                Version = document.State.Version
            }
        };
    }
}

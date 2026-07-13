using System.Collections.Concurrent;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

public sealed class InMemorySecretRepository : ISecretRepository
{
    private readonly ConcurrentDictionary<string, Secret> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);
        _secrets.TryGetValue(Key(tenantId, normalizedName), out var secret);
        return new(secret);
    }

    public ValueTask<IReadOnlyCollection<Secret>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);
        var secrets = _secrets.Values
            .Where(x => string.Equals(x.TenantId, tenantId, StringComparison.Ordinal))
            .ToArray();
        return new(secrets);
    }

    public ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        Validate(secret);
        var added = _secrets.TryAdd(Key(secret.TenantId, secret.Name), secret);
        return new(added);
    }

    public ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        Validate(secret);
        _secrets[Key(secret.TenantId, secret.Name)] = secret;
        return ValueTask.CompletedTask;
    }

    private static string Key(string tenantId, string name) => $"{tenantId.Length}:{tenantId}{name}";

    private static void Validate(Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ValidateTenantId(secret.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret.Name);
    }

    private static void ValidateTenantId(string tenantId) => ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
}

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
        SecretNameConstraints.Validate(normalizedName);
        _secrets.TryGetValue(Key(tenantId, normalizedName), out var secret);
        return new(secret);
    }

    public ValueTask<SecretRepositoryPage> ListPageAsync(
        string tenantId,
        SecretRepositoryListRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var filtered = _secrets.Values.Where(secret => string.Equals(secret.TenantId, tenantId, StringComparison.Ordinal));
        if (request.Status is not null)
            filtered = filtered.Where(secret => secret.Status == request.Status);
        if (request.ExcludedStatus is not null)
            filtered = filtered.Where(secret => secret.Status != request.ExcludedStatus);
        if (request.Search is not null)
        {
            filtered = filtered.Where(secret =>
                secret.Name.Contains(request.Search, StringComparison.OrdinalIgnoreCase) ||
                secret.DisplayName.Contains(request.Search, StringComparison.OrdinalIgnoreCase));
        }
        if (request.TypeName is not null)
            filtered = filtered.Where(secret => StringComparer.OrdinalIgnoreCase.Equals(secret.TypeName, request.TypeName));
        if (request.TypeNames.Count > 0)
            filtered = filtered.Where(secret => request.TypeNames.Contains(secret.TypeName, StringComparer.OrdinalIgnoreCase));
        if (request.StoreName is not null)
            filtered = filtered.Where(secret => StringComparer.OrdinalIgnoreCase.Equals(secret.StoreName, request.StoreName));
        if (request.StoreNames.Count > 0)
            filtered = filtered.Where(secret => request.StoreNames.Contains(secret.StoreName, StringComparer.OrdinalIgnoreCase));
        if (request.Scope is not null)
            filtered = filtered.Where(secret => StringComparer.OrdinalIgnoreCase.Equals(secret.Scope, request.Scope));
        if (request.ActiveOnly)
        {
            var now = request.Now!.Value;
            filtered = filtered.Where(secret =>
                secret.Status == SecretStatus.Active &&
                secret.Versions.Any(version =>
                    version.Status == SecretStatus.Active &&
                    !version.IsExpired(now)));
        }

        var ordered = filtered.OrderBy(secret => secret.Name, StringComparer.Ordinal).ToArray();
        var items = ordered.Skip(request.Skip).Take(request.Take).ToArray();
        return ValueTask.FromResult(new SecretRepositoryPage(items, ordered.LongLength));
    }

    public ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        SecretNameConstraints.Validate(secret.Name);
        ValidateTenantId(secret.TenantId);
        var added = _secrets.TryAdd(Key(secret.TenantId, secret.Name), secret);
        return new(added);
    }

    public ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        SecretNameConstraints.Validate(secret.Name);
        ValidateTenantId(secret.TenantId);
        _secrets[Key(secret.TenantId, secret.Name)] = secret;
        return ValueTask.CompletedTask;
    }

    private static string Key(string tenantId, string name) => $"{tenantId.Length}:{tenantId}{name}";

    private static void ValidateTenantId(string tenantId) => ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
}

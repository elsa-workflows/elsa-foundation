using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

public sealed class InMemorySecretRepository : ISecretRepository
{
    private readonly Lock _gate = new();

    // The secret holding each tenant's name, and beside it every secret deleted under that name, by id. A soft delete
    // moves a secret from the first to the second, which frees the name while the deleted secret keeps its history.
    private readonly Dictionary<string, Secret> _secrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, Secret>> _deleted = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);
        SecretNameConstraints.Validate(normalizedName);
        var key = Key(tenantId, normalizedName);
        lock (_gate)
        {
            if (_secrets.TryGetValue(key, out var secret))
                return new(secret);

            // No secret holds the name: the deleted one with the greatest id (for generated ids, the latest created)
            // still tells a deleted secret from one that never existed.
            return new(_deleted.TryGetValue(key, out var deleted)
                ? deleted.Values.MaxBy(candidate => candidate.Id, StringComparer.Ordinal)
                : null);
        }
    }

    public ValueTask<SecretRepositoryPage> ListPageAsync(
        string tenantId,
        SecretRepositoryListRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        Secret[] snapshot;
        lock (_gate)
            snapshot = [.. _secrets.Values, .. _deleted.Values.SelectMany(deleted => deleted.Values)];

        var filtered = snapshot.Where(secret => string.Equals(secret.TenantId, tenantId, StringComparison.Ordinal));
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

        var ordered = filtered
            .OrderBy(secret => secret.Name, StringComparer.Ordinal)
            .ThenBy(secret => secret.Status == SecretStatus.Deleted)
            .ThenBy(secret => secret.Id, StringComparer.Ordinal)
            .ToArray();
        var items = ordered.Skip(request.Skip).Take(request.Take).ToArray();
        return ValueTask.FromResult(new SecretRepositoryPage(items, ordered.LongLength));
    }

    public ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(secret);
        var key = Key(secret.TenantId, secret.Name);
        lock (_gate)
        {
            if (secret.Status == SecretStatus.Deleted)
                return new(Deleted(key).TryAdd(secret.Id, secret));

            // Only the secret holding the name refuses a new one; secrets deleted under it do not.
            return new(_secrets.TryAdd(key, secret));
        }
    }

    public ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(secret);
        var key = Key(secret.TenantId, secret.Name);
        lock (_gate)
        {
            if (secret.Status != SecretStatus.Deleted)
            {
                _secrets[key] = secret;
                return ValueTask.CompletedTask;
            }

            // Deleting frees the name, unless another secret holds it by now: saving an earlier secret's deleted copy
            // never removes its successor.
            if (_secrets.TryGetValue(key, out var holder) && string.Equals(holder.Id, secret.Id, StringComparison.Ordinal))
                _secrets.Remove(key);
            Deleted(key)[secret.Id] = secret;
        }

        return ValueTask.CompletedTask;
    }

    private Dictionary<string, Secret> Deleted(string key)
    {
        if (!_deleted.TryGetValue(key, out var deleted))
            _deleted[key] = deleted = new Dictionary<string, Secret>(StringComparer.Ordinal);
        return deleted;
    }

    private static string Key(string tenantId, string name) => $"{tenantId.Length}:{tenantId}{name}";

    private static void ValidateIdentity(Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        SecretNameConstraints.Validate(secret.Name);
        ValidateTenantId(secret.TenantId);
        // A deleted secret is kept by its id, so it needs one.
        if (secret.Status == SecretStatus.Deleted)
            ArgumentException.ThrowIfNullOrWhiteSpace(secret.Id, nameof(secret));
    }

    private static void ValidateTenantId(string tenantId) => ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
}

using System.Collections.Concurrent;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

public sealed class InMemorySecretRepository : ISecretRepository
{
    private readonly ConcurrentDictionary<string, Secret> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask<Secret?> FindAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        _secrets.TryGetValue(normalizedName, out var secret);
        return new(secret?.Status == SecretStatus.Deleted ? null : secret);
    }

    public ValueTask<IReadOnlyCollection<Secret>> ListAsync(CancellationToken cancellationToken = default)
    {
        var secrets = _secrets.Values.Where(x => x.Status != SecretStatus.Deleted).ToArray();
        return new(secrets);
    }

    public ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        var added = _secrets.TryAdd(secret.Name, secret);
        return new(added);
    }

    public ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        _secrets[secret.Name] = secret;
        return ValueTask.CompletedTask;
    }
}

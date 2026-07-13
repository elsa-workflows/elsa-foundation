using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

public interface ISecretRepository
{
    ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<Secret>> ListAsync(string tenantId, CancellationToken cancellationToken = default);
    ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default);
}

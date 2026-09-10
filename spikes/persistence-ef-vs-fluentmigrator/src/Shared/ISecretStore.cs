namespace Elsa.Persistence.Spike;

public interface ISecretStore
{
    Task AddAsync(SecretRecord record, CancellationToken cancellationToken = default);
    Task<SecretRecord?> GetByNameAsync(string tenantId, string name, CancellationToken cancellationToken = default);
}

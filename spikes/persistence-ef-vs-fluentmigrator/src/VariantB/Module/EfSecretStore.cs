using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.Spike.VariantB;

public sealed class EfSecretStore(SecretsDbContext context) : ISecretStore
{
    public async Task AddAsync(SecretRecord record, CancellationToken cancellationToken = default)
    {
        context.Secrets.Add(record);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<SecretRecord?> GetByNameAsync(string tenantId, string name, CancellationToken cancellationToken = default) =>
        context.Secrets.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Name == name,
            cancellationToken);
}

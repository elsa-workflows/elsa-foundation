using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.Spike.VariantA;

public static class VariantAMigrations
{
    public static async Task ApplyAsync(SecretsDbContext context, string expectedProviderName, CancellationToken cancellationToken = default)
    {
        ProviderGuard.Ensure(context.Database.ProviderName, expectedProviderName, context.GetType().Name);

        // EF Core 9+ IMigrator.MigrateAsync acquires IHistoryRepository.AcquireDatabaseLockAsync
        // (advisory lock / app lock) around the history table so concurrent hosts do not race.
        // Calling Database.MigrateAsync is enough to participate; do not roll a second lock.
        await context.Database.MigrateAsync(cancellationToken);
    }
}

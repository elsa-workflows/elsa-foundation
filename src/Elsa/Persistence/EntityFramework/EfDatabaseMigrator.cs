using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Applies or validates pending migrations after the provider guard.
/// <see cref="DatabaseFacade.MigrateAsync"/> already takes
/// <c>IHistoryRepository.AcquireDatabaseLockAsync</c> (EF 9+). Hosts that call
/// <c>IMigrator.Migrate</c> or apply pending migrations themselves must take that same lock;
/// wrapping <see cref="DatabaseFacade.MigrateAsync"/> in a second lock is not required and races.
/// </summary>
public static class EfDatabaseMigrator
{
    public static async Task ApplyAsync(
        DbContext context,
        string expectedProviderName,
        EfMigratePolicy policy = EfMigratePolicy.AutoMigrate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        EfProviderGuard.Ensure(context, expectedProviderName);

        switch (policy)
        {
            case EfMigratePolicy.AutoMigrate:
                await context.Database.MigrateAsync(cancellationToken);
                return;
            case EfMigratePolicy.Validate:
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
                if (pending.Length == 0)
                    return;
                throw new InvalidOperationException(
                    $"{context.GetType().Name} has pending migrations: {string.Join(", ", pending)}. " +
                    "Apply them out of process or set the migrate policy to AutoMigrate.");
            default:
                throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown EF migrate policy.");
        }
    }
}

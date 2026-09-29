using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Applies or validates pending migrations after the provider guard.
/// <see cref="DatabaseFacade.MigrateAsync"/> already takes
/// <c>IHistoryRepository.AcquireDatabaseLockAsync</c> (EF 9+). Hosts that call
/// <c>IMigrator.Migrate</c> or apply pending migrations themselves must take that same lock;
/// wrapping <see cref="DatabaseFacade.MigrateAsync"/> in a second lock is not required and races.
/// </summary>
/// <remarks>
/// Under both policies a pending contracting migration whose schema family is not yet finalized at the version its
/// opt-out names refuses the context's whole pending batch before anything runs, with
/// <see cref="EfContractingMigrationRefusedException"/> (spec 185, FR-024): <see cref="DatabaseFacade.MigrateAsync"/>
/// cannot apply some of a context's pending migrations and withhold others. Every host's module migrator and the
/// persistence tool's <c>apply</c> and <c>validate</c> come through here, so none of them can skip the check.
/// </remarks>
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
                if (await EfContractingMigrationCheck.FindRefusalAsync(context, pending: null, cancellationToken) is { } refusal)
                    throw refusal;
                await context.Database.MigrateAsync(cancellationToken);
                return;
            case EfMigratePolicy.Validate:
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
                if (pending.Length == 0)
                    return;
                // A pending batch that could not be applied either is reported as such, naming the family and version it
                // waits for, rather than pointing the operator at an `apply` that would refuse it too.
                if (await EfContractingMigrationCheck.FindRefusalAsync(context, pending, cancellationToken) is { } withheld)
                    throw withheld;
                throw new EfPendingMigrationsException(
                    $"{context.GetType().Name} has pending migrations: {string.Join(", ", pending)}. " +
                    "Apply them out of process (dotnet elsa persistence apply) or set " +
                    $"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)} to {nameof(EfMigratePolicy.AutoMigrate)}.");
            default:
                throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown EF migrate policy.");
        }
    }
}

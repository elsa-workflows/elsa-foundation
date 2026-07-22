using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Options;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elsa.Persistence.EFCore.Tasks;

/// <summary>
/// Executes EF Core migrations using the specified <see cref="ElsaDbContextBase"/> type.
/// </summary>
[UsedImplicitly]
[SingleNodeTask]
[Order(-100)]
public class RunMigrationsStartupTask<TDbContext>(IDbContextFactory<TDbContext> dbContextFactory, IMigrationsLockReclaimer migrationsLockReclaimer, IOptions<MigrationOptions> options) : IStartupTask
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        options.Value.RunMigrations.TryGetValue($"{typeof(TDbContext)}", out bool shouldRunMigrations);

        if (!shouldRunMigrations)
            return;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Reclaim a stale SQLite migrations lock left by a crashed/killed process before migrating. EF Core's
        // SQLite history repository guards MigrateAsync with a persistent __EFMigrationsLock row that is never
        // released if the owning process dies, so the next migration retries lock acquisition forever — the host
        // listens but the shell never activates (issue #949). This task holds the [SingleNodeTask] distributed
        // lock for TDbContext, so no peer is migrating this context concurrently.
        await migrationsLockReclaimer.ReclaimStaleLocksAsync(dbContext, options.Value.StaleMigrationLockTimeout, cancellationToken);

        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
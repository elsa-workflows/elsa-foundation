using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EFCore.Contracts;

/// <summary>
/// Reclaims a stale EF Core migrations lock before migrations run.
/// </summary>
/// <remarks>
/// EF Core's SQLite history repository guards migrations with a persistent <c>__EFMigrationsLock</c>
/// table row. Unlike SqlServer (<c>sp_getapplock</c>) or PostgreSQL (advisory locks), that row is NOT
/// released when the owning process dies: a crash or forced shutdown during a migration leaves it behind,
/// and every subsequent <see cref="RelationalDatabaseFacadeExtensions.MigrateAsync"/> then retries lock
/// acquisition forever — the host listens but the shell never activates (issue #949). This reclaimer
/// removes such an abandoned lock so startup converges.
/// </remarks>
public interface IMigrationsLockReclaimer
{
    /// <summary>
    /// Removes a migrations lock that has not been refreshed for at least <paramref name="staleAfter"/>.
    /// A no-op for providers whose migration lock the database releases automatically (SqlServer/PostgreSQL),
    /// for a database with no lock table yet, and for a lock whose timestamp is still within the window.
    /// </summary>
    /// <returns>The number of lock rows reclaimed (0 or 1).</returns>
    Task<int> ReclaimStaleLocksAsync(DbContext dbContext, TimeSpan staleAfter, CancellationToken cancellationToken = default);
}

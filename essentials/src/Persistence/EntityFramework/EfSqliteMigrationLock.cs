using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// <c>Database.MigrateAsync</c> for a SQLite database, without the two ways EF's migration lock hangs a start after a crash (#2196).
/// EF takes SQLite's lock by inserting a row into <c>__EFMigrationsLock</c> and deleting it when the migration ends, takes it on
/// every call even when nothing is pending, and retries for ever with no stale-lock timeout. A process killed in between leaves the
/// row behind, and every later start that migrates the file waits for it until someone deletes it by hand. The other providers
/// release their lock when the connection drops, so they need none of this and pass straight through.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing pending, nothing locked.</b> A database whose migrations are all applied is current, whoever holds the lock, and
/// migrations are only ever added by a new build, so the read cannot go stale into a missed migration: a migration's history
/// row is written only after its operations have run, so an empty pending list means every operation ran. <c>MigrateAsync</c>
/// would apply nothing, so it is not called. The row is not always committed atomically with the operations: a migration that
/// suppresses its transaction (SQLite table rebuilds do) writes it in a statement of its own after them. That does not weaken the
/// skip, because the row still comes last, but a process killed between the two leaves a migration pending whose operations
/// have run. The next start finds it pending, so it takes the lock and <c>MigrateAsync</c> runs it again, as it would have without
/// this class.
/// </para>
/// <para>
/// <b>A lock that outlives the bound fails the start; it is never removed.</b> With migrations pending, a held lock may be a live
/// migrator's, and EF's lock is a bare row: no file lock is held while it is held, so the database being unlocked or the file being
/// openable proves nothing about its holder, and a reclaimed row lets a second migrator in beside a live first one. The row's
/// <c>Timestamp</c> is when it was taken, so a lock younger than <see cref="DefaultStaleAfter"/> is waited for, as EF waits, and an
/// older one is reported with the way to clear it. A legitimate migration longer than the bound fails its waiting peers' start, which
/// an orchestrator retries, instead of letting them run beside it. One window stays: a peer that takes the lock between the
/// wait returning and EF's own acquire, and is killed there, still leaves EF waiting for it. Nothing here cancels a migration
/// that is running, so no watchdog can stop a legitimate one.
/// </para>
/// </remarks>
public static class EfSqliteMigrationLock
{
    /// <summary>How long a lock may be held before a start that waits for it reports it as stale.</summary>
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromMinutes(10);

    private const string LockTable = "__EFMigrationsLock";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Applies <paramref name="context"/>'s pending migrations as <c>Database.MigrateAsync</c> does, on SQLite only after the lock
    /// has been waited for, and not at all when none are pending.
    /// </summary>
    /// <param name="options">The host's migrate options; <see cref="EfMigrateOptions.SqliteMigrationLockStaleAfter"/> is how long a held lock is waited for before it is reported.</param>
    /// <exception cref="EfMigrationLockStaleException">Migrations are pending and the lock has been held longer than the bound.</exception>
    public static async Task MigrateAsync(DbContext context, EfMigrateOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        if (IsSqlite(context))
        {
            if (!(await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                return;
            await AwaitReleasedAsync(context, options, cancellationToken);
        }

        await context.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Returns once <paramref name="context"/>'s SQLite database holds no migration lock, which is immediately on any other provider.
    /// A lock taken after this returns is waited for by EF itself.
    /// </summary>
    internal static async Task AwaitReleasedAsync(DbContext context, EfMigrateOptions options, CancellationToken cancellationToken)
    {
        if (!IsSqlite(context))
            return;
        var bound = options.SqliteMigrationLockStaleAfter;
        var firstSeen = DateTimeOffset.UtcNow;
        while (await ReadLockAsync(context, cancellationToken) is { } held)
        {
            // An unreadable stamp ages from when this wait first saw the row, so a format EF changes still ends in a report.
            if (DateTimeOffset.UtcNow - (held.Since ?? firstSeen) > bound)
                throw new EfMigrationLockStaleException(context.Database.GetDbConnection().DataSource, held.Since, bound);
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static bool IsSqlite(DbContext context) =>
        string.Equals(context.Database.ProviderName, EfProviderNames.Sqlite, StringComparison.Ordinal);

    /// <summary>The lock the database holds, or null when it holds none. A database that does not exist yet holds none, and is not created by asking.</summary>
    private static async Task<HeldLock?> ReadLockAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (context.GetService<IRelationalDatabaseCreator>() is { } creator && !await creator.ExistsAsync(cancellationToken))
            return null;

        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{LockTable}'";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 0)
                return null;

            command.CommandText = $"SELECT \"Timestamp\" FROM \"{LockTable}\" WHERE \"Id\" = 1";
            if (await command.ExecuteScalarAsync(cancellationToken) is not { } stamp)
                return null;
            return new HeldLock(DateTimeOffset.TryParse(Convert.ToString(stamp, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var since) ? since : null);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <param name="Since">When the lock was taken; null when its stamp cannot be read.</param>
    private readonly record struct HeldLock(DateTimeOffset? Since);
}

/// <summary>
/// A SQLite migration lock has been held for longer than <see cref="EfSqliteMigrationLock.DefaultStaleAfter"/> (or the configured
/// <see cref="EfMigrateOptions.SqliteMigrationLockStaleAfter"/>) while migrations are pending. It is the lock a process leaves behind
/// when it is killed during a migration, or a live migration that has outlasted the bound; Elsa cannot tell which, so it removes nothing.
/// </summary>
public sealed class EfMigrationLockStaleException : InvalidOperationException
{
    internal EfMigrationLockStaleException(string dataSource, DateTimeOffset? lockedSince, TimeSpan staleAfter)
        : base(
            $"The SQLite database '{dataSource}' has had its EF migration lock (row Id = 1 of \"__EFMigrationsLock\") held " +
            (lockedSince is { } since ? $"since {since:u}, " : "") +
            $"for longer than {staleAfter}, and migrations are pending. A process killed during a migration leaves the lock behind. " +
            "If no migration is running against this database, remove it with: DELETE FROM \"__EFMigrationsLock\" WHERE \"Id\" = 1; " +
            $"then start again. A migration that legitimately takes longer needs {EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.SqliteMigrationLockStaleAfter)} raised.")
    {
        DataSource = dataSource;
        LockedSince = lockedSince;
    }

    /// <summary>The database whose lock is held.</summary>
    public string DataSource { get; }

    /// <summary>When the lock was taken, or null when its stamp could not be read.</summary>
    public DateTimeOffset? LockedSince { get; }
}

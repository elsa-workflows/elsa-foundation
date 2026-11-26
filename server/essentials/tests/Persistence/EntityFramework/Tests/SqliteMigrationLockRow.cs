using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// The row EF's SQLite history repository inserts into <c>__EFMigrationsLock</c> to take its migration lock, and leaves
/// behind when the process holding it is killed (#2196).
/// </summary>
internal static class SqliteMigrationLockRow
{
    /// <summary>Takes the lock as EF does, stamped <paramref name="takenAt"/>.</summary>
    public static async Task TakeAsync(DbContext context, DateTimeOffset takenAt)
    {
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsLock\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK___EFMigrationsLock\" PRIMARY KEY, \"Timestamp\" TEXT NOT NULL);");
        var stamp = takenAt.ToString("yyyy-MM-dd HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);
        await context.Database.ExecuteSqlAsync($"INSERT OR IGNORE INTO \"__EFMigrationsLock\"(\"Id\", \"Timestamp\") VALUES(1, {stamp});");
    }

    public static Task ReleaseAsync(DbContext context) =>
        context.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsLock\" WHERE \"Id\" = 1;");

    public static async Task<bool> IsHeldAsync(DbContext context) =>
        await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS \"Value\" FROM \"__EFMigrationsLock\"").SingleAsync() > 0;
}

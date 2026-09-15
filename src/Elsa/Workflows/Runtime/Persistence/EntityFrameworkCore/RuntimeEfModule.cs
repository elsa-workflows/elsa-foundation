using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>
/// Every Runtime EF participant shares one <see cref="BookmarkStateDbContext"/>, so they share one migration
/// set, one history table and one connection, whichever participant registers the context first.
/// </summary>
public static class RuntimeEfModule
{
    public const string HistoryModuleName = "ElsaRuntime";

    /// <summary>The <c>ConnectionStrings</c> entry every Runtime participant falls back to.</summary>
    public const string DefaultConnectionName = "Elsa";

    /// <summary>The database every Runtime participant uses on SQLite when no connection is configured.</summary>
    public const string DefaultSqliteConnectionString = "Data Source=elsa-runtime.db";

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}

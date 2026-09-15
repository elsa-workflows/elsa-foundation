using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>
/// Every Runtime EF participant shares one <see cref="BookmarkStateDbContext"/>, so they share one migration
/// set and one history table, whichever participant registers the context first.
/// </summary>
public static class RuntimeEfModule
{
    public const string HistoryModuleName = "ElsaRuntime";

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}

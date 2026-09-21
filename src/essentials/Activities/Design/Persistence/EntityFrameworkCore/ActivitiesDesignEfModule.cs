using Elsa.Persistence.EntityFramework;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore;

public static class ActivitiesDesignEfModule
{
    public const string HistoryModuleName = "ElsaActivitiesDesign";
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}

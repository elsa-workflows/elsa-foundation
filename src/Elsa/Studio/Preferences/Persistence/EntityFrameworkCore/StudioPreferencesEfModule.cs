using Elsa.Persistence.EntityFramework;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;

public static class StudioPreferencesEfModule
{
    public const string HistoryModuleName = "ElsaStudioPreferences";
    public const string TableName = "elsa_studio_preferences";
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}

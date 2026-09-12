using Elsa.Persistence.EntityFramework;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;

public static class StudioPreferencesEfModule
{
    public const string HistoryModuleName = "ElsaStudioPreferences";
    public const string TableName = "elsa_studio_preferences";
    public const string DefaultConnectionName = "ElsaStudioPreferences";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-studio-preferences.db";

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}

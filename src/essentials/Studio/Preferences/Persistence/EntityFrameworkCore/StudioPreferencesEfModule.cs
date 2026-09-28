using Elsa.Persistence.EntityFramework;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;

public static class StudioPreferencesEfModule
{
    public const string HistoryModuleName = "ElsaStudioPreferences";
    public const string TableName = "elsa_studio_preferences";

    /// <summary>The persisted-schema version every preference row is stamped with, and checked against when read.</summary>
    public const string SchemaVersion = "1.0.0";

    /// <summary>The schema family this module's rows belong to, checked against when a row is read.</summary>
    public const string SchemaFamily = "StudioPreferences";
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}

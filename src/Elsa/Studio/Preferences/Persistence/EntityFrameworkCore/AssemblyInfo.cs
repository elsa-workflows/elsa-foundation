using Elsa.Persistence.EntityFramework;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// not the hand-written EfModuleBinding each registration class still builds for itself (slice 2, #1872).
[assembly: EfModule(
    "Studio.Preferences",
    typeof(StudioPreferencesDbContext),
    HistoryModule = StudioPreferencesEfModule.HistoryModuleName,
    Sqlite = typeof(StudioPreferencesSqliteDbContext),
    SqlServer = typeof(StudioPreferencesSqlServerDbContext),
    PostgreSql = typeof(StudioPreferencesPostgreSqlDbContext),
    MySql = typeof(StudioPreferencesMySqlDbContext))]

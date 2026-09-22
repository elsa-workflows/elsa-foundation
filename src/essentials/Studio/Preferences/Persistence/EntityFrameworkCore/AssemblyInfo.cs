using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
[assembly: EfModule(
    "Studio.Preferences",
    typeof(StudioPreferencesDbContext),
    HistoryModule = StudioPreferencesEfModule.HistoryModuleName,
    Sqlite = typeof(StudioPreferencesSqliteDbContext),
    SqlServer = typeof(StudioPreferencesSqlServerDbContext),
    PostgreSql = typeof(StudioPreferencesPostgreSqlDbContext),
    MySql = typeof(StudioPreferencesMySqlDbContext),
    DisplayName = "Studio Preferences")]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (spec 171 slice 11,
// #1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Studio.Preferences")]

using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;

// The single, discoverable declaration of this module. EfModuleCatalog.Discover reads this,
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

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules
// (#1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Studio.Preferences")]

// The schema family this module owns, at the version its skew check reads. A host's readability
// report is derived from it alone; every family the stores check must be declared here, at the
// version they read; that holds by review until #2606 restores a compiled check.
[assembly: EfSchemaFamily(StudioPreferencesEfModule.SchemaFamily, "Studio.Preferences", StudioPreferencesEfModule.SchemaVersion)]

// Content and integrity columns: see Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns".
[assembly: EfSchemaContent(StudioPreferencesEfModule.SchemaFamily, typeof(StudioPreferenceRecord), nameof(StudioPreferenceRecord.ValueJson))]

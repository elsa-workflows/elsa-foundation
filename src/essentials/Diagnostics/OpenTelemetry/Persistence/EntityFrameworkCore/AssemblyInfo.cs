using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872). This module names its
// own default connection instead of the shared "Elsa" one (EfConnectionDefaults).
[assembly: EfModule(
    "Diagnostics.OpenTelemetry",
    typeof(EfOpenTelemetryDbContext),
    HistoryModule = EfOpenTelemetryModule.HistoryModuleName,
    Sqlite = typeof(OpenTelemetrySqliteDbContext),
    SqlServer = typeof(OpenTelemetrySqlServerDbContext),
    PostgreSql = typeof(OpenTelemetryPostgreSqlDbContext),
    MySql = typeof(OpenTelemetryMySqlDbContext),
    DisplayName = "OpenTelemetry",
    DefaultConnectionName = EfOpenTelemetryModule.DefaultConnectionName,
    DefaultSqliteConnectionString = EfOpenTelemetryModule.DefaultSqliteConnectionString)]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (spec 171 slice 11,
// #1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Diagnostics.OpenTelemetry")]

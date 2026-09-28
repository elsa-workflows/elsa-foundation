using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
[assembly: EfModule(
    "Diagnostics.StructuredLogs",
    typeof(StructuredLogsDbContext),
    HistoryModule = StructuredLogsEfModule.HistoryModuleName,
    Sqlite = typeof(StructuredLogsSqliteDbContext),
    SqlServer = typeof(StructuredLogsSqlServerDbContext),
    PostgreSql = typeof(StructuredLogsPostgreSqlDbContext),
    MySql = typeof(StructuredLogsMySqlDbContext),
    DisplayName = "Structured Logs")]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (spec 171 slice 11,
// #1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Diagnostics.StructuredLogs")]

// The schema family this module owns (spec 180, FR-001), at the version its skew check reads. A host's readability
// report is derived from it alone (spec 183, FR-020); EfSchemaFamilyDeclarationGuardTests fails the build when a
// family the stores check is not declared here, or is declared at another version.
[assembly: EfSchemaFamily(StructuredLogsEfModule.SchemaFamily, "Diagnostics.StructuredLogs", StructuredLogsEfModule.SchemaVersion)]

using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

// The single, discoverable declaration of this assembly's two modules (ADR 0076 D2): [EfModule] is
// AllowMultiple for exactly the two assemblies that carry two contexts, this one and Identity's.
// EfModuleCatalog.Discover reads these, and EfModuleBinding.For derives each registration class's
// binding from them (#1872). Each one's name is mirrored below into elsa-package.json's
// extensions.efModules (spec 171 slice 11, #1881); EfModuleDescriptorTests guards that the two never
// drift apart.
[assembly: EfModule(
    "Workflows.Runtime.Distributed.Placement",
    typeof(ExecutionPlacementDbContext),
    HistoryModule = ExecutionPlacementEfModule.HistoryModuleName,
    Sqlite = typeof(ExecutionPlacementSqliteDbContext),
    SqlServer = typeof(ExecutionPlacementSqlServerDbContext),
    PostgreSql = typeof(ExecutionPlacementPostgreSqlDbContext),
    MySql = typeof(ExecutionPlacementMySqlDbContext),
    DisplayName = "Distributed runtime execution placement")]

[assembly: EfModule(
    "Workflows.Runtime.Distributed.CommandTransport",
    typeof(ExecutionCommandTransportDbContext),
    HistoryModule = ExecutionCommandTransportEfModule.HistoryModuleName,
    Sqlite = typeof(ExecutionCommandTransportSqliteDbContext),
    SqlServer = typeof(ExecutionCommandTransportSqlServerDbContext),
    PostgreSql = typeof(ExecutionCommandTransportPostgreSqlDbContext),
    MySql = typeof(ExecutionCommandTransportMySqlDbContext),
    DisplayName = "Distributed runtime execution command transport")]

[assembly: ManifestExtension("efModules", "Workflows.Runtime.Distributed.Placement")]
[assembly: ManifestExtension("efModules", "Workflows.Runtime.Distributed.CommandTransport")]

// The schema families these modules own (spec 180, FR-001), each at the version its skew check reads. A host's
// readability report is derived from these alone (spec 183, FR-020); EfSchemaFamilyDeclarationGuardTests fails the
// build when a family the stores check is not declared here, or is declared at another version.
[assembly: EfSchemaFamily(ExecutionPlacementEfModule.SchemaFamily, "Workflows.Runtime.Distributed.Placement", ExecutionPlacementEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(ExecutionCommandTransportEfModule.SchemaFamily, "Workflows.Runtime.Distributed.CommandTransport", ExecutionCommandTransportEfModule.SchemaVersion)]

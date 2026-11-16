using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;

// The single, discoverable declaration of this assembly's two modules: [EfModule] is
// AllowMultiple for exactly the two assemblies that carry two contexts, this one and Identity's.
// EfModuleCatalog.Discover reads these, and EfModuleBinding.For derives each registration class's
// binding from them (#1872). Each one's name is mirrored below into elsa-package.json's
// extensions.efModules (#1881); EfModuleDescriptorTests guards that the two never
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

// The schema families these modules own, each at the version its skew check reads. A host's
// readability report is derived from these alone; every family the stores check must be declared
// here, at the version they read; that holds by review until #2606 restores a compiled check.
[assembly: EfSchemaFamily(ExecutionPlacementEfModule.SchemaFamily, "Workflows.Runtime.Distributed.Placement", ExecutionPlacementEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(ExecutionCommandTransportEfModule.SchemaFamily, "Workflows.Runtime.Distributed.CommandTransport", ExecutionCommandTransportEfModule.SchemaVersion)]

// Content and integrity columns: see Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns".
[assembly: EfSchemaContent(ExecutionCommandTransportEfModule.SchemaFamily, typeof(ExecutionCommandTransportItemEntity), nameof(ExecutionCommandTransportItemEntity.PayloadJson))]

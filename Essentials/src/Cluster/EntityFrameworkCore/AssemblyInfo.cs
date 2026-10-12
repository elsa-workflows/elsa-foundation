using Elsa.Cluster.EntityFrameworkCore;
using Elsa.Cluster.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this, so the
// persistence tool finds the membership table in any host closure that carries this assembly, and EfModuleBinding.For
// derives the registration's binding from it.
[assembly: EfModule(
    "Cluster.Membership",
    typeof(ClusterMembershipDbContext),
    HistoryModule = ClusterMembershipEfModule.HistoryModuleName,
    Sqlite = typeof(ClusterMembershipSqliteDbContext),
    SqlServer = typeof(ClusterMembershipSqlServerDbContext),
    PostgreSql = typeof(ClusterMembershipPostgreSqlDbContext),
    MySql = typeof(ClusterMembershipMySqlDbContext),
    DisplayName = "Cluster membership")]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules; EfModuleDescriptorTests guards that
// the two never drift apart.
[assembly: ManifestExtension("efModules", ClusterMembershipEfModule.Name)]

// The schema family this module owns (spec 180, FR-001), at the version its rows are stamped and read with. A host's
// readability report is derived from it alone (spec 183, FR-020); every SchemaFamily constant must be declared here, at
// the version its rows use; that holds by review until #2606 restores a compiled check.
[assembly: EfSchemaFamily(ClusterMembershipEfModule.SchemaFamily, ClusterMembershipEfModule.Name, ClusterMembershipEfModule.SchemaVersion)]

// Content and integrity columns: see Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns" (spec 180, FR-008, FR-009
// and FR-014).
[assembly: EfSchemaContent(ClusterMembershipEfModule.SchemaFamily, typeof(ClusterMemberEntity), nameof(ClusterMemberEntity.ReportJson))]

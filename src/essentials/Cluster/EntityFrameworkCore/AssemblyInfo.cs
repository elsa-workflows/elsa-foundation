using Elsa.Cluster.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this, so the
// persistence tool finds the membership table in any host closure that carries this assembly, and EfModuleBinding.For
// derives the registration's binding from it.
[assembly: EfModule(
    ClusterMembershipEfModule.Name,
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

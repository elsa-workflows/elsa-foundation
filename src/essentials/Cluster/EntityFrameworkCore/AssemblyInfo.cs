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
// readability report is derived from it alone (spec 183, FR-020); EfSchemaFamilyDeclarationGuardTests fails the build
// when a SchemaFamily constant is not declared here, or is declared at another version.
[assembly: EfSchemaFamily(ClusterMembershipEfModule.SchemaFamily, ClusterMembershipEfModule.Name, ClusterMembershipEfModule.SchemaVersion)]

// The family's content columns (spec 180, FR-009 and FR-014): the documents a read upcasts through the family's chain
// before it deserializes them, and a write that changes them restamps. Every document column of the family's tables is
// declared here; EfSchemaContentDeclarationTests fails the build when one is not, or when one declared is not in the
// model, and EfSchemaFamilyDeclarationGuardTests holds every read and write of them to the chain and the stamp.
[assembly: EfSchemaContent(ClusterMembershipEfModule.SchemaFamily, typeof(ClusterMemberEntity), nameof(ClusterMemberEntity.ReportJson))]

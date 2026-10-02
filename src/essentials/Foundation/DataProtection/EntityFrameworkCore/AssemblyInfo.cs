using Elsa.Foundation.DataProtection.EntityFrameworkCore;
using Elsa.Foundation.DataProtection.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this, so the
// persistence tool finds the key table in any host closure that carries this assembly, and EfModuleBinding.For derives
// the registration's binding from it.
[assembly: EfModule(
    "DataProtection.Keys",
    typeof(DataProtectionKeysDbContext),
    HistoryModule = DataProtectionKeysEfModule.HistoryModuleName,
    Sqlite = typeof(DataProtectionKeysSqliteDbContext),
    SqlServer = typeof(DataProtectionKeysSqlServerDbContext),
    PostgreSql = typeof(DataProtectionKeysPostgreSqlDbContext),
    MySql = typeof(DataProtectionKeysMySqlDbContext),
    DisplayName = "Data Protection keys")]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules; EfModuleDescriptorTests guards that
// the two never drift apart.
[assembly: ManifestExtension("efModules", "DataProtection.Keys")]

// The schema family this module owns (spec 180, FR-001), at the version its rows are stamped and read with. A host's
// readability report is derived from it alone (spec 183, FR-020); EfSchemaFamilyChainDeclarationGuardTests fails the build
// when a SchemaFamily constant is not declared here, or is declared at another version.
[assembly: EfSchemaFamily(DataProtectionKeysEfModule.SchemaFamily, "DataProtection.Keys", DataProtectionKeysEfModule.SchemaVersion)]

// Content and integrity columns: see src/essentials/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity
// columns" (spec 180, FR-008, FR-009 and FR-014). The element is what the key manager parses.
[assembly: EfSchemaContent(DataProtectionKeysEfModule.SchemaFamily, typeof(DataProtectionKeyEntity), nameof(DataProtectionKeyEntity.Xml))]

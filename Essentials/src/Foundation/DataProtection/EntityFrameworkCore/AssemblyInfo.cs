using Elsa.Foundation.DataProtection.EntityFrameworkCore;
using Elsa.Foundation.DataProtection.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this module. EfModuleCatalog.Discover reads this, so the
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

// The schema family this module owns, at the version its rows are stamped and read with. A host's
// readability report is derived from it alone; every SchemaFamily constant must be declared here, at
// the version its rows use; that holds by review until #2606 restores a compiled check.
[assembly: EfSchemaFamily(DataProtectionKeysEfModule.SchemaFamily, "DataProtection.Keys", DataProtectionKeysEfModule.SchemaVersion)]

// Content and integrity columns: see Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity
// columns". The element is what the key manager parses.
[assembly: EfSchemaContent(DataProtectionKeysEfModule.SchemaFamily, typeof(DataProtectionKeyEntity), nameof(DataProtectionKeyEntity.Xml))]

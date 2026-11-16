using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Entities;

// The single, discoverable declaration of this module. EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
// This module lives under Extensions/, not src/, but is included in the vocabulary on the same terms as
// every other module. It names its own default connection instead of the shared "Elsa"
// one (EfConnectionDefaults), because it must match whatever connection Activities and Workflows Design use.
[assembly: EfModule(
    "Elsa3.Activities.Design.Import",
    typeof(Elsa3ImportDbContext),
    HistoryModule = Elsa3ImportEfModule.HistoryModuleName,
    Sqlite = typeof(Elsa3ImportSqliteDbContext),
    SqlServer = typeof(Elsa3ImportSqlServerDbContext),
    PostgreSql = typeof(Elsa3ImportPostgreSqlDbContext),
    MySql = typeof(Elsa3ImportMySqlDbContext),
    DisplayName = "Elsa 3 import",
    DefaultConnectionName = Elsa3ImportEfModule.DefaultConnectionName,
    DefaultSqliteConnectionString = Elsa3ImportEfModule.DefaultSqliteConnectionString)]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules
// (#1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Elsa3.Activities.Design.Import")]

// The schema family this module owns, at the version its skew check reads. A host's readability
// report is derived from it alone; every family the stores check must be declared here, at the
// version they read; that holds by review until #2606 restores a compiled check.
[assembly: EfSchemaFamily(Elsa3ImportEfModule.SchemaFamily, "Elsa3.Activities.Design.Import", Elsa3ImportEfModule.SchemaVersion)]

// Content and integrity columns: see Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns".
[assembly: EfSchemaContent(Elsa3ImportEfModule.SchemaFamily, typeof(Elsa3ImportCollectionRecord), nameof(Elsa3ImportCollectionRecord.ContentJson))]
[assembly: EfSchemaContent(Elsa3ImportEfModule.SchemaFamily, typeof(Elsa3ImportReceiptRecord), nameof(Elsa3ImportReceiptRecord.ContentJson))]

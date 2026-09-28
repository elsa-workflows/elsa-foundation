using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Entities;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
// This module lives under src/extensions/, not src/, but is included in the vocabulary on the same terms as
// every other module (spec 171 D2, D3). It names its own default connection instead of the shared "Elsa"
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

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (spec 171 slice 11,
// #1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Elsa3.Activities.Design.Import")]

// The schema family this module owns (spec 180, FR-001), at the version its skew check reads. A host's readability
// report is derived from it alone (spec 183, FR-020); EfSchemaFamilyDeclarationGuardTests fails the build when a
// family the stores check is not declared here, or is declared at another version.
[assembly: EfSchemaFamily(Elsa3ImportEfModule.SchemaFamily, "Elsa3.Activities.Design.Import", Elsa3ImportEfModule.SchemaVersion)]

// The family's content columns (spec 180, FR-009 and FR-014): the documents a read upcasts through the family's chain
// before it deserializes them, and a write that changes them restamps. Every document column of the family's tables is
// declared here; EfSchemaContentDeclarationTests fails the build when one is not, or when one declared is not in the
// model, and EfSchemaFamilyDeclarationGuardTests holds every read and write of them to the chain and the stamp.
[assembly: EfSchemaContent(Elsa3ImportEfModule.SchemaFamily, typeof(Elsa3ImportCollectionRecord), nameof(Elsa3ImportCollectionRecord.ContentJson))]
[assembly: EfSchemaContent(Elsa3ImportEfModule.SchemaFamily, typeof(Elsa3ImportReceiptRecord), nameof(Elsa3ImportReceiptRecord.ContentJson))]

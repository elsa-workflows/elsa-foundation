using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;

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

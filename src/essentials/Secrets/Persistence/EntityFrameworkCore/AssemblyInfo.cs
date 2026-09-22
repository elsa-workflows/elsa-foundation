using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872). PostMigration declares
// the projection reindex the seam added in #1877 audits after every apply and never runs by itself.
[assembly: EfModule(
    "Secrets",
    typeof(SecretsDbContext),
    HistoryModule = SecretsEfModule.HistoryModuleName,
    Sqlite = typeof(SecretsSqliteDbContext),
    SqlServer = typeof(SecretsSqlServerDbContext),
    PostgreSql = typeof(SecretsPostgreSqlDbContext),
    MySql = typeof(SecretsMySqlDbContext),
    PostMigration = [typeof(SecretsProjectionReindex)])]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (spec 171 slice 11,
// #1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Secrets")]

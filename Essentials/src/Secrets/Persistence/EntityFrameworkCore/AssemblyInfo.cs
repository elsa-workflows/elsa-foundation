using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this module. EfModuleCatalog.Discover reads this,
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

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules
// (#1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Secrets")]

// The schema family this module owns, at the version its skew check reads. A host's readability
// report is derived from it alone; every family the stores check must be declared here, at the
// version they read; that holds by review until #2606 restores a compiled check.
[assembly: EfSchemaFamily(SecretsEfModule.SchemaFamily, "Secrets", SecretsEfModule.SchemaVersion)]

// Content and integrity columns: see Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns".
[assembly: EfSchemaContent(SecretsEfModule.SchemaFamily, typeof(SecretRecord), nameof(SecretRecord.Payload))]

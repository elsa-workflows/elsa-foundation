using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
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

// The schema family this module owns (spec 180, FR-001), at the version its skew check reads. A host's readability
// report is derived from it alone (spec 183, FR-020); EfSchemaFamilyChainDeclarationGuardTests fails the build when a
// family the stores check is not declared here, or is declared at another version.
[assembly: EfSchemaFamily(SecretsEfModule.SchemaFamily, "Secrets", SecretsEfModule.SchemaVersion)]

// Content and integrity columns: see src/essentials/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns" (spec 180, FR-008, FR-009
// and FR-014).
[assembly: EfSchemaContent(SecretsEfModule.SchemaFamily, typeof(SecretRecord), nameof(SecretRecord.Payload))]

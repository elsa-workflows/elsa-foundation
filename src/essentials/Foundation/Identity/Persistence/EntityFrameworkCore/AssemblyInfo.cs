using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this assembly's two modules (ADR 0076 D2): [EfModule] is
// AllowMultiple for exactly the two assemblies that carry two contexts, this one and Runtime.Distributed's.
// EfModuleCatalog.Discover reads these, and EfModuleBinding.For derives each registration class's
// binding from them (#1872). Each one's name is mirrored below into elsa-package.json's
// extensions.efModules (spec 171 slice 11, #1881); EfModuleDescriptorTests guards that the two never
// drift apart.
[assembly: EfModule(
    "Identity.Iam",
    typeof(IdentityIamDbContext),
    HistoryModule = IdentityIamEfModule.HistoryModuleName,
    Sqlite = typeof(IdentityIamSqliteDbContext),
    SqlServer = typeof(IdentityIamSqlServerDbContext),
    PostgreSql = typeof(IdentityIamPostgreSqlDbContext),
    MySql = typeof(IdentityIamMySqlDbContext),
    DisplayName = "Identity IAM")]

[assembly: EfModule(
    "Identity.ProviderConfiguration",
    typeof(IdentityProviderConfigurationDbContext),
    HistoryModule = IdentityProviderConfigurationEfModule.HistoryModuleName,
    Sqlite = typeof(IdentityProviderConfigurationSqliteDbContext),
    SqlServer = typeof(IdentityProviderConfigurationSqlServerDbContext),
    PostgreSql = typeof(IdentityProviderConfigurationPostgreSqlDbContext),
    MySql = typeof(IdentityProviderConfigurationMySqlDbContext),
    DisplayName = "Identity provider-configuration")]

[assembly: ManifestExtension("efModules", "Identity.Iam")]
[assembly: ManifestExtension("efModules", "Identity.ProviderConfiguration")]

using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;

// The single, discoverable declaration of this assembly's two modules (ADR 0076 D2): [EfModule] is
// AllowMultiple for exactly the two assemblies that carry two contexts, this one and Runtime.Distributed's.
// EfModuleCatalog.Discover reads these, and EfModuleBinding.For derives each registration class's
// binding from them (#1872).
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

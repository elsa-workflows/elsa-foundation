using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
[assembly: EfModule(
    "Secrets",
    typeof(SecretsDbContext),
    HistoryModule = SecretsEfModule.HistoryModuleName,
    Sqlite = typeof(SecretsSqliteDbContext),
    SqlServer = typeof(SecretsSqlServerDbContext),
    PostgreSql = typeof(SecretsPostgreSqlDbContext),
    MySql = typeof(SecretsMySqlDbContext))]

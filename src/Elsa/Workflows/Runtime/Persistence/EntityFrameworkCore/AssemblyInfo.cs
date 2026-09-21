using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
[assembly: EfModule(
    "Workflows.Runtime",
    typeof(RuntimeDbContext),
    HistoryModule = RuntimeEfModule.HistoryModuleName,
    Sqlite = typeof(RuntimeSqliteDbContext),
    SqlServer = typeof(RuntimeSqlServerDbContext),
    PostgreSql = typeof(RuntimePostgreSqlDbContext),
    MySql = typeof(RuntimeMySqlDbContext),
    DisplayName = "Runtime")]

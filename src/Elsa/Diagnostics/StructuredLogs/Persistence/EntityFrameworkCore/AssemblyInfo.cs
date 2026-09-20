using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
[assembly: EfModule(
    "Diagnostics.StructuredLogs",
    typeof(StructuredLogsDbContext),
    HistoryModule = StructuredLogsEfModule.HistoryModuleName,
    Sqlite = typeof(StructuredLogsSqliteDbContext),
    SqlServer = typeof(StructuredLogsSqlServerDbContext),
    PostgreSql = typeof(StructuredLogsPostgreSqlDbContext),
    MySql = typeof(StructuredLogsMySqlDbContext),
    DisplayName = "Structured Logs")]

using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872). This module names its
// own default connection instead of the shared "Elsa" one (EfConnectionDefaults).
[assembly: EfModule(
    "Diagnostics.OpenTelemetry",
    typeof(EfOpenTelemetryDbContext),
    HistoryModule = EfOpenTelemetryModule.HistoryModuleName,
    Sqlite = typeof(OpenTelemetrySqliteDbContext),
    SqlServer = typeof(OpenTelemetrySqlServerDbContext),
    PostgreSql = typeof(OpenTelemetryPostgreSqlDbContext),
    MySql = typeof(OpenTelemetryMySqlDbContext),
    DisplayName = "OpenTelemetry",
    DefaultConnectionName = EfOpenTelemetryModule.DefaultConnectionName,
    DefaultSqliteConnectionString = EfOpenTelemetryModule.DefaultSqliteConnectionString)]

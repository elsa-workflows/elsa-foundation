using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// not the hand-written EfModuleBinding each registration class still builds for itself (slice 2, #1872).
[assembly: EfModule(
    "Diagnostics.OpenTelemetry",
    typeof(EfOpenTelemetryDbContext),
    HistoryModule = EfOpenTelemetryModule.HistoryModuleName,
    Sqlite = typeof(OpenTelemetrySqliteDbContext),
    SqlServer = typeof(OpenTelemetrySqlServerDbContext),
    PostgreSql = typeof(OpenTelemetryPostgreSqlDbContext),
    MySql = typeof(OpenTelemetryMySqlDbContext))]

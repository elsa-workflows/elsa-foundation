using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

// The single, discoverable declaration of this assembly's two modules (ADR 0076 D2): [EfModule] is
// AllowMultiple for exactly the two assemblies that carry two contexts, this one and Identity's.
// EfModuleCatalog.Discover reads these, not the hand-written EfModuleBinding each registration class
// still builds for itself (slice 2, #1872).
[assembly: EfModule(
    "Workflows.Runtime.Distributed.Placement",
    typeof(ExecutionPlacementDbContext),
    HistoryModule = ExecutionPlacementEfModule.HistoryModuleName,
    Sqlite = typeof(ExecutionPlacementSqliteDbContext),
    SqlServer = typeof(ExecutionPlacementSqlServerDbContext),
    PostgreSql = typeof(ExecutionPlacementPostgreSqlDbContext),
    MySql = typeof(ExecutionPlacementMySqlDbContext))]

[assembly: EfModule(
    "Workflows.Runtime.Distributed.CommandTransport",
    typeof(ExecutionCommandTransportDbContext),
    HistoryModule = ExecutionCommandTransportEfModule.HistoryModuleName,
    Sqlite = typeof(ExecutionCommandTransportSqliteDbContext),
    SqlServer = typeof(ExecutionCommandTransportSqlServerDbContext),
    PostgreSql = typeof(ExecutionCommandTransportPostgreSqlDbContext),
    MySql = typeof(ExecutionCommandTransportMySqlDbContext))]

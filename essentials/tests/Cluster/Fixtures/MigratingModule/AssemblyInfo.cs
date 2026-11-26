using Elsa.Cluster.Fixtures.MigratingModule;
using Elsa.Persistence.EntityFramework;

[assembly: EfModule(
    MigratingModule.Name,
    typeof(MigratingModuleDbContext),
    HistoryModule = MigratingModule.HistoryModuleName,
    Sqlite = typeof(MigratingModuleSqliteDbContext),
    DisplayName = "Migrating module fixture")]

using Elsa.Cluster.Fixtures.FeedModule;
using Elsa.Persistence.EntityFramework;

[assembly: EfModule(
    FeedModule.Name,
    typeof(FeedModuleDbContext),
    HistoryModule = FeedModule.HistoryModuleName,
    Sqlite = typeof(FeedModuleSqliteDbContext),
    DisplayName = "Feed module fixture")]

[assembly: EfSchemaFamily(FeedModule.Family, FeedModule.Name, FeedModule.CurrentVersion, Upcasters = [typeof(FeedModuleOneToTwo)])]

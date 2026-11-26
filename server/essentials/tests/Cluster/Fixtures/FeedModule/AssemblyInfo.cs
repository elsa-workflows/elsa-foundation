using Elsa.Cluster.Fixtures.FeedModule;
using Elsa.Persistence.EntityFramework;

[assembly: EfModule(
    FeedModule.Name,
    typeof(FeedModuleDbContext),
    HistoryModule = FeedModule.HistoryModuleName,
    Sqlite = typeof(FeedModuleSqliteDbContext),
    DisplayName = "Feed module fixture")]

#if FEED_MODULE_PREVIOUS_RELEASE
// The previous release: its family is at version 1 and has no upcaster, so it reads version 1 alone.
[assembly: EfSchemaFamily(FeedModule.Family, FeedModule.Name, FeedModule.CurrentVersion)]
#else
[assembly: EfSchemaFamily(FeedModule.Family, FeedModule.Name, FeedModule.CurrentVersion, Upcasters = [typeof(FeedModuleOneToTwo)])]
#endif
